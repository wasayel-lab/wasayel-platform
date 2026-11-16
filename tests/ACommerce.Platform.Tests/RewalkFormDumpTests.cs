using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ الرِحلَةِ يَأخُذُ التالِيَ مِن الصَفحَة — `form_dump` مُشَغَّلَةً عَلى عَيِّنَةٍ مَعلومَة (‏2026-10-25، تَدقيقُ الإطلاق brief-P1b-2) ═══
//
// `RELEASE-READINESS.md` يَقولُ إنّ الرِحلَةَ تُمشى «مِن صَفحَةٍ أولى»، وكانَ السكربتُ يَبني `create-listing` و`checkout?step=2&…` عُنواناً ويَكتُبُ
// `grace=14 price=0` حَرفاً. صارَت الخُطُواتُ تَقرَأُ بابَ كُلِّ استِمارَةٍ وحُقولَها مِن صَفحَتِها بِـ`form_dump` — **وأَداةٌ تَقرَأُ الصَفحَةَ تُقاسُ قَبلَ أَن
// يوثَقَ بِها** (القاعِدَة ١٠): هذا المِلَفُّ يَقطَعُ ما بَينَ عَلامَتَيها بِحَرفِه (ومَعَه `dec` الَّذي تُناديه) ويُشَغِّلُه بِـbash عَلى صَفحَةٍ مَصنوعَةٍ جَوابُها
// مَعلوم، وعَلى نُسَخٍ مَحقونَةٍ يَجِبُ أَن يَتَغَيَّرَ جَوابُها — نَسَقُ `RewalkAnalysisBoundParseTests`. والجَولَةُ الحَيَّةُ بِأَرقامِها في RR §٧.
public class RewalkFormDumpTests(ITestOutputHelper output)
{
    private const string Start = "# ─── بِدايَةُ form_dump ───";
    private const string End = "# ─── نِهايَةُ form_dump ───";

    private static string Script() =>
        File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "rewalk-launch-journey.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>الكُتلَةُ بَينَ عَلامَتَيها، مَسبوقَةً بِسَطرِ <c>dec</c> مِن السكربتِ نَفسِه — عَلامَةٌ واحِدَةٌ لِكُلِّ طَرَف.</summary>
    private static string Block()
    {
        var s = Script();
        var a = s.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ form_dump غائِبَة.");
        Assert.Equal(a, s.LastIndexOf(Start, StringComparison.Ordinal));
        var b = s.IndexOf(End, a, StringComparison.Ordinal);
        Assert.True(b > a, "عَلامَةُ نِهايَةِ form_dump غائِبَة.");
        var dec = Regex.Match(s, @"^dec\(\) \{.*\}$", RegexOptions.Multiline);
        Assert.True(dec.Success, "سَطرُ dec() غائِب.");
        return dec.Value + "\n" + s[a..(b + End.Length)];
    }

    /// <summary>صَفحَةٌ مَصنوعَة: استِمارَةٌ أولى لا تَحمِلُ النَصَّ، ثُمَّ الَّتي تَحمِلُه — مَخفِيٌّ وخِيارٌ ومَطلوبٌ بِلا قيمَةٍ وبِقيمَة، وقيمَةٌ بِكِياناتِ Razor.</summary>
    private const string Page =
        "<html><body>\n" +
        "<form method=\"post\" action=\"/s/other\"><input type=\"hidden\" name=\"z\" value=\"9\" /></form>\n" +
        "<form method=\"get\" action=\"/s/checkout\">\n" +
        "  <input type=\"hidden\" name=\"step\" value=\"2\" />\n" +
        "  <input name=\"name\" required class=\"ac-input\" value=\"\" />\n" +
        "  <input name=\"phone\" required class=\"ac-input\" dir=\"ltr\" value=\"0500\" />\n" +
        "  <textarea name=\"note\" rows=\"3\" required></textarea>\n" +
        "  <input type=\"number\" name=\"grace\" class=\"ac-input\" min=\"0\" value=\"14\" />\n" +
        "  <input type=\"radio\" name=\"pay\" value=\"cash\" required />\n" +
        "  <input type=\"hidden\" name=\"addr\" value=\"&#x627;&#x644;&#x631;&#x64A;&#x627;&#x636;\" />\n" +
        "</form>\n</body></html>\n";

