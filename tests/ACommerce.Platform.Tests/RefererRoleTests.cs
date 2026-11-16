using System.Reflection;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ دَورُ التَحويلِ مِن Referer نَظيفٍ عَلى مُضيفِ المَتجَر (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · C2a-P2-01) ═══
//
// **المَقيسُ قَبل** (عِندَ `b8bf5b1`، خادِمٌ حَيٌّ عَلى 5151): ‏`POST /rewalk-071351/auth/phone/login` بِـReferer `http://rewalk-071351.localhost:5151/r/customer/login`
// — وهو العُنوانُ الَّذي يُعلِنُه الـcanonical وog:url لِصَفحَةِ الدَورِ عَلى مُضيفِها — ← `/rewalk-071351/login?stage=verify…` ثُمَّ `/rewalk-071351/me/name`: سَقَطَ
// الدَورُ مِن كُلِّ تَحويلٍ يَبنيه `Link(req, slug, …)`. **والآن** `StoreHostPage.RoleFromReferer` يَقرَأُ الـReferer النَظيفَ مِن مُضيفِ المَتجَرِ نَفسِه بَعدَ
// `/{hostSlug}` كَما قَرَأَ الحاقِنُ صَفحَتَه، وما سِواه كَما كانَ — والتَوصيفُ `RefererRoleCharacterizationTests` (‏e192c99) بِبَصمَتِه.

public class RefererRoleTests(ITestOutputHelper output)
{
    /// <summary>الدالَّةُ النَقِيَّة: (الـReferer، مُضيفُ الطَلَب، مَتجَرُ المُضيف) ← الدَور.</summary>
    [Theory]
    // نَظيفٌ بِدَورٍ مِن مُضيفِ المَتجَرِ نَفسِه — كانَ null
    [InlineData("http://ashare.localhost:5050/r/customer/login", "ashare.localhost:5050", "ashare", "customer")]
    [InlineData("http://ashare.localhost:5050/r/customer", "ashare.localhost:5050", "ashare", "customer")]
    [InlineData("http://ashare.localhost:5050/R/Vendor/listings/x?y=1", "ashare.localhost:5050", "ashare", "vendor")]
    [InlineData("https://ashare.example.test/r/vendor/me", "ashare.example.test", "ashare", "vendor")]
    [InlineData("https://Ashare.Example.Test/r/vendor/me", "ashare.example.test", "ashare", "vendor")]
    [InlineData("http://rewalk-071351.localhost:5151/r/customer/login", "rewalk-071351.localhost:5151", "rewalk-071351", "customer")]
    // بِشَكلِ المَسارِ كَما كان
    [InlineData("http://ashare.localhost:5050/ashare/r/customer/login", "ashare.localhost:5050", "ashare", "customer")]
    [InlineData("http://ejar.localhost:5050/ejar/r/vendor/x", "ashare.localhost:5050", "ashare", "vendor")]
    // مِن مُضيفٍ آخَرَ أَو مَنفَذٍ آخَرَ أَو مُخَطَّطٍ آخَر — لا دَور
    [InlineData("http://ejar.localhost:5050/r/customer/login", "ashare.localhost:5050", "ashare", null)]
    [InlineData("http://ashare.localhost:5151/r/customer/login", "ashare.localhost:5050", "ashare", null)]
    [InlineData("https://evil.test/r/customer/login", "ashare.localhost:5050", "ashare", null)]
    [InlineData("file:///r/customer/login", "", "ashare", null)]
    // بِلا مُضيفِ مَتجَرٍ (المَسار) — النَظيفُ بِلا دَور كَما كان
    [InlineData("http://localhost:5050/r/customer/login", "localhost:5050", null, null)]
    [InlineData("http://localhost:5050/ashare/r/customer/login", "localhost:5050", null, "customer")]
    // نَظيفٌ بِلا دَور، وجُذورُ المَنَصَّةِ، والفارِغُ والباطِل
    [InlineData("http://ashare.localhost:5050/login", "ashare.localhost:5050", "ashare", null)]
    [InlineData("http://ashare.localhost:5050/", "ashare.localhost:5050", "ashare", null)]
    [InlineData("http://ashare.localhost:5050/sw.js", "ashare.localhost:5050", "ashare", null)]
    [InlineData("http://ashare.localhost:5050/api/ashare/r/vendor/manifest.json", "ashare.localhost:5050", "ashare", null)]
    [InlineData("", "ashare.localhost:5050", "ashare", null)]
    [InlineData("not a url", "ashare.localhost:5050", "ashare", null)]
    [InlineData(null, "ashare.localhost:5050", "ashare", null)]
    public void The_referer_is_read_as_the_host_rewriter_read_the_page(string? referer, string? requestHost, string? hostSlug, string? role)
    {
        var got = StoreHostPage.RoleFromReferer(referer, requestHost, hostSlug);
        output.WriteLine($"«{referer}» عَلى {requestHost} ({hostSlug ?? "مَسار"}) ⇒ {got ?? "بِلا دَور"}");
        Assert.Equal(role, got);
    }

