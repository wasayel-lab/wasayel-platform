using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ لَقطَةُ مَظهَرٍ مُودَعَةٌ لا يُنتِجُها مَدخَلٌ حَيٌّ في سكربتِ الالتِقاط — أَثَرٌ يُشحَنُ ولا يُقارَن ═══
//
// **مُراجَعَةُ تَيّارِ W8-7 الجَولَة ١ (صُغرى، ‏2026-10-25)**: مَدخَلُ `member-create-listing-forbidden` في `capture-appearance.sh` عُلِّقَ
// (فَرعُ `?err=forbidden` حُذِفَ مِن الصَفحَة)، وبَقِيَ مِلَفُّه في `i18n-baseline/` — لا يُنتِجُه شَيءٌ ولا يُقارِنُه شَيء، كودٌ مَيِّتٌ يُشحَن
// (كُلفَةُ القاعِدَة ١: نُسخَةُ `widgets.css` في مَشروعِ Widgets). والحَذفُ بِاليَدِ لا يَمنَعُ يَتيماً ثانِياً — فَهذا الصَفُّ يَمنَعُه (القاعِدَة ٢).
public class AppearanceBaselineCaptureEntryTests(ITestOutputHelper output)
{
    private const string Script = "scripts/capture-appearance.sh";

    /// <summary>مَجلِداتُ اللَقَطاتِ الَّتي يَكتُبُها السكربتُ نَفسُه (‏<c>OUT_DIR/$name.html</c>). و<c>proof/</c> خارِجَها عَمداً: بَراهينُ مُجَرَّدَةٌ
    /// يَكتُبُها صاحِبُها بِيَدِه (‏<c>ashare.stripped.after</c>)، لا مَخرَجُ مَدخَل.</summary>
    private static readonly string[] BaselineDirs =
    {
        "tests/characterization/appearance/i18n-baseline",
        "tests/characterization/appearance/baseline",
    };

    /// <summary><para>أَسماءُ المَداخِلِ الحَيَّةِ في مَصفوفَتَي <c>PAGES=(…)</c> و<c>GUARDED_PAGES=(…)</c>: كُلُّ سَطرٍ يَبدَأُ — بَعدَ المَسافَة — بِـ
    /// <c>"name:</c>. والسَطرُ المُعَلَّقُ (<c>#</c> أَوَّلاً) لَيسَ مَدخَلاً ولو حَمَلَ الاسمَ بِحَرفِه، ومَصفوفَةُ <c>SHEETS</c> خارِجَ العَدّ
    /// (أَوراقُ أَنماطٍ في <c>css/</c>).</para></summary>
    internal static HashSet<string> CaptureNames(string script)
    {
        var text = script.Replace("\r\n", "\n", StringComparison.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var array in new[] { "PAGES", "GUARDED_PAGES" })
        {
            var open = Regex.Match(text, $@"(?m)^{array}=\($");
            Assert.True(open.Success, $"مَصفوفَةُ {array}=( مَفقودَة — الماسِحُ أَعمى.");
            var close = Regex.Match(text[(open.Index + open.Length)..], @"(?m)^\)$");
            Assert.True(close.Success, $"مَصفوفَةُ {array} بِلا إغلاق.");
            var body = text.Substring(open.Index + open.Length, close.Index);
            foreach (Match m in Regex.Matches(body, @"(?m)^[ \t]*""(?<n>[a-z0-9][a-z0-9-]*):"))
                names.Add(m.Groups["n"].Value);
        }
        return names;
    }

    /// <summary>ما في المَجلِدِ مِن <c>*.html</c> بِلا مَدخَلٍ حَيٍّ بِاسمِه — مُرَتَّباً.</summary>
    internal static List<string> Orphans(IEnumerable<string> baselineNames, IReadOnlySet<string> captureNames)
        => baselineNames.Where(n => !captureNames.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary><b>كُلُّ لَقطَةٍ مُودَعَةٍ يُنتِجُها مَدخَلٌ حَيّ</b> — ويَطبَعُ ما عَدَّه ويَفشَلُ دونَ المَقيس (القاعِدَة ١٠). مَقيسٌ يَومَ كِتابَتِه
    /// (‏2026-10-25): ‏138 مَدخَلاً حَيّاً، و‏137 لَقطَةً في <c>i18n-baseline/</c> و6 في <c>baseline/</c> بَعدَ حَذفِ اليَتيمِ الوَحيد.</summary>
    [Fact]
    public void Every_committed_appearance_baseline_has_a_live_capture_entry_that_produces_it()
    {
        var names = CaptureNames(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Script)));
        var perDir = BaselineDirs.Select(d =>
        {
            var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(dir), $"مَجلِدٌ مَفقود: {d}");
            var files = Directory.GetFiles(dir, "*.html").Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
            return (Dir: d, Files: files, Orphans: Orphans(files, names));
        }).ToList();
        output.WriteLine($"مَداخِلُ حَيَّة: {names.Count} · "
            + string.Join(" · ", perDir.Select(p => $"{p.Dir}: {p.Files.Count} لَقطَةً، يَتيمَة {p.Orphans.Count}"
                + (p.Orphans.Count == 0 ? "" : $" ({string.Join(", ", p.Orphans)})"))));

        Assert.True(names.Count >= 100, $"أَداةٌ عَمياء: {names.Count} مَدخَلاً حَيّاً — والمَقيسُ 138.");
        Assert.True(perDir[0].Files.Count >= 100, $"أَداةٌ عَمياء: {perDir[0].Files.Count} لَقطَةً في {perDir[0].Dir} — والمَقيسُ 137.");
        var orphans = perDir.SelectMany(p => p.Orphans.Select(o => $"{p.Dir}/{o}.html")).ToList();
        Assert.True(orphans.Count == 0,
            "لَقطَةٌ مُودَعَةٌ لا يُنتِجُها مَدخَلٌ حَيٌّ في " + Script + " — احذِفها مَعَ تَعليقِ مَدخَلِها، أَو أَعِد المَدخَل:\n  "
            + string.Join("\n  ", orphans));
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): سكربتٌ مَحقونٌ فيه مَدخَلٌ عامٌّ ومَدخَلٌ مَحروسٌ ومَدخَلٌ مُعَلَّقٌ وسَطرٌ في
    /// <c>SHEETS</c> — ولَقَطاتٌ بِأَسماءِ الأَربَعَةِ وخامِسَةٌ بِلا مَدخَل. فَاليَتيمُ: المُعَلَّقُ ووَرَقَةُ الأَنماطِ والخامِسَة، لا الحَيّان.</summary>
    [Fact]
    public void The_baseline_orphan_scanner_counts_a_commented_entry_and_a_stylesheet_as_absent()
    {
        const string script =
            "#!/usr/bin/env bash\nPAGES=(\n  \"a-page:/a\"\n  # \"b-page:/b\"\n  # ‏كانَ هُنا \"e-page:/e\" —\n)\n\n"
          + "SHEETS=(\n  \"site.css:/css/site.css\"\n  \"sheet-x:/css/x.css\"\n)\n\n"
          + "GUARDED_PAGES=(\n  \"c-page:member:/{member}/c\"\n)\n";
        var names = CaptureNames(script);
        var orphans = Orphans(new[] { "a-page", "b-page", "c-page", "d-page", "sheet-x" }, names);
        output.WriteLine($"مَحقونَة: مَداخِلُ {names.Count} ({string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal))}) · يَتيمَة {string.Join(", ", orphans)}");

        Assert.Equal(new[] { "a-page", "c-page" }, names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "b-page", "d-page", "sheet-x" }, orphans.ToArray());
    }
}
