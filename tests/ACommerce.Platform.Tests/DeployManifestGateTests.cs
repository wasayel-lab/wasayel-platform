using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَوّابَةُ ما يَصِلُ الـSpace العامَّ مُشَغَّلَةً — لا مَقروءَةً (‏2026-10-25، تَدقيقُ الإطلاق lens-checkers-ci-5، كُبرى) ═══════════════
//
// **المَقيسُ قَبل** (`2c02200`): `scripts/deploy-manifest.sh` هُوَ الحارِسُ الوَحيدُ الَّذي يُبقي `docs/` و`CLAUDE.md` و`scripts/`
// و`tests/` و`.github/` خارِجَ الـSpace — وهو **عامّ**: كُلُّ مِلَفٍّ يُدفَعُ يُقرَأُ في صَفحَةِ «Files». ولا صَفَّ يُشَغِّلُه: الحَقيبَةُ تَقرَأُ نَصَّه
// (`BuildIdentityEndpointTests`) و`deploy-hf.yml` يُناديه، وCI لا يُناديه. فَالطَفرَةُ M12 (`[ "$leak" -eq 0 ] || exit 1` ← `|| true`) تَترُكُ
// الحَقيبَةَ خَضراء، ويُدفَعُ ما تَسَرَّبَ في أَوَّلِ نَشر. **وصارَ** السكربتُ يُشَغَّلُ هُنا بِـbash في مُستَودَعِ git مَصنوعٍ إغلاقُ مَشروعِه
// يَبلُغُ `tests/` أَو `docs/` — فَيُنتَظَرُ رَمزُ 1 وسَطرُ `::error::` ولا تَجهيز؛ ومَعَه الحالَةُ النَظيفَةُ (0 وقائِمَةٌ بِلا مَحظور)، والمُستَودَعُ
// الحَقيقيُّ نَفسُه. (نَسَقُ `DeployDirectionTests`: عَلى Windows لا bash مَعلومَ المَوضِعِ فَيُطبَعُ ذلكَ ويُرجَع.)

