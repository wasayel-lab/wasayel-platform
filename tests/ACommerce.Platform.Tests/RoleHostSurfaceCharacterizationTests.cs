using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ تَهذيبِ مُضيفاتِ الأَدوار (‏2026-11-11، W13-4·C2z·5 — مُراجَعَةُ C2b: SEC-C2b-3 وC2b-P-05 وSEC-C2b-R2-4 وC2b-P2-05 وC2b-P3-01
//     وSEC-C2b-4 وC2b-P-03 وC2b-P2-02 وSEC-C2b-R2-5 وSEC-C2b-R3-2 وC2b-P-04 وC2b-P2-03 وC2b-P3-03) ═══
//
// **ما سَيَتَغَيَّر**: مُضيفُ الدَورِ لا يُفَهرَس (robots وخَريطَةٌ فارِغَة وX-Robots-Tag)؛ وحُضورُ الدَورِ لِلمُضيفِ يُقرَأُ طازَجاً لا مِن ذاكِرَةِ الخَمسِ دَقائِق؛
// ودَعوى `tenant_admin` مِن الرابِطِ أَو المُضيفِ لا تَرسُمُ لِغَيرِ المُدير بَطَلَ الإدارَةِ ولا تُكتَبُ كَعكَةَ دَورِه؛ وأَهدافُ «عَودَةِ» المُفَضَّلَةِ مِن باني الرَوابِطِ والتَبويبُ
// النَشِطُ بِالعُنوانِ القانونِيّ؛ ونَصُّ تَصادُمِ الوَكيلِ مِن القامُوس. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣).

public class RoleHostSurfaceCharacterizationTests(ITestOutputHelper output)
{
    private static readonly string?[] UrlRoles = [null, "", "customer", "vendor", "driver", "tenant_admin"];
    private static readonly string?[] Stored = [null, "", "customer", "vendor", "tenant_admin"];

    /// <summary>الدَورُ الَّذي تَرسُمُه الرَئيسِيَّةُ ويَبني مِنه الشَريط: «الرابِطُ وإلّا المُخَزَّن» كَما يُكتَبُ اليَومَ في <c>TenantHome</c> و<c>MainLayout</c> —
    /// وهُوَ <c>EffectiveRole.Resolve</c> بِحَرفِه في كُلِّ حالَةٍ ما عَدا دَعوى الإدارَةِ لِغَيرِ مُديرِها.</summary>
    [Fact]
    public void Every_combination_but_an_unheld_admin_claim_renders_the_role_it_renders_today()
    {
        var checkedCount = 0;
        foreach (var url in UrlRoles)
            foreach (var stored in Stored)
            {
                if (url == "tenant_admin" && stored != "tenant_admin") continue;   // ما سَيَتَغَيَّر
                var today = !string.IsNullOrEmpty(url) ? url : stored;
                var resolved = EffectiveRole.Resolve(null, false, url, stored);
                Assert.Equal(string.IsNullOrEmpty(today) ? null : today, string.IsNullOrEmpty(resolved) ? null : resolved);
                checkedCount++;
            }
        output.WriteLine($"تَركيباتٌ مَفحوصَة: {checkedCount}");
        Assert.Equal(26, checkedCount);
    }

    /// <summary>و«عَودَةُ» المُفَضَّلَةِ في وَضعِ المَسارِ بِحُروفِها: <c>/{slug}</c>، <c>/{slug}/explore</c>، <c>/{slug}/favorites</c>، <c>/{slug}/listings/{id}</c> —
    /// وهِيَ <c>AuthSession.LinkFor(slug, null, …)</c> بِلا طَلَب.</summary>
    [Theory]
    [InlineData("", "/ashare")]
    [InlineData("explore", "/ashare/explore")]
    [InlineData("favorites", "/ashare/favorites")]
    [InlineData("listings/3f2c", "/ashare/listings/3f2c")]
    public void The_favourite_return_targets_are_the_path_form_in_path_mode(string tail, string expected)
    {
        Assert.Equal(expected, StoreUrl.PathForm("ashare", null, tail));
        Assert.Equal(expected, StoreUrl.For(default, "ashare", null, tail));
    }

    /// <summary>والمَسارُ القانونِيُّ في وَضعِ المَسارِ هُوَ المَسارُ نَفسُه — فَمُطابَقَةُ التَبويبِ هُناكَ لا تَتَغَيَّر.</summary>
    [Theory]
    [InlineData("/ashare/explore")]
    [InlineData("/ashare/r/vendor/me")]
    [InlineData("/")]
    public void The_canonical_path_without_a_host_is_the_path(string path)
        => Assert.Equal(path, StoreHostPage.CanonicalPath(path, null));

    /// <summary>وعَلى مُضيفِ المَتجَرِ (لا الدَور) تَبقى الخَريطَةُ والـrobots كَما هُما — مَداخِلُ المَتجَرِ بِعَناوينِه النَظيفَة.</summary>
    [Fact]
    public void The_store_host_robots_and_sitemap_entries_are_what_they_were()
    {
        var robots = SeoDocuments.BuildRobotsTxt("https://ashare.example.test");
        Assert.Contains("Sitemap: https://ashare.example.test/sitemap.xml", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("Disallow: /\n", robots, StringComparison.Ordinal);
        var entries = SeoDocuments.StoreHostEntries(new Tenant { Id = "ashare" }, "https://ashare.example.test").Select(e => e.Loc).ToList();
        output.WriteLine(string.Join(" · ", entries));
        Assert.Contains("https://ashare.example.test/", entries);
    }
}
