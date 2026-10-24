using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ الرِحلَةِ يَقرَأُ مُهلَةَ التَحليلِ مِن مَصدَرِها — ويَرفُضُ ما لا يَفهَمُه (‏2026-10-24، تَيّارُ S15 البَند H(4)(5)) ═══
//
// `scripts/rewalk-launch-journey.sh` يَحسُبُ نافِذَةَ انتِظارِ التَحليلِ مِن مُهلَةِ نِداءِ الخَلفِيَّة (`AgentCallBound.Background`)، ويَحكُمُ عَلى
// «جارٍ» فَوقَ السَقفِ بِـ`FeasibilityAnalysisService.StaleAfter`. **وكانَت القِراءَةُ تَأخُذُ أَكبَرَ رَقَمٍ يُطابِقُ هِجاءً واحِداً** — فَإسنادُ
// «Timeout =» بِهِجاءٍ آخَرَ عَلى عَميلٍ (مُهلَةٌ تَقطَعُ النِداءَ قَبلَ مُهلَةِ الطَلَب) يَمُرُّ ولا يُرى. صارَت كُتلَةُ `analysis_bounds` تَعُدُّ كُلَّ
// إسنادٍ وتَخرُجُ بِـ2 ما لَم يَكُن كُلُّه «بِلا حَدّ». **وهذا المِلَفُّ يُشَغِّلُ الكُتلَةَ نَفسَها بِـbash** — عَلى المُستَودَعِ (فَتُطابِقُ الكود)،
// وعَلى نُسَخٍ مَحقونَةٍ (فَتَرفُض) — لا يُعيدُ كِتابَتَها (القاعِدَة ١٠: الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها).
public class RewalkAnalysisBoundParseTests(ITestOutputHelper output)
{
    private const string Start = "# ─── بِدايَةُ analysis_bounds ───";
    private const string End = "# ─── نِهايَةُ analysis_bounds ───";
    private const string Services = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services";

