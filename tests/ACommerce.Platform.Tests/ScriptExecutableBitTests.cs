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

public class ScriptExecutableBitTests
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

        if (OperatingSystem.IsWindows()) return;   // ‏لا بِتَّ تَنفيذٍ يُقرَأُ هُناك.

        // حَلقَةٌ لا تَعبيرُ LINQ (‏2026-10-13): الحارِسُ `IsWindows` أَعلاه لا يَعبُرُ إلى داخِلِ تَعبيرِ lambda فَيُحَذِّرُ CA1416 — والحَلقَةُ يَراها.
        var notExecutable = new List<string>();
        foreach (var n in named.Keys.OrderBy(n => n, StringComparer.Ordinal))
            if ((File.GetUnixFileMode(Path.Combine(RepoRoot, "scripts", n)) & UnixFileMode.UserExecute) == 0) notExecutable.Add(n);
        Assert.True(notExecutable.Count == 0,
            $"سكربتٌ تُشَغِّلُه وَثيقَتُه بِـ./ وهُوَ بِلا بِتِّ تَنفيذ ({named.Count} مُسَمّىً، {documents.Count} وَثيقَة) — "
          + "يُرَدُّ Permission denied (126) قَبلَ سَطرِه الأَوَّل؛ git update-index --chmod=+x scripts/<name>: "
          + string.Join("; ", notExecutable.Select(n => $"{n} ← {string.Join(", ", named[n])}")));
    }
}
