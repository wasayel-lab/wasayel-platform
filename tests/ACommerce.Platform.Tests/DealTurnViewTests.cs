using System.Text;
using ACommerce.Platform.I18n;
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
//
// **ثُمَّ قُلِبَت أَربَعَةُ صُفوفٍ بِأَسمائِها** (‏ADR-033 §٦‑ط، ‏2026-10-11؛ ولا يُعادُ حَرفٌ فَوقَه):
// المُحَرِّكُ صارَ يَسأَلُ `DealsPolicy.RequiredActorToLeave` — فاعِلَ المَرحَلَةِ المَدخولَة لا
// الحالِيَّة — فَالحارِسُ المِحوَريُّ يُقارِنُ بِها هِيَ (والغايَةُ هي هي: الواجِهَةُ = المُحَرِّك)،
// والحالاتُ المَحسوسَةُ الثَلاثُ كانَت تُثَبِّتُ الإزاحَةَ نَفسَها: «الراكِبُ نَشَرَ الطَلَبَ فَدَورُه»
// قَرَأَت فاعِلَ `Offered` مَن أَدخَلَها ثُمَّ أَعطَته الخُروجَ مِنها.

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

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ <c>IsMine_MatchesDealsPolicyActor_ForEveryStage</c>
    /// يُقارِنُ بِـ<c>DealsPolicy.Actor(stage)</c> — فاعِلِ المَرحَلَةِ الحالِيَّة، وهُوَ ما كانَ يَسأَلُه
    /// المُحَرِّك. وصارَ المُحَرِّكُ يَسأَلُ فاعِلَ المَرحَلَةِ المَدخولَة، فَيُقارَنُ بِه هُوَ — الغايَةُ نَفسُها:
    /// لا تَعِدُ الواجِهَةُ بِما يَرفُضُه <c>DealsService</c>.</summary>
    [Theory]
    [MemberData(nameof(Patterns))]
    public void IsMine_MatchesTheActorRequiredToLeave_ForEveryStage(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        {
            var actor    = DealsPolicy.RequiredActorToLeave(pattern, stage, payMethod: null);
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

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ <c>Trip_Offered_IsTheRidersTurn_NotTheDrivers</c>
    /// — «Offered فاعِلُها initiator (الراكِب نَشَرَ الطَلَب)» ثُمَّ الدَورُ لَه: قَرَأَ الفاعِلَ مَن أَدخَلَها
    /// وأَعطاه الخُروجَ مِنها. والخُروجُ مِن <c>Offered</c> دُخولُ <c>Booked</c> — «سائِق يَقبَل» بِحَرفِ تَعليقِ
    /// النَمَطِ في الكاتالوج — فَالدَورُ لِلسائِقِ والراكِبُ يَنتَظِر.</summary>
    [Fact]
    public void Trip_Offered_IsTheDriversTurn_ToTakeIt_NotTheRiders()
    {
        var rider  = DealTurnView.For("trip", DealStage.Offered, DealSide.Initiator);
        var driver = DealTurnView.For("trip", DealStage.Offered, DealSide.Counterparty);

        Assert.True(driver.IsMine);
        Assert.False(rider.IsMine);
        Assert.Equal(DealStage.Booked, driver.NextStage);
        Assert.Equal("counterparty", driver.ActorKey);
    }

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦‑ط): كانَ <c>Trip_Booked_IsTheDriversTurn</c> — فاعِلُ <c>Booked</c>
    /// (السائِقُ يَقبَل) يُخرِجُها. والخُروجُ مِنها دُخولُ <c>Confirmed</c> — «بَدء الرَّحلَة»، وفاعِلُها
    /// <c>either</c> — فَالدَورُ لِلطَرَفَين.</summary>
    [Fact]
    public void Trip_Booked_IsEithersTurn_ToStartTheTrip()
    {
        Assert.True(DealTurnView.For("trip", DealStage.Booked, DealSide.Counterparty).IsMine);
        Assert.True(DealTurnView.For("trip", DealStage.Booked, DealSide.Initiator).IsMine);
        Assert.Equal(DealStage.Confirmed, DealTurnView.For("trip", DealStage.Booked, DealSide.Initiator).NextStage);
    }

    /// <summary><b>قُلِبَ</b> (‏ADR-033 §٦‑ط): كانَ <c>Confirmed_IsEithersTurn</c> — فاعِلُ <c>Confirmed</c>
    /// يُخرِجُها، فَالبائِعُ يُدخِلُ صَفقَةَ البِطاقَةِ «دَفع» عَن المُشتَري (ويَقبِضُ بِنَقرَتِه). والخُروجُ
    /// مِنها دُخولُ <c>Paid</c> وفاعِلُها <c>initiator</c> — المُشتَري يَدفَع، والبائِعُ يَنتَظِر.</summary>
    [Fact]
    public void Marketplace_Confirmed_IsTheBuyersTurn_ToPay_AndTheSellerWaits()
    {
        var buyer  = DealTurnView.For("marketplace", DealStage.Confirmed, DealSide.Initiator);
        var seller = DealTurnView.For("marketplace", DealStage.Confirmed, DealSide.Counterparty);
        Assert.True(buyer.IsMine);
        Assert.False(seller.IsMine);
        Assert.Equal(DealStage.Paid, buyer.NextStage);
        Assert.Equal("initiator", seller.ActorKey);
    }

    [Fact]
    public void Classifieds_EndsAtConfirmed_WithNobodysTurn()
    {
        var turn = DealTurnView.For("classifieds", DealStage.Confirmed, DealSide.Initiator);
        Assert.True(turn.IsFinished);
        Assert.Null(turn.NextStage);
        Assert.False(turn.IsMine);
    }

    // ─── تَسمِيات الفاعِلين — مَفاتيح لا جُمَل ────────────────────────
    //
    // ‏ADR-001، الخِيار (د). كانَت هذِه الاختِبارات تُؤَكِّد **الجُملَة
    // العَرَبِيَّة** الَّتي تُرجِعُها الدالَّة؛ وصارَت تُؤَكِّد
    // **المِفتاح**. والجُملَةُ لَم تَسقُط مِن الميزان: يَحرُسُها
    // <c>MigratedActorLabels_AreByteIdentical_…</c> أَدناه، وهو يُقابِل
    // قيمَةَ القامُوس بِالحَرفِيَّةِ الَّتي كانَت في الكود **بايتاً
    // بايتاً** — فَتَشكيلٌ يَسقُط في النَقل يُحمِرُّ فَوراً بَدَل أَن
    // يَمُرّ صامِتاً على الشاشَة (القاعِدَة ١١: «كُلّ دَفعَة تَرحيل
    // تُبرهَن بِمُقارَنَة بايتيَّة»).

    [Theory]
    [InlineData("initiator")]
    [InlineData("counterparty")]
    [InlineData("either")]
    [InlineData("platform")]
    public void ActorLabelKey_IsAlwaysInTheClosedLexicon_ForTheKnownVocabulary(string actor)
    {
        Assert.Contains(DealTurnView.ActorLabelKey(actor), LocaleCatalog.Lexicon);
        foreach (var side in Enum.GetValues<DealSide>())
            Assert.Contains(DealTurnView.ActorLabelKey(actor, side), LocaleCatalog.Lexicon);
    }

    [Fact]
    public void ActorLabelKey_SaysYou_OnlyToTheSideThatActs()
    {
        Assert.Equal("deals.actor.you",          DealTurnView.ActorLabelKey("initiator", DealSide.Initiator));
        Assert.Equal("deals.actor.counterparty", DealTurnView.ActorLabelKey("initiator", DealSide.Counterparty));
        Assert.Equal("deals.actor.you",          DealTurnView.ActorLabelKey("counterparty", DealSide.Counterparty));
        Assert.Equal("deals.actor.counterparty", DealTurnView.ActorLabelKey("counterparty", DealSide.Initiator));
        // المَنصَّة لا تُصبِح «أَنتَ» أَبَداً.
        Assert.Equal("deals.actor.platform",     DealTurnView.ActorLabelKey("platform", DealSide.Initiator));
        Assert.Equal("deals.actor.you_or_other", DealTurnView.ActorLabelKey("either", DealSide.Initiator));
    }

    [Fact]
    public void ActorLabelKey_ForObserver_UsesNeutralVocabulary()
    {
        Assert.Equal("deals.actor.initiator",    DealTurnView.ActorLabelKey("initiator", DealSide.Observer));
        Assert.Equal("deals.actor.counterparty", DealTurnView.ActorLabelKey("counterparty", DealSide.Observer));
        Assert.Equal("deals.actor.either",       DealTurnView.ActorLabelKey("either", DealSide.Observer));
    }

    /// <summary>الفاعِلُ المَجهولُ يَسقُط إلى نَفسِه — تَماماً كَما كانَ
    /// قَبلَ التَرحيل: <c>L[x]</c> لِمِفتاحٍ خارِجَ القامُوس يُرجِع
    /// <c>x</c> حَرفاً، فَالسُلوكُ المَرئيّ لَم يَتَبَدَّل.</summary>
    [Fact]
    public void ActorLabelKey_ForAnUnknownActor_FallsBackToTheActorItself()
    {
        Assert.Equal("wormhole", DealTurnView.ActorLabelKey("wormhole"));
        Assert.Equal("wormhole", DealTurnView.ActorLabelKey("wormhole", DealSide.Initiator));
        Assert.Equal("wormhole", DealTurnView.ActorLabelKey("wormhole", DealSide.Observer));
        Assert.Equal("wormhole", LocaleCatalog.Text(LocaleCatalog.Arabic, "wormhole"));
    }

    // ─── حارِسُ التَطابُقِ البايتيّ ──────────────────────────────────
    //
    // الحَرفِيّاتُ أَدناه **مَنسوخَةٌ آليّاً** مِن نُسخَةِ الكود قَبلَ
    // التَرحيل (‏git show HEAD:…/DealTurnView.cs) لا مُعادَ كِتابَتُها —
    // وإعادَةُ الكِتابَة هي بِعَينِها ما يُسقِط التَشكيل.

    [Theory]
    [InlineData("deals.actor.initiator",    "صاحِب الطَلَب")]
    [InlineData("deals.actor.counterparty", "الطَّرَف الآخَر")]
    [InlineData("deals.actor.either",       "أَيّ مِن الطَّرَفَين")]
    [InlineData("deals.actor.platform",     "المَنصَّة")]
    [InlineData("deals.actor.you",          "أَنتَ")]
    [InlineData("deals.actor.you_or_other", "أَنتَ أَو الطَّرَف الآخَر")]
    public void MigratedActorLabels_AreByteIdentical_ToThePreMigrationLiterals(
        string key, string preMigrationLiteral)
    {
        var fromCatalog = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.NotNull(fromCatalog);
        Assert.Equal(
            Encoding.UTF8.GetBytes(preMigrationLiteral),
            Encoding.UTF8.GetBytes(fromCatalog!));
    }

    // ─── شَرح التَدَفُّق يُغَطّي كُلّ مَرحَلَة بِلا نَصّ مَفقود ────────

    [Theory]
    [MemberData(nameof(Patterns))]
    public void EveryStage_HasALabelAndAnActorLabel(string pattern)
    {
        foreach (var stage in DealsPolicy.StagesFor(pattern))
        {
            Assert.False(string.IsNullOrWhiteSpace(DealsPolicy.LabelAr(stage)));
            // المِفتاحُ لا يَكفي: يُحَلّ مِن القامُوس كَما يَفعَل razor،
            // فَمِفتاحٌ بِلا مَدخَلَة يَظهَر خاماً على الشاشَة — وذاك
            // ما يُمسِكُه <c>Assert.Contains</c> في المُعجَم.
            var key = DealTurnView.ActorLabelKey(DealsPolicy.Actor(stage), DealSide.Observer);
            Assert.Contains(key, LocaleCatalog.Lexicon);
            Assert.False(string.IsNullOrWhiteSpace(
                LocaleCatalog.Text(LocaleCatalog.Arabic, key)));
        }
    }
}
