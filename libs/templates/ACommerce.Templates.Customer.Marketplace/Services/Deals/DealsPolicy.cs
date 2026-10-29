namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para>سياسَة الـ Deal — تُحَدِّد لِكُلّ نَمَط: المَراحِل المُستَخدَمَة،
/// مَن يَملِك الإجراء في كُلّ مَرحَلَة، والمَرحَلَة التالِيَة.
/// تَحكُم state machine في <see cref="DealsService"/>.</para>
///
/// <para><b>واجِهَة، لا بَيانات.</b> كانَ هذا الصَّنف يَحمِل الأَنماط
/// الخَمسَة في <c>switch</c> مُجَمَّع؛ صارَت بَيانات في
/// <see cref="DealPatternCatalog"/> (META-MODEL §8، TESTING-PROTOCOL §5
/// الخُطوَة 4). التَّواقيع الأَربَعَة لَم تَتَغَيَّر حَرفيّاً، فَلا
/// مُستَهلِك واحِد احتاجَ تَعديلاً، و<see cref="DealsService"/> لَم
/// يُمَسّ. التَّطابُق مُبرهَن بِـ
/// <c>DealsPolicyCharacterizationTests</c>: كُتِبَ عَلى الـ switch
/// واخضَرَّ، ولَم يُمَسّ سَطر مِنه عِندَ الرَّفع.</para>
///
/// <para>حينَ يَنتَقِل الكاتالوج إلى تَخزين لِكُلّ مُستَأجِر، يَتَغَيَّر
/// مَصدَر القِراءَة وَحدَه — هذا السَّطح يَبقى كَما هو.</para>
/// </summary>
public static class DealsPolicy
{
    /// <summary>المَراحِل المُتَتالِيَة لِكُلّ نَمَط.</summary>
    public static IReadOnlyList<DealStage> StagesFor(string pattern)
        => DealPatternCatalog.For(pattern).StageOrder;

    /// <summary>المَرحَلَة التالِيَة في النَّمَط (null لَو وَصَلنا النِهايَة).</summary>
    public static DealStage? Next(string pattern, DealStage current)
        => DealPatternCatalog.For(pattern).Next(current);

    /// <summary>أَدوار اللاعِبين في كُلّ مَرحَلَة — مَن يُحَرِّك الإجراء؟
    /// "initiator" = صاحِب الطَّلَب (مُشتَري/راكِب/مُستَأجِر).
    /// "counterparty" = الطَّرَف الآخَر (بائِع/سائِق/مالِك).
    /// "platform" = المَنصَّة (إجراء آليّ مَثَلاً webhook دَفع).
    /// "either" = أَيّ مِن الطَّرَفَين.
    /// <para><b>ومَعناه «مَن يُؤَدّي المَرحَلَة» — مَن يُدخِلُ الصَفقَةَ إلَيها</b> (‏ADR-033 §٦‑ط،
    /// ‏2026-10-11): <c>Booked</c> يُؤَدّيها البائِعُ بِقَبولِه، و<c>Paid</c> المُشتَري بِدَفعِه. فَالخُروجُ
    /// مِن مَرحَلَةٍ لا يَسأَلُ فاعِلَها بَل فاعِلَ تاليها — <see cref="RequiredActorToLeave(Deal)"/>.</para></summary>
    public static string Actor(DealStage stage)
        => DealPatternCatalog.ActorFor(stage);

    /// <summary>تَسمِيَة المَرحَلَة — تُعرَض في الواجِهات.</summary>
    public static string LabelAr(DealStage s)
        => DealPatternCatalog.LabelFor(s);

