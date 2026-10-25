using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>خَليَّةُ حالِ الخُطوَةِ في جَداوِلِ الـADR لا تَقولُ «نُفِّذ» و«قيدَ الانتظار» مَعاً</b> (‏2026-10-25، تَدقيقُ الإطلاق lens-docs-vs-code-11،
/// تَيّارُ W8-6). <b>المَقيسُ قَبل</b>: صَفُّ الخُطوَة 3 في ADR-036 §٦ يَبدَأُ «قيدَ الانتظار» ثُمَّ «نُفِّذ ‏2026-10-12» وكوميتُه `e96c482` سَلَفٌ لِلرَأس —
/// صَفُّ الخُطوَة 2 أُبدِلَ انتِظارُه (`44d89ef`) وهذا لا. فَالقاعِدَة: كُلُّ صَفِّ جَدوَلٍ في <c>docs/ADR-*.md</c> تَقولُ خَليَّتُه الأَخيرَةُ «نُفِّذ» (بِلا
/// تَشكيل) لا تَحمِلُ «قيد الانتظار» إلّا مَشطوباً — يَطبَعُ ما عَدَّ ويَفشَلُ عَلى الصِفر، ويَحمَرُّ عَلى صَفٍّ مَصنوع.</para>
/// </summary>
public class AdrStepTableTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Bare(string s) => Regex.Replace(s, "[ً-ْـٰ‎‏]", "");

    /// <summary>أَتَقولُ الخَليَّةُ الأَخيرَةُ «نُفِّذ» و«قيد الانتظار» مَعاً بِلا شَطب؟ — و<c>null</c> لِصَفٍّ لا يَقولُ «نُفِّذ».</summary>
    internal static bool? SaysDoneAndPending(string row)
    {
        var cells = row.Trim().Trim('|').Split('|');
        var last = Bare(Regex.Replace(cells[^1], "~~.*?~~", ""));
        if (!last.Contains("نفذ", StringComparison.Ordinal)) return null;
        return last.Contains("قيد الانتظار", StringComparison.Ordinal);
    }

    [Fact]
    public void No_adr_step_row_says_done_and_pending_at_once()
    {
        var done = 0;
        var both = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "ADR-*.md").Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith('|')) continue;
                switch (SaysDoneAndPending(lines[i]))
                {
                    case true: both.Add($"{Path.GetFileName(path)[..7]}:{i + 1}"); done++; break;
                    case false: done++; break;
                }
            }
        }
        output.WriteLine($"صُفوفٌ تَقولُ «نُفِّذ»: {done} · ومَعَها «قيدَ الانتظار» بِلا شَطب: {both.Count}");
        Assert.True(done >= 20, $"أَداةٌ عَمياء: {done} صَفّاً.");
        Assert.True(both.Count == 0, "صَفٌّ يَقولُ «نُفِّذ» و«قيدَ الانتظار» مَعاً: " + string.Join("، ", both));
    }

    /// <summary><b>حَقنُ عَيب</b>: الصَفُّ كَما كانَ يُحمِرُّ المُتَحَقِّق، ومَشطوباً يَمُرّ، وصَفٌّ بِلا «نُفِّذ» لا يُعَدّ.</summary>
    [Fact]
    public void The_step_row_checker_goes_red_on_the_row_as_it_was()
    {
        Assert.True(SaysDoneAndPending("| 3 | كاتبُ OTO | **قيدَ الانتظار** — مع شطرِ OTO **نُفِّذ ‏2026-10-12** |"));
        Assert.False(SaysDoneAndPending("| 3 | كاتبُ OTO | ~~**قيدَ الانتظار** — مع شطرِ OTO~~ **نُفِّذ ‏2026-10-12** |"));
        Assert.Null(SaysDoneAndPending("| 4 | خُطوَة | **قيدَ الانتظار** |"));
    }
}
