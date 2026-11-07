using System.Net;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ سَطحِ الـAPI عَلى مُضيفِ المَتجَرِ وبابِ المُعَرِّفِ لِما يَنجو (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1 — القاعِدَة ٣) ═══
//
// **المَقيسُ قَبل** (عِندَ `b8bf5b1`، خادِمٌ حَيٌّ بِـ`MultiTenancy__BaseDomain=localhost`): المُشرِفُ أَنشَأَ مَتجَرَينِ `push` و`v1` (‏302 /admin) — فَعَلى
// `ashare.localhost` أَجابَ `GET /api/push/manifest.json` بِـ200 وجِسمُه بَيانُ مَتجَرِ `push`، و`/api/push/icon.svg` و`/og.png` و`/unread-counts` 200، و
// `POST /api/push/push/subscribe` بَلَغَ بابَ `push` (‏401 لِغِيابِ الكوكي) — بِسِياقِ `ashare` ومُستَأجِرِ Marten `push`؛ وعَلى `v1.localhost` بَلَغَ
// `GET /api/v1/deals` سَطحَ API المَنَصَّة (‏401). **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه**:
// ‏`/api/push/vapid-key` و`/api/{مَتجَرِ المُضيف}/…` يُخدَمانِ عَلى مُضيفِه، و`/api/{مَتجَرٍ آخَر}` و`/api/v1` و`/api/billing` و`/api` 404 عَلى مُضيفِ مَتجَرٍ
// عادِيّ، والجَذرُ يَمُرُّ بِكُلِّ `/api/…` كَما هُوَ؛ وجَوابُ بابِ المُعَرِّفِ لِأَسماءٍ تُجاوِرُ `push` و`v1` ولا تُساويها؛ **ومَسارُ مَتجَرٍ اسمُه `push` أَو `v1`
// يَبقى يَحُلُّه `SlugFromPath`** (الاسمانِ خارِجَ `ReservedTenantSlugs.All`). **وما يَتَغَيَّرُ عَمداً لا يَرِد**: ‏`/api/push/{غَيرِ vapid-key}` عَلى أَيِّ
// مُضيف، والكِتابَةُ إلى `/api/push/vapid-key`، ومُضيفا `push.R` و`v1.R`، وجَوابُ البابِ لِـ`push` و`v1` بِأَعيانِهِما.

