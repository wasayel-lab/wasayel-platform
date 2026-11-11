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
    [InlineData("ashare-vendor.example.test", "GET", "/me/listings", "Rewrite|ashare|/ashare/me/listings")]      // 13 — كانَ 404 في المَرحَلَةِ الأُولى؛ مُضيفُ دَورٍ مُنذُ W13-4·C2b·4 (والدَورُ بِالقَوالِب — RoleHostTests)
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
    [InlineData("best-admin.example.test", "GET", "/", "Rewrite|best|/best")]   // كانَ 404؛ مُضيفُ دَورِ الإدارَةِ لِمَتجَرِ best مُنذُ W13-4·C2b·4 — ووُجودُ الدَورِ في الحالّ
    [InlineData("shop-.example.test", "GET", "/", "NotFound||")]
    [InlineData("mail.example.test", "GET", "/", "Pass||")]
    [InlineData("ashare.evil.test", "GET", "/studio", "Pass||")]
    [InlineData("localhost:5050", "GET", "/studio", "Pass||")]
    // ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1) سَطحُ مَتجَرٍ اسمُه push لا يُخدَمُ عَلى مُضيفِ غَيرِه — كانَ 200 بِبَيانِه؛ ومِن /api/push/ مِفتاحُ
    // الإشعارِ وَحدَه بِـGET/HEAD؛ ومُضيفا push وv1 لا يُحَلّانِ (StoreHostName) — كانَ v1.R/api/v1/deals يَبلُغُ API المَنَصَّة.
    [InlineData("ashare.example.test", "GET", "/api/push/manifest.json", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push/icon.svg", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push/og.png", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push/unread-counts", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push/r/vendor/manifest.json", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/push/push/subscribe", "NotFound||")]
    [InlineData("ashare.example.test", "POST", "/api/push/vapid-key", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push/vapid-key/x", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/api/push", "NotFound||")]
    [InlineData("ashare.example.test", "GET", "/API/PUSH/VAPID-KEY", "Pass|ashare|")]
    [InlineData("v1.example.test", "GET", "/api/v1/deals", "NotFound||")]
    [InlineData("v1.example.test", "GET", "/", "NotFound||")]
    [InlineData("push.example.test", "GET", "/api/push/manifest.json", "NotFound||")]
    [InlineData("push.example.test", "GET", "/", "NotFound||")]
    [InlineData("billing.example.test", "GET", "/api/billing/paypal", "Pass||")]
    public void The_store_host_answer_matrix(string host, string method, string pathAndQuery, string expected)
    {
        var q = pathAndQuery.IndexOf('?');
        var (path, query) = q < 0 ? (pathAndQuery, "") : (pathAndQuery[..q], pathAndQuery[q..]);
        var answer = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, query, "https");
        output.WriteLine($"{method} {host}{pathAndQuery} ⇒ {Show(answer)}");
        Assert.Equal(expected, Show(answer));
    }

    /// <summary><b>G6 — سَطحُ الـAPI عَلى مُضيفِ المَتجَرِ بِالقَرارِ النَقِيِّ وَحدَه</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1): الحاجِزُ
    /// الثاني لا يَعتَمِدُ عَلى المُصادِق — فَلَو كانَ <c>push</c> أَو <c>v1</c> أَو <c>billing</c> مَتجَرَ مُضيفٍ لَما خُدِمَ لَه <c>/api/{اسمِه}/…</c>، ومِفتاحُ الإشعارِ يُخدَمُ
    /// بِطَرَفِه وطَريقَتَيه وَحدَها.</summary>
    [Theory]
    [InlineData("GET", "/api/push/manifest.json", "push", false)]
    [InlineData("GET", "/api/push/vapid-key", "push", true)]
    [InlineData("GET", "/api/v1/deals", "v1", false)]
    [InlineData("GET", "/API/V1/deals", "v1", false)]
    [InlineData("POST", "/api/v1/deals/x/advance", "v1", false)]
    [InlineData("POST", "/api/billing/paypal", "billing", false)]
    [InlineData("GET", "/api/ashare/manifest.json", "ashare", true)]
    [InlineData("GET", "/api/ashare", "ashare", true)]
    [InlineData("POST", "/api/ashare/push/subscribe", "ashare", true)]
    [InlineData("GET", "/api/ejar/manifest.json", "ashare", false)]
    [InlineData("GET", "/api/push/vapid-key", "ashare", true)]
    [InlineData("HEAD", "/api/push/vapid-key", "ashare", true)]
    [InlineData("GET", "/Api/Push/Vapid-Key", "ashare", true)]
    [InlineData("POST", "/api/push/vapid-key", "ashare", false)]
    [InlineData("GET", "/api/push/vapid-key/", "ashare", false)]
    [InlineData("GET", "/api/push/manifest.json", "ashare", false)]
    [InlineData("GET", "/api", "ashare", false)]
    [InlineData("GET", "/api/", "ashare", false)]
    [InlineData("GET", "/ashare/api/me/unread", "ashare", false)]
    [InlineData("GET", null, "ashare", false)]
    public void The_store_api_surface_is_its_own_and_the_public_push_key_only(string method, string? path, string slug, bool served)
    {
        output.WriteLine($"{method} {path} عَلى {slug}.R ⇒ {(TenantResolverMiddleware.StoreApiServed(method, path, slug) ? "يُخدَم" : "404")}");
        Assert.Equal(served, TenantResolverMiddleware.StoreApiServed(method, path, slug));
    }

    /// <summary><b>G6 — المُضيفُ وَحدَه</b>: مَقاطِعُ API المَنَصَّةِ لا تُحَلُّ مُضيفَ مَتجَرٍ (‏<see cref="StoreHostName"/>)، ويَبقى مَسارُها — والحاجِزانِ مُستَقِلّان.</summary>
    [Theory]
    [InlineData("push")]
    [InlineData("v1")]
    public void A_platform_api_segment_is_not_a_store_host_and_keeps_its_path(string name)
    {
        Assert.Null(TenantResolverMiddleware.StoreSlugFromHost($"{name}.example.test", Base));
        Assert.Equal(name, TenantResolverMiddleware.SlugFromHost($"{name}.example.test", Base));
        Assert.Equal(name, TenantResolverMiddleware.SlugFromPath($"/{name}/explore"));
    }

    /// <summary><b>G6 — المَسارُ المُرَمَّز</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-2/C2a-P-03): الدالَّةُ تَتَلَقّى المَسارَ كَما يَفُكُّه Kestrel
    /// (<c>req.Path.Value</c> — كُلُّ تَرميزٍ مَفكوكٌ إلّا <c>%2F</c>)، والوِجهَةُ تُكتَبُ مُرَمَّزَةً ASCII: كانَ «م» يُكتَبُ حَرفاً ← 500، و<c>%3F</c> يَصيرُ استِعلاماً،
    /// و<c>%25</c> يَصيرُ <c>%</c> باطِلاً، والمَسافَةُ تُكتَبُ مَسافَة. والاستِعلامُ كَما أُرسِلَ إلّا ما لا يَحمِلُه رَأس.</summary>
    [Theory]
    [InlineData("/studio/م", "", "https://example.test/studio/%D9%85")]
    [InlineData("/studio/café", "", "https://example.test/studio/caf%C3%A9")]
    [InlineData("/terms/‮", "", "https://example.test/terms/%E2%80%AE")]
    [InlineData("/admin/\r\nSet-Cookie:x=1", "", "https://example.test/admin/%0D%0ASet-Cookie:x=1")]
    [InlineData("/studio/??x", "", "https://example.test/studio/%3F%3Fx")]
    [InlineData("/studio/?", "", "https://example.test/studio/%3F")]
    [InlineData("/studio/%", "", "https://example.test/studio/%25")]
    [InlineData("/pricing/a b", "", "https://example.test/pricing/a%20b")]
    [InlineData("/pricing/é", "?q=%D9%85", "https://example.test/pricing/%C3%A9?q=%D9%85")]
    [InlineData("/studio/a%2Fb", "", "https://example.test/studio/a%2Fb")]
    [InlineData("/studio", "?q=م x", "https://example.test/studio?q=%D9%85%20x")]
    [InlineData("/studio", "?q=\r\n", "https://example.test/studio?q=%0D%0A")]
    public void An_encoded_platform_path_redirects_with_an_ascii_location(string decodedPath, string query, string location)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "GET", decodedPath, query, "https");
        output.WriteLine($"{decodedPath}{query} ⇒ {a.Verdict} {a.Location}");
        Assert.Equal((StoreHostVerdict.Redirect, location), (a.Verdict, a.Location));
        Assert.All(a.Location!, c => Assert.True(c is > ' ' and < (char)0x7F, $"مَحرَفٌ لا يَحمِلُه رَأس: U+{(int)c:X4}"));
    }

    /// <summary><b>G6 — إدارَةُ مَتجَرِ المُضيفِ عَلى مُضيفِه</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · C2a-P-01): <c>/admin/tenants/{مَتجَرِ المُضيف}</c>
    /// وما تَحتَه يَمُرُّ كَما هُوَ لِكُلِّ طَريقَة — كانَ 301 إلى الجَذرِ حَيثُ لا يُرسَلُ كوكيُّ المَتجَرِ فَتَصيرُ بِطاقاتُ <c>/manage</c> السِتُّ «غَير مُصَرَّح».
    /// وما سِواه في مَنطِقَةِ المَنَصَّةِ مُثَبَّتٌ في <c>StoreHostAdminAreaCharacterizationTests</c>.</summary>
    [Theory]
    [InlineData("GET", "/admin/tenants/ashare")]
    [InlineData("GET", "/admin/tenants/ashare/")]
    [InlineData("GET", "/admin/tenants/ashare/categories")]
    [InlineData("HEAD", "/admin/tenants/ashare/users")]
    [InlineData("GET", "/Admin/Tenants/ASHARE/roles")]
    [InlineData("POST", "/admin/tenants/ashare/categories/save")]
    [InlineData("POST", "/admin/tenants/ashare/users/0f8fad5b-d9cb-469f-a165-70867728950e/grant-admin")]
    public void The_host_store_admin_pages_pass_on_their_own_host(string method, string path)
        => Assert.Equal("Pass|ashare|", Show(TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, method, path, "?from=tenant", "https")));

    /// <summary>مَنطِقَةُ المَنَصَّةِ لا تَحمِلُ سِياقَ مَتجَرٍ عَلى أَيِّ مُضيف — وما سِواها يَحمِلُه.</summary>
    [Theory]
    [InlineData("/admin/tenants/ashare/categories", false)]
    [InlineData("/admin", false)]
    [InlineData("/studio/apps/ashare", false)]
    [InlineData("/ashare/admin", true)]
    [InlineData("/ashare/explore", true)]
    [InlineData("/api/ashare/manifest.json", true)]
    [InlineData("/sw.js", true)]
    [InlineData("/", true)]
    [InlineData(null, true)]
    public void A_platform_area_path_carries_no_store_context(string? path, bool carries)
        => Assert.Equal(carries, TenantResolverMiddleware.CarriesStoreContext(path));

    /// <summary>والمَنفَذُ أَرقامٌ وَحدَها — وما سِواه لا يُنقَلُ إلى الوِجهَة.</summary>
    [Theory]
    [InlineData("ashare.example.test:5050", "https://example.test:5050/studio")]
    [InlineData("ashare.example.test:", "https://example.test/studio")]
    [InlineData("ashare.example.test:50x0", "https://example.test/studio")]
    [InlineData("ashare.example.test:123456", "https://example.test/studio")]
    public void The_redirect_keeps_a_numeric_port_only(string host, string location)
        => Assert.Equal(location, TenantResolverMiddleware.AnswerStoreHost(host, Base, "GET", "/studio", "", "https").Location);

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
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}|{ctx.Items[SubdomainTenantPathMiddleware.OriginalPathItem]}|{ctx.Items[ACommerce.Platform.Shared.TenantKeys.Slug]}"));
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
        // ‏`OriginalString` لا `ToString()`: الثانِيَةُ تَفُكُّ التَرميزَ فَتُخفي ما كُتِبَ في الرَأسِ فِعلاً (‏2026-11-07، SEC-C2a-2).
        return ((int)res.StatusCode, res.Headers.Location?.OriginalString ?? "", await res.Content.ReadAsStringAsync());
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
            ("GET", "ashare.example.test", "/listings", 200, "", "/ashare/listings|/listings|ashare"),
            ("GET", "ashare.example.test", "/", 200, "", "/ashare|/|ashare"),
            ("GET", "ashare.example.test", "/studio?from=store", 302, "http://example.test/studio?from=store", ""),
            ("POST", "ashare.example.test", "/studio/apps/create", 404, "", ""),
            ("GET", "ashare.example.test", "/api/ejar/unread-counts", 404, "", ""),
            ("GET", "ashare-vendor.example.test", "/", 404, "", ""),
            ("GET", "vendor.ashare.example.test", "/", 404, "", ""),
            ("GET", "nosuch.example.test", "/", 404, "", ""),
            ("GET", "example.test", "/nosuch", 200, "", "/nosuch||"),
            ("GET", "www.example.test", "/", 200, "", "/||"),
        };
        foreach (var r in rows)
        {
            var (status, location, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {location}{body}");
            Assert.Equal((r.Status, r.Location, r.Body), (status, location, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }

    /// <summary><b>إدارَةُ مَتجَرِ المُضيفِ بِالوَسيطَينِ الحَقيقِيَّين</b> (‏2026-11-07، C2a-P-01): تَبلُغُ الطَرَفَ عَلى مُضيفِها بِمَسارِها كَما هُوَ وبِلا سِياقِ مَتجَرٍ
    /// (كَالجَذر — قِشرَةُ المَنَصَّة، ولا قُفلَ المُعَلَّق)، والكِتابَةُ إلَيها كَذلك؛ ومَتجَرٌ صالِحُ الاسمِ بِلا وَثيقَةٍ يَبقى 404 عَلى مُضيفِه.</summary>
    [Fact]
    public async Task The_host_store_admin_reaches_the_endpoint_on_its_own_host_without_a_store_context()
    {
        var world = new DocWorld().Put(new Tenant { Id = "ashare", Name = "عَشير" });
        await using var p = await StoreHostPipeline.StartAsync("example.test", world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Location, string Body)[]
        {
            ("GET",  "ashare.example.test", "/admin/tenants/ashare/categories?from=tenant", 200, "", "/admin/tenants/ashare/categories|/admin/tenants/ashare/categories|"),
            ("POST", "ashare.example.test", "/admin/tenants/ashare/categories/save", 200, "", "/admin/tenants/ashare/categories/save|/admin/tenants/ashare/categories/save|"),
            ("GET",  "ashare.example.test", "/admin/tenants/ejar/categories?from=tenant", 302, "http://example.test/admin/tenants/ejar/categories?from=tenant", ""),
            ("GET",  "nosuch.example.test", "/admin/tenants/nosuch/categories", 404, "", ""),
            ("GET",  "ashare.example.test", "/explore", 200, "", "/ashare/explore|/explore|ashare"),
        };
        foreach (var r in rows)
        {
            var (status, location, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {location}{body}");
            Assert.Equal((r.Status, r.Location, r.Body), (status, location, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }

    /// <summary><b>سَطحُ الـAPI بِالوَسيطَينِ الحَقيقِيَّين</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1) — والمَخزَنُ فيه مَتجَرانِ اسمُهُما
    /// <c>push</c> و<c>v1</c> كَما أَنشَأَهُما المُشرِفُ قَبلَ الإصلاح: سَطحُ <c>push</c> لا يُبلَغُ مِن مُضيفِ <c>ashare</c> (كانَ 200 بِسِياقِ <c>ashare</c>)، ولا
    /// <c>/api/v1</c> مِن <c>v1.R</c>، ولا مُضيفَ لَهُما؛ ومِفتاحُ الإشعارِ يُخدَم، ومَسارُ <c>push</c> و<c>v1</c> عَلى الجَذرِ بِسِياقِهِما.</summary>
    [Fact]
    public async Task The_store_host_does_not_serve_another_store_api_or_the_platform_api_on_the_wire()
    {
        var world = new DocWorld()
            .Put(new Tenant { Id = "ashare", Name = "عَشير" })
            .Put(new Tenant { Id = "push", Name = "push" })
            .Put(new Tenant { Id = "v1", Name = "v1" });
        await using var p = await StoreHostPipeline.StartAsync("example.test", world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Location, string Body)[]
        {
            ("GET",  "ashare.example.test", "/api/push/manifest.json", 404, "", ""),
            ("GET",  "ashare.example.test", "/api/push/icon.svg", 404, "", ""),
            ("GET",  "ashare.example.test", "/api/push/unread-counts", 404, "", ""),
            ("POST", "ashare.example.test", "/api/push/push/subscribe", 404, "", ""),
            ("GET",  "ashare.example.test", "/api/push/vapid-key", 200, "", "/api/push/vapid-key|/api/push/vapid-key|ashare"),
            ("GET",  "v1.example.test", "/api/v1/deals", 404, "", ""),
            ("GET",  "v1.example.test", "/", 404, "", ""),
            ("GET",  "push.example.test", "/api/push/manifest.json", 404, "", ""),
            ("GET",  "example.test", "/push/explore", 200, "", "/push/explore||push"),
            ("GET",  "example.test", "/v1", 200, "", "/v1||v1"),
            ("GET",  "example.test", "/api/push/manifest.json", 200, "", "/api/push/manifest.json||"),
        };
        foreach (var r in rows)
        {
            var (status, location, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {location}{body}");
            Assert.Equal((r.Status, r.Location, r.Body), (status, location, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }

    /// <summary><b>المَسارُ المُرَمَّزُ بِالوَسيطِ الحَقيقيّ</b> (‏2026-11-07، SEC-C2a-2/C2a-P-03): 301 بِوِجهَةٍ مُرَمَّزَة لا 500 — والطَلَبُ يَصِلُ مُرَمَّزاً كَما يُرسِلُه
    /// المُتَصَفِّحُ و<c>curl</c>، وKestrel يَفُكُّه قَبلَ الوَسيط.</summary>
    [Fact]
    public async Task An_encoded_platform_path_on_a_store_host_is_a_301_with_an_encoded_location_not_a_500()
    {
        var world = new DocWorld().Put(new Tenant { Id = "ashare", Name = "عَشير" });
        await using var p = await StoreHostPipeline.StartAsync("example.test", world.Store);
        var rows = new (string Method, string Path, string Location)[]
        {
            ("GET",  "/studio/%D9%85",                 "http://example.test/studio/%D9%85"),
            ("HEAD", "/studio/%D9%85",                 "http://example.test/studio/%D9%85"),
            ("GET",  "/studio/caf%C3%A9",              "http://example.test/studio/caf%C3%A9"),
            ("GET",  "/terms/%E2%80%AE",               "http://example.test/terms/%E2%80%AE"),
            ("GET",  "/admin/%0D%0ASet-Cookie:x=1",    "http://example.test/admin/%0D%0ASet-Cookie:x=1"),
            ("GET",  "/pricing/%C3%A9?q=%D9%85",       "http://example.test/pricing/%C3%A9?q=%D9%85"),
            ("GET",  "/studio/%3F%3Fx",                "http://example.test/studio/%3F%3Fx"),
            ("GET",  "/studio/%25",                    "http://example.test/studio/%25"),
            ("GET",  "/pricing/a%20b",                 "http://example.test/pricing/a%20b"),
        };
        foreach (var r in rows)
        {
            var (status, location, _) = await p.SendAsync(r.Method, "ashare.example.test", r.Path);
            output.WriteLine($"{r.Method} ashare.example.test{r.Path} ⇒ {status} {location}");
            Assert.Equal((302, r.Location), (status, location));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }
}
