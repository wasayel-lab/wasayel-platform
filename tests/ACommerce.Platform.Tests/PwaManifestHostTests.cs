using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَيانُ الـPWA يَتبَعُ المُضيف — بَيانٌ لِكُلِّ مُضيف (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·2) ═══
//
// **المَقيسُ قَبل** (`LivePwaManifestCharacterizationTests` عِندَ 1ec3bcd): المُعَرِّفُ والبِدايَةُ والنِطاقُ ورَوابِطُ المُختَصَراتِ `/{slug}/…` دائِماً، فَعَلى
// `ashare.R` نِطاقُ التَطبيقِ `/ashare/` والصَفَحاتُ النَظيفَةُ خارِجَه. **والآن** جَذرُ التَطبيقِ `PwaManifestDecision.AppBase` — `StoreUrl.For` نَفسُه: عَلى مُضيفِ
// المَتجَرِ `""` (فَالنِطاقُ `/`) و`/r/{role}`، وفي ما سِواه بِحَرفِه. ووَسمُ `<link rel="manifest">` (‏`Resolve`) لا يَتَغَيَّر.

public class PwaManifestHostTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("host", "ashare", null, "", "/")]
    [InlineData("host", "ashare", "vendor", "/r/vendor", "/r/vendor/")]
    [InlineData("host", "ejar", null, "/ejar", "/ejar/")]
    [InlineData("apex", "ashare", null, "/ashare", "/ashare/")]
    [InlineData("apex", "ashare", "vendor", "/ashare/r/vendor", "/ashare/r/vendor/")]
    [InlineData("none", "ashare", "customer", "/ashare/r/customer", "/ashare/r/customer/")]
    public void The_app_base_follows_the_host(string where, string slug, string? role, string appBase, string scope)
    {
        var view = where switch
        {
            "host" => new HostView("example.test", "ashare", "https", ""),
            "apex" => new HostView("example.test", null, "https", ""),
            _ => default,
        };
        var got = PwaManifestDecision.AppBase(view, slug, role);
        output.WriteLine($"{where}: ({slug}, {role ?? "—"}) ⇒ «{got}» · scope {got}/");
        Assert.Equal(appBase, got);
        Assert.Equal(scope, got + "/");
    }

    /// <summary>وَسمُ البَيانِ يُقرَأُ مِن المَسارِ بَعدَ الحَقنِ — فَعَلى المُضيفِ هُوَ نَفسُه كَما عَلى المَسار (مَسارُ API عَلى الأَصلِ نَفسِه).</summary>
    [Theory]
    [InlineData("/ashare/explore", false, "/api/ashare")]
    [InlineData("/ashare/r/vendor/me", true, "/api/ashare/r/vendor")]
    [InlineData("/ashare", true, null)]
    [InlineData("/ashare/login", false, null)]
    public void The_manifest_tag_is_read_from_the_injected_path(string injectedPath, bool hasRoles, string? root)
        => Assert.Equal(root, PwaManifestDecision.Resolve(injectedPath, true, "ashare",
            ACommerce.Templates.Customer.Marketplace.AuthSession.ExtractRoleFromPath(new Microsoft.AspNetCore.Http.PathString(injectedPath)), hasRoles));
}
