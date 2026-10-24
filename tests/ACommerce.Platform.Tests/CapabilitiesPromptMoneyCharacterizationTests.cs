using System.Security.Cryptography;
using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — ما يَحمِلُه مُوَجِّهُ التَحليلِ عَن المالِ عِندَ مَوضِعِ حَقنِه (‏ADR-032 §٢‑ب و§٤؛ «ب» S11·٥ البُنودُ 1 و4 و5) ═══
//
// **ما يُثَبَّتُ هُنا قَبلَ أَيِّ تَبديل** (القاعِدَة ٣) — مَقروءاً عِندَ مَوضِعِ الحَقنِ لا مِن المِلَفِّ عَلى القُرص:
//   ‏`FeasibilityPromptBuilder.Build` يُلحِقُ `SaudiDataProvider.CapabilitiesMarkdown` (المَورِدُ المُضَمَّنُ بِعَينِه) تَحتَ
//   «# قدرات منصة ACommerce» ثُمَّ «# قيود صارمة … لا تخترع أرقاماً»، و`BuildUserMessage` يُلحِقُ `PatternReasoning` المُخَزَّن.
//   والثَلاثَةُ المَقصودَةُ تَعِدُ بِما لا يَفعَلُه الكود:
//   (١) سَطرُ «متى يُستخدم» لِنَمَطِ المَتجَرِ يَقتَرِحُ «عمولة على المعاملة» نَموذَجَ إيراد — ولا آلِيَّةَ عُمولَةٍ بَعدَ ‏`ad828fa`
//       (‏ADR-032 §٢‑ب)، ولا تَقسيمَ لِمالِ صَفقَةٍ بَينَ أَطرافِها؛
//   (٢) سَطرُ «الدفع» يُسَمّي Tap وHyperPay وTamara وTabby — صِفرُ مِلَفٍّ لَها خارِجَ `Data/`، والمَدمَجُ ميسّرُ وَحدَه؛
//   (٣) تَعليلُ فَرعِ «منصة تربط طرفين + عمولة» في `PatternMatcher` يَقولُ «يحصّل الدفع ويوزّعه» — ووَسايِل لا تَمُرُّ بِمالِ
//       صَفقَة (‏ADR-032 §٤: «التوزيعُ يجعلُ وسايل تمسِكُ مالَ غيرِها» بَديلٌ مَرفوض).
//
// **وما لا يُمَسُّ أَبَداً**: بَقِيَّةُ المَرجِعِ بايتاً بايتاً (بَصمَةٌ بِحَجبِ السَطرَينِ وَحدَهُما) — فَيُبَرهِنُ كوميتُ الصِياغَةِ أَنّ
// تَغييرَه مَحصورٌ فيهِما (القاعِدَة ١٣)؛ وصَفُّ «المَرجِعُ يَبلُغُ المُوَجِّهَ بِحَرفِه». يَنقَلِبُ الصُفوفُ الثَلاثَةُ المَقصودَةُ
// بِأَسمائِها في كوميتِ الصِياغَة، ولا يُمَسُّ غَيرُها.

public class CapabilitiesPromptMoneyCharacterizationTests
{
    internal const string MarketplaceWhenLineOld =
        "- **متى يُستخدم**: بيع منتجات مادية أو رقمية من بائعين متعددين، عمولة على المعاملة أو اشتراك بائعين.";

    internal const string PaymentsLineOld =
        "- **الدفع**: Tap, HyperPay, Moyasar (mada/Visa)، وBNPL عبر Tamara/Tabby.";

    internal const string CommissionReasoningOld =
        "تربط بائعين ومشترين وتأخذ عمولة على المعاملة — نمط المتجر متعدد البائعين يحصّل الدفع ويوزّعه.";

    /// <summary>بادِئَتا السَطرَينِ المَحجوبَينِ في البَصمَة — ثابِتَتانِ قَبلَ الصِياغَةِ وبَعدَها.</summary>
    internal static readonly string[] MaskedPrefixes =
    {
        "- **متى يُستخدم**: بيع منتجات مادية",
        "- **الدفع**:",
    };

    /// <summary>بَصمَةُ المَرجِعِ بِحَجبِ السَطرَينِ — مَقيسَةٌ عِندَ ‏2939229 (أَوَّلُ ‏16 مِن SHA-256).</summary>
    internal const string RestOfReferenceFingerprint = "797924f94f9c5629";

    private static string Prompt() =>
        new FeasibilityPromptBuilder(new SaudiDataProvider()).Build(new IncubatorSession(), "- لا دروس");

