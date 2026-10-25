using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>فِهرِسُ الوَثائِقِ مِرآةٌ لِمُلحَقاتِ كُلِّ ADR</b> (‏2026-10-25، تَدقيقُ الإطلاق lens-docs-vs-code-10، تَيّارُ W8-6). <b>المَقيسُ قَبل</b>: صُفوفُ
/// <c>docs/README.md</c> لِـADR-032 و033 و035 و036 لا تَذكُرُ مُلحَقاتٍ مُؤَرَّخَةً فيها — ثَلاثَةَ عَشَرَ عُنواناً «إلحاقُ ‏…» بِلا تَسمِيَةٍ في صَفِّه (مِنها
/// §٨‑ح و§٧‑ص و§١٢)، فَمَن يَقرَأُ الفِهرِسَ لِيَعرِفَ ما تَغَيَّرَ لا يَراها. <b>وصارَ</b>: كُلُّ عُنوانِ مُلحَقٍ في ADR (<c>## N. إلحاقُ</c> أَو <c>### حَرف. إلحاقُ</c>
/// تَحتَ <c>## N.</c>) يُسَمّى في صَفِّ ذلكَ الـADR بِرَمزِه (<c>§N</c> أَو <c>§N‑حَرف</c>) — يَطبَعُ ما عَدَّ ويَفشَلُ عَلى الصِفر، ويَحمَرُّ عَلى صَفٍّ مَصنوعٍ بِلا الرَمز.</para>
/// </summary>
public class ReadmeAdrMirrorTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly Regex Major = new(@"^## (?<n>[٠-٩]+)\. ", RegexOptions.Compiled);
    private static readonly Regex MajorAppend = new(@"^## (?<n>[٠-٩]+)\. إلحاقُ", RegexOptions.Compiled);
    private static readonly Regex MinorAppend = new(@"^### (?<l>[ء-ي]+)\. إلحاقُ", RegexOptions.Compiled);

    /// <summary>رُموزُ عَناوينِ المُلحَقاتِ في نَصِّ ADR بِتَرتيبِها — <c>§N</c> لِلعُنوانِ الأَوَّل، و<c>§N‑حَرف</c> لِما تَحتَه.</summary>
    internal static IReadOnlyList<string> AppendLabels(string adr)
    {
        var labels = new List<string>();
        string? major = null;
        foreach (var line in adr.Split('\n'))
        {
            if (Major.Match(line) is { Success: true } m) major = m.Groups["n"].Value;
            if (MajorAppend.Match(line) is { Success: true } ma) labels.Add("§" + ma.Groups["n"].Value);
            else if (MinorAppend.Match(line) is { Success: true } mi && major is not null) labels.Add("§" + major + "‑" + mi.Groups["l"].Value);
        }
        return labels;
    }

    /// <summary>ما لا يُسَمّيه الصَفُّ مِن تِلكَ الرُموز — والرَمزُ لا يَليه رَقَم (<c>§١</c> لا يُحسَبُ حاضِراً في <c>§١٢</c>).</summary>
    internal static IReadOnlyList<string> Unmirrored(IEnumerable<string> labels, string row)
        => labels.Distinct().Where(l => !Regex.IsMatch(row, Regex.Escape(l) + "(?![٠-٩])")).ToList();

    [Fact]
    public void Every_dated_append_heading_in_an_adr_is_named_in_its_readme_row()
    {
        var readme = File.ReadAllLines(Path.Combine(RepoRoot, "docs", "README.md"));
        var adrs = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "ADR-*.md").Order(StringComparer.Ordinal).ToList();
        var total = 0;
        var missing = new List<string>();
        foreach (var path in adrs)
        {
            var name = Path.GetFileName(path);
            var row = readme.SingleOrDefault(l => l.StartsWith("| [`" + name + "`]", StringComparison.Ordinal));
            Assert.True(row is not null, $"{name} بِلا صَفٍّ في فِهرِسِ الوَثائِق.");
            var labels = AppendLabels(File.ReadAllText(path));
            total += labels.Count;
            missing.AddRange(Unmirrored(labels, row!).Select(l => $"{name[..7]} {l}"));
        }
        output.WriteLine($"ADR: {adrs.Count} · عَناوينُ مُلحَقات: {total} · بِلا مِرآة: {missing.Count}");
        Assert.True(adrs.Count >= 30 && total >= 20, $"أَداةٌ عَمياء: {adrs.Count} ADR و{total} مُلحَقاً.");
        Assert.True(missing.Count == 0, "مُلحَقٌ لا يُسَمّيه صَفُّه في docs/README.md: " + string.Join("، ", missing));
    }

    /// <summary><b>حَقنُ عَيب</b>: ADR مَصنوعٌ بِمُلحَقَينِ — رَئيسٍ وفَرعِيٍّ تَحتَ قِسمٍ لَيسَ مُلحَقاً — وصَفٌّ يُسَمّي واحِداً: المُتَحَقِّقُ يَجِدُ الآخَرَ.</summary>
    [Fact]
    public void The_mirror_checker_goes_red_on_an_injected_unmirrored_append()
    {
        const string adr = "# ADR-999\n## ١. القَرار\n### أ. نَصّ\n### ب. إلحاقُ ‏2026-10-25 — فَرعِيّ\n## ٢. إلحاقُ ‏2026-10-26 — رَئيس\n### أ. إلحاقُ ‏2026-10-27\n";
        var labels = AppendLabels(adr);
        Assert.Equal(new[] { "§١‑ب", "§٢", "§٢‑أ" }, labels);
        Assert.Equal(new[] { "§٢", "§٢‑أ" }, Unmirrored(labels, "| [`ADR-999`](…) | يُسَمّي §١‑ب وَحدَه | |"));
        Assert.Empty(Unmirrored(labels, "| §١‑ب و§٢ و§٢‑أ |"));
    }
}
