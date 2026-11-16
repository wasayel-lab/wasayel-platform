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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_six_admin_tools_send_the_store_admin_back_to_a_role_no_store_has</c>
    /// (‏<c>3331ceb</c>): «رَوابِطُ اللَوحَة 6 · سِهامٌ إلى /r/admin/manage 6 · الدَورُ المُستَخرَج admin · في الكاتالوج False». والآن كُلُّ أَداةٍ تَربِطُها
    /// اللَوحَةُ بِـ<c>?from=tenant</c> تَبني سَهمَها بِـ<c>AuthSession.LinkFor(Slug, null, "manage")</c> — الباني الواحِدُ الَّذي تَبني بِه الصَفَحاتُ رَوابِطَها —
    /// ولا مَسارَ <c>/r/admin/</c> في المَصادِرِ كُلِّها (الماسِحُ يَعُدُّ ويَفشَلُ عَلى الصِفر).</summary>
    [Fact]
    public void Every_admin_tool_the_panel_links_sends_its_back_arrow_through_the_role_free_link_builder()
    {
        var linked = LinkedFromPanel();
        var built = new List<string>();
        foreach (var tool in PanelTools)
        {
            var code = Read($"{AdminPages}/{tool}.razor");
            if (code.Contains("From == \"tenant\" ? AuthSession.LinkFor(Slug, null, \"manage\")", StringComparison.Ordinal)
                || code.Contains("\"tenant\" => AuthSession.LinkFor(Slug, null, \"manage\"),", StringComparison.Ordinal))
                built.Add(tool);
        }
        var scanned = 0;
        var deadRole = new List<string>();
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            scanned++;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(text));
            if (code.Contains("/r/admin/", StringComparison.Ordinal)) deadRole.Add(Path.GetFileName(file));
        }
        output.WriteLine($"رَوابِطُ اللَوحَة: {linked.Count} · سِهامٌ بِالباني: {built.Count} ({string.Join(", ", built)}) · مَصادِرُ مَمسوحَة: {scanned}" +
                         $" · /r/admin/ فيها: {deadRole.Count} ({string.Join(", ", deadRole)})");

        Assert.Equal(6, linked.Count);
        Assert.Equal(PanelTools, built);
        Assert.True(scanned >= 100, $"أَداةٌ عَمياء: {scanned} مَصدَراً مَمسوحاً.");
        Assert.Empty(deadRole);
    }

    /// <summary><b>وصَفُّ حَلِّ المَسار</b> — ما يَبنيه السَهمُ مَسارٌ مُسَجَّلٌ بِلا دَور: صَفحَةُ اللَوحَةِ تُعلِنُه، وجَدوَلُ المَساراتِ المَمسوحُ يَعرِفُه،
    /// و<c>ExtractRoleFromPath</c> لا يَستَخرِجُ مِنه دَوراً — فَـ<c>ResolveCookieBase</c> يَقرَأُ الكوكي العامَّ ثُمَّ أَيَّ كوكي دَورٍ لِلمَتجَرِ نَفسِه.</summary>
    [Fact]
    public void The_back_arrow_resolves_to_the_registered_role_free_panel_route()
    {
        var href = AuthSession.LinkFor("owner-test", null, "manage");
        var panel = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "TenantManage.razor"));

        Assert.Equal("/owner-test/manage", href);
        Assert.Null(AuthSession.ExtractRoleFromPath(new PathString(href)));
        Assert.Contains("@page \"/{slug}/manage\"", panel, StringComparison.Ordinal);
        Assert.Contains("/{slug}/manage", ComplianceInspectorTests.RepoRoutes);
    }
}
