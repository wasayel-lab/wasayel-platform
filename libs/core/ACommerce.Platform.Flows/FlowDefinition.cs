using System.Text.Json.Serialization;

namespace ACommerce.Platform.Flows;

/// <summary>
/// <para><b>تَسمِيَة مُوَطَّنَة</b> — عَرَبيّ وإنجليزيّ. العَرَبيّ
/// <b>إلزاميّ</b> (المَنصَّة عَرَبيَّة أَوَّلاً)، والإنجليزيّ اختِياريّ
/// إلى أَن تُنَفَّذ تَعَدُّدِيَّة اللُغَة.</para>
///
/// <para><b>ولِماذا هُنا أَصلاً</b>: الجَرد وَجَدَ تَسمِيات الحالات
/// مَنثورَة ومُكَرَّرَة حَرفِيّاً في أَكثَر مِن واجِهَة — سِتّ تَسمِيات
/// لِحالات الحاضِنَة في <c>IncubatorHome.razor</c> وِ<c>StudioHome.razor</c>
/// مَعاً. التَّعريف يَحمِلُها مَرَّةً واحِدَة.</para>
/// </summary>
public sealed record FlowLabel(string Ar, string? En = null);

/// <summary>
/// <para><b>حالَة واحِدَة في تَدَفُّق</b>.</para>
///
/// <para><see cref="IsTerminal"/> <b>إعلان لا اشتِقاق</b>: حالَة بِلا
/// انتِقال خارِج قَد تَكون نِهائيَّة بِالقَصد، أَو ثَغرَةً لَم يَنتَبِه
/// لَها أَحَد. الجَرد وَجَدَ الحالَتَين: <c>Reviewed</c> نِهائيَّة
/// بِالقَصد، وَ<c>DealStatus.Disputed</c> نِهائيَّة <b>بِلا قَصد</b> —
/// تَعليقُها يَقول «يَنتَظِر تَدَخُّل المَنصَّة» والتَّدَخُّل غَير
/// مُنَفَّذ. لَو اشتُقَّت النِهائيَّة مِن غِياب الانتِقالات لَما فَرَّقَ
/// المُصادِق بَينَهُما أَبَداً؛ وبِالإعلان يَصير الفَرق خَرقاً
/// يُطبَع (<c>dead_end_not_declared</c>).</para>
/// </summary>
/// <param name="Key">مِفتاح الحالَة كَما يُخَزَّن — <c>"pending"</c> أَو
/// <c>"Offered"</c>. يُقارَن <b>حَسّاساً لِلحالَة</b> (Ordinal) لِأَنّ
/// كُلّ مُقارَنات الحالات القائِمَة كَذلِك.</param>
/// <param name="Label">التَّسمِيَة المَعروضَة.</param>
/// <param name="IsTerminal">هَل يَنتَهي التَدَفُّق هُنا؟</param>
public sealed record FlowState(
    string Key,
    FlowLabel Label,
    bool IsTerminal = false);

