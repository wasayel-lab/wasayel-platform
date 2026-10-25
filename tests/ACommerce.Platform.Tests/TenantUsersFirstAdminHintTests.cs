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

    [Fact]
    public void Today_the_first_admin_hint_is_drawn_only_when_the_store_has_zero_users()
    {
        var page = Page();
        var (empty, elseAt, table) = Anchors(page);
        var hits = Regex.Matches(page, Regex.Escape(HintKey)).Select(m => m.Index).ToList();
        output.WriteLine($"مَواضِعُ التَلميح: {hits.Count} ({string.Join(", ", hits)}) · فَرعُ الفَراغ [{empty}..{elseAt}) · الجَدوَلُ عِندَ {table}" +
                         $" · HasTenantManage في الصَفحَة: {Regex.Matches(page, "HasTenantManage").Count}");

        Assert.Single(hits);
        Assert.InRange(hits[0], empty, elseAt);
        Assert.DoesNotContain("HasTenantManage", page, StringComparison.Ordinal);
    }
}
