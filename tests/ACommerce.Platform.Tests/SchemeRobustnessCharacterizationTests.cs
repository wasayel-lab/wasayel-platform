using System.Net;
using System.Net.Sockets;
using ACommerce.Kit.Tenants.Server;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُقرَأَ X-Forwarded-Proto بِقاعِدَةِ {http, https} في كُلِّ بانٍ ويُرمَّزَ تَحويلٌ مَحَلِّيٌّ غَيرُ ASCII (‏2026-11-11،
//     الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·2 — SEC-C2z-R3-2 وSEC-C2z-R3-3 وما بَقِيَ مِن SEC-C2z-R1-4 وC2z-PATHS-R2-02) ═══
//
// يُثَبِّتُ بايتاتِ اليَومِ لِما لا يَتَغَيَّر — ويَبقى المِلَفُّ بِبَصمَتِه بَعدَ التَبديل (القاعِدَة ٣):
// (١) الواجِهَةُ الأَمامِيَّةُ تُمَرِّرُ X-Forwarded-Proto المَقروءَ (غائِب، http، https، HTTPS، قيمَتان، javascript، gopher) إلى الإطارِ كَما يَفعَلُ اليَوم — 200
//     و`Request.Scheme` بِحَرفِ ما يَضَعُه `ForwardedHeadersMiddleware`؛ (٢) أَصلُ الطَلَبِ (`PayPalOrderSurface.OriginFrom`) و`StoreUrl.PlatformOrigin` بِلا نِطاقٍ
//     و`Sitemap:` في robots.txt بِـhttp وhttps بايتاً؛ (٣) `LocalRedirect` عَلى كُلِّ وِجهَةِ ASCII — المَقبولُ بِحَرفِه والمَردودُ إلى البَديل.
// وما يَتَغَيَّرُ عَمداً لا يُثَبَّتُ هُنا: قيمَةٌ لا تُقرَأُ مُخَطَّطاً (1http، 9، +x ← 500 اليَوم)، ومُخَطَّطٌ غَيرُ http(s) في وُسومِ App.razor وفي الأَصلِ وفي robots،
// ووِجهَةٌ مَحَلِّيَّةٌ غَيرُ ASCII (← 500 اليَوم).

file sealed class ProtoFront : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private ProtoFront(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<ProtoFront> StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var policy = new ForwardedHeadersPolicy();
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions());
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            app.Run(ctx => ctx.Response.WriteAsync(ctx.Request.Scheme));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new ProtoFront(app, port);
        }
    }

    public async Task<(int Status, string Body)> GetAsync(string? proto)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}/");
        if (proto is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class SchemeRobustnessCharacterizationTests
{
    [Theory]
    [InlineData(null, "http")]
    [InlineData("http", "http")]
    [InlineData("https", "https")]
    [InlineData("HTTPS", "HTTPS")]
    [InlineData("https, http", "http")]
    [InlineData("javascript", "javascript")]
    [InlineData("gopher", "gopher")]
    public async Task A_readable_forwarded_proto_passes_the_front_as_the_framework_sets_it(string? proto, string scheme)
    {
        await using var front = await ProtoFront.StartAsync();
        Assert.Equal((200, scheme), await front.GetAsync(proto));
    }

    private static HttpRequest Req(string scheme)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString("localhost:5050");
        return ctx.Request;
    }

    [Theory]
    [InlineData("http", "http://localhost:5050")]
    [InlineData("https", "https://localhost:5050")]
    public void The_request_origin_and_the_platform_origin_without_a_domain_keep_http_and_https(string scheme, string want)
    {
        Assert.Equal(want, PayPalOrderSurface.OriginFrom(Req(scheme)));
        Assert.Equal(want, StoreUrl.PlatformOrigin(Req(scheme)));
    }

    [Theory]
    [InlineData("http", "Sitemap: http://localhost:5050/sitemap.xml")]
    [InlineData("https", "Sitemap: https://localhost:5050/sitemap.xml")]
    public async Task Robots_names_its_sitemap_with_http_or_https_as_today(string scheme, string line)
    {
        var req = Req(scheme);
        req.HttpContext.Response.Body = new MemoryStream();
        await SeoHandlers.Robots(req.HttpContext);
        req.HttpContext.Response.Body.Position = 0;
        var body = await new StreamReader(req.HttpContext.Response.Body).ReadToEndAsync();
        Assert.Contains("\n" + line + "\n", body, StringComparison.Ordinal);
        Assert.Equal(SeoDocumentsRobots(scheme), body);
    }

    private static string SeoDocumentsRobots(string scheme) => ACommerce.Kit.Tenants.SeoDocuments.BuildRobotsTxt($"{scheme}://localhost:5050");

    [Theory]
    [InlineData("/ashare/explore", "/ashare/explore")]
    [InlineData("/ashare/explore?q=a%20b", "/ashare/explore?q=a%20b")]
    [InlineData("/ashare/%D9%85", "/ashare/%D9%85")]
    [InlineData("/", "/")]
    [InlineData("/r/vendor/me", "/r/vendor/me")]
    [InlineData("//evil.example", "/fallback")]
    [InlineData("/\\evil.example", "/fallback")]
    [InlineData("https://evil.example", "/fallback")]
    [InlineData("/a:b", "/fallback")]
    [InlineData("/a b", "/fallback")]
    [InlineData("/a\tb", "/fallback")]
    [InlineData("", "/fallback")]
    [InlineData(null, "/fallback")]
    public void A_local_redirect_in_ascii_resolves_as_today(string? candidate, string want)
        => Assert.Equal(want, LocalRedirect.Resolve(candidate, "/fallback"));
}
