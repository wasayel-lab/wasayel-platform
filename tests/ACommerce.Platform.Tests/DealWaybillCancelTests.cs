using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إلغاءُ صَفقَةٍ واحِدَةٍ مِن طَلَبِ شَحنٍ مُشتَرَكٍ عِندَ OTO (‏2026-10-30، تَدقيقُ الموجَة ١١ F1 + G-3) ═════════════════════════════
//
// **الحالُ المَقيسَة**: مُشتَرٍ أَتَمَّ «أ» (‏100) و«ب» (‏150) مِن بائِعٍ واحِدٍ نَقداً عِندَ الاستِلام؛ شَحَنَ البائِعُ فَأَنشَأَ `ShipAsync` طَلَباً واحِداً عِندَ OTO
// بِمُعَرِّفِ «أ» ومَبلَغِ تَحصيلٍ ‏250 وبَندَي «أ · ب»، والصَفقَتانِ عِندَ «شَحن» (د13 «ب»). الطَرَفانِ لا يُلغِيانِ (د37: `HoldsLiveOrder`)، وصاحِبُ المَتجَرِ يُلغي «ب»
// وَحدَها مِن الاستوديو — والطَلَبُ عِندَ OTO ما زالَ يَحمِلُ «ب» ومَبلَغَها. فَإمّا يُلغي البائِعُ الطَلَبَ مِن حِسابِه فَتَموتُ شُحنَةُ «أ» مَعَه، وإمّا يُحَصِّلُ
// المَندوبُ ‏250 ويُسَلِّمُ «أ» و«ب» لا يَنالُها إلّا سَطرُ «سُلِّمَت بَعدَ الإلغاء» — بِلا «رَدٌّ نَقدِيٌّ مُستَحَقّ» ولا إشعارٍ لِصاحِبِ المَتجَر (G-3، ولِلصَفقَةِ الواحِدَةِ أَيضاً).

