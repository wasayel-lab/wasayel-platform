using ACommerce.Platform.Flows;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>جِسر النَمَط إلى اللُغَة</b> — يُسقِط
/// <see cref="DealPatternDefinition"/> على <see cref="FlowDefinition"/>
/// بِلا أَن يَمَسّ مَصدَر الحَقيقَة. الكاتالوج يَبقى كَما هو،
/// و<see cref="DealsPolicy"/> يَبقى كَما هو، و<c>DealsService</c> لَم
/// يُمَسّ بِحَرف.</para>
///
/// <para><b>ولِماذا جِسر لا استِبدال</b> — والاختِيار مُعلَن لِأَنّ
/// لَه بَديلاً مَعقولاً: الاستِبدال الكامِل يَعني أَن يَقرَأ
/// <c>DealsService</c> مِن <see cref="FlowDefinition"/>، وذلِك يَمَسّ
/// المَسار الوَحيد الَّذي تَمُرّ مِنه <b>كُلّ</b> صَفقَة في المَنصَّة.
/// والجِسر يُعطي القيمَة نَفسَها لِلقارِئ (نَمَط الصَفقَة صارَ
/// مَوصوفاً بِنَفس اللُغَة الَّتي تَصِف البَقيَّة) بِمُخاطَرَة صِفر،
/// ويَجعَل الاستِبدال لاحِقاً <b>مِيكانيكِيّاً</b>: التَطابُق مُبرهَن
/// هُنا، فَلا يَبقى إلّا تَبديل مَوضِع القِراءَة.</para>
///
/// <para><b>وما يُبرهِنُه التَطابُق</b>
/// (<c>DealPatternFlowBridgeTests</c>): لِكُلّ نَمَط مِن الخَمسَة
/// والاحتِياطيّ، المُسقَط يُعطي <b>حَرفِيّاً</b> ما يُعطيه التَعريف —
/// نَفس تَرتيب المَراحِل، ونَفس التالي لِكُلّ مَرحَلَة، ونَفس
/// الفاعِل، ونَفس التَسمِيَة، ونَفس جَواب <c>Includes</c>. وكُلّ
/// مُسقَط <b>يَجتاز المُصادِق بِصِفر خَرق</b> — وهو ما لَم يَكُن
/// مَضموناً قَبل القِياس.</para>
///
/// <para><b>ولا تَرجَمَة لِلفاعِلين</b>: الأَربَعَة
/// (<c>initiator | counterparty | either | platform</c>) هي بِعَينِها
/// أَوائِل <see cref="FlowVocabulary.Actors"/> — صُمِّمَ المَعجَم
/// كَذلِك عَمداً في البَند ٢ لِيَكون هذا الجِسر سَطراً لا جَدوَلاً.</para>
/// </summary>
public static class DealPatternFlowBridge
{
    /// <summary>
    /// <para>يُسقِط تَعريف نَمَط على تَدَفُّق. المَراحِل تَصير حالات
    /// بِتَرتيبِها، وكُلّ زَوج مُتَتالٍ يَصير انتِقالاً يَملِكُه فاعِل
    /// المَرحَلَة <b>المَصدَر</b> — لِأَنّ <c>DealsService</c> يَفحَص
    /// <c>DealsPolicy.Actor(deal.Stage)</c> أَي فاعِل المَرحَلَة
    /// الحاليَّة لا التالِيَة.</para>
    ///
    /// <para>والمَرحَلَة الأَخيرَة <b>نِهائيَّة</b>: لا تالِيَ لَها في
    /// النَمَط. وهذا يَشمَل <c>classifieds</c> المُنتَهي عِندَ
    /// <c>Confirmed</c> قَصداً.</para>
    /// </summary>
    public static FlowDefinition ToFlow(DealPatternDefinition d)
    {
        var stages = d.Stages;
        var last   = stages.Count == 0 ? (DealStage?)null : stages[^1].Stage;

        var states = stages
            .Select(r => new FlowState(
                r.Stage.ToString(),
                new FlowLabel(r.LabelAr, r.Stage.ToString()),
                IsTerminal: r.Stage == last))
            .ToArray();

        var transitions = new List<FlowTransition>();

        if (stages.Count > 0)
            transitions.Add(new FlowTransition(
                FlowVocabulary.Genesis,
                stages[0].Stage.ToString(),
                stages[0].Actor,
                new FlowLabel("اِبدَأ " + stages[0].LabelAr, "Start"),
                new[] { "append_timeline" }));

        for (var i = 0; i < stages.Count - 1; i++)
        {
            var from = stages[i];
            var to   = stages[i + 1];

            // التَقَدُّم إلى الدَّفع يَكتُب سَطر الـ Timeline ويَسحَب
            // الدَّفع — أَثَرانِ، وهُما سَبَب كَون الحَقل قائِمَةً.
            var effects = to.Stage == DealStage.Paid
                ? new[] { "append_timeline", "capture_payment" }
                : new[] { "append_timeline" };

            transitions.Add(new FlowTransition(
                from.Stage.ToString(),
                to.Stage.ToString(),
                from.Actor,
                new FlowLabel(to.LabelAr, to.Stage.ToString()),
                effects));
        }

        return new FlowDefinition(
            Flow: "deal_stage." + d.Pattern,
            Label: new FlowLabel("مَراحِل نَمَط «" + d.Pattern + "»", "Deal stages: " + d.Pattern),
            States: states,
            Transitions: transitions,
            Initial: stages.Count == 0 ? "" : stages[0].Stage.ToString());
    }

    /// <summary>كُلّ الأَنماط المُسَجَّلَة مُسقَطَةً، بِتَرتيب
    /// أَسمائِها، ومَعَها الاحتِياطيّ.</summary>
    public static IReadOnlyDictionary<string, FlowDefinition> All()
    {
        var map = new Dictionary<string, FlowDefinition>(StringComparer.Ordinal);
        foreach (var (name, def) in DealPatternCatalog.Patterns) map[name] = ToFlow(def);
        map[DealPatternCatalog.FallbackPattern] = ToFlow(DealPatternCatalog.Fallback);
        return map;
    }
}
