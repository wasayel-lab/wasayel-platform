using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ لافِتَةُ التَرقِيَة لا تَعرِضُ تَنازُلاً بِثَمَن ══════════════════
//
// **العِلَّةُ المَقيسَة (‏2026-08-30)**: ‏`UpgradePrompt.razor` كانَت
// تَحمِلُ سُلَّماً **ثابِتاً** — `"spark" => lite`. وكانَ صَحيحاً ما
// دامَتِ الأَسعارُ والحُدودُ تَصعَدُ مَعاً. ثُمَّ رَفَعَ قَرارُ المالِكِ
// المَجّانِيَّةَ إلى سَقفِ `scale` (‏40 · 200 · 40) **ولَم يُمَسَّ
// السِعر**، فَانفَصَلَ الترتيبانِ — وصارَتِ اللافِتَةُ تَقولُ لِمَن
// اِستَنفَدَ ‏40 تَحليلاً:
//
//     «بَلَغتَ حَدَّ تَحاليلِ هذا الشَهر»
//     «‏3 تَحليل / شَهر · 10 تَحسين / شَهر · حَتَّى 3 تَطبيقات»
//     [ تَرقِيَة إلى Lite (‏199 ر.س / شهر) ]
//
// أَي: اِدفَع ‏199 ريالاً لِتَنزِلَ مِن ‏40 تَحليلاً إلى ‏3. ويَبلُغُها
// **أَنشَطُ** المُستَخدِمين — مَن بَلَغَ حَدَّه — بِالنَقرِ مِن
// `/studio` (‏`StudioHome.razor`) ومِن صَفحَةِ أَيِّ دِراسَة
// (‏`StudioStudy.razor`) بِأَيِّ `?upgrade=`.
//
// **ولِماذا لَم يُمسِكها فاحِصُ السُلَّمِ القائِم**: ‏
// `The_ladder_never_goes_down` ضُيِّقَ إلى الدَرَجاتِ المَدفوعَةِ
// وَحدَها بِحُجَّةِ أَنّ «لا أَحَدَ على `lite` لِيَرى نَفسَه أَدنى مِن
// مَجّانِيّ» — وهي حُجَّةٌ صادِقَةٌ عَن **سَطحِ البَيع**، والسَطحُ
// الَّذي اِنكَسَرَ لَيسَ سَطحَ البَيعِ بَل **لافِتَةَ التَرقِيَة**.
// فَقيسَ السَطحُ الخَطَأ.
//
// **والعِلاجُ يَبقى صَحيحاً بَعدَ قَلبِ العَلَم** (وذلك شَرطُ قَبولِه):
// السُلَّمُ يُحسَبُ مِنَ الحُدودِ لا يُكتَبُ ثابِتاً.
public class UpgradeLadderTests
{
    // ─── ١. الخاصِّيَّةُ نَفسُها ────────────────────────────────────

    /// <summary>
    /// <para><b>لا دَرَجَةٌ تُعرَضُ تَرقِيَةً وهي أَضيَقُ في أَيِّ
    /// حَدّ.</b> هذِه هي الخاصِّيَّةُ الَّتي سَقَطَت، وتُقاسُ على
    /// <b>كُلِّ</b> دَرَجَةٍ في الكاتالوجِ لا على واحِدَة.</para>
    /// </summary>
    [Fact]
    public void No_offered_upgrade_is_narrower_than_what_the_user_already_has()
    {
        var checkedCount = 0;
        var lies = new List<string>();

        foreach (var current in TierCatalog.All.Values)
        {
            var next = TierCatalog.NextAbove(current.Tier);
            if (next is null) continue;
            checkedCount++;

            if (next.AnalysesPerMonth < current.AnalysesPerMonth
             || next.RefinesPerMonth  < current.RefinesPerMonth
             || next.StoresMax        < current.StoresMax
             || (current.AllowExport && !next.AllowExport))
            {
                lies.Add(
                    $"{current.Tier} ({current.AnalysesPerMonth}/{current.RefinesPerMonth}/{current.StoresMax}) "
                  + $"→ {next.Tier} ({next.AnalysesPerMonth}/{next.RefinesPerMonth}/{next.StoresMax}) "
                  + $"بِـ{next.MonthlyPriceSar} ر.س");
            }
        }

        // عَدّاد: أَداةٌ تَفحَصُ صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(checkedCount >= 2,
            $"أَداةٌ عَمياء: فُحِصَت {checkedCount} دَعوَةَ تَرقِيَة — والمَقيسُ اثنَتانِ فَأَكثَر.");

