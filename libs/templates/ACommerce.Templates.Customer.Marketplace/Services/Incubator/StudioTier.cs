using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para>حُدود الباقَة لِمُدَّة ٣٠ يَوم. <b>وكُلُّ حَدٍّ مُنتَهٍ — لا
/// <c>int.MaxValue</c> بَعدَ اليَوم.</b></para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: كانَت <c>scale</c>
/// تَحمِل <c>int.MaxValue</c> في الحُدودِ الثَلاثَة، فَشَرطُ البَوّابَةِ
/// <c>u.AnalysesUsed &gt;= l.AnalysesPerMonth</c> <b>لا يَصدُق أَبَداً</b>
/// — أَي بَوّابَةٌ مَكتوبَةٌ لا تُغلَق. وكُلُّ تَحليلٍ نِداءُ نَموذَجِ
/// لُغَةٍ على <b>مِفتاحِ المالِك</b>: فَالحَدُّ اللانِهائيُّ لَيسَ
/// كَرَماً في باقَةٍ بَل <b>فاتورَةً مَفتوحَةً على حِسابِه</b>. وقَد
/// اجتَمَعَ ذلك مَعَ تَرقِيَةٍ ذاتِيَّةٍ بِلا دَفعٍ في
/// <c>/studio/billing/select</c>، فَصارَ أَيُّ زائِرٍ يَملِك سَقفاً
/// لا نِهائِيّاً على مِفتاحٍ لَيسَ لَه.</para>
///
/// <para><b>سُحِبَ <c>AllowCustomPattern</c></b> (كانَ <c>false</c> في
/// spark و lite، و<c>true</c> في growth و scale). كانَ يُعرَض ميزَةً
/// مَدفوعَة في صَفحَة الباقات وفي نافِذَة التَرقِيَة، و<b>لَم يَفحَصه
/// مَوضِع واحِد</b> — سَبع إصاباتٍ كُلُّها تَعريف أَو عَرض.</para>
///
/// <para>ولَم يُفرَض لِأَنّ المَسار الَّذي يَحرُسُه <b>غَير مَوجود</b>:
/// نَمَط التَطبيق تَستَنبِطُه قَواعِد
/// <see cref="PatternMatcher"/> مِن إجابات الاكتِشاف، ويُخَزَّن في
/// <c>IncubatorSession.SuggestedPattern</c>، ويَقرَؤُه
/// <c>/studio/s/{id}/build</c> مُباشَرَةً — واستِمارَة البِناء تُرسِل
/// الاسم والسلاج واللَون والشِعار والمَدينَة **ولا تُرسِل نَمَطاً**.
/// فَلا اختِيار لِلمُستَخدِم ولا تَعديل بَعد الإنشاء. حِراسَة مَعدومٍ
/// شَرطٌ لا يَكذِب أَبَداً — وذلك أَسوَأ مِن غِيابِه، لِأَنَّه يُوهِم
/// أَنّ المَنع قائِم.</para>
///
/// <para>القاعِدَة المُطَبَّقَة: تُباع الميزَة حينَ توجَد. فَحينَ يُبنى
/// اختِيار النَمَط، يَعود الحَقل ويَعود سَطراه في
/// <c>StudioBilling.razor</c> و<c>UpgradePrompt.razor</c> — ومَعَهُما
/// فَحصٌ حَقيقيّ عِندَ البِناء.</para>
/// </summary>
public sealed record TierLimits(
    string Tier, string LabelAr, int MonthlyPriceSar,
    int AnalysesPerMonth, int RefinesPerMonth, int StoresMax,
    bool AllowExport);

public static class TierCatalog
{
    /// <summary>
    /// <para><b>حُدودُ الدَرَجَةِ المَجّانِيَّةِ <u>قَبلَ</u> الرَفعِ
    /// المُؤَقَّت</b> — مَحفوظَةٌ بَياناتٍ لا تَعليقاً، فَالعَودَةُ يَومَ
    /// يَستَقِرُّ التَسعيرُ سَطرٌ واحِدٌ لا أَثَرٌ يُعادُ
    /// اكتِشافُه.</para>
    ///
    /// <para><b>ولِماذا سِجِلٌّ كامِلٌ لا ثَلاثَةُ أَعداد</b>: الرَفعُ
    /// مَسَّ أَربَعَةَ حُقولٍ لا ثَلاثَة (‏<c>AllowExport</c> مِنها)،
    /// وسِجِلٌّ واحِدٌ يَحمِلُها كُلَّها يَمنَعُ أَن يُعادَ ثَلاثَةٌ
    /// ويُنسى رابِع.</para>
    /// </summary>
    public static readonly TierLimits FreeTierBeforeRaise =
        new("spark", "Spark", 99, AnalysesPerMonth: 1, RefinesPerMonth: 3,
            StoresMax: 1, AllowExport: false);

