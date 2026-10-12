using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ `IDeliveryProvider` تَبقى بِسَبَبٍ مَكتوب — ويَحمَرُّ هذا يَومَ يَتَغَيَّرُ السَبَب (‏ADR-033 الخُطوَة 9؛ ‏ADR-018 §٣‑أ) ═══════
//
// السَبَبُ المَكتوبُ في رَأسِ المِلَف: «صِفرُ تَنفيذٍ وصِفرُ تَسجيلٍ وصِفرُ مُستَهلِكٍ وَقتَ التَشغيل»، والواجِهَةُ طَرَفُ صَفِّ
// `delivery` في جَدوَلِ القُدُراتِ لا غَير. **والقاعِدَةُ الثانِيَة**: سَبَبٌ لا يُقاسُ يَبلى — فَيَومَ يُنَفِّذُها شَيءٌ أَو
// يُسَجِّلُها (عَودَةُ مُحاكٍ، أَو ناقِلٌ يُمَدَّدُ عَلَيها خِلافَ القاعِدَةِ ١ في ‏ADR-033 §٢‑هـ) يَحمَرُّ هذا الصَفُّ بِاسمِ
// المِلَفّ، فَيُعادُ النَظَرُ في التَعليقِ ولا يَبقى بَعدَ سَبَبِه.

public class DeliveryInterfaceStaysTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string InterfaceFile = "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/IDeliveryProvider.cs";

    /// <summary>تَصريحُ نَوعٍ يَضَعُ الواجِهَةَ في قائِمَةِ أُصولِه — بِمُنشِئٍ أَوَّلِيٍّ أَو بِدونِه.</summary>
    private static readonly Regex Implements = new(
        @"\b(?:class|record|struct)\s+\w+(?:<[^>{;]*>)?\s*(?:\([^)]*\))?\s*:[^{;]*\bIDeliveryProvider\b", RegexOptions.Compiled);

    /// <summary>تَسجيلٌ في الحاوِيَة بِأَيِّ شَكل.</summary>
    private static readonly Regex Registers = new(
        @"\bAdd(?:Singleton|Scoped|Transient)\s*<\s*IDeliveryProvider\b|typeof\s*\(\s*IDeliveryProvider\s*\)|GetRequiredService\s*<\s*IDeliveryProvider\b",
        RegexOptions.Compiled);

    [Fact]
    public void The_delivery_interface_stays_with_zero_implementations_and_zero_registrations_and_says_why()
    {
        var scanned = 0;
        var breaches = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(RepoRoot, f).Replace('\\', '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal)) continue;
            scanned++;
            var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(f));
            if (Implements.IsMatch(code) || Registers.IsMatch(code)) breaches.Add(rel);
        }

        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {scanned} · ما يُنَفِّذُ IDeliveryProvider أَو يُسَجِّلُها: {breaches.Count}");
        Assert.True(scanned > 200, $"أَداةٌ عَمياء: {scanned} مِلَفّاً (المَقيسُ ‏2026-10-12: 279).");
        Assert.True(breaches.Count == 0,
            "سَبَبُ بَقاءِ IDeliveryProvider تَغَيَّر — يُنَفِّذُها أَو يُسَجِّلُها:\n  " + string.Join("\n  ", breaches) +
            $"\nيُعادُ النَظَرُ في رَأسِ {InterfaceFile} (‏ADR-033 الخُطوَة 9) في الكوميتِ نَفسِه.");

        var text = File.ReadAllText(Path.Combine(RepoRoot, InterfaceFile));
        Assert.Contains("public interface IDeliveryProvider", text, StringComparison.Ordinal);
        foreach (var reason in new[]
                 {
                     "ADR-018 §٣‑أ", "ProviderSelectionCharacterizationTests", "شَرطُ مَصيرِها", "ولا <c>Decide</c> لِمُزَوِّدِ شَحن",
                 })
            Assert.Contains(reason, text, StringComparison.Ordinal);
    }

    /// <summary>الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): عَيِّناتٌ مَحقونَةٌ تُمسَكُ، وما لَيسَ تَنفيذاً لا يُمسَك.</summary>
    [Theory]
    [InlineData("public sealed class MockDeliveryProvider : IDeliveryProvider { }", true)]
    [InlineData("internal class Oto(HttpClient http) : IDisposable, IDeliveryProvider { }", true)]
    [InlineData("public record Fake : Base, ACommerce.Kit.Delivery.IDeliveryProvider;", true)]
    [InlineData("services.AddSingleton<IDeliveryProvider, MockDeliveryProvider>();", true)]
    [InlineData("services.AddScoped(typeof(IDeliveryProvider), x);", true)]
    [InlineData("public interface IDeliveryProvider { }", false)]
    [InlineData("public sealed class OtoClient { }", false)]
    [InlineData("var name = nameof(IDeliveryProvider);", false)]
    public void The_scanner_catches_an_implementation_or_a_registration_and_nothing_else(string code, bool caught)
        => Assert.Equal(caught, Implements.IsMatch(code) || Registers.IsMatch(code));
}