    private static string[] PromptLines() => Prompt().Replace("\r\n", "\n").Split('\n');

    internal static string MaskedFingerprint(string capabilities)
    {
        var lines = capabilities.Replace("\r\n", "\n").Split('\n')
            .Select(l => MaskedPrefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)) ? "«مَحجوب»" : l);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    [Fact]
    public void The_capabilities_reference_reaches_the_system_prompt_verbatim_under_its_heading()
    {
        var caps = new SaudiDataProvider().CapabilitiesMarkdown;
        Assert.True(caps.Length > 500, $"أَداةٌ عَمياء: المَورِدُ المُضَمَّنُ طولُه {caps.Length} — لَم يُقرَأ.");

        var prompt = Prompt();
        const string heading = "# قدرات منصة ACommerce (اقترح النمط المناسب إن لاءم الفكرة)";
        var at = prompt.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(at >= 0, "عُنوانُ القُدُراتِ غائِبٌ عَن المُوَجِّه.");
        var tail = prompt[(at + heading.Length)..];
        var end = tail.IndexOf("# قيود صارمة", StringComparison.Ordinal);
        Assert.True(end > 0, "«# قيود صارمة» غائِبٌ بَعدَ القُدُرات.");
        Assert.Equal(caps.Replace("\r\n", "\n").Trim(), tail[..end].Replace("\r\n", "\n").Trim());
    }

    [Fact]
    public void The_rest_of_the_capabilities_reference_is_pinned_byte_for_byte_with_the_two_money_lines_masked()
    {
        var caps = new SaudiDataProvider().CapabilitiesMarkdown;
        var masked = caps.Replace("\r\n", "\n").Split('\n')
            .Count(l => MaskedPrefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)));
        Assert.Equal(MaskedPrefixes.Length, masked);
        Assert.Equal(RestOfReferenceFingerprint, MaskedFingerprint(caps));
    }

    /// <summary>(‏2026-10-24، كوميتُ الصِياغَة — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_marketplace_line_offers_the_analyst_a_commission_revenue_model</c>
    /// يُثبِتُ السَطرَ القَديمَ في المُوَجِّه، وصارَ يُثبِتُه غائِباً؛ والجَديدُ مُثَبَّتٌ في <c>CapabilitiesPromptMoneyTests</c>.)</summary>
    [Fact]
    public void The_marketplace_line_no_longer_offers_the_analyst_a_commission_revenue_model()
        => Assert.DoesNotContain(MarketplaceWhenLineOld, PromptLines());

    /// <summary>(‏2026-10-24، كوميتُ الصِياغَة — انقَلَبَ: كانَ <c>Today_the_payments_line_names_gateways_the_code_does_not_integrate</c>.)</summary>
    [Fact]
    public void The_payments_line_no_longer_names_gateways_the_code_does_not_integrate()
        => Assert.DoesNotContain(PaymentsLineOld, PromptLines());

    /// <summary>(‏2026-10-24، كوميتُ الصِياغَة — انقَلَبَ: كانَ <c>Today_the_commission_branch_reasoning_promises_to_collect_and_distribute_the_payment</c>
    /// يُثبِتُ التَعليلَ القَديمَ في المُطابِقِ وفي رِسالَةِ التَحليل، وصارَ يُثبِتُه غائِباً عَنهُما؛ والنَمَطُ والثِقَةُ كَما كانا.)</summary>
    [Fact]
    public void The_commission_branch_reasoning_no_longer_promises_to_collect_and_distribute_the_payment()
    {
        var s = PatternMatcher.Match(new Dictionary<string, string>
            { ["offer"] = "twosided", ["payment"] = "commission", ["realtime"] = "no" });
        Assert.Equal("marketplace", s.Pattern);
        Assert.NotEqual(CommissionReasoningOld, s.ReasoningAr);

        // ‏عِندَ مَوضِعِ الحَقن: التَعليلُ المُخَزَّنُ لا يَبلُغُ رِسالَةَ التَحليلِ بِحَرفِه القَديم.
        var session = new IncubatorSession
        {
            SuggestedPattern = s.Pattern, PatternConfidence = s.Confidence, PatternReasoning = s.ReasoningAr,
        };
        Assert.DoesNotContain($"## النمط المقترح أولياً: marketplace (high) — {CommissionReasoningOld}",
            FeasibilityPromptBuilder.BuildUserMessage(session).Replace("\r\n", "\n").Split('\n'));
    }
}
