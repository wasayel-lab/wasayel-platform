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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 L)</b> — كانَ <c>Today_cart_add_writes_a_cart_without_asking_whether_the_store_uses_one</c>
    /// (‏<c>bfda98a</c>). والآن النُقطَةُ تَسأَلُ بابَ الصَفحَةِ نَفسَه (<see cref="CartDoor.Opens"/> بِشَخصِيَّةِ المُستَأجِر) بَعدَ الهُوِيَّةِ وقَبلَ قِراءَةِ
    /// الإعلانِ والسَلَّةِ وأَيِّ كِتابَة، وتَرتَدُّ بِرَمزِه؛ والصَفحَةُ تَسأَلُ الدالَّةَ نَفسَها لا <c>UsesCart</c> مُباشَرَة، وتَقولُ الرَمزَ مِن القامُوس.</summary>
    [Fact]
    public void Cart_add_asks_the_page_s_door_before_any_cart_read_or_write()
    {
        var body = CartAdd();
        output.WriteLine($"جِسمُ cart/add: {body.Length} مِحرَفاً");
        var who = body.IndexOf("var (userId, _, _) = parsed.Value;", StringComparison.Ordinal);
        var door = body.IndexOf("if (!Services.Patterns.CartDoor.Opens(Services.Patterns.PatternProfileResolver.Of(await tenants.FindAsync(slug))))", StringComparison.Ordinal);
        var back = body.IndexOf("return Results.Redirect(Link(req, slug, $\"listings/{id}?err={Services.Patterns.CartDoor.ClosedCode}\"));", StringComparison.Ordinal);
        var listing = body.IndexOf("await s.LoadAsync<Listing>(id);", StringComparison.Ordinal);
        var cart = body.IndexOf("LoadAsync<ACommerce.Kit.Cart.Cart>", StringComparison.Ordinal);
        var write = body.IndexOf("s.Store(cart);", StringComparison.Ordinal);
        Assert.True(who > 0 && door > who && back > door && listing > back && cart > listing && write > cart,
            $"البابُ لا يَقَعُ بَعدَ الهُوِيَّةِ وقَبلَ القِراءَةِ والكِتابَة: who={who} door={door} back={back} listing={listing} cart={cart} write={write}");
        Assert.Equal(1, body.Split("CartDoor.Opens(").Length - 1);

        var page = ListingPage();
        Assert.Contains("@if (ACommerce.Templates.Customer.Marketplace.Services.Patterns.CartDoor.Opens(profile))", page, StringComparison.Ordinal);
        Assert.DoesNotContain("UsesCart", page, StringComparison.Ordinal);
        Assert.Contains("else if (Err == ACommerce.Templates.Customer.Marketplace.Services.Patterns.CartDoor.ClosedCode)", page, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"danger\" Message=\"@L[\"listings.detail.err_cart_closed\"]\" />", page, StringComparison.Ordinal);
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "listings.detail.err_cart_closed");
        Assert.NotEqual("listings.detail.err_cart_closed", text);
        Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text));
        Assert.Equal("cart_closed", CartDoor.ClosedCode);
    }

    /// <summary>المُوجَبُ والسالِبُ لِلدالَّة: تُفتَحُ لِشَخصِيَّةِ <see cref="AppPattern.Marketplace"/> وَحدَها، وتُغلَقُ لِكُلِّ نَمَطٍ آخَرَ ولِشَخصِيَّةٍ لَم تُحسَب —
    /// ومُستَأجِرٌ غائِبٌ يُحسَبُ <c>Marketplace</c> (‏<c>PatternOf(null)</c>) فَتُفتَح، كَما تَرسُمُها الصَفحَةُ لَه.</summary>
    [Theory]
    [InlineData(AppPattern.Marketplace, true)]
    [InlineData(AppPattern.Rental, false)]
    [InlineData(AppPattern.Roommate, false)]
    [InlineData(AppPattern.Trip, false)]
    [InlineData(AppPattern.Service, false)]
    public void The_door_opens_for_the_marketplace_profile_only(AppPattern pattern, bool opens)
    {
        Assert.Equal(opens, CartDoor.Opens(PatternProfileResolver.Profile(pattern)));
        Assert.False(CartDoor.Opens(null));
        Assert.True(CartDoor.Opens(PatternProfileResolver.Of(null)));
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
