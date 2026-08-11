using ACommerce.Platform.Flows;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بَوّابَة لُغَة وَصف التَدَفُّق</b> — لِكُلّ رَمز خَرق
/// <b>مُوجَب وسالِب</b>: تَعريف سَليم لا يُطلِق الرَمز، وتَعريف مَخروق
/// يُطلِقُه. بِنَفس تَرتيب <c>DealPatternValidatorTests</c>.</para>
///
/// <para><b>ولِماذا السالِب لِكُلّ رَمز شَرط لا تَرَف</b>: مُصادِق
/// لا يُطلِق رَمزاً أَبَداً يَخضَرّ دائِماً ويَحرُس لا شَيء. السالِب
/// وَحدَه يُثبِت أَنّ الفَحص يَعمَل.</para>
/// </summary>
public class FlowDefinitionValidatorTests
{
    // ─── مادَّة الاختِبار ────────────────────────────────────────────────

    private static FlowLabel L(string ar) => new(ar, null);

    /// <summary>تَدَفُّق سَليم بِأَبسَط شَكل مُعتَبَر: اقتِراح ثُمَّ
    /// قَرار — نَفس هَيكَل تَعريفات الأَدوار والمَظهَر.</summary>
    private static FlowDefinition Sound() => new(
        Flow: "sample",
        Label: L("تَدَفُّق تَجريبيّ"),
        States: new[]
        {
            new FlowState("pending",  L("مُعَلَّق")),
            new FlowState("approved", L("مُعتَمَد"), IsTerminal: true),
            new FlowState("rejected", L("مَرفوض"), IsTerminal: true),
        },
        Transitions: new[]
        {
            new FlowTransition(FlowVocabulary.Genesis, "pending",  "system",    L("اقتِراح")),
            new FlowTransition("pending", "approved", "moderator", L("اِعتَمِد"), new[] { "revalidate_definition" }),
            new FlowTransition("pending", "rejected", "moderator", L("اِرفُض")),
        },
        Initial: "pending");

    private static IReadOnlyList<string> Codes(FlowDefinition d)
        => FlowDefinitionValidator.Validate(d).Select(x => x.Code).ToArray();

    private static void ShouldRaise(FlowDefinition d, string code)
        => Assert.Contains(code, Codes(d));

    private static void ShouldNotRaise(FlowDefinition d, string code)
        => Assert.DoesNotContain(code, Codes(d));

    // ─── المُوجَب الشامِل ────────────────────────────────────────────────

    [Fact]
    public void Sound_definition_passes_with_zero_violations()
    {
        var v = FlowDefinitionValidator.Validate(Sound());
        Assert.True(v.Count == 0,
            "تَعريف سَليم أَطلَقَ خُروقاً: " + string.Join(" | ", v.Select(x => x.Code)));
        Assert.True(FlowDefinitionValidator.IsValid(Sound()));
    }

    // ─── الصِحَّة البُنيَوِيَّة: مُوجَب وسالِب لِكُلّ رَمز ───────────────

    [Fact]
    public void flow_name_empty()
    {
        ShouldNotRaise(Sound(), "flow_name_empty");
        ShouldRaise(Sound() with { Flow = "  " }, "flow_name_empty");
    }

    [Fact]
    public void flow_label_missing_ar()
    {
        ShouldNotRaise(Sound(), "flow_label_missing_ar");
        ShouldRaise(Sound() with { Label = L("") }, "flow_label_missing_ar");
    }

    [Fact]
    public void states_empty()
    {
        ShouldNotRaise(Sound(), "states_empty");
        ShouldRaise(Sound() with { States = Array.Empty<FlowState>() }, "states_empty");
    }

    [Fact]
    public void state_key_empty()
    {
        ShouldNotRaise(Sound(), "state_key_empty");
        ShouldRaise(
            Sound() with { States = new[] { new FlowState("", L("بِلا مِفتاح")) } },
            "state_key_empty");
    }

    [Fact]
    public void duplicate_state()
    {
        ShouldNotRaise(Sound(), "duplicate_state");
        var s = Sound();
        ShouldRaise(
            s with { States = s.States.Concat(new[] { new FlowState("pending", L("مُعَلَّق ثانِيَةً")) }).ToArray() },
            "duplicate_state");
    }

    [Fact]
    public void state_label_missing_ar()
    {
        ShouldNotRaise(Sound(), "state_label_missing_ar");
        var s = Sound();
        ShouldRaise(
            s with { States = new[] { new FlowState("pending", L("")), s.States[1], s.States[2] } },
            "state_label_missing_ar");
    }

