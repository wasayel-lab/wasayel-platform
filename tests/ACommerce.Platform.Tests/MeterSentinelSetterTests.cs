using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ جُزءٌ مِن G15 — لِكُلِّ قيمَةٍ حارِسَةٍ مَوضِعٌ يَضَعُها (القاعِدَة ١)، ولا تُصادِفُ اسمَ مَتجَرٍ ولا اسمَ دَور ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2). القيمَةُ بِلا واضِعٍ تَنمو بِالخَيال؛ فَيُعَدُّ كُلُّ مَرجِعٍ إلَيها في كودِ
// `libs/` و`apps/` (بِلا التَعليقاتِ وبِلا مِلَفِّ تَعريفِها) ويَحمَرُّ ما كانَ صِفراً.

public class MeterSentinelSetterTests(ITestOutputHelper output)
{
    private static IReadOnlyList<(string Name, string Value)> Declared()
        => typeof(MeterSentinels).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!)).ToList();

    /// <summary>مَواضِعُ كُلِّ اسمٍ في الكود — <c>MeterSentinels.{اسم}</c> خارِجَ مِلَفِّ التَعريف، بِلا تَعليقات.</summary>
    internal static IReadOnlyDictionary<string, int> SetterSites(IEnumerable<string> names, IEnumerable<(string Path, string Text)> files)
    {
        var counts = names.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        foreach (var (_, text) in files)
        {
            var code = MeterListenerShapeTests.CodeOf(text);
            foreach (var n in counts.Keys.ToList())
                counts[n] += Regex.Matches(code, $@"\bMeterSentinels\.{n}\b").Count;
        }
        return counts;
    }

    private static IEnumerable<(string Path, string Text)> CodeFiles()
        => new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && Path.GetFileName(f) != "MeterSentinels.cs")
            .Select(f => (f, File.ReadAllText(f)));

    [Fact]
    public void Every_sentinel_has_at_least_one_setter_site_in_libs_or_apps()
    {
        var declared = Declared();
        var files = CodeFiles().ToList();
        var sites = SetterSites(declared.Select(d => d.Name), files);
        output.WriteLine($"[G15] مِلَفّاتٌ مَقروءَة {files.Count} · قِيَمٌ حارِسَة {declared.Count}: "
            + string.Join(" · ", sites.Select(s => $"{s.Key} {s.Value}")));
        Assert.True(files.Count > 400 && declared.Count > 0, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً و{declared.Count} قيمَة.");
        var orphans = sites.Where(s => s.Value == 0).Select(s => s.Key).ToList();
        Assert.True(orphans.Count == 0, "قيمَةٌ حارِسَةٌ بِلا مَوضِعٍ يَضَعُها — تَدخُلُ المُعجَمَ مَعَ واضِعِها: " + string.Join(", ", orphans));
    }

    /// <summary><b>حَقنُ العَيب</b>: قيمَةٌ تُعلَنُ ولا يَضَعُها شَيء — العَدّادُ يَراها صِفراً.</summary>
    [Fact]
    public void An_injected_sentinel_without_a_setter_counts_zero()
    {
        var sites = SetterSites(["None", "Injected"], [("a.cs", "x = MeterSentinels.None; // MeterSentinels.Injected\ny = MeterSentinels.NoneOther;")]);
        Assert.Equal(1, sites["None"]);
        Assert.Equal(0, sites["Injected"]);
    }

    [Fact]
    public void The_sentinels_are_a_closed_lexicon_that_no_store_or_role_slug_can_collide_with()
    {
        var declared = Declared();
        Assert.Equal(declared.Select(d => d.Value).Order(StringComparer.Ordinal), MeterSentinels.All.Order(StringComparer.Ordinal));
        Assert.Equal(declared.Count, MeterSentinels.All.Distinct(StringComparer.Ordinal).Count());
        var storeSlug = new Regex("^[a-z0-9-]+$");
        var roleSlug = new Regex("^[a-z][a-z0-9_]*$");
        Assert.All(MeterSentinels.All, s =>
        {
            Assert.StartsWith("_", s, StringComparison.Ordinal);
            Assert.DoesNotMatch(storeSlug, s);
            Assert.DoesNotMatch(roleSlug, s);
        });
    }
}