    // ═══ طَريقَةُ الدَفعِ تُسقِطُ مَرحَلَةً — قاعِدَةُ كودٍ خَلفَ مِفتاح ═══════════
    //
    // ‏ADR-033 §٦ (‏2026-10-11): صَفقَةُ النَقدِ عِندَ الاستِلامِ لا تَمُرُّ
    // بِـ`Paid` قَبلَ الشَحن — «مَدفوعَة» قَبلَ التَسليمِ وَصفٌ لا يَصدُقُ عَلَيها،
    // والمُشتَري كانَ يَرى «دَورُكَ الآن: تَأكيد: شَحن» وهُوَ لا يَشحَن، والبائِعُ
    // لا يَستَطيعُ نَقلَها إلى الشَحنِ بِنَفسِه (موجَزُ الفَجَوات P1-3).
    // **وما زالَ مِن ذلك — يُقال** (مُراجَعَةُ ‏2026-10-11؛ ‏ADR-033 §٦‑ج مُلحَقاً): شَطرُ
    // البائِعِ زالَ (يَنقُلُها مِن `Confirmed` إلى `Shipping` بِنَفسِه)، أَمّا «دَورُكَ الآن: تَأكيد:
    // شَحن» لِلمُشتَري فَانتَقَلَ مَرحَلَةً ولَم يَزُل: عِندَ `Confirmed` (‏`either`) التالي `Shipping`
    // والمُشتَري مِن فاعِلَيه. يَبقى `either` أَو يَصيرُ `counterparty` وَحدَه لِلنَقد — سُؤالُ المالِكِ
    // «ب» S4·٤؛ وإن صارَ فَبِقاعِدَةِ كودٍ ثانِيَةٍ خَلفَ المِفتاحِ نَفسِه كَـ`Skips`، لا بِصَفٍّ في
    // الكاتالوج. مُثَبَّتٌ بِاسمِه في `DealCashPolicyTests`.
    // **وأُغلِقَ ‏2026-10-11 بِلا قاعِدَةِ نَقدٍ ثانِيَة — ولا يُعادُ حَرفٌ فَوقَه** (‏ADR-033 §٦‑ط): الجُملَةُ
    // كانَت عَرَضَ إزاحَةِ الفاعِل لا سُؤالَ نَقد — الخِدمَةُ تَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة. وصارَ
    // الخُروجُ مِن `Confirmed` يَسأَلُ فاعِلَ تاليها (`RequiredActorToLeave` أَدناه): لِلنَقدِ `Shipping` =
    // `counterparty` فَالبائِعُ وَحدَه يُعلِنُ الشَحن، ولِلبِطاقَةِ `Paid` = `initiator` فَالمُشتَري يَدفَع.
    //
    // **ولِماذا كودٌ هُنا لا صَفٌّ في الكاتالوج** (‏CLAUDE.md القاعِدَتان ٤ و٥):
    // النَمَطُ بَياناتٌ (مَراحِلُ وفاعِلون) ويَبقى كَما هُو — الجَدوَلُ الذَهَبِيُّ في
    // `DealsPolicyCharacterizationTests` لَم يُمَسَّ حَرفاً، و`Next(pattern, stage)`
    // تُجيبُ كَما كانَت. أَمّا «أَيُّ مَرحَلَةٍ تُسقَط» فَحِسابٌ عَلى **حالَةِ وَقتِ
    // التَشغيل** (ما اختارَهُ المُشتَري عِندَ الباب) لا اختِيارٌ مِن مَجموعَةٍ
    // مُنتَهِيَةٍ في التَعريف، فَيَبقى كوداً خَلفَ مِفتاحٍ واحِد:
    // `CheckoutPayMethods.CashOnDelivery`. والدَوالُّ نَقِيَّةٌ تَقرَؤُها الخِدمَةُ
    // والشاشَةُ والـAPI مِن مَوضِعٍ واحِد — فَلا تَعِدُ الواجِهَةُ بِمَرحَلَةٍ
    // سَيَرفُضُها المُحَرِّك (القاعِدَة ١٢).
    //
    // **وما لا يَتَغَيَّر**: صَفقَةٌ بِلا `pay_method` (عَرضٌ عَلى إعلان، أَو بَذرَةُ
    // الاستوديو) أَو بِطاقَةٌ تَمشي الطَريقَ كامِلاً كَما كانَت.

    /// <summary>طَريقَةُ الدَفعِ المَكتوبَةُ عَلى الصَفقَة، أَو <c>null</c> حَيثُ لَم
    /// تُكتَب — <b>ولا يُطَبَّعُ الغِيابُ نَقداً</b> هُنا: الغِيابُ يَعني «الطَريقَ
    /// كامِلاً»، بِخِلافِ <c>CheckoutPaymentPolicy.Normalize</c> الَّتي تُجيبُ سُؤالَ
    /// القَبضِ عِندَ الباب.</summary>
    public static string? PayMethodOf(Deal deal)
        => deal.Attributes.TryGetValue(CheckoutPayMethods.AttributeKey, out var m) ? m : null;

