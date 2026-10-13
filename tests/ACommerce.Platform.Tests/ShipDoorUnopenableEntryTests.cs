using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Today_a_vendor_entry_that_does_not_open_draws_the_ship_form_whose_every_click_answers_carrier_unavailable(bool placed)
    {
        var w = await UnopenableAsync(placed);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        var carrier = await w.Shipping.CarrierReadyForAsync(Slug, deal);
        var canShip = DealShipAuthorization.IsAllowed(deal, DealShipTests.Vendor, carrier);
        var held = DealShipAuthorization.HeldRefusal(deal, DealShipTests.Vendor, carrier);
        var click = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        output.WriteLine($"[طَلَب={placed}] جاهِز: {carrier} · الباب: {canShip} · المَحجوز: {held ?? "-"} · النَقرَة: {click.Code ?? "ok"} · نِداءاتُ OTO: {w.Oto.Calls.Count}");

        Assert.True(carrier);
        Assert.True(canShip);
        Assert.Null(held);
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, click.Code);
        Assert.Empty(w.Oto.Calls);
    }
}
