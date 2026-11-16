using System.Reflection;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ دَورِ التَحويلِ مِن الـReferer لِما يَنجو (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · C2a-P2-01 — القاعِدَة ٣) ═══
//
// **المَقيسُ قَبل** (عِندَ `b8bf5b1`، خادِمٌ حَيٌّ بِـ`MultiTenancy__BaseDomain=localhost` عَلى ‏5151): ‏`GET` ‏`rewalk-071351.localhost/rewalk-071351/r/customer`
// ← canonical وog:url ‏`http://rewalk-071351.localhost:5151/r/customer` (العُنوانُ النَظيف)؛ ومِنه `POST /rewalk-071351/auth/phone/login` بِـReferer
// `http://rewalk-071351.localhost:5151/r/customer/login` ← `Location: /rewalk-071351/login?stage=verify…` ثُمَّ `/rewalk-071351/me/name` — **سَقَطَ الدَورُ**؛
// والطَلَبُ نَفسُه بِـReferer بِشَكلِ المَسار ← `/rewalk-071351/r/customer/login?stage=verify…`. لِأَنّ `RoleFromReferer` يَسأَلُ `ExtractRoleFromPath` عَن مَسارِ
// الـReferer كَما هُوَ، وهو يَنتَظِرُ `parts[1]=="r"`. **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ، ولا يُعادُ حَرفٌ مِنه بَعدَ الإصلاح**: كُلُّ
// Referer بِلا مُضيفِ مَتجَر (المَسار)، وكُلُّ Referer بِشَكلِ المَسارِ عَلى أَيِّ مُضيف، وكُلُّ Referer نَظيفٍ مِن مُضيفٍ آخَرَ أَو مَنفَذٍ آخَر، وكُلُّ Referer نَظيفٍ
// بِلا دَورٍ عَلى مُضيفِه — جَوابُه اليَومَ بِحَرفِه. **وما يَتَغَيَّرُ عَمداً لا يَرِد**: Referer نَظيفٌ بِدَورٍ (`/r/{role}/…`) مِن مُضيفِ المَتجَرِ نَفسِه.
//
// و`Link` داخِلِيَّة (`internal`) — تُنادى بِالانعِكاسِ كَما تُنادى `DealsService.IsActorAllowed` في `DealCashPathCharacterizationTests`، فَالمَقيسُ جِسمُها نَفسُه.

public class RefererRoleCharacterizationTests(ITestOutputHelper output)
{
    private static readonly MethodInfo LinkMethod = typeof(MarketplaceTemplateExtensions)
        .GetMethod("Link", BindingFlags.NonPublic | BindingFlags.Static, [typeof(HttpRequest), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("‏`MarketplaceTemplateExtensions.Link` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    private static string Link(string host, string? hostSlug, string? referer, string slug, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString(host);
        ctx.Request.Method = "POST";
        if (hostSlug is not null) ctx.Items[StoreHostItems.Slug] = hostSlug;
        if (referer is not null) ctx.Request.Headers.Referer = referer;
        return (string)LinkMethod.Invoke(null, [ctx.Request, slug, path])!;
    }

    /// <summary>بِلا مُضيفِ مَتجَر (المَسار، والنِطاقُ فارِغ): الدَورُ مِن مَسارِ الـReferer كَما هُوَ.</summary>
    [Theory]
    [InlineData("http://localhost:5050/ashare/r/customer/login", "/ashare/r/customer/login?stage=verify")]
    [InlineData("http://localhost:5050/ashare/R/Vendor/listings", "/ashare/r/vendor/login?stage=verify")]
    [InlineData("http://localhost:5050/ashare/login", "/ashare/login?stage=verify")]
    [InlineData("http://localhost:5050/ashare", "/ashare/login?stage=verify")]
    [InlineData("http://localhost:5050/r/customer/login", "/ashare/login?stage=verify")]
    [InlineData("https://evil.test/ashare/r/vendor/x", "/ashare/r/vendor/login?stage=verify")]
    [InlineData("https://evil.test/r/customer/login", "/ashare/login?stage=verify")]
    [InlineData("", "/ashare/login?stage=verify")]
    [InlineData("not a url", "/ashare/login?stage=verify")]
    [InlineData(null, "/ashare/login?stage=verify")]
    public void Without_a_store_host_the_role_comes_from_the_referer_path_as_today(string? referer, string location)
    {
        var got = Link("localhost:5050", null, referer, "ashare", "login?stage=verify");
        output.WriteLine($"«{referer}» ⇒ {got}");
        Assert.Equal(location, got);
    }

    /// <summary>عَلى مُضيفِ المَتجَر: الـReferer بِشَكلِ المَسارِ (أَيّاً كانَ مُضيفُه)، والنَظيفُ مِن مُضيفٍ آخَرَ أَو مَنفَذٍ آخَر، والنَظيفُ بِلا دَور — كَما اليَوم.</summary>
    [Theory]
    [InlineData("http://ashare.localhost:5050/ashare/r/customer/login", "/ashare/r/customer/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/Ashare/r/customer", "/ashare/r/customer/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/ashare/login", "/ashare/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/login", "/ashare/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/", "/ashare/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/explore", "/ashare/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/sw.js", "/ashare/login?stage=verify")]
    [InlineData("http://ashare.localhost:5050/api/ashare/manifest.json", "/ashare/login?stage=verify")]
    [InlineData("http://ejar.localhost:5050/r/customer/login", "/ashare/login?stage=verify")]
    [InlineData("http://ejar.localhost:5050/ejar/r/vendor/x", "/ashare/r/vendor/login?stage=verify")]
    [InlineData("http://ashare.localhost:5151/r/customer/login", "/ashare/login?stage=verify")]
    [InlineData("https://evil.test/r/customer/login", "/ashare/login?stage=verify")]
    [InlineData("not a url", "/ashare/login?stage=verify")]
    [InlineData(null, "/ashare/login?stage=verify")]
    public void On_a_store_host_the_answers_that_survive_are_todays(string? referer, string location)
    {
        var got = Link("ashare.localhost:5050", "ashare", referer, "ashare", "login?stage=verify");
        output.WriteLine($"«{referer}» ⇒ {got}");
        Assert.Equal(location, got);
    }
}
