using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «سُلِّمَ» عَلى صَفقَةِ نَقدٍ في «نِزاع» يُكتَب، فَحَسمُها إلغاءً يَكتُبُ رَدَّ النَقدِ مُستَحَقّاً (‏2026-11-05، الموجَة ١٢ G3 · ٤ — D-1؛ تَوصيفُه `b3da2d7`) ═══
//
// **القَرار** (د38 (٢) «إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة»؛ P1): الوارِدُ يُلحِقُ `oto_delivered_while_disputed` مَرَّةً بِأُنبوبِ الإلحاقِ وشَرطِه عَلى المَخزون —
// والصَفقَةُ لا تَتَحَرَّكُ والجَوابُ ‏200 كَما كان — و`DealsPolicy.CollectedInCash` يَقرَؤُه فَيَكتُبُ فَرعُ الإلغاءِ `cash_refund_due` ويُسَمّيه `OwnerActionOf` لِصاحِبِ المَتجَر.
// ومِلَفُّ التَوصيفِ `OtoDeliveredWhileDisputedTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class OtoDeliveredWhileDisputedSwitchTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;
    private static readonly DealCanceller Owner = new(OtoDeliveredWhileDisputedTests.Owner, "مالِك التَّطبيق", IsStoreAdmin: true);

    /// <summary><b>المُوجِب</b>: «سُلِّمَ» يُلحَقُ مَرَّةً (حِفظٌ واحِدٌ وسَطرُ إلحاقٍ واحِدٌ داخِلِيّ) والصَفقَةُ في «نِزاع» عِندَ «شَحن»، وإعادَتُه لا تَكتُب؛ ثُمَّ الحَسمُ إلغاءً يَكتُبُ
    /// <c>cash_refund_due</c> بِفاعِلِه صاحِبِ المَتجَر، فَيُخبَرُ صاحِبُ المَتجَرِ «رَدٌّ نَقديٌّ مُستَحَقّ» مَرَّةً ويُخبَرُ الطَرَفانِ بِالحَسم.</summary>
    [Fact]
    public async Task A_delivery_while_disputed_is_recorded_once_and_a_cancel_resolution_writes_the_cash_refund_due_and_tells_the_store_owner()
    {
        var w = await OtoDeliveredWhileDisputedTests.DisputedShippedCashDeal();
        OtoDeliveredWhileDisputedTests.OtoSays(w, "delivered");
        var (saves, lines) = (w.Docs.SaveCalls, Stored(w).Timeline.Count);

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));
        var (savedOnce, linedOnce) = (w.Docs.SaveCalls - saves, Stored(w).Timeline.Count - lines);
        var again = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));

        Assert.Equal((OtoOrderStatusGate.DealNotActive, 200), (outcome.Code, OtoOrderStatusGate.StatusFor(outcome.Code)));
        Assert.Equal(OtoOrderStatusGate.DealNotActive, again.Code);
        Assert.Equal((1, 1), (savedOnce, linedOnce));
        Assert.Equal(saves + 1, w.Docs.SaveCalls);
        Assert.Equal("delivered", Stored(w).Refs[DealShipping.DeliveredWhileDisputedRef]);
        Assert.Null(DealRefLabels.LabelKeyFor(Stored(w).Timeline[^1].Note));
        Assert.Equal((DealStage.Shipping, DealStatus.Disputed), (Stored(w).Stage, Stored(w).Status));
        Assert.True(DealsPolicy.CollectedInCash(Stored(w)));

        var l = new L(new HttpContextAccessor());
        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Id, Owner, DealDisputeResolution.Cancel, "حَسم");
        Assert.True(res.Ok, res.Violation?.Code);
        await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyDisputeResolvedAsync(Slug, res.Deal!, Owner.ActorId, res.Outcome!, l);

        var toOwner = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == Owner.ActorId).ToArray();
        var due = Assert.Single(Stored(w).Timeline, e => e.Action == DealEvent.CashRefundDue);
        output.WriteLine($"الوارِد: {outcome.Code} → 200 · حِفظات +{savedOnce} · أَسطُر +{linedOnce} · الإعادَة +0 · الحَسم: {Stored(w).Status} · cash_refund_due=1 · " +
                         $"ما عَلى صاحِبِ المَتجَر: {DealNotices.OwnerActionOf(Stored(w))} · لِصاحِبِ المَتجَر: {toOwner.Length} «{toOwner.FirstOrDefault()?.Title}»");
        Assert.Equal(Owner.ActorId, due.ActorId);
        Assert.Equal(DealNotices.OwnerAction.CashRefundDue, DealNotices.OwnerActionOf(Stored(w)));
        var told = Assert.Single(toOwner);
        Assert.Equal(l["notifications.deal_cash_refund_due.title"], told.Title);
    }

    /// <summary><b>السالِب</b>: الحَسمُ استِئنافاً لا يَكتُبُ رَدّاً — الصَفقَةُ قائِمَةٌ عِندَ «شَحن» والمَرجِعُ باقٍ، ولا شَيءَ لِصاحِبِ المَتجَر.</summary>
    [Fact]
    public async Task A_resume_resolution_after_a_delivery_while_disputed_writes_no_refund_line()
    {
        var w = await OtoDeliveredWhileDisputedTests.DisputedShippedCashDeal();
        OtoDeliveredWhileDisputedTests.OtoSays(w, "delivered");
        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));

        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Id, Owner, DealDisputeResolution.Resume, "استِئناف");

        Assert.True(res.Ok, res.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (Stored(w).Status, Stored(w).Stage));
        Assert.DoesNotContain(Stored(w).Timeline, e => e.Action == DealEvent.CashRefundDue);
        Assert.Null(DealNotices.OwnerActionOf(Stored(w)));
    }

    /// <summary>الدالَّةُ النَقِيَّة — مُوجَبٌ وسالِب: «نِزاع» ورَدُّ <c>deal_not_active</c> بِلا المَرجِعِ ⇒ نَعَم؛ والمُلغاةُ (لِأُختِها <c>DeliveredAfterCancel</c>)، والقائِمَة، والمَرجِعُ مَكتوبٌ قَبلُ،
    /// ورَمزٌ آخَرُ ⇒ لا.</summary>
    [Fact]
    public void DeliveredWhileDisputed_positive_and_negative()
    {
        var notActive = new OtoNoticeViolation(OtoOrderStatusGate.DealNotActive, "x");
        static Deal With(DealStatus status, bool recorded = false)
        {
            var d = new Deal { Id = Guid.NewGuid(), Status = status, Stage = DealStage.Shipping };
            if (recorded) d.Refs[DealShipping.DeliveredWhileDisputedRef] = "delivered";
            return d;
        }
        Assert.True(OtoOrderStatusGate.DeliveredWhileDisputed(With(DealStatus.Disputed), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredWhileDisputed(With(DealStatus.Cancelled), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredWhileDisputed(With(DealStatus.Active), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredWhileDisputed(With(DealStatus.Disputed, recorded: true), notActive));
        Assert.False(OtoOrderStatusGate.DeliveredWhileDisputed(With(DealStatus.Disputed), new OtoNoticeViolation(OtoOrderStatusGate.NotDelivered, "x")));
    }

    /// <summary><b><see cref="DealsPolicy.CollectedInCash"/> — مُوجَبٌ وسالِبٌ لِلشَرطِ الجَديد</b>: نَقدٌ بِالمَرجِعِ ⇒ قُبِض؛ وبِطاقَةٌ بِالمَرجِعِ، ونَقدٌ بِلا مَرجِعٍ ولا سَطرٍ ⇒ لا؛
    /// وسَطرُ <c>cash_on_delivery</c> كَما كان.</summary>
    [Theory]
    [InlineData("cash", true, false, true)]
    [InlineData("cash", false, true, true)]
    [InlineData("cash", false, false, false)]
    [InlineData("card", true, false, false)]
    public void CollectedInCash_reads_the_delivered_while_disputed_ref_for_cash_deals_only(string pay, bool recorded, bool cashLine, bool expected)
    {
        var d = new Deal { Id = Guid.NewGuid(), Status = DealStatus.Disputed, Stage = DealStage.Shipping };
        d.Attributes[CheckoutPayMethods.AttributeKey] = pay == "cash" ? CheckoutPayMethods.CashOnDelivery : CheckoutPayMethods.Card;
        if (recorded) d.Refs[DealShipping.DeliveredWhileDisputedRef] = "delivered";
        if (cashLine) d.Timeline.Add(new(DealStage.Delivered, DealStage.Delivered, DealEvent.CashOnDelivery, null, null, null, DateTime.UtcNow));
        Assert.Equal(expected, DealsPolicy.CollectedInCash(d));
    }
}
