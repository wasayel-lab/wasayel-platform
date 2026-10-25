using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>اصطِلاحُ الوَقتِ المَعروضِ لَه ADR، وعَدَدُه في الوَثيقَةِ عَدَدُ الماسِح</b> (‏2026-10-25، تَدقيقُ الإطلاق lens-identity-rules-4، تَيّارُ W8-6).
/// <b>المَقيسُ قَبل</b>: <c>grep -E "CultureTime|InZone|DisplayTime" docs/ADR-*.md</c> = صِفر — والاصطِلاحُ يَعبُرُ كُلَّ صَفحَةٍ تَرسُمُ وَقتاً؛ و<c>RELEASE-READINESS</c>
/// §١ البَند ٦ يَقولُ «10 مَواضِعَ مُثَبَّتَة» والماسِحُ ‏19 مُنذُ <c>e871cec</c> — رَقَمٌ في وَثيقَةٍ لا يَحرُسُه شَيءٌ فَشاخَ صامِتاً (القاعِدَة ٢).
/// <b>وصارَ</b>: ‏ADR-039 يُسَمّي الدالَّةَ والافتِراضِيَّةَ و«ب» S15·٣ وأَمرَ النَقضِ المَقيس، وأَصنافُ مُثَبَّتاتِه الأَربَعَةُ مَجموعُها عَدَدُ
/// <c>DisplayTimeZoneTests.PinnedUnzoned</c> — يُقرَأُ مِن نَصِّ المَصدَرِ لا بِالتَجميع، فَنَقضُ الاصطِلاحِ لا يَكسِرُ البِناءَ بَل يُحمِّرُ هذا حَتّى يُلحَقَ الـADR.</para>
/// </summary>
public class AdrDisplayTimeTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var path = Path.Combine([RepoRoot, .. parts]);
        Assert.True(File.Exists(path), $"مَفقود: {string.Join('/', parts)}");
        return File.ReadAllText(path);
    }

    private static string Adr039()
        => File.ReadAllText(Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "ADR-039-*.md").Single());

    /// <summary>عَدَدُ مُدخَلاتِ <c>PinnedUnzoned</c> في نَصِّ المَصدَر — سَطرٌ يَبدَأُ بِـ<c>("</c> بَينَ إعلانِها وأَوَّلِ <c>};</c> بَعدَه؛ والتَعليقُ لا يُعَدّ.</summary>
    internal static int PinnedCount(string source)
    {
        var start = source.IndexOf("PinnedUnzoned =", StringComparison.Ordinal);
        if (start < 0) return -1;
        // الخاتِمَةُ سَطرٌ «};» وَحدَه — لا «};» داخِلَ مَقطَعٍ مُثَبَّت (‏«expiresAt={d.NewExpiresAt:yyyy-MM-dd}; order=» فيه واحِدَة).
        var lines = source[start..].Split('\n');
        var close = Array.FindIndex(lines, l => l.Trim() == "};");
        return lines.Take(close < 0 ? lines.Length : close).Count(l => l.TrimStart().StartsWith("(\"", StringComparison.Ordinal));
    }

    /// <summary>أَعدادُ صُفوفِ جَدوَلِ الأَصنافِ في البَند ٥ مِن §٢ — الخَليَّةُ الثانِيَةُ رَقَماً، مِن «أَربَعَةُ أَصناف» حَتّى البَند ٦.</summary>
    internal static IReadOnlyList<int> ClassCounts(string adr)
    {
        var i = adr.IndexOf("أَربَعَةُ أَصناف", StringComparison.Ordinal);
        if (i < 0) return [];
        var j = adr.IndexOf("\n6. ", i, StringComparison.Ordinal);
        return adr[i..(j < 0 ? adr.Length : j)].Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith('|'))
            .Select(l => l.Split('|', StringSplitOptions.TrimEntries))
            .Where(c => c.Length > 3 && int.TryParse(c[2], out _))
            .Select(c => int.Parse(c[2]))
            .ToList();
    }

    [Fact]
    public void Adr039_names_the_convention_and_its_pinned_classes_sum_to_the_scanner()
    {
        var adr = Adr039();
        var pinned = PinnedCount(Read("tests", "ACommerce.Platform.Tests", "DisplayTimeZoneTests.cs"));
        var classes = ClassCounts(adr);
        output.WriteLine($"PinnedUnzoned: {pinned} · أَصنافُ ADR-039: {classes.Count} مَجموعُها {classes.Sum()}");
        Assert.True(pinned > 0, "أَداةٌ عَمياء: صِفرُ مُثَبَّتٍ في DisplayTimeZoneTests.");
        Assert.Equal(4, classes.Count);
        Assert.Equal(pinned, classes.Sum());
        Assert.Contains($"والـ{pinned} أَربَعَةُ أَصناف", adr, StringComparison.Ordinal);

        foreach (var phrase in new[]
                 {
                     "`InZone(stored, timeZoneId?)`", "`FormatTime(stored, format, timeZoneId?)`", "`DefaultTimeZone = \"Asia/Riyadh\"`",
                     "«ب» S15‑launch‑journey‑debts · ٣",
                     "for c in 2d20144 865acb3 e871cec ade15cd; do git show \"$c\" -- . ':(exclude)docs' | git apply -R --3way --index; done",
                 })
            Assert.Contains(phrase, adr, StringComparison.Ordinal);

        // ما يُسَمّيه الـADR قائِمٌ في الكودِ بِحَرفِه — نَصّاً لا تَجميعاً، فَنَقضُ الاصطِلاحِ لا يَكسِرُ بِناءَ هذا المِلَفّ.
        var ctx = Read("libs", "kits", "Culture", "ACommerce.Kit.Culture.Core", "CultureContext.cs");
        foreach (var signature in new[]
                 {
                     "public const string DefaultTimeZone = \"Asia/Riyadh\";",
                     "public static DateTime InZone(DateTime stored, string? timeZoneId = null)",
                     "public static string FormatTime(DateTime stored, string format, string? timeZoneId = null)",
                 })
            Assert.Contains(signature, ctx, StringComparison.Ordinal);

        var rr = Read("docs", "RELEASE-READINESS.md");
        Assert.Contains("تَدقيقُ الإطلاق lens-identity-rules-4", rr, StringComparison.Ordinal);
        Assert.Contains($"‏{pinned} مُثَبَّتاً · صِفرُ مُخالِف", rr, StringComparison.Ordinal);
    }

    /// <summary><b>حَقنُ عَيب</b>: مَصدَرٌ مَصنوعٌ بِثَلاثَةِ مُثَبَّتاتٍ (أَحَدُها فيه «};» داخِلَ نَصِّه) وسَطرِ تَعليقٍ ومَصفوفَةٍ أُخرى بَعدَها، وADR مَصنوعٌ بِصِنفَينِ مَجموعُهُما 3 — ثُمَّ يُحذَفُ صِنف:
    /// العَدّادانِ يُطابِقانِ ثُمَّ يَفتَرِقان.</summary>
    [Fact]
    public void The_count_readers_answer_an_injected_source_and_adr()
    {
        const string src = "private static readonly (string, string, string)[] PinnedUnzoned =\n{\n    (\"a\", \"b\", \"c\"),\n    // (\"x\", \"y\", \"z\"),\n" +
                           "    (\"d\", \"e {x}; f\"),\n    (\"g\", \"h\", \"i\"),\n};\nvar other = new[] { (\"j\", \"k\") };\n";
        Assert.Equal(3, PinnedCount(src));
        Assert.Equal(-1, PinnedCount("لا مَصفوفَة"));

        const string adr = "5. نَصّ. والـ3 أَربَعَةُ أَصناف:\n\n   | الصِنف | العَدَد | المَواضِع |\n   |---|---:|---|\n   | أ | 2 | `X` |\n   | ب | 1 | `Y` |\n\n6. **التالي**\n   | ج | 9 | `Z` |\n";
        Assert.Equal(new[] { 2, 1 }, ClassCounts(adr));
        Assert.Equal(PinnedCount(src), ClassCounts(adr).Sum());
        Assert.NotEqual(PinnedCount(src), ClassCounts(adr.Replace("   | ب | 1 | `Y` |\n", "", StringComparison.Ordinal)).Sum());
        Assert.Empty(ClassCounts("بِلا جَدوَل"));
    }
}