    private static readonly MethodInfo LinkMethod = typeof(MarketplaceTemplateExtensions)
        .GetMethod("Link", BindingFlags.NonPublic | BindingFlags.Static, [typeof(HttpRequest), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("‏`MarketplaceTemplateExtensions.Link` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    /// <summary><b>التَحويلُ نَفسُه</b> — <c>Link</c> بِجِسمِها: مِن صَفحَةِ الدَورِ بِعُنوانِها النَظيفِ عَلى مُضيفِها يَبقى التَحويلُ في <c>/r/{role}</c>.</summary>
    [Theory]
    [InlineData("http://rewalk-071351.localhost:5151/r/customer/login", "rewalk-071351", "login?stage=verify&attempt=x", "/rewalk-071351/r/customer/login?stage=verify&attempt=x")]
    [InlineData("http://rewalk-071351.localhost:5151/r/customer/login?stage=verify", "rewalk-071351", "me/name", "/rewalk-071351/r/customer/me/name")]
    [InlineData("http://rewalk-071351.localhost:5151/r/vendor/listings/new", "rewalk-071351", "me/listings", "/rewalk-071351/r/vendor/me/listings")]
    public void A_post_from_a_clean_role_page_on_its_store_host_keeps_the_role(string referer, string slug, string path, string location)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Method = "POST";
        ctx.Request.Host = new HostString($"{slug}.localhost:5151");
        ctx.Items[StoreHostItems.Slug] = slug;
        ctx.Request.Headers.Referer = referer;
        var got = (string)LinkMethod.Invoke(null, [ctx.Request, slug, path])!;
        output.WriteLine($"{referer} ⇒ {got}");
        Assert.Equal(location, got);
    }

    /// <summary><b>الحارِس</b>: القِراءَةُ لا تُحاكي امتِناعَ الحاقِنِ عَن جُذورِ المَنَصَّة لِأَنّ <c>r</c> لَيسَ جَذراً مَخدوماً — فَإن صارَ (نُقطَةٌ أَو صَفحَةٌ عَلى
    /// <c>/r/…</c> مِن الجَذر) احمَرَّ هذا، وصارَ لِزاماً أَن تُحاكيه.</summary>
    [Fact]
    public void The_role_segment_is_not_a_served_root()
    {
        var roots = RootEndpointsReservedTests.RootSegments();
        output.WriteLine($"الجُذورُ المَخدومَة: {roots.Count}");
        Assert.True(roots.Count > 10, "أَداةٌ عَمياء");
        Assert.DoesNotContain("r", roots.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.False(ReservedTenantSlugs.RootEndpoints.Contains("r") || ReservedTenantSlugs.PlatformAreas.Contains("r")
                     || string.Equals("r", ReservedTenantSlugs.ApiRoot, StringComparison.OrdinalIgnoreCase));
    }
}
