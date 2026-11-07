using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ حَجزِ «r» اسمَ مُضيف (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · C2b-P2-01 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: عَلى مُضيفِ المَتجَرِ `/r/{role}/…` عَلامَةُ الدَور، و`/{slug}/…` مَسارٌ يَحمِلُ المَتجَرَ فَلا يُحقَن — ومَتجَرٌ اسمُه
// `r` يَجعَلُ الاثنَينِ واحِداً: `r.R/r/vendor/me/listings` يَمُرُّ بِلا حَقنٍ (المَقطَعُ الأَوَّلُ اسمُ المَتجَر) ولا مَسارَ `/{slug}/{role}/…` ← 404، وكُلُّ رابِطِ دَورٍ
// نَظيفٍ يَبنيه `StoreUrl` عَلى مُضيفَيه كَذلك (مَقيسٌ حَيّاً عِندَ f8e02c1: ‏/r/vendor و/r/vendor/login و/r/vendor/me/listings و/r/customer/explore ← 404، وأَوَّلُ
// تَشغيلٍ لِعُضوٍ جَديدٍ يَنتَهي إلَيها). فَيَصيرُ `r` مَحجوزاً **اسمَ مُضيفٍ وَحدَه** (`StoreHostName`، كَـ`push` و`v1`) — لا مَتجَرَ جَديدٌ بِه، والقائِمُ بِلا مُضيف.
// **وما يَنجو ويُثَبَّتُ هُنا**: مَسارُ مَتجَرٍ اسمُه `r` كَما هُوَ (الحَلُّ والرَوابِطُ بِشَكلِ المَسار)، وجيرانُه (`a`، `ar`، `ra`، `rr`، `r1`، `r-a`) أَسماءُ
// مُضيفٍ صالِحَةٌ تُقبَلُ في بابِ الإنشاءِ وتُحَلُّ مُضيفاتُها ودَورُها كَما اليَوم، و`r` خارِجَ المَحجوزِ الَّذي يَقرَؤُه حَلُّ المَسارِ وخارِجَ ما يُخدَمُ مِن الجَذر.
// والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class StoreNamedRCharacterizationTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly HostView Apexed = new(Apex, null, "https", "");

    [Theory]
    [InlineData("a")]
    [InlineData("ar")]
    [InlineData("ra")]
    [InlineData("rr")]
    [InlineData("r1")]
    [InlineData("r-a")]
    public void Neighbours_of_r_stay_valid_host_names_and_creatable(string slug)
    {
        output.WriteLine($"«{slug}»: {string.Join(" · ", StoreHostName.Validate(slug).Select(v => v.Code))} · SlugRefusal={TenantFromAnalysisFactory.SlugRefusal(slug) ?? "—"}");
        Assert.True(StoreHostName.IsValid(slug));
        Assert.Null(TenantFromAnalysisFactory.SlugRefusal(slug));
        Assert.Equal(slug, TenantResolverMiddleware.StoreSlugFromHost($"{slug}.{Apex}", Apex));
        Assert.Equal($"https://{slug}.{Apex}/", StoreUrl.Open(Apexed, slug));
    }

    [Theory]
    [InlineData("ar.example.test", "GET", "/r/vendor/me", "/ar/r/vendor/me")]
    [InlineData("ar.example.test", "GET", "/explore", "/ar/explore")]
    [InlineData("ar-vendor.example.test", "GET", "/me/listings", "/ar/r/vendor/me/listings")]
    [InlineData("ar-vendor.example.test", "POST", "/auth/logout", "/ar/auth/logout")]
    [InlineData("rr.example.test", "GET", "/r/customer/explore", "/rr/r/customer/explore")]
    public void Hosts_of_the_neighbours_resolve_as_today(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Apex, method, path, "", "https", RoleHostPages.Templates);
        output.WriteLine($"{host} {method} {path} ⇒ {a.Verdict} {a.Path}");
        Assert.Equal(StoreHostVerdict.Rewrite, a.Verdict);
        Assert.Equal(expected, a.Path);
    }

    /// <summary>مَسارُ مَتجَرٍ اسمُه <c>r</c> — الحَلُّ مِن المَسار، والرَوابِطُ بِشَكلِ المَسارِ بِلا نِطاقٍ ومِن الجَذر.</summary>
    [Fact]
    public void A_store_named_r_keeps_its_path()
    {
        Assert.Equal("r", TenantResolverMiddleware.SlugFromPath("/r"));
        Assert.Equal("r", TenantResolverMiddleware.SlugFromPath("/r/explore"));
        Assert.Equal("r", TenantResolverMiddleware.SlugFromPath("/r/r/vendor/me/listings"));
        Assert.False(ReservedTenantSlugs.Contains("r"));
        Assert.False(ReservedTenantSlugs.ServedAtRoot("r"));

        foreach (var view in new[] { default(HostView), Apexed })
        {
            Assert.Equal("/r/r/vendor/me/listings", StoreUrl.For(view, "r", "vendor", "me/listings"));
            Assert.Equal("/r/r/vendor/login", StoreUrl.Link(view, "r", null, "r/vendor/login"));
            Assert.Equal("/r/r/customer/explore", StoreUrl.App(view, "r", "customer", "explore"));
            Assert.Equal("/r/explore", StoreUrl.For(view, "r", null, "explore"));
            Assert.Equal("/r", StoreUrl.For(view, "r", null, ""));
        }
        Assert.Equal("/r", StoreUrl.Open(default, "r"));
        output.WriteLine("مَتجَرُ r: الحَلُّ مِن المَسارِ والرَوابِطُ بِشَكلِه — 5 × 2 + 6");
    }
}
