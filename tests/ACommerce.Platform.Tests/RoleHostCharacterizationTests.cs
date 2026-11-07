using ACommerce.Kit.Roles;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ مُضيفاتِ الأَدوار (‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ**: (١) `{slug}-{rolehost}.R` يُحَلُّ مُضيفَ دَورٍ لِمَتجَرِه (كانَ 404 في المَرحَلَةِ الأُولى)، ويُحقَنُ دَورُه في مَسارِ
// `GET` قَوالِبِ صَفَحاتِ الدَورِ وَحدَها؛ (٢) دَورُ الرابِطِ أَو المُضيفِ `tenant_admin` لا يَمنَحُ صَلاحِيّاتِه إلّا لِمَن دَورُه المُخَزَّنُ هُوَ — كانَ
// `EffectiveRole.Resolve` يُقَدِّمُ دَورَ الرابِطِ بِلا فَحصِ مِلكِيَّة، فَعُضوٌ عَلى `/{slug}/r/tenant_admin/manage` (أَو `{slug}-admin.R/manage`) يَرى لَوحَةَ
// الإدارَة (G7)؛ (٣) حِفظُ أَدوارٍ يُرَدُّ إن صادَمَ مُضيفُ دَورٍ جَديدٍ مَتجَراً قائِماً. **وما يَنجو ويُثَبَّتُ هُنا**: كُلُّ صَفٍّ سِوى ذلك — مُضيفُ المَتجَرِ ومِلصَقاتٌ
// باطِلَةٌ تَبقى 404 ولَو انتَهَت بِاسمِ دَور، والدَورُ في المَسارِ عَلى مُضيفِ المَتجَر، وتَقديمُ دَورِ الرابِطِ غَيرِ الإداريِّ والـ`as` المَملوك، وتَركيبُ الأَدوارِ
// ورَمزُ الاختِيارِ الفارِغ. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class RoleHostCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("ashare.example.test", "GET", "/r/vendor/me", "Rewrite|ashare|/ashare/r/vendor/me")]
    [InlineData("ashare.example.test", "POST", "/r/vendor/listings/create", "Rewrite|ashare|/ashare/r/vendor/listings/create")]
    [InlineData("ashare.example.test", "GET", "/me/listings", "Rewrite|ashare|/ashare/me/listings")]
    [InlineData("ashare.example.test", "GET", "/", "Rewrite|ashare|/ashare")]
    [InlineData("best-vendor-x.example.test", "GET", "/", "Rewrite|best-vendor-x|/best-vendor-x")]
    [InlineData("vendor.ashare.example.test", "GET", "/", "NotFound||")]
    [InlineData("tenant_admin.example.test", "GET", "/", "NotFound||")]
    [InlineData("shop--vendor.example.test", "GET", "/", "NotFound||")]
    [InlineData("-vendor.example.test", "GET", "/", "NotFound||")]
    [InlineData("vendor.example.test", "GET", "/", "Rewrite|vendor|/vendor")]
    [InlineData("v1-vendor.example.test", "GET", "/", "NotFound||")]
    [InlineData("www-vendor.example.test", "GET", "/", "NotFound||")]
    [InlineData("ashare-sales.example.test", "GET", "/", "Rewrite|ashare-sales|/ashare-sales")]
    [InlineData("www.example.test", "GET", "/", "Pass||")]
    [InlineData("example.test", "GET", "/ashare/r/vendor/me", "Pass||")]
    public void The_answers_that_survive_role_hosts(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, "", "https");
        var shown = $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}";
        output.WriteLine($"{method} {host}{path} ⇒ {shown}");
        Assert.Equal(expected, shown);
    }

    [Theory]
    [InlineData("host", true, null, "customer", "host")]
    [InlineData("host", false, "driver", "customer", "driver")]
    [InlineData("", false, "vendor", "customer", "vendor")]
    [InlineData("", false, null, "customer", "customer")]
    [InlineData("", false, "tenant_admin", "tenant_admin", "tenant_admin")]
    [InlineData("", false, "customer", "tenant_admin", "customer")]
    [InlineData("", false, null, "", null)]
    public void The_effective_role_answers_that_survive(string? formAs, bool owns, string? urlRole, string? stored, string? expected)
        => Assert.Equal(expected, EffectiveRole.Resolve(formAs, owns, urlRole, stored));

    private static readonly IReadOnlyCollection<Role> Roles =
        new[] { "customer", "vendor", "tenant_admin" }.Select((s, i) => RoleCatalog.InstantiateRole(RoleCatalog.Find(s)!, i)).ToList();

    [Theory]
    [InlineData(null, "tenant_admin", "tenant.manage", true)]
    [InlineData(null, "customer", "tenant.manage", false)]
    [InlineData("vendor", "customer", "listing.create", true)]
    [InlineData("customer", "vendor", "listing.create", false)]
    [InlineData("tenant_admin", "tenant_admin", "tenant.manage", true)]
    [InlineData(null, null, "tenant.manage", false)]
    public void The_permission_door_answers_that_survive(string? urlRole, string? stored, string permission, bool opens)
    {
        var got = PermissionDoor.Opens(Roles, urlRole, stored, permission);
        output.WriteLine($"({urlRole ?? "—"}, {stored ?? "—"}, {permission}) ⇒ {got}");
        Assert.Equal(opens, got);
    }

    [Fact]
    public void Composing_roles_answers_as_today()
    {
        var composed = RolesSaveService.Compose([], ["vendor", "customer", "nope"], "customer");
        Assert.Equal(new[] { "customer", "vendor" }, composed.Select(r => r.Slug).ToArray());
        Assert.Equal(new[] { "customer", "vendor" }, composed.Select(r => r.CatalogSlug).ToArray());
        Assert.Equal(new[] { true, false }, composed.Select(r => r.IsDefault).ToArray());
        Assert.Null(RolesSaveService.WhyInvalid(new RolesSaveRequest(["vendor"], null)));
        Assert.Equal(TenantConfigCodes.RolesEmptySelection, RolesSaveService.WhyInvalid(new RolesSaveRequest(["nope"], null)));
    }
}
