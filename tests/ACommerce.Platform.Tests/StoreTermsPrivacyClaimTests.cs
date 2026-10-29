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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، كوميتُ التَصحيح)</b> — كانَ <c>Today_the_store_terms_say_we_share_with_no_third_party</c> (‏<c>0bb86a3</c>):
    /// «قيَمٌ مَفحوصَة: 2506 · تَقولُ «لا نُشارِكها مَع أَطراف ثالِثَة»: 1 (ar:terms.privacy.body)». والآن لا قيمَةَ في القامُوسِ — بِأَيِّ لُغَةٍ ولِأَيِّ صَفحَة —
    /// تَقولُها.</summary>
    [Fact]
    public void No_dictionary_value_says_we_share_with_no_third_party()
    {
        var (scanned, carriers) = Scan();
        output.WriteLine($"قيَمٌ مَفحوصَة: {scanned} · تَقولُ «لا نُشارِكها مَع أَطراف ثالِثَة»: {carriers.Count} ({string.Join(", ", carriers)})");
        Assert.True(scanned > 100, $"أَداةٌ عَمياء: {scanned} قيمَة (القاعِدَة ١٠).");
        Assert.Empty(carriers);
    }

    /// <summary><b>وما صارَ يَقولُه — ما تَقولُه مُوافَقَةُ الاستوديو بَعدَ د20 بِلا مُزَوِّدِ النَموذَج</b> (فَالشُروطُ لِلعُضوِ لا لِصاحِبِ الفِكرَة)، والجُملَةُ الأولى
    /// (ما نَحفَظُه) بِحَرفِها؛ والإحالَةُ لَها باب: رابِطٌ إلى <c>/privacy</c> — الصَفحَةُ الَّتي تُسَمّي المُستَلِمينَ (<c>platform.privacy.share_*</c>) — ونَصُّه مِن القامُوس.</summary>
    [Fact]
    public void The_store_terms_privacy_clause_says_what_the_consent_says_and_links_the_policy_that_names_the_recipients()
    {
        var body = LocaleCatalog.Find(LocaleCatalog.Arabic, "terms.privacy.body")!;
        Assert.StartsWith("نَحفَظ بَياناتكَ الشَخصِيَّة\n                    (اسم، هاتِف، مَوقِع) لِاستِخدامِها داخِل التَّطبيق فَقَط.", body, StringComparison.Ordinal);
        Assert.EndsWith("لا نَبيعُها، ونُشارِكُها مَعَ الجِهاتِ المُسَمّاةِ في سِياسَةِ الخُصوصِيَّةِ فَقَط.", body, StringComparison.Ordinal);
        Assert.Contains("لا نَبيعُ بَياناتِك، ونُشارِكُها مَعَ مُزَوِّدِ نَموذَجِ اللُغَةِ لِتَوليدِ الدِراسَةِ ومَعَ الجِهاتِ المُسَمّاةِ في سِياسَةِ الخُصوصِيَّةِ فَقَط.",
            LocaleCatalog.Find(LocaleCatalog.Arabic, "studio.consent.item3_body"), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find(LocaleCatalog.Arabic, "terms.privacy.policy_link")));
        var terms = SellerDisplayNameTests.Code("Components", "Pages", "Terms.razor");
        Assert.Contains("@L.Markup(\"terms.privacy.body\") <a href=\"/privacy\">@L.Markup(\"terms.privacy.policy_link\")</a>", terms, StringComparison.Ordinal);
        Assert.True(RouteResolutionTests.Resolves("/privacy", RouteResolutionTests.Declared("GET")));
        Assert.Contains("@doc.Markup(\"platform.privacy.share_", PrivacyShareInventoryTests.PrivacyPage(), StringComparison.Ordinal);
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
