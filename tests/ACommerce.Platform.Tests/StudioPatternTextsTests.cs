using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ نَصّا النَمَطِ بَعدَ التَرحيل (‏ADR-032 §٦‑ج الصَفّ 3(أ)(ب)) ═══
//
// **التَرحيلُ أَوَّلاً بايتاً بايتاً** (القاعِدَة ١١): تَعليلُ فَرعِ «تَبيعُ مُنتَجات» خَرَجَ مِن `PatternMatcher.cs` إلى
// `ar.json` بِحَرفِه، و`StudioPatternTextsCharacterizationTests` بَقِيَ أَخضَرَ بِلا تَعديلِ حَرف — وهو البُرهان. وهذا المِلَفُّ
// يُثَبِّتُ أَنّ النَصَّ يُقرَأُ مِن القامُوسِ لا مِن الكود، فَلا يَعودُ حَرفِيّاً صامِتاً.

public class StudioPatternTextsTests
{
    private static string MatcherCode()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Incubator", "PatternMatcher.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                                                         && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
    }

    /// <summary><b>التَعليلُ مِن القامُوسِ بِالعَرَبِيَّةِ صَراحَةً</b>: المِفتاحُ في المَعجَمِ المُغلَق، والمُطابِقُ يُرجِعُ قيمَتَه
    /// لِلفَرعَينِ بِعَينِها، ولا يَحمِلُ الكودُ النَصَّ.</summary>
    [Theory]
    [InlineData("physical", "oneoff")]
    [InlineData("digital", "commission")]
    public void The_products_reasoning_is_read_from_the_dictionary_not_written_in_the_matcher(string offer, string payment)
    {
        var key = PatternMatcher.ProductsReasoningKey;
        Assert.Contains(key, LocaleCatalog.Lexicon);
        var value = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(value));

        var s = PatternMatcher.Match(new Dictionary<string, string> { ["offer"] = offer, ["payment"] = payment, ["realtime"] = "no" });
        Assert.Equal("marketplace", s.Pattern);
        Assert.Equal(value, s.ReasoningAr);

        var code = MatcherCode();
        Assert.Contains("LocaleCatalog.Text(LocaleCatalog.Arabic, ProductsReasoningKey)", code, StringComparison.Ordinal);
        Assert.DoesNotContain(value!, code, StringComparison.Ordinal);
    }
}