file sealed class ApiSurfaceProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    private ApiSurfaceProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    public static async Task<ApiSurfaceProbe> StartAsync(string baseDomain, IDocumentStore store)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            builder.Services.AddSingleton(store);
            builder.Services.AddMemoryCache();
            using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.UsePlatformMultiTenancy();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}|tenant={ctx.Items[TenantKeys.Slug]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new ApiSurfaceProbe(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async Task<(int Status, string Body)> SendAsync(string method, string host, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (method == "POST") req.Content = new FormUrlEncodedContent([]);
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

public class StoreHostApiSurfaceCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    private static string Show(StoreHostAnswer a) => $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}";

    /// <summary>بِالدالَّةِ النَقِيَّة: سَطحُ الـAPI عَلى مُضيفِ مَتجَرٍ عادِيٍّ والجَذر — ما يَنجو.</summary>
    [Theory]
    [InlineData("ashare.example.test", "GET", "/api/push/vapid-key", "Pass|ashare|")]
    [InlineData("ashare.example.test", "HEAD", "/api/push/vapid-key", "Pass|ashare|")]
    [InlineData("ejar.example.test", "GET", "/api/push/vapid-key", "Pass|ejar|")]
    [InlineData("ashare.example.test", "GET", "/api/ashare/manifest.json", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/ashare/r/vendor/manifest.json", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/ashare/icon.svg", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/ashare/og.png", "Pass|ashare|")]
    [InlineData("ashare.example.test", "POST", "/api/ashare/push/subscribe", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/API/Ashare/unread-counts", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/ashare", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/ejar/manifest.json", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/ejar/unread-counts", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/v1/deals", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/v1", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/v1/deals/0f8fad5b-d9cb-469f-a165-70867728950e/advance", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/billing/paypal", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/billing/paddle", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/payments/receipt/x", "NotFound||")]
    [InlineData("example.test", "GET", "/api/push/manifest.json", "Pass||")]
    [InlineData("example.test", "GET", "/api/push/vapid-key", "Pass||")]
    [InlineData("example.test", "GET", "/api/v1/deals", "Pass||")]
    [InlineData("example.test", "POST", "/api/billing/paypal", "Pass||")]
    [InlineData("www.example.test", "GET", "/api/push/manifest.json", "Pass||")]
    [InlineData("localhost:5050", "GET", "/api/push/manifest.json", "Pass||")]
    public void The_api_surface_on_an_ordinary_store_host_and_the_apex_keeps_its_answer(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, "", "https");
        output.WriteLine($"{method} {host}{path} ⇒ {Show(a)}");
        Assert.Equal(expected, Show(a));
    }

    /// <summary>وبِلا نِطاقٍ — الافتِراضيّ — لا يُمَسُّ طَلَبٌ إلى <c>/api</c> عَلى أَيِّ مُضيف.</summary>
    [Theory]
    [InlineData("push.example.test", "/api/push/manifest.json")]
    [InlineData("v1.example.test", "/api/v1/deals")]
    [InlineData("ashare.example.test", "/api/push/manifest.json")]
    public void With_no_base_domain_the_api_surface_passes_untouched(string host, string path)
        => Assert.Equal("Pass||", Show(TenantResolverMiddleware.AnswerStoreHost(host, null, "GET", path, "", "https")));

    /// <summary><b>المَسارُ يَبقى</b>: مَتجَرٌ اسمُه <c>push</c> أَو <c>v1</c> يَحُلُّه مَسارُه، والاسمانِ خارِجَ القائِمَةِ الَّتي يَقرَؤُها الحالّ — فَالإصلاحُ لا يُفقِدُ
    /// مَتجَراً قائِماً مَسارَه (دَرسُ <c>lang</c>، C2a-P-02).</summary>
    [Theory]
    [InlineData("push")]
    [InlineData("v1")]
    public void A_store_named_after_a_platform_api_segment_keeps_its_path(string slug)
    {
        Assert.False(ReservedTenantSlugs.Contains(slug));
        Assert.Equal(slug, TenantResolverMiddleware.SlugFromPath($"/{slug}"));
        Assert.Equal(slug, TenantResolverMiddleware.SlugFromPath($"/{slug}/explore"));
        Assert.Equal(slug, TenantResolverMiddleware.SlugFromPath($"/{slug}/r/vendor/me"));
        Assert.Null(TenantResolverMiddleware.SlugFromPath($"/api/{slug}/manifest.json"));
    }

    /// <summary>وبابُ المُعَرِّفِ لِأَسماءٍ تُجاوِرُ <c>push</c> و<c>v1</c> ولا تُساويها — وما كانَ مَحجوزاً يَبقى.</summary>
    [Theory]
    [InlineData("pushes", null)]
    [InlineData("push-up", null)]
    [InlineData("pushcart", null)]
    [InlineData("v2", null)]
    [InlineData("v10", null)]
    [InlineData("v1x", null)]
    [InlineData("payments", null)]
    [InlineData("deals", null)]
    [InlineData("vapid-key", null)]
    [InlineData("billing", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("api", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("Billing", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("push_", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("v1-", TenantFromAnalysisFactory.SlugFormat)]
    public void The_one_slug_door_answers_the_neighbours_of_the_api_segments_as_today(string slug, string? code)
    {
        output.WriteLine($"«{slug}» ⇒ {TenantFromAnalysisFactory.SlugRefusal(slug) ?? "يَمُرّ"} [{string.Join(" ", StoreHostName.Validate(slug).Select(v => v.Code))}]");
        Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(slug));
    }

    /// <summary>بِالوَسيطَينِ الحَقيقِيَّينِ وبِمَتجَرٍ اسمُه <c>push</c> في المَخزَن: مُضيفُ <c>ashare</c> يَخدِمُ مِفتاحَ الإشعارِ وسَطحَه هُوَ ويَرُدُّ ما لِغَيرِه، والجَذرُ
    /// يَمُرُّ بِـ<c>/api/…</c> بِلا سِياقِ مَتجَر، ومَسارُ <c>push</c> عَلى الجَذرِ يَحمِلُ سِياقَه.</summary>
    [Fact]
    public async Task On_the_wire_the_store_host_api_surface_and_the_apex_keep_their_answers()
    {
        var world = new DocWorld()
            .Put(new Tenant { Id = "ashare", Name = "عَشير" })
            .Put(new Tenant { Id = "ejar", Name = "إيجار" })
            .Put(new Tenant { Id = "push", Name = "push" });
        await using var p = await ApiSurfaceProbe.StartAsync(Base, world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Body)[]
        {
            ("GET",  "ashare.example.test", "/api/push/vapid-key", 200, "/api/push/vapid-key|tenant=ashare"),
            ("GET",  "ashare.example.test", "/api/ashare/manifest.json", 200, "/api/ashare/manifest.json|tenant=ashare"),
            ("POST", "ashare.example.test", "/api/ashare/push/subscribe", 200, "/api/ashare/push/subscribe|tenant=ashare"),
            ("GET",  "ashare.example.test", "/api/ejar/manifest.json", 404, ""),
            ("GET",  "ashare.example.test", "/api/v1/deals", 404, ""),
            ("POST", "ashare.example.test", "/api/billing/paypal", 404, ""),
            ("GET",  "ashare.example.test", "/explore", 200, "/ashare/explore|tenant=ashare"),
            ("GET",  "example.test", "/api/push/manifest.json", 200, "/api/push/manifest.json|tenant="),
            ("GET",  "example.test", "/api/v1/deals", 200, "/api/v1/deals|tenant="),
            ("GET",  "example.test", "/push/explore", 200, "/push/explore|tenant=push"),
        };
        foreach (var r in rows)
        {
            var (status, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {body}");
            Assert.Equal((r.Status, r.Body), (status, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }
}
