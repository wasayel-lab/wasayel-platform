using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «أَكمِل الشَّحن» تَحتَ عَلامَةِ «يَحتاجُ تَدَخُّلاً» — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G3 · ٧ — D-5) ═══
//
// **المَقيس** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى ويُطبَعُ ما يَتَغَيَّر): `DealShipAuthorization.Validate` لا يَسأَلُ العَلامَة، فَصَفقَةُ نَقدٍ عِندَ «تأكيد» طَلَبُها عِندَ OTO
// وعَلَيها `oto_needs_intervention` تُجيزُها البَوّابَة — فَتَرسُمُ الصَفحَةُ «أَكمِل الشَّحن» (`canShip` مِن `IsAllowed`) و`HeldRefusal` لا شَيء، وكُلُّ نَقرَةٍ تَسأَلُ OTO
// «Order Tracking» ثُمَّ تُجابُ `needs_intervention` بِلا كِتابَة — زِرٌّ جَوابُه واحِدٌ أَبَداً (القاعِدَة ١٢). **وما يَبقى**: النَقرَةُ تُجابُ `needs_intervention` ولا تَكتُبُ سَطراً
// والصَفقَةُ عِندَ «تأكيد» والعَلامَةُ قائِمَة؛ والوارِدُ يُخرِجُها إلى «شَحن» حينَ تُثبِتُ OTO شُحنَتَها وبوليصَتَها (`ShipsFirst` لا يَسأَلُ العَلامَة)؛ وصَفقَةٌ بِلا عَلامَةٍ تُجيزُها البَوّابَة.

public class OtoInterventionShipDoorTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string OtoId = "540789";
    private static Deal Stored(DealShipTests.World w) => w.Docs.Read<Deal>(w.Id.ToString())!;

    /// <summary>صَفقَةُ نَقدٍ عِندَ «تأكيد» طَلَبُها عِندَ OTO، وOTO تَقولُ <paramref name="status"/> — وبِـ<paramref name="flag"/> نُقِرَت «أَكمِل الشَّحن» مَرَّةً فَعُلِّمَت.</summary>
    internal static async Task<DealShipTests.World> PlacedAtConfirmed(string status, bool flag)
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OtoIdRef, OtoId);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OtoId, status, label: null);
        if (flag)
        {
            Assert.Equal(OtoOrderStatusGate.Intervention, (await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null)).Code);
            Assert.True(DealShipping.NeedsIntervention(Stored(w)));
        }
        return w;
    }

    /// <summary><b>صَفقَةٌ مُعَلَّمَة</b>: ما يَبقى — النَقرَةُ <c>needs_intervention</c> بِلا سَطرٍ والصَفقَةُ عِندَ «تأكيد» مُعَلَّمَة. ويُطبَعُ ما يَتَغَيَّر: أَتُجيزُها البَوّابَةُ (فَيُرسَمُ الزِرّ)،
    /// وما تَقولُه <c>HeldRefusal</c>، وكَم سُؤالاً لِـOTO في النَقرَة.</summary>
    [Fact]
    public async Task A_flagged_deal_at_confirmed_answers_needs_intervention_on_every_click_and_writes_nothing()
    {
        var w = await PlacedAtConfirmed("lostOrDamaged", flag: true);
        var deal = Stored(w);
        var (calls, lines) = (w.Oto.CallsTo(OtoClient.OrderStatusPath), deal.Timeline.Count);

        var allowed = DealShipAuthorization.IsAllowed(deal, DealShipTests.Vendor, carrierReady: true);
        var held = DealShipAuthorization.HeldRefusal(deal, DealShipTests.Vendor, carrierReady: true);
        var click = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);

        output.WriteLine($"مُعَلَّمَة: البَوّابَةُ تُجيز={allowed} · HeldRefusal={held ?? "—"} · النَقرَة: {click.Code} · أَسئِلَةُ OTO +{w.Oto.CallsTo(OtoClient.OrderStatusPath) - calls} · " +
                         $"أَسطُر +{Stored(w).Timeline.Count - lines}");
        Assert.Equal(OtoOrderStatusGate.Intervention, click.Code);
        Assert.Equal(lines, Stored(w).Timeline.Count);
        Assert.Equal(DealStage.Confirmed, Stored(w).Stage);
        Assert.True(DealShipping.NeedsIntervention(Stored(w)));
    }

    /// <summary><b>السالِب — لا يَتَغَيَّرُ بِحَرف</b>: صَفقَةٌ بِلا عَلامَةٍ (OTO تَقولُ <c>searchingDriver</c>) تُجيزُها البَوّابَةُ و<c>HeldRefusal</c> لا شَيء.</summary>
    [Fact]
    public async Task An_unflagged_placed_deal_at_confirmed_is_allowed_by_the_gate()
    {
        var w = await PlacedAtConfirmed("searchingDriver", flag: false);

        Assert.True(DealShipAuthorization.IsAllowed(Stored(w), DealShipTests.Vendor, carrierReady: true));
        Assert.Null(DealShipAuthorization.HeldRefusal(Stored(w), DealShipTests.Vendor, carrierReady: true));
    }

    /// <summary><b>والوارِدُ مَخرَجُها — لا يَتَغَيَّرُ بِحَرف</b>: بَعدَ العَلامَةِ تَقولُ OTO <c>pickedUp</c> بِبوليصَة ⇒ الوارِدُ يُخرِجُ الصَفقَةَ إلى «شَحن» (والعَلامَةُ لا تُمسِكُه)،
    /// فَلا «أَكمِل الشَّحن» بَعدَها: تاليها لَيسَ «شَحن».</summary>
    [Fact]
    public async Task After_the_flag_an_inbound_that_confirms_the_shipment_moves_the_deal_to_shipping()
    {
        var w = await PlacedAtConfirmed("lostOrDamaged", flag: true);
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OtoId, "pickedUp");

        var outcome = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, status: "pickedUp"));

        output.WriteLine($"الوارِد: {outcome.Code} · {Stored(w).Stage} · البَوّابَة: {DealShipAuthorization.Validate(Stored(w), DealShipTests.Vendor, carrierReady: true)?.Code ?? "تُجيز"}");
        Assert.Equal(DealStage.Shipping, Stored(w).Stage);
        Assert.Equal(DealShipAuthorization.DealNotReadyToShip, DealShipAuthorization.Validate(Stored(w), DealShipTests.Vendor, carrierReady: true)?.Code);
    }
}