public class DealWaybillCancelTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    /// <summary>مُلغي الاستوديو — صاحِبُ المَتجَرِ بِسُلطَةِ الإدارَة، كَما يَبنيه بابُ <c>/studio/apps/{slug}/deals/{id}/cancel</c>.</summary>
    internal static readonly DealCanceller StoreOwner = new(Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"), "صاحِبُ المَتجَر", IsStoreAdmin: true);

    private static Deal Stored(DealShipGroupTests.Pair w, Guid id) => w.Docs.Read<Deal>(id.ToString())!;

    /// <summary>صَفقَتا السَلَّةِ مَشحونَتانِ بِطَلَبٍ واحِدٍ عِندَ OTO — مَبلَغُ تَحصيلِه مَجموعُهُما.</summary>
    internal static async Task<DealShipGroupTests.Pair> ShippedGroup()
    {
        var w = await DealShipGroupTests.Grouped(await DealShipGroupTests.TwoConfirmedCashDeals());
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, "Riyadh")).Ok);
        return w;
    }

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): صاحِبُ المَتجَرِ يُلغي «ب» وَحدَها مِن طَلَبٍ مُشتَرَكٍ حَيٍّ عِندَ OTO — <b>يُقبَلُ الإلغاء</b>، و«أ» قائِمَةٌ عِندَ «شَحن»،
    /// والطَلَبُ الواحِدُ عِندَ OTO بِمَبلَغِ تَحصيلِه ‏250 — لا شَيءَ في الكودِ يَقولُ إنّ الشُحنَةَ تَحمِلُ «أ» أَيضاً.</para>
    /// </summary>
    [Fact]
    public async Task Today_the_store_owner_cancels_one_deal_of_a_live_grouped_waybill_alone_and_the_order_keeps_the_summed_amount()
    {
        var w = await ShippedGroup();

        var cancelled = await w.Deals.CancelAsync(Slug, w.Second, StoreOwner, "طَلَبَه المُشتَري");

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        var order = Assert.Single(DealShipGroupTests.Orders(w.Oto));
        output.WriteLine($"الإلغاء: ok={cancelled.Ok} · «أ» {a.Status}/{a.Stage} · «ب» {b.Status}/{b.Stage} · طَلَبُ OTO {order.AmountDue}");
        Assert.True(cancelled.Ok, cancelled.Violation?.Code);
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (a.Status, a.Stage));
        Assert.Equal(DealStatus.Cancelled, b.Status);
        Assert.Equal(250m, order.AmountDue);
        Assert.True(DealShipping.HoldsLiveOrder(a) && DealShipping.HoldsLiveOrder(b));
    }

    /// <summary>
    /// <para><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣، G-3): ثُمَّ يُسَلِّمُ المَندوبُ ويُحَصِّلُ ‏250 — «أ» تَصيرُ «تَسليم»، و«ب» المُلغاةُ لا يُكتَبُ لَها إلّا سَطرُ
    /// <c>oto_delivered_after_cancel</c>: <b>لا <c>cash_refund_due</c></b> (لِأَنّ <c>CollectedInCash(ب)</c> كاذِبٌ — لَم تَبلُغ «تَسليم» بِكاتِبِ الانتِقال)،
    /// فَلا ما يَقولُه <c>OwnerActionOf</c> لِصاحِبِ المَتجَر — والمُشتَري دَفَعَ ‏150 عَن مُلغاةٍ ولا شَيءَ يَقولُ إنّها مُستَحَقَّة.</para>
    /// </summary>
    [Fact]
    public async Task Today_a_delivered_notice_after_a_member_was_cancelled_writes_no_cash_refund_due_and_names_no_owner_action()
    {
        var w = await ShippedGroup();
        Assert.True((await w.Deals.CancelAsync(Slug, w.Second, StoreOwner, "طَلَبَه المُشتَري")).Ok);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));

        var (a, b) = (Stored(w, w.First), Stored(w, w.Second));
        output.WriteLine($"الوارِد: {outcome.Code} · «أ» {a.Stage} · «ب» {b.Status} · أَسطُرُ «ب»: {string.Join("، ", b.Timeline.Select(e => e.Action))}");
        Assert.Equal(DealStage.Delivered, a.Stage);
        Assert.Equal("delivered", b.Refs[DealShipping.DeliveredAfterCancelRef]);
        Assert.DoesNotContain(b.Timeline, e => e.Action == DealEvent.CashRefundDue);
        Assert.Null(DealNotices.OwnerActionOf(b));
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (G-3 لِلصَفقَةِ الواحِدَة): صَفقَةُ نَقدٍ بِلا أَخَواتٍ شُحِنَت ثُمَّ أَلغاها صاحِبُ المَتجَرِ ثُمَّ سَلَّمَها المَندوب —
    /// السَطرُ وَحدَه، بِلا <c>cash_refund_due</c>.</summary>
    [Fact]
    public async Task Today_a_single_cash_deal_delivered_after_its_cancel_writes_no_cash_refund_due()
    {
        var w = await DealShipGroupTests.TwoConfirmedCashDeals();   // بِلا رَبطِ أَخَوات: صَفقَتانِ مِن سَلَّتَين
        Assert.True((await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, "Riyadh")).Ok);
        Assert.True((await w.Deals.CancelAsync(Slug, w.First, StoreOwner, "طَلَبَه المُشتَري")).Ok);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");

        await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.First));

        var a = Stored(w, w.First);
        Assert.Equal("delivered", a.Refs[DealShipping.DeliveredAfterCancelRef]);
        Assert.DoesNotContain(a.Timeline, e => e.Action == DealEvent.CashRefundDue);
        Assert.Null(DealNotices.OwnerActionOf(a));
    }

    /// <summary><b>تَوصيفُ «اليَوم»</b> (صَفحَةُ صَفقَةِ الاستوديو): شَريحَةُ «طَلَبُ شَحنٍ قائِمٌ» واحِدَةٌ لا تُسَمّي ما يَحمِلُه الطَلَبُ غَيرَ الصَفقَة، وزِرُّ «إلغاء الطَّلَب كامِلاً»
    /// لا يَعرِفُ إلّا أَخَواتِ فاتورَةِ البَوّابَة — فَصَفقَةُ النَقدِ في طَلَبٍ مُشتَرَكٍ تُرسَمُ بِزِرِّ «إلغاء» وَحدَها.</summary>
    [Fact]
    public void Today_the_studio_page_names_no_other_deal_of_the_waybill_and_offers_the_single_cancel()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioAppDealDetail.razor"));
        Assert.Contains("if (deal is not null) cancelShared = !GatewayInvoiceShare.IsValid(deal, await Deals.InvoiceSiblingsAsync(Slug, deal));", page, StringComparison.Ordinal);
        Assert.Contains("@L.Markup(\"studio.deal.oto_order_live\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("oto_order_live_group", page, StringComparison.Ordinal);
    }
}
