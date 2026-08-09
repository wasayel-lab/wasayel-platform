namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>كاتالوج أَنماط الصَّفقات — مَوضِع واحِد</b>. كُلّ ما كانَ
/// مَنثوراً في <c>switch</c> مُجَمَّع صارَ هُنا بَياناً:
/// <see cref="DealPatternDefinition"/> لِكُلّ نَمَط، مَبنيّ مِن جَدوَلَي
/// الفاعِلين والتَّسمِيات.</para>
///
/// <para><b>حَدّ هذه المَوجَة، مُعلَناً</b>: الكاتالوج <b>في الكود</b>
/// (in-code data catalog) لا في Marten. الشَّكل مُصَمَّم لِلتَّخزين
/// لاحِقاً — وَثيقَة لِكُلّ نَمَط تَحتَ مُستَأجِرِها — ولكِنّ التَّخزين
/// <b>لَم يُنَفَّذ</b> في هذه المَوجَة: لا Postgres مَحَلّيّ لِلتَّحَقُّق،
/// وبَوّابَة التَّحَقُّق أَولى مِن بَوّابَة التَّخزين. حينَ يُنَفَّذ،
/// يَتَغَيَّر <see cref="For"/> وَحدَه: قِراءَة الوَثيقَة بَدَل قِراءَة
/// القاموس — والعَقد العامّ (<see cref="DealsPolicy"/>) لا يَتَحَرَّك.</para>
///
/// <para>مَعجَم الأَنماط هُنا هو مَعجَم <b>التَّدَفُّق</b>، لا مَعجَم
/// شَخصيَّة الواجِهَة (<c>AppPattern</c>) — التَّنبيه في META-MODEL §5.</para>
/// </summary>
public static class DealPatternCatalog
{
    /// <summary>مُفرَدات الفاعِلين المُغلَقَة — أَربَعَة لا خامِس لَها
    /// (META-MODEL §4.2). <c>DealsService.IsActorAllowed</c> يَفهَم هذه
    /// حَصراً، وما عَداها يُرفَض.</summary>
    public static readonly IReadOnlyList<string> Actors =
        new[] { "initiator", "counterparty", "either", "platform" };

    /// <summary>الفاعِل حينَ لا تَكون المَرحَلَة مِن أَيّ نَمَط مَعروف —
    /// المَنصَّة (سُلوك الفَرع الافتِراضيّ القَديم، مَحروس بِاختِبار
    /// التَّوصيف).</summary>
    public const string PlatformActor = "platform";

    /// <summary>الفاعِل المُخَوَّل لِكُلّ مَرحَلَة. اليَوم <b>مُشتَرَك</b>
    /// بَينَ كُلّ الأَنماط، لِذلِك يَعيش جَدوَلاً واحِداً؛ ومَع ذلِك
    /// يُنسَخ في كُلّ صَفّ داخِل التَّعريف
    /// (<see cref="DealStageRule.Actor"/>) لِيَبقى التَّعريف مُكتَفِياً
    /// بِذاتِه حينَ يُخَزَّن — ولِيُصبِح اختِلاف الفاعِل بَينَ نَمَطَين
    /// مُمكِناً بِلا تَغيير شَكل.</summary>
    public static readonly IReadOnlyDictionary<DealStage, string> DefaultActors =
        new Dictionary<DealStage, string>
        {
            [DealStage.Offered]   = "initiator",
            [DealStage.Booked]    = "counterparty",
            [DealStage.Confirmed] = "either",
            [DealStage.Paid]      = "initiator",
            [DealStage.Shipping]  = "counterparty",
            [DealStage.Delivered] = "counterparty",
            [DealStage.Received]  = "initiator",
            [DealStage.Reviewed]  = "either",
        };

    /// <summary>تَسمِيَة كُلّ مَرحَلَة — تُعرَض في الواجِهات.</summary>
    public static readonly IReadOnlyDictionary<DealStage, string> DefaultLabelsAr =
        new Dictionary<DealStage, string>
        {
            [DealStage.Offered]   = "عَرض/طَلَب",
            [DealStage.Booked]    = "حَجز",
            [DealStage.Confirmed] = "تَأكيد",
            [DealStage.Paid]      = "دَفع",
            [DealStage.Shipping]  = "شَحن",
            [DealStage.Delivered] = "تَسليم",
            [DealStage.Received]  = "استِلام",
            [DealStage.Reviewed]  = "تَقييم",
        };

