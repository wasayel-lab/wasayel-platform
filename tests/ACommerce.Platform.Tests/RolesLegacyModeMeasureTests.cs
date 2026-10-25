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

    /// <summary>مِفتاحُ السَطرِ الَّذي يَقولُ ما يَفعَلُه الحِفظُ الفارِغ — واحِدٌ لِلصَفحَتَين.</summary>
    private const string EmptyWarningKey = "roles.save.empty_warning";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_the_roles_form_draws_no_line_saying_what_an_empty_save_grants</c> (‏<c>c8f9437</c>): مَفاتيحُ الاستِمارَتَينِ 2 و5 بِلا سَطرٍ عَن الفارِغ.
    /// والآن كُلُّ استِمارَةٍ تَرسُمُ <c>roles.save.empty_warning</c> مَرَّةً قَبلَ زِرِّ الحِفظِ مُباشَرَةً (القاعِدَة ١٧: عَلى الفِعلِ لا في صَفحَةٍ تَشرَح)،
    /// وبَقِيَّةُ مَفاتيحِها بِعَينِها وبِتَرتيبِها.</summary>
    [Theory]
    [MemberData(nameof(RolesPages))]
    public void The_roles_form_says_what_an_empty_save_grants_just_before_its_submit(string page, string action)
    {
        var form = Form(page, action);
        var keys = Keys(form);
        output.WriteLine($"{Path.GetFileName(page)}: {keys.Count} مِفتاحاً — {string.Join(" · ", keys)}");

        var expected = page.EndsWith("StudioAppRoles.razor", StringComparison.Ordinal)
            ? new[] { "studio.app_roles.default", EmptyWarningKey, "studio.app_roles.save" }
            : new[] { "admin.tenant_roles.signup_fields", "admin.tenant_roles.field_sep", "admin.tenant_roles.default",
                      EmptyWarningKey, "admin.tenant_roles.cancel", "admin.tenant_roles.save" };
        Assert.Equal(expected, keys);
        var warnAt = form.IndexOf(EmptyWarningKey, StringComparison.Ordinal);
        Assert.True(warnAt > form.LastIndexOf("RoleCatalog.All", StringComparison.Ordinal) && warnAt < form.IndexOf("type=\"submit\"", StringComparison.Ordinal),
            $"{page}: السَطرُ لَيسَ بَينَ قائِمَةِ الأَدوارِ وزِرِّ الحِفظ.");
    }

    /// <summary>نَصُّ السَطرِ في القامُوسِ العَرَبيّ (القاعِدَة ١١)، ويُسَمّي ما يُعطيه الفارِغ: الإدارَةَ وبَياناتِ المُستَخدِمين.</summary>
    [Fact]
    public void The_empty_save_warning_lives_in_the_arabic_dictionary()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json"));
        Assert.True(doc.RootElement.TryGetProperty(EmptyWarningKey, out var v), $"مِفتاحٌ غائِبٌ عَن ar.json: {EmptyWarningKey}");
        var text = v.GetString() ?? "";
        output.WriteLine($"{EmptyWarningKey} = {text}");
        Assert.Contains("إدارَتُه", text, StringComparison.Ordinal);
        Assert.Contains("هَواتِفِ", text, StringComparison.Ordinal);
    }

    /// <summary>ولا ثالِثَةَ: كُلُّ استِمارَةٍ في المَصادِرِ تُرسِلُ إلى <c>…/roles/save</c> واحِدَةٌ مِن الاثنَتَين (القاعِدَة ١٠: يَعُدُّ ويَفشَلُ عَلى الصِفر).
    /// <para><b>‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-3 الجَولَة ١، تَيّارُ W8-10)</b>: الماسِحُ صارَ <see cref="RolesSaveFormsIn"/> — دالَّةً تُحقَنُ
    /// بِعَيِّناتٍ في <see cref="The_roles_save_form_scanner_counts_every_spelling_of_the_action"/>؛ وكانَ يَقبَلُ تَهجِئَةً واحِدَةً
    /// (<c>action="@($"…/roles/save")"</c>) فَاستِمارَةٌ ثالِثَةٌ بِمَسارٍ حَرفِيٍّ تَمُرُّ بِلا عَدّ.</para></summary>
    [Fact]
    public void Exactly_two_forms_post_to_a_roles_save_door()
    {
        var found = RolesSaveFormsIn(EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)));
        output.WriteLine($"استِمارَاتُ roles/save: {found.Count} — {string.Join(" · ", found.Select(f => $"{f.File}: {f.Action}"))}");
        Assert.Equal(2, found.Count);
    }

    /// <summary>كُلُّ <c>action</c> أَو <c>formaction</c> يُرسِلُ إلى <c>…/roles/save</c> — بِمَسارٍ حَرفِيٍّ بِعَلامَتَي تَنصيصٍ مُزدَوِجَتَين أَو
    /// مُفرَدَتَين، أَو بِـ<c>@($"…")</c>، وبِمَسافاتٍ حَولَ <c>=</c> أَو بِلا. ولا يَعُدُّ ما في تَعليقِ <c>@* … *@</c>، ولا بابَ <c>roles/saved</c>
    /// أَو <c>roles/delete</c>. <b>وما لا يَراه — يُقال</b>: مَسارٌ في مُتَغَيِّرٍ (<c>action="@SaveUrl"</c>) — قيمَةُ الوَسمِ لا تَحمِلُ المَسار.</summary>
    internal static List<(string File, string Action)> RolesSaveFormsIn(IEnumerable<(string File, string Text)> sources) =>
        sources.SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripMarkupComments(f.Text),
                    @"(?<![\w-])(?:form)?action\s*=\s*(?:""(?<a>@\(\$?""[^""]*""\))""|""(?<a>[^""]*)""|'(?<a>[^']*)')")
                .Where(m => Regex.IsMatch(m.Groups["a"].Value, @"/roles/save(?![\w-])"))
                .Select(m => (File: Path.GetFileName(f.File), Action: m.Groups["a"].Value)))
            .ToList();

    /// <summary>القاعِدَة ١٠ — الماسِحُ يُحمِرُّ بِعَيِّنَةٍ مَحقونَة: كُلُّ تَهجِئَةٍ تُعَدُّ، وما لَيسَ استِمارَةَ حِفظٍ لا يُعَدّ.</summary>
    [Theory]
    [InlineData("<form method=\"post\" action=\"@($\"/studio/apps/{Slug}/roles/save\")\">", 1)]
    [InlineData("<form method=\"post\" action=\"/admin/tenants/x/roles/save\">", 1)]
    [InlineData("<form method='post' action='/admin/tenants/x/roles/save'>", 1)]
    [InlineData("<form method=\"post\" action = \"/admin/tenants/x/roles/save?back=1\">", 1)]
    [InlineData("<form method=\"post\" action=\"@(\"/x/roles/save\")\">", 1)]
    [InlineData("<button type=\"submit\" formaction=\"/x/roles/save\">", 1)]
    [InlineData("<form action=\"/a/roles/save\"></form><form action=\"@($\"/b/{Id}/roles/save\")\"></form>", 2)]
    [InlineData("@* <form method=\"post\" action=\"/x/roles/save\"> *@", 0)]
    [InlineData("<form method=\"post\" action=\"/x/roles/saved\">", 0)]
    [InlineData("<form method=\"post\" action=\"/x/roles/delete\">", 0)]
    [InlineData("<form method=\"post\" data-action=\"/x/roles/save\">", 0)]
    [InlineData("<form method=\"post\" action=\"@SaveUrl\">", 0)]
    public void The_roles_save_form_scanner_counts_every_spelling_of_the_action(string sample, int expected)
    {
        var found = RolesSaveFormsIn(new[] { ("x/Injected.razor", sample) });
        output.WriteLine($"مَحقونَة: {found.Count} (المُتَوَقَّع {expected}) — {string.Join(" · ", found.Select(f => f.Action))}");
        Assert.Equal(expected, found.Count);
    }

    // ─── سكربتُ القياس ─────────────────────────────────────────────────────────────────────────

    private static string ScriptCode()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "count-listing-owners.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        return WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_the_owner_count_script_prints_no_aggregate_of_the_legacy_role_mode</c> (‏<c>c8f9437</c>): «أَسطُرُ الخُلاصَة: 3 · RolePermissions.Has: 0».
    /// والآن يَسأَلُ لِكُلِّ مَتجَرٍ دالَّةَ الصَلاحِيَّةِ نَفسَها (لا <c>Roles.Count == 0</c> مَنسوخاً — فَقَلبُ <c>RoleCatalog.cs:133</c> يُصَفِّرُه بِلا تَعديل)،
    /// ويَعُدُّ مُستَخدِمي المَتجَرِ المَوروثِ عَدَداً، ويَجمَعُه في سَطرِ خُلاصَةٍ رابِعٍ بِالاستوديو والمالِكِ والمُستَخدِمين.</summary>
    [Fact]
    public void The_owner_count_script_aggregates_the_legacy_role_mode_with_the_products_own_permission_function()
    {
        var code = ScriptCode();
        var summary = code[code.IndexOf("فُحِصَ: {tenants.Count} مَتجَراً", StringComparison.Ordinal)..];
        var summaryLines = Regex.Matches(summary[..summary.IndexOf("if (mismatches.Count > 0)", StringComparison.Ordinal)], @"Console\.WriteLine\(").Count + 1;
        var asksPermission = Regex.Matches(code, Regex.Escape("RolePermissions.Has(t.Roles, activeRole: null, \"tenant.manage\")")).Count;
        output.WriteLine($"أَسطُرُ الخُلاصَة: {summaryLines} · RolePermissions.Has(t.Roles, null, tenant.manage): {asksPermission} · «وَضعٌ مَوروث» في الخُلاصَة: {summary.Contains("وَضعٌ مَوروث", StringComparison.Ordinal)}");

        Assert.Equal(4, summaryLines);
        Assert.Equal(1, asksPermission);
        Assert.DoesNotContain("Roles.Count == 0", code[..code.IndexOf("static bool HasActiveRole", StringComparison.Ordinal)]
            .Replace("t.Roles.Count == 0 ? \"— وَضعٌ مَوروث\"", "", StringComparison.Ordinal), StringComparison.Ordinal);
        foreach (var piece in new[] { "{legacy}", "{legacyStudio}", "{legacyOwned}", "{legacyMembers}" })
            Assert.Contains(piece, summary, StringComparison.Ordinal);
        Assert.Contains("members = await tq.Query<User>().Where(u => u.DeletedAt == null).CountAsync();", code, StringComparison.Ordinal);
    }
}
