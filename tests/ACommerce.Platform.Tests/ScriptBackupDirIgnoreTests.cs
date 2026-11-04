using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما تَكتُبُه سكربتاتُ الإنتاجِ في `backups/` لا يُتتَبَّع — بِقِياسِ git نَفسِه (‏2026-11-04، الموجَة ١٢ G1 · ٢، مُراجَعَةُ 6af4f75) ═══
//
// **المَقيسُ قَبل**: خَمسَةُ سكربتاتٍ تُشَغَّلُ مِن جَذرِ المُستَودَعِ عَلى الإنتاج (`DEPLOY.md`) — `repair-commercial-categories` و`repair-plan-day-end`
// و`repair-studio-tier` و`repair-role-floor` و`withdraw-erased-owner-listings` — تَكتُبُ افتِراضاً في `backups/` بَيانَ ما مَسَّتهُ (مُعَرِّفاتُ
// الحِساباتِ والإعلاناتِ وإصداراتُها) وأَمرَ استِرجاعِه (`.restore.sql`)، و`git check-ignore -v backups/…json` خَرَجَ 1: لا قاعِدَةَ تَتَجاهَلُه، فَـ`git add .`
// واحِدٌ يُودِعُ مُعَرِّفاتِ الإنتاجِ في تاريخِ GitHub — الثُقبُ الَّذي سُدَّ لِـ`*.dump` (‏DEPLOY.md §٤‑د) ولِـ`/global.json`. والحاجِزُ هُنا شَكلُه لا وَعدُه
// (القاعِدَة ٢): كُلُّ سكربتٍ افتِراضُ مَجَلَّدِه `"backups"` يُعَدّ، ومِلَفٌّ في ذلكَ المَجَلَّدِ مُتَجاهَلٌ بِـ`git check-ignore` — ويَفشَلُ عَلى صِفرِ سكربت.

public class ScriptBackupDirIgnoreTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly Regex DefaultBackupDir = new(@"backupIdx\s*\+\s*1\s*\]\s*:\s*""(?<dir>[^""]+)""\s*;");

    private static int CheckIgnore(string path)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = RepoRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "check-ignore", "-q", "--no-index", "--", path }) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Fail("أَداةٌ غائِبَة: git — لا يُقاسُ .gitignore بِدونِها."); return -1; }
        using (p)
        {
            var err = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(15_000), "git check-ignore لَم يَنتَهِ في خَمسَ عَشرَةَ ثانِيَة.");
            Assert.True(p.ExitCode is 0 or 1, $"git check-ignore خَرَجَ {p.ExitCode}: {err.Trim()}");
            return p.ExitCode;
        }
    }

    [Fact]
    public void Every_scripts_default_backup_dir_is_ignored_by_git()
    {
        var found = new List<(string Script, string Dir)>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, "scripts"), "*.cs").OrderBy(f => f, StringComparer.Ordinal))
            foreach (Match m in DefaultBackupDir.Matches(File.ReadAllText(f)))
                found.Add((Path.GetFileName(f), m.Groups["dir"].Value));
        var tracked = found.Where(x => CheckIgnore($"{x.Dir}/{Path.GetFileNameWithoutExtension(x.Script)}-20261104-000000.restore.sql") != 0).ToList();
        output.WriteLine($"سكربتاتٌ تَكتُبُ مَجَلَّدَ نُسخَةٍ افتِراضِيّاً {found.Count} ({string.Join("، ", found.Select(x => $"{x.Script} → {x.Dir}/"))}) · غَيرُ مُتَجاهَلٍ {tracked.Count}");
        Assert.True(found.Count >= 5, $"أَداةٌ عَمياء: {found.Count} سكربتاً بِمَجَلَّدِ نُسخَةٍ افتِراضِيّ (المَقيسُ ‏2026-11-04: ‏5).");
        Assert.True(tracked.Count == 0, "مَجَلَّدٌ يَكتُبُ فيه سكربتُ إنتاجٍ ولا يَتَجاهَلُه git: " + string.Join("، ", tracked.Select(x => $"{x.Script} → {x.Dir}/")));
    }

    /// <summary>القِياسُ لَيسَ أَعمى: مِلَفٌّ تَحتَ <c>backups/</c> — في الجَذرِ أَو تَحتَ <c>scripts/</c> لِمَن يُشَغِّلُ مِن هُناك — مُتَجاهَل، ومِلَفٌّ بِالاسمِ نَفسِه
    /// خارِجَه وسكربتٌ مُتَتَبَّعٌ لا يُتَجاهَلان.</summary>
    [Theory]
    [InlineData("backups/withdraw-erased-owner-listings-20261104-000000.json", 0)]
    [InlineData("scripts/backups/role-floor-20261104-000000.restore.sql", 0)]
    [InlineData("withdraw-erased-owner-listings-20261104-000000.json", 1)]
    [InlineData("scripts/withdraw-erased-owner-listings.cs", 1)]
    public void Git_ignores_the_backup_dir_and_nothing_beside_it(string path, int expected)
        => Assert.Equal(expected, CheckIgnore(path));
}
