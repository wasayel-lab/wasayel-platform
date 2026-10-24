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

    /// <summary>(‏2026-10-24) <b>وتَعليلُ فَرعِ «تربط طرفين + عمولة» مِن القامُوسِ كَذلِك</b> — رُحِّلَ بِحَرفِه قَبلَ صِياغَتِه
    /// («ب» S11·٥ البَند 5)، و<c>CapabilitiesPromptMoneyCharacterizationTests</c> وجَدوَلُ الفُروعِ أَخضَرانِ بِلا تَعديلِ حَرف.</summary>
    [Fact]
    public void The_commission_reasoning_is_read_from_the_dictionary_not_written_in_the_matcher()
    {
        var key = PatternMatcher.CommissionReasoningKey;
        Assert.Contains(key, LocaleCatalog.Lexicon);
        var value = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(value));

        var s = PatternMatcher.Match(new Dictionary<string, string> { ["offer"] = "twosided", ["payment"] = "commission", ["realtime"] = "no" });
        Assert.Equal("marketplace", s.Pattern);
        Assert.Equal(value, s.ReasoningAr);

        var code = MatcherCode();
        Assert.Contains("LocaleCatalog.Text(LocaleCatalog.Arabic, CommissionReasoningKey)", code, StringComparison.Ordinal);
        Assert.DoesNotContain(value!, code, StringComparison.Ordinal);
    }

    // ═══ الصِياغَة (‏2026-10-13) — نَمَطٌ واحِدٌ يَخدُمُ بائِعاً واحِداً أَو بائِعينَ كُثُراً (‏ADR-032 §٦) ═══

    /// <summary>النَصُّ الجَديدُ بِحَرفِه — ولا يَعِدُ بِمَسارِ ضَمٍّ أَو دَعوَةٍ لا وُجودَ لَه (‏ADR-032 §٦‑ح‑2): البائِعونَ «يُسَجِّلونَ
    /// فيه» بِأَنفُسِهِم (‏<c>SelfGrantPolicy.SelfGrantable</c>)، والمالِكُ بائِعُه الواحِدُ إن شاء.</summary>
    internal const string ProductsReasoning =
        "تبيع منتجات — نمط المتجر نفسه يعمل ببائع واحد (أنت) أو ببائعين متعددين يسجّلون فيه، بلا تبديل نمط.";

    /// <summary>سَطرُ مَرجِعِ القُدُراتِ الجَديدُ بِحَرفِه، والسَطرُ المُضادُّ بَعدَه بِلا شَطرِه المَحذوف.</summary>
    internal const string CapabilitiesOneOrManyLine =
        "- **بائع واحد أو بائعون متعددون**: النمط نفسه — متجر كل منتجاته لصاحبه هو هذا النمط ببائع واحد، ويبيع فيه غيره متى سجّلوا فيه، بلا تبديل نمط (لا نمط أبسط للبائع الواحد).";

    internal const string CapabilitiesContraLine = "- **متى لا يُستخدم (مضاد)**: خدمات تحتاج جدولة معقّدة.";

    [Fact]
    public void The_products_reasoning_says_one_pattern_serves_one_or_many_vendors()
    {
        Assert.Equal(ProductsReasoning, LocaleCatalog.Find(LocaleCatalog.Arabic, PatternMatcher.ProductsReasoningKey));
        Assert.Equal(ProductsReasoning,
            PatternMatcher.Match(new Dictionary<string, string> { ["offer"] = "physical" }).ReasoningAr);
        Assert.DoesNotContain("يناسب البيع المباشر", ProductsReasoning, StringComparison.Ordinal);
        foreach (var promise in new[] { "دعوة", "تدعو", "تضيف", "يضم" })
            Assert.DoesNotContain(promise, ProductsReasoning, StringComparison.Ordinal);
    }

    [Fact]
    public void The_capabilities_reference_says_the_same_pattern_serves_one_or_many_vendors()
    {
        var lines = new SaudiDataProvider().CapabilitiesMarkdown.Split('\n');
        Assert.True(lines.Length > 20, $"أَداةٌ عَمياء: المَورِدُ المُضَمَّنُ {lines.Length} سَطراً.");

        var at = Array.IndexOf(lines, CapabilitiesOneOrManyLine);
        Assert.True(at > 0, "السَطرُ الجَديدُ غائِبٌ عَن المَورِدِ المُضَمَّن.");
        Assert.Equal(CapabilitiesContraLine, lines[at + 1]);
        Assert.Contains("### 1) نمط المتجر متعدد البائعين", lines[..at].Last(l => l.StartsWith("### ", StringComparison.Ordinal)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.Contains("نمط أبسط", StringComparison.Ordinal) && !l.Contains("لا نمط أبسط", StringComparison.Ordinal));
    }
}
