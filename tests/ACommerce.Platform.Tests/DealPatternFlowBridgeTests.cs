using ACommerce.Platform.Flows;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بُرهان الجِسر</b> — أَنّ إسقاط نَمَط الصَفقَة على لُغَة
/// التَدَفُّق <b>لا يَفقِد شَيئاً</b>. لِكُلّ نَمَط مِن الخَمسَة
/// والاحتِياطيّ: نَفس تَرتيب المَراحِل، ونَفس التالي لِكُلّ مَرحَلَة،
/// ونَفس الفاعِل، ونَفس التَسمِيَة، ونَفس جَواب <c>Includes</c>.</para>
///
/// <para><b>وهذا هو ما يَجعَل الاستِبدال لاحِقاً مِيكانيكِيّاً</b>:
/// حينَ يُقَرَّر أَن يَقرَأ <c>DealsService</c> مِن
/// <see cref="FlowDefinition"/> بَدَل <c>DealsPolicy</c>، لا يَبقى
/// سُؤال «هَل يُعطي الجَديد نَفس الجَواب؟» — هذا المِلَفّ يُجيبُه
/// الآن، قَبل أَن يُمَسّ سَطر واحِد في المَسار الحَيّ.</para>
///
/// <para><b>والحَدّ مُعلَن</b>: الجِسر <b>لا يَقرَؤُه أَحَد في وَقت
/// التَشغيل</b>. الكاتالوج ما زالَ مَصدَر الحَقيقَة،
/// و<c>DealsService</c> لَم يُمَسّ بِحَرف في هذه المَوجَة.</para>
/// </summary>
public class DealPatternFlowBridgeTests
{
    /// <summary>الخَمسَة المُسَجَّلَة والاحتِياطيّ.</summary>
    public static TheoryData<string> Patterns() => new()
    {
        "trip", "rental", "marketplace", "classifieds", "service",
        DealPatternCatalog.FallbackPattern,
    };

    private static DealPatternDefinition Source(string pattern)
        => pattern == DealPatternCatalog.FallbackPattern
            ? DealPatternCatalog.Fallback
            : DealPatternCatalog.Patterns[pattern];

    // ─── التَطابُق: كُلّ ما يُعلِنُه التَعريف يُعلِنُه المُسقَط ─────────

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Stage_order_survives_the_projection(string pattern)
    {
        var d = Source(pattern);
        Assert.Equal(
            d.StageOrder.Select(s => s.ToString()).ToArray(),
            DealPatternFlowBridge.ToFlow(d).StateKeys);
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Next_stage_survives_the_projection(string pattern)
    {
        var d = Source(pattern);
        var f = DealPatternFlowBridge.ToFlow(d);

        foreach (var stage in Enum.GetValues<DealStage>())
        {
            var expected = d.Next(stage);
            var actual   = f.NextStates(stage.ToString());

            if (expected is null)
                Assert.Empty(actual);           // آخِر مَرحَلَة، أَو ليسَت مِن النَمَط
            else
                Assert.Equal(new[] { expected.Value.ToString() }, actual);
        }
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void Actor_and_label_and_membership_survive_the_projection(string pattern)
    {
        var d = Source(pattern);
        var f = DealPatternFlowBridge.ToFlow(d);

        foreach (var stage in Enum.GetValues<DealStage>())
        {
            var key = stage.ToString();

            // العُضوِيَّة
            Assert.Equal(d.Includes(stage), f.HasState(key));
            if (!d.Includes(stage)) continue;

            // التَسمِيَة
            Assert.Equal(d.LabelFor(stage), f.State(key)!.Label.Ar);

            // الفاعِل — يَملِكُه الانتِقال الخارِج مِن المَرحَلَة، لِأَنّ
            // DealsService يَفحَص فاعِل المَرحَلَة الحاليَّة لا التالِيَة.
            var outgoing = f.From(key);
            if (d.Next(stage) is not null)
            {
                Assert.Single(outgoing);
                Assert.Equal(d.ActorFor(stage), outgoing[0].Actor);
            }
            else
            {
                Assert.Empty(outgoing);
            }
        }
    }

    /// <summary>الفاعِل المُخَوَّل يُجيب بِنَفس القَرار مِن
    /// السَطحَين — وهذا هو السُؤال الوَحيد الَّذي يَطرَحُه مَسار
    /// الكِتابَة فِعلاً.</summary>
    [Theory]
    [MemberData(nameof(Patterns))]
    public void Allows_agrees_with_the_policy_for_every_stage_and_actor(string pattern)
    {
        var d = Source(pattern);
        var f = DealPatternFlowBridge.ToFlow(d);

        foreach (var stage in Enum.GetValues<DealStage>())
        foreach (var actor in FlowVocabulary.Actors)
        {
            var next = d.Next(stage);
            if (next is null) continue;

            var required = d.ActorFor(stage)!;
            var expected = required == actor
                        || (required == "either" && (actor == "initiator" || actor == "counterparty"));

            Assert.Equal(expected, f.Allows(stage.ToString(), next.Value.ToString(), actor));
        }
    }

    // ─── وكُلّ مُسقَط يَجتاز المُصادِق ──────────────────────────────────

    /// <summary><b>لَم يَكُن هذا مَضموناً قَبل القِياس</b>: أَنماط
    /// الصَفقَة كُتِبَت قَبل اللُغَة، فَاجتِيازُها بَوّابَتَها بِصِفر
    /// خَرق نَتيجَة لا افتِراض. ومِنها أَنّ آخِر مَرحَلَة في كُلّ
    /// نَمَط مُعلَنَة نِهائيَّة — بِما فيها <c>classifieds</c>
    /// المُنتَهي عِندَ <c>Confirmed</c> قَصداً.</summary>
    [Theory]
    [MemberData(nameof(Patterns))]
    public void Every_projected_pattern_passes_the_validator(string pattern)
    {
        var f = DealPatternFlowBridge.ToFlow(Source(pattern));
        var v = FlowDefinitionValidator.Validate(f);

        Assert.True(v.Count == 0,
            $"النَمَط «{pattern}» أَطلَقَ خُروقاً: {string.Join(" | ", v.Select(x => x.Code))}");
    }

    [Fact]
    public void All_projects_five_patterns_plus_the_fallback()
    {
        var all = DealPatternFlowBridge.All();
        Assert.Equal(
            new[] { "classifieds", "default", "marketplace", "rental", "service", "trip" },
            all.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

        foreach (var (name, f) in all)
            Assert.Equal("deal_stage." + name, f.Flow);
    }

    /// <summary>الأَثَر المُزدَوَج في مَوضِعِه: التَقَدُّم إلى
    /// <c>Paid</c> يَكتُب السَطر ويَسحَب الدَّفع. الأَنماط الَّتي بِلا
    /// دَفع داخِليّ لا تَحمِلُه.</summary>
    [Fact]
    public void Capture_payment_appears_exactly_on_transitions_into_Paid()
    {
        foreach (var (name, f) in DealPatternFlowBridge.All())
        foreach (var t in f.Transitions)
        {
            var expected = t.To == nameof(DealStage.Paid);
            Assert.Equal(expected, t.EffectKeys.Contains("capture_payment"));
            Assert.Contains("append_timeline", t.EffectKeys);
        }

        // marketplace يَمُرّ بِالدَّفع، وclassifieds لا.
        Assert.Contains(DealPatternFlowBridge.All()["marketplace"].Transitions,
            t => t.EffectKeys.Contains("capture_payment"));
        Assert.DoesNotContain(DealPatternFlowBridge.All()["classifieds"].Transitions,
            t => t.EffectKeys.Contains("capture_payment"));
    }
}
