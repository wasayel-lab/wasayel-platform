using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «إلغاء الطَّلَب كامِلاً» يُلغي أُختَ الشُحنَةِ المُتَنازَعَ عَلَيها أَيضاً — حَسماً إلغاءً (‏2026-11-05، الموجَة ١٢ G3 · ٥ — D-2؛ تَوصيفُه `5ad2083`) ═══
//
// **القَرار** (د38؛ قاعِدَةُ G3 · ٢ لِأُختِ الفاتورَة): `CancelWholeInvoiceAsync` يَضُمُّ أَخَواتِ الشُحنَةِ السائِرَةِ في «نِزاع» إلى ما يَحسِمُه إلغاءً — بِبَوّابَةِ الحَسمِ وجِسمِ الإلغاءِ
// نَفسِه — لِصاحِبِ المَتجَرِ وبِلا شَرطِ المُهلَة، فَتَصدُقُ الشَريحَة؛ وما سَلَّمَته OTO أَثناءَ النِزاعِ (‏D-1) يُكتَبُ رَدُّ نَقدِه مُستَحَقّاً ويُخبَرُ صاحِبُ المَتجَر.
// ومِلَفُّ التَوصيفِ `DealWaybillDisputedSiblingTests.cs` بِبَصمَتِه قَبلُ وبَعدُ.

public class DealWaybillDisputedSiblingSwitchTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static Deal Stored(DealShipGroupTests.Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;
    private static readonly DealCanceller StoreOwner = DealWaybillCancelTests.StoreOwner;

    /// <summary><b>المُوجِب</b>: الطَلَبُ كُلُّه مِن «أ» يُلغي «أ» و«ب» — «ب» بِسَطرِ إلغاءٍ بِاسمِ صاحِبِ المَتجَر — ولا تَبقى صَفقَةٌ قائِمَةٌ في الشُحنَة؛ وبِتَسليمٍ أَثناءَ النِزاعِ يُكتَبُ عَلى «ب»
    /// <c>cash_refund_due</c> مَرَّةً ويُخبَرُ صاحِبُ المَتجَرِ «رَدٌّ نَقديٌّ مُستَحَقّ» مَرَّة.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task A_store_whole_order_cancel_resolves_the_disputed_waybill_sibling_as_a_cancel(bool deliveredWhileDisputed, int refundLines)
    {
        var w = await DealWaybillDisputedSiblingTests.DisputedWaybillSibling(deliveredWhileDisputed);
        var l = new L(new HttpContextAccessor());

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, w.First, StoreOwner, "طَلَبَه المُشتَري");
        var notices = GatewayOwnerMoneyNoticeTests.Notices(w.Docs);
        foreach (var gone in order.Cancelled) await notices.NotifyCancelledByStoreAsync(Slug, gone, StoreOwner.ActorId, l);

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        var toOwner = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == StoreOwner.ActorId && n.Title == l["notifications.deal_cash_refund_due.title"]).ToArray();
        output.WriteLine($"{(deliveredWhileDisputed ? "سُلِّمَت أَثناءَ النِزاع" : "لَم تُسَلَّم")}: {(order.Result.Ok ? "ok" : order.Result.Violation!.Code)} · أُلغِيَ {order.Cancelled.Count} · " +
                         $"أ={a.Status} ب={b.Status} · cash_refund_due عَلى «ب»={b.Timeline.Count(e => e.Action == DealEvent.CashRefundDue)} · «رَدٌّ نَقديٌّ مُستَحَقّ» لِصاحِبِ المَتجَر: {toOwner.Length}");
        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(new[] { w.First, w.Second }.OrderBy(x => x), order.Cancelled.Select(d => d.Id).OrderBy(x => x));
        Assert.Equal(w.First, order.Cancelled[^1].Id);
        Assert.All(new[] { a, b }, d => Assert.Equal(DealStatus.Cancelled, d.Status));
        Assert.Equal(StoreOwner.ActorId, b.Timeline.Last(e => e.Action == "cancelled").ActorId);
        Assert.Equal(refundLines, b.Timeline.Count(e => e.Action == DealEvent.CashRefundDue));
        Assert.Equal(refundLines, toOwner.Length);
        Assert.Empty(DealCancelAuthorization.LiveOnWaybill(a, await w.Deals.WaybillSiblingsAsync(Slug, a)));
    }

    /// <summary><b>السالِب</b>: أُختٌ في «نِزاع» بَلَغَت «تَسليم» لا تَسيرُ في الشُحنَة (<see cref="DealCancelAuthorization.OnLiveWaybill"/> كاذِبَة) فَلا تُحسَمُ بِإلغاءِ أُختِها —
    /// «أ» تُلغى وَحدَها و«ب» في «نِزاع».</summary>
    [Fact]
    public async Task A_disputed_sibling_past_delivery_is_not_resolved_by_the_whole_order_cancel()
    {
        var w = await DealWaybillDisputedSiblingTests.DisputedWaybillSibling(deliveredWhileDisputed: false);
        Stored(w, w.Second).Stage = DealStage.Delivered;

        var order = await w.Deals.CancelWholeInvoiceAsync(Slug, w.First, StoreOwner, "طَلَبَه المُشتَري");

        Assert.True(order.Result.Ok, order.Result.Violation?.Code);
        Assert.Equal(new[] { w.First }, order.Cancelled.Select(d => d.Id).ToArray());
        Assert.Equal(DealStatus.Disputed, Stored(w, w.Second).Status);
    }
}
