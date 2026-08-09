using ACommerce.Templates.Customer.Marketplace.Services.Deals;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>مَوقِع المُستَخدِم الحاليّ مِن الصَّفقَة.</summary>
public enum DealSide
{
    /// <summary>صاحِب الطَلَب — مَن بَدَأ الصَّفقَة.</summary>
    Initiator,
    /// <summary>الطَّرَف الآخَر — بائِع/سائِق/مالِك.</summary>
    Counterparty,
    /// <summary>لا طَرَف: زائِر يَقرَأ شَرح التَدَفُّق قَبل أَن يَبدَأ.</summary>
    Observer
}

/// <summary>ما يَحتاج المُستَخدِم مَعرِفَتَه الآن عَن صَفقَة قائِمَة.</summary>
/// <param name="IsMine">هَل الإجراء التالي مَطلوب مِنّي أَنا؟</param>
/// <param name="ActorKey">الفاعِل المُخَوَّل كَما تُعلِنُه
/// <see cref="DealsPolicy.Actor"/> — أَحَد أَربَعَة: initiator |
/// counterparty | either | platform.</param>
/// <param name="NextStage">المَرحَلَة الَّتي يَنتَقِل إلَيها التَقَدُّم،
/// أَو <c>null</c> عِندَ آخِر مَرحَلَة.</param>
/// <param name="IsFinished">لا مَرحَلَة تالِيَة — انتَهى التَدَفُّق.</param>
public sealed record DealTurn(
    bool IsMine,
    string ActorKey,
    DealStage? NextStage,
    bool IsFinished);

/// <summary>
/// <para>طَبَقَة عَرض فَوق <see cref="DealsPolicy"/> — تُجيب سُؤالاً
/// واحِداً: «هَل الدَور دَوري، وَما المَطلوب؟». <b>قِراءَة فَقَط</b>:
/// لا تُعَدِّل السِّياسَة وَلا تُكَرِّر مَراحِلَها وَلا تَحمِل نُسخَة
/// ثانِيَة مِن أَيّ نَصّ مَرحَلَة — كُلّ شَيء يُشتَقّ مِن
/// <c>StagesFor</c>/<c>Next</c>/<c>Actor</c>/<c>LabelAr</c>.</para>
///
/// <para>دَوالّ نَقِيَّة بِلا قاعِدَة بَيانات لِتُختَبَر مُباشَرَةً.</para>
/// </summary>
public static class DealTurnView
{
    /// <summary>يُحَدِّد مَوقِع مُستَخدِم مِن أَطراف الصَّفقَة.</summary>
    public static DealSide SideOf(Guid userId, Guid initiatorId, Guid? counterpartyId)
    {
        if (userId != Guid.Empty && userId == initiatorId) return DealSide.Initiator;
        if (userId != Guid.Empty && counterpartyId is { } c && c == userId) return DealSide.Counterparty;
        return DealSide.Observer;
    }

    /// <summary>
    /// حالَة الدَور عِندَ مَرحَلَة مُعَيَّنَة. الفاعِل المُخَوَّل هو فاعِل
    /// <b>المَرحَلَة الحاليَّة</b> — نَفس القاعِدَة الَّتي يَفرِضُها
    /// <c>DealsService.AdvanceAsync</c>، فَلا تَفتَرِق الواجِهَة عَن
    /// المُحَرِّك.
    /// </summary>
    public static DealTurn For(string pattern, DealStage stage, DealSide side)
    {
        var next  = DealsPolicy.Next(pattern, stage);
        var actor = DealsPolicy.Actor(stage);

        var mine = next is not null && side switch
        {
            DealSide.Initiator    => actor is "initiator" or "either",
            DealSide.Counterparty => actor is "counterparty" or "either",
            _                     => false
        };

        return new DealTurn(mine, actor, next, next is null);
    }

    /// <summary>تَسمِيَة الفاعِل بِلا مَنظور — لِشَرح التَدَفُّق العامّ.</summary>
    public static string ActorLabelAr(string actorKey) => actorKey switch
    {
        "initiator"    => "صاحِب الطَلَب",
        "counterparty" => "الطَّرَف الآخَر",
        "either"       => "أَيّ مِن الطَّرَفَين",
        "platform"     => "المَنصَّة",
        _              => actorKey
    };

    /// <summary>تَسمِيَة الفاعِل مِن مَنظور المُشاهِد — «أَنتَ» حينَ
    /// يَنطَبِق، وَإلّا «الطَّرَف الآخَر».</summary>
    public static string ActorLabelAr(string actorKey, DealSide side) => (actorKey, side) switch
    {
        ("platform", _)                          => "المَنصَّة",
        (_, DealSide.Observer)                   => ActorLabelAr(actorKey),
        ("either", _)                            => "أَنتَ أَو الطَّرَف الآخَر",
        ("initiator", DealSide.Initiator)        => "أَنتَ",
        ("counterparty", DealSide.Counterparty)  => "أَنتَ",
        ("initiator", DealSide.Counterparty)     => "الطَّرَف الآخَر",
        ("counterparty", DealSide.Initiator)     => "الطَّرَف الآخَر",
        _                                        => ActorLabelAr(actorKey)
    };
}
