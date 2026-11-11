using System.Net;
using System.Net.Sockets;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.I18n;
using ACommerce.Platform.MultiTenancy;
using StudioNewStore = ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioNewStore;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تَأخُذَ وِجهَةُ تَحويلِ /studio/new مُخَطَّطَها مِن {http, https} (‏2026-11-11، مُراجَعَةُ الإغلاقِ الأَخيرِ لِـW13-4 الجَولَةُ الثانِيَة ·
//     W13-4·C2f-R2·1 — R2-SEC-1) ═══
//
// **ما سَيَتَغَيَّر**: `StudioNewStore` يُحَوِّلُ الزائِرَ وغَيرَ المُوافِقِ مِن الخادِمِ بِـ`Nav.NavigateTo` (C1D-1)، و`NavigationManager` يَبني الوِجهَةَ المُطلَقَةَ مِن
// `Request.Scheme` بِحَرفِه — والواجِهَةُ الأَمامِيَّةُ تُمَرِّرُ المَقروءَ بِحَرفِه (`SchemeRobustnessCharacterizationTests`) — فَـ`X-Forwarded-Proto: javascript` كانَ
// `302 Location: javascript://…/studio/auth`، و`mailto` ‏`mailto:/studio/auth` (مَقيسٌ حَيّاً عِندَ `0f788af` في الوَضعَين).
// **وهذا المِلَفُّ يُثَبِّتُ بايتاتِ اليَومِ لِما لا يَتَغَيَّر** — ويَبقى بِبَصمَتِه بَعدَ التَبديل (القاعِدَة ٣): الصَفحَةُ الحَقيقِيَّةُ بِحَقنِها خَلفَ الواجِهَةِ الأَمامِيَّةِ
// الحَقيقِيَّة (`UsePlatformForwardedHeaders`) ونُقطَةِ Razor الحَقيقِيَّة، والزائِرُ بِلا جَلسَة: (١) الوِجهَةُ المُطلَقَةُ بِحَرفِها بِلا `X-Forwarded-Proto` وبِـhttp
// وhttps وHTTPS وقيمَتَين، وبِمُضيفٍ بِحالَةِ أَحرُفٍ ومَنفَذٍ افتِراضِيٍّ وIPv6 وIDN؛ (٢) والتَنَقُّلُ المُحَسَّنُ يَأخُذُ 302 نَفسَه حينَ تُطابِقُ الوِجهَةُ مُخَطَّطَ الطَلَبِ
// ومُضيفَه بِحَرفِهِما، وإلّا التَحويلَ المُعتِمَ (200 بِـ`blazor-enhanced-nav-redirect-location`) — كِلاهُما كَما يُقَرِّرُه الإطارُ اليَوم.
// وما يَتَغَيَّرُ عَمداً لا يُثَبَّتُ هُنا: مُخَطَّطٌ مَقروءٌ غَيرُ http(s) (javascript، gopher، mailto، ab). وفَرعُ غَيرِ المُوافِقِ يَسأَلُ القاعِدَةَ فَيَقيسُه حَيّاً
// `rewalk-launch-journey.sh` («GET /studio/new (لا قَبولَ بَعد) → 302 · إلى /studio/consent…»).

file sealed class StudioNewFront : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private StudioNewFront(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<StudioNewFront> StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var policy = new ForwardedHeadersPolicy();
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions());
            builder.Services.AddRazorComponents();
            builder.Services.AddHttpContextAccessor();
            // ما تَحقِنُه الصَفحَةُ كَما يُسَجِّلُه القالِب — والمَخزَنُ لا يُسأَلُ في فَرعِ الزائِر (لا جَلسَةَ قَبلَ HasConsentedAsync)، فَلا قاعِدَة.
            builder.Services.AddSingleton<IDocumentStore>(_ => DocumentStore.For(o => o.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")));
            builder.Services.AddScoped<StudioAuth>();
            builder.Services.AddScoped<StudioQueries>();
            builder.Services.AddScoped<StudioTierService>();
            builder.Services.AddScoped<L>();
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            app.UseRouting();
            app.UseAntiforgery();
            app.MapRazorComponents<StudioNewStore>();
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new StudioNewFront(app, port);
        }
    }

    public async Task<(int Status, string? Location, string? Enhanced)> GetAsync(string host, string? proto, bool enhanced = false)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}/studio/new");
        request.Headers.Host = host;
        if (proto is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        if (enhanced) request.Headers.TryAddWithoutValidation("Accept", "text/html; blazor-enhanced-nav=on");
        using var response = await client.SendAsync(request);
        var location = response.Headers.TryGetValues("Location", out var l) ? l.Single() : null;
        var opaque = response.Headers.TryGetValues("blazor-enhanced-nav-redirect-location", out var e) ? e.Single() : null;
        return ((int)response.StatusCode, location, opaque);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class StudioNewRedirectCharacterizationTests
{
    [Theory]
    [InlineData("localhost:5050", null, "http://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "http", "http://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "https", "https://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "HTTPS", "https://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "https, http", "http://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "http, https", "https://localhost:5050/studio/auth")]
    [InlineData("LOCALHOST:5050", null, "http://localhost:5050/studio/auth")]
    [InlineData("localhost:80", null, "http://localhost/studio/auth")]
    [InlineData("localhost:443", "https", "https://localhost/studio/auth")]
    [InlineData("localhost", "https", "https://localhost/studio/auth")]
    [InlineData("[::1]:5050", null, "http://[::1]:5050/studio/auth")]
    [InlineData("xn--mgbh0fb.localhost:5150", "https", "https://xn--mgbh0fb.localhost:5150/studio/auth")]
    [InlineData("example.test", "https", "https://example.test/studio/auth")]
    public async Task The_visitor_is_sent_to_sign_in_at_the_address_the_framework_builds_today(string host, string? proto, string location)
    {
        await using var front = await StudioNewFront.StartAsync();
        Assert.Equal((302, location, (string?)null), await front.GetAsync(host, proto));
    }

    [Theory]
    [InlineData("localhost:5050", null, "http://localhost:5050/studio/auth")]
    [InlineData("localhost:5050", "https", "https://localhost:5050/studio/auth")]
    [InlineData("example.test", "https", "https://example.test/studio/auth")]
    public async Task An_enhanced_navigation_takes_the_same_302_when_scheme_and_host_match(string host, string? proto, string location)
    {
        await using var front = await StudioNewFront.StartAsync();
        Assert.Equal((302, location, (string?)null), await front.GetAsync(host, proto, enhanced: true));
    }

    [Theory]
    [InlineData("LOCALHOST:5050", null)]
    [InlineData("localhost:5050", "HTTPS")]
    [InlineData("localhost:80", null)]
    public async Task An_enhanced_navigation_whose_target_does_not_spell_scheme_and_host_as_sent_takes_the_opaque_redirect(string host, string? proto)
    {
        await using var front = await StudioNewFront.StartAsync();
        var (status, location, opaque) = await front.GetAsync(host, proto, enhanced: true);
        Assert.Equal(200, status);
        Assert.Null(location);
        Assert.StartsWith("_framework/opaque-redirect?url=", opaque, StringComparison.Ordinal);
    }
}
