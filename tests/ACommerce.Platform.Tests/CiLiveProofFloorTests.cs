using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَرضِيَّةُ CI لا تَنزِلُ دونَ ما لا يَستَطيعُ أَن يَرجِعَ مُبكِراً (‏2026-11-05، الموجَة ١٢ G5 · G-DOCS-1) ═══
//
// **المَقيسُ قَبل**: الرَقَمُ في `ci.yml` (`./scripts/live-proof-floor.sh live-results/live.trx 55`) كُتِبَ يَومَ ‏2026-10-25 (`bcd9423`) عَلى ‏39 صِنفاً
// حَيّاً، ولَم يَمَسَّه إيداعٌ بَعدَه، والتَعليقُ فَوقَه يَقولُ «يُرفَعُ مَعَ كُلِّ بُرهانٍ حَيٍّ يُضاف». وصارَت الأَصنافُ 64 وما يَبلُغُ القاعِدَةَ عَلى
// قاعِدَةٍ فارِغَةٍ بِمُرَشِّحِ CI نَفسِه مِئَتَين ونَيِّفاً — فَكانَ نَحوُ مِئَتَي بُرهانٍ يَستَطيعُ أَن يَصيرَ يَرجِعُ قَبلَ قاعِدَتِه والخُطوَةُ خَضراء:
// الأَداةُ العَمياءُ نَفسُها الَّتي بُنِيَت الخُطوَةُ لِتَمنَعَها (القاعِدَة ١٠).
//
// **والحارِسُ هُنا حَدٌّ أَدنى مَحسوبٌ لا رَقَمٌ مَنقول** (القاعِدَة ٢): كُلُّ صِنفِ `Live…ProofTests` في مُرَشِّحِ CI لا يَحمِلُ رُجوعاً مِن حارِسٍ فَرعِيٍّ
// (`LiveProofJobTests.SubGuardReturns` — الفاحِصُ القائِمُ لا نُسخَتُه، القاعِدَة ٨) يَبلُغُ القاعِدَةَ بِصَفٍّ واحِدٍ عَلى الأَقَلّ، فَالأَرضِيَّةُ دونَ عَدَدِها
// عَمياءُ بِالبُرهان. والرَقَمُ نَفسُه في `ci.yml` مَقيسٌ (عَدُّ «بَلَغَ القاعِدَة» عَلى قاعِدَةٍ فارِغَة)، وهذا الحارِسُ يَمنَعُ نُزولَه تَحتَ ما يُحسَب.
// **وحُدودُه — تُقال**: يَعُدُّ أَصنافاً لا صُفوفاً (صَفُّ `Theory` بِبَياناتِه لا يُعَدُّ مِن المَصدَر)، فَلا يَمنَعُ أَرضِيَّةً بَينَ عَدَدِ الأَصنافِ والمَقيس؛
// ويَرى ما يَراه `SubGuardReturns` بِحُدودِه المَكتوبَةِ فَوقَه.
public class CiLiveProofFloorTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>رُموزُ الخَرقِ — مُعجَمٌ مُغلَق: <c>floor_missing</c> (لا نِداءَ لِـ<c>live-proof-floor.sh</c> بِرَقَمٍ في <c>ci.yml</c>) ·
    /// <c>floor_below_unreturning</c> (الأَرضِيَّةُ دونَ عَدَدِ الأَصنافِ الَّتي لا تَرجِعُ مُبكِراً).</summary>
    internal static readonly IReadOnlyList<string> Codes = ["floor_missing", "floor_below_unreturning"];

    internal static string? Breach(int? floor, int unreturning) =>
        floor is null ? "floor_missing"
        : floor < unreturning ? "floor_below_unreturning"
        : null;

    /// <summary>الرَقَمُ الَّذي تُمَرِّرُه <c>ci.yml</c> لِـ<c>live-proof-floor.sh</c> — في سَطرٍ غَيرِ مُعَلَّق.</summary>
    internal static int? FloorOf(string ci)
    {
        foreach (var line in ci.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.TrimStart().StartsWith('#')) continue;
            var m = Regex.Match(line, @"\./scripts/live-proof-floor\.sh\s+\S*live\.trx\s+(?<n>\d+)\s*$");
            if (m.Success) return int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    /// <summary>ما يَستَثنيه مُرَشِّحُ الخُطوَةِ الحَيَّةِ بِـ<c>FullyQualifiedName!~</c>.</summary>
    internal static IReadOnlyList<string> ExcludedOf(string ci) =>
        Regex.Matches(ci, @"FullyQualifiedName!~(?<x>[\w.]+)").Select(m => m.Groups["x"].Value).Distinct().ToList();

    /// <summary>أَصنافُ <c>Live…ProofTests</c> المُعلَنَةُ في المَصادِر (عَقدُ <c>LiveProofCollectionTests</c> نَفسُه)، مَقسومَةً: لا تَرجِعُ مِن حارِسٍ فَرعِيّ ·
    /// تَرجِع · يَستَثنيها المُرَشِّح.</summary>
    internal static (IReadOnlyList<string> Unreturning, IReadOnlyList<string> Returning, IReadOnlyList<string> Excluded) Classes(
        IEnumerable<string> sources, IReadOnlyList<string> excluded)
    {
        var declared = new SortedSet<string>(StringComparer.Ordinal);
        var returning = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in sources)
        {
            var code = WriteEndpointGuardTests.StripComments(raw);
            foreach (Match m in Regex.Matches(code, @"public\s+(?:sealed\s+)?class\s+(?<c>Live\w*ProofTests)\b")) declared.Add(m.Groups["c"].Value);
            foreach (var r in LiveProofJobTests.SubGuardReturns(code)) returning.Add(r.Where[..r.Where.IndexOf(':')]);
        }
        var outOfFilter = declared.Where(c => excluded.Any(x => c.Contains(x, StringComparison.Ordinal) || x.Contains(c, StringComparison.Ordinal))).ToList();
        var inFilter = declared.Except(outOfFilter).ToList();
        return (inFilter.Where(c => !returning.Contains(c)).ToList(), inFilter.Where(returning.Contains).ToList(), outOfFilter);
    }

    private static string Ci() =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests", "ACommerce.Platform.Tests"), "*.cs")
            .Where(f => Path.GetFileName(f) is not ("LiveProofJobTests.cs" or "CiLiveProofFloorTests.cs"))   // ‏عَيِّناتُ الفاحِصَينِ نُصوصٌ لا أَصناف
            .Select(File.ReadAllText);

    [Fact]
    public void The_ci_live_floor_is_not_below_the_live_proofs_that_cannot_return_early()
    {
        var ci = Ci();
        var floor = FloorOf(ci);
        var excluded = ExcludedOf(ci);
        var (unreturning, returning, outOfFilter) = Classes(Sources(), excluded);
        output.WriteLine($"أَرضِيَّةُ ci.yml: {floor?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"} · أَصنافُ البَراهينِ الحَيَّةِ في مُرَشِّحِ CI: "
            + $"{unreturning.Count + returning.Count} · لا تَرجِعُ مِن حارِسٍ فَرعِيّ: {unreturning.Count} · تَرجِع: {returning.Count} ({string.Join(", ", returning)}) · "
            + $"يَستَثنيها المُرَشِّح: {outOfFilter.Count} ({string.Join(", ", outOfFilter)})");
        Assert.True(unreturning.Count > 20, $"أَداةٌ عَمياء: {unreturning.Count} صِنفاً لا يَرجِعُ مُبكِراً.");
        Assert.True(Breach(floor, unreturning.Count) is null,
            $"{Breach(floor, unreturning.Count)}: أَرضِيَّةُ ci.yml {floor} دونَ {unreturning.Count} صِنفاً حَيّاً لا يَرجِعُ مُبكِراً — كُلٌّ مِنها يَبلُغُ القاعِدَةَ بِصَفٍّ عَلى الأَقَلّ، "
            + "فَالخُطوَةُ تَبقى خَضراءَ وبَراهينُ تَرجِعُ قَبلَ قاعِدَتِها. ارفَع الرَقَمَ إلى «بَلَغَ القاعِدَة» مَقيساً عَلى قاعِدَةٍ فارِغَةٍ بِمُرَشِّحِ CI.");
    }

    [Theory]
    [InlineData(null, 5, "floor_missing")]
    [InlineData(10, 5, null)]
    [InlineData(4, 5, "floor_below_unreturning")]
    [InlineData(5, 5, null)]
    public void Every_floor_breach_code_has_a_positive_and_a_negative_row(int? floor, int unreturning, string? code)
    {
        Assert.Equal(code, Breach(floor, unreturning));
        Assert.True(code is null || Codes.Contains(code));
    }

    /// <summary>والحارِسُ يَحمَرُّ فِعلاً عَلى <c>ci.yml</c> الحَقيقيِّ مَحقوناً: رَقَمٌ دونَ العَدَدِ بِواحِد، والرَقَمُ القَديمُ ‏55، وسَطرٌ مُعَلَّقٌ لا يُقرَأ.</summary>
    [Fact]
    public void The_floor_guard_goes_red_on_an_injected_low_literal()
    {
        var ci = Ci();
        var (unreturning, _, _) = Classes(Sources(), ExcludedOf(ci));
        Assert.True(unreturning.Count > 20, $"أَداةٌ عَمياء: {unreturning.Count}.");
        string WithFloor(string n) => Regex.Replace(ci, @"(\./scripts/live-proof-floor\.sh\s+\S*live\.trx\s+)\d+", "${1}" + n);
        var cases = new (string Name, string Text, string Code)[]
        {
            ("دونَ العَدَدِ بِواحِد", WithFloor((unreturning.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), "floor_below_unreturning"),
            ("الرَقَمُ القَديم 55", WithFloor("55"), "floor_below_unreturning"),
            ("سَطرُ الأَرضِيَّةِ مُعَلَّق", Regex.Replace(ci, @"(?m)^(\s+)(run: \./scripts/live-proof-floor\.sh)", "$1# $2"), "floor_missing"),
        };
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = Breach(FloorOf(c.Text), unreturning.Count);
            output.WriteLine($"«{c.Name}» → {got ?? "لا خَرق"}");
            if (got != c.Code) missed.Add($"«{c.Name}»: {got ?? "لا خَرق"} لا {c.Code}");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ فاتَ الحارِسَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }

    /// <summary>وتَقسيمُ الأَصنافِ يَرى ما يَعُدُّه: صِنفٌ بِحارِسِ العَلَمِ وَحدَه لا يَرجِع، وصِنفٌ بِحارِسٍ فَرعِيٍّ يَرجِع، والمُستَثنى بِالمُرَشِّحِ خارِجَهُما.</summary>
    [Fact]
    public void The_class_split_sees_what_it_counts()
    {
        // ‏الإعلانُ مَقطوعٌ بِـ`+` عَمداً: `LiveProofCollectionTests` و`SubGuardReturns` يَمسَحانِ نَصَّ هذا المِلَفِّ أَيضاً، وعَيِّنَةٌ مُتَّصِلَةٌ تُعَدُّ صِنفاً.
        const string Decl = "public class ", Sealed = "public sealed class ";
        var sources = new[]
        {
            Decl + "LiveAProofTests { void T() { if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return; } }",
            Sealed + "LiveBProofTests { void T() { if (!Enabled) return; if (doc is null) { output.WriteLine($\"{LiveProofJobTests.EarlyReturnMarker}\"); return; } } }",
            Decl + "LiveCProofTests { }",
            Decl + "NotLiveProofHelper { }",
        };
        var (unreturning, returning, excluded) = Classes(sources, ["LiveCProofTests"]);
        Assert.Equal(["LiveAProofTests"], unreturning);
        Assert.Equal(["LiveBProofTests"], returning);
        Assert.Equal(["LiveCProofTests"], excluded);
    }
}