    /// <summary>
    /// <para><b>أَمَرفوعَةٌ الدَرَجَةُ المَجّانِيَّةُ رَفعاً
    /// مُؤَقَّتاً؟</b> — عَلامَةٌ يَقرَؤُها الاختِبار، فَالرَفعُ
    /// حالَةٌ مُعلَنَةٌ لا انجِرافٌ صامِت.</para>
    ///
    /// <para><b>قَرارُ المالِكِ يَومَ ‏2026-08-30</b>: تُرفَعُ الحُدودُ
    /// <b>حَتّى يَستَقِرَّ التَسعير</b> — ولا يُفتَرَضُ سِعرٌ ولا
    /// يُبنى بَيعٌ ذاتيّ. فَهذِه العَلامَةُ تَعودُ <c>false</c> وتَعودُ
    /// مَعَها <see cref="FreeTierBeforeRaise"/> يَومَ يُكتَبُ
    /// السِعر.</para>
    /// </summary>
    public const bool FreeTierTemporarilyRaised = true;

    public static readonly IReadOnlyDictionary<string, TierLimits> All = new Dictionary<string, TierLimits>
    {
        // ═══ رَفعٌ **مُؤَقَّت** — حَتّى يَستَقِرَّ التَسعير ═══════════
        //
        // **الحالَةُ الَّتي رُفِعَت** (‏`FreeTierBeforeRaise`): ‏1 تَحليل
        // · 3 تَحسينات · مَتجَرٌ واحِد · بِلا تَصديرِ دِراسَة. وهي
        // **حالَةُ كُلِّ مُستَأجِرٍ في المُستَودَعِ كُلِّه** لا حالَةَ
        // مَن اختارَ المَجّانِيَّة: `StudioUser.Tier` افتِراضُها
        // `"spark"`، وكاتِبُها الوَحيدُ خَلفَ رَفضٍ لا يُعبَر. أَي
        // أَنّ هذِه الأَرقامَ لَم تَكُن سِياسَةَ باقَةٍ بَل **سَقفَ
        // المُنتَجِ كُلِّه**.
        //
        // **والأَرقامُ الجَديدَةُ مَنقولَةٌ لا مَنحوتَة** (القاعِدَة ١٦):
        // هي بِحَرفِها أَرقامُ `scale` أَدناه — تَكليفُ المالِكِ يَومَ
        // ‏2026-08-30 (‏40 · 200 · 40). فَلا رَقمَ يُخترَعُ هُنا، ويَبقى
        // السَقفُ **مُنتَهِياً** فَتُغلَقُ البَوّابَةُ في وَجهِ حَلقَةٍ
        // آلِيَّةٍ تُنفِقُ مِفتاحَ نَموذَجِ لُغَةِ المالِك — وهي بِعَينِها
        // الكَلفَةُ الَّتي أَغلَقَتها المَوجَةُ السابِقَة.
        //
        // **وما لَم يُمَسّ**: السِعرُ ‏99 كَما هُوَ — الرَفعُ حُدودٌ لا
        // تَسعير، ونَصُّ المالِكِ صَريح: لا يُفتَرَضُ سِعر. وحارِسُ
        // `RefusePrice` بِحَرفِه، فَلا دَرَجَةَ بِسِعرٍ تُمنَحُ ذاتِيّاً.
        //
        // **والعَودَة** سَطران: `FreeTierTemporarilyRaised = false` وهذا
        // السَطرُ يُعادُ مِن `FreeTierBeforeRaise`.
        ["spark"]  = new("spark",  "Spark",   99,  AnalysesPerMonth: 40, RefinesPerMonth: 200,
                         StoresMax: 40, AllowExport: true),
        ["lite"]   = new("lite",   "Lite",    199, AnalysesPerMonth: 3, RefinesPerMonth: 10,
                         StoresMax: 3, AllowExport: true),
        ["growth"] = new("growth", "Growth",  399, AnalysesPerMonth: 10, RefinesPerMonth: 50,
                         StoresMax: 10, AllowExport: true),
        // ‏`scale` — أَرقامٌ **مُنتَهِيَة**، مَصدَرُها تَكليفُ المالِكِ
        // يَومَ ‏2026-08-30 (‏40 تَحليلاً · 200 تَحسيناً · 40 مَتجَراً)
        // لا اجتِهادُ الكود (القاعِدَة ١٦). وهي أَربَعَةُ أَضعافِ
        // `growth` في التَحاليلِ والمَتاجِر — فَالسَقفُ يَبقى بَعيداً
        // عَن مُستَخدِمٍ حَقيقيّ، وقَريباً بِما يَكفي لِيُغلِقَ البابَ
        // في وَجهِ حَلقَةٍ آلِيَّة.
        ["scale"]  = new("scale",  "Scale",   999, AnalysesPerMonth: 40,
                         RefinesPerMonth: 200, StoresMax: 40,
                         AllowExport: true),
    };

