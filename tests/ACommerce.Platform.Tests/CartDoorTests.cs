using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بابُ السَلَّةِ في الصَفحَةِ وَحدَها — `cart/add` يَكتُبُ سَلَّةً في مَتجَرٍ لا يَستَعمِلُها (تَيّارُ S15 البَند L، ‏2026-10-24) ═══
//
// صَفحَةُ الإعلانِ لا تَرسُمُ استِمارَةَ `cart/add` إلّا لِنَمَطٍ `UsesCart` (‏`PatternProfile`) — والمَتجَرُ إيجارٌ أَو سَكَنٌ أَو مَشاويرُ أَو خِدمَة
// يَرسُمُ «تَواصَل» و«قَدِّم عَرضاً» مَكانَها. والنُقطَةُ لا تَسأَلُ عَن النَمَط: طَلَبٌ مَصنوعٌ يَكتُبُ سَلَّةً لا تَعرِضُها الواجِهَة، و`checkout/submit`
// يُحَوِّلُها صَفَقاتٍ عَلى مَتجَرٍ لَم يَختَر البَيعَ بِالسَلَّة. ما يُرسَمُ لَيسَ ما يُقبَل (القاعِدَة ١٢).
//
// **صَفُّ «Today_» يُقلَبُ بِاسمِه** في كوميتِ الإصلاح (القاعِدَة ٣)، والصَفُّ الآخَرُ يَبقى بِحَرفِه.
public class CartDoorTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Source(string rel)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel);
        Assert.True(File.Exists(path), $"مَفقود: {path}");
        return WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
    }

    /// <summary>جِسمُ نُقطَةِ <c>cart/add</c> — مِن مَسارِها حَتّى مُرَشِّحِ الباقَةِ الَّذي يَختِمُها.</summary>
    private static string CartAdd()
    {
        var code = Source($"{Template}/MarketplaceTemplateExtensions.cs");
        var a = code.IndexOf("\"/{slug}/listings/{id:guid}/cart/add\"", StringComparison.Ordinal);
        Assert.True(a > 0, "نُقطَةُ cart/add غائِبَة.");
        var b = code.IndexOf(".RequireStoreWritable();", a, StringComparison.Ordinal);
        Assert.True(b > a);
        return code[a..b];
    }

    private static string ListingPage()
        => WriteEndpointGuardTests.StripMarkupComments(Source($"{Template}/Components/Pages/TenantListingDetail.razor"));

    /// <summary><b>توصيفُ اليَوم</b>: الصَفحَةُ تَسأَلُ <c>UsesCart</c>، والنُقطَةُ لا تَسأَلُ النَمَطَ ولا المُستَأجِر — تَقرَأُ السَلَّةَ وتَكتُبُها.</summary>
    [Fact]
    public void Today_cart_add_writes_a_cart_without_asking_whether_the_store_uses_one()
    {
        var body = CartAdd();
        output.WriteLine($"جِسمُ cart/add: {body.Length} مِحرَفاً");
        Assert.Contains("s.Store(cart);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("UsesCart", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PatternProfileResolver", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tenants.FindAsync", body, StringComparison.Ordinal);
        Assert.Contains("@if (profile?.UsesCart == true)", ListingPage(), StringComparison.Ordinal);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: نَمَطٌ واحِدٌ مِن خَمسَةٍ يَستَعمِلُ السَلَّة — فَالمَتاجِرُ الَّتي لا تَرسُمُ الاستِمارَةَ مَوجودَةٌ لا افتِراضِيَّة.</summary>
    [Fact]
    public void Only_the_marketplace_pattern_uses_the_cart()
    {
        var uses = Enum.GetValues<AppPattern>().Where(p => PatternProfileResolver.Profile(p).UsesCart).ToArray();
        output.WriteLine($"أَنماطٌ تَستَعمِلُ السَلَّة: {string.Join(", ", uses)} مِن {Enum.GetValues<AppPattern>().Length}");
        Assert.Equal(new[] { AppPattern.Marketplace }, uses);
        Assert.Equal(5, Enum.GetValues<AppPattern>().Length);
    }
}
