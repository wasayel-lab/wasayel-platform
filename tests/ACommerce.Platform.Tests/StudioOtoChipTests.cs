using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شَريحَةُ «طَلَبُ شَحنٍ قائِمٌ عِندَ OTO» في صَفحَةِ صَفقَةِ الاستوديو عَلى صَفقَةٍ سَلَّمَتها OTO — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٦ — D-4) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): الصَفحَةُ تَرسُمُ الشَريحَةَ العامَّةَ بِشَرطِ `DealShipping.HoldsLiveOrder` — و«delivered» لَيسَ حالاً نِهائِيّاً عِندَ OTO
// (`OtoClient.IsTerminal`)، فَيَبقى صادِقاً لِلأَبَد بَعدَ التَسليم؛ وفَرعُ المَجموعَةِ نُقِلَ إلى `LiveOnWaybill` والعامُّ لا. فَصَفقَةُ نَقدٍ سُلِّمَت تُقالُ لِصاحِبِ المَتجَرِ «طَلَبُ شَحنٍ قائِمٌ
// … يُلغي البائِعُ الشُّحنَة» — ولا شُحنَةَ قائِمَة — ولا يُقالُ إنّ الإلغاءَ يَكتُبُ رَدَّ ثَمَنِها مُستَحَقّاً. **وما يَبقى**: الوَقائِعُ نَفسُها — بَعدَ التَسليمِ الطَلَبُ «قائِم» عِندَ OTO بِحَرفِ
// `HoldsLiveOrder`، ولا يَسيرُ في شُحنَة (`OnLiveWaybill` كاذِبَة)، ونَقدُه مَقبوض (`CollectedInCash`)؛ وقَبلَ التَسليمِ الشُحنَةُ سائِرَة.

public class StudioOtoChipTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static Deal Stored(DealShipGroupTests.Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    internal static string StudioPage() => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppDealDetail.razor"));

    /// <summary>صَفقَةُ نَقدٍ وَحدَها في طَلَبِها عِندَ OTO — مَشحونَةً، ثُمَّ (بِـ<paramref name="delivered"/>) سَلَّمَتها OTO بِالوارِد.</summary>
    internal static async Task<(DealShipGroupTests.Pair W, Deal Deal)> SingleShipped(bool delivered)
    {
        var w = await DealShipGroupTests.TwoConfirmedCashDeals();
        Assert.True((await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh")).Ok);
        if (delivered)
        {
            w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");
            Assert.Equal(OtoOrderStatusGate.Delivered, (await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First))).Code);
        }
        return (w, Stored(w, w.First));
    }

    /// <summary><b>صَفقَةُ نَقدٍ سَلَّمَتها OTO وصَفقَةٌ في الطَريق</b>: ما يَبقى — وَقائِعُ الدَوالِّ الثَلاث. ويُطبَعُ ما يَتَغَيَّر: أَيُّ شَريحَةٍ يَرسُمُها شَرطُ الصَفحَةِ اليَوم.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_delivered_cash_deal_still_holds_its_order_at_oto_and_no_longer_rides_a_live_waybill(bool delivered)
    {
        var (_, deal) = await SingleShipped(delivered);

        var page = StudioPage();
        Assert.True(page.Length > 1000, "أَداةٌ عَمياء: صَفحَةُ صَفقَةِ الاستوديو لَم تُقرَأ.");
        output.WriteLine($"{(delivered ? "سُلِّمَت" : "في الطَريق")}: {deal.Status}/{deal.Stage} · HoldsLiveOrder={DealShipping.HoldsLiveOrder(deal)} · OnLiveWaybill={DealCancelAuthorization.OnLiveWaybill(deal)} · " +
                         $"CollectedInCash={DealsPolicy.CollectedInCash(deal)} · شَرطُ الشَريحَةِ في الصَفحَة: {(page.Contains("@if (DealShipping.HoldsLiveOrder(deal) && deal.Status is", StringComparison.Ordinal) ? "HoldsLiveOrder" : "غَيرُه")}");
        Assert.True(DealShipping.HoldsLiveOrder(deal));
        Assert.Equal(!delivered, DealCancelAuthorization.OnLiveWaybill(deal));
        Assert.Equal(delivered, DealsPolicy.CollectedInCash(deal));
        Assert.Equal((DealStatus.Active, delivered ? DealStage.Delivered : DealStage.Shipping), (deal.Status, deal.Stage));
    }
}
