using System.Diagnostics;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ أَداةُ النَمَطِ المَحسوبِ لا تَفتَحُ مُتَصَفِّحاً عَلى خادِمٍ لَيسَ عَلى هذا الجِهاز (قَرارُ المالِك د26 «ب»، ‏2026-10-29) ═══
//
// `scripts/dump-computed.cs` يَفتَحُ الصَفَحاتِ بِـChromium عَبرَ CDP، والصَفحَةُ تُطلِقُ بَعدَ التَصييرِ ما تُطلِقُه (`POST …/view` في تَفصيلِ
// الإعلان) — فَتَفريغٌ عَلى الخادِمِ المَنشورِ كِتابَةٌ في بَياناتِ مَتجَرٍ حَيّ. والحَدُّ «لا يُشَغَّلُ التَطويرُ عَلى قاعِدَةِ الإنتاج» (CLAUDE.md)
// صارَ فَحصاً آليّاً **قَبلَ** أَن يُفتَحَ مُتَصَفِّح، لِكُلِّ مَتجَرٍ بِلا اسم — `ashare` تَجريبِيٌّ كَغَيرِه. والصَفّانِ يُشَغِّلانِ الأَداةَ نَفسَها
// (لا نُسخَةً مِن شَرطِها): السالِبُ يَحمَرُّ إن فُتِحَ مُتَصَفِّحٌ لِهَدَفٍ بَعيد، والمُوجَبُ يَحمَرُّ إن رُدَّ هَدَفٌ مَحَلِّيّ — ومُتَصَفِّحٌ
// مَفقودٌ عَمداً (`WSL_CHROME`) يَجعَلُ المُوجَبَ يَنتَهي عِندَ البَحثِ عَنه، فَلا يَحتاجُ الصَفُّ خادِماً ولا Chromium.

public class DumpComputedTargetGuardTests
{
    private static (int Code, string Output) Run(string outDir, IEnumerable<string> targets)
    {
        var script = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "dump-computed.cs");
        Assert.True(File.Exists(script), $"مَصدَرٌ مَفقود: {script}");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ThemeZeroEquivalenceTests.RepoRoot,
        };
        foreach (var a in new[] { "run", script, "--", outDir }.Concat(targets)) psi.ArgumentList.Add(a);
        psi.Environment["WSL_CHROME"] = Path.Combine(outDir, "no-such-chrome");
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(TimeSpan.FromMinutes(4)), "الأَداةُ لَم تَنتَهِ في أَربَعِ دَقائِق.");
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "wsl-dump-guard-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>سالِب: هَدَفٌ بَعيد — والمُضيفُ الَّذي يَبدَأُ بِـ<c>localhost</c> ولَيسَ هُوَ — يُرَدُّ بِرَمزِ ٢ قَبلَ البَحثِ عَن مُتَصَفِّح،
    /// ويُسَمّى في الرِسالَة؛ ومُجَلَّدُ المُخرَجِ لا يُكتَبُ فيه شَيء.</summary>
    [Fact]
    public void A_target_off_this_machine_is_refused_before_any_browser_is_looked_for()
    {
        var dir = TempDir();
        var outDir = Path.Combine(dir, "out");
        var (code, output) = Run(outDir, new[] { "ashare-portal|https://example.com/ashare", "x|http://localhost.example.com/ashare" });

        Assert.Equal(2, code);
        Assert.Contains("هَدَفٌ لَيسَ عَلى هذا الجِهاز", output, StringComparison.Ordinal);
        Assert.Contains("https://example.com/ashare", output, StringComparison.Ordinal);
        Assert.Contains("http://localhost.example.com/ashare", output, StringComparison.Ordinal);
        Assert.DoesNotContain("لَم يُعثَر على Chrome", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outDir), "كُتِبَ مُجَلَّدُ المُخرَجِ لِهَدَفٍ مَردود.");
    }

    /// <summary>مُوجَب: الأَهدافُ عَلى هذا الجِهاز (<c>localhost</c> و<c>127.0.0.1</c> و<c>[::1]</c>) تَمُرُّ بِالحارِسِ وتُعَدّ، ثُمَّ تَنتَهي الأَداةُ
    /// عِندَ المُتَصَفِّحِ المَفقودِ عَمداً — لا عِندَ الحارِس.</summary>
    [Fact]
    public void Targets_on_this_machine_pass_the_guard_and_reach_the_browser_lookup()
    {
        var dir = TempDir();
        var (code, output) = Run(Path.Combine(dir, "out"), new[]
        {
            "a|http://localhost:1/ashare", "b|http://127.0.0.1:1/ashare/r/customer", "c|http://[::1]:1/ashare/r/host",
        });

        Assert.Equal(2, code);
        Assert.Contains("الأَهداف: 3 — كُلُّها عَلى هذا الجِهاز", output, StringComparison.Ordinal);
        Assert.Contains("لَم يُعثَر على Chrome", output, StringComparison.Ordinal);
        Assert.DoesNotContain("هَدَفٌ لَيسَ عَلى هذا الجِهاز", output, StringComparison.Ordinal);
    }
}