public class DeployManifestGateTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private bool NoBash()
    {
        if (!OperatingSystem.IsWindows() && File.Exists("/bin/bash")) return false;
        output.WriteLine("لا /bin/bash هُنا (Windows) — يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
        return true;
    }

    internal static (int Code, string Out, string Err) Run(string exe, string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment.Remove("GIT_DIR");
        psi.Environment.Remove("GIT_WORK_TREE");
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "t";
        psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "t@example.invalid";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(30_000), $"{exe} لَم يَنتَهِ في ثَلاثينَ ثانِيَة.");
        return (p.ExitCode, stdout.Result.Trim(), stderr.Result.Trim());
    }

    /// <summary>مُستَودَعٌ مَصنوع: مَشروعُ التَطبيقِ يُحيلُ إلى مَكتَبَة، ومِلَفّاتُ الجَذرِ الخَمسَة، و<c>docs/</c> و<c>tests/</c> مُتَتَبَّعان —
    /// والسكربتُ الحَقيقيُّ بِحَرفِه في <c>scripts/</c>. و<paramref name="extraReference"/> إحالَةٌ ثانِيَةٌ مِن مَشروعِ التَطبيق.</summary>
    internal static DirectoryInfo Scratch(string? extraReference)
    {
        var dir = Directory.CreateTempSubdirectory("wasayel-deploy-manifest-");
        void Write(string rel, string text)
        {
            var path = Path.Combine(dir.FullName, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        var refs = "    <ProjectReference Include=\"..\\..\\libs\\a\\a.csproj\" />\n"
                 + (extraReference is null ? "" : $"    <ProjectReference Include=\"{extraReference}\" />\n");
        Write("apps/V1.App/V1.App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\">\n  <ItemGroup>\n" + refs + "  </ItemGroup>\n</Project>\n");
        Write("apps/V1.App/Program.cs", "// app\n");
        Write("libs/a/a.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("libs/a/A.cs", "// a\n");
        Write("libs/unreferenced/u.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        foreach (var f in new[] { "Directory.Build.props", "Directory.Packages.props", "Dockerfile", ".dockerignore", "README.md", "CLAUDE.md" })
            Write(f, f + "\n");
        Write("docs/plan.md", "# خُطَّةُ الإنتاج\n");
        Write("docs/tool/tool.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("tests/t/t.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("tests/t/T.cs", "// t\n");
        Directory.CreateDirectory(Path.Combine(dir.FullName, "scripts"));
        File.Copy(Path.Combine(RepoRoot, "scripts", "deploy-manifest.sh"), Path.Combine(dir.FullName, "scripts", "deploy-manifest.sh"));

        var everything = Directory.EnumerateFileSystemEntries(dir.FullName).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        foreach (var git in new[] { new[] { "init", "-q", "-b", "main" }, ["add", "--", .. everything], ["commit", "-q", "-m", "scratch"] })
        {
            var (code, o, e) = Run("git", dir.FullName, git);
            Assert.True(code == 0, $"git {string.Join(' ', git)} خَرَجَ {code}: {o} {e}");
        }
        return dir;
    }

    private (int Code, string Out, string Err) Manifest(DirectoryInfo dir, params string[] args)
    {
        var r = Run("/bin/bash", dir.FullName, [Path.Combine(dir.FullName, "scripts", "deploy-manifest.sh"), .. args]);
        output.WriteLine($"[{string.Join(' ', args)}] → {r.Code} · {r.Err.Split('\n').FirstOrDefault()}");
        return r;
    }

    private static readonly string[] Forbidden = ["docs/", "scripts/", "tests/", ".github/", "CLAUDE.md", "docker-compose.yml"];

    [Fact]
    public void A_clean_closure_lists_the_app_its_references_and_the_five_root_files_and_nothing_forbidden()
    {
        if (NoBash()) return;
        var dir = Scratch(extraReference: null);
        try
        {
            var (code, outText, err) = Manifest(dir);
            Assert.True(code == 0, $"رَمزُ {code}: {err}");
            var files = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(new[]
            {
                ".dockerignore", "Directory.Build.props", "Directory.Packages.props", "Dockerfile", "README.md",
                "apps/V1.App/Program.cs", "apps/V1.App/V1.App.csproj", "libs/a/A.cs", "libs/a/a.csproj",
            }, files);
            Assert.DoesNotContain(files, f => Forbidden.Any(p => f.StartsWith(p, StringComparison.Ordinal)));
            Assert.Contains("مَشاريع في الإغلاق: 2 · مِلَفّات مَسموحَة: 9", err, StringComparison.Ordinal);
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary><b>الإغلاقُ يَبلُغُ مَحظوراً ⇒ رَمزُ 1 وسَطرُ <c>::error::</c> يُسَمّي البادِئَة، ولا تَجهيز</b> — في كُلِّ واحِدَةٍ مِن ثَلاثِ عَيِّنات.</summary>
    [Fact]
    public void A_closure_that_reaches_tests_or_docs_exits_1_names_the_prefix_and_stages_nothing()
    {
        if (NoBash()) return;
        var cases = new (string Name, string Reference, string Prefix)[]
        {
            ("التَطبيقُ يُحيلُ إلى مَشروعِ اختِبار", "..\\..\\tests\\t\\t.csproj", "tests/"),
            ("التَطبيقُ يُحيلُ إلى أَداةٍ تَحتَ docs", "..\\..\\docs\\tool\\tool.csproj", "docs/"),
        };
        foreach (var c in cases)
        {
            var dir = Scratch(c.Reference);
            try
            {
                var stage = Path.Combine(dir.FullName, "..", Path.GetFileName(dir.FullName) + "-stage");
                var (code, outText, err) = Manifest(dir, "--stage", stage);
                output.WriteLine($"«{c.Name}» → {code} · {err.Split('\n').FirstOrDefault()}");
                Assert.True(code == 1, $"«{c.Name}»: رَمزُ {code} لا 1 — {err}");
                Assert.Contains($"::error::تَسَرَّبَ إلى القائِمَة", err, StringComparison.Ordinal);
                Assert.Contains($"«{c.Prefix}»", err, StringComparison.Ordinal);
                Assert.Equal("", outText);                                       // ‏لا قائِمَةَ تُطبَع
                Assert.False(Directory.Exists(stage), $"«{c.Name}»: جُهِّزَ {stage} مِن قائِمَةٍ مُتَسَرِّبَة.");
            }
            finally { dir.Delete(recursive: true); }
        }

        // ‏والثالِثَة: إحالَةٌ إلى مَشروعٍ غائِبٍ رَمزُ 1 أَيضاً — لا قائِمَةٌ ناقِصَةٌ تَمُرّ.
        var missing = Scratch("..\\..\\libs\\gone\\gone.csproj");
        try
        {
            var (code, _, err) = Manifest(missing);
            Assert.True(code == 1, $"إحالَةٌ إلى غائِب: رَمزُ {code} — {err}");
            Assert.Contains("::error::مَشروع مُحالٌ إلَيه غَير مَوجود", err, StringComparison.Ordinal);
        }
        finally { missing.Delete(recursive: true); }
    }

    [Fact]
    public void Staging_a_clean_closure_copies_exactly_the_listed_files_from_head()
    {
        if (NoBash()) return;
        var dir = Scratch(extraReference: null);
        var stage = Path.Combine(dir.FullName, "..", Path.GetFileName(dir.FullName) + "-stage");
        try
        {
            var (code, outText, err) = Manifest(dir, "--stage", stage);
            Assert.True(code == 0, $"رَمزُ {code}: {err}");
            var listed = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            var staged = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(stage, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            output.WriteLine($"مُدرَجٌ {listed.Length} · مُجَهَّزٌ {staged.Length}");
            Assert.True(listed.Length > 0, "أَداةٌ عَمياء: قائِمَةٌ فارِغَة.");
            Assert.Equal(listed, staged);
        }
        finally
        {
            dir.Delete(recursive: true);
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    /// <summary><b>والمُستَودَعُ الحَقيقيُّ نَفسُه يَعبُرُها</b> — قِراءَةٌ بِلا <c>--stage</c>: رَمزُ 0، وقائِمَةٌ بِلا مَحظور، ومَشروعُ التَطبيقِ فيها.</summary>
    [Fact]
    public void The_real_tree_passes_the_gate_with_nothing_forbidden()
    {
        if (NoBash()) return;
        var (code, outText, err) = Run("/bin/bash", RepoRoot, Path.Combine(RepoRoot, "scripts", "deploy-manifest.sh"));
        output.WriteLine(err.Split('\n').FirstOrDefault() ?? "");
        Assert.True(code == 0, $"رَمزُ {code}: {err}");
        var files = outText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(files.Length > 100, $"أَداةٌ عَمياء: {files.Length} مِلَفّاً في القائِمَة.");
        Assert.Contains("apps/V1.App/V1.App.csproj", files);
        Assert.Contains("Dockerfile", files);
        var leaked = files.Where(f => Forbidden.Any(p => f.StartsWith(p, StringComparison.Ordinal))).ToList();
        Assert.True(leaked.Count == 0, "تَسَرَّبَ: " + string.Join(", ", leaked.Take(10)));
    }
}
