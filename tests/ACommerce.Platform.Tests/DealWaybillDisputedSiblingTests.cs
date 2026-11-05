using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «إلغاء الطَّلَب كامِلاً» عَلى شُحنَةٍ حَيَّةٍ وأُختُها في «نِزاع» — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٥ — D-2) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): `CancelWholeInvoiceAsync` يَأخُذُ أَخَواتِ الشُحنَةِ مِن `LiveOnWaybill` (وهي تَعُدُّ «نِزاعاً») ثُمَّ يُرَشِّحُها
// `Status == Active` — فَصاحِبُ المَتجَرِ يُلغي «أ» وَحدَها بِـ`wholeInvoice: true` (فَلا يُسأَلُ عَن الشُحنَة)، و«ب» تَبقى في الشُحنَةِ والمَندوبُ يُحَصِّلُ «أ»+«ب»، والشَريحَةُ
// تَقولُ «إلغاء الطَّلَب كامِلاً». **وما يَبقى**: الطَلَبُ يُقبَلُ و«أ» مُلغاة، ولا نِداءَ إلغاءٍ إلى OTO (لا يَملِكُه الكود)، والطَلَبُ عِندَ OTO كَما هو.

public class DealWaybillDisputedSiblingTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static Deal Stored(DealShipGroupTests.Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>صَفقَتا السَلَّةِ في طَلَبٍ واحِدٍ حَيٍّ عِندَ OTO، و«ب» في «نِزاع» بِبابِ صاحِبِ المَتجَر — وبِـ<paramref name="deliveredWhileDisputed"/> سَلَّمَتها OTO أَثناءَه (‏D-1).</summary>
    internal static async Task<DealShipGroupTests.Pair> DisputedWaybillSibling(bool deliveredWhileDisputed)
    {
        var w = await DealWaybillCancelTests.ShippedGroup(told: true);
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Second, DealWaybillCancelTests.StoreOwner.ActorId, "صاحِبُ المَتجَر", "لَم تَصِل")).Ok);
        if (deliveredWhileDisputed) await w.Deals.AttachRefAsync(Slug, w.Second, DealShipping.DeliveredWhileDisputedRef, "delivered");
        return w;
    }

    /// <summary><b>صاحِبُ المَتجَرِ يُلغي الطَلَبَ كُلَّه مِن «أ» و«ب» في «نِزاع»</b> — بِتَسليمٍ أَثناءَ النِزاعِ وبِغَيرِه: ما يَبقى — الطَلَبُ يُقبَلُ و«أ» مُلغاة. ويُطبَعُ ما يَتَغَيَّر:
    /// حالُ «ب»، وما أُلغِيَ، و<c>cash_refund_due</c> عَلى «ب».</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_store_cancelling_the_whole_order_with_a_disputed_waybill_sibling_cancels_the_active_deal(bool deliveredWhileDisputed)
    {
        var w = await DisputedWaybillSibling(deliveredWhileDisputed);

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, w.First, DealWaybillCancelTests.StoreOwner, "طَلَبَه المُشتَري");

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        output.WriteLine($"{(deliveredWhileDisputed ? "سُلِّمَت أَثناءَ النِزاع" : "لَم تُسَلَّم")}: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} · أُلغِيَ {order.Cancelled.Count} · " +
                         $"أ={a.Status} ب={b.Status} · cash_refund_due عَلى «ب»={b.Timeline.Count(e => e.Action == DealEvent.CashRefundDue)} · " +
                         $"«ب» ما زالَت في الشُحنَة={DealCancelAuthorization.OnLiveWaybill(b) && b.Status is DealStatus.Active or DealStatus.Disputed}");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(DealStatus.Cancelled, a.Status);
        Assert.Equal(250m, Assert.Single(DealShipGroupTests.Orders(w.Oto)).AmountDue);
    }
}
