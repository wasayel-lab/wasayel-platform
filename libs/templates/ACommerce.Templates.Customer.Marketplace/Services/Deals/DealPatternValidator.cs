namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرق واحِد في تَعريف نَمَط. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ.</summary>
public sealed record DealPatternViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>بَوّابَة T5/T6</b> (TESTING-PROTOCOL §4) كَدَوالّ نَقِيَّة فَوق
/// <see cref="DealPatternDefinition"/>: لا قاعِدَة بَيانات، لا وَقت، لا
/// عَشوائيَّة — نَفس المُدخَل يُعطي نَفس القائِمَة دائِماً.</para>
///
/// <para><b>T5 — الصِّحَّة البُنيَويَّة</b>: المَراحِل مِن مُفرَدات
/// <see cref="DealStage"/> حَصراً، تَبدَأ بِـ Offered، بِلا تَكرار،
/// ولِكُلّ مَرحَلَة فاعِل مُعَرَّف مِن الأَربَعَة.</para>
///
/// <para><b>T6 — الخَصائِص المُبرهَنَة</b>: حَتميَّة الانتِهاء (المَشي مِن
/// أَوَّل مَرحَلَة يَبلُغ آخِرَها بِلا دَورَة)، ولا مَرحَلَة يَتيمَة (لا
/// مَرحَلَة مُعلَنَة خارِج السِّلسِلَة، ولا مَرحَلَة غَير نِهائيَّة بِلا
/// مَن يُحَرِّكها).</para>
///
/// <para><b>ما لا تَفحَصُه هذه الدَوالّ عَمداً</b>: أَن يَنتَهي النَّمَط
/// بِـ <c>Reviewed</c>. النَّمَط <c>classifieds</c> يَنتَهي عِندَ
/// <c>Confirmed</c> قَصداً (لا دَفع داخِليّ)، فَجَعل ذلِك خَرقاً يَرفُض
/// نَمَطاً قِياسيّاً قائِماً. والاكتِمال التِّلقائيّ وَمَسارا الإلغاء
/// والنِزاع سُلوك <see cref="DealsService"/> لا سُلوك التَّعريف —
/// مَوضِعُهما T7 بِمُحاكاة، لا هُنا.</para>
///
/// <para>هذه بِذرَة كُتلَة ب: أَيّ نَمَط مُوَلَّد مُستَقبَلاً يَمُرّ مِن
/// هُنا قَبل أَن يَراه بَشَر.</para>
/// </summary>
public static class DealPatternValidator
{
    /// <summary>أَقصى عَدَد خُطُوات مَعقول — عَدَد المُفرَدات نَفسُه.
    /// تَجاوُزُه يَعني دَورَة.</summary>
    private static readonly int MaxStages = Enum.GetValues<DealStage>().Length;

    /// <summary>T5 + T6 مَعاً — القائِمَة فارِغَة تَعني نَمَطاً صالِحاً.</summary>
    public static IReadOnlyList<DealPatternViolation> Validate(DealPatternDefinition d)
        => ValidateStructure(d).Concat(ValidateProperties(d)).ToArray();

    /// <summary>هَل يَجتاز البَوّابَتَين؟</summary>
    public static bool IsValid(DealPatternDefinition d) => Validate(d).Count == 0;

