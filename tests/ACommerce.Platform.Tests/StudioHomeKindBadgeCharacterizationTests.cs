using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ شارَةِ نَوعِ المَتجَرِ في لَوحَةِ الاستوديو — قَبلَ تَبديلِ نَصِّها ═══
//
// **المَقيسُ** (موجَزُ الفَجَوات P0-7، ‏2026-10-10؛ ‏ADR-032 §٦‑ج الصَفُّ 3(ج)):
// `StudioHome.razor` تَرسُمُ لِكُلِّ مَتجَرٍ في «تَطبيقاتي» شارَةً بِفَرعٍ واحِد —
// `t.Roles.Count == 0 ? L["studio.home.kind_store"] : L["studio.home.kind_multirole"]`.
// والصِفرُ هُوَ «الوَضعُ الموروث» الَّذي يَمنَحُ كُلَّ صَلاحِيَّةٍ لِكُلِّ مُسَجَّل:
// `RolePermissions.Has` تَرُدُّ `true` قَبلَ أَن تَنظُرَ في الدَورِ أَصلاً
// (‏`RoleCatalog.cs:133`)، و`PermissionFilter.cs:34` يُمَرِّرُ بِالشَرطِ نَفسِه.
// وكانَ القامُوسُ يُسَمّي هذا الوَضعَ «مَتجَر» (‏`ar.json:500`) والمَتجَرَ
// السَليمَ ذا الأَدوارِ «تَطبيق دَور-مُتَعَدِّد» (‏`:501`) — نَصٌّ يَقلِبُ
// المَعنى: المالِكُ الَّذي أَلغى كُلَّ صَناديقِ الأَدوارِ في `roles/save`
// (ولا رَفضَ لِلفارِغِ هُناك، مُثَبَّتاً في `TenantConfigDecisionTests`) يَرى
// «مَتجَر» فَيَطمَئِنّ — وهُوَ بِالضَبطِ المَوضِعُ الَّذي يَضُرّ.
//
// **ما يُثَبِّتُه هذا المِلَفُّ ويَبقى أَخضَرَ بِلا تَعديلِ حَرفٍ بَعدَ تَبديلِ
// النَصّ (القاعِدَة ٣)**: الفَرعُ بِحَرفِه — فَالإصلاحُ نَصٌّ لا كود، وإغلاقُ
// الوَضعِ الموروثِ نَفسِه يَستَوجِبُ ADR مُستَقِلّاً (‏ADR-032 §٦‑د؛ ‏ADR-027 §٥
// البَند 3) لا يُتَّخَذُ هُنا؛ ودَلالَةُ الصِفر؛ ووُجودُ المِفتاحَينِ في المَعجَمِ
// بِلا تَرجُمَةٍ إنجليزِيَّةٍ تُظِلُّهُما؛ وأَنّ لَقطَةَ أَساسِ `studio-home` لا
// تَرسُمُ الشارَةَ أَصلاً فَلا بايتَ فيها يَتَبَدَّلُ بِتَبديلِ النَصّ (القاعِدَة ١٣).
// وأَمّا القيمَتانِ نَفسُهُما فَفي `StudioHomeKindBadgeTests` — مِلَفٌّ مُستَقِلّ،
// لِأَنَّهُما هُما ما يَتَبَدَّل.

