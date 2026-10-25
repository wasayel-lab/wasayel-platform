using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ التَراجُعُ بِاليَدِ في §٤‑ب يُبنى ولا يَنشُرُ غُرفَةَ العَمَل — مُشَغَّلاً لا مَقروءاً (‏2026-10-25، تَدقيقُ الإطلاق lens-docs-vs-code-4، كُبرى) ═══
//
// **المَقيسُ قَبل** (`dace7cd`): كُتلَةُ §٤‑ب كانَت `git checkout <sha>` ثُمَّ `git push --force … HEAD:refs/heads/main` مِن الشَجَرَةِ كامِلَةً — فَلا
// `.source-revision` (غَيرُ مُتَتَبَّع، تَكتُبُه `deploy-hf.yml` وَحدَها بَعدَ التَجهيز) وحارِسُ الـ`Dockerfile` يَخرُجُ 1 («لا بِناءَ بِلا بَصمَة»)،
// ويُدفَعُ `docs/` و`tests/` و`scripts/` و`CLAUDE.md` بِتاريخِها إلى Space عامّ. **وصارَت** الكُتلَةُ خُطُواتِ الوَظيفَةِ نَفسَها: `deploy-manifest.sh
// --stage` ثُمَّ `.source-revision` ثُمَّ مُستَودَعٌ بِإيداعٍ واحِدٍ يُدفَع. وهُنا تُشَغَّلُ بِحَرفِها مِن `docs/DEPLOY.md` عَلى مُستَودَعٍ مَصنوع
// (`DeployManifestGateTests.Scratch` — السكربتُ الحَقيقيُّ بِحَرفِه) إلى مُستَودَعِ Space عارٍ، ويُشَغَّلُ حارِسُ الـ`Dockerfile` بِحَرفِه عَلى ما دُفِع؛
// والكُتلَةُ القَديمَةُ بِحَرفِها عَيِّنَةٌ مَحقونَةٌ يَحمَرُّ عَلَيها المُتَحَقِّق (القاعِدَة ١٠). (عَلى Windows لا bash مَعلومَ المَوضِعِ فَيُطبَعُ ذلكَ ويُرجَع.)
public class DeployManualRollbackTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string NewBlockStart = "```bash\n# ‏(‏2026-10-25) خُطُواتُ وَظيفَةِ النَشرِ نَفسُها بِاليَد";
    private const string OldBlock =
        "git clone https://github.com/wasayel-lab/wasayel-platform.git w && cd w\n" +
        "git checkout <الإيداع السابق>\n" +
        "git push --force \"https://<user>:<HF_TOKEN>@huggingface.co/spaces/<owner>/<space>.git\" HEAD:refs/heads/main\n";
    private const string SpaceUrl = "https://<user>:<HF_TOKEN>@huggingface.co/spaces/<owner>/<space>.git";

    private static readonly string[] Forbidden = ["docs/", "scripts/", "tests/", ".github/", "CLAUDE.md", "docker-compose.yml"];

    private bool NoBash()
    {
        if (!OperatingSystem.IsWindows() && File.Exists("/bin/bash")) return false;
        output.WriteLine("لا /bin/bash هُنا (Windows) — يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
        return true;
    }

    private static string Deploy() => File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md"));

    /// <summary>الكُتلَةُ الجَديدَةُ بِحَرفِها مِن الدَليل — بِلا سِياجِها.</summary>
    private static string NewBlock()
    {
        var deploy = Deploy();
        var a = deploy.IndexOf(NewBlockStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ التَراجُعِ بِاليَدِ غائِبَةٌ عَن §٤‑ب — الأَداةُ عَمياء.");
        Assert.Equal(a, deploy.LastIndexOf(NewBlockStart, StringComparison.Ordinal));
        var body = deploy[(a + "```bash\n".Length)..];
        return body[..(body.IndexOf("\n```", StringComparison.Ordinal) + 1)];
    }

    /// <summary>حارِسُ البَصمَةِ في الـ<c>Dockerfile</c> بِحَرفِه — مِن قِراءَةِ <c>.source-revision</c> إلى رَفضِها — يُشَغَّلُ في <paramref name="dir"/>.</summary>
    private static (int Code, string Err) DockerfileGuard(string dir)
    {
        var docker = File.ReadAllText(Path.Combine(RepoRoot, "Dockerfile"));
        var a = docker.IndexOf("    rev=\"$(tr -d ' \\t\\r\\n' < .source-revision", StringComparison.Ordinal);
        Assert.True(a > 0, "حارِسُ .source-revision غائِبٌ عَن الـDockerfile.");
        var b = docker.IndexOf("    echo \"[build-identity] البَصمَة: $rev\"", a, StringComparison.Ordinal);
        Assert.True(b > a);
        var guard = docker[a..b].Replace(" \\\n", "\n", StringComparison.Ordinal);
        var script = Path.Combine(dir, "..", Path.GetFileName(dir) + "-guard.sh");
        File.WriteAllText(script, "set -eu\n" + guard + "\necho ok\n", new UTF8Encoding(false));
        try
        {
            var (code, _, err) = DeployManifestGateTests.Run("/bin/bash", dir, script);
            return (code, err);
        }
        finally { File.Delete(script); }
    }

    private sealed record Pushed(int Code, string Err, int Commits, string[] Files, string? Revision, string Message);

    /// <summary>يُشَغِّلُ <paramref name="block"/> مِن جَذرِ مُستَودَعٍ مَصنوعٍ إلى مُستَودَعِ Space عارٍ، ويَقرَأُ ما وَصَلَه.</summary>
    private Pushed RunBlock(string block, DirectoryInfo repo, string space)
    {
        var (_, sha, _) = DeployManifestGateTests.Run("git", repo.FullName, "rev-parse", "HEAD");
        var body = block
            .Replace("git clone https://github.com/wasayel-lab/wasayel-platform.git w && cd w\n", "", StringComparison.Ordinal)
            .Replace("<الإيداع السابق>", sha, StringComparison.Ordinal)
            .Replace(SpaceUrl, space, StringComparison.Ordinal);
        Assert.DoesNotContain("<الإيداع السابق>", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SpaceUrl, body, StringComparison.Ordinal);
        var script = Path.Combine(repo.FullName, "..", repo.Name + "-rollback.sh");
        // ‏والمُجَلَّدُ المُؤَقَّتُ الَّذي تُنشِئُه الكُتلَةُ يُمحى بَعدَها — تَنظيفُ الاختِبارِ لا جُزءٌ مِن الكُتلَة.
        File.WriteAllText(script, "set -euo pipefail\n" + body + "if [ -n \"${stage:-}\" ]; then cd / && rm -rf \"$(dirname \"$stage\")\"; fi\n",
            new UTF8Encoding(false));
        try
        {
            var (code, outText, err) = DeployManifestGateTests.Run("/bin/bash", repo.FullName, script);
            output.WriteLine($"→ {code} · {err.Split('\n').FirstOrDefault()}");
            if (code != 0) return new Pushed(code, err + outText, 0, [], null, "");
            var (_, count, _) = DeployManifestGateTests.Run("git", space, "rev-list", "--count", "main");
            var (_, files, _) = DeployManifestGateTests.Run("git", space, "ls-tree", "-r", "--name-only", "main");
            var (revCode, rev, _) = DeployManifestGateTests.Run("git", space, "show", "main:.source-revision");
            var (_, message, _) = DeployManifestGateTests.Run("git", space, "log", "-1", "--format=%s", "main");
            return new Pushed(code, err, int.Parse(count), files.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                revCode == 0 ? rev.Trim() : null, message);
        }
        finally { File.Delete(script); }
    }

    private static string BareSpace(DirectoryInfo repo)
    {
        var space = Path.Combine(repo.FullName, "..", repo.Name + "-space.git");
        var (code, o, e) = DeployManifestGateTests.Run("git", Path.GetDirectoryName(repo.FullName)!, "init", "-q", "--bare", "-b", "main", space);
        Assert.True(code == 0, $"git init --bare: {o} {e}");
        return space;
    }

    private static void Clean(DirectoryInfo repo, string space)
    {
        repo.Delete(recursive: true);
        if (Directory.Exists(space)) Directory.Delete(space, recursive: true);
    }

    /// <summary><b>الكُتلَةُ الجَديدَةُ بِحَرفِها</b>: إيداعٌ واحِدٌ رِسالَتُه <c>deploy: &lt;sha&gt;</c>، وصِفرُ مِلَفٍّ مَحظور، و<c>.source-revision</c> بِالرَقَم،
    /// وحارِسُ الـ<c>Dockerfile</c> بِحَرفِه يَمُرُّ عَلى الشَجَرَةِ الَّتي دُفِعَت.</summary>
    [Fact]
    public void The_documented_manual_rollback_pushes_one_flat_commit_with_its_revision_and_nothing_forbidden()
    {
        if (NoBash()) return;
        var repo = DeployManifestGateTests.Scratch(extraReference: null);
        var space = BareSpace(repo);
        try
        {
            var (_, sha, _) = DeployManifestGateTests.Run("git", repo.FullName, "rev-parse", "HEAD");
            var pushed = RunBlock(NewBlock(), repo, space);
            Assert.True(pushed.Code == 0, $"رَمزُ {pushed.Code}: {pushed.Err}");
            output.WriteLine($"إيداعات: {pushed.Commits} · مِلَفّات: {pushed.Files.Length} · بَصمَة: {pushed.Revision}");
            Assert.Equal(1, pushed.Commits);
            Assert.Equal($"deploy: {sha}", pushed.Message);
            Assert.Equal(sha, pushed.Revision);
            Assert.True(pushed.Files.Length > 1, "أَداةٌ عَمياء: صِفرُ مِلَفٍّ مَدفوع.");
            Assert.DoesNotContain(pushed.Files, f => Forbidden.Any(p => f.StartsWith(p, StringComparison.Ordinal)));
            Assert.Contains("apps/V1.App/V1.App.csproj", pushed.Files);

            var checkout = Path.Combine(repo.FullName, "..", repo.Name + "-pushed");
            var (cc, co, ce) = DeployManifestGateTests.Run("git", Path.GetDirectoryName(repo.FullName)!, "clone", "-q", space, checkout);
            Assert.True(cc == 0, $"git clone: {co} {ce}");
            try
            {
                var (gc, ge) = DockerfileGuard(checkout);
                Assert.True(gc == 0, $"حارِسُ الـDockerfile خَرَجَ {gc} عَلى ما دُفِع: {ge}");
            }
            finally { Directory.Delete(checkout, recursive: true); }
        }
        finally { Clean(repo, space); }
    }

    /// <summary><b>والكُتلَةُ القَديمَةُ بِحَرفِها — العَيِّنَةُ المَحقونَة</b>: تَدفَعُ التاريخَ والمَحظورَ ولا بَصمَة، وحارِسُ الـ<c>Dockerfile</c> يَخرُجُ 1
    /// عَلى ما دَفَعَت — فَالمُتَحَقِّقُ أَعلاه يُفَرِّقُ لا يُوافِقُ دائِماً. وهي باقِيَةٌ في الدَليلِ تَحتَ سَطرِ ⚠ مُؤَرَّخ.</summary>
    [Fact]
    public void The_old_manual_rollback_goes_red_it_cannot_build_and_it_publishes_the_workroom()
    {
        if (NoBash()) return;
        var deploy = Deploy();
        var old = deploy.IndexOf("```bash\n" + OldBlock + "```", StringComparison.Ordinal);
        Assert.True(old > 0, "الكُتلَةُ القَديمَةُ لَيسَت في الدَليلِ بِحَرفِها — العَيِّنَةُ لا تَصِفُ ما كانَ مَكتوباً.");
        Assert.Contains("**لا تُنَفَّذُ الكُتلَةُ التالِيَة**", deploy[(old - 400)..old], StringComparison.Ordinal);

        var repo = DeployManifestGateTests.Scratch(extraReference: null);
        var space = BareSpace(repo);
        try
        {
            var pushed = RunBlock(OldBlock, repo, space);
            Assert.True(pushed.Code == 0, $"رَمزُ {pushed.Code}: {pushed.Err}");
            Assert.Null(pushed.Revision);
            Assert.Contains(pushed.Files, f => Forbidden.Any(p => f.StartsWith(p, StringComparison.Ordinal)));

            var checkout = Path.Combine(repo.FullName, "..", repo.Name + "-pushed");
            DeployManifestGateTests.Run("git", Path.GetDirectoryName(repo.FullName)!, "clone", "-q", space, checkout);
            try
            {
                var (gc, ge) = DockerfileGuard(checkout);
                output.WriteLine($"حارِسُ الـDockerfile عَلى الكُتلَةِ القَديمَة → {gc} · {ge}");
                Assert.Equal(1, gc);
                Assert.Contains("لا بِناءَ بِلا بَصمَة", ge, StringComparison.Ordinal);
            }
            finally { Directory.Delete(checkout, recursive: true); }
        }
        finally { Clean(repo, space); }
    }

    /// <summary>وخُطُواتُها خُطُواتُ الوَظيفَةِ نَفسُها بِأَسمائِها — يَحمَرُّ إن افتَرَقَت <c>deploy-hf.yml</c> عَن الدَليل.</summary>
    [Fact]
    public void The_manual_steps_are_the_deploy_jobs_own_steps()
    {
        var yml = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy-hf.yml"));
        Assert.Contains("bash scripts/deploy-manifest.sh --stage \"${RUNNER_TEMP}/hf-space\"", yml, StringComparison.Ordinal);
        Assert.Contains("printf '%s\\n' \"${sha}\" > \"${RUNNER_TEMP}/hf-space/.source-revision\"", yml, StringComparison.Ordinal);
        Assert.Contains("git init -q -b main", yml, StringComparison.Ordinal);
        Assert.Contains("git commit -q -m \"deploy: ${sha}\"", yml, StringComparison.Ordinal);
        var block = NewBlock();
        Assert.Contains("bash scripts/deploy-manifest.sh --stage \"$stage\"", block, StringComparison.Ordinal);
        Assert.Contains("printf '%s\\n' \"$sha\" > \"$stage/.source-revision\"", block, StringComparison.Ordinal);
        Assert.True(block.IndexOf("--stage", StringComparison.Ordinal) < block.IndexOf(".source-revision", StringComparison.Ordinal),
            "البَصمَةُ قَبلَ التَجهيز — والتَجهيزُ يَمحو مُجَلَّدَه أَوَّلاً.");
        Assert.Contains("git init -q -b main", block, StringComparison.Ordinal);
        Assert.Contains("git commit -q -m \"deploy: $sha\"", block, StringComparison.Ordinal);
        Assert.Contains("lens-docs-vs-code-4", Deploy(), StringComparison.Ordinal);
    }
}
