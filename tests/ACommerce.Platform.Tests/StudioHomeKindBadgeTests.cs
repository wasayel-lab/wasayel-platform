using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ شارَةُ نَوعِ المَتجَرِ كانَت تَقلِبُ المَعنى — والنَصُّ صارَ يَصِفُ الواقِع ═══
//
// **المَقيسُ** (موجَزُ الفَجَوات P0-7، ‏2026-10-10؛ ‏ADR-032 §٦‑ج الصَفُّ 3(ج)):
// `studio.home.kind_store` = «مَتجَر» كانَ يُرسَمُ حينَ `Roles.Count == 0` — وهُوَ
// الوَضعُ الموروثُ الَّذي يَمنَحُ كُلَّ صَلاحِيَّةٍ لِكُلِّ مُسَجَّل (‏`RoleCatalog.cs:133`؛
// `PermissionFilter.cs:34`) — و`studio.home.kind_multirole` = «تَطبيق دَور-مُتَعَدِّد»
// حينَ لِلمَتجَرِ أَدوارٌ، وهُوَ المَتجَرُ السَليم. فَالمالِكُ الَّذي أَلغى كُلَّ صَناديقِ
// الأَدوارِ (ولا رَفضَ لِلفارِغِ في `roles/save`) كانَ يَرى «مَتجَر» ويَطمَئِنّ.
//
// **القَرارُ المُنَفَّذُ تَحتَ التَفويضِ العامّ** (مُسَجَّلٌ في سِجِلِّ أَسئِلَةِ المالِك «أ»):
// نَصٌّ لا كود — الفَرعُ في `StudioHome.razor` بِحَرفِه (مُثَبَّتٌ في
// `StudioHomeKindBadgeCharacterizationTests` قَبلَ هذا الكوميت، وبَصمَتُه لَم تَتَبَدَّل)،
// والمِفتاحانِ بِاسمَيهِما التاريخِيَّين. الصِفرُ يَقولُ الحَقيقَةَ «بِلا أَدوار (وَضعٌ
// مَوروث)»، وما سِواهُ «مَتجَر» — فَمَتجَرٌ كُلُّ إعلاناتِه لِمالِكِه هُوَ المُنتَجُ نَفسُه
// لا «تَطبيقَ دَورٍ مُتَعَدِّد» (‏ADR-032 §٦: لا نَمَطَ ولا حَقلَ ولا باقَةَ لِلمُفرَد).
//
// **ولِماذا يُثَبَّتُ النَصُّ القَديمُ بِحَرفِه هُنا**: الزَوجُ المَقلوبُ لَو عادَ بِتَعديلِ
// حَرفٍ في القامُوس لَما رَآه أَحَد — فَالفَحصُ يَحمَرُّ عَلَيه بِاسمِه، لا عَلى «تَغَيَّرَ
// النَصّ» عُموماً.

public class StudioHomeKindBadgeTests
{
    private const string ZeroRolesKey = StudioHomeKindBadgeCharacterizationTests.ZeroRolesKey;
    private const string SomeRolesKey = StudioHomeKindBadgeCharacterizationTests.SomeRolesKey;

    /// <summary>الزَوجُ المَقلوبُ كَما كانَ في `ar.json:500-501` حَتّى ‏2026-10-11 —
    /// لا يُعادُ تَوليدُه، يُقارَنُ بِه.</summary>
    private const string OldZeroRolesLabel = "مَتجَر";
    private const string OldSomeRolesLabel = "تَطبيق دَور-مُتَعَدِّد";

    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)!;

    // ─── الأَخضَرُ الجَديد ──────────────────────────────────────────────

    /// <summary>
    /// <para><b>الصِفرُ يَقولُ إنَّه بِلا أَدوارٍ ويُسَمّي الوَضعَ بِاسمِه</b> —
    /// بِالعِبارَةِ الَّتي يَستَعمِلُها المُستَودَعُ نَفسُه لِهذِه الحالَة
    /// («الوَضع الموروث» في ‏ADR-027 وADR-032 و`RoleCatalog.cs`)، فَلا تَسمِيَةَ
    /// مُختَرَعَة.</para>
    /// </summary>
    [Fact]
    public void The_zero_roles_badge_says_the_store_has_no_roles_and_names_the_legacy_mode()
    {
        Assert.Equal("بِلا أَدوار (وَضعٌ مَوروث)", Ar(ZeroRolesKey));
    }

    /// <summary><b>والمَتجَرُ ذو الأَدوارِ مَتجَر</b> — لا «تَطبيقَ دَورٍ
    /// مُتَعَدِّد»: التَعَدُّدُ خاصِّيَّةُ المُنتَجِ لا نَوعٌ يُشارُ إلَيه.</summary>
    [Fact]
    public void The_healthy_store_badge_is_simply_a_store()
    {
        Assert.Equal("مَتجَر", Ar(SomeRolesKey));
    }

    // ─── الأَحمَرُ المَحروس — الزَوجُ المَقلوبُ بِحَرفِه ─────────────────

    /// <summary>
    /// <para><b>لَو عادَ الزَوجُ المَقلوبُ لَاحمَرَّ هذا الفَحصُ بِاسمِه.</b>
    /// طَرَفاهُ مُستَقِلّان: عَودَةُ «مَتجَر» إلى الصِفرِ وَحدَها خَرق، وعَودَةُ
    /// «تَطبيق دَور-مُتَعَدِّد» إلى ما سِواهُ وَحدَها خَرق.</para>
    /// </summary>
    [Fact]
    public void The_old_inverted_pair_would_fail_here_if_it_returned()
    {
        Assert.NotEqual(OldZeroRolesLabel, Ar(ZeroRolesKey));
        Assert.NotEqual(OldSomeRolesLabel, Ar(SomeRolesKey));

        // والقَلبُ بِعَينِه: ما كانَ اسمَ الصِفرِ صارَ اسمَ المَتجَرِ السَليم.
        Assert.Equal(OldZeroRolesLabel, Ar(SomeRolesKey));
    }

    /// <summary>والمِفتاحانِ يُقرَآنِ مِن الفَرعِ القَديمِ نَفسِه — فَالنَصُّ الجَديدُ
    /// يَبلُغُ الشاشَةَ مِن المَوضِعِ الَّذي كانَ يَبلُغُها مِنه القَديم، ولا
    /// حَرفِيَّةَ عَرَبِيَّةً أُضيفَت إلى الصَفحَة (القاعِدَة ١١).</summary>
    [Fact]
    public void The_new_labels_reach_the_screen_through_the_same_branch()
    {
        var razor = StudioHomeKindBadgeCharacterizationTests.StudioHome();
        Assert.Contains(
            "@(t.Roles.Count == 0 ? L[\"studio.home.kind_store\"] : L[\"studio.home.kind_multirole\"])",
            razor, StringComparison.Ordinal);
        Assert.DoesNotContain("بِلا أَدوار", razor, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(razor, @"st-chip"">[^@<]*[؀-ۿ]"),
            "الشارَةُ تَحمِلُ حَرفِيَّةً عَرَبِيَّةً بَدَلَ مِفتاحٍ مِن القامُوس.");
    }
}
