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

    // ─── والرَمزُ لا يُكتَبُ في سَطرِ أَمر (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-6 الجَولَة ٢، الصُغرى ٤) ───
    //
    // ‏الكُتلَةُ أَعلاه تَضَعُ رَمزَ الكِتابَةِ في عُنوانِ الدَفعِ — يَظهَرُ في argv (‏`ps`) ما دامَ `git push` يَعمَل، ويَبقى في سِجِلِّ الصَدَفَة. والكُتلَةُ
    // الثالِثَةُ في §٤‑ب تَقرَؤُه بِـ`read -rs` وتُمَرِّرُه رَأسَ `Authorization` في بيئَةِ git (`GIT_CONFIG_*`) إلى عُنوانٍ بِلا رَمز. وهُنا تُشَغَّلُ الاثنَتانِ
    // بِحَرفِهِما وغِلافٌ لِـ`git` في أَوَّلِ PATH يُسَجِّلُ argv كُلِّ نِداءٍ ورَأسَ الدَفع، ويُحَوِّلُ عُنوانَ HF إلى مُستَودَعِ Space عارٍ مَحَلِّيّ.

    private const string TokenFreeBlockStart = "```bash\n# ‏(‏2026-10-25، مُراجَعَةُ W8-6 الجَولَة ٢) الخُطُواتُ نَفسُها";
    private const string FakeToken = "hf_FAKEtokenForTheRollbackTest0123456789";

    /// <summary>الكُتلَةُ الثالِثَةُ بِحَرفِها مِن الدَليل — بِلا سِياجِها.</summary>
    private static string TokenFreeBlock()
    {
        var deploy = Deploy();
        var a = deploy.IndexOf(TokenFreeBlockStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ التَراجُعِ بِلا رَمزٍ في سَطرِ الأَمرِ غائِبَةٌ عَن §٤‑ب — الأَداةُ عَمياء.");
        Assert.Equal(a, deploy.LastIndexOf(TokenFreeBlockStart, StringComparison.Ordinal));
        var body = deploy[(a + "```bash\n".Length)..];
        return body[..(body.IndexOf("\n```", StringComparison.Ordinal) + 1)];
    }

    private sealed record Shimmed(Pushed Pushed, string[] Argv, string[] PushHeaders);

    /// <summary>يُشَغِّلُ <paramref name="block"/> بِحَرفِه — <c>&lt;الإيداع السابق&gt;</c> و<c>&lt;user&gt;</c> و<c>&lt;HF_TOKEN&gt;</c> وَحدَها تُستَبدَل — والرَمزُ في
    /// مَدخَلِه، وغِلافُ <c>git</c> يُسَجِّلُ argv كُلِّ نِداءٍ (و<c>GIT_CONFIG_VALUE_0</c> عِندَ الدَفع) ويُحَوِّلُ عُنوانَ HF إلى <paramref name="space"/>.</summary>
    private Shimmed RunShimmed(string block, DirectoryInfo repo, string space)
    {
        var (_, sha, _) = DeployManifestGateTests.Run("git", repo.FullName, "rev-parse", "HEAD");
        var (_, realGit, _) = DeployManifestGateTests.Run("/bin/bash", repo.FullName, "-c", "command -v git");
        Assert.False(string.IsNullOrWhiteSpace(realGit), "أَداةٌ غائِبَة: git.");
        var body = block
            .Replace("git clone https://github.com/wasayel-lab/wasayel-platform.git w && cd w\n", "", StringComparison.Ordinal)
            .Replace("<الإيداع السابق>", sha, StringComparison.Ordinal)
            .Replace("<HF_TOKEN>", FakeToken, StringComparison.Ordinal)
            .Replace("<user>", "tester", StringComparison.Ordinal);
        var work = Path.Combine(repo.FullName, "..", repo.Name + "-shim");
        Directory.CreateDirectory(work);
        var argvLog = Path.Combine(work, "argv.log");
        var headerLog = Path.Combine(work, "headers.log");
        File.WriteAllText(Path.Combine(work, "git"),
            "#!/bin/bash\n" +
            "printf '%s\\n' \"$*\" >> \"$SHIM_ARGV\"\n" +
            "if [ \"${1:-}\" = push ]; then\n" +
            "  printf '%s\\n' \"${GIT_CONFIG_VALUE_0:-}\" >> \"$SHIM_HEADERS\"\n" +
            "  args=(); for a in \"$@\"; do case \"$a\" in https://*huggingface.co/*) args+=(\"$SHIM_SPACE\") ;; *) args+=(\"$a\") ;; esac; done\n" +
            "  exec \"$SHIM_REAL_GIT\" \"${args[@]}\"\n" +
            "fi\n" +
            "exec \"$SHIM_REAL_GIT\" \"$@\"\n", new UTF8Encoding(false));
        DeployManifestGateTests.Run("chmod", work, "+x", Path.Combine(work, "git"));
        var script = Path.Combine(work, "rollback.sh");
        File.WriteAllText(script, "set -euo pipefail\n" + body + "if [ -n \"${stage:-}\" ]; then cd / && rm -rf \"$(dirname \"$stage\")\"; fi\n",
            new UTF8Encoding(false));
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script })
            {
                WorkingDirectory = repo.FullName, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment.Remove("GIT_DIR");
            psi.Environment.Remove("GIT_WORK_TREE");
            psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "t";
            psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "t@example.invalid";
            psi.Environment["PATH"] = work + ":" + psi.Environment["PATH"];
            psi.Environment["SHIM_ARGV"] = argvLog;
            psi.Environment["SHIM_HEADERS"] = headerLog;
            psi.Environment["SHIM_SPACE"] = space;
            psi.Environment["SHIM_REAL_GIT"] = realGit.Trim();
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardInput.Write(FakeToken + "\n");
            p.StandardInput.Close();
            var outText = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(60_000), "الكُتلَةُ لَم تَنتَهِ في دَقيقَة.");
            output.WriteLine($"→ {p.ExitCode} · {err.Result.Split('\n').FirstOrDefault()}");
            string[] Lines(string f) => File.Exists(f) ? File.ReadAllLines(f) : [];
            if (p.ExitCode != 0) return new Shimmed(new Pushed(p.ExitCode, err.Result + outText.Result, 0, [], null, ""), Lines(argvLog), Lines(headerLog));
            var (_, count, _) = DeployManifestGateTests.Run("git", space, "rev-list", "--count", "main");
            var (_, files, _) = DeployManifestGateTests.Run("git", space, "ls-tree", "-r", "--name-only", "main");
            var (revCode, rev, _) = DeployManifestGateTests.Run("git", space, "show", "main:.source-revision");
            var (_, message, _) = DeployManifestGateTests.Run("git", space, "log", "-1", "--format=%s", "main");
            return new Shimmed(new Pushed(0, err.Result, int.Parse(count), files.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                revCode == 0 ? rev.Trim() : null, message), Lines(argvLog), Lines(headerLog));
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// <summary>هَل يَحمِلُ argv نِداءٍ ما الرَمزَ أَو تَرميزَه — حُكمُ الصَفِّ أَدناه، يُطَبَّقُ عَلى الكُتلَتَين.</summary>
    internal static bool TokenInArgv(IEnumerable<string> argv)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("tester:" + FakeToken));
        return argv.Any(l => l.Contains(FakeToken, StringComparison.Ordinal) || l.Contains(basic, StringComparison.Ordinal));
    }

    /// <summary><b>الكُتلَةُ الثالِثَةُ بِحَرفِها</b>: الرَمزُ مِن مَدخَلِها صِفرُ مَوضِعٍ في argv كُلِّ نِداءٍ لِـgit، ورَأسُ الدَفعِ يَحمِلُه (‏<c>Basic tester:token</c>)،
    /// وعُنوانُ الدَفعِ بِلا رَمز — والدَفعُ نَفسُه إيداعٌ واحِدٌ بِبَصمَتِه وبِلا مَحظور، وحارِسُ الـ<c>Dockerfile</c> يَمُرُّ عَلى ما دُفِع.</summary>
    [Fact]
    public void The_token_free_manual_rollback_keeps_the_token_out_of_every_argv_and_pushes_the_same_flat_commit()
    {
        if (NoBash()) return;
        var block = TokenFreeBlock();
        Assert.DoesNotContain("<HF_TOKEN>", block, StringComparison.Ordinal);
        Assert.Contains("IFS= read -rs", block, StringComparison.Ordinal);
        var repo = DeployManifestGateTests.Scratch(extraReference: null);
        var space = BareSpace(repo);
        try
        {
            var (_, sha, _) = DeployManifestGateTests.Run("git", repo.FullName, "rev-parse", "HEAD");
            var run = RunShimmed(block, repo, space);
            Assert.True(run.Pushed.Code == 0, $"رَمزُ {run.Pushed.Code}: {run.Pushed.Err}");
            output.WriteLine($"نِداءاتُ git: {run.Argv.Length} · دَفعات: {run.PushHeaders.Length} · إيداعات: {run.Pushed.Commits} · مِلَفّات: {run.Pushed.Files.Length}");
            Assert.True(run.Argv.Length >= 4, $"أَداةٌ عَمياء: {run.Argv.Length} نِداءً لِـgit.");
            Assert.False(TokenInArgv(run.Argv), "الرَمزُ في argv نِداءٍ لِـgit: " + string.Join(" | ", run.Argv));
            var header = Assert.Single(run.PushHeaders);
            Assert.Equal("Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("tester:" + FakeToken)), header);
            Assert.Contains(run.Argv, l => l.StartsWith("push --force https://huggingface.co/spaces/<owner>/<space>.git ", StringComparison.Ordinal));
            Assert.Equal(1, run.Pushed.Commits);
            Assert.Equal($"deploy: {sha}", run.Pushed.Message);
            Assert.Equal(sha, run.Pushed.Revision);
            Assert.DoesNotContain(run.Pushed.Files, f => Forbidden.Any(p => f.StartsWith(p, StringComparison.Ordinal)));
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

    /// <summary><b>والكُتلَةُ الثانِيَةُ بِحَرفِها — العَيِّنَةُ المَحقونَة</b>: تَدفَعُ ما تَدفَعُه الثالِثَة، لكِنّ الرَمزَ في argv الدَفع — فَحُكمُ الصَفِّ أَعلاه يَحمَرُّ
    /// عَلَيها، فَهُوَ لَيسَ أَعمى. وهي باقِيَةٌ في الدَليلِ تَحتَ سَطرِ ⚠ مُؤَرَّخ.</summary>
    [Fact]
    public void The_token_check_goes_red_on_the_block_that_puts_the_token_in_the_push_url()
    {
        if (NoBash()) return;
        var deploy = Deploy();
        var at = deploy.IndexOf(NewBlockStart, StringComparison.Ordinal);
        Assert.Contains("**الرَمزُ في عُنوانِ الدَفعِ أَدناه يَبقى في سَطرِ الأَمر**", deploy[(at - 500)..at], StringComparison.Ordinal);
        var repo = DeployManifestGateTests.Scratch(extraReference: null);
        var space = BareSpace(repo);
        try
        {
            var run = RunShimmed(NewBlock(), repo, space);
            Assert.True(run.Pushed.Code == 0, $"رَمزُ {run.Pushed.Code}: {run.Pushed.Err}");
            Assert.Equal(1, run.Pushed.Commits);
            Assert.True(TokenInArgv(run.Argv), "العَيِّنَةُ لا تُحمِرُّ المُتَحَقِّق: " + string.Join(" | ", run.Argv));
            Assert.Contains(run.Argv, l => l.StartsWith("push --force https://tester:" + FakeToken + "@huggingface.co/", StringComparison.Ordinal));
        }
        finally { Clean(repo, space); }
    }
}
