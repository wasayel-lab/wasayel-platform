using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اسمُ خُطوَةِ الطَبَقاتِ الساكِنَةِ يَقولُ ما يُشَغَّل (‏2026-10-25، تَدقيقُ الإطلاق lens-checkers-ci-10) ═════════════════════════
//
// **المَقيسُ قَبلَ** (عِندَ dace7cd): `ci.yml` يُسَمّي الخُطوَةَ «Static verification layers 1-5, 7» وتَعليقُها «الطَبَقات الساكِنَة ١–٥ و٧»،
// و`verify-static.sh` يُشَغِّلُ ١–٥ و٧–١٠ (الثامِنَةُ وُلِدَت ‏2026-08-15، والتاسِعَةُ والعاشِرَةُ مَعَ سَطحِ الـAPI) ويَختِمُ «All nine static
// layers green». ثَلاثُ طَبَقاتٍ أُضيفَت ولَم يَتبَعها الاسمُ لِأَنّ لا شَيءَ يَربِطُه بِالمَصفوفَة — القاعِدَة ٢: الحَدُّ الَّذي لا يُقاسُ يَنهار.
//
// **فَالمَصدَرُ واحِد**: مَصفوفَةُ `LAYERS` في `verify-static.sh`. مِنها تُشتَقُّ المَدَياتُ («1-5, 7-10»)، ويُسأَلُ عَنها اسمُ الخُطوَةِ في
// `ci.yml` وسَطرُ المُلَخَّصِ في السكربتِ وعَدُّ «All nine» — فَطَبَقَةٌ تُضافُ إلى المَصفوفَةِ بِلا الاسمِ تُحمِرُّ هُنا.

public class StaticLayerLabelTests(ITestOutputHelper output)
{
    private static string Read(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {Path.Combine(parts)} طولُه {text.Length}.");
        return text;
    }

    // ‏`"7|Literal-text debt   |verify-i18n-debt.sh"` داخِلَ `LAYERS=( … )`.
    private static readonly Regex LayerRow = new(@"^\s*""(?<n>\d+)\|[^|]*\|[^|""]+\.sh""\s*$", RegexOptions.Multiline);

    /// <summary>أَرقامُ الطَبَقاتِ في مَصفوفَةِ <c>LAYERS</c> بِتَرتيبِها.</summary>
    private static List<int> Layers(string script)
    {
        var open = script.IndexOf("LAYERS=(", StringComparison.Ordinal);
        Assert.True(open >= 0, "verify-static.sh بِلا LAYERS=( — الفاحِصُ لا يَجِدُ مَصدَرَه.");
        var close = script.IndexOf("\n)", open, StringComparison.Ordinal);
        Assert.True(close > open, "LAYERS=( بِلا إغلاق.");
        return LayerRow.Matches(script[open..close]).Select(m => int.Parse(m.Groups["n"].Value)).ToList();
    }

    /// <summary>«1, 2, 3, 4, 5, 7, 8, 9, 10» ⇒ «1-5, 7-10».</summary>
    private static string Ranges(IEnumerable<int> layers)
    {
        var sorted = layers.Distinct().OrderBy(n => n).ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j > i ? $"{sorted[i]}-{sorted[j]}" : $"{sorted[i]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>
    /// خُروقٌ بِرُموزٍ ثابِتَة: <c>ci_step_label</c> اسمُ الخُطوَةِ في <c>ci.yml</c> لا يُطابِقُ المَدَيات ·
    /// <c>summary_label</c> سَطرُ المُلَخَّصِ في السكربت · <c>layer_count_word</c> عَدُّ «All N static layers green».
    /// </summary>
    private static List<string> Breaches(string script, string ci, out string ranges, out int count)
    {
        var layers = Layers(script);
        count = layers.Count;
        ranges = Ranges(layers);
        var breaches = new List<string>();
        // ‏سَطرُ خُطوَةٍ لا تَعليق: `#` قَبلَ `- name:` لا يُعَدّ.
        if (!Regex.IsMatch(ci, $@"^[ \t]*- name: Static verification layers {Regex.Escape(ranges)}[ \t]*$", RegexOptions.Multiline))
            breaches.Add("ci_step_label");
        if (!script.Contains($"Static verification summary (layers {ranges})", StringComparison.Ordinal)) breaches.Add("summary_label");
        if (count >= Words.Length || !script.Contains($"All {Words[count]} static layers green.", StringComparison.Ordinal)) breaches.Add("layer_count_word");
        return breaches;
    }

    [Fact]
    public void The_ci_step_and_the_summary_name_exactly_the_layers_the_script_runs()
    {
        var breaches = Breaches(Read("scripts", "verify-static.sh"), Read(".github", "workflows", "ci.yml"), out var ranges, out var count);
        output.WriteLine($"verify-static.sh: {count} طَبَقَةً في LAYERS ⇒ «{ranges}» · خُروق {breaches.Count}");
        Assert.True(count >= 5, $"أَداةٌ عَمياء: {count} طَبَقَةً مَقروءَةً مِن LAYERS.");
        Assert.True(breaches.Count == 0, string.Join(", ", breaches));
    }

    /// <summary><b>حَقنُ عَيب</b>: الاسمُ القَديمُ يَعود، وطَبَقَةٌ تُضافُ إلى المَصفوفَةِ وَحدَها، وتَعليقٌ يَحمِلُ الاسمَ الصَحيحَ لا يُعَدّ.</summary>
    [Fact]
    public void Every_label_breach_goes_red_on_its_injected_sample()
    {
        var script = Read("scripts", "verify-static.sh");
        var ci = Read(".github", "workflows", "ci.yml");
        Assert.Empty(Breaches(script, ci, out var ranges, out _));
        Assert.Equal("1-3", Ranges([3, 1, 2]));
        Assert.Equal("1, 3", Ranges([3, 1]));
        Assert.Equal("1-3, 5, 7-8", Ranges([1, 2, 3, 5, 7, 8]));

        static string Once(string text, string from, string to)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"عَيِّنَةُ الحَقنِ غائِبَة: {from}");
            return text[..at] + to + text[(at + from.Length)..];
        }

        var step = $"- name: Static verification layers {ranges}\n";
        var row = "    \"10|API contract shape  |verify-api-contract.sh\"\n";
        var cases = new (string Name, string Script, string Ci, string[] Codes)[]
        {
            ("الاسمُ القَديمُ يَعود", script, Once(ci, step, "- name: Static verification layers 1-5, 7\n"), ["ci_step_label"]),
            ("الاسمُ في تَعليقٍ وَحدَه", script, Once(ci, step, $"# {step}      - name: Static verification\n"), ["ci_step_label"]),
            ("طَبَقَةٌ في المَصفوفَةِ وَحدَها", Once(script, row, row + "    \"11|Injected layer     |verify-injected.sh\"\n"), ci,
                ["ci_step_label", "summary_label", "layer_count_word"]),
        };
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = Breaches(c.Script, c.Ci, out var r, out var n);
            output.WriteLine($"«{c.Name}» → {n} طَبَقَة «{r}» · {string.Join(", ", got)}");
            if (!got.SequenceEqual(c.Codes)) missed.Add($"«{c.Name}»: المُنتَظَرُ {string.Join(", ", c.Codes)} والمَقروء {string.Join(", ", got)}");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ فاتَ الفاحِصَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }
}
