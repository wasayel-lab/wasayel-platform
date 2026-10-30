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

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>2b241f2</c> — كانَ <c>Today_complete_shipping_on_a_lost_shipment_answers_retry_with_no_flag</c>: <c>shipment_unconfirmed</c> ×2
    /// بِلا عَلامَة): OTO تَقولُ <c>lostOrDamaged</c> ⇒ النَقرَةُ الأولى تُعَلِّمُ <c>oto_needs_intervention</c> وتُرَدُّ <c>needs_intervention</c>، والثانِيَةُ تُرَدُّ بِالرَمزِ
    /// نَفسِه بِلا كِتابَةٍ ثانِيَة؛ ولِلرَمزِ نَصٌّ عَلى صَفحَةِ الصَفقَة.</summary>
    [Fact]
    public async Task Complete_shipping_on_a_lost_shipment_flags_it_once_and_answers_needs_intervention()
    {
        var w = await PlacedAtConfirmed("lostOrDamaged");

        var first = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);
        var lines = Stored(w).Timeline.Count;
        var second = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);

        var deal = Stored(w);
        output.WriteLine($"النَقرَتان: {first.Code} · {second.Code} · العَلامَة: {deal.Refs.GetValueOrDefault(DealShipping.InterventionRef) ?? "—"} · أَسطُر {lines}→{deal.Timeline.Count}");
        Assert.Equal(OtoOrderStatusGate.Intervention, first.Code);
        Assert.Equal(OtoOrderStatusGate.Intervention, second.Code);
        Assert.Equal("lostOrDamaged", deal.Refs[DealShipping.InterventionRef]);
        Assert.True(DealShipping.NeedsIntervention(deal));
        Assert.Equal(lines, deal.Timeline.Count);
        Assert.Equal(DealStage.Confirmed, deal.Stage);
        Assert.Equal("deals.detail.err_needs_intervention", DealRefusalTexts.KeyFor(first.Code));
        Assert.NotEqual("deals.detail.err_needs_intervention",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "deals.detail.err_needs_intervention"));
    }

    /// <summary><b>وأَخَواتُ الطَلَبِ يُعَلَّمنَ مَعَها، والبائِعُ وصاحِبُ المَتجَرِ يُخبَرانِ مَرَّةً لِكُلِّ صَفقَة</b> — بِأُنبوبِ الوارِدِ نَفسِه (<c>NotifyInterventionAsync</c>)؛
    /// ونَقرَةٌ ثانِيَةٌ لا تُخبِرُ أَحَداً.</summary>
    [Fact]
    public async Task A_lost_grouped_shipment_flags_every_deal_of_the_order_and_tells_the_vendor_and_the_owner_once()
    {
        var owner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        var w = await DealShipGroupTests.Grouped(await DealShipGroupTests.TwoConfirmedCashDeals(notices: GatewayOwnerMoneyNoticeTests.Notices));
        w.Docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَر", OwnerUserId = owner });
        foreach (var id in new[] { w.First, w.Second })
        {
            await w.Deals.AttachRefAsync(Slug, id, DealShipping.OrderRef, w.First.ToString("N"));
            await w.Deals.AttachRefAsync(Slug, id, DealShipping.OtoIdRef, OtoId);
        }
        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), OtoId, "lostOrDamaged", label: null);

        var first = await w.Shipping.ShipAsync(Slug, w.Second, DealShipTests.Vendor, null);
        var told = w.Docs.Stored.OfType<Notification>().ToArray();
        var again = await w.Shipping.ShipAsync(Slug, w.First, DealShipTests.Vendor, null);

        output.WriteLine($"الأولى: {first.Code} · الثانِيَة: {again.Code} · إشعارات: البائِع {told.Count(n => n.UserId == DealShipTests.Seller)} · المالِك {told.Count(n => n.UserId == owner)} · بَعدَ الثانِيَة {w.Docs.Stored.OfType<Notification>().Count()}");
        Assert.Equal(OtoOrderStatusGate.Intervention, first.Code);
        Assert.Equal(OtoOrderStatusGate.Intervention, again.Code);
        Assert.All(new[] { w.First, w.Second }, id => Assert.True(DealShipping.NeedsIntervention(w.Docs.Read<Deal>(id.ToString())!)));
        Assert.Equal(2, told.Count(n => n.UserId == DealShipTests.Seller));
        Assert.Equal(2, told.Count(n => n.UserId == owner));
        Assert.Equal(told.Length, w.Docs.Stored.OfType<Notification>().Count());
    }

    /// <summary><b>السالِب</b>: حالٌ لا يَحتاجُ تَدَخُّلاً ولا يُثبِتُ شُحنَةً (<c>searchingDriver</c> بِلا بوليصَة) ⇒ <c>shipment_unconfirmed</c> كَما كان بِلا عَلامَة؛ وجَوابٌ
    /// <c>lostOrDamaged</c> عَن طَلَبٍ غَيرِ طَلَبِنا لا يُعَلِّمُ شَيئاً.</summary>
    [Theory]
    [InlineData("searchingDriver", true)]
    [InlineData("lostOrDamaged", false)]
    public async Task A_non_intervention_or_foreign_answer_keeps_shipment_unconfirmed_and_no_flag(string status, bool ours)
    {
        var w = await PlacedAtConfirmed(status);
        if (!ours) w.Oto.Routes[OtoClient.OrderStatusPath] = _ => OtoRouteHandler.Tracking("another-order", OtoId, status, label: null);

        var res = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, null);

        Assert.Equal(DealShipping.ShipmentUnconfirmed, res.Code);
        Assert.False(Stored(w).Refs.ContainsKey(DealShipping.InterventionRef));
    }
}