    [Fact]
    public void initial_state_empty()
    {
        ShouldNotRaise(Sound(), "initial_state_empty");
        ShouldRaise(Sound() with { Initial = "" }, "initial_state_empty");
    }

    [Fact]
    public void initial_state_unknown()
    {
        ShouldNotRaise(Sound(), "initial_state_unknown");
        ShouldRaise(Sound() with { Initial = "draft" }, "initial_state_unknown");
    }

    [Fact]
    public void transition_from_unknown()
    {
        ShouldNotRaise(Sound(), "transition_from_unknown");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("ghost", "approved", "moderator", L("مِن مَجهول")) }).ToArray() },
            "transition_from_unknown");
    }

    [Fact]
    public void transition_to_unknown()
    {
        ShouldNotRaise(Sound(), "transition_to_unknown");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "ghost", "moderator", L("إلى مَجهول")) }).ToArray() },
            "transition_to_unknown");
    }

    [Fact]
    public void actor_missing()
    {
        ShouldNotRaise(Sound(), "actor_missing");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "approved", "", L("بِلا فاعِل")) }).ToArray() },
            "actor_missing");
    }

    [Fact]
    public void actor_out_of_vocabulary()
    {
        ShouldNotRaise(Sound(), "actor_out_of_vocabulary");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "approved", "wizard", L("فاعِل مُختَرَع")) }).ToArray() },
            "actor_out_of_vocabulary");
    }

    [Fact]
    public void transition_label_missing_ar()
    {
        ShouldNotRaise(Sound(), "transition_label_missing_ar");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = new[] {
                s.Transitions[0],
                new FlowTransition("pending", "approved", "moderator", L("")),
                s.Transitions[2] } },
            "transition_label_missing_ar");
    }

    [Fact]
    public void effect_out_of_vocabulary()
    {
        ShouldNotRaise(Sound(), "effect_out_of_vocabulary");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = new[] {
                s.Transitions[0],
                new FlowTransition("pending", "approved", "moderator", L("اِعتَمِد"), new[] { "send_rocket" }),
                s.Transitions[2] } },
            "effect_out_of_vocabulary");
    }

    /// <summary>الأَثَر قائِمَة لِأَنّ الواقِع كَذلِك: التَقَدُّم إلى
    /// <c>Paid</c> يَكتُب سَطر الـ Timeline <b>ويَسحَب الدَّفع</b>.
    /// وتَكرار مِفتاح في القائِمَة لَغو يُرفَض.</summary>
    [Fact]
    public void duplicate_effect()
    {
        ShouldNotRaise(Sound(), "duplicate_effect");
        var s = Sound();

        // المُوجَب: أَثَرانِ مُختَلِفان عَلى انتِقال واحِد — مَشروع.
        ShouldNotRaise(
            s with { Transitions = new[] {
                s.Transitions[0],
                new FlowTransition("pending", "approved", "moderator", L("اِعتَمِد"),
                    new[] { "revalidate_definition", "invalidate_tenant_cache" }),
                s.Transitions[2] } },
            "duplicate_effect");

        ShouldRaise(
            s with { Transitions = new[] {
                s.Transitions[0],
                new FlowTransition("pending", "approved", "moderator", L("اِعتَمِد"),
                    new[] { "write_audit", "write_audit" }),
                s.Transitions[2] } },
            "duplicate_effect");
    }

    [Fact]
    public void duplicate_transition()
    {
        ShouldNotRaise(Sound(), "duplicate_transition");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "rejected", "moderator", L("اِرفُض ثانِيَةً")) }).ToArray() },
            "duplicate_transition");
    }

    // ─── الخَصائِص المُبرهَنَة ───────────────────────────────────────────

    /// <summary>الرَمز الَّذي يَكشِف الحالات المَيِّتَة الخَمس الَّتي
    /// وَجَدَها الجَرد — أَغلى فَحص في المُصادِق.</summary>
    [Fact]
    public void state_unreachable()
    {
        ShouldNotRaise(Sound(), "state_unreachable");
        var s = Sound();
        // حالَة مُعلَنَة لا يَبلُغُها انتِقال — تَماماً كَـ
        // OfferStatus.Expired و ReportStatus.UnderReview.
        ShouldRaise(
            s with { States = s.States.Concat(new[] {
                new FlowState("expired", L("مُنتَهٍ"), IsTerminal: true) }).ToArray() },
            "state_unreachable");
    }

    [Fact]
    public void terminal_state_has_exit()
    {
        ShouldNotRaise(Sound(), "terminal_state_has_exit");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("approved", "pending", "moderator", L("عَودَة")) }).ToArray() },
            "terminal_state_has_exit");
    }

    /// <summary>الفَحص الَّذي يُمَيِّز <c>Reviewed</c> المَقصودَة مِن
    /// <c>DealStatus.Disputed</c> المَنسِيَّة: كِلتاهُما بِلا مَخرَج،
    /// والفَرق أَنّ الأُولى تُعلِن نِهائيَّتَها.</summary>
    [Fact]
    public void dead_end_not_declared()
    {
        ShouldNotRaise(Sound(), "dead_end_not_declared");
        var s = Sound();
        ShouldRaise(
            s with { States = new[] {
                s.States[0],
                new FlowState("approved", L("مُعتَمَد")),   // بِلا IsTerminal
                s.States[2] } },
            "dead_end_not_declared");
    }

    [Fact]
    public void self_transition_without_effect()
    {
        ShouldNotRaise(Sound(), "self_transition_without_effect");
        var s = Sound();
        ShouldRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "pending", "moderator", L("لا شَيء")) }).ToArray() },
            "self_transition_without_effect");

        // والسالِب المُقابِل: انتِقال إلى النَفس <b>بِأَثَر</b> مَشروع —
        // «أَعِد المُصادَقَة بِلا تَغيير حالَة» حالَة واقِعِيَّة.
        ShouldNotRaise(
            s with { Transitions = s.Transitions.Concat(new[] {
                new FlowTransition("pending", "pending", "moderator", L("أَعِد الفَحص"),
                    new[] { "revalidate_definition" }) }).ToArray() },
            "self_transition_without_effect");
    }

    // ─── سُلوك الاستِعلام (ما تَعتَمِد عَلَيه مَسارات الكِتابَة) ────────

    [Fact]
    public void Allows_honours_actor_and_direction()
    {
        var d = Sound();
        Assert.True(d.Allows("pending", "approved", "moderator"));
        Assert.False(d.Allows("pending", "approved", "initiator"));   // فاعِل غَير مُخَوَّل
        Assert.False(d.Allows("approved", "pending", "moderator"));   // اتِّجاه غَير مُعلَن
        Assert.False(d.Allows("pending", "ghost", "moderator"));      // حالَة مَجهولَة
    }

    /// <summary><c>either</c> يَقبَل الطَرَفَين — كَما يَفهَمُها
    /// <c>DealsService.IsActorAllowed</c> حَرفاً.</summary>
    [Fact]
    public void Either_accepts_both_parties_but_not_platform()
    {
        var d = Sound() with
        {
            Transitions = new[]
            {
                new FlowTransition(FlowVocabulary.Genesis, "pending", "system", L("اقتِراح")),
                new FlowTransition("pending", "approved", "either", L("اِعتَمِد")),
                new FlowTransition("pending", "rejected", "moderator", L("اِرفُض")),
            }
        };
        Assert.True(d.Allows("pending", "approved", "initiator"));
        Assert.True(d.Allows("pending", "approved", "counterparty"));
        Assert.True(d.Allows("pending", "approved", "either"));
        Assert.False(d.Allows("pending", "approved", "platform"));
        Assert.False(d.Allows("pending", "approved", "system"));
    }

    [Fact]
    public void ReachableStates_walks_from_initial_and_genesis()
    {
        Assert.Equal(new[] { "pending", "approved", "rejected" }, Sound().ReachableStates());
    }

    [Fact]
    public void Vocabulary_is_closed()
    {
        Assert.True(FlowVocabulary.IsActor("moderator"));
        Assert.False(FlowVocabulary.IsActor("wizard"));
        Assert.False(FlowVocabulary.IsActor(null));
        Assert.True(FlowVocabulary.IsEffect("capture_payment"));
        Assert.False(FlowVocabulary.IsEffect("send_rocket"));
        Assert.False(FlowVocabulary.IsEffect(null));

        // الأَربَعَة الأُولى مِن الفاعِلين هي مُفرَدات DealPatternCatalog
        // حَرفاً — شَرط هِجرَة DealPattern بِلا تَرجَمَة (البَند ٥).
        Assert.Equal(
            new[] { "initiator", "counterparty", "either", "platform" },
            FlowVocabulary.Actors.Take(4).ToArray());
    }
}