    // ─── T5 — الصِّحَّة البُنيَويَّة ────────────────────────────────────
    public static IReadOnlyList<DealPatternViolation> ValidateStructure(DealPatternDefinition d)
    {
        var v = new List<DealPatternViolation>();

        if (string.IsNullOrWhiteSpace(d.Pattern))
            v.Add(new("pattern_name_empty", "اسم النَّمَط فارِغ."));

        if (d.Stages.Count == 0)
        {
            v.Add(new("stages_empty", "النَّمَط بِلا مَراحِل."));
            return v;   // بَقيَّة الفُحوص بِلا مَعنى عَلى قائِمَة فارِغَة
        }

        foreach (var r in d.Stages)
        {
            if (!Enum.IsDefined(r.Stage))
                v.Add(new("stage_out_of_vocabulary",
                    $"المَرحَلَة «{(int)r.Stage}» خارِج مُفرَدات DealStage الثَّمانِ."));

            if (string.IsNullOrWhiteSpace(r.Actor))
                v.Add(new("actor_missing", $"المَرحَلَة «{r.Stage}» بِلا فاعِل."));
            else if (!DealPatternCatalog.Actors.Contains(r.Actor))
                v.Add(new("actor_out_of_vocabulary",
                    $"فاعِل المَرحَلَة «{r.Stage}» = «{r.Actor}» خارِج الأَربَعَة."));

            if (string.IsNullOrWhiteSpace(r.LabelAr))
                v.Add(new("label_missing", $"المَرحَلَة «{r.Stage}» بِلا تَسمِيَة."));
        }

        if (d.Stages[0].Stage != DealStage.Offered)
            v.Add(new("first_stage_not_offered",
                $"النَّمَط يَبدَأ بِـ «{d.Stages[0].Stage}» لا بِـ Offered."));

        var seen = new HashSet<DealStage>();
        foreach (var r in d.Stages)
            if (!seen.Add(r.Stage))
                v.Add(new("duplicate_stage", $"المَرحَلَة «{r.Stage}» مُكَرَّرَة."));

        return v;
    }

    // ─── T6 — الخَصائِص المُبرهَنَة ─────────────────────────────────────
    public static IReadOnlyList<DealPatternViolation> ValidateProperties(DealPatternDefinition d)
    {
        var v = new List<DealPatternViolation>();
        if (d.Stages.Count == 0)
        {
            v.Add(new("stages_empty", "النَّمَط بِلا مَراحِل."));
            return v;
        }

        // حَتميَّة الانتِهاء: المَشي بِـ Next مِن أَوَّل مَرحَلَة يَنتَهي،
        // بِلا زِيارَة مُكَرَّرَة، وعِندَ آخِر مَرحَلَة مُعلَنَة.
        var visited = new List<DealStage>();
        var guard = new HashSet<DealStage>();
        var current = d.Stages[0].Stage;
        visited.Add(current);
        guard.Add(current);

        var steps = 0;
        while (d.Next(current) is DealStage next)
        {
            steps++;
            if (steps > MaxStages || !guard.Add(next))
            {
                v.Add(new("termination_not_deterministic",
                    $"المَشي في «{d.Pattern}» لَم يَنتَهِ — دَورَة عِندَ «{next}»."));
                return v;   // لا تُكمِل المَشي في دَورَة
            }
            visited.Add(next);
            current = next;
        }

        if (current != d.Stages[^1].Stage)
            v.Add(new("walk_misses_last_stage",
                $"المَشي تَوَقَّف عِندَ «{current}» لا عِندَ «{d.Stages[^1].Stage}»."));

        // لا مَرحَلَة يَتيمَة (١): مَرحَلَة مُعلَنَة لا تَصِلُها السِّلسِلَة.
        foreach (var r in d.Stages)
            if (!visited.Contains(r.Stage))
                v.Add(new("orphan_stage_unreachable",
                    $"المَرحَلَة «{r.Stage}» مُعلَنَة ولا تَصِلُها السِّلسِلَة."));

        // لا مَرحَلَة يَتيمَة (٢): مَرحَلَة غَير نِهائيَّة لا يَملِك أَحَد
        // تَحريكَها (فاعِلُها خارِج الأَربَعَة الَّتي يَفهَمُها
        // DealsService.IsActorAllowed).
        foreach (var r in d.Stages)
            if (d.Next(r.Stage) is not null && !DealPatternCatalog.Actors.Contains(r.Actor))
                v.Add(new("orphan_stage_unmovable",
                    $"المَرحَلَة «{r.Stage}» لا يَملِك أَحَد تَحريكَها (فاعِل «{r.Actor}»)."));

        return v;
    }
}
