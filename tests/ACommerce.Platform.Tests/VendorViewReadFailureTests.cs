using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ عَرضُ حِسابِ الشَحنِ لِلبائِعِ يَقرَأُ العُطلَ غِياباً — فَتُرسَمُ استِمارَةُ اللَصقِ لِبائِعٍ لَه مَدخَل (تَيّارُ S15 البَند J، ‏2026-10-24) ═══
//
// G5 (iii) فَرَّقَ العُطلَ مِن الغِيابِ لِلناقِلِ ولِلتَسجيلِ ولِلَّصق (`ReadVendorEntryAsync` ← `vendor_entry_read_failed`)، وأَبقى `ViewAsync` — العَرضَ
// وَحدَه — عَلى `VendorEntryAsync` الَّتي تُجيبُ العُطلَ `null` (وثيقَتُها تَقولُه: «حَيثُ يَرسُمُ العُطلُ الاستِمارَة، والنَقرَةُ بَعدَها تَقرَأُ مِن جَديدٍ
// مُغلَقَة»). فَبائِعٌ لَه حِسابٌ يَعمَلُ تَعَثَّرَت قِراءَتُه يَرى في «إعلاناتي» استِمارَةَ لَصقٍ (أَو تَسجيلٍ) كَمَن لا حِسابَ لَه — والنَقرَةُ تُرَدّ.
//
// **صَفُّ «Today_» يُقلَبُ بِاسمِه** في كوميتِ الإصلاح (القاعِدَة ٣)، والصَفُّ الآخَرُ يَبقى بِحَرفِه.
public class VendorViewReadFailureTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;

    private static string MyListings() => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/MyListings.razor")));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 J)</b> — كانَ <c>Today_a_throwing_entry_read_views_as_no_entry</c> (‏<c>059c397</c>):
    /// «العَرض: null (لا مَدخَل)». والآن قِراءَةٌ تَرمي حالٌ ثالِثَة (<c>ReadFailed</c>) بِلا ذَيلٍ ولا حال، والصَفحَةُ تُسقِطُ قِسمَ الشَحنِ وتَقولُ السَبَبَ
    /// مِن القامُوس — قَبلَ أَن يُستَهلَكَ رابِطُ التَفعيل.</summary>
    [Fact]
    public async Task A_throwing_entry_read_views_as_read_failed_and_the_page_draws_no_form()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var thrown = VendorEntryReadFailureTests.FailEntryReads(w.Docs, afterLoads: 0, failures: 1);

        var view = await VendorTokenRefusedTests.PasteSurface(w).ViewAsync(Slug, DealShipTests.Seller);
        output.WriteLine($"قِراءاتٌ رَمَت: {thrown()} · العَرض: {(view is null ? "null (لا مَدخَل)" : view.ToString())}");

        Assert.Equal(1, thrown());
        Assert.Equal(new VendorShippingView(null, null, null, ReadFailed: true), view);

        var page = MyListings();
        var read = page.IndexOf("shippingView = await Vendors.ViewAsync(Slug, Auth.UserId!.Value);", StringComparison.Ordinal);
        var drop = page.IndexOf("if (shippingView is { ReadFailed: true })", StringComparison.Ordinal);
        var none = page.IndexOf("shippingInput = Vs.VendorShippingInput.None;", drop, StringComparison.Ordinal);
        var link = page.IndexOf("activationLink = Vendors.TakeActivationLink(", StringComparison.Ordinal);
        Assert.True(read > 0 && drop > read && none > drop && link > none,
            $"الإسقاطُ لا يَقَعُ بَينَ القِراءَةِ ورابِطِ التَفعيل: read={read} drop={drop} none={none} link={link}");
        Assert.Contains("else if (Auth.IsAuthenticated && shippingReadFailed)", page, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"warning\" Message=\"@L[\"shipping.vendor.read_failed\"]\" />", page, StringComparison.Ordinal);
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "shipping.vendor.read_failed");
        Assert.NotEqual("shipping.vendor.read_failed", text);
        Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text));
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: قِراءَةٌ تَنجَحُ تَعرِضُ ذَيلَ الرَمزِ وحالَ الوارِد، والغِيابُ <c>null</c>.</summary>
    [Fact]
    public async Task A_readable_entry_views_its_tail_and_an_absent_one_views_null()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var surface = VendorTokenRefusedTests.PasteSurface(w);

        var view = await surface.ViewAsync(Slug, DealShipTests.Seller);
        output.WriteLine($"العَرض: {view}");

        Assert.NotNull(view);
        Assert.Equal("bbbb", view!.TokenTail);
        Assert.Equal("59", view.WebhookId);
        Assert.Null(view.WebhookRefusal);
        Assert.Null(await surface.ViewAsync(Slug, DealShipTests.Buyer));
    }
}