        Assert.True(lies.Count == 0,
            "لافِتَةٌ تَعرِضُ تَنازُلاً بِثَمَن:\n  " + string.Join("\n  ", lies));
    }

    /// <summary>
    /// <para><b>ومَن يَملِكُ السَقفَ لا يُعرَضُ عَلَيهِ شَيء.</b> ما
    /// دامَ الرَفعُ قائِماً فَالمَجّانِيَّةُ في مُستَوى
    /// <c>scale</c> — فَلا دَرَجَةَ تَفوقُها، و<c>null</c> هي
    /// الإجابَةُ الصادِقَة: اللافِتَةُ تَقولُ «بَلَغتَ حَدَّك»
    /// <b>بِلا زِرِّ بَيع</b>.</para>
    /// </summary>
    [Fact]
    public void While_the_free_tier_is_raised_nothing_is_offered_to_it()
    {
        if (!TierCatalog.FreeTierTemporarilyRaised) return;
        Assert.Null(TierCatalog.NextAbove("spark"));
    }

    /// <summary>
    /// <para><b>ويَومَ يَعودُ التَسعيرُ يَعودُ السُلَّمُ الأَصليُّ
    /// بِلا تَعديلِ حَرف</b> — يُقاسُ بِتَشغيلِ الدالَّةِ نَفسِها على
    /// الحالَةِ المَحفوظَةِ في <c>FreeTierBeforeRaise</c>، لا
    /// بِالوَعد.</para>
    /// </summary>
    [Fact]
    public void When_the_raise_is_reverted_the_original_rung_returns()
    {
        var before = TierCatalog.FreeTierBeforeRaise;   // 1 / 3 / 1 / بِلا تَصدير
        var lite   = TierCatalog.For("lite");

        Assert.True(TierCatalog.Exceeds(lite, before),
            "‏lite لَم تَعُد تَفوقُ المَجّانِيَّةَ قَبلَ الرَفع — فَالسُلَّمُ "
            + "الأَصليُّ لا يَعود.");

        // وهي **أَرخَصُ** ما يَفوقُها — أَي أَنَّها هي المَعروضَةُ فِعلاً.
        //
        // و`spark` تُستَثنى بِاسمِها كَما تَستَثني `NextAbove` الدَرَجَةَ
        // الحالِيَّة: نَحنُ نُحاكي الحالَةَ بَعدَ الرُجوع، وفيها
        // `spark` **هي** `before` — فَلا تَفوقُ نَفسَها. وبِدونِ هذا
        // الاستِثناءِ يَقيسُ الفَحصُ خَلطاً بَينَ الحالَتَين.
        var cheapestAbove = TierCatalog.All.Values
            .Where(t => !string.Equals(t.Tier, before.Tier, StringComparison.Ordinal))
            .Where(t => TierCatalog.Exceeds(t, before))
            .OrderBy(t => t.MonthlyPriceSar)
            .First();
        Assert.Equal("lite", cheapestAbove.Tier);
    }

    // ─── ٢. الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠) ────

    /// <summary>
    /// <para><b>حَقنُ عَيب — والمَقيسُ هُوَ العَطَبُ الأَصليُّ
    /// حَرفاً.</b> «صِفرُ مُخالَفَة» مِن فاحِصٍ لا يَرى شَيئاً لا
    /// يُمَيَّزُ عَن «صِفرُ مُخالَفَة» مِن فاحِصٍ يَرى كُلَّ شَيء.
    /// فَتُحقَنُ الحالَةُ الَّتي وَقَعَت فِعلاً — مَجّانِيَّةٌ
    /// مَرفوعَةٌ و<c>lite</c> أَضيَقُ مِنها — ويُشتَرَطُ أَن
    /// تُمسَك.</para>
    /// </summary>
    [Fact]
    public void The_ladder_checker_can_actually_go_red()
    {
        var raisedFree = new TierLimits("spark", "Spark", 99,
            AnalysesPerMonth: 40, RefinesPerMonth: 200, StoresMax: 40, AllowExport: true);
        var narrowerPaid = new TierLimits("lite", "Lite", 199,
            AnalysesPerMonth: 3, RefinesPerMonth: 10, StoresMax: 3, AllowExport: true);

        // ‏(أ) العَطَبُ يُمسَك: الأَضيَقُ لا يَفوقُ الأَوسَعَ مَهما غَلا.
        Assert.False(TierCatalog.Exceeds(narrowerPaid, raisedFree),
            "المُصَنِّفُ عَدَّ دَرَجَةً أَضيَقَ «تَرقِيَة» — فَهُوَ يُمَرِّرُ العَطَبَ الأَصليّ.");

        // ‏(ب) ونَظيرَتُها الأَوسَعُ تَمُرّ — فَالفاحِصُ لا يَرفُضُ
        //     كُلَّ شَيءٍ لِيَبدُوَ يَقِظاً.
        var widerPaid = raisedFree with { Tier = "wider", MonthlyPriceSar = 999, StoresMax = 41 };
        Assert.True(TierCatalog.Exceeds(widerPaid, raisedFree),
            "المُصَنِّفُ رَفَضَ دَرَجَةً أَوسَعَ فِعلاً — فَهُوَ يَرفُضُ كُلَّ شَيء.");

        // ‏(ج) والمُساوِيَةُ تَماماً لَيسَت تَرقِيَة — وهذِه بِعَينِها
        //     حالُ `scale` مُقابِلَ المَجّانِيَّةِ المَرفوعَةِ اليَوم.
        Assert.False(TierCatalog.Exceeds(raisedFree with { Tier = "equal" }, raisedFree),
            "المُصَنِّفُ عَدَّ المُساوِيَ تَرقِيَةً — فَيُباعُ ما هُوَ مَملوكٌ أَصلاً.");
    }

    /// <summary>
    /// <para><b>واللافِتَةُ تَقرَأُ الدالَّةَ لا تَكتُبُ سُلَّماً.</b>
    /// كُلُّ ما فَوقَه يَقيسُ <see cref="TierCatalog.NextAbove"/> —
    /// ولَو عادَ <c>UpgradePrompt.razor</c> إلى تَرتيبٍ مَكتوبٍ في
    /// جِسمِه لَبَقِيَ كُلُّ ذلكَ أَخضَرَ والعَطَبُ عائِدٌ. فَالجِسرُ
    /// بَينَ الطَرَفَينِ يُقاسُ هُنا، بِنَفسِ شَكلِ
    /// <c>StudioUpgradeReasonTests</c> حَرفاً (القاعِدَة ٢).</para>
    /// </summary>
    [Fact]
    public void The_banner_reads_the_computed_ladder_and_hardcodes_none()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "UpgradePrompt.razor");
        var razor = File.ReadAllText(path);
        Assert.True(razor.Length > 500, "أَداةٌ عَمياء: `UpgradePrompt.razor` لَم يُقرَأ.");

        Assert.Contains("TierCatalog.NextAbove(CurrentTier)", razor, StringComparison.Ordinal);

        // ولا سُلَّمَ مَكتوباً بِاسمِ دَرَجَةٍ في جِسمِ الحِساب — وهي
        // بِعَينِها الأَسطُرُ الَّتي أَنتَجَت «اِدفَع لِتَنزِل».
        foreach (var hardcoded in new[] { "\"spark\"  =>", "\"spark\" =>", "\"lite\"   =>", "\"lite\" =>" })
        {
            Assert.False(razor.Contains(hardcoded, StringComparison.Ordinal),
                $"سُلَّمٌ مَكتوبٌ ثابِتاً عادَ إلى اللافِتَة: «{hardcoded}» — "
              + "والحُدودُ والأَسعارُ لا تَصعَدانِ مَعاً بَعدَ الرَفع.");
        }
    }

    // ─── ٣. حِراسَةُ التَصدير: حَيَّةٌ لا مَيِّتَة ───────────────────

    /// <summary>
    /// <para><b>ولِماذا لَم تُحذَف <c>AllowExport</c> كَما حُذِفَت
    /// <c>AllowCustomPattern</c> — والفَرقُ مَقيسٌ لا مَذوق.</b></para>
    ///
    /// <para>‏<c>AllowCustomPattern</c> حُذِفَت لِأَنّ المَسارَ الَّذي
    /// تَحرُسُه <b>غَيرُ مَوجودٍ إطلاقاً</b>: لا استِمارَةَ تُرسِلُ
    /// نَمَطاً، ولا تَعديلَ بَعدَ الإنشاء — فَالشَرطُ لا يَصدُقُ في
    /// أَيِّ حالَةٍ يُمكِنُ أَن تَقَع.</para>
    ///
    /// <para>و<c>AllowExport</c> اليَومَ <b>خامِلَةٌ لا مَعدومَة</b>:
    /// المَسارُ قائِمٌ (<c>GET /studio/s/{id}/export.xlsx</c>)، والحَقلُ
    /// يُقرَأُ عِندَه، و<c>FreeTierBeforeRaise.AllowExport = false</c>
    /// — أَي أَنَّ الشَرطَ يَصدُقُ فَورَ قَلبِ العَلَم. فَحَذفُه
    /// اليَومَ يَعني إعادَةَ كِتابَتِه غَداً، وهذا الفَحصُ يُثَبِّتُ
    /// الفَرقَ حَتّى لا يُعادَ الجَدَل.</para>
    /// </summary>
    [Fact]
    public void The_export_gate_is_not_a_dead_guard()
    {
        // الحَقلُ لَه حالَتانِ حَقيقِيَّتانِ في بَياناتِ المُستَودَعِ
        // نَفسِه — إحداهُما مَحفوظَةٌ في `FreeTierBeforeRaise`.
        Assert.False(TierCatalog.FreeTierBeforeRaise.AllowExport,
            "‏AllowExport بِلا حالَةِ مَنعٍ مَحفوظَةٍ صارَ حِراسَةَ مَعدوم — يُحذَف.");

        // وما دامَ الرَفعُ قائِماً فَهُوَ **خامِلٌ**، ويُقالُ ولا يُبتلَع.
        if (TierCatalog.FreeTierTemporarilyRaised)
        {
            Assert.All(TierCatalog.All.Values, t => Assert.True(t.AllowExport,
                $"‏{t.Tier} تَمنَعُ التَصديرَ بَينَما الرَفعُ قائِم — حالَةٌ ثالِثَةٌ غَيرُ مَقصودَة."));
        }
    }
}
