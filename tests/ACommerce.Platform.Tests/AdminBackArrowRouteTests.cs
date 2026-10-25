using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سَهمُ الرُجوعِ مِن سِتِّ صَفَحاتِ إدارَةٍ يَفتَحُ «تَحتاج تَسجيل دُخول» لِمُديرِ المَتجَرِ نَفسِه (‏lens-dead-doors-6) ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-3 — `lens-dead-doors-6` (صُغرى، أَقدَمُ مِن الفَرع)**: لَوحَةُ المَتجَرِ (`TenantManage.razor`) تَربِطُ
// سِتَّ أَدَواتٍ بِـ`?from=tenant`، وكُلٌّ مِنها يَرسُمُ سَهمَ رُجوعِه إلى `/{Slug}/r/admin/manage`. والمَسارُ يُطابِقُ قالِبَ `/{slug}/r/{role}/manage`
// لَفظاً، لكِنّ «admin» لَيسَ دَوراً في أَيِّ كاتالوج، و`AuthSession.ResolveCookieBase` عَلى مَسارٍ بِدَورٍ يَقرَأُ كوكي ذلِكَ الدَورِ **وَحدَه بِلا سُقوط**
// — `.acommerce.auth.{slug}.admin` لا يَكتُبُه بابٌ — فَالصَفحَةُ تَرى المُديرَ مَجهولاً.
//
// **المَقيسُ حَيّاً (‏2026-10-25، الرَأسُ `0b96ff1`، قاعِدَةٌ مَحَلِّيَّة `wasayel_w83a`)**: عُضوٌ مُنِحَ «tenant_admin» في `hint-probe`، يَفتَحُ كُلَّ أَداةٍ
// بِـ`?from=tenant` (‏200)، والسَهمُ في السِتِّ `/hint-probe/r/admin/manage` ← ‏200 بِـ«تَحتاج تَسجيل دُخول» وصِفرِ رابِطِ أَداة؛ و`/hint-probe/manage`
// بِالجَلسَةِ نَفسِها ← ‏200 لَوحَةُ المَتجَرِ بِرَوابِطِها السِتّ.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣)، وصَفُّ `Today_` يُقلَبُ بِاسمِه في كوميتِ الإصلاح.
public class AdminBackArrowRouteTests(ITestOutputHelper output)
{
    private const string AdminPages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin";

    internal static readonly string[] PanelTools =
        ["TenantAttributes", "TenantBranding", "TenantCategories", "TenantRegions", "TenantRoles", "TenantUsers"];

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path)));
    }

    /// <summary>الأَدَواتُ الَّتي تَربِطُها لَوحَةُ المَتجَرِ بِـ<c>?from=tenant</c> — مِن مَصدَرِ اللَوحَةِ لا مِن قائِمَةٍ مَكتوبَة.</summary>
    private static List<string> LinkedFromPanel()
    {
        var panel = Read("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/TenantManage.razor");
        return Regex.Matches(panel, @"/admin/tenants/\{Slug\}/(?<p>[a-z]+)\?from=tenant").Select(m => m.Groups["p"].Value).ToList();
    }

    [Fact]
    public void Today_six_admin_tools_send_the_store_admin_back_to_a_role_no_store_has()
    {
        var linked = LinkedFromPanel();
        var dead = new List<string>();
        foreach (var page in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, AdminPages.Replace('/', Path.DirectorySeparatorChar)), "*.razor"))
        {
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(page)));
            if (code.Contains("$\"/{Slug}/r/admin/manage\"", StringComparison.Ordinal)) dead.Add(Path.GetFileNameWithoutExtension(page));
        }
        dead.Sort(StringComparer.Ordinal);
        var role = AuthSession.ExtractRoleFromPath(new PathString("/owner-test/r/admin/manage"));
        output.WriteLine($"رَوابِطُ اللَوحَة: {linked.Count} ({string.Join(", ", linked)}) · سِهامٌ إلى /r/admin/manage: {dead.Count} ({string.Join(", ", dead)})" +
                         $" · الدَورُ المُستَخرَج: {role} · في الكاتالوج: {RoleCatalog.Find("admin") is not null}");

        Assert.Equal(6, linked.Count);
        Assert.Equal(PanelTools, dead);
        Assert.Equal("admin", role);
        Assert.Null(RoleCatalog.Find("admin"));
        Assert.NotEqual(AuthSession.CookieName("owner-test"), AuthSession.CookieName("owner-test", role));
    }
}