public class StudioHomeKindBadgeCharacterizationTests
{
    internal const string ZeroRolesKey = "studio.home.kind_store";
    internal const string SomeRolesKey = "studio.home.kind_multirole";

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    internal static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    internal static string StudioHome() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "StudioHome.razor");

    // ─── الفَرعُ بِحَرفِه — نَصٌّ يُبَدَّل لا كود ──────────────────────────

    /// <summary>
    /// <para><b>الشارَةُ فَرعٌ واحِدٌ عَلى عَدِّ الأَدوار، ويَقرَأُ مِفتاحَين مِن
    /// القامُوس.</b> الصِفرُ يَأخُذُ الأَوَّلَ وما سِواهُ الثاني — وهذا السَطرُ
    /// لا يُمَسُّ في كوميتِ النَصّ: لَو تَبَدَّلَ لَصارَ الإصلاحُ قَراراً
    /// مِعمارِيّاً بِلا ADR.</para>
    /// </summary>
    [Fact]
    public void The_badge_is_one_branch_on_the_role_count_reading_two_dictionary_keys()
    {
        var razor = StudioHome();

        Assert.Contains(
            "@(t.Roles.Count == 0 ? L[\"studio.home.kind_store\"] : L[\"studio.home.kind_multirole\"])",
            razor, StringComparison.Ordinal);

        // ومَوضِعٌ واحِدٌ لِكُلِّ مِفتاح — لا فَرعٌ ثانٍ يَقرَأُه بِشَرطٍ آخَر.
        Assert.Equal(1, Regex.Matches(razor, Regex.Escape(ZeroRolesKey)).Count);
        Assert.Equal(1, Regex.Matches(razor, Regex.Escape(SomeRolesKey)).Count);
    }

    // ─── دَلالَةُ الصِفر — الوَضعُ الموروث ────────────────────────────────

    /// <summary>
    /// <para><b>ما تُسَمّيه الشارَةُ عِندَ الصِفر هُوَ الوَضعُ الموروث</b>:
    /// مَتجَرٌ بِلا أَدوارٍ يَمنَحُ أَيَّ صَلاحِيَّةٍ لِأَيِّ مُسَجَّلٍ — حَتّى
    /// بِلا دَورٍ نَشِطٍ أَصلاً. والمَتجَرُ ذو الأَدوارِ يَرفُضُ ما لَم يَحمِلْه
    /// الدَور. فَتَسمِيَةُ الصِفر «مَتجَر» تَصِفُ العَكسَ تَماماً.</para>
    /// <para>تَوصيفٌ لا استِحسان — <c>LegacyMode_IsUntouched_AndItsConsumerIsNamed</c>
    /// في <c>StudioBuildRoleFloorTests</c> يُسَمّي مُستَهلِكَه المَقصود، ولا
    /// يُغلَقُ الوَضعُ هُنا.</para>
    /// </summary>
    [Fact]
    public void Zero_roles_is_the_legacy_mode_that_grants_everything_to_everyone()
    {
        Assert.True(RolePermissions.Has(Array.Empty<Role>(), activeRole: null, "tenant.manage"));
        Assert.True(RolePermissions.Has(Array.Empty<Role>(), activeRole: "", "listing.create"));

        var customer = new Role { Slug = "customer", CatalogSlug = "customer", Permissions = ["listing.view"] };
        Assert.False(RolePermissions.Has([customer], "customer", "tenant.manage"));
        Assert.False(RolePermissions.Has([customer], activeRole: null, "listing.view"));
    }

    // ─── المِفتاحانِ في المَعجَم، ولا ظِلَّ إنجليزِيّاً فَوقَهُما ─────────────

    /// <summary>
    /// <para><b>المِفتاحانِ في المَعجَمِ العَرَبيِّ بِقيمَةٍ حَقيقِيَّة، ولا
    /// تَرجُمَةٌ إنجليزِيَّةٌ لَهُما</b> — فَما يَراه كُلُّ مالِكٍ هُوَ القيمَةُ
    /// العَرَبِيَّةُ وَحدَها، وتَبديلُ `ar.json` وَحدَه إصلاحٌ كامِلٌ لا نِصفُه.</para>
    /// </summary>
    [Fact]
    public void Both_badge_keys_live_in_the_arabic_lexicon_and_no_english_value_shadows_them()
    {
        var lexicon = LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[] { ZeroRolesKey, SomeRolesKey })
        {
            Assert.Contains(key, lexicon);
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find(LocaleCatalog.Arabic, key)));
            Assert.False(LocaleCatalog.IsPlaceholderKey(LocaleCatalog.Arabic, key), $"«{key}» قيمَةٌ نائِبَة.");
            Assert.Null(LocaleCatalog.Find("en", key));
        }
    }

    // ─── لَقطَةُ الأَساسِ لا تَرسُمُ الشارَة — فَلا بايتَ يَتَبَدَّل ────────────

    /// <summary>
    /// <para><b>لَقطَةُ أَساسِ <c>studio-home</c> مُلتَقَطَةٌ لِمِلَفِّ
    /// <c>studio</c> بِصِفرِ تَطبيق</b> (‏`capture-appearance.sh`: «‏`studio`
    /// صِفرُ تَطبيق — ولِذلك بِالضَبط تَبقى `studio-home` كَما هي»)، فَفَرعُ
    /// «لا تَطبيقات بَعد» هُوَ المَرسوم ولا <c>st-chip</c> واحِدَةٌ في الصَفحَة.
    /// وهذا هُوَ بُرهانُ القاعِدَة ١٣ لِكوميتِ النَصّ: الفَرقُ مَحصورٌ في
    /// سَطرَي القامُوس، ولَقطَةُ الأَساسِ لا تَتَبَدَّلُ بايتاً — لِأَنَّها لا
    /// تَحمِلُ الشارَةَ أَصلاً، لا لِأَنَّ أَحَداً قَدَّرَ ذلكَ بِالعَين.</para>
    /// </summary>
    [Fact]
    public void The_studio_home_baseline_never_renders_the_badge_so_the_text_change_cannot_touch_it()
    {
        var baseline = Source("tests", "characterization", "appearance", "i18n-baseline", "studio-home.html");

        var noApps = LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.home.no_apps");
        Assert.False(string.IsNullOrWhiteSpace(noApps));
        Assert.Contains(noApps!, baseline, StringComparison.Ordinal);

        Assert.DoesNotContain("st-chip", baseline, StringComparison.Ordinal);
    }
}
