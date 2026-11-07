using System.Reflection;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ رَوابِطِ المَتجَرِ بِشَكلِ المَسارِ لِما يَنجو قَبلَ أَن تَعرِفَ المُضيف (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: `AuthSession.LinkFor` (‏170 مَوضِعَ استِدعاء) و`Link(req, slug, …)` (كُلُّ تَحويلٍ بَعدَ استِمارَة) يَصيرانِ عارِفَينِ
// بِمُضيفِ المَتجَر — عَلى `{slug}.R` يُسقِطانِ `/{slug}` البادِئ. **وما يَنجو ويُثَبَّتُ هُنا**: كُلُّ ما لَيسَ عَلى مُضيفِ المَتجَرِ نَفسِه — بِلا سِياقِ طَلَب،
// وطَلَبٌ بِلا عَناصِرِ الحاقِن (النِطاقُ فارِغ، أَو الجَذر)، وطَلَبٌ عَلى مُضيفِ مَتجَرٍ آخَر — يُجابُ بِشَكلِ المَسارِ بِحَرفِه، والـReferer يَختارُ الدَورَ كَما
// كان. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل؛ وبُرهانُ ما يُرسَمُ بايتاً بِبايتٍ بَوّابَةُ المَظهَر (`scripts/compare-appearance.sh`).

public class StoreLinkCharacterizationTests(ITestOutputHelper output)
{
    /// <summary><c>AuthSession.LinkFor</c> بِشَكلِ المَسار — والطَلَبُ الحالِيُّ (إن كانَ) لَيسَ عَلى مُضيفِ مَتجَر.</summary>
    [Theory]
    [InlineData("ashare", null, "", "/ashare")]
    [InlineData("ashare", null, "explore", "/ashare/explore")]
    [InlineData("ashare", null, "/explore", "/ashare/explore")]
    [InlineData("ashare", null, "explore/", "/ashare/explore")]
    [InlineData("ashare", null, "login?err=x&as=vendor", "/ashare/login?err=x&as=vendor")]
    [InlineData("ashare", null, "?deleted=1", "/ashare/?deleted=1")]
    [InlineData("ashare", "", "me", "/ashare/me")]
    [InlineData("ashare", "vendor", "", "/ashare/r/vendor")]
    [InlineData("ashare", "vendor", "me/listings", "/ashare/r/vendor/me/listings")]
    [InlineData("ashare", "tenant_admin", "manage", "/ashare/r/tenant_admin/manage")]
    [InlineData("ashare", null, "r/vendor/login", "/ashare/r/vendor/login")]
    [InlineData("rewalk-071351", "customer", "/explore", "/rewalk-071351/r/customer/explore")]
    [InlineData("theme-demo", "customer", "listings/3f2c?x=1", "/theme-demo/r/customer/listings/3f2c?x=1")]
    public void LinkFor_answers_with_the_path_form(string slug, string? role, string path, string expected)
    {
        var got = AuthSession.LinkFor(slug, role, path);
        output.WriteLine($"({slug}, {role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    private static readonly MethodInfo LinkMethod = typeof(MarketplaceTemplateExtensions)
        .GetMethod("Link", BindingFlags.NonPublic | BindingFlags.Static, [typeof(HttpRequest), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("‏`MarketplaceTemplateExtensions.Link` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    /// <summary><c>Link(req, slug, path)</c> بِشَكلِ المَسار: الطَلَبُ بِلا مَتجَرِ مُضيفٍ (أَو بِمَتجَرٍ آخَرَ عَلى مُضيفِه)، والدَورُ مِن الـReferer كَما كان.</summary>
    [Theory]
    // بِلا نِطاق — المَسارُ وَحدَه
    [InlineData("localhost:5050", null, "http://localhost:5050/ashare/r/customer/login", "ashare", "login?stage=verify&attempt=x", "/ashare/r/customer/login?stage=verify&attempt=x")]
    [InlineData("localhost:5050", null, "http://localhost:5050/ashare/login", "ashare", "me/name", "/ashare/me/name")]
    [InlineData("localhost:5050", null, "", "ashare", "login?err=phone_required", "/ashare/login?err=phone_required")]
    [InlineData("localhost:5050", null, "http://localhost:5050/ashare/r/vendor/listings/new", "ashare", "me/listings", "/ashare/r/vendor/me/listings")]
    [InlineData("example.test", null, "https://example.test/theme-demo/r/driver/deals", "theme-demo", "deals/1?ok=1", "/theme-demo/r/driver/deals/1?ok=1")]
    // Referer نَظيفٌ ولا مُضيفَ في الطَلَب (النِطاقُ فارِغ) — لا دَور
    [InlineData("ashare.localhost:5050", null, "http://ashare.localhost:5050/r/customer/login", "ashare", "me", "/ashare/me")]
    // مَتجَرٌ آخَرُ عَلى مُضيفِه: الرابِطُ لِغَيرِه بِشَكلِ المَسار — والـReferer يُقرَأُ بَعدَ `/{مَتجَرِ المُضيف}` كَما حَقَنَه الحاقِن، فَلا دَور (مَقيس)
    [InlineData("ejar.localhost:5050", "ejar", "http://ejar.localhost:5050/ashare/r/vendor/x", "ashare", "me", "/ashare/me")]
    [InlineData("ejar.localhost:5050", "ejar", "", "ashare", "", "/ashare")]
    public void Link_answers_with_the_path_form_off_the_stores_own_host(string host, string? hostSlug, string referer, string slug, string path, string expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Method = "POST";
        ctx.Request.Host = new HostString(host);
        if (hostSlug is not null) ctx.Items[ACommerce.Kit.Tenants.StoreHostItems.Slug] = hostSlug;
        if (referer.Length > 0) ctx.Request.Headers.Referer = referer;
        var got = (string)LinkMethod.Invoke(null, [ctx.Request, slug, path])!;
        output.WriteLine($"{host} ({hostSlug ?? "مَسار"}) «{referer}» ⇒ {got}");
        Assert.Equal(expected, got);
    }
}