    public static TierLimits For(string tier)
        => All.TryGetValue(tier, out var t) ? t : All["spark"];

    /// <summary>
    /// <para><b>أَتَفوقُ <paramref name="candidate"/> على
    /// <paramref name="current"/> فِعلاً؟</b> — لا أَغلى ثَمَناً، بَل
    /// <b>أَوسَعُ حَدّاً</b>: لا تَنقُصُ في أَيِّ حَدٍّ، وتَزيدُ في
    /// واحِدٍ عَلى الأَقَلّ.</para>
    ///
    /// <para><b>ولِماذا دالَّةٌ نَقِيَّةٌ هُنا لا تَرتيبٌ مَكتوبٌ في
    /// الشاشَة</b> (القاعِدَة ٢): التَرتيبُ الثابِتُ
    /// (<c>spark → lite → growth → scale</c>) كانَ صَحيحاً ما دامَتِ
    /// الأَسعارُ والحُدودُ تَصعَدُ مَعاً. ويَومَ رُفِعَتِ
    /// المَجّانِيَّةُ إلى سَقفِ <c>scale</c> انفَصَلَ الأَمران —
    /// فَصارَ التَرتيبُ الثابِتُ يَعرِضُ <b>تَنازُلاً بِثَمَن</b>. دالَّةٌ
    /// تُحسَبُ مِنَ الأَرقامِ نَفسِها تَبقى صَحيحَةً على
    /// <b>طَرَفَي</b> العَلَم، فَلا تَحتاجُ أَن تُذكَرَ يَومَ
    /// يُقلَب.</para>
    /// </summary>
    public static bool Exceeds(TierLimits candidate, TierLimits current)
        => candidate.AnalysesPerMonth >= current.AnalysesPerMonth
        && candidate.RefinesPerMonth  >= current.RefinesPerMonth
        && candidate.StoresMax        >= current.StoresMax
        && (candidate.AllowExport || !current.AllowExport)
        && (candidate.AnalysesPerMonth > current.AnalysesPerMonth
         || candidate.RefinesPerMonth  > current.RefinesPerMonth
         || candidate.StoresMax        > current.StoresMax
         || (candidate.AllowExport && !current.AllowExport));

