namespace ACommerce.Kit.Subscriptions;

/// <summary>
/// <para><b>هَل تُباعُ هذِه الباقَةُ لِمُستَخدِمِ المَتجَر؟</b> — دالّاتٌ
/// نَقِيَّة: لا Marten، ولا وَقت، ولا عَشوائيَّة.</para>
///
/// <para><b>القَرارُ الَّذي كَتَبَ هذا المِلَفّ (‏2026-08-23)، حَرفيّاً مِن
/// المالِك</b>: «لا تَسمَح لِلتاجِر بِاستِلام حَوالات» و«إمّا بَيعٌ بِلا
/// رُسوم أَو تَكامُلُ بَوّابَةِ دَفعٍ خاصَّةٍ بِه لاحِقاً». فَالمَتجَرُ
/// الَّذي لا مُزَوِّدَ دَفعٍ لَه <b>لا يَبيع باقاتٍ بِسِعر إطلاقاً</b> —
/// لا تُعرَض ولا تُقبَل.</para>
///
/// <para><b>وما نَسَخَه هذا القَرار</b>: آلِيَّةُ «طَلَبِ اشتِراكٍ مُعَلَّق
/// ← اعتِماد» المَبنِيَّةُ يَومَ ‏2026-08-22 (‏`ADR-002`)، وفيها تَعليماتُ
/// حَوالَةٍ إلى <b>حِساب التاجِر</b>. وقَد كانَت جَواباً صَحيحاً عَن
/// السُؤال الخَطَأ: السُؤالُ لَم يَكُن «كَيفَ يَقبِضُ التاجِرُ يَدَوِيّاً؟»
/// بَل «أَيَقبِضُ التاجِرُ أَصلاً؟» — والجَوابُ لا. فَحُذِفَت الآلِيَّةُ
/// كامِلَةً بَدَلَ أَن تُترَك مُعَطَّلَةً (القاعِدَة ١).</para>
///
/// <para><b>والمَجّانِيَّةُ تَبقى ذاتِيَّةً كَما كانَت</b> — لا شَيءَ
/// يُمنَح مَجّاناً هُنا إلّا ما هُوَ مَجّانيٌّ بِتَعريفِه.</para>
/// </summary>
public static class PlanPurchasePolicy
{
    // ─── رُموزُ الخَرق — مِن مَعجَمٍ مُغلَق، ثابِتَةٌ لِلاختِبارات
    //     ولِرِسالَةِ الواجِهَة ────────────────────────────────────────

    /// <summary>لا باقَةَ بِهذا المُعَرِّف.</summary>
    public const string PlanNotFound = "plan_not_found";

    /// <summary>باقَةٌ بِسِعرٍ في مَتجَرٍ بِلا مُزَوِّدِ دَفعٍ مَضبوط.</summary>
    public const string PaidUnavailable = "plan_paid_unavailable";

    public static readonly IReadOnlyList<string> Codes =
        new[] { PlanNotFound, PaidUnavailable };

    // ─── القَرار ─────────────────────────────────────────────────────

    /// <summary>باقَةٌ بِلا سِعرٍ تُباعُ دائِماً؛ وبِسِعرٍ لا تُباع إلّا
    /// مَعَ مُزَوِّدِ دَفعٍ مَضبوط.</summary>
    public static bool IsPurchasable(Plan plan, bool paymentProviderConfigured)
        => plan.Price <= 0m || paymentProviderConfigured;

    /// <summary>رَمزُ الخَرق، أَو <c>null</c> إن جازَ الشِراء.</summary>
    public static string? Refuse(Plan? plan, bool paymentProviderConfigured)
        => plan is null ? PlanNotFound
         : IsPurchasable(plan, paymentProviderConfigured) ? null
         : PaidUnavailable;

    /// <summary>
    /// <para>الباقاتُ المَعروضَة. <b>والتَكافُؤُ بِالمَرجِع مَقصود</b>:
    /// قائِمَةٌ لا يُحذَف مِنها شَيءٌ تُرجَع <b>هي نَفسُها</b> لا نُسخَةً
    /// مُتَساوِيَة — فَمَتجَرٌ كُلُّ باقاتِه مَجّانِيَّةٌ لا يَمُرّ
    /// بِفَرزٍ ولا نَسخ، ولا تَتَغَيَّر صَفحَتُه بايتاً.</para>
    /// </summary>
    public static IReadOnlyList<Plan> Visible(
        IReadOnlyList<Plan> plans, bool paymentProviderConfigured)
    {
        if (paymentProviderConfigured) return plans;
        var free = plans.Where(p => p.Price <= 0m).ToList();
        return free.Count == plans.Count ? plans : free;
    }
}
