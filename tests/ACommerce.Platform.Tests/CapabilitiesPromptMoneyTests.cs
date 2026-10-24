using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ ما يَحمِلُه مُوَجِّهُ التَحليلِ عَن المالِ بَعدَ الصِياغَة (‏2026-10-24؛ ADR-032 §٢‑ب و§٤؛ «ب» S11·٥ البُنودُ 1 و4 و5) ═══
//
// **ما يوجَدُ في الكودِ لا ما يُرجى**: لا آلِيَّةَ عُمولَة (‏`ad828fa`)، ولا تَقسيمَ لِمالِ صَفقَة (‏ADR-032 §٤ — بَديلٌ مَرفوض)،
// والدَفعُ بِالبِطاقَةِ عَبرَ مِفتاحِ ميسّر السِرِّيِّ لِحِسابِ صاحِبِ المَتجَر (‏`moyasar_api.provider.json` — «حِسابُ مُيَسِّر حِسابُك،
// ووَسايِل لا تَمُرّ بِمالِه») أَو نَقداً عِندَ الاستِلام. والنُصوصُ القَديمَةُ بِحَرفِها في <c>CapabilitiesPromptMoneyCharacterizationTests</c>،
// وبَقِيَّةُ المَرجِعِ مُثَبَّتَةٌ هُناكَ بِبَصمَتِها — فَالفَرقُ مَحصورٌ في السَطرَينِ والتَعليل.

public class CapabilitiesPromptMoneyTests
{
    internal const string MarketplaceWhenLine =
        "- **متى يُستخدم**: بيع منتجات مادية أو رقمية من بائعين متعددين، اشتراك بائعين (لا عمولة على المعاملة: المنصة لا تقتطع من مال الصفقة ولا تقسمه بين البائعين).";

    internal const string PaymentsLine =
        "- **الدفع**: Moyasar (mada/Visa) بالمفتاح السري لحساب صاحب المتجر نفسه — المال يصل حسابه مباشرة ولا تمر به المنصة؛ والدفع عند الاستلام.";

    internal const string CommissionReasoning =
        "تربط بائعين ومشترين وتأخذ عمولة على المعاملة — نمط المتجر متعدد البائعين يجمعهم في متجر واحد، والدفع يصل حساب صاحب المتجر بمفتاح ميسر الخاص به أو نقداً عند الاستلام؛ والمنصة لا تقتطع عمولة ولا توزّع المال.";

    private static string[] PromptLines() =>
        new FeasibilityPromptBuilder(new SaudiDataProvider()).Build(new IncubatorSession(), "- لا دروس")
            .Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void The_marketplace_line_reaches_the_prompt_and_offers_no_commission_model()
    {
        var lines = PromptLines();
        Assert.True(lines.Length > 40, $"أَداةٌ عَمياء: المُوَجِّهُ {lines.Length} سَطراً.");
        Assert.Contains(MarketplaceWhenLine, lines);
        Assert.DoesNotContain(lines, l => l.Contains("عمولة على المعاملة أو", StringComparison.Ordinal));
    }

    [Fact]
    public void The_payments_line_reaches_the_prompt_and_names_only_what_the_code_integrates()
    {
        var lines = PromptLines();
        Assert.Contains(PaymentsLine, lines);

        // ‏الأَسماءُ الأَربَعَةُ الَّتي كانَ يَعِدُ بِها لا تَبقى في مَرجِعِ القُدُراتِ في أَيِّ سَطر — وصِفرُ مِلَفٍّ لَها خارِجَ `Data/`
        // («ب» S11·٥ البَند 1). (المَرجِعُ لا المُوَجِّهُ كُلُّه: بَياناتُ المُنافِسينَ المَحقونَةُ قَد تُسَمّي بَعضَها مُنافِساً لا تَكامُلاً.)
        var caps = new SaudiDataProvider().CapabilitiesMarkdown.Replace("\r\n", "\n").Split('\n');
        var named = new[] { "Tap", "HyperPay", "Tamara", "Tabby" };
        foreach (var gateway in named)
            Assert.DoesNotContain(caps, l => l.Contains(gateway, StringComparison.Ordinal));

        // ‏وما يُسَمّيه مَدمَجٌ فِعلاً: تَعريفُ ميسّر بِالمِفتاحِ السِرِّيّ في الكاتالوج، وحِسابُه لِلمَتجَر لا لِلمَنَصَّة.
        var definition = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "core", "ACommerce.Platform.Providers", "Definitions", "moyasar_api.provider.json");
        var json = File.ReadAllText(definition);
        Assert.Contains("\"kind\": \"secret_key\"", json, StringComparison.Ordinal);
        Assert.Contains("ووَسايِل لا تَمُرّ بِمالِه", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_commission_branch_reasoning_says_the_platform_neither_takes_nor_distributes_the_money()
    {
        Assert.Equal(CommissionReasoning, LocaleCatalog.Find(LocaleCatalog.Arabic, PatternMatcher.CommissionReasoningKey));

        var s = PatternMatcher.Match(new Dictionary<string, string>
            { ["offer"] = "twosided", ["payment"] = "commission", ["realtime"] = "no" });
        Assert.Equal(("marketplace", "high"), (s.Pattern, s.Confidence));
        Assert.Equal(CommissionReasoning, s.ReasoningAr);
        foreach (var promise in new[] { "يحصّل الدفع", "ويوزّعه" })
            Assert.DoesNotContain(promise, s.ReasoningAr, StringComparison.Ordinal);

        var session = new IncubatorSession
        {
            SuggestedPattern = s.Pattern, PatternConfidence = s.Confidence, PatternReasoning = s.ReasoningAr,
        };
        Assert.Contains($"## النمط المقترح أولياً: marketplace (high) — {CommissionReasoning}",
            FeasibilityPromptBuilder.BuildUserMessage(session).Replace("\r\n", "\n").Split('\n'));
    }
}
