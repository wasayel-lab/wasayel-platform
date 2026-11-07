using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ الرَوابِطِ مِن صَفحَةٍ تَحمِلُ دَورَها لِما يَنجو، قَبلَ أَن يَبقى رابِطُ دَورِ الصَفحَةِ عَلى المُضيف (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-02 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: عَلى مُضيفِ المَتجَرِ صَفَحاتُ `/r/{role}/…` (حَيثُ يَنزِلُ دُخولُ الدَورِ بِتَحويلِ `For`) تَبني رَوابِطَها بِدَورِها
// (`LinkFor(slug, Role, …)` — شَريطُ التَنَقُّلِ كُلُّه)، و`StoreUrl.Link` كانَ يُسَمّي لِذلكَ الدَورِ مُضيفَه `{slug}-{rolehost}.R` مُطلَقاً — والكَعكَةُ عَلى مُضيفِ المَتجَر:
// كُلُّ نَقرَةٍ قَفزَةٌ إلى مُضيفٍ بِلا جَلسَة (مَقيسٌ حَيّاً: 19 هَدَفاً خارِجَ المُضيفِ مِن `/r/customer` و`/r/vendor/me/listings`). وكَذلكَ رابِطُ دَورِ الصَفحَةِ عَلى مُضيفِ
// دَورٍ آخَر. فَيَصيرُ دَورُ الصَفحَةِ (`/r/{role}` في مَسارِ الطَلَبِ بَعدَ الحَقن) كَدَورِ المُضيف: `For` عَلى المُضيفِ نَفسِه؛ والعابِرُ المُعلَنُ (بِطاقاتُ الأَدوار، وبَيتُ
// الدَورِ بَعدَ المُنتَقي) يُسَمّى بِاسمِه `StoreUrl.App`. **وما يَنجو ويُثَبَّتُ هُنا** — والطَلَبُ مِن `HttpContext` كَما يَترُكُه الحاقِن (`AuthSession.LinkFor` بِالوَصّالِ
// الساكِنِ كَما يَضَعُه `Program.cs`): وَضعُ المَسارِ بِحَرفِه، وكُلُّ رابِطٍ بِلا دَورٍ عَلى المُضيفِ نَفسِه، ودَورُ المُضيفِ نَفسُه، ودَورٌ مُسَمّىً غَيرُ دَورِ الصَفحَةِ
// ودَورِ المُضيف (وبِطاقَةُ دَورٍ `r/{role}/login`) يُسَمّي تَطبيقَه، ودَورٌ مُؤَلَّفٌ بِلا اسمِ مُضيفٍ عَلى مُضيفِ المَتجَر. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class PageRoleLinkCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>طَلَبُ <c>GET</c> كَما يَترُكُه الحاقِن: المَسارُ بَعدَ الحَقن، وعَناصِرُ المُضيف.</summary>
    internal static DefaultHttpContext Page(string where, string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "https";
        ctx.Request.Path = path;
        ctx.Request.Host = new HostString(where switch { "store" => "ashare.example.test", "vendor" => "ashare-vendor.example.test", _ => "localhost" });
        if (where != "none")
        {
            ctx.Items[StoreHostItems.Apex] = "example.test";
            ctx.Items[StoreHostItems.Slug] = "ashare";
            if (where == "vendor") ctx.Items[StoreHostItems.Role] = "vendor";
        }
        return ctx;
    }

    internal static string LinkForOn(HttpContext ctx, string slug, string? role, string path)
    {
        var previous = AuthSession.HttpAccessor;
        AuthSession.HttpAccessor = new HttpContextAccessor { HttpContext = ctx };
        try { return AuthSession.LinkFor(slug, role, path); }
        finally { AuthSession.HttpAccessor = previous; }
    }

    [Theory]
    // وَضعُ المَسار — صَفحَةُ دَورٍ وصَفحَةٌ بِلا دَور
    [InlineData("none", "/ashare/r/vendor/me/listings", "ashare", "vendor", "explore", "/ashare/r/vendor/explore")]
    [InlineData("none", "/ashare/r/vendor/me/listings", "ashare", null, "notifications", "/ashare/notifications")]
    [InlineData("none", "/ashare/r/vendor", "ashare", null, "r/customer/login", "/ashare/r/customer/login")]
    [InlineData("none", "/ashare/explore", "ashare", "customer", "", "/ashare/r/customer")]
    // مُضيفُ المَتجَر، صَفحَةُ دَور: بِلا دَورٍ عَلَيه، ودَورٌ آخَرُ وبِطاقَةُ دَورٍ آخَرَ تُسَمّيانِ تَطبيقَهُما، والمُؤَلَّفُ عَلَيه
    [InlineData("store", "/ashare/r/vendor/me/listings", "ashare", null, "notifications", "/notifications")]
    [InlineData("store", "/ashare/r/vendor/me/listings", "ashare", null, "me/data/download", "/me/data/download")]
    [InlineData("store", "/ashare/r/vendor/me/listings", "ashare", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("store", "/ashare/r/vendor", "ashare", null, "r/customer/login", "https://ashare-customer.example.test/login")]
    [InlineData("store", "/ashare/r/vendor/me", "ashare", "mandoob", "me", "/r/mandoob/me")]
    // مُضيفُ المَتجَر، صَفحَةٌ بِلا دَور: بِطاقاتُ الأَدوارِ ودَورٌ مُسَمّىً تُسَمّي تَطبيقَها
    [InlineData("store", "/ashare", "ashare", null, "r/vendor/login", "https://ashare-vendor.example.test/login")]
    [InlineData("store", "/ashare/explore", "ashare", "vendor", "me/listings", "https://ashare-vendor.example.test/me/listings")]
    [InlineData("store", "/ashare/explore", "ashare", null, "explore?category=x", "/explore?category=x")]
    // مُضيفُ الدَور: دَورُه، وبِلا دَور، والعابِرُ إلى دَورٍ آخَر
    [InlineData("vendor", "/ashare/r/vendor/me", "ashare", "vendor", "explore", "/explore")]
    [InlineData("vendor", "/ashare/r/vendor/me", "ashare", null, "notifications", "/notifications")]
    [InlineData("vendor", "/ashare/r/vendor/me", "ashare", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("vendor", "/ashare/r/customer/explore", "ashare", null, "notifications", "/notifications")]
    [InlineData("vendor", "/ashare/r/customer/explore", "ashare", "vendor", "me", "/me")]
    // مَتجَرٌ آخَرُ عَلى هذا المُضيف — بِشَكلِ المَسار
    [InlineData("store", "/ashare/r/vendor/me", "ejar", "vendor", "me", "/ejar/r/vendor/me")]
    public void The_link_from_a_page_answers_that_survive(string where, string path, string slug, string? role, string tail, string expected)
    {
        var got = LinkForOn(Page(where, path), slug, role, tail);
        output.WriteLine($"{where} {path}: ({slug}, {role ?? "—"}, «{tail}») ⇒ {got}");
        Assert.Equal(expected, got);
    }
}
