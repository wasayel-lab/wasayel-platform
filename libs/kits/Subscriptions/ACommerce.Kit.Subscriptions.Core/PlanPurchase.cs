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

    /// <summary>
    /// <para><b>القاعِدَةُ عَلى السِعرِ وَحدَه</b> — بِلا وَثيقَةِ باقَة.
    /// بِلا سِعرٍ تُمنَح دائِماً؛ وبِسِعرٍ لا تُمنَح إلّا مَعَ مُزَوِّدِ
    /// دَفعٍ مَضبوط.</para>
    ///
    /// <para><b>ولِماذا انفَصَلَت عَن <see cref="Plan"/> (‏2026-08-30)</b>:
    /// السُؤالُ نَفسُه حَرفاً — «أَتُمنَح باقَةٌ بِسِعرٍ ذاتِيّاً؟» —
    /// يُطرَح على <b>دَرَجَةِ الاستوديو</b> (<c>TierLimits</c>)، وهي
    /// لَيسَت <c>Plan</c> ولا تَصيرُ واحِدَة. وكانَ الجَوابُ هُناكَ
    /// «نَعَم، خُذها» فَتَسَرَّبَ الإيراد. والبَديلُ عَن هذا التَعميمِ
    /// نَسخُ الشَرطِ في مِلَفٍّ ثانٍ — <b>أُنبوبٌ ثانٍ لِقَرارٍ واحِدٍ
    /// يَنجَرِف</b> (القاعِدَة ٨).</para>
    ///
    /// <para><b>والتَكافُؤُ صِفريّ</b>: الحِملُ القائِمُ على
    /// <see cref="Plan"/> يُفَوِّض إلى هذا حَرفاً، فَلا يَتَغَيَّر جَوابُ
    /// مُنادٍ واحِدٍ بايتاً — و<c>PlanMoneyPathCharacterizationTests</c>
    /// يَبقى أَخضَرَ بِلا تَعديلِ حَرف (القاعِدَة ٣).</para>
    /// </summary>
    public static bool IsPurchasable(decimal price, bool paymentProviderConfigured)
        => price <= 0m || paymentProviderConfigured;

    /// <summary>باقَةٌ بِلا سِعرٍ تُباعُ دائِماً؛ وبِسِعرٍ لا تُباع إلّا
    /// مَعَ مُزَوِّدِ دَفعٍ مَضبوط.</summary>
    public static bool IsPurchasable(Plan plan, bool paymentProviderConfigured)
        => IsPurchasable(plan.Price, paymentProviderConfigured);

    /// <summary>
    /// <para>رَمزُ الخَرق، أَو <c>null</c> إن جازَ المَنح. و<c>null</c>
    /// سِعراً يَعني «لا بَندَ بِهذا المُعَرِّف».</para>
    ///
    /// <para><b>واسمٌ مُختَلِفٌ لا حِملٌ ثانٍ، ويُقالُ لِماذا</b>:
    /// <c>Refuse(null, …)</c> مَكتوبَةٌ في اختِبارَينِ قائِمَين،
    /// وحِملانِ يَقبَلانِ <c>null</c> يَجعَلانِها <b>مُبهَمَةً فَلا
    /// تُبنى</b>. والقاعِدَةُ ٣ تَقول: التَوصيفُ يَخضَرُّ بَعدَ التَبديلِ
    /// <b>بِلا تَعديلِ حَرف</b> — فَالاسمُ هُوَ الَّذي يَتَبَدَّل، لا
    /// الاختِبار.</para>
    /// </summary>
    public static string? RefusePrice(decimal? price, bool paymentProviderConfigured)
        => price is null ? PlanNotFound
         : IsPurchasable(price.Value, paymentProviderConfigured) ? null
         : PaidUnavailable;

    /// <summary>رَمزُ الخَرق، أَو <c>null</c> إن جازَ الشِراء.</summary>
    public static string? Refuse(Plan? plan, bool paymentProviderConfigured)
        => RefusePrice(plan?.Price, paymentProviderConfigured);

    /// <summary>
    /// <para>الباقاتُ المَعروضَة. <b>والتَكافُؤُ بِالمَرجِع مَقصود</b>:
    /// قائِمَةٌ لا يُحذَف مِنها شَيءٌ تُرجَع <b>هي نَفسُها</b> لا نُسخَةً
    /// مُتَساوِيَة — فَمَتجَرٌ كُلُّ باقاتِه مَجّانِيَّةٌ لا يَمُرّ
    /// بِفَرزٍ ولا نَسخ، ولا تَتَغَيَّر صَفحَتُه بايتاً.</para>
    /// </summary>
    // ─── المَسار — ثَلاثَةُ مَخارِجَ لا اثنان ─────────────────────────
    //
    // <b>ولِماذا مَخرَجٌ ثالِث</b>: `Refuse` تَقول «مَمنوع» أَو «مَسموح»،
    // و«مَسموح» في مَسارِ الاشتِراكِ تَعني <b>مَنحاً ذاتِيّاً بِلا
    // قَبض</b>. وذلكَ صَحيحٌ لِلمَجّانِيَّةِ وحدَها. فَيَومَ صارَ
    // لِلمَتجَرِ مُزَوِّدُ دَفع، «مَسموح» لِباقَةٍ بِسِعرٍ يَجِب أَن
    // تَعني <b>«ادفَع عِندَ مُزَوِّدِه»</b> لا «خُذها».
    //
    // <b>وهذا هُوَ بِعَينِه العَطَبُ الَّذي وَثَّقَه ADR-002</b>: النُقطَةُ
    // كانَت تُحَمِّلُ الباقَةَ <b>وتَتَجاهَل `Price`</b> فَتَمنَحُ
    // الحِصَّةَ بِنَقرَة. وفَتحُ الحَقلِ `PaymentProviderConfigured`
    // بِكاتِبٍ حَقيقيٍّ كانَ سَيُعيدُ العَطَبَ مِن بابٍ آخَر لَولا هذا
    // المَخرَج.
    //
    // <b>والتَكافُؤُ الصِفريّ</b>: عِندَ `paymentProviderConfigured ==
    // false` — وهو حالُ كُلّ مَتجَرٍ قَبلَ هذِه المَوجَة — الجَوابُ
    // <b>مُطابِقٌ لِـ`Refuse` حَرفاً</b>، ومَقيسٌ بِاختِبار.
    public enum PlanPurchaseRoute
    {
        /// <summary>تُمنَح ذاتِيّاً — المَجّانِيَّةُ وَحدَها.</summary>
        Grant,

        /// <summary>تُرَدّ بِرَمزٍ مِن المَعجَمِ المُغلَق.</summary>
        Refuse,

        /// <summary>تُدفَع عِندَ مُزَوِّدِ التاجِر — <b>ووَسايِل لَيسَت
        /// في المَسار، بِالبِناءِ لا بِالنِيَّة</b>. ولا اشتِراكَ
        /// يُفتَح هُنا: لا بُرهانَ دَفعٍ في هذا النَوع.</summary>
        PayAtProvider,
    }

    public static (PlanPurchaseRoute Route, string? Refusal) Decide(
        Plan? plan, bool paymentProviderConfigured)
    {
        if (plan is null) return (PlanPurchaseRoute.Refuse, PlanNotFound);
        if (plan.Price <= 0m) return (PlanPurchaseRoute.Grant, null);

        return paymentProviderConfigured
            ? (PlanPurchaseRoute.PayAtProvider, null)
            : (PlanPurchaseRoute.Refuse, PaidUnavailable);
    }

    public static IReadOnlyList<Plan> Visible(
        IReadOnlyList<Plan> plans, bool paymentProviderConfigured)
    {
        if (paymentProviderConfigured) return plans;
        var free = plans.Where(p => p.Price <= 0m).ToList();
        return free.Count == plans.Count ? plans : free;
    }
}