    private const string Expected =
        "ACTION /s/checkout get\nHIDDEN step=2\nFIELD name\nFIELD phone\nVALUE phone=0500\nFIELD note\nVALUE grace=14\nRADIO pay=cash\nHIDDEN addr=الرياض";

    private (int Code, string Out) Run(string block, string body)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-rewalk-form-");
        try
        {
            var page = Path.Combine(dir.FullName, "page.html");
            File.WriteAllText(page, Page, new System.Text.UTF8Encoding(false));
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + block + "\n" + body + "\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, page })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000), "الكُتلَةُ لَم تَنتَهِ في عَشرِ ثَوانٍ.");
            return (p.ExitCode, o.TrimEnd('\n'));
        }
        finally { dir.Delete(recursive: true); }
    }

    private const string Dump = "form_dump \"$1\" 'name=\"step\" value=\"2\"'";

    /// <summary><b>الاستِمارَةُ الَّتي تَحمِلُ النَصَّ لا أَوَّلُ استِمارَة</b>، بِبابِها وطَريقَتِها وكُلِّ حَقلٍ بِنَوعِه، والكِياناتُ مَفكوكَة؛ وبِلا مُطابَقَةٍ لا يُطبَعُ شَيء؛
    /// و<c>fd_action</c>/<c>fd_get</c>/<c>fd_kind</c> تَقرَأُ الجَوابَ نَفسَه — ويُطبَعُ عَدَدُ الأَسطُر.</summary>
    [Fact]
    public void The_dump_reads_the_matching_form_its_action_and_every_field_by_kind()
    {
        var block = Block();
        var (code, got) = Run(block, Dump);
        output.WriteLine($"form_dump → {code} · {got.Split('\n').Length} سَطراً:\n{got}");
        Assert.Equal(0, code);
        Assert.Equal(Expected, got);

        var (_, none) = Run(block, "form_dump \"$1\" 'لا شَيءَ يُطابِقُ هذا'");
        Assert.Equal("", none);

        var (_, helpers) = Run(block, "D=\"$(" + Dump + ")\"; printf '%s|%s|%s|%s\\n' \"$(fd_action \"$D\")\" \"$(fd_get \"$D\" HIDDEN step)\" " +
                                      "\"$(fd_get \"$D\" VALUE grace)\" \"$(fd_kind \"$D\" FIELD | tr '\\n' ' ')\"");
        output.WriteLine($"المُساعِدات → {helpers}");
        Assert.Equal("/s/checkout|2|14|name phone note ", helpers);
    }

    /// <summary><b>حَقنُ عَيب</b>: النَصُّ لا يُسأَل (أَوَّلُ استِمارَةٍ تُؤخَذ)، والمَخفِيُّ يُقرَأُ مَطلوباً، والمَطلوبُ لا يُرى، والكِياناتُ لا تُفَكّ —
    /// كُلٌّ يُغَيِّرُ الجَوابَ عَن المَعلوم، وإلّا فَالصَفُّ أَعلاه لا يَرى ما يَحرُس.</summary>
    [Fact]
    public void The_dump_rows_can_actually_go_red_on_an_injected_fault()
    {
        var live = Block();
        var faults = new (string Name, string From, string To)[]
        {
            ("النَصُّ لا يُسأَل", "next if index($open . $body, $ENV{MUST}) < 0;", ""),
            ("المَخفِيُّ مَطلوب", "if    ($t eq \"hidden\")", "if    ($t eq \"hiddenX\")"),
            ("المَطلوبُ لا يُرى", "print \"FIELD $n\\n\" if $a =~ /\\brequired\\b/;", ""),
            ("الكِياناتُ لا تُفَكّ", "dec \"$1\" | MUST=", "cat \"$1\" | MUST="),
        };
        var blind = new List<string>();
        foreach (var f in faults)
        {
            Assert.Single(Regex.Matches(live, Regex.Escape(f.From)));
            var (_, got) = Run(live.Replace(f.From, f.To, StringComparison.Ordinal), Dump);
            output.WriteLine($"«{f.Name}» → {got.Replace('\n', ' ')}");
            if (got == Expected) blind.Add(f.Name);
        }
        output.WriteLine($"حُقِنَ {faults.Length} عُيوب؛ لَم يُرَ مِنها {blind.Count}");
        Assert.True(blind.Count == 0, "عَيبٌ لا تَراه العَيِّنَة: " + string.Join("، ", blind));
    }
}
