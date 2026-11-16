using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَدخَلُ بائِعٍ لا يَنفَكُّ يَرسُمُ بابَ الشَحنِ الَّذي يُجابُ «لا ناقِل» في كُلِّ نَقرَة — تَوصيفُ «اليَوم» (القاعِدَة ١٢) ═══════════════════
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (ix)): `CarrierReadyForAsync` تَسأَلُ المَدخَلَ بِلا فَكٍّ (فَعّال، خِزانَةٌ مُتاحَة، رَمزٌ مَختوم) — فَمَدخَلٌ مَختومٌ
// بِمِفتاحٍ رَئيسٍ تَبَدَّل (أَو بِسِياقٍ لا يُطابِق) «جاهِز»، وصَفحَةُ الصَفقَةِ (`MyDealDetail`: `canShip`/`shipHeldKey`) تَرسُمُ لِلبائِعِ «إنشاء بوليصَة» أَو
// «أَكمِل الشَّحن»، و`/ship` تَرُدُّ كُلَّ نَقرَةٍ `carrier_unavailable` لِأَنّ `CarrierForAsync` لا يَفُكُّه.
//
// **الصَفُّ يَحسِبُ ما تَحسِبُه الصَفحَةُ اليَومَ بِسُطورِها** (`var carrier = await Shipping.CarrierReadyForAsync(Slug, deal);` ثُمَّ `IsAllowed`/`HeldRefusal`
// بِالبائِعِ نَفسِه) — و**ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_».
//
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(shipping)» بَعدَ ‏762ff2a؛ ‏ADR-033 §٧‑ص‑١٠): الصَفحَةُ تَسأَلُ `CarrierReadyForViewerAsync` — الجاهِزِيَّةُ بِلا فَكٍّ، ثُمَّ
// لِلناظِرِ الَّذي تُجيزُه البَوّابَةُ وَحدَه يُفَكُّ الناقِلُ ويُرمى. والصَفُّ المَقلوبُ يَحسِبُ ما تَحسِبُه الصَفحَةُ بِسُطورِها الجَديدَة.

public class ShipDoorUnopenableEntryTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    /// <summary>صَفقَةُ نَقدٍ عِندَ «تأكيد» ومَدخَلُ بائِعِها مَختومٌ بِمِفتاحٍ غَيرِ مِفتاحِ العالَم (مِفتاحٌ رَئيسٌ تَبَدَّل) — وبِطَلَبٍ عِندَ OTO أَو بِلاه.</summary>
    internal static async Task<DealShipTests.World> UnopenableAsync(bool placed)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller));
        if (placed) await w.Deals.AttachRefAsync(Slug, w.Id, DealShipping.OrderRef, w.Id.ToString("N"));
        return w;
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_vendor_entry_that_does_not_open_draws_the_ship_form_whose_every_click_answers_carrier_unavailable</c>
    /// (‏<c>762ff2a</c>): «جاهِز: True · الباب: True · المَحجوز: - · النَقرَة: carrier_unavailable». والآن لا بابَ — والخُطوَةُ المَحجوزَةُ عِندَ OTO (طَلَبٌ قائِم)
    /// تَقولُ <c>carrier_unavailable</c> بِنَصِّه، والصَفقَةُ بِلا طَلَبٍ لا مَحجوزَ لَها (بابُها «تَأكيد: شَحن» بِلا OTO كَمَن لا ناقِلَ لَه)؛ والنَقرَةُ تُجابُ كَما كانَت.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_vendor_entry_that_does_not_open_draws_no_ship_door_and_a_held_deal_says_carrier_unavailable(bool placed)
    {
        var w = await UnopenableAsync(placed);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var carrier = await w.Shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        var canShip = DealShipAuthorization.IsAllowed(deal, DealShipTests.Vendor, carrier);
        var held = DealShipAuthorization.HeldRefusal(deal, DealShipTests.Vendor, carrier);
        var click = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"[طَلَب={placed}] جاهِز: {carrier} · الباب: {canShip} · المَحجوز: {held ?? "-"} · النَقرَة: {click.Code ?? "ok"} · نِداءاتُ OTO: {w.Oto.Calls.Count}");

        Assert.False(carrier);
        Assert.False(canShip);
        Assert.Equal(placed ? DealShipAuthorization.CarrierUnavailable : null, held);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary><b>المُوجَب</b>: مَدخَلٌ يَنفَكُّ — والاحتِياطُ في مَتجَرِ العَرضِ لِمَن لا مَدخَلَ لَه — بابُه مَرسوم، والنَقرَةُ تَشحَن.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_entry_that_opens_or_the_demo_fallback_draws_the_door_and_the_click_ships(bool vendorEntry)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: vendorEntry);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var carrier = await w.Shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);

        Assert.True(carrier);
        Assert.True(DealShipAuthorization.IsAllowed(deal, DealShipTests.Vendor, carrier));
        Assert.True((await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Ok);
    }

    /// <summary><b>ولا فَكَّ لِمَن لا تُجيزُه البَوّابَة</b>: المُشتَري يَفتَحُ الصَفحَةَ نَفسَها — لا مُحاوَلَةَ لِفَكِّ مَدخَلِ البائِع (عَدّادُ «does not open» صِفر
    /// عَلى مَدخَلٍ يَرمي إن فُكّ)، والبائِعُ في العالَمِ نَفسِه مُحاوَلَةٌ واحِدَة.</summary>
    [Fact]
    public async Task A_viewer_the_gate_refuses_never_triggers_an_unseal_and_the_vendor_triggers_one()
    {
        var w = await UnopenableAsync(placed: false);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        var log = new ShipCapturingLogger();
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        var shipping = new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, log, w.Providers, w.Vault, new FixedClock(DealShipTests.Now));
        int Opens() => log.Lines.Count(l => l.Contains("does not open", StringComparison.Ordinal));

        var buyer = await shipping.CarrierReadyForViewerAsync(Slug, deal, new DealShipper(DealShipTests.Buyer, "مُشتَرٍ"));
        var afterBuyer = Opens();
        var vendor = await shipping.CarrierReadyForViewerAsync(Slug, deal, DealShipTests.Vendor);
        output.WriteLine($"المُشتَري: جاهِز={buyer} · فَكّ={afterBuyer} — البائِع: جاهِز={vendor} · فَكّ={Opens() - afterBuyer}");

        Assert.Equal(0, afterBuyer);
        Assert.False(DealShipAuthorization.IsAllowed(deal, new DealShipper(DealShipTests.Buyer, "مُشتَرٍ"), buyer));
        Assert.False(vendor);
        Assert.Equal(1, Opens() - afterBuyer);
    }
}