    /// <summary>نَقدٌ عِندَ الاستِلام — بِحَرفِ المَعجَم (Ordinal).</summary>
    public static bool IsCashOnDelivery(string? payMethod)
        => string.Equals(payMethod, CheckoutPayMethods.CashOnDelivery, StringComparison.Ordinal);

    public static bool IsCashOnDelivery(Deal deal) => IsCashOnDelivery(PayMethodOf(deal));

    /// <summary>أَقُبِضَ ثَمَنُ هذِه الصَفقَةِ نَقداً؟ — صَفقَةُ نَقدٍ (<see cref="IsCashOnDelivery(Deal)"/>) بَلَغَت «تَسليم» فَكَتَبَ كاتِبُ الانتِقالِ سَطرَ
    /// <see cref="DealEvent.CashOnDelivery"/> — «لَحظَةُ القَبضِ عِندَ البائِع» (‏ADR-033 §٦). نَظيرُ <see cref="GatewayPayments.CollectedByGateway"/> لِلبِطاقَة؛ يَسأَلُه
    /// الإلغاءُ لِيَكتُبَ <see cref="DealEvent.CashRefundDue"/> (‏2026-10-29، قَرارُ المالِك د38 (٢)؛ مُراجَعَةُ تَيّارِ S5 الأولى). دالَّةٌ نَقِيَّة، وهُنا لا في الخِدمَة:
    /// كاتِبُ السَطرِ واحِدٌ هُناك (`DealCashPolicyTests` يَعُدُّه).</summary>
    public static bool CollectedInCash(Deal deal)
        => IsCashOnDelivery(deal) && deal.Timeline.Any(e => e.Action == DealEvent.CashOnDelivery);

    /// <summary><b>القاعِدَةُ الواحِدَة</b>: <c>Paid</c> تُسقَطُ عَن النَقد — ولا
    /// مَرحَلَةَ أُخرى ولا طَريقَةَ أُخرى.</summary>
    public static bool Skips(DealStage stage, string? payMethod)
        => stage == DealStage.Paid && IsCashOnDelivery(payMethod);

    /// <summary>مَراحِلُ النَمَطِ بَعدَ إسقاطِ ما تُسقِطُه طَريقَةُ الدَفع — لِلـstepper
    /// وشَرحِ التَدَفُّق، فَلا تُرسَمُ خُطوَةٌ لَن تُزار.</summary>
    public static IReadOnlyList<DealStage> StagesFor(string pattern, string? payMethod)
        => StagesFor(pattern).Where(s => !Skips(s, payMethod)).ToArray();

    /// <summary>المَرحَلَةُ التالِيَةُ بِوَعيِ طَريقَةِ الدَفع — تَتَخَطّى المُسقَطَةَ
    /// إلى ما بَعدَها. ومَرحَلَةٌ مُسقَطَةٌ بَلَغَتها صَفقَةٌ قَديمَةٌ قَبلَ هذا
    /// القَرارِ تَمضي مِنها إلى تاليها كَالمُعتاد — لا تُحبَس.</summary>
    public static DealStage? Next(string pattern, DealStage current, string? payMethod)
    {
        var next = Next(pattern, current);
        while (next is { } n && Skips(n, payMethod)) next = Next(pattern, n);
        return next;
    }

    /// <summary>مِن الصَفقَةِ نَفسِها — المَوضِعُ الَّذي تَقرَؤُه الخِدمَةُ والشاشَة.
    /// <para><b>ومَرحَلَةٌ مُسقَطَةٌ تَقِفُ عِندَها الصَفقَةُ تَبقى في القائِمَة</b> (مُراجَعَةُ المَوجَةِ الثانِيَة،
    /// ‏2026-10-11): صَفقَةُ نَقدٍ قَديمَةٌ بَلَغَت <c>Paid</c> قَبلَ الإسقاطِ كانَت تَأخُذُ قائِمَةً بِلا
    /// <c>Paid</c>، فَلا يَجِدُ الـstepper مَرحَلَتَها (<c>IndexOf</c> = ‏‎-1) ولا يُعَلِّمُ شَيئاً — لا مُنجَزاً ولا
    /// حالِيّاً. فَتُبقى المُسقَطَةُ حَيثُ هِيَ مَرحَلَةُ الصَفقَة، ولا تَظهَرُ لِغَيرِها.</para></summary>
    public static IReadOnlyList<DealStage> StagesFor(Deal deal)
    {
        var payMethod = PayMethodOf(deal);
        return StagesFor(deal.Pattern).Where(s => !Skips(s, payMethod) || s == deal.Stage).ToArray();
    }

