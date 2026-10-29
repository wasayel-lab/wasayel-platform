using ACommerce.Kit.Delivery;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بائِعٌ ضاعَ رَمزُ حِسابِه عِندَ OTO — تَوصيفُ «اليَوم» قَبلَ قَرارِ المالِك د12 (‏2026-10-29) ═══════════════════════════════
//
// **(١) «أ» بِاختِيارِ المالِك**: «يُحجب رابط التفعيل حين يضيع الرمز، ويظهر بدله "أعد المحاولة"، فلا يفعّل البائع حسابًا لا نملك رمزه». **المَقيسُ
// اليَوم**: `RegisterAsync` يَضَعُ رابِطَ التَفعيلِ في وِعاءِ العَرضِ **قَبلَ** الخَتم، فَإن تَعَثَّرَ الخَتمُ `SealAttempts` مَرّاتٍ صَعِدَ الاستِثناءُ (صَفحَةُ خَطَأ)
// والرابِطُ باقٍ تَعرِضُه «إعلاناتي» في أَوَّلِ فَتحٍ بَعدَه — حِسابٌ يُفَعَّلُ ولا نَملِكُ رَمزَه. (والاستِثناءُ نَفسُه مُثَبَّتٌ في `VendorRegisterSealRetryTests`.)
//
// **(٢) «أ»**: «يُسمح للبائع بلصق رمز جديد بيده … في حالتين وحدهما: رمز محفوظ لا يُفكّ، ورمز ضاع حفظه بعد تسجيل نجح عند OTO». **المَقيسُ اليَوم**:
// في وَضعِ الرَمزِ السوقِيِّ اللَصقُ مُغلَقٌ لِكُلِّ بائِع (`vendor_paste_closed`) ولَو كانَ رَمزُه المَحفوظُ لا يُفَكّ، والتَسجيلُ يُجابُ `vendor_entry_unreadable`
// بِنَصٍّ يُحيلُ إلى «مالِكِ المَتجَر» الَّذي لا يَملِكُ سَحبَ رَمزِ البائِع. يَنقَلِبُ كُلُّ صَفٍّ بِاسمِه في كوميتِ القَرار.

public class VendorTokenRecoveryTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    [Fact]
    public async Task Today_a_register_whose_token_is_lost_escapes_and_leaves_the_activation_link_for_the_next_view()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        VendorRegisterSealRetryTests.FailSaves(w.Docs, failAt: 2, failures: VendorShippingService.SealAttempts);

        var error = await Record.ExceptionAsync(() =>
            surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound));
        var link = surface.TakeActivationLink(Slug, owner);

        output.WriteLine($"الاستِثناء: {error?.GetType().Name ?? "لا"} · رابِطُ التَفعيل: {link ?? "لا"}");
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("https://login.tryoto.com/XYZ", link);
    }

    [Fact]
    public async Task Today_register_mode_refuses_a_paste_even_for_an_entry_whose_token_does_not_open_and_sends_to_the_store_owner()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller));   // مَختومٌ بِمِفتاحٍ آخَر — لا يُفَكّ هُنا
        var surface = VendorRegisterBucketTests.Surface(w);
        Assert.Equal(VendorShippingInput.Register, surface.Input);

        var paste = await surface.SaveTokenAsync(Slug, ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!, "RT-pasted-9d9d", Inbound);
        var register = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(DealShipTests.Seller), VendorShippingTests.Form(), Inbound);

        output.WriteLine($"اللَصق: {paste.Code} · التَسجيل: {register.Code} · نِداءات: {w.Oto.Calls.Count}");
        Assert.Equal(VendorShippingService.PasteClosed, paste.Code);
        Assert.Equal(VendorShippingService.EntryUnreadable, register.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Contains("مالِكِ المَتجَر", LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.err_" + VendorShippingService.EntryUnreadable), StringComparison.Ordinal);
    }
}