    private static string Repo(string rel) => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel);

    private static string Block()
    {
        var script = File.ReadAllText(Repo("scripts/rewalk-launch-journey.sh"));
        var a = script.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ analysis_bounds غائِبَة.");
        Assert.Equal(a, script.LastIndexOf(Start, StringComparison.Ordinal));
        var b = script.IndexOf(End, a, StringComparison.Ordinal);
        Assert.True(b > a);
        // ويَقرَأُ المَسارُ الرَئيسُ القيمَتَينِ مِنها وَحدَها، مَرَّةً واحِدَة.
        Assert.Equal(1, script.Split("BOUND_OUT=\"$(analysis_call_bound \"$BACKENDS\")\" || exit 2").Length - 1);
        Assert.Equal(1, script.Split("STALE_SECS=\"$(stale_after_secs \"$ANALYSIS_SVC\")\" || exit 2").Length - 1);
        return script[a..(b + End.Length)];
    }

    private (int Code, string Out, string Err) Run(string function, string file)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-rewalk-bounds-");
        try
        {
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + Block() + $"\n{function} \"$1\"\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, file })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            var e = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000));
            output.WriteLine($"{function} {Path.GetFileName(file)} → {p.ExitCode} · {o.Trim()} {e.Trim()}");
            return (p.ExitCode, o.Trim(), e.Trim());
        }
        finally { dir.Delete(recursive: true); }
    }

    private (int Code, string Out, string Err) RunOn(string function, string source, Func<string, string> inject)
    {
        var dir = Directory.CreateTempSubdirectory("wasayel-rewalk-src-");
        try
        {
            var path = Path.Combine(dir.FullName, Path.GetFileName(source));
            var text = File.ReadAllText(Repo(source));
            var injected = inject(text);
            Assert.NotEqual(text, injected);   // الحَقنُ وَقَعَ فِعلاً — وإلّا فَالصَفُّ يَقيسُ المُستَودَعَ مَرَّتَين
            File.WriteAllText(path, injected, new System.Text.UTF8Encoding(false));
            return Run(function, path);
        }
        finally { dir.Delete(recursive: true); }
    }

    private const string Backends = Services + "/AgentBackends.cs";
    private const string Analysis = Services + "/Incubator/FeasibilityAnalysisService.cs";
    private const string Open = "Timeout = System.Threading.Timeout.InfiniteTimeSpan";

    /// <summary>عَلى المُستَودَع: الرَقَمانِ هُما رَقَما الكود — مُهلَةُ الخَلفِيَّةِ وعَدَدُ إسناداتِ العَميل، و<c>StaleAfter</c> بِالثَواني.</summary>
    [Fact]
    public void On_the_repository_the_block_reads_the_numbers_the_code_runs_on()
    {
        var (code, outText, _) = Run("analysis_call_bound", Repo(Backends));
        Assert.Equal(0, code);
        Assert.Equal($"{(int)AgentCallBound.Background.TotalSeconds} 3", outText);

        var stale = Run("stale_after_secs", Repo(Analysis));
        Assert.Equal(0, stale.Code);
        Assert.Equal($"{(int)FeasibilityAnalysisService.StaleAfter.TotalSeconds}", stale.Out);
    }

    /// <summary>عَميلٌ يُسنَدُ إلَيه حَدٌّ بِهِجاءٍ آخَرَ يُخرِجُ بِـ2 ويُسَمّي العَدَد — لا يُقرَأُ أَكبَرُ ما يُطابِق.</summary>
    [Fact]
    public void A_client_bound_in_another_spelling_exits_2_and_names_the_count()
    {
        var (code, outText, err) = RunOn("analysis_call_bound", Backends, t =>
        {
            var i = t.IndexOf(Open, StringComparison.Ordinal);
            return t[..i] + "Timeout = TimeSpan.FromSeconds(30)" + t[(i + Open.Length)..];
        });
        Assert.Equal(2, code);
        Assert.Equal("", outText);
        Assert.Contains("3 إسنادَ «Timeout =»", err);
        Assert.Contains("مِنها 2 «بِلا حَدّ»", err);
    }

    /// <summary>ولا إسنادَ أَصلاً — أَداةٌ تَقرَأُ صِفراً عَمياء (القاعِدَة ١٠).</summary>
    [Fact]
    public void No_client_assignment_at_all_exits_2()
    {
        var (code, _, err) = RunOn("analysis_call_bound", Backends, t => t.Replace(Open, "/* حُذِف */"));
        Assert.Equal(2, code);
        Assert.Contains("0 إسنادَ «Timeout =»", err);
    }

    /// <summary>سَطرُ الخَلفِيَّةِ غائِباً أَو مُكَرَّراً يُخرِجُ بِـ2 — رَقَمٌ واحِدٌ يَحكُم.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void The_background_line_must_be_exactly_one(int lines)
    {
        const string bg = "public static readonly TimeSpan Background = TimeSpan.FromSeconds(420);";
        var (code, _, err) = RunOn("analysis_call_bound", Backends, t => lines == 0
            ? t.Replace(bg, "public static readonly TimeSpan Background = TimeSpan.FromMinutes(7);")
            : t.Replace(bg, bg + "\n    // Background = TimeSpan.FromSeconds(999)"));
        Assert.Equal(2, code);
        Assert.Contains($": {lines} سَطر", err);
    }

    /// <summary>و<c>StaleAfter</c> بِغَيرِ هِجائِه يُخرِجُ بِـ2 — لا يُحكَمُ عَلى «جارٍ» بِرَقَمٍ لَم يُقرَأ.</summary>
    [Fact]
    public void StaleAfter_in_another_spelling_exits_2()
    {
        var (code, _, err) = RunOn("stale_after_secs", Analysis,
            t => t.Replace("StaleAfter = TimeSpan.FromMinutes(28)", "StaleAfter = TimeSpan.FromSeconds(1680)"));
        Assert.Equal(2, code);
        Assert.Contains(": 0 سَطر", err);
    }
}
