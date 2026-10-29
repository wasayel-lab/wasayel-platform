using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شُروطُ المَتجَرِ تَقولُ «لا نُشارِكها مَع أَطراف ثالِثَة» — والسِياسَةُ تُسَمّي اثنَي عَشَرَ مُستَلِماً ═══
//
// مُراجَعَةُ تَيّارِ S2 الجَولَة ١ (‏2026-10-29): `terms.privacy.body` — يُرسَمُ في `Terms.razor` لِكُلِّ مَتجَرٍ ويَقبَلُه كُلُّ عُضوٍ قَبلَ أَوَّلِ كِتابَة — يَقولُ
// «لا نُشارِكها مَع أَطراف ثالِثَة بِدون إذنِكَ الصَّريح». وهِيَ الجُملَةُ نَفسُها الَّتي صَحَّحَها المالِكُ في البَندِ الثالِثِ مِن مُوافَقَةِ الاستوديو (قَرارُه د20 «أ»:
// «والبند ٣ اليوم يقول غير الصحيح»)، و`/privacy` §٥ تُسَمّي مَن تَصِلُه البَيانات (مُزَوِّدُ رَمزِ الدُخولِ، والاستِضافَة، وخِدمَةُ إشعاراتِ المُتَصَفِّح، وOTO…).
// **تَوصيفُ «اليَوم» قَبلَ التَصحيح** (القاعِدَة ٣): الماسِحُ يَقرَأُ كُلَّ قيمَةٍ في القامُوسِ بِكُلِّ لُغاتِه — مَجموعَةٌ تَحوي كُلَّ مِفتاحِ خُصوصِيَّةٍ أَو شُروطٍ يُرسَم —
// ويُطابِقُ الجُملَةَ بِلا تَشكيلٍ وصيغَتَها الإنجليزِيَّة. يُقلَبُ صَفُّ «Today_» بِاسمِه في كوميتِ التَصحيح.
public class StoreTermsPrivacyClaimTests(ITestOutputHelper output)
{
    /// <summary>«لا نُشارِكُها مَعَ أَطرافٍ ثالِثَة» بِلا تَشكيل — وصيغَتُها الإنجليزِيَّةُ («لا نُشارِكُها مَعَ طَرَفٍ ثالِث»).</summary>
    internal static bool ClaimsNoThirdParty(string value)
        => AccountDeletionRetentionTests.Bare(value).Contains("لا نشاركها مع أطراف ثالثة", StringComparison.Ordinal)
           || Regex.IsMatch(value, @"\b(?:do not|don't|never)\s+share\b[^.]*\bthird[- ]part", RegexOptions.IgnoreCase);

    internal static (int Scanned, IReadOnlyList<string> Carriers) Scan()
    {
        var scanned = 0;
        var carriers = new List<string>();
        foreach (var lang in LocaleCatalog.Languages)
            foreach (var e in LocaleCatalog.Raw(lang))
            {
                scanned++;
                if (ClaimsNoThirdParty(e.Value)) carriers.Add($"{lang}:{e.Key}");
            }
        return (scanned, carriers);
    }

    [Fact]
    public void Today_the_store_terms_say_we_share_with_no_third_party()
    {
        var (scanned, carriers) = Scan();
        output.WriteLine($"قيَمٌ مَفحوصَة: {scanned} · تَقولُ «لا نُشارِكها مَع أَطراف ثالِثَة»: {carriers.Count} ({string.Join(", ", carriers)})");
        Assert.True(scanned > 100, $"أَداةٌ عَمياء: {scanned} قيمَة (القاعِدَة ١٠).");
        Assert.Equal(new[] { "ar:terms.privacy.body" }, carriers);
    }

    /// <summary>والماسِحُ يَرى ما يَدَّعيه — عَيِّناتٌ مَحقونَةٌ مَعلومَةُ الجَواب (القاعِدَة ١٠).</summary>
    [Fact]
    public void The_scanner_sees_the_claim_in_both_languages_and_not_the_no_selling_sentence()
    {
        Assert.True(ClaimsNoThirdParty("لا نُشارِكها مَع أَطراف ثالِثَة بِدون إذنِكَ الصَّريح."));
        Assert.True(ClaimsNoThirdParty("We do not share it with any third party."));
        Assert.True(ClaimsNoThirdParty("we never share your data with third-parties"));
        Assert.False(ClaimsNoThirdParty("لا نَبيع بَياناتك لِأَيّ طَرَف ثالِث."));
        Assert.False(ClaimsNoThirdParty("We do not sell your data to any third party."));
    }
}