    /// <summary>مِن الصَفقَةِ نَفسِها — المَوضِعُ الَّذي تَقرَؤُه الخِدمَةُ والشاشَة.</summary>
    public static DealStage? Next(Deal deal) => Next(deal.Pattern, deal.Stage, PayMethodOf(deal));

    // ═══ مَن يُخرِجُ الصَفقَةَ مِن مَرحَلَتِها — فاعِلُ المَرحَلَةِ الَّتي تَدخُلُها ═══════════
    //
    // ‏ADR-033 §٦‑ط (‏2026-10-11). عَمودُ الفاعِلِ في الكاتالوجِ يُجيبُ «مَن يُؤَدّي هذِه المَرحَلَة»:
    // تَعليقاتُ الأَنماطِ الخَمسَةِ في `DealPatternCatalog` تَقرَؤُه كَذلك («بائِع يُؤَكِّد»، «سائِق يَقبَل»،
    // «مالِك يَقبَل»، «عَرض سِعر» — كُلُّها `Booked` بِفاعِلِها `counterparty`)، وشَرحُ التَدَفُّقِ
    // (`FlowExplainer`) يَرسُمُ لِكُلِّ مَرحَلَةٍ «يُحَرِّكها: <فاعِلُها>»، وصَفُّ `Reviewed = either` لا
    // مَعنى لَه إلّا كَذلك (آخِرُ مَرحَلَةٍ في كُلِّ نَمَطٍ تَبلُغُها، فَلا يَخرُجُ مِنها أَحَد).
    //
    // **والإزاحَةُ الَّتي أُصلِحَت**: كانَت `DealsService.AdvanceAsync` والشاشَةُ و`DealTurnView` تَسأَلُ
    // `Actor(deal.Stage)` — فاعِلَ المَرحَلَةِ **الحالِيَّة** — فَيَخرُجُ المُشتَري مِن `Offered` بِنَفسِه
    // إلى «حَجز»، ويُعلِنُ «شَحن» مِن `Paid` عَن البائِع، ولا يَشحَنُ البائِعُ صَفقَةَ بِطاقَة، واستَعارَ
    // `/accept` المُبادِرَ سِنينَ لِيَمُرَّ مِن بَوّابَةِ `Offered`. فَالسُؤالُ صارَ فاعِلَ التالي بِوَعيِ
    // طَريقَةِ الدَفع، مِن دالَّةٍ واحِدَةٍ تَقرَؤُها الخِدمَةُ والقَبولُ والشاشَةُ والـAPI فَلا يَفتَرِقون.
    // **والكاتالوجُ بَياناتٌ لَم تُمَسّ** — الجَدوَلُ الذَهَبِيُّ حَرفاً؛ المَقروءُ مِنه وَحدَه تَغَيَّر.

    /// <summary>الفاعِلُ الَّذي يُخرِجُ الصَفقَةَ مِن <paramref name="stage"/>: فاعِلُ تاليها بِوَعيِ
    /// طَريقَةِ الدَفع — و<c>null</c> عِندَ آخِرِ مَرحَلَة، لا يَخرُجُ مِنها أَحَد.</summary>
    public static string? RequiredActorToLeave(string pattern, DealStage stage, string? payMethod)
        => Next(pattern, stage, payMethod) is { } next ? Actor(next) : null;

    /// <summary>مِن الصَفقَةِ نَفسِها — المَوضِعُ الَّذي تَقرَؤُه الخِدمَةُ والشاشَةُ والـAPI.</summary>
    public static string? RequiredActorToLeave(Deal deal)
        => RequiredActorToLeave(deal.Pattern, deal.Stage, PayMethodOf(deal));
}