    /// <summary>
    /// <para><b>الدَرَجَةُ الَّتي تُعرَضُ تَرقِيَةً لِمَن هُوَ على
    /// <paramref name="currentTier"/></b> — أَرخَصُ دَرَجَةٍ
    /// <see cref="Exceeds">تَفوقُه فِعلاً</see>، أَو <c>null</c> إن لَم
    /// توجَد.</para>
    ///
    /// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: كانَت
    /// <c>UpgradePrompt.razor</c> تَحمِلُ سُلَّماً ثابِتاً
    /// (<c>"spark" =&gt; lite</c>). وبَعدَ رَفعِ المَجّانِيَّةِ إلى
    /// ‏40 تَحليلاً · 200 تَحسيناً · 40 مَتجَراً، صارَتِ اللافِتَةُ
    /// تَعرِضُ على مَن بَلَغَ حَدَّه: <b>«تَرقِيَة إلى Lite — ‏199
    /// ر.س / شهر»</b> مُقابِلَ <b>3 تَحاليل</b> وهُوَ يَملِك ‏40 —
    /// أَي أَن يَدفَعَ لِيَنزِل إلى ثُلثِ عُشرِ ما عِندَه. ويَبلُغُها
    /// أَنشَطُ المُستَخدِمينَ بِالنَقر: <c>/studio</c> و صَفحَةُ أَيِّ
    /// دِراسَة، بِأَيِّ <c>?upgrade=</c>.</para>
    ///
    /// <para><b>وحينَ لا توجَد دَرَجَةٌ أَوسَع</b> — وهي حالُ
    /// المَجّانِيَّةِ المَرفوعَةِ اليَوم — تُرَدُّ <c>null</c>،
    /// فَتَقولُ اللافِتَةُ «بَلَغتَ حَدَّك» <b>بِلا زِرِّ بَيع</b>.
    /// وذلك هُوَ الصِدق: لا شَيءَ يُباعُ لِمَن يَملِكُ السَقف.</para>
    /// </summary>
    public static TierLimits? NextAbove(string currentTier)
    {
        var current = For(currentTier);
        return All.Values
            .Where(t => !string.Equals(t.Tier, current.Tier, StringComparison.Ordinal))
            .Where(t => Exceeds(t, current))
            .OrderBy(t => t.MonthlyPriceSar)
            .FirstOrDefault();
    }
}

/// <summary>
/// <para><b>هَل تُمنَح هذِه الدَرَجَةُ ذاتِيّاً؟ — تَفويضٌ إلى
/// <see cref="ACommerce.Kit.Subscriptions.PlanPurchasePolicy"/>، لا
/// قَرارٌ جَديد.</b></para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>:
/// <c>POST /studio/billing/select</c> كانَ يُنادي مُزَوِّدَ الدَفعِ
/// (وجَوابُه في المُحاكي «نَجَحَ» دائِماً) ثُمَّ يَكتُب
/// <c>u.Tier = tier</c> ويَحفَظ. فَأَيُّ مُستَخدِمِ استوديو يَرفَع
/// نَفسَه إلى <c>scale</c> (‏999 ريالاً) <b>بِنَقرَةٍ واحِدَةٍ وبِلا
/// دَفع</b>. وهذا بِحَرفِه العَطَبُ الَّذي وَثَّقَته ‏ADR-002 §١ في
/// <c>POST /{slug}/plans/{planId}/subscribe</c> — «يُحَمِّلُ الباقَةَ
/// <b>ويَتَجاهَل <c>Price</c></b>» — وعولِجَ هُناكَ يَومَ ‏2026-08-23
/// بِـ‏ADR-003. <b>وهذِه النُقطَةُ هي المَوضِعُ الوَحيدُ الَّذي لَم
/// يَبلُغه العِلاج.</b></para>
///
/// <para><b>ولا مَعجَمَ خَرقٍ جَديداً</b> (القاعِدَة ٨): نَفسُ
/// <c>PlanPurchasePolicy.PaidUnavailable</c> الَّذي تَقرَؤُه
/// <c>Plans.razor</c> ويُقاسُ في
/// <c>PlanMoneyPathCharacterizationTests</c>.</para>
/// </summary>
public static class StudioTierPurchase
{
    /// <summary>
    /// <para><b>أَيوجَد مَسارُ دَفعٍ <u>ذاتيٌّ</u> لِدَرَجَةِ
    /// الاستوديو؟ — لا، ويُقالُ لِماذا بِالقياسِ لا بِالرَأي.</b></para>
    ///
    /// <list type="number">
    ///   <item>مَسارُ Paddle/PayPal القائِمُ يَنتَهي إلى
    ///   <c>TenantPlan.ExpiresAt</c> بِسلاجِ مُستَأجِر
    ///   (<c>PayPalBillingService.Apply</c>) — و<c>StudioUser</c>
    ///   وَثيقَةٌ أُخرى في إيجارٍ آخَر <b>بِلا حَقلِ انتِهاءٍ
    ///   إطلاقاً</b>. فَرَبطُها يَعني <b>باعِثَ تَمديدٍ ثانِياً</b>،
    ///   وهُوَ ما تَمنَعُه ‏ADR-009 §٢-ب بِاسمِه.</item>
    ///
    ///   <item>إنشاءُ المُعامَلَةِ عِندَ Paddle مَحروسٌ بِـ
    ///   <c>PlatformAdminGuard</c> بِالتَصميم — لا نُقطَةَ ذاتِيَّةَ
    ///   الخِدمَةِ أَصلاً.</item>
    ///
    ///   <item>و<b>مُدَّةُ اشتِراكِ الدَرَجَة</b> غَيرُ مَوجودَةٍ في
    ///   أَيّ وَثيقَة — و<c>PeriodDays = 30</c> فَترَةُ حِصَّةٍ لا
    ///   فَترَةُ اشتِراك. فَاختِراعُها خَرقٌ لِلقاعِدَة ١٦.</item>
    /// </list>
    ///
    /// <para><b>ولِماذا ثابِتٌ لا مِفتاحُ تَهيئَة</b>: مِفتاحٌ يُقلَبُ
    /// إلى <c>true</c> لا يَفتَح شَيئاً — لا مَسارَ خَلفَه — فَيَصير
    /// <b>زِرّاً يَقول «قَريباً»</b> بِثَوبِ إعداد. يَتَبَدَّل هذا
    /// الثابِتُ يَومَ يوجَد المَسار، ومَعَه يُحمَرُّ اختِبارُه.</para>
    /// </summary>
    public const bool SelfServiceCheckoutExists = false;

