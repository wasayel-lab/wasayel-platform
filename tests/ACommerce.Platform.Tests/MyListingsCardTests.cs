using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── د41: بِطاقَةُ «إعلاناتي» في «حِسابي» لِمَن لا يَنشُرُ ولا إعلانَ لَه ────────────────────
//
// قَرارُ المالِك د41 «أ» (‏2026-10-28): «تُخفى البِطاقَةُ في هذِه الحالَةِ وَحدَها: دَورٌ لا يَنشُرُ ولَيسَ لِصاحِبِه إعلانٌ واحِد. وتَبقى لِكُلِّ مَن
// يَستَطيعُ النَشرَ أَو لَه إعلاناتٌ سابِقَة، وتَبقى كَما هِيَ في المَتاجِرِ الَّتي بِلا أَدوار.» وتَنفيذُه يَبدَأُ «بِاختِبارٍ يُثَبِّتُ صَفحَةَ «حِسابي» كَما هِيَ
// اليَوم» (القاعِدَة ٣) — وهذا هُو: البِطاقَةُ تَحتَ الشُروطِ نَفسِها الَّتي تَحتَها بِطاقَةُ «المُفَضَّلَة» غَيرُ المَشروطَة، أَي لِكُلِّ صاحِبِ جَلسَة.

public class MyListingsCardTests(ITestOutputHelper output)
{
    internal static string MePath => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Me.razor");

    internal const string ListingsCard = "<AcActionCard Label=\"@(L[\"me.action.my_listings\"])\"";
    internal const string FavoritesCard = "<AcActionCard Label=\"@(L[\"me.action.favorites\"])\"";

    internal static (IReadOnlyList<string> Listings, IReadOnlyList<string> Favorites) CardConditions()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(MePath));
        var markup = page[..page.IndexOf("@code {", StringComparison.Ordinal)];
        return (UpgradePromptPriceTests.EnclosingIfConditions(markup, markup.IndexOf(ListingsCard, StringComparison.Ordinal)),
                UpgradePromptPriceTests.EnclosingIfConditions(markup, markup.IndexOf(FavoritesCard, StringComparison.Ordinal)));
    }

    /// <summary><b>اليَوم</b>: بِطاقَةُ «إعلاناتي» مَرسومَةٌ بِلا شَرطٍ يَخُصُّها — شُروطُها المُحيطَةُ هِيَ شُروطُ «المُفَضَّلَة» بِحَرفِها، فَيَراها مَن دَورُه لا يَنشُرُ
    /// ولا إعلانَ لَه، فَتَفتَحُ لَه صَفحَةً فارِغَةً بِلا إجراء (‏<c>me.listings</c> بِلا «إعلان جَديد» وبِلا «اِختَر دَورَك» إن لَم يَكُن دَورٌ يَنشُر).</summary>
    [Fact]
    public void Today_the_my_listings_card_is_drawn_under_the_favorites_cards_conditions_alone()
    {
        var (listings, favorites) = CardConditions();
        output.WriteLine($"شُروطُ «إعلاناتي»: [{string.Join(" | ", listings)}] · شُروطُ «المُفَضَّلَة»: [{string.Join(" | ", favorites)}]");
        Assert.Equal(favorites, listings);
        Assert.DoesNotContain(listings, c => c.Contains("istings", StringComparison.Ordinal));
    }
}
