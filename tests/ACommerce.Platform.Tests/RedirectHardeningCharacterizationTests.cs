using System.Net;
using System.Net.Sockets;
using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ تَحصينِ التَحويلاتِ والرُؤوس (‏2026-11-11، W13-4·C2z·3 — مُراجَعاتُ C2a وC2b: SEC-C2a-3 وSEC-C2a-R3-1 وSEC-C2b-R2-3
//     وSEC-C2a-5 وC2a-P-04 وSEC-C2b-5 وSEC-C2b-R2-6 وC2b-P2-06 وSEC-C2b-R3-3) ═══
//
// **ما سَيَتَغَيَّر**: مُخَطَّطُ وِجهَةِ تَحويلِ مَنطِقَةِ المَنَصَّةِ مِن {http, https} وَحدَهُما (كانَ `X-Forwarded-Proto` بِحَرفِه: `javascript://`)؛ والتَحويلُ مُؤَقَّت
// (كانَ 301 دائِماً و`/terms` عَلى المُضيفِ سُؤالٌ مَفتوح، والتَراجُعُ نِطاقٌ فارِغ)؛ وذَيلُ `StoreUrl` عَلى المُضيفِ لا يَصيرُ نِسبِيَّ المُخَطَّط (`/\evil.com`)؛ و`Host`
// لا يُقرَأُ ‏500 (`xn--zz`، مَنفَذٌ فَوقَ 65535). **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣): الوِجهَةُ بِمُخَطَّطِ http وhttps وبِلا مُخَطَّطٍ
// بِحَرفِها، والحُكمُ (تَحويلٌ لِـGET/HEAD، و404 لِلكِتابَة)؛ والذُيولُ العادِيَّةُ؛ والمُضيفاتُ العادِيَّةُ عَلى الأُنبوبِ الحَقيقيّ؛ ومَساراتُ الأَدوارِ الصالِحَة.

file sealed class HostHeaderProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private HostHeaderProbe(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<HostHeaderProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformRequestFront();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Host.Value}|{ctx.Request.Path}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new HostHeaderProbe(app, port);
        }
    }

    /// <summary>طَلَبٌ خامٌ بِرَأسِ <c>Host</c> كَما يُكتَب (لا يُطَبِّعُه عَميل) — يُعيدُ رَمزَ الحالَة.</summary>
    public async Task<int> GetAsync(string host, string path)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n"));
        var buffer = new byte[64];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var read = await stream.ReadAtLeastAsync(buffer, 12, throwOnEndOfStream: false, cts.Token);
        return int.Parse(Encoding.ASCII.GetString(buffer, 0, read).Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RedirectHardeningCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("http", "ashare.example.test", "/studio", "?from=store", "http://example.test/studio?from=store")]
    [InlineData("https", "ashare.example.test", "/studio", "", "https://example.test/studio")]
    [InlineData(null, "ashare.example.test", "/admin", "", "https://example.test/admin")]
    [InlineData("", "ashare.example.test:5050", "/pricing", "", "https://example.test:5050/pricing")]
    [InlineData("https", "ashare-vendor.example.test", "/studio/%D9%85", "", "https://example.test/studio/%D9%85")]
    [InlineData("http", "ashare.example.test", "/terms", "", "http://example.test/terms")]
    public void A_platform_area_on_a_store_host_still_leads_to_the_same_apex_url(string? scheme, string host, string path, string query, string location)
    {
        foreach (var method in new[] { "GET", "HEAD" })
        {
            var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, query, scheme);
            Assert.Equal(StoreHostVerdict.Redirect, a.Verdict);
            Assert.Equal(location, a.Location);
        }
        Assert.Equal(StoreHostVerdict.NotFound, TenantResolverMiddleware.AnswerStoreHost(host, Base, "POST", path, query, scheme).Verdict);
        output.WriteLine($"{scheme ?? "—"} {host}{path}{query} ⇒ {location}");
    }

    [Theory]
    [InlineData(null, "", "/")]
    [InlineData(null, "explore", "/explore")]
    [InlineData(null, "explore?category=deals", "/explore?category=deals")]
    [InlineData(null, "listings/3f2c", "/listings/3f2c")]
    [InlineData("vendor", "me/listings", "/r/vendor/me/listings")]
    [InlineData(null, "ashare/explore", "/ashare/ashare/explore")]
    [InlineData(null, "studio", "/ashare/studio")]
    public void Ordinary_tails_on_a_store_host_are_what_they_were(string? role, string tail, string expected)
        => Assert.Equal(expected, StoreUrl.For(new HostView(Base, "ashare", "https", ""), "ashare", role, tail));

    [Theory]
    [InlineData(null, "localhost", "/ashare")]
    [InlineData(null, "localhost:5050", "/ashare/explore")]
    [InlineData(null, "LOCALHOST", "/")]
    [InlineData(null, "xn--mgbh0fb.localhost", "/")]        // ‏punycode صالِح يُقرَأ
    [InlineData(Base, "ashare.example.test", "/explore")]
    [InlineData(Base, "example.test", "/studio")]
    [InlineData(Base, "example.test:65535", "/")]
    public async Task Ordinary_host_headers_reach_the_app(string? baseDomain, string host, string path)
    {
        await using var probe = await HostHeaderProbe.StartAsync(baseDomain);
        var got = await probe.GetAsync(host, path);
        output.WriteLine($"{baseDomain ?? "مَسار"} · Host {host} {path} ⇒ {got}");
        Assert.Equal(200, got);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/explore")]
    [InlineData("/me/listings")]
    public void A_sound_home_route_stays_sound(string route)
        => Assert.DoesNotContain(RoleDefinitionValidator.Validate(Sound() with { HomeRoute = route }), v => v.Code == "home_route_malformed");

    private static RoleDefinition Sound() => new()
    {
        Slug = "driver",
        Icon = "🚗",
        HomeRoute = "/explore",
        Label = new LocalizedText("سائِق"),
        Description = new LocalizedText("سائِق مَركَبَة."),
        Permissions = ["listing.browse", "offer.submit"],
        Fields = [],
        Composition = new RoleComposition(),
    };
}
