using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ صَفقَةُ نَقدٍ في «نِزاع» تُسَلِّمُها OTO ثُمَّ يُحسَمُ نِزاعُها إلغاءً — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٤ — D-1) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): `OtoOrderStatusGate.Decide` يَرُدُّ «نِزاعاً» بِـ`deal_not_active`، و`DeliveredAfterCancel` لِلمُلغاةِ وَحدَها،
// فَالوارِدُ يُجابُ ‏200 ولا يُكتَبُ شَيء — ولا تُعيدُه OTO. ثُمَّ يَحسِمُ صاحِبُ المَتجَرِ النِزاعَ إلغاءً: `CollectedInCash` كاذِبٌ (لَم تَبلُغ «تَسليم» بِكاتِبِ الانتِقال)، فَلا
// `cash_refund_due` ولا إشعارَ لِصاحِبِ المَتجَر — والمَندوبُ حَصَّلَ النَقدَ والمالُ عِندَ البائِع. يُناقِضُ د38 (٢) «إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة».
// **وما يَبقى**: الصَفقَةُ لا تَتَحَرَّكُ بِالوارِدِ (عِندَ «شَحن»، في «نِزاع»)، والجَوابُ ‏200 بِرَمزِه، والحَسمُ يُقبَلُ والصَفقَةُ مُلغاة؛ وحالٌ غَيرُ «سُلِّم» لا يَكتُبُ شَيئاً.

public class OtoDeliveredWhileDisputedTests(ITestOutputHelper output)
{
    internal static readonly Guid Owner = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
    private const string Slug = DealShipTests.Slug;
    /// <summary>مُعَرِّفُ الطَلَبِ عِندَ OTO كَما يَكتُبُه الشَحنُ في عالَمِ <c>DealShipTests</c> — بِحَرفِ <c>OtoOrderStatusTests</c>.</summary>
    private const string OurOtoId = "540789";
    private const string TrackingUrl = "https://app.tryoto.com/otoflex-tracking?enc=x";
    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    /// <summary>صَفقَةُ نَقدٍ شُحِنَت بِنُقطَةِ الشَحنِ نَفسِها ثُمَّ فَتَحَ صاحِبُ المَتجَرِ عَلَيها نِزاعاً — ووَثيقَةُ المَتجَرِ بِمالِكِه.</summary>
    internal static async Task<DealShipTests.World> DisputedShippedCashDeal()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        Assert.True(shipped.Ok, shipped.Code);
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = Owner });
        Assert.True((await w.Deals.DisputeAsync(Slug, w.Id, Owner, "مالِك التَّطبيق", "لَم تَصِل")).Ok);
        return w;
    }

    internal static void OtoSays(DealShipTests.World w, string status)
        => w.Oto.Routes[ACommerce.Kit.Delivery.OtoClient.OrderStatusPath] = body =>
            OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OurOtoId, status, TrackingUrl);

    /// <summary><b>«سُلِّمَ» عَلى صَفقَةِ نَقدٍ في «نِزاع» ثُمَّ حَسمُها إلغاءً</b>: ما يَبقى — ‏200 بِـ<c>deal_not_active</c>، والصَفقَةُ عِندَ «شَحن» في «نِزاع» لا تَتَحَرَّك، والحَسمُ يُقبَلُ
    /// فَتُلغى. ويُطبَعُ ما يَتَغَيَّر: ما يُكتَبُ عِندَ الوارِد، و<c>cash_refund_due</c> عِندَ الحَسم، وما يُخبَرُ بِه صاحِبُ المَتجَر.</summary>
    [Fact]
    public async Task A_delivered_notice_on_a_disputed_cash_deal_never_moves_it_and_its_cancel_resolution_cancels_it()
    {
        var w = await DisputedShippedCashDeal();
        OtoSays(w, "delivered");
        var (saves, lines) = (w.Docs.SaveCalls, Stored(w).Timeline.Count);

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id));

        var atNotice = $"الوارِد: {outcome.Code} → {OtoOrderStatusGate.StatusFor(outcome.Code)} · حِفظات +{w.Docs.SaveCalls - saves} · أَسطُر +{Stored(w).Timeline.Count - lines} · " +
                       $"مَراجِع [{string.Join(", ", Stored(w).Refs.Keys.Where(k => k.StartsWith("oto_delivered", StringComparison.Ordinal)))}]";
        Assert.Equal(OtoOrderStatusGate.DealNotActive, outcome.Code);
        Assert.Equal(200, OtoOrderStatusGate.StatusFor(outcome.Code));
        Assert.Equal((DealStage.Shipping, DealStatus.Disputed), (Stored(w).Stage, Stored(w).Status));
        Assert.DoesNotContain(Stored(w).Timeline, e => e.StageAfter == DealStage.Delivered);

        var l = new L(new HttpContextAccessor());
        var res = await w.Deals.ResolveDisputeAsync(Slug, w.Id, new DealCanceller(Owner, "مالِك التَّطبيق", IsStoreAdmin: true),
            DealDisputeResolution.Cancel, "حَسم");
        Assert.True(res.Ok, res.Violation?.Code);
        await GatewayOwnerMoneyNoticeTests.Notices(w.Docs).NotifyDisputeResolvedAsync(Slug, res.Deal!, Owner, res.Outcome!, l);

        var toOwner = w.Docs.Stored.OfType<Notification>().Where(n => n.UserId == Owner).ToArray();
        output.WriteLine($"{atNotice} · الحَسم: {Stored(w).Status} · cash_refund_due={Stored(w).Timeline.Count(e => e.Action == DealEvent.CashRefundDue)} · " +
                         $"ما عَلى صاحِبِ المَتجَر: {DealNotices.OwnerActionOf(Stored(w))?.ToString() ?? "لا شَيء"} · لِصاحِبِ المَتجَر: {toOwner.Length}");
        Assert.Equal(DealStatus.Cancelled, Stored(w).Status);
    }

    /// <summary><b>السالِب — لا يَتَغَيَّرُ بِحَرف</b>: حالٌ غَيرُ «سُلِّم» عَلى الصَفقَةِ نَفسِها في «نِزاع» ⇒ <c>not_delivered</c>، ولا حِفظَ ولا سَطر.</summary>
    [Fact]
    public async Task A_status_other_than_delivered_on_a_disputed_deal_writes_nothing()
    {
        var w = await DisputedShippedCashDeal();
        OtoSays(w, "shipped");
        var (saves, lines) = (w.Docs.SaveCalls, Stored(w).Timeline.Count);

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, status: "shipped"));

        Assert.Equal(OtoOrderStatusGate.NotDelivered, outcome.Code);
        Assert.Equal(saves, w.Docs.SaveCalls);
        Assert.Equal(lines, Stored(w).Timeline.Count);
        Assert.DoesNotContain(Stored(w).Refs.Keys, k => k.StartsWith("oto_delivered", StringComparison.Ordinal));
    }
}
