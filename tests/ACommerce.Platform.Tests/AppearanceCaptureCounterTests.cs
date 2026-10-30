using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سكربتُ الالتِقاطِ يَعُدُّ ما كَتَبَ ويَفشَلُ عَلى الصِفر (‏2026-10-30، تَدقيقُ الموجَة ١١ CO15 — القاعِدَة ١٠) ═══
//
// كانَ سَطرُ الخِتامِ في `scripts/capture-appearance.sh` يَطبَعُ طولَ المَصفوفَةِ لا ما كُتِب، ويَخرُجُ بِـ0 ولو لَم تُكتَب صَفحَة — و«اِلتُقِطَت»
// بِلا عَدّادٍ لا تُمَيَّزُ عَن أَداةٍ عَمياء. هُنا يُشَغَّلُ السكربتُ نَفسُه بِـbash عَلى نُسخَتَينِ مَحقونَتَين: مَصفوفاتُه الثَلاثُ فارِغَةٌ (فَيَفشَلُ بِعَدَدٍ
// مَطبوعٍ صِفر)، ومَدخَلٌ عامٌّ واحِدٌ يُجيبُه `curl` مَصنوعٌ في رَأسِ PATH (فَيَعُدُّ واحِداً ويَنجَح) — المُوجَبُ والسالِب، والخادِمُ غَيرُ لازِم.
public class AppearanceCaptureCounterTests(ITestOutputHelper output)
{
    private static string Script() =>
        File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "capture-appearance.sh")).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>جِسمُ مَصفوفَةٍ <c>NAME=(…)</c> يُستَبدَلُ بِما يُعطى — والمَصفوفَةُ يَجِبُ أَن تَكونَ في السكربتِ مَرَّةً واحِدَة.</summary>
    private static string WithArray(string script, string array, string body)
    {
        var m = Regex.Matches(script, $@"(?ms)^{array}=\(\n.*?^\)$");
        Assert.True(m.Count == 1, $"مَصفوفَةُ {array}=( وُجِدَت {m.Count} مَرَّة — الحَقنُ أَعمى.");
        return script[..m[0].Index] + $"{array}=(\n{body})" + script[(m[0].Index + m[0].Length)..];
    }

    /// <summary><c>curl</c> مَصنوع: يَكتُبُ صَفحَةً صَغيرَةً في وِجهَةِ <c>-o</c> ويَطبَعُ ‏200 لِـ<c>-w</c>.</summary>
    private const string FakeCurl =
        "#!/bin/bash\nout=\"\"\nwhile [ $# -gt 0 ]; do case \"$1\" in -o) out=\"$2\"; shift 2;; -w) shift 2;; *) shift;; esac; done\n"
      + "if [ -n \"$out\" ] && [ \"$out\" != /dev/null ]; then printf '<html>ok</html>\\n' > \"$out\"; fi\nprintf 200\n";

    private (int Code, string Out, int Written) Run(string script)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-capture-counter-");
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(dir.FullName, "bin"));
            var curl = Path.Combine(bin.FullName, "curl");
            File.WriteAllText(curl, FakeCurl, new System.Text.UTF8Encoding(false));
            File.SetUnixFileMode(curl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(dir.FullName, "capture.sh");
            File.WriteAllText(path, script, new System.Text.UTF8Encoding(false));
            var outDir = Path.Combine(dir.FullName, "out");
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { path, outDir, "http://127.0.0.1:9" })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.Environment["PATH"] = bin.FullName + ":" + (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin");
            foreach (var k in psi.Environment.Keys.Where(k => k.StartsWith("WSL_CAPTURE_", StringComparison.Ordinal)).ToList())
                psi.Environment.Remove(k);   // ‏لا مِلَفَّ مُهَيَّأ: لا دُخولَ ولا صَفحَةَ مَحروسَة
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(20_000), "السكربتُ لَم يَنتَهِ في عِشرينَ ثانِيَة.");
            var written = Directory.Exists(outDir) ? Directory.GetFiles(outDir, "*.html").Length : 0;
            return (p.ExitCode, stdout.Result + stderr.Result, written);
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary><b>السالِب</b>: مَصفوفاتٌ فارِغَةٌ ولا مِلَفّ ⇐ «الصَفَحاتُ المُلتَقَطَة: 0» و<c>✗ BLIND CAPTURE</c> ومَخرَجٌ ‏1 — لا «اِلتُقِطَت» خَضراء.</summary>
    [Fact]
    public void A_capture_that_writes_no_page_prints_zero_and_fails()
    {
        var s = Script();
        foreach (var array in new[] { "PAGES", "GUARDED_PAGES", "SHEETS" })
            s = WithArray(s, array, "");

        var (code, text, written) = Run(s);
        output.WriteLine($"مَخرَج {code} · كُتِبَ {written}\n{text}");

        Assert.Equal(0, written);
        Assert.Contains("الصَفَحاتُ المُلتَقَطَة: 0", text, StringComparison.Ordinal);
        Assert.Contains("✗ BLIND CAPTURE", text, StringComparison.Ordinal);
        Assert.Equal(1, code);
    }

    /// <summary><b>المُوجَب</b>: مَدخَلٌ عامٌّ واحِدٌ يُكتَب ⇐ يُعَدُّ واحِداً ويَنجَح — فَالعَدّادُ يَعُدُّ ما كُتِبَ لا طولَ مَصفوفَةٍ ولا شَيئاً ثابِتاً.</summary>
    [Fact]
    public void A_capture_that_writes_one_page_counts_it_and_passes()
    {
        var s = WithArray(Script(), "PAGES", "  \"a-page:/a\"\n");
        s = WithArray(s, "GUARDED_PAGES", "");
        s = WithArray(s, "SHEETS", "");

        var (code, text, written) = Run(s);
        output.WriteLine($"مَخرَج {code} · كُتِبَ {written}\n{text}");

        Assert.Equal(1, written);
        Assert.Contains("الصَفَحاتُ المُلتَقَطَة: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BLIND CAPTURE", text, StringComparison.Ordinal);
        Assert.Equal(0, code);
    }

    /// <summary>والعَدّادُ يَزيدُ في الحَلقَتَينِ كِلتَيهِما بَعدَ كِتابَةِ المِلَفّ — صَفحَةٌ مَحروسَةٌ لا تُعَدُّ قَبلَ أَن تُكتَب ولا تُنسى.</summary>
    [Fact]
    public void The_counter_rises_after_each_written_page_public_and_guarded()
    {
        var s = Script();
        Assert.Equal(2, Regex.Matches(s, @"captured=\$\(\(captured \+ 1\)\)").Count);
        var guardedWrite = s.IndexOf("guarded_ok=$((guarded_ok + 1))", StringComparison.Ordinal);
        var guardedCount = s.IndexOf("captured=$((captured + 1))", guardedWrite, StringComparison.Ordinal);
        Assert.True(guardedWrite > 0 && guardedCount > guardedWrite && guardedCount - guardedWrite < 80, "عَدُّ المَحروسَةِ لَيسَ بِجِوارِ كِتابَتِها.");
        Assert.True(s.IndexOf("if [ \"$captured\" -eq 0 ]", StringComparison.Ordinal) > guardedCount, "فَحصُ الصِفرِ قَبلَ الحَلقَة.");
    }
}
