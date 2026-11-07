using System.Net;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ الحاقِنِ عَلى مُضيفِ المَتجَرِ لِما يَنجو قَبلَ حَجزِ الجُذورِ الثَلاثَةِ وقَبلَ قَواعِدِ مُضيفِ المَتجَر (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·4/٥ — القاعِدَة ٣) ═══
//
// **الوَسيطُ الحَقيقيُّ عَلى مُضيفٍ مُصَغَّرٍ حَيّ** (`UseSubdomainTenantPath` كَما يُرَكِّبُه `UsePlatformHost`، وبَعدَه طَرَفٌ يَردُّ المَسارَ بَعدَ الحَقنِ وسلاجَ
// المُضيف) — لا الدَوالُّ النَقِيَّة، لِأَنّ البَندَينِ ٤ و٥ يُغَيِّرانِ ما يَسأَلُه الوَسيطُ لا ما يُسأَلُ عَنه. **وما يُثَبَّتُ هُنا صُفوفُ A2 §2.3 الَّتي تَنجو**
// (‏1–3 و10–12 و16–18) وجُذورُ البِنيَةِ التَحتِيَّةِ وAPI المَتجَرِ نَفسِه ومُضيفٌ خارِجَ النِطاقِ ونِطاقٌ فارِغ. **وما يَتَغَيَّرُ عَمداً لا يَرِد**: ‏`/sw.js`
// و`/offline.html` و`/lang/…` (البَند ٤)، ومَناطِقُ المَنَصَّةِ و`/api/{مَتجَرٍ آخَر}/…` ومِلصَقٌ لا يَصلُحُ اسمَ مَتجَرٍ وأَسماءُ المِحوَرِ الآخَرِ
// المَحجوزَةُ في مَسارِ مُضيفِ المَتجَر (البَند ٥). والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ البَندَين.

file sealed class StoreHostProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    private StoreHostProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<StoreHostProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.Run(ctx => ctx.Response.WriteAsync(
                $"{ctx.Request.Path}{ctx.Request.QueryString}|{ctx.Items[SubdomainTenantPathMiddleware.HostSlugItem]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            return new StoreHostProbe(app, client);
        }
    }

    public async Task<(int Status, string Body)> GetAsync(string host, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Host = host;
        using var res = await Client.SendAsync(req);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class StoreHostPipelineCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    /// <summary>(المُضيف، المَسار) ← (المَسارُ بَعدَ الحَقن | سلاجُ المُضيف) — والجَوابُ 200 مِن الطَرَفِ في كُلِّ صَفّ.</summary>
    [Theory]
    // صُفوفُ A2 §2.3 الَّتي تَنجو
    [InlineData("ashare.example.test", "/", "/ashare|ashare")]
    [InlineData("ashare.example.test", "/listings/3f2c", "/ashare/listings/3f2c|ashare")]
    [InlineData("ashare.example.test", "/ashare/listings/3f2c", "/ashare/listings/3f2c|ashare")]
    [InlineData("ashare.example.test", "/ejar/listings", "/ashare/ejar/listings|ashare")]
    [InlineData("ashare.example.test", "/r/vendor/me", "/ashare/r/vendor/me|ashare")]
    [InlineData("ashare.example.test", "/_framework/blazor.web.js", "/_framework/blazor.web.js|ashare")]
    [InlineData("www.example.test", "/", "/|")]
    [InlineData("example.test", "/ashare", "/ashare|")]
    // ومَعَها: الاستِعلامُ والمَنفَذُ وحالَةُ الأَحرُف، وجُذورُ البِنيَةِ التَحتِيَّة، وAPI المَتجَرِ نَفسِه، والمُضيفُ خارِجَ النِطاق
    [InlineData("ashare.example.test", "/explore?q=1", "/ashare/explore?q=1|ashare")]
    [InlineData("ASHARE.example.test:8443", "/login", "/ashare/login|ashare")]
    [InlineData("ashare.example.test", "/ashare/r/driver/deals", "/ashare/r/driver/deals|ashare")]
    [InlineData("ashare.example.test", "/css/site.css", "/css/site.css|ashare")]
    [InlineData("ashare.example.test", "/_content/pkg/x.css", "/_content/pkg/x.css|ashare")]
    [InlineData("ashare.example.test", "/uploads/2026/x.png", "/uploads/2026/x.png|ashare")]
    [InlineData("ashare.example.test", "/branding/ashare.css", "/branding/ashare.css|ashare")]
    [InlineData("ashare.example.test", "/realtime", "/realtime|ashare")]
    [InlineData("ashare.example.test", "/health", "/health|ashare")]
    [InlineData("ashare.example.test", "/robots.txt", "/robots.txt|ashare")]
    [InlineData("ashare.example.test", "/sitemap.xml", "/sitemap.xml|ashare")]
    [InlineData("ashare.example.test", "/api/ashare/unread-counts", "/api/ashare/unread-counts|ashare")]
    [InlineData("ashare.example.test", "/api/push/vapid-key", "/api/push/vapid-key|ashare")]
    [InlineData("localhost:5050", "/studio", "/studio|")]
    [InlineData("example.test", "/studio", "/studio|")]
    [InlineData("example.test", "/", "/|")]
    [InlineData("ashare.evil.test", "/", "/|")]
    public async Task The_store_host_injection_answers_as_today(string host, string path, string expected)
    {
        await using var probe = await StoreHostProbe.StartAsync(Base);
        var (status, body) = await probe.GetAsync(host, path);
        output.WriteLine($"{host}{path} ⇒ {status} {body}");
        Assert.Equal((200, expected), (status, body));
    }

    /// <summary>الصَفُّ الثامِنَ عَشَر: التَطويرُ المَحَلِّيُّ بِنِطاقِ <c>localhost</c>.</summary>
    [Fact]
    public async Task A_store_under_localhost_resolves_for_local_development()
    {
        await using var probe = await StoreHostProbe.StartAsync("localhost");
        var answers = new[]
        {
            await probe.GetAsync("ashare.localhost:5050", "/"),
            await probe.GetAsync("theme-demo.localhost:5050", "/explore"),
            await probe.GetAsync("localhost:5050", "/ashare"),
        };
        foreach (var a in answers) output.WriteLine($"{a.Status} {a.Body}");
        Assert.Equal(new[] { (200, "/ashare|ashare"), (200, "/theme-demo/explore|theme-demo"), (200, "/ashare|") }, answers);
    }

    /// <summary>وبِلا نِطاقٍ أَساسٍ مُهَيَّأٍ — الافتِراضيّ — لا مُضيفَ يُحَلُّ ولا مَسارَ يُمَسّ.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task With_no_base_domain_nothing_is_rewritten(string? baseDomain)
    {
        await using var probe = await StoreHostProbe.StartAsync(baseDomain);
        var answers = new[]
        {
            await probe.GetAsync("ashare.example.test", "/"),
            await probe.GetAsync("ashare.example.test", "/studio"),
            await probe.GetAsync("ashare.example.test", "/sw.js"),
            await probe.GetAsync("localhost:5050", "/ashare/listings"),
        };
        foreach (var a in answers) output.WriteLine($"{a.Status} {a.Body}");
        Assert.Equal(new[] { (200, "/|"), (200, "/studio|"), (200, "/sw.js|"), (200, "/ashare/listings|") }, answers);
    }
}
