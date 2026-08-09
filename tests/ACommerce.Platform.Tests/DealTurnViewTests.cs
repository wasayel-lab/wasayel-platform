using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبارات «دَورُكَ الآن» ─────────────────────────────────────────
// DealTurnView طَبَقَة عَرض فَوق DealsPolicy — قِراءَة فَقَط. الخَطَر
// الحَقيقيّ الَّذي تَحرُسُه هذِه الاختِبارات: أَن تَعِد الواجِهَة
// المُستَخدِمَ بِإجراء يَرفُضُه DealsService (أَو تُخفي إجراءً يَملِكُه).
// لِذلِك الاختِبار المِحوَريّ يُقارِن IsMine بِـ DealsPolicy.Actor نَفسِها
// عَبر كُلّ الأَنماط وَكُلّ المَراحِل.

public class DealTurnViewTests
{
    public static TheoryData<string> Patterns => new()
    {
        "trip", "rental", "marketplace", "classifieds", "service",
        "pattern_from_the_future"
    };

    // ─── تَحديد الجانِب ─────────────────────────────────────────────

    [Fact]
    public void SideOf_IdentifiesInitiator_Counterparty_AndOutsider()
    {
        var initiator = Guid.NewGuid();
        var counter   = Guid.NewGuid();
        var stranger  = Guid.NewGuid();

        Assert.Equal(DealSide.Initiator,    DealTurnView.SideOf(initiator, initiator, counter));
        Assert.Equal(DealSide.Counterparty, DealTurnView.SideOf(counter,   initiator, counter));
        Assert.Equal(DealSide.Observer,     DealTurnView.SideOf(stranger,  initiator, counter));
    }

    [Fact]
    public void SideOf_TreatsUnassignedCounterparty_AsObserver()
    {
        var initiator = Guid.NewGuid();
        Assert.Equal(DealSide.Observer,
            DealTurnView.SideOf(Guid.NewGuid(), initiator, counterpartyId: null));
    }

    [Fact]
    public void SideOf_NeverMatchesEmptyGuid()
    {
        // صَفقَة بِلا طَرَف ثانٍ: CounterpartyId = null، وَمُستَخدِم مَجهول
        // (Guid.Empty) يَجِب ألّا يَنزَلِق إلى دَور المُبادِر.
        Assert.Equal(DealSide.Observer,
            DealTurnView.SideOf(Guid.Empty, Guid.Empty, null));
    }

    // ─── الحارِس المِحوَريّ: الواجِهَة = السِّياسَة ────────────────────