    /// <summary>رَمزُ الخَرق، أَو <c>null</c> إن جازَ المَنحُ ذاتِيّاً.
    /// و<c>tier</c> خارِجَ الكاتالوج يُرَدُّ بِـ
    /// <c>PlanPurchasePolicy.PlanNotFound</c>.</summary>
    public static string? Refuse(string? tier)
        => ACommerce.Kit.Subscriptions.PlanPurchasePolicy.RefusePrice(
            tier is not null && TierCatalog.All.TryGetValue(tier, out var t)
                ? t.MonthlyPriceSar
                : (decimal?)null,
            SelfServiceCheckoutExists);

    /// <summary>أَتُرسَمُ لَها نَقرَةُ اختِيار؟ — <b>الشاشَةُ
    /// والنُقطَةُ يَقرَآنِ الدالَّةَ نَفسَها</b>، فَلا تَعرِض الشاشَةُ
    /// ما تَرُدُّه النُقطَة.</summary>
    public static bool IsSelfGrantable(string tier) => Refuse(tier) is null;
}

/// <summary>
/// <para><b>سَبَبُ دَعوَةِ التَرقِيَة — مَعجَمٌ مُغلَقٌ بِتَعريفٍ
/// واحِد.</b> أَربَعَةٌ لا خامِس، وهي بِعَينِها قيَمُ <c>?upgrade=</c>
/// في العُنوان.</para>
///
/// <para><b>ولِماذا صارَت ثَوابِتَ</b> (‏2026-08-30): كانَت مَكتوبَةً
/// <b>حَرفِيّاً في ثَمانِيَةِ مَواضِع</b> — أَربَعٍ تَكتُبُها في
/// العُنوان وثَلاثٍ تُطابِقُها في <c>UpgradePrompt.razor</c>. ومَعجَمٌ
/// مُغلَقٌ بِلا تَعريفٍ واحِدٍ يَنجَرِف: خَطَأُ إملاءٍ في طَرَفٍ يَجعَل
/// الرِسالَةَ <b>تَصمُت</b> — أَي رَفضاً واقِعاً وشاشَةً لا تَقول
/// شَيئاً، وهُوَ بِعَينِه «الرَفضُ المُبتلَع».</para>
///
/// <para><b>وانجِرافٌ وُجِدَ عِندَ العَدّ ويُقالُ ولا يُبتلَع</b>:
/// نُقطَةُ التَصديرِ كانَت تَرُدُّ بِـ<c>?upgrade=refine</c> —
/// فَتَقولُ الشاشَةُ «بَلَغتَ حَدَّ التَحسينات» لِمَن لَم يَبلُغه،
/// و<b>سَطرٌ يَكذِب أَسوَأُ مِن سَطرٍ غائِب</b>. صارَ لَها
/// <see cref="Export"/> بِنَصِّها.</para>
/// </summary>
public static class StudioUpgradeReason
{
    /// <summary>بَلَغَ حَدَّ التَحاليلِ الشَهريّ.</summary>
    public const string Analyses = "analyze";

