using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ سكربتٌ تُشَغِّلُه وَثيقَةٌ بِـ`./scripts/…` يُشَغَّلُ بِه فِعلاً (‏2026-10-12، تَيّارُ S10) ═══════════════════
//
// **الكَلفَةُ الَّتي كَتَبَته**: `DEPLOY.md` §٢·و يَقولُ `./scripts/verify-production-boot.sh`، والمِلَفُّ مُتَتَبَّعٌ
// ‏`100644` — فَعَلى لينكس يُرَدُّ «Permission denied» بِرَمزِ 126 قَبلَ سَطرِه الأَوَّل، ومِثلُه أَربَعَةٌ أُخرى تُشَغِّلُها
// وَثائِقُها هكَذا (`verify-architecture.sh` في `CLAUDE.md`، و`verify-i18n-debt.sh` و`verify-api-endpoints.sh`
// و`verify-api-contract.sh`). ولَم يَرَه أَحَد: `verify-static.sh` يُناديها بِـ`bash` فَالـCI أَخضَر، والمالِكُ عَلى وِندوز
// حَيثُ لا يُقرَأُ بِتُّ التَنفيذِ ويُودَعُ كُلُّ سكربتٍ جَديدٍ ‏`100644` — فَالعَطَبُ يَعودُ مَعَ كُلِّ سكربتٍ يُضافُ مِن هُناك.
//
// **والحارِسُ هُنا لا في الذاكِرَة** (القاعِدَة ٢): كُلُّ `./scripts/X.sh` في وَثائِقِ المُستَودَعِ الحَيَّةِ ومَساراتِ CI
// ورُؤوسِ السكربتاتِ مَوجودٌ وقابِلٌ لِلتَنفيذ. ويُقرَأُ البِتُّ مِن نِظامِ المِلَفّات: `actions/checkout` عَلى لينكس يَضبِطُه
// مِن نَمَطِ الفِهرِس، فَهُوَ نَمَطُ الإيداعِ بِعَينِه هُناك. وعَلى وِندوز لا بِتَّ يُقرَأُ فَيُتَخَطّى. (‏`docs/heritage/`
// مَنقولٌ مِن المُستَودَعِ القَديمِ ويَصِفُ سكربتاتِه هُوَ، فَلا يُعَدّ.)
//
// **والبِتُّ مِن الفِهرِسِ لا مِن نِظامِ المِلَفّات** (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): الصَفُّ كانَ
// يَرجِعُ عَلى وِندوز قَبلَ أَن يَقيسَ ولا يَطبَعُ شَيئاً — وهُناكَ بِعَينِه يُودَعُ السكربتُ ‏`100644` — ويَقرَأُ نَمَطَ شَجَرَةِ العَمَلِ
// لا ما أُودِع: `chmod +x` بِلا `git update-index --chmod=+x` يَخضَرُّ مَحَلِّيّاً ويُرَدُّ 126 بَعدَ أَوَّلِ `checkout`. فَيُقرَأُ الآنَ
// `git ls-files -s -- scripts/*.sh` عَلى كُلِّ نِظام — ‏`100755` أَو لا — ويُطبَعُ ما فُحِصَ ويَفشَلُ عَلى الصِفر.

