using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَبولٌ عَلى لَقطَةٍ قَديمَة — تَوصيفُ «اليَوم» قَبلَ إعادَةِ الفَحصِ في جَلسَةِ الكِتابَة ═══
//
// مُراجَعَةُ ‏2026-10-11 عَلى `8769fa4` (البَندُ الكَبيرُ الأَوَّل): `DealsService.AcceptAsync`
// تَفحَصُ **لَقطَةً** مِن `QuerySession` (`Validate(loaded, by)`)، ثُمَّ تُعَيِّنُ في جَلسَةٍ
// ثانِيَة، ثُمَّ تُحَمِّلُ في جَلسَةِ كِتابَةٍ ثالِثَةٍ وتُنادي `MoveAsync` **بِلا إعادَةِ فَحص**.
// فَكِتابَةٌ تَقَعُ بَينَ اللَقطَةِ وتَحميلِ الكِتابَةِ لا تَراها البَوّابَة:
//   • إلغاءُ المُشتَري ← يُكتَبُ `advanced` فَوقَ صَفقَةٍ `Cancelled` ويُرجَعُ `Accepted`، فَتُرسِلُ
//     `/accept` لِلمُشتَري «قَبِلَ البائِعُ طَلَبَكَ» عَن طَلَبٍ أَلغاه.
//   • قَبولٌ ثانٍ (نَقرَةٌ مُزدَوِجَة) ← تَمضي الصَفقَةُ ثانِيَةً `Booked → Confirmed` بِمُلاحَظَةِ
//     «قُبِلَ العَرض»، والقَبولانِ `Ok` فَإشعاران.
// وكانَ المَسارُ القَديمُ (‏`AdvanceAsync` داخِلَ القَبول) يَفحَصُ `Status != Active` في جَلسَتِه
// فَيَرفُضُ — ويُهمِلُ النَتيجَة؛ والجَديدُ لا يُهمِلُ نَتيجَةً لَكِنَّه **يَكتُب**.
//
// **المَقيسُ قَبلَ هذا المِلَفّ** (مِجَسٌّ خارِجَ المُستَودَعِ عَلى Postgres المَحَلِّيّ، ‏100
// مُحاوَلَةٍ مُتَزامِنَةٍ لِكُلِّ مَشهَد، مُستَأجِرٌ عَشوائِيٌّ نُظِّفَ بَعدَه): قَبولٌ ‖ إلغاء ←
// `advanced` بَعدَ `cancelled` في 15/100 (والمُراجِعُ قاسَ 7/100 — والفَرقُ تَوقيت)؛ قَبولٌ ‖
// قَبول ← `Ok` مَرَّتَينِ في 100/100، ومَرحَلَةٌ مُضاعَفَةٌ (`Confirmed`) في 1/100. والسِباقُ
// الحَقيقِيُّ حَظّ — فَلا يُثَبَّتُ بِه شَيء.
//
// **ولِماذا عالَمُ الوَثائِقِ في الذاكِرَة** (`EndpointDocWorld.cs`؛ القاعِدَة ٨ — الأَداةُ القائِمَة
// بِخُطّافٍ اختِيارِيٍّ لا أَداةٌ رابِعَة): يُدخِلُ الكِتابَةَ الثالِثَةَ **في المَوضِعِ نَفسِه كُلَّ
// مَرَّة** — قَبلَ الجَلسَةِ الكاتِبَةِ الثانِيَة، أَي بَعدَ جَلسَةِ التَعيينِ وقَبلَ جَلسَةِ
// الانتِقال — فَيَصيرُ السِباقُ تَرتيباً حَتمِيّاً يَعمَلُ في CI بِلا قاعِدَة. والخِدمَةُ هي هي
// (`DealsService` الحَقيقِيَّة)، والإلغاءُ والقَبولُ الثاني يَمُرّانِ بِبابَيهِما الحَقيقِيَّين.
//
// **وما لا يَقيسُه هذا المِلَفّ — ويُقال**: كِتابَةٌ تَقَعُ **بَعدَ** تَحميلِ جَلسَةِ الكِتابَةِ
// وقَبلَ حِفظِها تَضيعُ بِآخِرِ كاتِب (لا تَزامُنَ تَفاؤُلِيَّ عَلى `Deal`) — والعالَمُ في الذاكِرَةِ
// يَتَشارَكُ المَرجِعَ فَلا يُمَثِّلُها. تِلكَ مُراجَعَةٌ مَعلومَةٌ لِمَوجَةٍ لاحِقَةٍ بِقَرارِها.
//
// **وما يُقلَبُ وما لا يُقلَب** (القاعِدَة ٣): صَفّا «اليَوم» يُقلَبانِ بِاسمِهِما في كوميتِ
// الإصلاح؛ وصَفُّ الكِتابَةِ الَّتي لا تَمَسُّ ما تَفحَصُه البَوّابَةُ يَبقى بِحَرفِه — إعادَةُ الفَحصِ
// لا تَرُدُّ قَبولاً سَليماً.