/// <summary>
/// <para><b>انتِقال واحِد</b> — مِن حالَة إلى حالَة، بِفاعِل مُخَوَّل،
/// وبِاسم أَثَر <b>مُحال إلَيه لا مَحمول</b>.</para>
///
/// <para><b>وهذا هو حَدّ اللُغَة، مُعلَناً في الشَكل نَفسِه</b>:
/// <see cref="Effect"/> <b>مِفتاح</b> مِن مُعجَم مُغلَق يَملِكُه الكود،
/// لا وَصف لِما يَقَع. المُبَرِّر مَقيس مِن الجَرد:
/// <c>DealsService</c> عِندَ بُلوغ <c>Paid</c> يَحسِب العُمولَة ثُمَّ
/// <b>يَسحَب الدَّفع مِن مُزَوِّد خارِجيّ</b> بِمُحاوَلَة وفَشَل
/// وتَسجيل في الـ Timeline، وعِندَ الإلغاء <b>يُرجِع المَبلَغ</b>. لا
/// يُوصَف ذلِك بِـ JSON، ولا يُحاوَل — فَالمُحاوَلَة تُنتِج لُغَةَ
/// بَرمَجَةٍ ثانِيَة بِلا مُنَقِّح ولا اختِبار. التَّعريف يُجيب «مَن
/// يَملِك؟» و«إلى أَين؟»، والكود يُجيب «ماذا يَقَع؟».</para>
/// </summary>
/// <param name="From">مِفتاح حالَة المَصدَر، أَو <see cref="FlowVocabulary.Genesis"/>
/// لِلانتِقال المُنشِئ (لا حالَة قَبلَه).</param>
/// <param name="To">مِفتاح حالَة الهَدَف.</param>
/// <param name="Actor">الفاعِل المُخَوَّل — مِن
/// <see cref="FlowVocabulary.Actors"/> حَصراً.</param>
/// <param name="Label">تَسمِيَة الإجراء («اِعتَمِد»، «اِرفُض») لا
/// تَسمِيَة الحالَة.</param>
/// <param name="Effects">مَفاتيح الآثار <b>بِتَرتيب وُقوعِها</b>،
/// أَو فارِغَة لِانتِقال يُغَيِّر الحالَة ولا يَفعَل شَيئاً آخَر.</param>
public sealed record FlowTransition(
    string From,
    string To,
    string Actor,
    FlowLabel Label,
    IReadOnlyList<string>? Effects = null)
{
    /// <summary>
    /// <para>الآثار مُطَبَّعَة — فارِغَة لا <c>null</c>.</para>
    ///
    /// <para><b>ولِماذا قائِمَة لا مِفتاحاً واحِداً</b>: القِياس فَرَضَها.
    /// كانَ الحَقل <c>string?</c> حَتَّى أُريدَ وَصف تَقَدُّم الصَفقَة
    /// إلى <c>Paid</c>، فَظَهَرَ أَنَّه <b>يَكتُب سَطر الـ Timeline
    /// ويَسحَب الدَّفع مَعاً</b> (<c>DealsService.cs:116</c> ثُمَّ
    /// <c>:134</c>)، وأَنّ الإلغاء <b>يُرجِع المَبلَغ ويَكتُب السَطر</b>.
    /// مِفتاح واحِد كانَ سَيَصِف نِصف ما يَقَع — وتَعريف يَصِف النِصف
    /// أَسوَأ مِن تَعريف لا يوجَد، لِأَنَّه يُقرَأ ويُصَدَّق.</para>
    /// </summary>
    public IReadOnlyList<string> EffectKeys { get; } = Effects ?? Array.Empty<string>();

    /// <summary><b>مُساواة بُنيَوِيَّة</b> — المُوَلَّدَة تُقارِن
    /// <see cref="Effects"/> بِالمَرجِع (قائِمَة)، فَتَعريفانِ
    /// مُتَطابِقان مَبنيّانِ مُنفَصِلَين كانا سَيَختَلِفان. والمُقارَنَة
    /// هي بِالضَبط ما يُبَرهِن أَنّ الأَدوار والمَظهَر تَعريف
    /// واحِد.</summary>
    public bool Equals(FlowTransition? other)
        => other is not null
        && string.Equals(From,  other.From,  StringComparison.Ordinal)
        && string.Equals(To,    other.To,    StringComparison.Ordinal)
        && string.Equals(Actor, other.Actor, StringComparison.Ordinal)
        && Label == other.Label
        && EffectKeys.SequenceEqual(other.EffectKeys, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(From, StringComparer.Ordinal);
        h.Add(To, StringComparer.Ordinal);
        h.Add(Actor, StringComparer.Ordinal);
        h.Add(Label);
        foreach (var e in EffectKeys) h.Add(e, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// <para><b>المَعاجِم المُغلَقَة</b> — ما تَفهَمُه اللُغَة وما عَداه
/// خَرق. مُستَخرَجَة مِن الجَرد لا مُخترَعَة.</para>
/// </summary>
public static class FlowVocabulary
{
    /// <summary><c>From</c> لِلانتِقال المُنشِئ — لا حالَة قَبلَه.
    /// الجَرد وَجَدَ هذا في كُلّ تَدَفُّق بِلا استِثناء: الاقتِراح
    /// يَكتُب <c>pending</c> مِن لا شَيء، والصَفقَة تُنشَأ
    /// <c>Offered</c>. رَمز صَريح أَصدَق مِن <c>null</c> صامِت.</summary>
    public const string Genesis = "(genesis)";

    /// <summary>
    /// <para><b>الفاعِلون السِتَّة</b>. الأَربَعَة الأُولى هي مُفرَدات
    /// <c>DealPatternCatalog.Actors</c> حَرفاً — فَلا يَحتاج
    /// <c>DealPattern</c> تَرجَمَةً حينَ يُهاجِر (البَند ٥).</para>
    ///
    /// <para>وَالاثنان الأَخيران أَضافَهُما الجَرد: <c>moderator</c>
    /// لِأَنّ التَّبليغات وتَعريفات الأَدوار والمَظهَر يُقَرِّرُها
    /// <b>بَشَر مُخَوَّل ليسَ طَرَفاً في المُعامَلَة</b> — وطَيُّه في
    /// <c>platform</c> كانَ سَيَخلِط قَراراً بَشَريّاً بِفِعل آليّ؛
    /// وَ<c>system</c> لِما يَقَع بِلا فاعِل بَشَريّ أَصلاً
    /// (<c>Analyzing → Completed</c> عِندَ عَودَة المُحَلِّل،
    /// وَ<c>Active → Completed</c> عِندَ بُلوغ آخِر مَرحَلَة).</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Actors =
        new[] { "initiator", "counterparty", "either", "platform", "moderator", "system" };

    private static readonly HashSet<string> ActorSet = new(Actors, StringComparer.Ordinal);
    public static bool IsActor(string? actor) => actor is not null && ActorSet.Contains(actor);

    /// <summary>
    /// <para><b>مُعجَم الآثار المُغلَق</b> — كُلّ مِفتاح هُنا لَه كود
    /// يُنَفِّذُه اليَوم، ومَوضِعُه مَذكور. إضافَة مِفتاح بِلا كود
    /// تَجعَل التَّعريف يَكذِب، ولِذلِك المُصادِق يَرفُض أَيّ مِفتاح
    /// خارِج هذه القائِمَة.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> Effects =
        new[]
        {
            // DealsService.cs:121-147 — حِساب العُمولَة ثُمَّ Capture
            "capture_payment",
            // DealsService.cs:172-189 — إرجاع المَبلَغ قَبل الإلغاء
            "refund_payment",
            // MarketplaceTemplateExtensions.cs:955 — رَفض كُلّ العُروض الشَقيقَة
            "close_sibling_offers",
            // MarketplaceTemplateExtensions.cs:~960 — مُحادَثَة تَنسيق ٢٤ ساعَة
            "open_coordination_chat",
            // MarketplaceTemplateExtensions.cs:1131-1139 — إنهاء المُحادَثَة
            "expire_coordination_chat",
            // TenantRoleService.cs:191-204 / TenantThemeService — إعادَة المُصادَقَة عِندَ الاعتِماد
            "revalidate_definition",
            // TenantRoleService.cs:169, 211 — إبطال كاش المُستَأجِر
            "invalidate_tenant_cache",
            // AuditWriter.WriteAsync — سَطر تَدقيق
            "write_audit",
            // FeasibilityAnalysisService.cs:~200 — إطلاق التَّحليل في الخَلفِيَّة
            "run_analysis",
            // DealsService.cs:116 — سَطر في Timeline الصَفقَة
            "append_timeline",
        };

    private static readonly HashSet<string> EffectSet = new(Effects, StringComparer.Ordinal);
    public static bool IsEffect(string? effect) => effect is not null && EffectSet.Contains(effect);
}

/// <summary>
/// <para><b>تَدَفُّق كامِل كَبَيانات</b> — حالاتُه وانتِقالاتُه وفاعِلوه
/// ومَبدَؤُه ومُنتَهاه. سِجِلّ بَيانات خالِص: لا Marten ولا HTTP ولا
/// وَقت ولا عَشوائيَّة — يُبنى في الذاكِرَة اليَوم، ويُقرَأ مِن وَثيقَة
/// غَداً بِلا تَغيير في شَكلِه.</para>
///
/// <para><b>حَدّ هذه المَوجَة صَراحَةً</b>: <b>لا شَيء في وَقت
/// التَّشغيل يَقرَأ هذه التَعريفات لِيُقَرِّر</b> — عَدا التَدَفُّقَين
/// المُوَحَّدَين في البَند ٤. قيمَتُها اليَوم: تَوثيق مَقيس، ومُصادَقَة
/// تَكشِف الحالات المَيِّتَة، وجاهِزيَّة شَكل. الاعتِماد يَأتي
/// تَدَفُّقاً تَدَفُّقاً بِتَوصيف يَخضَرّ أَوَّلاً — لا دُفعَةً واحِدَة.</para>
///
/// <para>الصِحَّة البُنيَوِيَّة والخَصائِص المُبرهَنَة في
/// <see cref="FlowDefinitionValidator"/> — بِنَفس قالِب
/// <c>DealPatternValidator</c> الَّذي أُثبِتَ في مَوجَة الأَنماط.</para>
/// </summary>
/// <param name="Flow">اسم التَدَفُّق (slug) — <c>tenant_role</c>،
/// <c>offer</c>، <c>deal_stage</c>…</param>
/// <param name="Label">تَسمِيَة التَدَفُّق لِلعَرض.</param>
/// <param name="States">الحالات — بِتَرتيب العَرض.</param>
/// <param name="Transitions">الانتِقالات المَسموحَة، ولا شَيء غَيرُها.</param>
/// <param name="Initial">مِفتاح الحالَة الابتِدائيَّة — ما يُكتَب عِندَ
/// الإنشاء.</param>
public sealed record FlowDefinition(
    string Flow,
    FlowLabel Label,
    IReadOnlyList<FlowState> States,
    IReadOnlyList<FlowTransition> Transitions,
    string Initial)
{
    /// <summary>مَفاتيح الحالات بِتَرتيبِها. مُستَثناة مِن التَسَلسُل
    /// لِأَنَّها مُشتَقَّة مِن <see cref="States"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> StateKeys { get; } =
        States.Select(s => s.Key).ToArray();

    /// <summary>الحالَة بِمِفتاحِها، أَو <c>null</c>.</summary>
    public FlowState? State(string key)
    {
        foreach (var s in States) if (string.Equals(s.Key, key, StringComparison.Ordinal)) return s;
        return null;
    }

    /// <summary>هَل المِفتاح حالَة مُعلَنَة في هذا التَدَفُّق؟</summary>
    public bool HasState(string key) => State(key) is not null;

    /// <summary>الانتِقالات الخارِجَة مِن حالَة.</summary>
    public IReadOnlyList<FlowTransition> From(string state)
        => Transitions.Where(t => string.Equals(t.From, state, StringComparison.Ordinal)).ToArray();

    /// <summary>الحالات الَّتي يُمكِن بُلوغُها مِن حالَة بِخُطوَة واحِدَة.</summary>
    public IReadOnlyList<string> NextStates(string state)
        => From(state).Select(t => t.To).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// <para><b>هَل يُسمَح بِهذا الانتِقال لِهذا الفاعِل؟</b> — السُؤال
    /// الوَحيد الَّذي يَطرَحُه مَسار كِتابَة. فاعِل <c>either</c>
    /// المُعلَن في الانتِقال يَقبَل <c>initiator</c> و<c>counterparty</c>
    /// مَعاً، تَماماً كَما يَفهَمُها <c>DealsService.IsActorAllowed</c>.</para>
    /// </summary>
    public bool Allows(string from, string to, string actor)
        => Transitions.Any(t =>
            string.Equals(t.From, from, StringComparison.Ordinal) &&
            string.Equals(t.To, to, StringComparison.Ordinal) &&
            ActorSatisfies(t.Actor, actor));

    /// <summary>هَل الانتِقال مَسموح أَصلاً، بِصَرف النَظَر عَن الفاعِل؟</summary>
    public bool Allows(string from, string to)
        => Transitions.Any(t =>
            string.Equals(t.From, from, StringComparison.Ordinal) &&
            string.Equals(t.To, to, StringComparison.Ordinal));

    /// <summary><c>either</c> يَقبَل الطَرَفَين؛ وما عَداه تَطابُق حَرفيّ.</summary>
    private static bool ActorSatisfies(string required, string actual)
        => string.Equals(required, actual, StringComparison.Ordinal)
        || (string.Equals(required, "either", StringComparison.Ordinal)
            && (string.Equals(actual, "initiator", StringComparison.Ordinal)
             || string.Equals(actual, "counterparty", StringComparison.Ordinal)));

    /// <summary>الحالات المُعلَنَة نِهائيَّةً.</summary>
    public IReadOnlyList<string> TerminalStates()
        => States.Where(s => s.IsTerminal).Select(s => s.Key).ToArray();

    /// <summary>
    /// <para><b>الحالات البالِغَة</b> — ما يُبلَغ فِعلاً بِالمَشي مِن
    /// <see cref="Initial"/> عَبر الانتِقالات المُعلَنَة. هذه هي الدالَّة
    /// الَّتي تَكشِف الحالات المَيِّتَة الخَمس الَّتي وَجَدَها الجَرد.</para>
    /// </summary>
    public IReadOnlyList<string> ReachableStates()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        if (HasState(Initial)) { seen.Add(Initial); queue.Enqueue(Initial); }

        // الانتِقال المُنشِئ قَد يَبلُغ حالَةً غَير الابتِدائيَّة.
        foreach (var t in Transitions)
            if (string.Equals(t.From, FlowVocabulary.Genesis, StringComparison.Ordinal)
                && HasState(t.To) && seen.Add(t.To))
                queue.Enqueue(t.To);

        while (queue.Count > 0)
            foreach (var next in NextStates(queue.Dequeue()))
                if (HasState(next) && seen.Add(next))
                    queue.Enqueue(next);

        return States.Where(s => seen.Contains(s.Key)).Select(s => s.Key).ToArray();
    }
}
