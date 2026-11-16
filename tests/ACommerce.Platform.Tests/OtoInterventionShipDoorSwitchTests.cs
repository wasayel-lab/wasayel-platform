using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «أَكمِل الشَّحن» لا يُرسَمُ ولا يُقبَلُ تَحتَ عَلامَةِ «يَحتاجُ تَدَخُّلاً» (‏2026-11-05، الموجَة ١٢ G3 · ٧ — D-5؛ تَوصيفُه في الكوميتِ السابِق) ═══
//
// **القَرار** (القاعِدَة ١٢، القاعِدَة ٦، ‏ADR-038): `DealShipAuthorization.Validate` يَرُدُّ `needs_intervention` (‏`ShipmentNeedsIntervention`، الرَمزُ الثالِثَ عَشَرَ بِحَرفِ رَمزِ الوارِد) ما دامَت
// `DealShipping.NeedsIntervention` صادِقَة — بَعدَ `oto_order_closed` وقَبلَ سُؤالِ الناقِل — فَلا تَرسُمُ الصَفحَةُ الاستِمارَة (`canShip` مِن `IsAllowed`) وتَقولُ نَصَّ الرَمزِ نَفسِه
// (`HeldRefusal` ← `deals.detail.err_needs_intervention`) بِلا تَعديلِ `MyDealDetail`، والنُقطَةُ تَرُدُّ قَبلَ «Order Tracking». ومِلَفُّ التَوصيفِ `OtoInterventionShipDoorTests.cs` بِبَصمَتِه.

public class OtoInterventionShipDoorSwitchTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    /// <summary><b>المُوجِب</b>: الصَفقَةُ المُعَلَّمَةُ تَرُدُّها البَوّابَةُ <c>needs_intervention</c>، و<c>HeldRefusal</c> يَقولُه فَتَرسُمُ الصَفحَةُ نَصَّه، والنَقرَةُ تُرَدُّ بِه <b>بِلا سُؤالِ OTO</b>
    /// ولا سَطر.</summary>
    [Fact]
    public async Task A_flagged_deal_is_refused_by_the_gate_with_needs_intervention_and_the_click_asks_oto_nothing()
    {
        var w = await OtoInterventionShipDoorTests.PlacedAtConfirmed("lostOrDamaged", flag: true);
        var deal = Stored(w);
        var (calls, lines) = (w.Oto.CallsTo(OtoClient.OrderStatusPath), deal.Timeline.Count);

        var refusal = DealShipAuthorization.Validate(deal, DealShipTests.Vendor, carrierReady: true);
        var held = DealShipAuthorization.HeldRefusal(deal, DealShipTests.Vendor, carrierReady: true);
        var click = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);

        output.WriteLine($"مُعَلَّمَة: البَوّابَة={refusal?.Code} · HeldRefusal={held} → {DealRefusalTexts.KeyFor(held)} · النَقرَة: {click.Code} · أَسئِلَةُ OTO +{w.Oto.CallsTo(OtoClient.OrderStatusPath) - calls} · " +
                         $"أَسطُر +{Stored(w).Timeline.Count - lines}");
        Assert.Equal(DealShipAuthorization.ShipmentNeedsIntervention, refusal?.Code);
        Assert.Equal(OtoOrderStatusGate.Intervention, DealShipAuthorization.ShipmentNeedsIntervention);
        Assert.False(DealShipAuthorization.IsAllowed(deal, DealShipTests.Vendor, carrierReady: true));
        Assert.Equal("deals.detail.err_needs_intervention", DealRefusalTexts.KeyFor(held));
        Assert.Equal(OtoOrderStatusGate.Intervention, click.Code);
        Assert.Equal(calls, w.Oto.CallsTo(OtoClient.OrderStatusPath));
        Assert.Equal(lines, Stored(w).Timeline.Count);
    }

    /// <summary><b>البَوّابَةُ النَقِيَّة — مُوجَبٌ وسالِبٌ لِلرَمزِ الجَديد</b>: العَلامَةُ بِحالٍ مِن <see cref="OtoClient.InterventionStatuses"/> ⇒ تُرَدّ؛ وبِلا عَلامَة، وعَلامَةٌ بِحالٍ غَيرِهِما،
    /// وطَلَبٌ أَغلَقَته OTO (رَمزُه قَبلَه) ⇒ لا <c>needs_intervention</c>.</summary>
    [Theory]
    [InlineData("lostOrDamaged", null, true)]
    [InlineData("destroyed", null, true)]
    [InlineData(null, null, false)]
    [InlineData("searchingDriver", null, false)]
    [InlineData("lostOrDamaged", "canceled", false)]
    public async Task Shipment_needs_intervention_positive_and_negative(string? flag, string? closed, bool refused)
    {
        var w = await OtoInterventionShipDoorTests.PlacedAtConfirmed("searchingDriver", flag: false);
        var deal = Stored(w);
        if (flag is not null) deal.Refs[DealShipping.InterventionRef] = flag;
        if (closed is not null) deal.Refs[DealShipping.ClosedRef] = closed;

        var code = DealShipAuthorization.Validate(deal, DealShipTests.Vendor, carrierReady: true)?.Code;

        output.WriteLine($"العَلامَة {flag ?? "—"} · الإغلاق {closed ?? "—"}: {code ?? "تُجيز"}");
        Assert.Equal(refused, code == DealShipAuthorization.ShipmentNeedsIntervention);
        if (closed is not null) Assert.Equal(DealShipAuthorization.OrderClosedAtOto, code);
    }
}
