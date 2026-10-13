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
//
// **ووُسِّعَ الفاحِصُ ‏2026-10-13 (مُراجَعَةُ المَوجَة 3، G4 (xxix)) — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق**: كانَ يَعمى عَن صيَغِ تَسجيلٍ شائِعَة
// (`TryAdd…` و`AddKeyed…` و`ServiceDescriptor.…<>` والاسمِ المُؤَهَّلِ بِمَجالِه و`GetService<>`/`GetServices<>`) وعَن «المُستَهلِك» كُلِّه —
// والرَأسُ يَدَّعي «صِفرَ مُستَهلِكٍ وَقتَ التَشغيل». صارَ يَمسِكُ الثَلاثَة: تَنفيذاً، وتَسجيلاً أَو حَلّاً مِن الحاوِيَة، ومُستَهلِكاً بِنَوعِها
// (مُعامِلُ مُنشِئٍ أَو دالَّة، وحَقلٌ أَو خاصِّيَّة، ومُتَغَيِّرٌ ونَمَطُ `is`، و`as` والقَلب) — بِعَيِّناتٍ مَحقونَةٍ لِكُلِّ صيغَةٍ مُوجَبَةً وسالِبَة.

public class DeliveryInterfaceStaysTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string InterfaceFile = "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/IDeliveryProvider.cs";

    /// <summary>تَصريحُ نَوعٍ يَضَعُ الواجِهَةَ في قائِمَةِ أُصولِه — بِمُنشِئٍ أَوَّلِيٍّ أَو بِدونِه.</summary>
    private static readonly Regex Implements = new(
        @"\b(?:class|record|struct)\s+\w+(?:<[^>{;]*>)?\s*(?:\([^)]*\))?\s*:[^{;]*\bIDeliveryProvider\b", RegexOptions.Compiled);

    /// <summary>تَسجيلٌ في الحاوِيَة بِأَيِّ شَكل.
    /// (‏2026-10-13، G4 (xxix): و<c>TryAdd…</c> و<c>AddKeyed…</c>، و<c>ServiceDescriptor.…&lt;&gt;</c>، والحَلُّ بِـ<c>GetService&lt;&gt;</c>
    /// و<c>GetServices&lt;&gt;</c> و<c>GetKeyedService&lt;&gt;</c> — والاسمُ مُؤَهَّلاً بِمَجالِه أَو بِـ<c>global::</c>.)</summary>
    private const string Name = @"(?:global::)?(?:\w+\.)*IDeliveryProvider\b";

    private static readonly Regex Registers = new(
        @"\b(?:Try)?Add(?:Keyed)?(?:Singleton|Scoped|Transient)\s*<\s*" + Name +
        @"|\bServiceDescriptor\s*\.\s*\w+\s*<\s*" + Name +
        @"|typeof\s*\(\s*" + Name + @"\s*\)" +
        @"|\bGet(?:Required)?(?:Keyed)?Services?\s*<\s*" + Name,
        RegexOptions.Compiled);

    /// <summary>مُستَهلِكٌ بِنَوعِها (‏2026-10-13، G4 (xxix)): تَصريحٌ يَحمِلُ النَوعَ — مُعامِلُ مُنشِئٍ أَو دالَّةٍ أَو لامدا، حَقلٌ أَو خاصِّيَّة، مُتَغَيِّرٌ
    /// أَو نَمَطُ <c>is</c> — مُباشِراً أَو داخِلَ قَوسَي نَوعٍ (<c>IEnumerable&lt;IDeliveryProvider&gt; all</c>)؛ و<c>as</c>، والقَلبُ. وتَصريحُ الواجِهَةِ
    /// نَفسِها لَيسَ مُستَهلِكاً.</summary>
    private static readonly Regex Consumes = new(
        @"(?<!\binterface\s+)\b" + Name + @"\??(?:\s*>)*\s+@?[A-Za-z_]\w*" +
        @"|\bas\s+" + Name +
        @"|\(\s*" + Name + @"\s*\)\s*[\w(]",
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
            if (Implements.IsMatch(code) || Registers.IsMatch(code) || Consumes.IsMatch(code)) breaches.Add(rel);
        }

        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {scanned} · ما يُنَفِّذُ IDeliveryProvider أَو يُسَجِّلُها أَو يَستَهلِكُها: {breaches.Count}");
        Assert.True(scanned > 200, $"أَداةٌ عَمياء: {scanned} مِلَفّاً (المَقيسُ ‏2026-10-12: 279).");
        Assert.True(breaches.Count == 0,
            "سَبَبُ بَقاءِ IDeliveryProvider تَغَيَّر — يُنَفِّذُها أَو يُسَجِّلُها أَو يَستَهلِكُها:\n  " + string.Join("\n  ", breaches) +
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
    // (‏2026-10-13، G4 (xxix)): صيَغُ التَسجيلِ والحَلِّ الَّتي كانَ يَعمى عَنها — مُوجَبَة.
    [InlineData("services.TryAddSingleton<IDeliveryProvider, Oto>();", true)]
    [InlineData("services.AddKeyedScoped<IDeliveryProvider, Oto>(\"oto\");", true)]
    [InlineData("services.TryAddKeyedTransient<ACommerce.Kit.Delivery.IDeliveryProvider, Oto>(\"oto\");", true)]
    [InlineData("services.AddSingleton<global::ACommerce.Kit.Delivery.IDeliveryProvider>(oto);", true)]
    [InlineData("services.Add(ServiceDescriptor.Singleton<IDeliveryProvider, Oto>());", true)]
    [InlineData("var d = sp.GetService<IDeliveryProvider>();", true)]
    [InlineData("var all = sp.GetServices<IDeliveryProvider>();", true)]
    [InlineData("var k = sp.GetRequiredKeyedService<IDeliveryProvider>(\"oto\");", true)]
    // والمُستَهلِكُ بِنَوعِها — مُوجَب.
    [InlineData("public Shipping(IDeliveryProvider delivery) { }", true)]
    [InlineData("public sealed class Shipping(IDeliveryProvider? delivery) { }", true)]
    [InlineData("private readonly IEnumerable<IDeliveryProvider> _all;", true)]
    [InlineData("app.MapPost(\"/x\", ([FromServices] IDeliveryProvider d) => 1);", true)]
    [InlineData("if (x is IDeliveryProvider dp) { }", true)]
    [InlineData("var d = x as IDeliveryProvider;", true)]
    [InlineData("var d = (IDeliveryProvider)sp.GetService(t);", true)]
    // وما لَيسَ تَنفيذاً ولا تَسجيلاً ولا استِهلاكاً — سالِب.
    [InlineData("public interface IDeliveryProvider\n{\n    string ProviderName { get; }\n}", false)]
    [InlineData("string IDeliveryProviderName = \"\";", false)]
    [InlineData("services.AddSingleton<OtoClient>();", false)]
    [InlineData("var name = typeof(OtoClient).Name;", false)]
    public void The_scanner_catches_an_implementation_or_a_registration_and_nothing_else(string code, bool caught)
        => Assert.Equal(caught, Implements.IsMatch(code) || Registers.IsMatch(code) || Consumes.IsMatch(code));
}
