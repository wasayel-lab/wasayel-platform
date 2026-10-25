using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حِفظُ الأَدوارِ بِلا اختِيارٍ يُدخِلُ المَتجَرَ «الوَضعَ المَوروث» — يُقاسُ ويُقالُ عَلى الفِعل (‏brief-P0-7، ‏ADR-032 §٦‑د) ═══
//
// **تَدقيقُ الإطلاقِ عِندَ `dace7cd`، تَيّارُ W8-3 — `brief-P0-7` (حاجِبَة، قَرارُ مالِك)**: `RolesSaveService.Compose` بِصِفرِ اختِيارٍ تُخرِجُ
// صِفرَ أَدوارٍ ويَحفَظُها `SaveAsync` بِلا شَرط (مُثَبَّتٌ في `TenantConfigDecisionTests.Roles_that_were_not_selected_are_dropped`)، و
// `RolePermissions.Has` تَرُدُّ `true` لِكُلِّ صَلاحِيَّةٍ حينَ لا أَدوار (‏`RoleCatalog.cs:133`، مُثَبَّتٌ في `StudioBuildRoleFloorTests`) —
// فَـ`TenantAdminGuard.HasTenantManage` يُعطي إدارَةَ المَتجَرِ لِكُلِّ مُسَجَّل.
//
// **المَقيسُ حَيّاً (‏2026-10-25، الرَأسُ `cb5f69b`، قاعِدَةٌ مَحَلِّيَّة `wasayel_w83a`، Development)**: عُضوٌ سَجَّلَ نَفسَه في واجِهَةِ `order`
// (يَبذُرُه `PlatformSeed` في كُلِّ بيئَةٍ بِلا أَدوار) بِرَمزِ المُحاكي — لا دَورَ ولا مَنح — فَتَحَ `/admin/tenants/order/users` ‏200 بِجَدوَلِ
// المُستَخدِمينَ وهَواتِفِهِم؛ وكوكيُّه نَفسُه عَلى `owner-test` (أَربَعَةُ أَدوار) يُرَدُّ إلى الدُخول. **ولا عَدَّ لِلحالِ في الإنتاج**: سكربتُ
// `count-listing-owners.cs` يَطبَعُ «— وَضعٌ مَوروث» في سَطرِ كُلِّ مَتجَرٍ ولا يَجمَعُه، ولا يَعُدُّ مَن يَحمِلُ `tenant.manage` بِه؛ وصَفحَتا
// الأَدوارِ (الاستوديو والإدارَة) تَحفَظانِ الفارِغَ بِلا سَطرٍ يَقولُ ما يَفعَلُه.
//
// **والقَرارُ لِلمالِك** (رَفضُ الفارِغِ بِرَمزِ خَرقٍ ثابِت، أَو قَلبُ `RoleCatalog.cs:133` بِـADR — ‏ADR-032 §٦‑د و‏ADR-027 §٥ البَند 3) — لا يُتَّخَذُ
// هُنا. هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ ما يُنَفَّذُ بِلا قَرار: سَطرُ القياسِ الإجماليّ، والتَحذيرُ عَلى الفِعلِ مِن القامُوس.
// وصُفوفُ `Today_` تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.
public class RolesLegacyModeMeasureTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        return File.ReadAllText(path);
    }

    /// <summary>صَفحَتا حِفظِ الأَدوارِ وبابُ كُلٍّ مِنهُما — لا ثالِثَةَ تَكتُبُ <c>roles/save</c> (الماسِحُ أَدناه يَعُدُّ).</summary>
    public static readonly TheoryData<string, string> RolesPages = new()
    {
        { $"{TemplateRoot}/Components/Pages/StudioAppRoles.razor", "/studio/apps/{Slug}/roles/save" },
        { $"{TemplateRoot}/Components/Pages/Admin/TenantRoles.razor", "/admin/tenants/{Slug}/roles/save" },
    };

    /// <summary>استِمارَةُ <c>roles/save</c> في الصَفحَةِ بِلا تَعليقات — مِن <c>&lt;form</c> إلى <c>&lt;/form&gt;</c>ـها.</summary>
    private static string Form(string page, string action)
    {
        var code = WriteEndpointGuardTests.StripMarkupComments(Read(page));
        var at = code.IndexOf($"action=\"@($\"{action}\")\"", StringComparison.Ordinal);
        Assert.True(at > 0, $"{page}: لا استِمارَةَ إلى {action} — القَصُّ أَعمى.");
        var open = code.LastIndexOf("<form", at, StringComparison.Ordinal);
        var close = code.IndexOf("</form>", at, StringComparison.Ordinal);
        Assert.True(open > 0 && close > at, $"{page}: استِمارَةٌ لا تُغلَق.");
        return code[open..(close + "</form>".Length)];
    }

    /// <summary>مَفاتيحُ القامُوسِ الَّتي تَرسُمُها الاستِمارَة، بِتَرتيبِ ظُهورِها.</summary>
    private static List<string> Keys(string form)
        => Regex.Matches(form, @"L(?:\.Markup\(|\[)""(?<k>[a-z0-9_.]+)""").Select(m => m.Groups["k"].Value).ToList();

    /// <summary><b>«اليَوم»</b>: الاستِمارَتانِ تَحفَظانِ أَيَّ اختِيارٍ — والفارِغَ — ومَفاتيحُهُما هذِه بِعَينِها، لا سَطرَ بَينَها يَقولُ إنّ صِفرَ
    /// الأَدوارِ يُعطي كُلَّ مُسَجَّلٍ كُلَّ صَلاحِيَّة. يُقلَبُ بِاسمِه في كوميتِ التَبديل.</summary>
    [Theory]
    [MemberData(nameof(RolesPages))]
    public void Today_the_roles_form_draws_no_line_saying_what_an_empty_save_grants(string page, string action)
    {
        var keys = Keys(Form(page, action));
        output.WriteLine($"{Path.GetFileName(page)}: {keys.Count} مِفتاحاً — {string.Join(" · ", keys)}");

        var expected = page.EndsWith("StudioAppRoles.razor", StringComparison.Ordinal)
            ? new[] { "studio.app_roles.default", "studio.app_roles.save" }
            : new[] { "admin.tenant_roles.signup_fields", "admin.tenant_roles.field_sep", "admin.tenant_roles.default",
                      "admin.tenant_roles.cancel", "admin.tenant_roles.save" };
        Assert.Equal(expected, keys);
    }

    /// <summary>ولا ثالِثَةَ: كُلُّ استِمارَةٍ في المَصادِرِ تُرسِلُ إلى <c>…/roles/save</c> واحِدَةٌ مِن الاثنَتَين (القاعِدَة ١٠: يَعُدُّ ويَفشَلُ عَلى الصِفر).</summary>
    [Fact]
    public void Exactly_two_forms_post_to_a_roles_save_door()
    {
        var found = EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(f.Text), @"action=""@\(\$""(?<a>[^""]*/roles/save)""\)""")
                .Select(m => (File: Path.GetFileName(f.File), Action: m.Groups["a"].Value)))
            .ToList();
        output.WriteLine($"استِمارَاتُ roles/save: {found.Count} — {string.Join(" · ", found.Select(f => $"{f.File}: {f.Action}"))}");
        Assert.Equal(2, found.Count);
    }

    // ─── سكربتُ القياس ─────────────────────────────────────────────────────────────────────────

    private static string ScriptCode()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "count-listing-owners.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        return WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
    }

    /// <summary><b>«اليَوم»</b>: السكربتُ يَطبَعُ «— وَضعٌ مَوروث» في سَطرِ كُلِّ مَتجَرٍ (مَقيسٌ: ثَلاثَةٌ مِن خَمسَةٍ عَلى القاعِدَةِ المَحَلِّيَّة) ولا
    /// يَجمَعُه، ولا يَسأَلُ دالَّةَ الصَلاحِيَّةِ نَفسَها، ولا يَعُدُّ مُستَخدِمي مَتجَرٍ بِلا أَدوار. يُقلَبُ بِاسمِه في كوميتِ التَبديل.</summary>
    [Fact]
    public void Today_the_owner_count_script_prints_no_aggregate_of_the_legacy_role_mode()
    {
        var code = ScriptCode();
        var summary = code[code.IndexOf("فُحِصَ: {tenants.Count} مَتجَراً", StringComparison.Ordinal)..];
        var summaryLines = Regex.Matches(summary[..summary.IndexOf("if (mismatches.Count > 0)", StringComparison.Ordinal)], @"Console\.WriteLine\(").Count + 1;
        var asksPermission = Regex.Matches(code, @"RolePermissions\.Has\(").Count;
        output.WriteLine($"أَسطُرُ الخُلاصَة: {summaryLines} · RolePermissions.Has: {asksPermission} · «وَضعٌ مَوروث» في الخُلاصَة: {summary.Contains("وَضعٌ مَوروث", StringComparison.Ordinal)}");

        Assert.Equal(3, summaryLines);
        Assert.Equal(0, asksPermission);
        Assert.DoesNotContain("وَضعٌ مَوروث", summary, StringComparison.Ordinal);
    }
}