    /// <summary>بَلَغَ حَدَّ التَحسينات.</summary>
    public const string Refines = "refine";

    /// <summary>بَلَغَ حَدَّ التَطبيقاتِ المَبنِيَّة.</summary>
    public const string Stores = "build";

    /// <summary>ميزَةُ التَصديرِ لَيسَت في باقَتِه — <b>حَجبُ ميزَةٍ
    /// لا خَرقُ حِصَّة</b>، ولِذلك رَمزٌ رابِعٌ لا إعادَةُ استِعمالِ
    /// ثالِث.</summary>
    public const string Export = "export";

    /// <summary>رُموزُ خَرقِ الحِصَّةِ وَحدَها — ما تُصدِرُه
    /// <see cref="StudioTierService.GateCheck"/>.</summary>
    public static readonly IReadOnlyList<string> QuotaCodes =
        new[] { Analyses, Refines, Stores };

    public static readonly IReadOnlyList<string> All =
        new[] { Analyses, Refines, Stores, Export };
}

/// <summary>
/// خِدمَة الـ tier gates — تَفحَص الحُدود قَبل العَمَلِيّات وتَكتُب الـ
/// counters. كُلّ ٣٠ يَوم تُعاد الفَترَة تِلقائيّاً.
/// </summary>
public sealed class StudioTierService
{
    private readonly IDocumentStore _store;
    public StudioTierService(IDocumentStore store) => _store = store;