public class DealAcceptStaleSnapshotCharacterizationTests
{
    private const string Slug = "zz-stale-snapshot";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفقَةُ النَقدِ لا تَمَسُّ المالَ في القَبولِ ولا
    /// في الإلغاء، فَإن مَسَّته أَحمَرَّ الصَفُّ بَدَلَ أَن يُحاكى.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("القَبولُ والإلغاءُ لِصَفقَةِ نَقدٍ لا يَمَسّانِ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه، فَالنِداءُ مُكتَمِلٌ عِندَ العَودَة —
    /// وإن لَم يَكتَمِل فَالأَداةُ تَكذِب، فَيَحمَرُّ هُنا بَدَلَ أَن يَعلَق.</summary>
    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ كَما يُولِدُها <c>/checkout/submit</c> — بِالخِدمَةِ نَفسِها:
    /// <c>StartAsync</c> ثُمَّ مَرجِعُ المالِكِ ثُمَّ رَبطُه طَرَفاً (<c>bound</c>). و
    /// <paramref name="boundAtBirth"/> = <c>false</c> عَرضٌ عَلى إعلانٍ بِلا رَبط، يُعَيِّنُ القَبولُ
    /// فيه القابِلَ طَرَفاً.</summary>
    private static (DealsService Deals, DocWorld World, Guid Id) CashCartDeal(bool boundAtBirth = true)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        if (boundAtBirth) Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return (deals, world, deal.Id);
    }

    /// <summary>يُدخِلُ <paramref name="between"/> مَرَّةً واحِدَةً قَبلَ <b>الجَلسَةِ الكاتِبَةِ
    /// الثانِيَة</b> بَعدَ التَركيب — في <c>AcceptAsync</c>: بَعدَ جَلسَةِ التَعيينِ وقَبلَ جَلسَةِ
    /// الانتِقال، أَي بَعدَ أَن فُحِصَت اللَقطَة. ويُرجِعُ عَدّاداً يُثبِتُ أَنّه وَقَع: إن لَم يَقَع
    /// فَالأَداةُ عَمياءُ ويَحمَرُّ الصَفّ (القاعِدَة ١٠). وجَلَساتُ ما يَقَعُ بَينَهُما لا تُعَدّ.</summary>
    private static Func<int> Between(DocWorld world, Action between)
    {
        var writers = 0;
        var fired = 0;
        world.BeforeMember = name =>
        {
            if (name != "LightweightSession" || fired > 0) return;
            if (++writers < 2) return;
            fired++;
            between();
        };
        return () => fired;
    }

    private static DealAcceptor BySeller => new(Seller, "بائِع");

    // ═══ ١) «اليَوم» — يُقلَبانِ بِاسمِهِما في كوميتِ الإصلاح ═══════════════════════

    /// <summary><b>اليَوم</b>: يُلغي المُشتَري بَعدَ أَن فُحِصَت لَقطَةُ القَبولِ وقَبلَ جَلسَةِ
    /// الانتِقال — فَيُكتَبُ <c>advanced</c> فَوقَ الصَفقَةِ المُلغاة (حالَتُها <c>Cancelled</c>
    /// ومَرحَلَتُها <c>Booked</c>)، ويُرجَعُ <c>Accepted</c> فَيُشعَرُ المُشتَري بِقَبولِ ما أَلغاه.
    /// وهُوَ سِجِلُّ المِجَسِّ الحَيِّ بِحَرفِه: <c>[created, note, bound, cancelled, advanced]</c>.</summary>
    [Fact]
    public void Today_a_buyer_cancel_between_the_snapshot_and_the_write_session_gets_advanced_written_onto_the_cancelled_deal_and_Ok()
    {
        var (deals, world, id) = CashCartDeal();
        DealCancelResult? cancel = null;
        var fired = Between(world, () =>
            cancel = Now(deals.CancelAsync(Slug, id, new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ عَن الطَلَب")));

        var accept = Now(deals.AcceptAsync(Slug, id, BySeller));

        Assert.Equal(1, fired());
        Assert.True(cancel!.Ok);
        Assert.True(accept.Ok);
        Assert.Null(accept.Violation);
        var after = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, after.Status);
        Assert.Equal(DealStage.Booked, after.Stage);
        Assert.Equal(new[] { "created", "note", "bound", "cancelled", "advanced" },
            after.Timeline.Select(e => e.Action).ToArray());
        Assert.Equal(Seller, after.Timeline[^1].ActorId);
    }

    /// <summary><b>اليَوم</b>: نَقرَةٌ ثانِيَةٌ مِن البائِعِ نَفسِه — فُحِصَت لَقطَتُها في
    /// <c>Offered</c>، ثُمَّ أَتَمَّت الأولى قَبولَها كُلَّه قَبلَ جَلسَةِ انتِقالِها — فَتَمضي الصَفقَةُ
    /// ثانِيَةً <c>Booked → Confirmed</c> بِمُلاحَظَةِ «قُبِلَ العَرض»، والقَبولانِ <c>Ok</c> (فَإشعاران
    /// لِلمُشتَري مِن <c>/accept</c>). مِرآةُ الحالَةِ الواحِدَةِ مِن مِئَةٍ في المِجَسّ.</summary>
    [Fact]
    public void Today_a_second_accept_that_lands_between_the_first_snapshot_and_its_write_session_moves_the_deal_again_and_both_are_Ok()
    {
        var (deals, world, id) = CashCartDeal();
        DealAcceptResult? landed = null;
        var fired = Between(world, () => landed = Now(deals.AcceptAsync(Slug, id, BySeller)));

        var stale = Now(deals.AcceptAsync(Slug, id, BySeller));

        Assert.Equal(1, fired());
        Assert.True(landed!.Ok);
        Assert.True(stale.Ok);
        Assert.Null(stale.Violation);
        var after = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Active, after.Status);
        Assert.Equal(DealStage.Confirmed, after.Stage);
        var moves = after.Timeline.Where(e => e.Action == "advanced").ToArray();
        Assert.Equal(2, moves.Length);
        Assert.Equal((DealStage.Offered, DealStage.Booked), (moves[0].StageBefore, moves[0].StageAfter));
        Assert.Equal((DealStage.Booked, DealStage.Confirmed), (moves[1].StageBefore, moves[1].StageAfter));
        Assert.All(moves, m => Assert.Equal("قُبِلَ العَرض", m.Note));
    }

    // ═══ ٢) ما لا يُقلَب — كِتابَةٌ بَينَ الجَلسَتَينِ لا تَمَسُّ ما تَفحَصُه البَوّابَة ═════════

    /// <summary><b>المُوجِب، ويَبقى بِحَرفِه بَعدَ الإصلاح</b>: كِتابَةٌ في المَوضِعِ نَفسِه لا
    /// تُغَيِّرُ حالَةً ولا مَرحَلَةً ولا طَرَفاً (مَرجِعٌ يُلحَقُ بِالصَفقَة) لا تَمنَعُ قَبولاً — في
    /// صَفقَةِ السَلَّةِ المَربوطَةِ عِندَ الوِلادَة، وفي العَرضِ بِلا طَرَفٍ حَيثُ يُعَيِّنُ القَبولُ
    /// نَفسُه القابِلَ طَرَفاً قَبلَ جَلسَةِ الانتِقال.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_write_between_the_snapshot_and_the_write_session_that_changes_nothing_the_gate_reads_does_not_stop_the_accept(bool boundAtBirth)
    {
        var (deals, world, id) = CashCartDeal(boundAtBirth);
        var fired = Between(world, () =>
            Now(deals.AttachRefAsync(Slug, id, "buyer_note", "اتَّصِل قَبلَ الوُصول")));

        var accept = Now(deals.AcceptAsync(Slug, id, BySeller));

        Assert.Equal(1, fired());
        Assert.True(accept.Ok, accept.Violation?.Code);
        var after = world.Read<Deal>(id.ToString())!;
        Assert.Equal(DealStatus.Active, after.Status);
        Assert.Equal(DealStage.Booked, after.Stage);
        Assert.Equal(Seller, after.CounterpartyId);
        var moves = after.Timeline.Where(e => e.Action == "advanced").ToArray();
        Assert.Single(moves);
        Assert.Equal(Seller, moves[0].ActorId);
    }
}
