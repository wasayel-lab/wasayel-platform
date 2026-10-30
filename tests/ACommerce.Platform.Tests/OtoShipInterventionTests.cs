using ACommerce.Kit.Delivery;
using ACommerce.Kit.Notifications;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «أَكمِل الشَّحن» عَلى شُحنَةٍ فُقِدَت (‏2026-10-30، تَدقيقُ الموجَة ١١ F4 — د9 (١)) ════════════════════════════════════════════════════
//
// **الحالُ المَقيسَة**: قَرارُ المالِك د9 (١) «أ» — «يُعلَّم "يحتاج تدخّلًا" ويُبلَّغ البائع وصاحب المتجر» — طُبِّقَ في الوارِدِ وَحدَه (`DecideOneAsync` ←
// `FlagInterventionAsync`). و`ShipAsync` بَعدَ `GetOrderStatusAsync` لا يَعرِفُ إلّا الحالَ النِهائِيّ، فَطَلَبٌ تَقولُ OTO إنّه `lostOrDamaged` (وبائِعُه رُفِضَ
// تَسجيلُ وارِدِه فَلا إشعارَ يَصِل) يُجابُ كُلَّ نَقرَةٍ `shipment_unconfirmed` («أَعِد المُحاوَلَةَ بَعدَ قَليل») بِلا عَلامَةٍ ولا إشعار — بابٌ بِلا نِهايَة (القاعِدَة ١٢).

public class OtoShipInterventionTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string OtoId = "540789";

    private static async Task<DealShipTests.World> PlacedAtConfirmed(string status)
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, OtoId);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OtoId, status, label: null);
        return w;
    }

    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    /// <summary><b>تَوصيفُ «اليَوم»</b> (القاعِدَة ٣): OTO تَقولُ <c>lostOrDamaged</c> عَن طَلَبِ صَفقَةٍ عِندَ «تأكيد» — «أَكمِل الشَّحن» مَرَّتَينِ تُجابانِ
    /// <c>shipment_unconfirmed</c>، بِلا <c>oto_needs_intervention</c>.</summary>
    [Fact]
    public async Task Today_complete_shipping_on_a_lost_shipment_answers_retry_with_no_flag()
    {
        var w = await PlacedAtConfirmed("lostOrDamaged");

        var first = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);
        var second = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);

        var deal = Stored(w);
        output.WriteLine($"النَقرَتان: {first.Code} · {second.Code} · العَلامَة: {deal.Refs.GetValueOrDefault(DealShipping.InterventionRef) ?? "—"}");
        Assert.Equal(DealShipping.ShipmentUnconfirmed, first.Code);
        Assert.Equal(DealShipping.ShipmentUnconfirmed, second.Code);
        Assert.False(deal.Refs.ContainsKey(DealShipping.InterventionRef));
        Assert.False(DealShipping.NeedsIntervention(deal));
    }
}
