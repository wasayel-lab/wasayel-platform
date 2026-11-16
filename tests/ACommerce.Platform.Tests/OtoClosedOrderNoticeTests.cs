using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبٌ أَغلَقَته OTO نَفسُها — أَيَبقى يَحبِسُ إلغاءَ الطَرَف؟ (‏2026-10-30، تَدقيقُ الموجَة ١١ F3 — د37) ════════════════════════════════════════
//
// **الحالُ المَقيسَة**: `DealShipping.HoldsLiveOrder` = طَلَبٌ عِندَ OTO و`!ClosedAtOto` — و`ClosedRef` (‏`oto_closed_status`) لا يَكتُبُه إلّا `ShipAsync` حينَ يَسأَلُ
// OTO فَتُجيبُ بِحالٍ نِهائِيّ. فَبائِعٌ أَلغى الطَلَبَ مِن لَوحَتِه عِندَ OTO (مِثالُ «أ» S4·2)، وأَرسَلَت OTO `orderStatus=canceled`، يُجابُ الوارِدُ `not_delivered` ‏200
// ولا يُكتَبُ شَيء؛ والمُشتَري والبائِعُ يُرَدّانِ `deal_past_cancel_stage` والشَريحَةُ تَقولُ «طَلَبٌ قائِم» — حَتّى نَقرَةِ «أَكمِل الشَّحن» لا يُقالُ لِأَحَدٍ أَن يَنقُرَها.

public class OtoClosedOrderNoticeTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string OtoId = "540789";

    /// <summary>صَفقَةُ نَقدٍ عِندَ «تأكيد» بِطَلَبِها عِندَ OTO (مَرجِعا الطَلَبِ والمُعَرِّف)، وOTO تُجيبُ عَن طَلَبِها بِـ<paramref name="status"/>.</summary>
    private static async Task<DealShipTests.World> PlacedAtConfirmed(string status)
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, OtoId);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OtoId, status, label: null);
        return w;
    }

    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>97167af</c> — كانَ <c>Today_a_canceled_order_notice_writes_nothing_and_the_party_cancel_stays_refused</c>): OTO تَقولُ
    /// «canceled» عَن طَلَبِ صَفقَةٍ عِندَ «تأكيد» — الوارِدُ <c>not_delivered</c> كَما كان <b>ويَكتُبُ <c>oto_closed_status</c></b>، فَلا «طَلَبَ حَيّاً» وإلغاءُ المُشتَري
    /// والبائِعِ يُقبَلانِ (مُوجِب)؛ وإعادَةُ الإشعارِ لا تَكتُبُ سَطراً ثانِياً.</summary>
    [Fact]
    public async Task A_canceled_order_notice_closes_the_order_and_the_party_cancel_is_allowed()
    {
        var w = await PlacedAtConfirmed("canceled");

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, OtoId, status: "canceled"));
        var lines = Stored(w).Timeline.Count;
        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, OtoId, status: "canceled"));

        var deal = Stored(w);
        var buyer = DealCancelAuthorization.Validate(deal, new DealCanceller(DealShipTests.Buyer, "مُشتَرٍ", IsStoreAdmin: false))?.Code;
        var vendor = DealCancelAuthorization.Validate(deal, new DealCanceller(DealShipTests.Seller, "بائِع", IsStoreAdmin: false))?.Code;
        output.WriteLine($"الوارِد: {outcome.Code} · مُغلَق: {deal.Refs.GetValueOrDefault(DealShipping.ClosedRef)} · حَيّ؟ {DealShipping.HoldsLiveOrder(deal)} · المُشتَري: {buyer ?? "يُقبَل"} · البائِع: {vendor ?? "يُقبَل"} · أَسطُر: {lines}→{deal.Timeline.Count}");
        Assert.Equal(OtoOrderStatusGate.NotDelivered, outcome.Code);
        Assert.Equal("canceled", deal.Refs[DealShipping.ClosedRef]);
        Assert.True(DealShipping.ClosedAtOto(deal));
        Assert.False(DealShipping.HoldsLiveOrder(deal));
        Assert.Null(buyer);
        Assert.Null(vendor);
        Assert.Equal(lines, deal.Timeline.Count);
        Assert.True((await w.Deals.CancelAsync(Slug, w.Id, new DealCanceller(DealShipTests.Buyer, "مُشتَرٍ", IsStoreAdmin: false), "عَدَلتُ")).Ok);
    }

    /// <summary><b>السالِب</b>: حالٌ غَيرُ نِهائِيٍّ (<c>searchingDriver</c>) لا يُغلِق — لا <c>oto_closed_status</c>، والطَلَبُ حَيٌّ، والإلغاءُ يُرَدُّ <c>deal_past_cancel_stage</c> كَما كان؛
    /// وجَوابٌ عَن طَلَبٍ غَيرِ طَلَبِنا لا يُغلِقُ شَيئاً.</summary>
    [Theory]
    [InlineData("searchingDriver", true)]
    [InlineData("canceled", false)]
    public async Task A_non_terminal_or_foreign_answer_keeps_the_order_live(string status, bool ours)
    {
        var w = await PlacedAtConfirmed(status);
        if (!ours) w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Tracking("another-order", OtoId, status, label: null);

        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, OtoId, status: status));

        var deal = Stored(w);
        Assert.False(deal.Refs.ContainsKey(DealShipping.ClosedRef));
        Assert.True(DealShipping.HoldsLiveOrder(deal));
        Assert.Equal(DealCancelAuthorization.DealPastCancelStage,
            DealCancelAuthorization.Validate(deal, new DealCanceller(DealShipTests.Buyer, "مُشتَرٍ", IsStoreAdmin: false))?.Code);
    }
}
