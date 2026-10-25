using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ خَليَّةٌ زائِدَةٌ في صَفِّ جَدوَلٍ نَصٌّ لا يُرى ═══
//
// **المَقيسُ** (‏2026-10-25، تَدقيقُ الإطلاق — تَيّارُ W8-3): مِرآةُ مُلحَقِ ADR-032 §٦‑د في فِهرِسِ `docs/README.md` أُلحِقَت **بَعدَ** الخَطِّ
// العَمودِيِّ الخاتِمِ لِلصَفّ (`94426f5`)، فَصارَ الصَفُّ أَربَعَ خَلايا تَحتَ رَأسٍ مِن ثَلاث — وGFM يُسقِطُ ما زادَ عَلى عَدَدِ خَلايا الرَأسِ صامِتاً:
// المُلحَقُ مَكتوبٌ في المِلَفِّ ولا يُرسَمُ في الصَفحَة. وقيسَت الوَثائِقُ كُلُّها: ‏1932 صَفّاً.
//
// **والعَدُّ عَدُّ GFM بِحَرفِه**: الصَفُّ يُقسَمُ عَلى `|` قَبلَ أَيِّ تَحليلٍ داخِلِيّ، فَالخَطُّ داخِلَ `…` فاصِلٌ ما لَم يُهرَّب (`\|`) — مِثالُ المُواصَفَة
// «including inside other inline spans». وبِهذا العَدِّ خَمسَةُ صُفوفٍ قائِمَةٍ قَبلَ التَيّارِ تَزيدُ خَلاياها لِخَطٍّ غَيرِ مُهرَّبٍ داخِلَ شيفرَة
// (فَيُقطَعُ نَصُّها في الصَفحَة): مُثَبَّتَةٌ في <see cref="PinnedPreExisting"/> بِمِلَفِّها ومَقطَعِها — تَتَقَلَّصُ ولا تَنمو، وكُلُّ مُثَبَّتٍ لَم يَعُد زائِداً يُحمِرُّ
// حَتّى يُحذَف (القاعِدَة ١).
//
// القاعِدَة ٢: الحَدُّ الَّذي لا يُقاسُ آلِيّاً يَنهار — فَهذا الفاحِصُ يَعُدُّ الصُفوفَ ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).
public class DocsTableCellCountTests(ITestOutputHelper output)
{
    private static readonly Regex Delimiter = new(@"^\s*\|?\s*:?-{3,}", RegexOptions.Compiled);

    /// <summary>صُفوفٌ زائِدَةُ الخَلايا قَبلَ هذا الفاحِص — مِلَفٌّ ومَقطَعٌ يَرِدُ في الصَفّ. <b>تَتَقَلَّصُ ولا تَنمو.</b></summary>
    internal static readonly (string File, string Fragment)[] PinnedPreExisting =
    [
        ("docs/ADR-032-THE-PRODUCT-IS-A-MULTI-VENDOR-STORE-SAAS-PAID-BY-SUBSCRIPTION.md", "`Aad = tenant|capability|field`"),
        ("docs/ADR-032-THE-PRODUCT-IS-A-MULTI-VENDOR-STORE-SAAS-PAID-BY-SUBSCRIPTION.md", "`pattern: \"^sk_(live|test)_\"`"),
        ("docs/README.md", "`active|revoked`"),
        ("docs/RELEASE-READINESS.md", "`GET …/checkout?step=1|2|3`"),
        ("docs/VERIFICATION-LAYERS.md", "`|| true`"),
    ];

    /// <summary>عَدَدُ خَلايا صَفٍّ كَما يَعُدُّها GFM: الخَطُّ المُهرَّبُ <c>\|</c> لَيسَ فاصِلاً — وداخِلَ <c>`…`</c> فاصِلٌ كَغَيرِه —
    /// والخَطّانِ الطَرَفِيّانِ لا يَفتَحانِ خَليَّة.</summary>
    internal static int Cells(string row)
    {
        var s = row.Trim();
        s = s.Replace("\\|", "x", StringComparison.Ordinal);
        if (s.StartsWith('|')) s = s[1..];
        if (s.EndsWith('|')) s = s[..^1];
        return s.Split('|').Length;
    }

    [Fact]
    public void No_table_row_in_the_docs_has_more_cells_than_its_header()
    {
        var docs = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs");
        var files = Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("Wasayel_Package", StringComparison.Ordinal))
            .ToList();

        var rows = 0;
        var tables = 0;
        var excess = new List<string>();
        var pinnedSeen = new HashSet<int>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (!lines[i].TrimStart().StartsWith('|') || !Delimiter.IsMatch(lines[i + 1])) continue;
                tables++;
                var header = Cells(lines[i]);
                var k = i + 2;
                for (; k < lines.Length && lines[k].TrimStart().StartsWith('|'); k++)
                {
                    rows++;
                    var cells = Cells(lines[k]);
                    if (cells <= header) continue;
                    var rel = Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, file).Replace('\\', '/');
                    var pin = Array.FindIndex(PinnedPreExisting, p => p.File == rel && lines[k].Contains(p.Fragment, StringComparison.Ordinal));
                    if (pin >= 0) pinnedSeen.Add(pin);
                    else excess.Add($"{rel}:{k + 1} — {cells} خَلايا تَحتَ رَأسٍ مِن {header}");
                }
                i = k - 1;
            }
        }

        var stale = Enumerable.Range(0, PinnedPreExisting.Length).Where(i => !pinnedSeen.Contains(i))
            .Select(i => $"{PinnedPreExisting[i].File} {PinnedPreExisting[i].Fragment}").ToList();
        output.WriteLine($"مِلَفّات: {files.Count} · جَداوِل: {tables} · صُفوف: {rows} · زائِدَةٌ جَديدَة: {excess.Count} · مُثَبَّتَةٌ قائِمَة: {pinnedSeen.Count}/{PinnedPreExisting.Length}");
        Assert.True(rows >= 1000, $"أَداةٌ عَمياء: {rows} صَفّاً في {files.Count} مِلَفّاً — والمَقيسُ ‏1932.");
        Assert.True(excess.Count == 0, "صُفوفٌ تَزيدُ خَلاياها عَلى رَأسِها — والزائِدُ لا يُرسَم:\n" + string.Join("\n", excess));
        Assert.True(stale.Count == 0, "مُثَبَّتٌ لَم يَعُد زائِداً — احذِفه مِن PinnedPreExisting (القائِمَةُ تَتَقَلَّص):\n" + string.Join("\n", stale));
    }

    /// <summary>الفاحِصُ يَعُدُّ كَما يَعُدُّ GFM — يُقاسُ قَبلَ أَن يوثَقَ بِه (القاعِدَة ١٠).</summary>
    [Theory]
    [InlineData("| a | b | c |", 3)]
    [InlineData("| a | b | c | d", 4)]          // الخَطُّ الخاتِمُ قَبلَ المُلحَق — شَكلُ العَيبِ المَقيس
    [InlineData("| a | `x|y` | c |", 4)]       // الخَطُّ داخِلَ الشيفرَةِ فاصِلٌ في GFM ما لَم يُهرَّب
    [InlineData("| a | `x\\|y` | c |", 3)]
    [InlineData("| a | b \\| c | d |", 3)]     // الخَطُّ المُهرَّبُ لَيسَ فاصِلاً
    [InlineData("a | b", 2)]
    public void The_cell_counter_counts_like_the_renderer(string row, int expected)
        => Assert.Equal(expected, Cells(row));
}
