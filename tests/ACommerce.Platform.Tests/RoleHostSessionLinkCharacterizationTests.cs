using System.Reflection;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ رَوابِطِ المَتجَرِ وتَحويلاتِه لِما يَنجو قَبلَ أَن يَبقى الرابِطُ بِلا دَورٍ عَلى مُضيفِ الدَور (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: (١) رابِطٌ بِلا دَورٍ عَلى مُضيفِ الدَور (`StoreUrl.Link(view, slug, null, …)` — الجَرَس، وتَنزيلُ بَياناتي، ومُرَشِّحاتُ الاستِكشاف،
// ورَقائِقُ التَصنيف، و«إلغاء»/«رُجوع») كانَ يُسَمّي مُضيفَ المَتجَرِ `{scheme}://{slug}.R/…` مُطلَقاً — والكَعكَةُ مَقصورَةٌ عَلى مُضيفِ الدَور (لا `Domain=`)،
// فَالنَقرَةُ تَحمِلُ العُضوَ إلى حَيثُ لا جَلسَةَ لَه؛ (٢) تَحويلُ «حِفظِ بَياناتِ الدَور» عَلى مُضيفٍ كانَ يَبني مُضيفَ الدَورِ (`RoleHome`) فَيُغادِرُ المُضيفَ الَّذي كَتَبَ
// الكَعكَة. **وما يَنجو ويُثَبَّتُ هُنا**: وَضعُ المَسارِ بِحَرفِه؛ ورَوابِطُ مُضيفِ المَتجَرِ بِلا دَورٍ نَظيفَة؛ والرابِطُ العابِرُ المُعلَنُ إلى تَطبيقِ دَورٍ آخَر (بِطاقاتُ
// الأَدوار، ودَورٌ مُسَمّى غَيرُ دَورِ المُضيف) يُسَمّي مُضيفَ ذلكَ التَطبيق؛ ودَورُ المُضيفِ نَفسُه نَظيف؛ وما لا يَحقِنُه الحاقِنُ بِشَكلِ المَسار؛ وبَيتُ الدَورِ بَعدَ
// اختِيارِه في المُنتَقي (`RoleHome`) — عابِرٌ مُعلَن. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class RoleHostSessionLinkCharacterizationTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly HostView StoreHost = new(Apex, "ashare", "https", "");
    private static HostView RoleHost(string role) => new(Apex, "ashare", "https", "") { HostRole = role };

    private static HostView ViewAt(string where) => where switch
    {
        "store" => StoreHost,
        "vendor" => RoleHost("vendor"),
        "apex" => new HostView(Apex, null, "https", ""),
        _ => default,
    };

    /// <summary><c>StoreUrl.Link</c> — ما يَنجو: المَسار، ومُضيفُ المَتجَرِ بِلا دَور، والعابِرُ المُعلَن، ودَورُ المُضيفِ نَفسُه، ومَتجَرٌ آخَر.</summary>
    [Theory]
    [InlineData("none", "ashare", null, "notifications", "/ashare/notifications")]
    [InlineData("none", "ashare", null, "me/data/download", "/ashare/me/data/download")]
    [InlineData("none", "ashare", "vendor", "me/listings", "/ashare/r/vendor/me/listings")]
    [InlineData("none", "ashare", null, "r/vendor/login", "/ashare/r/vendor/login")]
    [InlineData("apex", "ashare", null, "explore?category=x", "/ashare/explore?category=x")]
    [InlineData("store", "ashare", null, "notifications", "/notifications")]
    [InlineData("store", "ashare", null, "me/data/download", "/me/data/download")]
    [InlineData("store", "ashare", null, "explore?filters=open", "/explore?filters=open")]
    [InlineData("store", "ashare", null, "terms?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("store", "ashare", null, "r/vendor/login", "https://ashare-vendor.example.test/login")]
    [InlineData("store", "ashare", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("store", "ashare", "mandoob", "me", "/r/mandoob/me")]
    [InlineData("vendor", "ashare", "vendor", "notifications", "/notifications")]
    [InlineData("vendor", "ashare", "vendor", "me/data/download", "/me/data/download")]
    [InlineData("vendor", "ashare", "vendor", "", "/")]
    [InlineData("vendor", "ashare", null, "r/vendor/login", "/login")]
    [InlineData("vendor", "ashare", null, "r/customer/login", "https://ashare-customer.example.test/login")]
    [InlineData("vendor", "ashare", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("vendor", "ashare", "mandoob", "me", "https://ashare.example.test/r/mandoob/me")]
    [InlineData("vendor", "ejar", null, "explore", "/ejar/explore")]
    [InlineData("vendor", "ejar", "vendor", "me", "/ejar/r/vendor/me")]
    public void The_link_answers_that_survive(string where, string slug, string? role, string path, string expected)
    {
        var got = StoreUrl.Link(ViewAt(where), slug, role, path);
        output.WriteLine($"{where}: ({slug}, {role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary><c>StoreUrl.For</c> — ما لا يُغادِرُ المُضيف (تَحويلاتُ الاستِمارات وما بَعدَ الدُخول) عَلى المُضيفاتِ الثَلاثَة.</summary>
    [Theory]
    [InlineData("none", null, "login?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("store", null, "login?returnUrl=%2Fashare%2Flistings%2Fcreate", "/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("vendor", null, "login?returnUrl=%2Fashare%2Flistings%2Fcreate", "/login?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("vendor", null, "terms?returnUrl=%2Fashare%2Flistings%2Fcreate", "/ashare/terms?returnUrl=%2Fashare%2Flistings%2Fcreate")]
    [InlineData("vendor", null, "me/listings", "/me/listings")]
    [InlineData("vendor", "vendor", "me/listings", "/me/listings")]
    [InlineData("store", null, "me/listings", "/me/listings")]
    [InlineData("none", null, "me/listings", "/ashare/me/listings")]
    public void The_redirect_answers_that_survive(string where, string? role, string path, string expected)
    {
        var got = StoreUrl.For(ViewAt(where), "ashare", role, path);
        output.WriteLine($"{where}: ({role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    private static readonly MethodInfo RoleHomeMethod = typeof(MarketplaceTemplateExtensions)
        .GetMethod("RoleHome", BindingFlags.NonPublic | BindingFlags.Static, [typeof(HttpRequest), typeof(string), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("‏`MarketplaceTemplateExtensions.RoleHome` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    /// <summary>بَيتُ الدَورِ بَعدَ اختِيارِه في المُنتَقي (<c>POST /{slug}/me/role/save</c> — <c>RoleHome</c>): بِشَكلِ المَسارِ بِلا دَورٍ كَما كان، وعَلى مُضيفٍ لِلمَتجَرِ
    /// مُضيفُ الدَورِ المُختار — عابِرٌ مُعلَن.</summary>
    [Theory]
    [InlineData("none", "vendor", "/me/listings", "/ashare/me/listings")]
    [InlineData("none", "customer", "", "/ashare")]
    [InlineData("store", "vendor", "/me/listings", "https://ashare-vendor.example.test/me/listings")]
    [InlineData("store", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("vendor", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("vendor", "vendor", "/me/listings", "/me/listings")]
    public void The_role_home_after_picking_a_role_answers_as_today(string where, string role, string home, string expected)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "https";
        ctx.Request.Path = "/ashare/me/role/save";
        var view = ViewAt(where);
        ctx.Request.Host = new HostString(where switch { "store" => "ashare.example.test", "vendor" => "ashare-vendor.example.test", _ => "localhost" });
        if (view.Apex is { } apex) ctx.Items[StoreHostItems.Apex] = apex;
        if (view.HostSlug is { } hostSlug) ctx.Items[StoreHostItems.Slug] = hostSlug;
        if (view.HostRole is { } hostRole) ctx.Items[StoreHostItems.Role] = hostRole;
        var got = (string)RoleHomeMethod.Invoke(null, [ctx.Request, "ashare", role, home])!;
        output.WriteLine($"{where}: ({role}, «{home}») ⇒ {got}");
        Assert.Equal(expected, got);
    }
}
