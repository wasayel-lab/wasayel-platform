using System.Net;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ وِجهَةِ تَحويلِ مَنطِقَةِ المَنَصَّةِ عَلى مُضيفِ المَتجَرِ لِما يَنجو (‏2026-11-07، مُراجَعَةُ تَيّارِ C2a الجَولَةُ الأُولى · SEC-C2a-2/C2a-P-03 — القاعِدَة ٣) ═══
//
// **المَقيسُ قَبل** (عِندَ `14c6f29`، خادِمٌ حَيٌّ بِـ`MultiTenancy__BaseDomain=localhost` ومُضيفُ `ashare.localhost`): ‏`/studio/%D9%85` و`/studio/caf%C3%A9`
// و`/terms/%E2%80%AE` و`/admin/%0d%0aSet-Cookie:x=1` و`/pricing/%C3%A9?q=%D9%85` ← **500** (`InvalidOperationException: Invalid non-ASCII or control character
// in header` عِندَ `set_Location`، ‏22 سَطراً في سِجِلِّ الخادِم)، و`HEAD` كَذلك؛ و`/studio/%3F%3Fx` ← ‏301 إلى `…/studio/??x` (المَسارُ صارَ استِعلاماً)؛
// و`/studio/%25` ← `…/studio/%`؛ و`/pricing/a%20b` ← `…/pricing/a b`. والعِلَّة: الوِجهَةُ تُبنى مِن المَسارِ **المَفكوك** (`req.Path.Value`).
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه**: وِجهَةُ كُلِّ مَسارٍ عادِيٍّ (حُروفٌ وأَرقامٌ وشَرطَةٌ
// ومَحارِفُ المَسارِ المَسموحَة) والاستِعلامُ كَما أُرسِلَ والمَنفَذُ والمُخَطَّطُ و`HEAD` — بِالوَسيطِ الحَقيقيِّ عَلى مُضيفٍ مُصَغَّر، وبِالدالَّةِ النَقِيَّةِ لِما لا
// يُرسِلُه `HttpClient` كَما هُوَ. **وما يَتَغَيَّرُ عَمداً لا يَرِد**: المَسارُ الَّذي يَحمِلُ مَحرَفاً خارِجَ ASCII أَو مَحرَفَ تَحَكُّمٍ أَو `?`/`%`/مَسافَةً مَفكوكَة.

file sealed class RedirectProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    private RedirectProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    public static async Task<RedirectProbe> StartAsync(string baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new RedirectProbe(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async Task<(int Status, string Location)> SendAsync(string method, string host, string pathAndQuery)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), pathAndQuery);
        req.Headers.Host = host;
        using var res = await Client.SendAsync(req);
        return ((int)res.StatusCode, res.Headers.Location?.OriginalString ?? "");
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class StoreHostRedirectLocationCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    /// <summary>بِالوَسيطِ الحَقيقيّ: (الطَريقَة، المُضيف، المَسارُ والاستِعلام) ← 301 والوِجهَةُ بِحَرفِها — المَسارُ والاستِعلامُ والمَنفَذُ كَما أُرسِلَت.</summary>
    [Fact]
    public async Task An_ordinary_platform_path_on_a_store_host_redirects_to_the_apex_with_the_same_path_query_and_port()
    {
        await using var p = await RedirectProbe.StartAsync(Base);
        var rows = new (string Method, string Host, string PathAndQuery, string Location)[]
        {
            ("GET",  "ashare.example.test",      "/studio",                                   "http://example.test/studio"),
            ("GET",  "ashare.example.test",      "/studio?from=store",                        "http://example.test/studio?from=store"),
            ("GET",  "ashare.example.test",      "/studio/apps/my-store-2/pwa?saved=1",       "http://example.test/studio/apps/my-store-2/pwa?saved=1"),
            ("GET",  "ashare.example.test",      "/admin",                                    "http://example.test/admin"),
            ("GET",  "ashare.example.test",      "/admin/tenants/ejar/users?from=tenant",     "http://example.test/admin/tenants/ejar/users?from=tenant"),
            ("GET",  "ashare.example.test",      "/terms/en?x=1&y=%D9%85",                    "http://example.test/terms/en?x=1&y=%D9%85"),
            ("GET",  "ashare.example.test",      "/billing/paypal/return?token=AB-12",        "http://example.test/billing/paypal/return?token=AB-12"),
            ("GET",  "ashare.example.test:8443", "/contact",                                  "http://example.test:8443/contact"),
            ("GET",  "ashare.example.test",      "/privacy/",                                 "http://example.test/privacy/"),
            ("HEAD", "ashare.example.test",      "/pricing",                                  "http://example.test/pricing"),
            ("HEAD", "ashare.example.test",      "/refunds?lang=en",                          "http://example.test/refunds?lang=en"),
        };
        foreach (var r in rows)
        {
            var (status, location) = await p.SendAsync(r.Method, r.Host, r.PathAndQuery);
            output.WriteLine($"{r.Method} {r.Host}{r.PathAndQuery} ⇒ {status} {location}");
            Assert.Equal((301, r.Location), (status, location));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }

    /// <summary>بِالدالَّةِ النَقِيَّة: مَحارِفُ المَسارِ المَسموحَةُ (غَيرُ المَحجوزَةِ والفَواصِلُ الفَرعِيَّةُ و<c>:</c> و<c>@</c>) تَبقى بِحَرفِها في الوِجهَة،
    /// والمُخَطَّطُ مِن الطَلَبِ و<c>https</c> بِلا مُخَطَّط.</summary>
    [Theory]
    [InlineData("/studio/a-b_c.d~e/f:g@h!$&'()*+,;=", "", "https", "https://example.test/studio/a-b_c.d~e/f:g@h!$&'()*+,;=")]
    [InlineData("/admin/tenants/x9/users", "?a=b&c=%20", "https", "https://example.test/admin/tenants/x9/users?a=b&c=%20")]
    [InlineData("/pricing", "", "http", "http://example.test/pricing")]
    [InlineData("/pricing", "", null, "https://example.test/pricing")]
    [InlineData("/studio/", "?", "https", "https://example.test/studio/?")]
    public void The_pure_answer_keeps_every_allowed_path_character_as_it_is(string path, string query, string? scheme, string location)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "GET", path, query, scheme);
        output.WriteLine($"{path}{query} ⇒ {a.Verdict} {a.Location}");
        Assert.Equal((StoreHostVerdict.Redirect, "ashare", location), (a.Verdict, a.Slug, a.Location));
    }
}
