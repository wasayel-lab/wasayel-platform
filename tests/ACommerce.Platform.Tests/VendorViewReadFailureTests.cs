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

    /// <summary><b>توصيفُ اليَوم</b>: قِراءَةٌ تَرمي تُعرَضُ كَـ«لا مَدخَل» (<c>null</c>) — والصَفحَةُ لا تَعرِفُ حالاً ثالِثَة.</summary>
    [Fact]
    public async Task Today_a_throwing_entry_read_views_as_no_entry()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var thrown = VendorEntryReadFailureTests.FailEntryReads(w.Docs, afterLoads: 0, failures: 1);

        var view = await VendorTokenRefusedTests.PasteSurface(w).ViewAsync(Slug, DealShipTests.Seller);
        output.WriteLine($"قِراءاتٌ رَمَت: {thrown()} · العَرض: {(view is null ? "null (لا مَدخَل)" : view.ToString())}");

        Assert.Equal(1, thrown());
        Assert.Null(view);
        Assert.DoesNotContain("ReadFailed", MyListings(), StringComparison.Ordinal);
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
