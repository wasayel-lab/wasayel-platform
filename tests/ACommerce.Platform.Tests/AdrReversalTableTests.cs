using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>كُلُّ صَفِّ نَقضٍ «لَم يُقَس بَعدَ الحَلّ» لَه قِياسٌ مُؤَرَّخٌ أَخضَر</b> (‏2026-10-25، تَدقيقُ الإطلاق — بَندُ المُنَسِّقِ في تَيّارِ W8-6).
/// <b>المَقيسُ قَبل</b>: جَداوِلُ النَقضِ الثَلاثَةُ (‏ADR-032 §٨‑ح‑١١، ‏ADR-033 §٧‑ص‑١٢، ‏ADR-036 §١٢‑٨) تَحمِلُ أَربَعَةَ عَشَرَ صَفّاً عَمودُها الأَخيرُ
/// «لَم يُقَس بَعدَ الحَلّ» — نَقضٌ مَوعودٌ بِلا بُرهان، وبَقِيَ مَفتوحاً في تَقريرِ S14 وَحدَه. <b>وصارَ</b>: ‏ADR-032 §٨‑ح‑١٢ جَدوَلٌ واحِدٌ مَقيسٌ عِندَ
/// رَأسٍ مُسَمّى، وهذا الفاحِصُ يَشتَرِطُ لِكُلِّ كوميتٍ في صَفٍّ كَذاكَ صَفّاً هُناكَ يَقولُ البِناءَ 0 والاختِباراتِ «صِفرُ فَشَل» — فَصَفٌّ جَديدٌ «لَم يُقَس»
/// في أَيِّ ADR يُحمِّرُه حَتّى يُقاس.</para>
/// </summary>
public class AdrReversalTableTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string Unmeasured = "لم يُقَس بعدَ الحلّ";
    private const string MeasuredHeading = "12. **النقضُ اليوم للكوميتاتِ الأربعةَ عشرَ";

    private static readonly Regex RowSha = new(@"^\| `(?<sha>[0-9a-f]{7})` \(", RegexOptions.Compiled);

    /// <summary>كوميتاتُ الصُفوفِ الَّتي يَقولُ نَصُّها «لَم يُقَس بَعدَ الحَلّ».</summary>
    internal static IReadOnlyList<string> UnmeasuredShas(string adr)
        => adr.Split('\n')
            .Select(l => (Line: l, M: RowSha.Match(l)))
            .Where(x => x.M.Success && x.Line.Contains(Unmeasured, StringComparison.Ordinal))
            .Select(x => x.M.Groups["sha"].Value)
            .ToList();

    /// <summary>كوميتاتُ جَدوَلِ القِياسِ في ‏ADR-032 §٨‑ح‑١٢ الَّتي يَقولُ صَفُّها البِناءَ 0 والاختِباراتِ «صِفرُ فَشَل».</summary>
    internal static IReadOnlyList<string> MeasuredGreenShas(string adr032)
    {
        var at = adr032.IndexOf(MeasuredHeading, StringComparison.Ordinal);
        if (at < 0) return [];
        return adr032[at..].Split('\n')
            .Select(l => (Line: l, M: RowSha.Match(l)))
            .Where(x => x.M.Success && x.Line.Contains("البناءُ 0 أخطاء", StringComparison.Ordinal)
                        && x.Line.Contains("صفرُ فشل", StringComparison.Ordinal)
                        && x.Line.Contains("نظيفٌ، وملفّاتُه = أبوه", StringComparison.Ordinal))
            .Select(x => x.M.Groups["sha"].Value)
            .ToList();
    }

    [Fact]
    public void Every_unmeasured_reversal_row_has_a_green_measured_row_in_adr032()
    {
        var adrs = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "ADR-*.md").Order(StringComparer.Ordinal).ToList();
        var unmeasured = adrs.SelectMany(p => UnmeasuredShas(File.ReadAllText(p))).Distinct().ToList();
        var adr032 = File.ReadAllText(adrs.Single(p => Path.GetFileName(p).StartsWith("ADR-032-", StringComparison.Ordinal)));
        var measured = MeasuredGreenShas(adr032);
        var missing = unmeasured.Except(measured).ToList();
        output.WriteLine($"ADR: {adrs.Count} · صُفوفُ «{Unmeasured}»: {unmeasured.Count} · مَقيسَةٌ خَضراءُ في §٨‑ح‑١٢: {measured.Count} · بِلا قِياس: {missing.Count}");
        Assert.True(unmeasured.Count > 0 && measured.Count > 0, "أَداةٌ عَمياء: صِفرُ صَفٍّ في أَحَدِ الطَرَفَين.");
        Assert.True(missing.Count == 0, "صَفُّ نَقضٍ «لَم يُقَس» بِلا قِياسٍ أَخضَرَ في ADR-032 §٨‑ح‑١٢: " + string.Join("، ", missing));
    }

    /// <summary><b>حَقنُ عَيب</b>: ADR مَصنوعٌ بِصَفَّينِ «لَم يُقَس» وجَدوَلُ قِياسٍ يُسَمّي أَحَدَهُما أَخضَرَ والآخَرَ بِفَشَل — المُتَحَقِّقُ يَجِدُ الثاني.</summary>
    [Fact]
    public void The_reversal_checker_goes_red_on_an_injected_unmeasured_row()
    {
        const string adr = "| `aaaaaaa` (§١) | يتعارض | نظيف | فيُحَلُّ يدويًّا؛ **ولم يُقَس بعدَ الحلّ** |\n" +
                           "| `bbbbbbb` (§٢) | يتعارض | نظيف | **ولم يُقَس بعدَ الحلّ** |\n| `ccccccc` (§٣) | نظيف | نظيف | نظيف |\n";
        Assert.Equal(new[] { "aaaaaaa", "bbbbbbb" }, UnmeasuredShas(adr));

        const string table = "قبل\n| `bbbbbbb` (X) | يتعارض | 9 كوميتًا — نظيفٌ، وملفّاتُه = أبوه | البناءُ 0 أخطاء، والاختباراتُ 10 · صفرُ فشل |\n" +
                             MeasuredHeading + " …\n" +
                             "| `aaaaaaa` (X) | يتعارض | 9 كوميتًا — نظيفٌ، وملفّاتُه = أبوه | البناءُ 0 أخطاء، والاختباراتُ 10 · صفرُ فشل |\n" +
                             "| `bbbbbbb` (X) | يتعارض | 9 كوميتًا — نظيفٌ، وملفّاتُه = أبوه | البناءُ 0 أخطاء، والاختباراتُ 10 · فشلٌ واحد |\n";
        Assert.Equal(new[] { "aaaaaaa" }, MeasuredGreenShas(table));
        Assert.Equal(new[] { "bbbbbbb" }, UnmeasuredShas(adr).Except(MeasuredGreenShas(table)));
        Assert.Empty(MeasuredGreenShas("بِلا عُنوان"));
    }
}
