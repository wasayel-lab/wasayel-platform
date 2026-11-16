using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نُصوصُ شَخصِيّاتِ الأَنماطِ مِن القامُوس — لا حَرفِيَّةَ عَرَبِيَّةً في `PatternProfile.cs` (القاعِدَة ١١، ‏2026-10-13) ═══
//
// الكوميتُ الَّذي رَحَّلَها أَبقى `PatternProfileTextsCharacterizationTests` أَخضَرَ بِلا تَعديلِ حَرف (بُرهانُ النَصِّ المَحلول)
// ولَقطَتَي البَطَلِ بايتاً (بُرهانُ الصَفحَة). وهذا المِلَفُّ يَحرُسُ ما بَعدَه: نَصٌّ عَرَبِيٌّ يَعودُ حَرفِيّاً إلى المِلَفِّ يُحمِرُّه،
// ومِفتاحٌ يَختَفي مِن القامُوسِ أَو يُضافُ بِلا صَفٍّ هُنا يُحمِرُّه.

public class PatternProfileTextsTests(ITestOutputHelper output)
{
    /// <summary>المَفاتيحُ الاثنا عَشَرَ بِأَسمائِها — خَمسَةُ أَزرارٍ وخَمسَةُ أَبطالٍ وتَحذيرا احتِيال.</summary>
    internal static readonly string[] Keys =
    {
        "pattern.roommate.cta",    "pattern.roommate.hero",    "pattern.roommate.fraud_warning",
        "pattern.rental.cta",      "pattern.rental.hero",      "pattern.rental.fraud_warning",
        "pattern.marketplace.cta", "pattern.marketplace.hero",
        "pattern.trip.cta",        "pattern.trip.hero",
        "pattern.service.cta",     "pattern.service.hero",
    };

    private static string Source()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Patterns", "PatternProfile.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 2000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    /// <summary>الكودُ بِلا تَعليقات — أَسطُرُ <c>///</c> و<c>//</c> تُحذَفُ إلى آخِرِ السَطر (لا رابِطَ في المِلَفِّ يُقطَع).</summary>
    private static string Code() => Regex.Replace(Source(), @"//[^\n]*", "");

    [Fact]
    public void No_Arabic_string_literal_is_left_in_PatternProfile_cs()
    {
        var literals = Regex.Matches(Code(), "\"(?:[^\"\\\\\\n]|\\\\.)*\"").Select(m => m.Value).ToList();
        var arabic = literals.Where(l => Regex.IsMatch(l, "[؀-ۿ]")).ToList();
        output.WriteLine($"فُحِصَت {literals.Count} حَرفِيَّةً نَصِّيَّة في PatternProfile.cs، مِنها {arabic.Count} عَرَبِيَّة.");
        Assert.True(literals.Count >= Keys.Length, $"أَداةٌ عَمياء: {literals.Count} حَرفِيَّةً فَقَط.");
        Assert.True(arabic.Count == 0, "نَصٌّ عَرَبِيٌّ حَرفِيٌّ عادَ إلى المِلَفّ:\n" + string.Join("\n", arabic));
    }

    [Fact]
    public void Every_profile_text_reads_its_own_key_from_the_dictionary()
    {
        var read = Regex.Matches(Code(), "Text\\(\"(?<k>[a-z0-9_.]+)\"\\)").Select(m => m.Groups["k"].Value).ToList();
        output.WriteLine($"فُحِصَ {read.Count} نِداءً لِـText في PatternProfile.cs، ومَفاتيحُ الجَدوَلِ {Keys.Length}.");
        Assert.Equal(Keys.OrderBy(k => k, StringComparer.Ordinal), read.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var key in Keys)
        {
            var ar = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
            Assert.False(string.IsNullOrWhiteSpace(ar), $"{key} غائِبٌ عَن ar.json أَو فارِغ");
            Assert.Matches("[؀-ۿ]", ar);
        }
    }
}
