using System.Net;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُضيفُ المَتجَرِ يَخدِمُ ما لِلمَتجَرِ وَحدَه — G6 وG12 (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·5) ═══
//
// **المَقيسُ قَبل** (A2 §2.3 عِندَ `b03b375`، وصُفوفُه الناجِيَةُ مُوَصَّفَةٌ في `StoreHostPipelineCharacterizationTests` عِندَ `ecd351e`): ‏`/studio`
// و`/admin/tenants/ejar/users` عَلى `ashare.R` تُخدَمُ بِسِياقِ `ashare`؛ و`/api/ejar/…` يُخدَمُ عَلى مُضيفِ `ashare`؛ و`ashare-vendor.R` يُقرَأُ مَتجَراً
// اسمُه `ashare-vendor`؛ و`vendor.ashare.R` و`tenant_admin.R` يُرسَمانِ صَفحَةَ المَنَصَّة؛ ومَتجَرٌ لا وُجودَ لَه عَلى مُضيفِه 200. **والآن** قَرارٌ نَقِيٌّ
// واحِد (`AnswerStoreHost`) يَقرَؤُه الحاقِن، ووُجودُ المَتجَرِ في `TenantResolverMiddleware` (القاعِدَة ٧).

public class StoreHostRequestTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    private static string Show(StoreHostAnswer a) => $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}";

    /// <summary><b>G6 — المَصفوفَةُ النَقِيَّة</b>: (المُضيف، الطَريقَة، المَسار) ← (الحُكم | المَتجَر | المَسارُ أَو الوِجهَة). وفيها صُفوفُ A2 §2.3 الثَمانِيَةَ عَشَرَ
    /// بِأَجوِبَةِ المَرحَلَةِ الأُولى (والصَفُّ 18 بِنِطاقِ <c>localhost</c> في اختِبارِه).</summary>
    [Theory]
    // A2 §2.3
    [InlineData("ashare.example.test", "GET", "/", "Rewrite|ashare|/ashare")]                                    // 1
    [InlineData("ashare.example.test", "GET", "/listings/3f2c", "Rewrite|ashare|/ashare/listings/3f2c")]         // 2
    [InlineData("ashare.example.test", "GET", "/ashare/listings/3f2c", "Pass|ashare|")]                          // 3
    [InlineData("ashare.example.test", "GET", "/sw.js", "Pass|ashare|")]                                         // 4 — كانَ /ashare/sw.js
    [InlineData("ashare.example.test", "GET", "/offline.html", "Pass|ashare|")]                                  // 5
    [InlineData("ashare.example.test", "GET", "/lang/en", "Pass|ashare|")]                                       // 6
    [InlineData("ashare.example.test", "GET", "/studio", "Redirect|ashare|https://example.test/studio")]         // 7 — كانَ بِسِياقِ ashare
    [InlineData("ashare.example.test", "GET", "/admin/tenants/ejar/users", "Redirect|ashare|https://example.test/admin/tenants/ejar/users")] // 8
    [InlineData("ashare.example.test", "GET", "/api/ejar/unread-counts", "NotFound||")]                          // 9 — كانَ يُخدَم
    [InlineData("ashare.example.test", "GET", "/ejar/listings", "Rewrite|ashare|/ashare/ejar/listings")]         // 10
    [InlineData("ashare.example.test", "GET", "/r/vendor/me", "Rewrite|ashare|/ashare/r/vendor/me")]             // 11
    [InlineData("ashare.example.test", "GET", "/_framework/blazor.web.js", "Pass|ashare|")]                      // 12
    [InlineData("ashare-vendor.example.test", "GET", "/me/listings", "NotFound||")]                              // 13 — مُضيفُ دَورٍ لا يُحَلُّ في المَرحَلَةِ الأُولى
    [InlineData("vendor.ashare.example.test", "GET", "/", "NotFound||")]                                         // 14 — كانَ صَفحَةَ المَنَصَّة
    [InlineData("tenant_admin.example.test", "GET", "/", "NotFound||")]                                          // 15
    [InlineData("www.example.test", "GET", "/", "Pass||")]                                                       // 16
    [InlineData("example.test", "GET", "/ashare", "Pass||")]                                                     // 17
    // ومَعَها
    [InlineData("ashare.example.test", "HEAD", "/pricing", "Redirect|ashare|https://example.test/pricing")]
    [InlineData("ashare.example.test", "GET", "/terms/en?x=1", "Redirect|ashare|https://example.test/terms/en?x=1")]
    [InlineData("ashare.example.test:8443", "GET", "/contact", "Redirect|ashare|https://example.test:8443/contact")]
    [InlineData("ashare.example.test", "POST", "/studio/apps/create", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/billing/paypal/return", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/auth/logout", "Rewrite|ashare|/ashare/auth/logout")]
    [InlineData("ashare.example.test", "POST", "/r/vendor/listings/create", "Rewrite|ashare|/ashare/r/vendor/listings/create")]
    [InlineData("ashare.example.test", "GET", "/me/data/download", "Rewrite|ashare|/ashare/me/data/download")]
    [InlineData("ashare.example.test", "GET", "/support", "Rewrite|ashare|/ashare/support")]
    [InlineData("ashare.example.test", "GET", "/help", "Rewrite|ashare|/ashare/help")]
    [InlineData("ashare.example.test", "GET", "/api/ashare/unread-counts", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/API/Ashare/og.png", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/push/vapid-key", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/api/v1/deals", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/billing/paypal", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/robots.txt", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/sitemap.xml", "Pass|ashare|")]
    [InlineData("ashare.example.test", "GET", "/uploads/2026/x.png", "Pass|ashare|")]
    [InlineData("xn--mgbh0fb.example.test", "GET", "/", "NotFound||")]
    [InlineData("ab--cd.example.test", "GET", "/", "NotFound||")]
    [InlineData("best-admin.example.test", "GET", "/", "NotFound||")]
    [InlineData("shop-.example.test", "GET", "/", "NotFound||")]
    [InlineData("mail.example.test", "GET", "/", "Pass||")]
    [InlineData("ashare.evil.test", "GET", "/studio", "Pass||")]
    [InlineData("localhost:5050", "GET", "/studio", "Pass||")]
    public void The_store_host_answer_matrix(string host, string method, string pathAndQuery, string expected)
    {
        var q = pathAndQuery.IndexOf('?');
        var (path, query) = q < 0 ? (pathAndQuery, "") : (pathAndQuery[..q], pathAndQuery[q..]);
        var answer = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, query, "https");
        output.WriteLine($"{method} {host}{pathAndQuery} ⇒ {Show(answer)}");
        Assert.Equal(expected, Show(answer));
    }

    /// <summary>الصَفُّ 18 — التَطويرُ المَحَلِّيُّ بِنِطاقِ <c>localhost</c>، والوِجهَةُ تَحمِلُ المَنفَذ.</summary>
    [Fact]
    public void Under_localhost_the_store_resolves_and_the_platform_redirect_keeps_the_port()
    {
        Assert.Equal("Rewrite|ashare|/ashare", Show(TenantResolverMiddleware.AnswerStoreHost("ashare.localhost:5050", "localhost", "GET", "/", "", "http")));
        Assert.Equal("Redirect|ashare|http://localhost:5050/studio", Show(TenantResolverMiddleware.AnswerStoreHost("ashare.localhost:5050", "localhost", "GET", "/studio", "", "http")));
        Assert.Equal("Pass||", Show(TenantResolverMiddleware.AnswerStoreHost("localhost:5050", "localhost", "GET", "/studio", "", "http")));
    }

    /// <summary>وبِلا نِطاقٍ — الافتِراضيّ — لا يُمَسُّ طَلَب.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_base_domain_every_request_passes_untouched(string? baseDomain)
    {
        foreach (var (host, path) in new[] { ("ashare.example.test", "/studio"), ("x.y.example.test", "/"), ("ashare.example.test", "/api/ejar/x") })
            Assert.Equal("Pass||", Show(TenantResolverMiddleware.AnswerStoreHost(host, baseDomain, "GET", path, "", "https")));
    }

    /// <summary><b>G12 — كُلُّ مَنطِقَةِ مَنَصَّةٍ لا تُخدَمُ عَلى مُضيفِ مَتجَر</b>: 301 إلى الجَذرِ لِـGET، و404 لِلكِتابَة — والعَدَدُ يُطبَع.</summary>
    [Fact]
    public void Platform_areas_are_not_served_on_store_hosts()
    {
        foreach (var area in ReservedTenantSlugs.PlatformAreas)
        {
            Assert.Equal($"Redirect|ashare|https://example.test/{area}/x",
                         Show(TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "GET", $"/{area}/x", "", "https")));
            Assert.Equal("NotFound||", Show(TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "POST", $"/{area}/x", "", "https")));
        }
        output.WriteLine($"مَناطِقُ المَنَصَّةِ المَفحوصَة: {ReservedTenantSlugs.PlatformAreas.Count} ({string.Join(" · ", ReservedTenantSlugs.PlatformAreas.Order())})");
        Assert.True(ReservedTenantSlugs.PlatformAreas.Count >= 9);
    }

    /// <summary>وكُلُّ جَذرِ بِنيَةٍ تَحتِيَّةٍ يُخدَمُ كَما هُوَ عَلى مُضيفِ المَتجَر.</summary>
    [Fact]
    public void Infrastructure_roots_are_served_as_they_are_on_store_hosts()
    {
        foreach (var root in ReservedTenantSlugs.RootEndpoints)
            Assert.Equal("Pass|ashare|", Show(TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "GET", $"/{root}", "", "https")));
        output.WriteLine($"جُذورُ البِنيَةِ المَفحوصَة: {ReservedTenantSlugs.RootEndpoints.Count}");
        Assert.True(ReservedTenantSlugs.RootEndpoints.Count >= 16);
    }

    /// <summary><b>كُلُّ جَذرٍ مَخدومٍ مُصَنَّف</b> (ماسِحُ G3): بِنيَةٌ تَحتِيَّةٌ أَو مَنطِقَةُ مَنَصَّةٍ أَو <c>api</c> — فَجَذرٌ جَديدٌ لا يَقَعُ في المُضيفِ بِلا قَرار.
    /// والصِنفانِ مَحجوزانِ ولا يَتَقاطَعان.</summary>
    [Fact]
    public void Every_served_root_is_classified_for_store_hosts()
    {
        var roots = RootEndpointsReservedTests.RootSegments().Keys.ToList();
        var unclassified = roots.Where(r => !ReservedTenantSlugs.RootEndpoints.Contains(r) && !ReservedTenantSlugs.PlatformAreas.Contains(r)
                                            && !string.Equals(r, ReservedTenantSlugs.ApiRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        output.WriteLine($"الجُذورُ: {roots.Count} · غَيرُ مُصَنَّف: {unclassified.Count} {string.Join(", ", unclassified)}");
        Assert.True(roots.Count > 10);
        Assert.Empty(unclassified);
        Assert.All(ReservedTenantSlugs.RootEndpoints.Concat(ReservedTenantSlugs.PlatformAreas).Append(ReservedTenantSlugs.ApiRoot),
                   r => Assert.True(ReservedTenantSlugs.Contains(r), $"«{r}» غَيرُ مَحجوز"));
        Assert.Empty(ReservedTenantSlugs.RootEndpoints.Intersect(ReservedTenantSlugs.PlatformAreas, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>والمُعَرِّفُ مِن المُضيفِ لا يُعطى لِمِلصَقٍ لا يَصلُحُ اسمَ مَتجَر — والمَتجَرُ القائِمُ الَّذي لا يَجتازُه يَبقى بِمَسارِه.</summary>
    [Theory]
    [InlineData("ashare.example.test", "ashare")]
    [InlineData("ashare-vendor.example.test", null)]
    [InlineData("ab--cd.example.test", null)]
    [InlineData("www.example.test", null)]
    public void The_store_slug_from_a_host_is_a_store_host_name(string host, string? slug)
    {
        Assert.Equal(slug, TenantResolverMiddleware.StoreSlugFromHost(host, Base));
        Assert.Equal("ab--cd", TenantResolverMiddleware.SlugFromPath("/ab--cd/listings"));
    }
}

// ─── الوَسيطانِ الحَقيقِيّانِ عَلى مُضيفٍ مُصَغَّر: الحاقِنُ والحالُّ بِمَخزَنٍ في الذاكِرَة ─────────────────────────────

file sealed class StoreHostPipeline : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    private StoreHostPipeline(WebApplication app, HttpClient client) { _app = app; Client = client; }

    public static async Task<StoreHostPipeline> StartAsync(string baseDomain, IDocumentStore store)
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
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}|{ctx.Items[SubdomainTenantPathMiddleware.OriginalPathItem]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new StoreHostPipeline(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async Task<(int Status, string Location, string Body)> SendAsync(string method, string host, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (method == "POST") req.Content = new FormUrlEncodedContent([]);
        using var res = await Client.SendAsync(req);
        return ((int)res.StatusCode, res.Headers.Location?.ToString() ?? "", await res.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class StoreHostPipelineTests(ITestOutputHelper output)
{
    /// <summary><b>G12 بِالوَسيطِ الحَقيقيّ</b>: مَنطِقَةُ المَنَصَّةِ 301 إلى الجَذرِ والكِتابَةُ إلَيها 404، و<c>/api/{مَتجَرٍ آخَر}</c> 404، ومُضيفُ دَورٍ أَو عُمقٌ ثانٍ 404،
    /// ومَتجَرٌ صالِحُ الاسمِ بِلا وَثيقَةٍ 404 مِن الحالّ — والمَتجَرُ القائِمُ يُحقَنُ ويُحفَظُ مَسارُه الظاهِر. والمَسارُ عَلى الجَذرِ لِمَتجَرٍ مَجهولٍ لا يُمَسّ.</summary>
    [Fact]
    public async Task The_store_host_pipeline_serves_only_what_belongs_to_the_store()
    {
        var world = new DocWorld().Put(new Tenant { Id = "ashare", Name = "عَشير" });
        await using var p = await StoreHostPipeline.StartAsync("example.test", world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Location, string Body)[]
        {
            ("GET", "ashare.example.test", "/listings", 200, "", "/ashare/listings|/listings"),
            ("GET", "ashare.example.test", "/", 200, "", "/ashare|/"),
            ("GET", "ashare.example.test", "/studio?from=store", 301, "http://example.test/studio?from=store", ""),
            ("POST", "ashare.example.test", "/studio/apps/create", 404, "", ""),
            ("GET", "ashare.example.test", "/api/ejar/unread-counts", 404, "", ""),
            ("GET", "ashare-vendor.example.test", "/", 404, "", ""),
            ("GET", "vendor.ashare.example.test", "/", 404, "", ""),
            ("GET", "nosuch.example.test", "/", 404, "", ""),
            ("GET", "example.test", "/nosuch", 200, "", "/nosuch|"),
            ("GET", "www.example.test", "/", 200, "", "/|"),
        };
        foreach (var r in rows)
        {
            var (status, location, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {location}{body}");
            Assert.Equal((r.Status, r.Location, r.Body), (status, location, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }
}