    [Theory]
    [MemberData(nameof(Patterns))]
    public void IsMine_MatchesDealsPolicyActor_ForEveryStage(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        {
            var actor    = DealsPolicy.Actor(stage);
            var isLast   = DealsPolicy.Next(pattern, stage) is null;

            var asInitiator = DealTurnView.For(pattern, stage, DealSide.Initiator);
            var asCounter   = DealTurnView.For(pattern, stage, DealSide.Counterparty);
            var asObserver  = DealTurnView.For(pattern, stage, DealSide.Observer);

            // آخِر مَرحَلَة: لا أَحَد لَه دَور — لا تَقَدُّم بَعدَها.
            if (isLast)
            {
                Assert.False(asInitiator.IsMine);
                Assert.False(asCounter.IsMine);
                Assert.True(asInitiator.IsFinished);
                continue;
            }

            Assert.Equal(actor is "initiator" or "either", asInitiator.IsMine);
            Assert.Equal(actor is "counterparty" or "either", asCounter.IsMine);
            Assert.False(asObserver.IsMine);   // الزائِر لا يُحَرِّك شَيئاً
            Assert.Equal(actor, asInitiator.ActorKey);
        }
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void NextStage_MatchesPolicyNext_ForEveryStage(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        {
            var turn = DealTurnView.For(pattern, stage, DealSide.Initiator);
            Assert.Equal(DealsPolicy.Next(pattern, stage), turn.NextStage);
            Assert.Equal(turn.NextStage is null, turn.IsFinished);
        }
    }

    // ─── حالات مَحسوسَة (لِتَوثيق السُّلوك المُتَوَقَّع صَراحَةً) ────────

    [Fact]
    public void Trip_Offered_IsTheRidersTurn_NotTheDrivers()
    {
        // trip: Offered فاعِلُها initiator (الراكِب نَشَرَ الطَلَب).
        var rider  = DealTurnView.For("trip", DealStage.Offered, DealSide.Initiator);
        var driver = DealTurnView.For("trip", DealStage.Offered, DealSide.Counterparty);

        Assert.True(rider.IsMine);
        Assert.False(driver.IsMine);
        Assert.Equal(DealStage.Booked, rider.NextStage);
    }

    [Fact]
    public void Trip_Booked_IsTheDriversTurn()
    {
        Assert.True(DealTurnView.For("trip", DealStage.Booked, DealSide.Counterparty).IsMine);
        Assert.False(DealTurnView.For("trip", DealStage.Booked, DealSide.Initiator).IsMine);
    }

    [Fact]
    public void Confirmed_IsEithersTurn()
    {
        Assert.True(DealTurnView.For("marketplace", DealStage.Confirmed, DealSide.Initiator).IsMine);
        Assert.True(DealTurnView.For("marketplace", DealStage.Confirmed, DealSide.Counterparty).IsMine);
    }

    [Fact]
    public void Classifieds_EndsAtConfirmed_WithNobodysTurn()
    {
        var turn = DealTurnView.For("classifieds", DealStage.Confirmed, DealSide.Initiator);
        Assert.True(turn.IsFinished);
        Assert.Null(turn.NextStage);
        Assert.False(turn.IsMine);
    }

    // ─── تَسمِيات الفاعِلين ─────────────────────────────────────────

    [Theory]
    [InlineData("initiator")]
    [InlineData("counterparty")]
    [InlineData("either")]
    [InlineData("platform")]
    public void ActorLabelAr_IsNonEmpty_ForTheClosedVocabulary(string actor)
    {
        Assert.False(string.IsNullOrWhiteSpace(DealTurnView.ActorLabelAr(actor)));
        foreach (var side in Enum.GetValues<DealSide>())
            Assert.False(string.IsNullOrWhiteSpace(DealTurnView.ActorLabelAr(actor, side)));
    }

    [Fact]
    public void ActorLabelAr_SaysYou_OnlyToTheSideThatActs()
    {
        Assert.Equal("أَنتَ",              DealTurnView.ActorLabelAr("initiator", DealSide.Initiator));
        Assert.Equal("الطَّرَف الآخَر",     DealTurnView.ActorLabelAr("initiator", DealSide.Counterparty));
        Assert.Equal("أَنتَ",              DealTurnView.ActorLabelAr("counterparty", DealSide.Counterparty));
        Assert.Equal("الطَّرَف الآخَر",     DealTurnView.ActorLabelAr("counterparty", DealSide.Initiator));
        // المَنصَّة لا تُصبِح «أَنتَ» أَبَداً.
        Assert.Equal("المَنصَّة",           DealTurnView.ActorLabelAr("platform", DealSide.Initiator));
    }

    [Fact]
    public void ActorLabelAr_ForObserver_UsesNeutralVocabulary()
    {
        Assert.Equal("صاحِب الطَلَب",   DealTurnView.ActorLabelAr("initiator", DealSide.Observer));
        Assert.Equal("الطَّرَف الآخَر",  DealTurnView.ActorLabelAr("counterparty", DealSide.Observer));
        Assert.Equal("أَيّ مِن الطَّرَفَين", DealTurnView.ActorLabelAr("either", DealSide.Observer));
    }

    // ─── شَرح التَدَفُّق يُغَطّي كُلّ مَرحَلَة بِلا نَصّ مَفقود ────────

    [Theory]
    [MemberData(nameof(Patterns))]
    public void EveryStage_HasALabelAndAnActorLabel(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        {
            Assert.False(string.IsNullOrWhiteSpace(DealsPolicy.LabelAr(stage)));
            Assert.False(string.IsNullOrWhiteSpace(
                DealTurnView.ActorLabelAr(DealsPolicy.Actor(stage), DealSide.Observer)));
        }
    }
}