    public async Task<StudioUser?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        return await qs.LoadAsync<StudioUser>(userId, ct);
    }

    /// <summary>طول فَترَة الحِصَّة بِالأَيّام.</summary>
    public const int PeriodDays = 30;

    /// <summary><b>قاعِدَة انقِضاء الفَترَة، نَقِيَّة</b> — بِلا قاعِدَة
    /// بَيانات ولا ساعَة ضِمنِيَّة، لِتُختَبَر وَحدَها. الشَرط
    /// <c>&gt;=</c> لا <c>&gt;</c>: هو سُلوك اليَوم حَرفاً.</summary>
    public static bool PeriodElapsed(DateTime periodStart, DateTime nowUtc)
        => (nowUtc - periodStart).TotalDays >= PeriodDays;

    /// <summary>
    /// <para><b>يُطَبِّق دَوَران الفَترَة على نُسخَة في الذاكِرَة</b> —
    /// نَفس الحِساب الَّذي كانَ يُكتَب، بِلا كِتابَة. يُعيد
    /// <c>true</c> إن دارَت الفَترَة فِعلاً.</para>
    /// </summary>
    public static bool ApplyPeriodRollover(StudioUser user, DateTime nowUtc)
    {
        if (!PeriodElapsed(user.PeriodStart, nowUtc)) return false;
        user.PeriodStart  = nowUtc;
        user.AnalysesUsed = 0;
        user.RefinesUsed  = 0;
        return true;
    }

    /// <summary>
    /// <para><b>قِراءَة نَقِيَّة</b> — لا تَمَسّ قاعِدَة البَيانات
    /// بِكِتابَة. تُفتَح بِـ<c>QuerySession</c> فَلا تَملِك أَن تَكتُب
    /// أَصلاً (المَنع بُنيَويّ لا اتِّفاقيّ)، ويُطَبَّق دَوَران الفَترَة
    /// على النُسخَة المُعادَة وَحدَها — فَالمَعروض هو <b>الحالَة
    /// الفِعلِيَّة</b> كَما كانَ تَماماً.</para>
    ///
    /// <para><b>ولِماذا انفَصَلَت</b>: كانَت تُسَمّى
    /// <c>LoadWithLimitsAsync</c> وتَحوي <c>Store</c> و
    /// <c>SaveChangesAsync</c>. فَنِداءُ عَرضٍ في
    /// <c>StudioShell.razor</c> — وهو غِلاف كُلّ صَفَحات الاستوديو —
    /// كانَ يَكتُب في قاعِدَة البَيانات <b>عِندَ كُلّ رَسم</b>. اِسمٌ
    /// يَقول «حَمِّل» وفِعلٌ يَكتُب: أَسوَأ ما في العَطَب أَنّ
    /// المُنادي لا يُمكِنُه أَن يَعلَم.</para>
    /// </summary>
    public async Task<(StudioUser User, TierLimits Limits)> ReadWithLimitsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        var user = await qs.LoadAsync<StudioUser>(userId, ct)
                   ?? throw new InvalidOperationException("user not found");
        // نُسخَة مُنفَصِلَة عَن أَيّ تَتَبُّع — التَعديل هُنا عَرضٌ لا حِفظ.
        ApplyPeriodRollover(user, DateTime.UtcNow);
        return (user, TierCatalog.For(user.Tier));
    }

    /// <summary>
    /// <para>نَتيجَةُ البَوّابَة. <b>و<see cref="BreachCode"/> رَمزٌ مِن
    /// <see cref="StudioUpgradeReason"/> لا جُملَةٌ عَرَبِيَّة</b>.</para>
    ///
    /// <para><b>ولِماذا تَبَدَّلَ الحَقل</b> (‏2026-08-30): كانَ
    /// <c>Reason</c> جُملَةً مَكتوبَةً في الكودِ ولَه <b>صِفرُ
    /// مُستَهلِك</b> — المُنادونَ الثَلاثَةُ يَقرَؤونَ <c>Allowed</c>
    /// وَحدَها ثُمَّ يُعيدونَ التَوجيهَ بِنَصٍّ <b>حَرفيٍّ</b>
    /// (<c>?upgrade=analyze</c>) تُطابِقُه <c>UpgradePrompt</c> بِنَصٍّ
    /// حَرفيٍّ آخَر. فَكانَ لَدَينا مَعجَمٌ مُغلَقٌ بِلا تَعريفٍ
    /// واحِد — يَنجَرِف بِخَطَأِ إملاءٍ فَتَصمُت الرِسالَة، وجُملَةٌ
    /// عَرَبِيَّةٌ في C# لا يَراها أَحَد.</para>
    /// </summary>
    public sealed record GateCheck(bool Allowed, int Used, int Limit, string? BreachCode);

    public async Task<GateCheck> CheckAnalyzeAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.AnalysesUsed >= l.AnalysesPerMonth
            ? new(false, u.AnalysesUsed, l.AnalysesPerMonth, StudioUpgradeReason.Analyses)
            : new(true,  u.AnalysesUsed, l.AnalysesPerMonth, null);
    }

    public async Task<GateCheck> CheckRefineAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.RefinesUsed >= l.RefinesPerMonth
            ? new(false, u.RefinesUsed, l.RefinesPerMonth, StudioUpgradeReason.Refines)
            : new(true,  u.RefinesUsed, l.RefinesPerMonth, null);
    }

    public async Task<GateCheck> CheckBuildAsync(Guid uid, CancellationToken ct = default)
    {
        var (u, l) = await ReadWithLimitsAsync(uid, ct);
        return u.StoresBuilt >= l.StoresMax
            ? new(false, u.StoresBuilt, l.StoresMax, StudioUpgradeReason.Stores)
            : new(true,  u.StoresBuilt, l.StoresMax, null);
    }

    public Task RecordAnalysisAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.AnalysesUsed++, ct);

    public Task RecordRefineAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.RefinesUsed++, ct);

    public Task RecordStoreBuiltAsync(Guid uid, CancellationToken ct = default)
        => Bump(uid, u => u.StoresBuilt++, ct);

    /// <summary>
    /// <para><b>الكِتابَة الصَريحَة</b> — ونُقطَة المَعنى الَّتي تَقَع
    /// عِندَها: استِهلاك الحِصَّة فِعلاً. هُنا وَحدَه يُثَبَّت دَوَران
    /// الفَترَة في قاعِدَة البَيانات، لا عِندَ كُلّ رَسم.</para>
    ///
    /// <para>والتَرتيب مَقصود: يَدور أَوَّلاً ثُمَّ يَزيد — وإلّا
    /// زادَ عَدّاداً مِن فَترَةٍ مُنقَضِيَة. والدَوَران والزِيادَة في
    /// <b>حِفظٍ واحِد</b>، فَلا تَقَع إحداهُما دونَ الأُخرى.</para>
    /// </summary>
    // ═══ قياسُ استِهلاكِ نَماذِجِ اللُغَة ═════════════════════════════
    //
    // **ولِماذا هُنا لا في خِدمَةٍ جَديدَة** (القاعِدَة ٨: لا أُنبوبَ
    // رابِع): هذِه هي الخِدمَةُ الَّتي تَعرِفُ إيجارَ الاستوديو
    // وتَملِكُ عَدّاداتِ `StudioUser`، والسُؤالُ الَّذي يُجابُ بِهذا
    // السَطرِ («كَم أَنفَقَ هذا المُستَخدِم؟») هو سُؤالُ العَدّادِ
    // نَفسِه بِوَحدَةٍ أَدَقّ. خِدمَةٌ ثانِيَةٌ تَفتَحُ جَلسَةً ثانِيَةً
    // على نَفسِ الإيجارِ لِنَفسِ السُؤالِ تَجريدٌ بِلا مُستَهلِكٍ
    // يُميِّزُه.

    /// <summary>
    /// <para><b>يَكتُبُ سَطرَ نِداءٍ واحِد — والفَشَلُ في الكِتابَةِ لا
    /// يَكسِرُ المَسار.</b></para>
    ///
    /// <para><b>ولِماذا يُبتلَعُ الاستِثناءُ هُنا وحدَه</b>: القياسُ
    /// <b>مُراقِبٌ لا حارِس</b>. البَوّابَةُ (<see cref="CheckAnalyzeAsync"/>)
    /// تَرفَعُ ما يَقَعُ لِأَنّ عُبورَها بِلا عَدٍّ ثُغرَة؛ وهذا
    /// السَطرُ يَصِفُ نِداءً <b>وَقَعَ فِعلاً</b> — فَتَعَذُّرُ وَصفِه
    /// يَنقُصُ تَقريراً، ورَفعُه يَقتُلُ تَحليلاً اكتَمَل. ومُستَخدِمٌ
    /// يَخسَرُ دِراسَتَه لِأَنّ صَفَّ قياسٍ لَم يُكتَب عَطَبٌ أَسوَأُ
    /// مِنَ العَطَبِ الَّذي جاءَ القياسُ يَكشِفُه.</para>
    ///
    /// <para><b>ولا يَصمُتُ</b>: يُطبَعُ التَحذيرُ بِنَفسِ صيغَةِ
    /// <c>TenantThemeService</c>/<c>TenantProviderService</c> — ابتِلاعٌ
    /// مَسموعٌ لا ابتِلاعٌ صامِت.</para>
    /// </summary>
    public async Task RecordModelCallAsync(
        Metering.ModelCallRecord line, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.LightweightSession(StudioAuth.Tenant);
            s.Store(line);
            await s.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[metering] تَعَذَّرَ تَسجيلُ سَطرِ استِهلاكِ نَموذَجِ لُغَة "
              + $"(‏{line.Provider}/{line.Model}/{line.Operation}): {ex.Message}");
        }
    }

    /// <summary>
    /// <para><b>القِراءَةُ التَجميعِيَّة</b> — نَقِيَّةُ الأَثَر:
    /// <c>QuerySession</c> فَلا تَملِكُ أَن تَكتُبَ أَصلاً (‏نَفسُ
    /// حُجَّةِ <see cref="ReadWithLimitsAsync"/>).</para>
    ///
    /// <para><paramref name="userId"/> <c>null</c> = كُلُّ
    /// المُستَخدِمين — أَي فاتورَةُ المالِكِ الكامِلَةُ مِن
    /// <paramref name="sinceUtc"/>.</para>
    /// </summary>
    public async Task<Metering.ModelCallTotals> ReadModelUsageAsync(
        Guid? userId, DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var qs = _store.QuerySession(StudioAuth.Tenant);
        IQueryable<Metering.ModelCallRecord> q = qs.Query<Metering.ModelCallRecord>()
            .Where(r => r.AtUtc >= sinceUtc);
        if (userId is Guid u) q = q.Where(r => r.UserId == u);
        return Metering.ModelCallTotals.Of(await q.ToListAsync(ct));
    }

    private async Task Bump(Guid uid, Action<StudioUser> mutate, CancellationToken ct)
    {
        await using var s = _store.LightweightSession(StudioAuth.Tenant);
        var u = await s.LoadAsync<StudioUser>(uid, ct);
        if (u is null) return;
        ApplyPeriodRollover(u, DateTime.UtcNow);
        mutate(u);
        s.Store(u);
        await s.SaveChangesAsync(ct);
    }
}
