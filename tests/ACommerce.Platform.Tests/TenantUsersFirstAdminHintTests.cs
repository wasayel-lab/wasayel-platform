using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَلميحُ «أَوَّلِ مُشرِف» لا يُرسَمُ إلّا حينَ لا مُستَخدِمَ في المَتجَر (‏brief-P0-8) ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-3 — `brief-P0-8` (صُغرى)**: `admin.tenant_users.empty_hint` («المُستَخدِمُ الأَوَّلُ يَظهَرُ هُنا
// بَعدَ أَن يُسَجِّلَ في واجِهَةِ المَتجَرِ نَفسِها عَبرَ /{0}/login — سَجِّل أَنتَ أَوَّلاً هُناك، ثُمَّ عُد … واضغَط «مَنح إدارَة»…») يُرسَمُ داخِلَ
// `@if (users.Count == 0)` وَحدَه. فَمَتى سَبَقَ المالِكَ إلى مَتجَرِه غَيرُه رَأى صُفوفَهم بِلا سَطرٍ يَقولُ كَيفَ يَصيرُ لَه صَفّ — وهو مَوضِعُ
// التَعَثُّرِ (ب) في `RELEASE-READINESS.md` §٢·١ بِحَرفِه.
//
// **المَقيسُ حَيّاً (‏2026-10-25، الرَأسُ `f99eacd`، قاعِدَةٌ مَحَلِّيَّة `wasayel_w83a`، المُشرِفُ بِرَمزِ المُحاكي)**: مَتجَرٌ أَنشَأَه المُشرِفُ
// (`hint-probe`) بِدَورَي `tenant_admin` و`customer`، وعُضوٌ واحِدٌ سَجَّلَ مِن `/hint-probe/login` — `/admin/tenants/hint-probe/users` ‏200 بِصَفٍّ
// واحِد وزِرِّ «مَنح إدارَة» واحِد و**صِفرِ** تَلميح؛ و`owner-test` (أَربَعَةُ أَدوارٍ لا يَحمِلُ أَحَدُها `tenant.manage`، ثَلاثَةُ مُستَخدِمين) صِفرُ
// تَلميح؛ و`theme-demo` و`order` (بِلا أَدوار — الوَضعُ المَوروث، كُلُّ مُسَجَّلٍ يَحمِلُ `tenant.manage`) صِفرُ تَلميح.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣)، وصَفُّ `Today_` يُقلَبُ بِاسمِه في كوميتِ الإصلاح.
public class TenantUsersFirstAdminHintTests(ITestOutputHelper output)
{
    private const string HintKey = "\"admin.tenant_users.empty_hint\"";

    private static string Page()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Admin", "TenantUsers.razor");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(path)));
    }

    /// <summary>مَواضِعُ الصَفحَةِ الثَلاثَة: شَرطُ الفَراغ، و<c>else</c> الَّتي تَليه، وبِطاقَةُ الجَدوَلِ داخِلَها.</summary>
    private static (int Empty, int Else, int Table) Anchors(string page)
    {
        var empty = page.IndexOf("@if (users.Count == 0)", StringComparison.Ordinal);
        var elseAt = empty < 0 ? -1 : page.IndexOf("else", empty, StringComparison.Ordinal);
        var table = elseAt < 0 ? -1 : page.IndexOf("<AcCard>", elseAt, StringComparison.Ordinal);
        Assert.True(empty > 0 && elseAt > empty && table > elseAt, $"أَداةٌ عَمياء: الفَراغ {empty} · else {elseAt} · الجَدوَل {table}.");
        return (empty, elseAt, table);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الإصلاح)</b> — كانَ <c>Today_the_first_admin_hint_is_drawn_only_when_the_store_has_zero_users</c>
    /// (‏<c>6b4715c</c>): «مَواضِعُ التَلميح 1 (داخِلَ فَرعِ الفَراغ) · HasTenantManage في الصَفحَة 0». والآن مَوضِعٌ ثانٍ داخِلَ <c>else</c> قَبلَ بِطاقَةِ
    /// الجَدوَل، تَحتَ <c>@if (!anyManager)</c> — والشَرطُ يَكتُبُه سَطرٌ واحِدٌ مِن دالَّةِ الحارِسِ نَفسِها، فَلا تَعريفٌ ثانٍ لِـ«مَن يُديرُ المَتجَر»
    /// يَنجَرِفُ عَن البابِ الَّذي يَفتَحُ لَوحَتَه (القاعِدَة ٢).</summary>
    [Fact]
    public void The_first_admin_hint_is_drawn_above_the_table_while_no_listed_user_holds_tenant_manage()
    {
        var page = Page();
        var (empty, elseAt, table) = Anchors(page);
        var hits = Regex.Matches(page, Regex.Escape(HintKey)).Select(m => m.Index).ToList();
        var guardAt = page.IndexOf("@if (!anyManager)", elseAt, StringComparison.Ordinal);
        var writers = Regex.Matches(page, @"\banyManager\s*=(?!=)").Count;
        output.WriteLine($"مَواضِعُ التَلميح: {hits.Count} ({string.Join(", ", hits)}) · فَرعُ الفَراغ [{empty}..{elseAt}) · الشَرطُ عِندَ {guardAt}" +
                         $" · الجَدوَلُ عِندَ {table} · كُتّابُ anyManager: {writers}");

        Assert.Equal(2, hits.Count);
        Assert.InRange(hits[0], empty, elseAt);
        Assert.InRange(hits[1], elseAt, table);
        Assert.True(guardAt > elseAt && guardAt < hits[1], "التَلميحُ فَوقَ الجَدوَلِ لا يَسأَلُ «أَلا يَحمِلُ أَحَدٌ tenant.manage؟».");
        Assert.Contains("<AcAlert Variant=\"info\" Message=\"@(L.Format(\"admin.tenant_users.empty_hint\", Slug))\" />",
            page[elseAt..table], StringComparison.Ordinal);

        Assert.Equal(1, writers);
        Assert.Contains("anyManager = users.Any(u => Services.TenantAdminGuard.HasTenantManage(tenant, u));", page, StringComparison.Ordinal);
    }

    /// <summary><b>ولا زِرَّ فَوقَ الجَدوَل</b> — نَسَقُ <c>TenantUsersEmptyStateTests.No_button_was_added_before_the_click_count_was_measured</c>
    /// لِلفَرعِ الثاني: ‏ADR-032 §٦‑د «يُبنى فَقَط إن أَثبَتَ قِياسُ §٦‑هـ مُستَهلِكَه»، والزِرُّ الوَحيدُ «مَنح إدارَة» في صَفِّ صاحِبِه.</summary>
    [Fact]
    public void The_hint_above_the_table_adds_no_button()
    {
        var page = Page();
        var (_, elseAt, table) = Anchors(page);
        var above = page[elseAt..table];
        Assert.True(above.Length > 40, $"أَداةٌ عَمياء: {above.Length} مِحرَفاً بَينَ else والجَدوَل.");
        foreach (var door in new[] { "ActionHref", "ActionLabel", "<form", "<a ", "<button" })
            Assert.DoesNotContain(door, above, StringComparison.Ordinal);
    }
    // ─── التَلميحُ يُسَمّي زِرّاً لا يُرسَمُ في مَتجَرٍ بِلا دَورِ tenant_admin (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-3 الجَولَتانِ ١ و٢، صُغرى) ──────────
    //
    // **تَوصيفُ «اليَوم»** (القاعِدَة ٣ — يُقلَبُ بِاسمِه في كوميتِ الإصلاح): التَلميحُ فَوقَ الجَدوَلِ تَحتَ `@if (!anyManager)` وَحدَه، ونَصُّه يَقولُ
    // «واضغَط «مَنح إدارَة» بِجِوارِ هاتِفِك» — وزِرُّ «مَنح إدارَة» داخِلَ `@if (hasAdminRole)`. فَفي مَتجَرٍ بِلا دَورِ `tenant_admin` يَدُلُّ التَلميحُ
    // عَلى زِرٍّ لا يُرسَمُ أَبَداً؛ ولَقطَةُ `admin-tenant-users` (‏owner-test: أَربَعَةُ أَدوارٍ بِلا tenant_admin) تَحمِلُه تَحتَ تَحذيرِ no_admin_role
    // وبِصِفرِ زِرِّ مَنح.

    private static string Baseline() => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "tests", "characterization", "appearance", "i18n-baseline", "admin-tenant-users.html"));

    [Fact]
    public void Today_the_hint_above_the_table_names_a_grant_button_a_store_without_the_admin_role_never_draws()
    {
        var page = Page();
        var (_, elseAt, table) = Anchors(page);
        var hintGuard = page.IndexOf("@if (!anyManager)", elseAt, StringComparison.Ordinal);
        Assert.True(hintGuard > elseAt && hintGuard < table, "لا شَرطَ لِلتَلميحِ فَوقَ الجَدوَل — الأَداةُ عَمياء.");
        Assert.DoesNotContain("hasAdminRole", page[hintGuard..table], StringComparison.Ordinal);

        var buttonGuard = page.IndexOf("@if (hasAdminRole)", table, StringComparison.Ordinal);
        var grant = page.IndexOf("/grant-admin", table, StringComparison.Ordinal);
        Assert.True(buttonGuard > table && grant > buttonGuard, $"زِرُّ المَنحِ خارِجَ @if (hasAdminRole): الشَرط {buttonGuard} · الزِرّ {grant}.");

        Assert.Contains("«مَنح إدارَة»", ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "admin.tenant_users.empty_hint"),
            StringComparison.Ordinal);

        var snap = Baseline();
        Assert.Contains("ac-alert ac-alert-warning", snap, StringComparison.Ordinal);   // no_admin_role
        Assert.Contains("ac-alert ac-alert-info", snap, StringComparison.Ordinal);      // التَلميح
        Assert.DoesNotContain("grant-admin", snap, StringComparison.Ordinal);
        output.WriteLine($"شَرطُ التَلميحِ يَسأَلُ hasAdminRole: لا · زِرُّ المَنحِ تَحتَ hasAdminRole: نَعَم · اللَقطَة: تَلميحٌ 1 · زِرُّ مَنحٍ 0");
    }
}