    /// <summary>اسم التَّعريف الاحتِياطيّ — ليسَ نَمَطاً مُسَجَّلاً، بَل
    /// ما يَسقُط عَلَيه أَيّ اسم غَير مَعروف.</summary>
    public const string FallbackPattern = "default";

    /// <summary>التَّعريف الاحتِياطيّ: أَقصَر دَورَة كامِلَة.</summary>
    public static readonly DealPatternDefinition Fallback = Define(
        FallbackPattern,
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Reviewed);

    /// <summary>الأَنماط الخَمسَة القِياسيَّة — بَيانات، مَوضِع واحِد.
    /// المُقارَنَة <b>حَسّاسَة لِلحالَة</b> (Ordinal) كَما كانَ الـ switch.</summary>
    public static readonly IReadOnlyDictionary<string, DealPatternDefinition> Patterns =
        new Dictionary<string, DealPatternDefinition>(StringComparer.Ordinal)
        {
            // راكِب يَنشُر طَلَب → سائِق يَقبَل → بَدء الرَّحلَة (Confirmed)
            // → إكمال (Delivered) → تَقييم.
            ["trip"] = Define("trip",
                DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
                DealStage.Delivered, DealStage.Reviewed),

            // مُستَأجِر يَطلُب → مالِك يَقبَل → دَفع تَأمين → تَسليم →
            // إعادَة (Received) → تَقييم.
            ["rental"] = Define("rental",
                DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
                DealStage.Paid, DealStage.Delivered, DealStage.Received,
                DealStage.Reviewed),

            // مُشتَري يَطلُب → بائِع يُؤَكِّد → دَفع → شَحن → استِلام →
            // تَقييم.
            ["marketplace"] = Define("marketplace",
                DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
                DealStage.Paid, DealStage.Shipping, DealStage.Delivered,
                DealStage.Reviewed),

            // إعلانات بِلا دَفع داخِليّ — اِنتَهَت دَورَة عِندَ الاتِّصال.
            ["classifieds"] = Define("classifieds",
                DealStage.Offered, DealStage.Booked, DealStage.Confirmed),

            // خَدَمَة: طَلَب → عَرض سِعر → تَأكيد → دَفع → تَأديَة
            // (Delivered) → تَقييم.
            ["service"] = Define("service",
                DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
                DealStage.Paid, DealStage.Delivered, DealStage.Reviewed),
        };

    /// <summary>تَعريف نَمَط بِاسمِه، والاحتِياطيّ لِأَيّ اسم غَير
    /// مَعروف (أَو فارِغ/null) — نَفس سُلوك الفَرع <c>_</c> القَديم.</summary>
    public static DealPatternDefinition For(string? pattern)
        => pattern is not null && Patterns.TryGetValue(pattern, out var d) ? d : Fallback;

    /// <summary>الفاعِل المُخَوَّل بِمَرحَلَة، بِلا نَظَر إلى النَّمَط —
    /// الجَدوَل مُشتَرَك اليَوم، والافتِراضيّ <c>platform</c>.</summary>
    public static string ActorFor(DealStage stage)
        => DefaultActors.TryGetValue(stage, out var a) ? a : PlatformActor;

    /// <summary>تَسمِيَة مَرحَلَة، والافتِراضيّ اسمُها البَرمَجيّ.</summary>
    public static string LabelFor(DealStage stage)
        => DefaultLabelsAr.TryGetValue(stage, out var l) ? l : stage.ToString();

    /// <summary>صَفّ مَرحَلَة مَبنيّ مِن الجَدوَلَين المُشتَرَكَين.</summary>
    public static DealStageRule Row(DealStage stage)
        => new(stage, ActorFor(stage), LabelFor(stage));

    /// <summary>بِناء تَعريف نَمَط مِن تَسَلسُل مَراحِل.</summary>
    public static DealPatternDefinition Define(string pattern, params DealStage[] stages)
        => new(pattern, stages.Select(Row).ToArray());
}