public class ScriptExecutableBitTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static readonly Regex DotSlashScript = new(@"(?<![\w/.])\./scripts/(?<name>[A-Za-z0-9_.-]+\.sh)\b");

    private static IEnumerable<string> LivingDocuments()
    {
        var docs = Path.Combine(RepoRoot, "docs");
        foreach (var md in Directory.EnumerateFiles(docs, "*.md", SearchOption.TopDirectoryOnly)) yield return md;
        foreach (var root in new[] { "CLAUDE.md", "README.md", "INSTALL.md" })
            if (File.Exists(Path.Combine(RepoRoot, root))) yield return Path.Combine(RepoRoot, root);
        foreach (var yml in Directory.EnumerateFiles(Path.Combine(RepoRoot, ".github", "workflows"), "*.yml")) yield return yml;
        foreach (var sh in Directory.EnumerateFiles(Path.Combine(RepoRoot, "scripts"), "*.sh")) yield return sh;
    }

    /// <summary>الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): الشَكلُ المُوَثَّقُ يُمسَك، والمَسارُ المُطلَقُ أَو المُركَّبُ
    /// لا يُحسَبُ استِدعاءً بِـ`./`.</summary>
    [Fact]
    public void ThePattern_CatchesTheDocumentedForm_AndOnlyIt()
    {
        Assert.Equal("verify-production-boot.sh",
            DotSlashScript.Match("./scripts/verify-production-boot.sh --keep-log").Groups["name"].Value);
        Assert.Equal("verify-static.sh", DotSlashScript.Match("        run: ./scripts/verify-static.sh").Groups["name"].Value);
        Assert.DoesNotMatch(DotSlashScript, "bash scripts/verify-architecture.sh");
        Assert.DoesNotMatch(DotSlashScript, "../scripts/verify-css.sh");
        Assert.DoesNotMatch(DotSlashScript, "apps/./scripts/x.sh");
    }

    /// <summary><b>كُلُّ سكربتٍ تُشَغِّلُه وَثيقَةٌ حَيَّةٌ بِـ<c>./scripts/…</c> مَوجودٌ وبِتُّ تَنفيذِه مَضبوط</b> — والعَدّادانِ
    /// (الوَثائِقُ المَقروءَة والسكربتاتُ المُسَمّاة) يُطبَعانِ في الرِسالَةِ ويَفشَلانِ عَلى الصِفر.</summary>
    [Fact]
    public void EveryScriptADocumentRunsWithDotSlash_ExistsAndIsExecutable()
    {
        var documents = LivingDocuments().ToList();
        Assert.True(documents.Count > 20, $"أَداةٌ عَمياء: {documents.Count} وَثيقَةً مَقروءَة.");

        var named = documents
            .SelectMany(d => DotSlashScript.Matches(File.ReadAllText(d)).Select(m => (Doc: d, Name: m.Groups["name"].Value)))
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.Select(x => Path.GetRelativePath(RepoRoot, x.Doc)).Distinct().ToList());
        Assert.True(named.Count > 5, $"أَداةٌ عَمياء: {named.Count} سكربتاً مُسَمّىً في {documents.Count} وَثيقَة.");
        Assert.Contains("verify-production-boot.sh", named.Keys);

        var missing = named.Keys.Where(n => !File.Exists(Path.Combine(RepoRoot, "scripts", n))).ToList();
        Assert.True(missing.Count == 0,
            "وَثيقَةٌ تُشَغِّلُ سكربتاً لا وُجودَ لَه: "
          + string.Join("; ", missing.Select(m => $"{m} ← {string.Join(", ", named[m])}")));

        // ‏(‏2026-10-24) نَمَطُ الإيداعِ مِن الفِهرِسِ عَلى كُلِّ نِظام — لا رُجوعَ عَلى وِندوز، ولا قِراءَةَ لِشَجَرَةِ العَمَل.
        var (modes, malformed) = ParseIndexModes(GitLsFilesScripts());
        output.WriteLine($"وَثائِقُ مَقروءَة: {documents.Count} · سكربتاتٌ مُسَمّاةٌ بِـ./: {named.Count} · أَسطُرُ الفِهرِس: {modes.Count} (مُشَوَّهَة {malformed.Count})");
        Assert.True(modes.Count > 5, $"أَداةٌ عَمياء: {modes.Count} سَطراً مِن git ls-files -s -- scripts/*.sh.");
        Assert.True(malformed.Count == 0, "سَطرُ فِهرِسٍ لا يُقرَأ: " + string.Join("; ", malformed));

        var notExecutable = NotExecutableInIndex(named.Keys, modes);
        foreach (var n in named.Keys.OrderBy(n => n, StringComparer.Ordinal))
            output.WriteLine($"  {(modes.TryGetValue("scripts/" + n, out var m) ? m : "غَيرُ مُتَتَبَّع")}  scripts/{n}");
        output.WriteLine($"فُحِصَ {named.Count} سكربتاً في الفِهرِس، ومِنها بِلا 100755: {notExecutable.Count}");
        Assert.True(notExecutable.Count == 0,
            $"سكربتٌ تُشَغِّلُه وَثيقَتُه بِـ./ وهُوَ في الفِهرِسِ بِلا 100755 ({named.Count} مُسَمّىً، {documents.Count} وَثيقَة) — "
          + "يُرَدُّ Permission denied (126) قَبلَ سَطرِه الأَوَّل؛ git update-index --chmod=+x scripts/<name>: "
          + string.Join("; ", notExecutable.Select(n => $"{n} ← {string.Join(", ", named[n])}")));
    }

    /// <summary>مُخرَجُ <c>git ls-files -s -- scripts/*.sh</c> مُشَغَّلاً في جَذرِ المُستَودَع — نَمَطُ ما أُودِع، لا ما في القُرص.</summary>
    private static string GitLsFilesScripts()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "ls-files", "-s", "--", "scripts/*.sh" }) psi.ArgumentList.Add(a);
        System.Diagnostics.Process p;
        try { p = System.Diagnostics.Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Fail("أَداةٌ غائِبَة: git — لا يُقرَأُ نَمَطُ الإيداعِ بِدونِها."); return ""; }
        using (p)
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(15_000), "git ls-files لَم يَنتَهِ في خَمسَ عَشرَةَ ثانِيَة.");
            Assert.True(p.ExitCode == 0, $"git ls-files خَرَجَ {p.ExitCode}: {err.Trim()} — لَيسَ شَجَرَةَ عَمَلٍ أَو الأَداةُ مُعَطَّلَة.");
            return stdout.GetAwaiter().GetResult();
        }
    }

    /// <summary>سُطورُ <c>git ls-files -s</c> (‏<c>&lt;نَمَط&gt; &lt;بَصمَة&gt; &lt;مَرحَلَة&gt;\t&lt;مَسار&gt;</c>) خَريطَةَ مَسارٍ ← نَمَط؛ وما لا يُطابِقُ
    /// الشَكلَ يُعادُ مُشَوَّهاً لا يُبتلَع.</summary>
    internal static (IReadOnlyDictionary<string, string> Modes, IReadOnlyList<string> Malformed) ParseIndexModes(string lsFiles)
    {
        var modes = new Dictionary<string, string>(StringComparer.Ordinal);
        var malformed = new List<string>();
        foreach (var raw in lsFiles.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var m = Regex.Match(line, @"^(?<mode>[0-7]{6}) [0-9a-f]{40,64} [0-3]\t(?<path>.+)$");
            if (m.Success) modes[m.Groups["path"].Value] = m.Groups["mode"].Value;
            else malformed.Add(line);
        }
        return (modes, malformed);
    }

    /// <summary>ما لَيسَ <c>100755</c> في الفِهرِسِ مِن السكربتاتِ المُسَمّاة — وغَيرُ المُتَتَبَّعِ مِنها مَعَها: لا نَمَطَ يُودَعُ لَه.</summary>
    internal static IReadOnlyList<string> NotExecutableInIndex(IEnumerable<string> named, IReadOnlyDictionary<string, string> modes)
        => named.Where(n => !modes.TryGetValue("scripts/" + n, out var mode) || mode != "100755")
            .OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary><b>حَقنُ عَيب — قارِئُ الفِهرِسِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): عَيِّنَةٌ مِن ثَلاثَةِ أَسطُرٍ بِشَكلِ <c>git ls-files -s</c>
    /// — سكربتٌ ‏<c>100755</c> وسكربتٌ ‏<c>100644</c> (ما يُودِعُه وِندوز) — وسكربتٌ مُسَمّىً غَيرُ مُتَتَبَّع، وسَطرٌ مُشَوَّه.</summary>
    [Fact]
    public void The_index_mode_reader_can_actually_go_red()
    {
        const string sample =
            "100755 f25300f53aa94ac0f7c0d7b38c972ef840f9c31b 0\tscripts/verify-production-boot.sh\n"
          + "100644 e5d91c7040b37503b796dc03f0808379acf46c8b 0\tscripts/walk-after-the-key.sh\r\n"
          + "not a line of git ls-files\n";
        var (modes, malformed) = ParseIndexModes(sample);
        Assert.Equal(2, modes.Count);
        Assert.Equal("100755", modes["scripts/verify-production-boot.sh"]);
        Assert.Equal("100644", modes["scripts/walk-after-the-key.sh"]);
        Assert.Equal(new[] { "not a line of git ls-files" }, malformed);

        var named = new[] { "verify-production-boot.sh", "walk-after-the-key.sh", "never-added.sh" };
        var red = NotExecutableInIndex(named, modes);
        output.WriteLine($"حُقِنَ {named.Length} سكربتاً، ويُرَدُّ {red.Count}: {string.Join(", ", red)}");
        Assert.Equal(new[] { "never-added.sh", "walk-after-the-key.sh" }, red);
        Assert.Empty(NotExecutableInIndex(new[] { "verify-production-boot.sh" }, modes));
    }
}
