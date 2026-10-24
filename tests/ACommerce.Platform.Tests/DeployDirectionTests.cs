using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ النَشرُ لا يَدفَعُ ما لَيسَ بَعدَ ما يَخدِم — `deploy_direction` مُشَغَّلَةً عَلى مُستَودَعٍ مَصنوع (‏2026-10-24، تَيّارُ S16) ═══
//
// **المَقيسُ قَبل** (`1c72873`): `deploy-hf.yml` يَستَنسِخُ `workflow_run.head_sha` (أَو رَقمَ `workflow_dispatch`) ويَدفَعُ شَجَرَتَه بِـ`--force`،
// وصِفرُ `merge-base` و`is-ancestor` و`ls-remote` في أَسطُرِه غَيرِ المُعَلَّقَة (`ProductionBootVerdictTests.GuardTokens`) — فَإعادَةُ تَشغيلٍ
// خَضراءُ لِكوميتٍ أَقدَمَ بَعدَ نَشرِ أَحدَثَ مِنه تَراجُعٌ صامِتٌ فَوقَ الإنتاج، وعِلاجُه في DEPLOY §٢·و فَحصٌ بِاليَد. والحُكمُ صارَ دالَّةً
// بَينَ عَلامَتَينِ في خُطوَةٍ قَبلَ الدَفع — **تُقطَعُ هُنا كَما هِيَ وتُشَغَّلُ بِـbash** في مُستَودَعِ git مَصنوعٍ بِسَلَفٍ وخَلَفٍ وفَرعٍ يَتيم
// (نَسَقُ `ProductionBootVerdictTests` و`ProviderVaultDeploymentTests`). **وعَلى Windows لا bash مَعلومَ المَوضِع** فَيُطبَعُ ذلكَ ويُرجَع.
//
// ‏(‏2026-10-24 — تَيّارُ S16، الجَولَةُ ١ لِلمُراجَعَة) **وقُلِبَ «غَيرُ المَعروفِ يُدفَع»**: في `4e58708` كانَت كُلُّ حالَةِ خَطِّ أَساسٍ غَيرِ `measured`
// — و`measured` بِلا رَقم — تَطبَعُ «push» وتَخرُجُ بِصِفر؛ مَقيساً عَلى الدالَّةِ مَقطوعَةً: ‏4 حالاتٍ مِن 4 بِلا النَقرَة «push» بِرَمز 0، وبَعدَ القَلبِ ‏4 مِن 4
// «refuse» بِرَمز 1 و«push» بِـ`::warning::` مَعَ `allow_rollback`. فَقُلِبَت صُفوفُها بِأَسمائِها هُنا، ومَعجَمُ الحالاتِ يُقرَأُ مِن خُطوَتِه في الصَفِّ ٤.

public class DeployDirectionTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string Start = "# ─── بِدايَةُ deploy_direction ───";
    private const string End = "# ─── نِهايَةُ deploy_direction ───";

    private static string Workflow()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy-hf.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(text.Length > 10_000, "أَداةٌ عَمياء: deploy-hf.yml لَم يُقرَأ.");
        return text;
    }

    /// <summary>الدالَّةُ بَينَ عَلامَتَيها بِحَرفِها — عَلامَةٌ واحِدَةٌ لِكُلِّ طَرَف.</summary>
    private static string DirectionFunction()
    {
        var text = Workflow();
        var a = text.IndexOf(Start, StringComparison.Ordinal);
        Assert.True(a > 0, $"عَلامَةٌ غائِبَة: {Start}");
        Assert.Equal(a, text.LastIndexOf(Start, StringComparison.Ordinal));
        var b = text.IndexOf(End, a, StringComparison.Ordinal);
        Assert.True(b > a, $"عَلامَةُ النِهايَةِ غائِبَة: {End}");
        return text[a..(b + End.Length)];
    }

    private bool NoBash()
    {
        if (!OperatingSystem.IsWindows()) return false;
        output.WriteLine("Windows — لا bash مَعلومَ المَوضِع؛ يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
        return true;
    }

    private static (int Code, string Out) Run(string exe, string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        // ‏مُستَودَعٌ مَصنوعٌ لا يَرِثُ GIT_DIR ولا GIT_WORK_TREE مِن بيئَةِ المُشَغِّل — وإلّا سَأَلَ git مُستَودَعاً آخَر.
        psi.Environment.Remove("GIT_DIR");
        psi.Environment.Remove("GIT_WORK_TREE");
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["GIT_AUTHOR_NAME"] = psi.Environment["GIT_COMMITTER_NAME"] = "t";
        psi.Environment["GIT_AUTHOR_EMAIL"] = psi.Environment["GIT_COMMITTER_EMAIL"] = "t@example.invalid";
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(20_000), $"{exe} لَم يَنتَهِ في عِشرينَ ثانِيَة.");
        return (p.ExitCode, text.Trim());
    }

    /// <summary>مُستَودَعٌ مَصنوع: <c>A ← B ← C</c> عَلى <c>main</c>، و<c>X</c> إيداعٌ يَتيمٌ بِلا نَسَبٍ إلَيها.</summary>
    private sealed record History(DirectoryInfo Dir, string A, string B, string C, string X);

    private static History Scratch()
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-deploy-direction-");
        string Git(params string[] a)
        {
            var (code, text) = Run("git", dir.FullName, a);
            Assert.True(code == 0, $"git {string.Join(' ', a)} خَرَجَ {code}: {text}");
            return text;
        }
        Git("init", "-q", "-b", "main");
        string Commit(string name)
        {
            File.WriteAllText(Path.Combine(dir.FullName, name + ".txt"), name);
            Git("add", name + ".txt");
            Git("commit", "-q", "-m", name);
            return Git("rev-parse", "HEAD");
        }
        var a = Commit("a");
        var b = Commit("b");
        var c = Commit("c");
        Git("checkout", "-q", "--orphan", "other");
        Git("rm", "-q", "-rf", ".");
        var x = Commit("x");
        Git("checkout", "-q", "main");
        return new History(dir, a, b, c, x);
    }

    /// <summary>يُشَغِّلُ <c>deploy_direction</c> في المُستَودَعِ المَصنوعِ ويُرجِعُ رَمزَها وحُكمَها وسَبَبَها.</summary>
    private (int Code, string Decision, string Reason) Direction(History h, string candidate, string live, string state, string allow)
    {
        var script = Path.Combine(h.Dir.FullName, "..", Path.GetFileName(h.Dir.FullName) + ".sh");
        File.WriteAllText(script, "set -uo pipefail\n" + DirectionFunction() + "\ndeploy_direction \"$1\" \"$2\" \"$3\" \"$4\"\n", new UTF8Encoding(false));
        try
        {
            var (code, text) = Run("/bin/bash", h.Dir.FullName, script, candidate, live, state, allow);
            var lines = text.Split('\n');
            var decision = lines.FirstOrDefault(l => l.StartsWith("decision=", StringComparison.Ordinal))?["decision=".Length..] ?? "";
            var reason = lines.Length > 1 ? lines[1] : "";
            output.WriteLine($"[{state} · allow={allow}] → {code} · {decision} · {reason}");
            return (code, decision, reason);
        }
        finally
        {
            File.Delete(script);
        }
    }

    // ─── ١. الحالاتُ الخَمسُ المُسَمّاة — كُلٌّ بِحُكمِه ورَمزِه ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_five_named_cases_decide_push_skip_or_refuse_on_a_real_history()
    {
        if (NoBash()) return;
        var h = Scratch();
        try
        {
            var cases = new (string Name, string Candidate, string Live, string State, string Allow, int Code, string Decision, string Says)[]
            {
                ("المُرَشَّحُ هُوَ ما يَخدِم",            h.C, h.C, "measured", "false", 0, "skip",   "نَفسَه"),
                ("ما يَخدِمُ سَلَفٌ لِلمُرَشَّح",          h.C, h.A, "measured", "false", 0, "push",   "تَقَدُّم"),
                ("المُرَشَّحُ سَلَفٌ لِما يَخدِم",         h.A, h.C, "measured", "false", 1, "refuse", "تَراجُع"),
                ("لا نَسَبَ بَينَهُما",                   h.C, h.X, "measured", "false", 1, "refuse", "لا نَسَبَ"),
                ("ما يَخدِمُ غَيرُ مَعروف — فَشِلَ القِياسُ فَيُرفَض", h.C, "",  "failed",   "false", 1, "refuse", "لَم يَقِس"),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                var (code, decision, reason) = Direction(h, c.Candidate, c.Live, c.State, c.Allow);
                if (code != c.Code || decision != c.Decision || !reason.Contains(c.Says, StringComparison.Ordinal))
                    wrong.Add($"«{c.Name}»: {code}/{decision} «{reason}» لا {c.Code}/{c.Decision} «…{c.Says}…»");
            }
            output.WriteLine($"فُحِصَت {cases.Length} حالات عَلى تاريخٍ مَصنوع (A←B←C، ويَتيمٌ X)؛ أَخطَأَ الحُكمُ في {wrong.Count}");
            Assert.True(cases.Length == 5, "أَداةٌ عَمياء: الحالاتُ أَقَلُّ مِمّا كُتِب.");
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }
        finally
        {
            h.Dir.Delete(recursive: true);
        }
    }

    // ─── ٢. وما حَولَها — السَماحُ المَقصود، وما لَيسَ في التاريخ، وكُلُّ حالَةٍ «لَم يُقَس» ─────────────────────────────────

    /// <summary><b>التَراجُعُ المَقصودُ يُدفَعُ بِتَحذيرٍ يُسَمّيه</b>، و<b>ما يَخدِمُه الحَيُّ ولَيسَ في تاريخِنا يُرفَضُ</b> (لا يُستَنتَجُ أَنّه أَقدَم)،
    /// والسَماحُ لا يُغَيِّرُ «لا دَفعَ» ولا «تَقَدُّم»، وكُلُّ حالَةِ خَطِّ أَساسٍ غَيرِ <c>measured</c> — ومَعَها <c>measured</c> بِلا رَقم — تُدفَعُ
    /// كَما قَبلَ الحارِس. وسِلسِلَةٌ لَيسَت <c>true</c> بِحَرفِها لا تَسمَح.
    /// <b>(‏2026-10-24 — تَيّارُ S16، الجَولَةُ ١ لِلمُراجَعَة)</b> وقُلِبَ «تُدفَعُ كَما قَبلَ الحارِس»: ما لَم يُقَس <b>يُرفَضُ</b> بِلا السَماح، ويُدفَعُ بِتَحذيرٍ
    /// يُسَمّيه مَعَه — صُفوفُه الأَربَعَةُ قُلِبَت بِأَسمائِها، وأُضيفَ ثَلاثٌ بِالسَماحِ وواحِدٌ بِسَماحٍ لَيسَ <c>true</c> بِحَرفِه.</summary>
    [Fact]
    public void A_deliberate_rollback_pushes_with_a_named_warning_and_a_live_commit_outside_the_history_is_refused()
    {
        if (NoBash()) return;
        var h = Scratch();
        try
        {
            const string Missing = "0123456789abcdef0123456789abcdef01234567";
            var cases = new (string Name, string Candidate, string Live, string State, string Allow, int Code, string Decision, string Says)[]
            {
                ("أَقدَمُ بِسَماح",                         h.A, h.C, "measured", "true",  0, "push",   "::warning::تَراجُعٌ مَقصود"),
                ("بِلا نَسَبٍ بِسَماح",                      h.C, h.X, "measured", "true",  0, "push",   "::warning::تَراجُعٌ مَقصود"),
                ("أَقدَمُ بِسَماحٍ لَيسَ true بِحَرفِه",      h.A, h.C, "measured", "True",  1, "refuse", "::error::"),
                ("أَقدَمُ وسَماحٌ فارِغ",                    h.B, h.C, "measured", "",      1, "refuse", "allow_rollback=true"),
                ("ما يَخدِمُ لَيسَ في التاريخ",              h.C, Missing, "measured", "false", 1, "refuse", "لَيسَ في تاريخِ"),
                ("ما يَخدِمُ لَيسَ في التاريخ، بِسَماح",     h.C, Missing, "measured", "true",  0, "push",   "لَيسَ في تاريخِ"),
                ("المُرَشَّحُ هُوَ ما يَخدِم، بِسَماح",       h.B, h.B, "measured", "true",  0, "skip",   "نَفسَه"),
                ("تَقَدُّمٌ بِسَماح",                        h.C, h.B, "measured", "true",  0, "push",   "تَقَدُّم"),
                ("أَجابَ ولا نُقطَة (أَوَّلُ نَشر) فَيُرفَض",   h.A, "",  "absent",   "false", 1, "refuse", "لَم يَقِس"),
                ("لا مُضيفَ فَيُرفَض",                       h.A, "",  "nohost",   "false", 1, "refuse", "لَم يَقِس"),
                ("قيسَ ولا رَقمَ فَيُرفَض",                  h.A, "",  "measured", "false", 1, "refuse", "لَم يَقِس"),
                ("رَقمٌ وحالٌ غَيرُ measured فَيُرفَض",       h.A, h.C, "failed",   "false", 1, "refuse", "لَم يَقِس"),
                ("فَشِلَ القِياسُ، بِسَماح",                   h.C, "",  "failed",   "true",  0, "push",   "::warning::تَراجُعٌ مَقصود (allow_rollback=true): الحارِسُ لَم يَقِس"),
                ("أَوَّلُ نَشرٍ، بِسَماح",                     h.A, "",  "absent",   "true",  0, "push",   "::warning::تَراجُعٌ مَقصود (allow_rollback=true): الحارِسُ لَم يَقِس"),
                ("لا مُضيفَ، بِسَماح",                        h.A, "",  "nohost",   "true",  0, "push",   "::warning::تَراجُعٌ مَقصود (allow_rollback=true): الحارِسُ لَم يَقِس"),
                ("فَشِلَ القِياسُ بِسَماحٍ لَيسَ true بِحَرفِه", h.C, "",  "failed",   "True",  1, "refuse", "::error::لا يُدفَعُ ما لَيسَ بَعدَ ما يَخدِم: الحارِسُ لَم يَقِس"),
            };
            var wrong = new List<string>();
            foreach (var c in cases)
            {
                var (code, decision, reason) = Direction(h, c.Candidate, c.Live, c.State, c.Allow);
                if (code != c.Code || decision != c.Decision || !reason.Contains(c.Says, StringComparison.Ordinal))
                    wrong.Add($"«{c.Name}»: {code}/{decision} «{reason}» لا {c.Code}/{c.Decision} «…{c.Says}…»");
            }
            output.WriteLine($"فُحِصَت {cases.Length} حالَةً؛ أَخطَأَ الحُكمُ في {wrong.Count}");
            Assert.True(cases.Length >= 16, "أَداةٌ عَمياء: الحالاتُ أَقَلُّ مِمّا كُتِب.");
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }
        finally
        {
            h.Dir.Delete(recursive: true);
        }
    }

    // ─── ٣. الوَظيفَةُ تُناديها حَيثُ يَلزَم — قَبلَ الدَفع، وما بَعدَها لا يَجري إلّا عَلى «push» ───────────────────────────────

    /// <summary><b>الخُطوَةُ بَعدَ خَطِّ الأَساسِ وقَبلَ الدَفع، والخُطُواتُ الثَلاثُ بَعدَها مَشروطَةٌ بِحُكمِها، والسَماحُ لا يَأتي إلّا مِن
    /// <c>workflow_dispatch</c></b> — مُدخَلٌ بُوليانِيٌّ افتِراضُه <c>false</c>، و<c>workflow_run</c> لا مُدخَلَ لَه. وتُقرَأُ الأَسطُرُ غَيرُ المُعَلَّقَة.</summary>
    [Fact]
    public void The_workflow_decides_after_the_baseline_and_before_the_push_and_only_a_dispatch_input_allows_a_rollback()
    {
        var wf = Workflow();
        var steps = Regex.Matches(wf, @"^      - name: (?<n>.+)$", RegexOptions.Multiline).Select(m => (Name: m.Groups["n"].Value.Trim(), m.Index)).ToList();
        output.WriteLine($"خُطُواتُ mirror: {steps.Count} — {string.Join(" · ", steps.Select(s => s.Name))}");
        Assert.True(steps.Count >= 8, $"أَداةٌ عَمياء: {steps.Count} خُطوَة.");
        int At(string name)
        {
            var i = steps.FindIndex(s => s.Name == name);
            Assert.True(i >= 0, $"الخُطوَةُ «{name}» غائِبَة.");
            return i;
        }
        string Body(int i) => wf[steps[i].Index..(i + 1 < steps.Count ? steps[i + 1].Index : wf.Length)];

        var baseline = At("Measure the live baseline before pushing");
        var decide = At("Refuse a push that is not forward of what the live serves");
        var push = At("Push the filtered tree to the Space");
        Assert.Equal(baseline + 1, decide);
        Assert.Equal(decide + 1, push);

        var body = Body(decide);
        Assert.Contains("ALLOW_ROLLBACK: ${{ github.event_name == 'workflow_dispatch' && inputs.allow_rollback == true }}", body, StringComparison.Ordinal);
        Assert.Contains("deploy_direction \"${candidate}\" \"${base_commit:-}\" \"${base_state:-failed}\" \"${ALLOW_ROLLBACK:-false}\"", body, StringComparison.Ordinal);
        Assert.Contains("echo \"deploy_decision=${decision}\" >> \"$GITHUB_ENV\"", body, StringComparison.Ordinal);
        Assert.Contains("[ \"${decision}\" != \"refuse\" ] || exit 1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("if:", body.Split("\n        run: |")[0], StringComparison.Ordinal);   // ‏الخُطوَةُ نَفسُها لا تُتَخَطّى

        var guarded = steps.Skip(push).Select((s, k) => (s.Name, Body(push + k))).ToList();
        foreach (var (name, b) in guarded) output.WriteLine($"  بَعدَ الحُكم: «{name}» — {(b.Contains("\n        if: env.deploy_decision == 'push'\n", StringComparison.Ordinal) ? "مَشروطَة" : "غَيرُ مَشروطَة")}");
        Assert.Equal(3, guarded.Count);
        Assert.All(guarded, g => Assert.Contains("\n        if: env.deploy_decision == 'push'\n", g.Item2, StringComparison.Ordinal));

        var input = Regex.Match(wf, @"^      allow_rollback:\n(?<b>(?:        .*\n)+)", RegexOptions.Multiline);
        Assert.True(input.Success, "المُدخَلُ allow_rollback غائِبٌ عَن workflow_dispatch.");
        Assert.Contains("        type: boolean\n", input.Groups["b"].Value, StringComparison.Ordinal);
        Assert.Contains("        default: false\n", input.Groups["b"].Value, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(wf, @"inputs\.allow_rollback").Count);
    }

    /// <summary><b>حَقنُ عَيب — الصُفوفُ أَعلاه تَحمَرُّ عَلى دالَّةٍ تُخطِئ</b>: تُشَغَّلُ الدالَّةُ نَفسُها وقَد حُقِنَ فيها عَيبٌ واحِد — «سَلَفٌ لِلمُرَشَّحِ»
    /// يُقلَبُ فَيُدفَعُ الأَقدَم، والمُساواةُ تُدفَع، وما لَيسَ في التاريخِ لا يُسَمّى، وما يَخدِمُ غَيرُ مَعروفٍ يُرفَض، والسَماحُ بِأَيِّ قيمَة — ويُشتَرَطُ
    /// أَن يَختَلِفَ حُكمُ حالَةٍ واحِدَةٍ في الأَقَلِّ أَو سَبَبُها عَن الحَيّ. (مَقيساً: حَذفُ فَرعِ «لَيسَ في التاريخ» لا يُغَيِّرُ الحُكم — <c>merge-base</c>
    /// يَخرُجُ 128 فَيُرفَضُ بِـ«تَعَذَّرَ السُؤال» — ويُغَيِّرُ السَبَب، فَالمُقارَنَةُ بِالسَطرَينِ لا بِالحُكمِ وَحدَه.)
    /// <b>(‏2026-10-24 — تَيّارُ S16، الجَولَةُ ١ لِلمُراجَعَة)</b> وقُلِبَ «غَيرُ المَعروفِ يُرفَض» بِاسمِه إلى «غَيرُ المَعروفِ يُدفَع»: الرَفضُ صارَ الحَيّ،
    /// والعَيبُ المَحقونُ هُوَ فَرعُ <c>4e58708</c> بِحَرفِه — فَالحالاتُ تَرى العَيبَ الَّذي كانَ قائِماً، لا عَيباً مُتَخَيَّلاً.</summary>
    [Fact]
    public void The_direction_rows_can_actually_go_red_on_an_injected_fault()
    {
        if (NoBash()) return;
        var live = DirectionFunction();
        var faults = new (string Name, string From, string To)[]
        {
            ("السَلَفُ مَقلوب", "git merge-base --is-ancestor \"${live}\" \"${candidate}\"; rc=$?", "git merge-base --is-ancestor \"${candidate}\" \"${live}\"; rc=$?"),
            ("المُساواةُ تُدفَع", "echo \"decision=skip\"", "echo \"decision=push\""),
            ("ما لَيسَ في التاريخِ لا يُسَمّى", "if ! git cat-file -e \"${live}^{commit}\" 2>/dev/null; then", "if false; then"),
            ("غَيرُ المَعروفِ يُدفَع (فَرعُ 4e58708 بِحَرفِه)",
                "              rel=\"الحارِسُ لَم يَقِس ما يَخدِمُه الحَيّ",
                "              echo \"decision=push\"\n              echo \"الحارِسُ لَم يَقِس: ما يَخدِمُه الحَيُّ غَيرُ مَعروف (خَطُّ الأَساس [${state}]، commit=${live:-<لا شَيء>}) — يُدفَعُ كَما قَبلَ الحارِس.\"\n              return 0\n              rel=\"الحارِسُ لَم يَقِس ما يَخدِمُه الحَيّ"),
            ("السَماحُ بِأَيِّ قيمَة", "if [ \"${allow}\" = \"true\" ]; then", "if [ -n \"${allow}\" ]; then"),
        };
        var h = Scratch();
        try
        {
            const string Missing = "0123456789abcdef0123456789abcdef01234567";
            var probes = new (string Candidate, string Live, string State, string Allow)[]
            {
                (h.C, h.C, "measured", "false"), (h.C, h.A, "measured", "false"), (h.A, h.C, "measured", "false"),
                (h.C, h.X, "measured", "false"), (h.C, "", "failed", "false"), (h.C, Missing, "measured", "false"),
                (h.A, h.C, "measured", "True"),
            };
            string Verdicts(string fn) => string.Join(" | ", probes.Select(p =>
            {
                var script = Path.Combine(h.Dir.FullName, "..", Path.GetFileName(h.Dir.FullName) + "-f.sh");
                File.WriteAllText(script, "set -uo pipefail\n" + fn + "\ndeploy_direction \"$1\" \"$2\" \"$3\" \"$4\"\n", new UTF8Encoding(false));
                try
                {
                    var (code, text) = Run("/bin/bash", h.Dir.FullName, script, p.Candidate, p.Live, p.State, p.Allow);
                    // ‏الحُكمُ وسَبَبُه مَعاً: عَيبٌ يُبقي الحُكمَ ويُسقِطُ اسمَ السَبَبِ عَيبٌ — «يُرفَضُ بِاسمِه» جُزءٌ مِن العَقد.
                    return $"{code}:{string.Join(" / ", text.Split('\n').Take(2))}";
                }
                finally { File.Delete(script); }
            }));
            var baseline = Verdicts(live);
            output.WriteLine($"الحَيّ: {baseline}");
            var blind = new List<string>();
            foreach (var f in faults)
            {
                Assert.Single(Regex.Matches(live, Regex.Escape(f.From)));
                var v = Verdicts(live.Replace(f.From, f.To, StringComparison.Ordinal));
                output.WriteLine($"«{f.Name}»: {v}");
                if (v == baseline) blind.Add(f.Name);
            }
            output.WriteLine($"حُقِنَ {faults.Length} عَيباً عَلى {probes.Length} حالات؛ لَم يُرَ مِنها {blind.Count}");
            Assert.True(blind.Count == 0, "عَيبٌ لا تَراه الحالات: " + string.Join("، ", blind));
        }
        finally
        {
            h.Dir.Delete(recursive: true);
        }
    }

    // ─── ٤. (‏2026-10-24 — تَيّارُ S16، الجَولَةُ ١ لِلمُراجَعَة) ما يَكتُبُه خَطُّ الأَساسِ غَيرَ «measured» لا يُدفَعُ بِلا النَقرَة ─────────

    /// <summary><b>مَعجَمُ حالاتِ خَطِّ الأَساسِ يُقرَأُ مِن خُطوَتِه لا مِن قائِمَةٍ هُنا</b>: كُلُّ <c>base_state="…"</c> في «Measure the live baseline before
    /// pushing» — واليَومَ أَربَع: <c>measured</c> و<c>absent</c> و<c>failed</c> و<c>nohost</c> — تُشَغَّلُ بِها الدالَّةُ ورَقمُ الحَيِّ فارِغ (وهو ما تَكتُبُه
    /// الخُطوَةُ ما لَم تَرَ <c>startedAt</c> صالِحاً): بِلا <c>allow_rollback</c> <b>تُرفَضُ</b> بِـ«الحارِسُ لَم يَقِس»، ومَعَه تُدفَعُ بِـ<c>::warning::</c> يُسَمّيه،
    /// وبِسَماحٍ لَيسَ <c>true</c> بِحَرفِه تُرفَض. فَحالَةٌ خامِسَةٌ تُضافُ إلى الخُطوَةِ تُقاسُ هُنا قَبلَ أَن تَبلُغَ دَفعاً. وكانَ هذا كُلُّه «push» بِرَمز 0 في
    /// <c>4e58708</c> — الثَغرَةُ الَّتي كُتِبَ الحارِسُ لَها: <c>/health</c> يَرُدُّ 5xx وN+1 يُبنى، فَيُدفَعُ N فَوقَه أَخضَر.</summary>
    [Fact]
    public void Every_baseline_state_the_measuring_step_writes_refuses_an_unmeasured_push_without_the_tick()
    {
        if (NoBash()) return;
        var wf = Workflow();
        const string Name = "      - name: Measure the live baseline before pushing\n";
        var a = wf.IndexOf(Name, StringComparison.Ordinal);
        Assert.True(a > 0, "خُطوَةُ خَطِّ الأَساسِ غائِبَة.");
        var b = wf.IndexOf("\n      - name: ", a + Name.Length, StringComparison.Ordinal);
        Assert.True(b > a, "لا خُطوَةَ بَعدَ خَطِّ الأَساس.");
        var states = Regex.Matches(wf[a..b], @"base_state=""(?<s>[a-z]+)""").Select(m => m.Groups["s"].Value)
            .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        output.WriteLine($"حالاتُ خَطِّ الأَساسِ في خُطوَتِه: {states.Count} — {string.Join(" · ", states)}");
        Assert.True(states.Count >= 4, $"أَداةٌ عَمياء: {states.Count} حالَة.");
        Assert.Contains("measured", states);

        var h = Scratch();
        try
        {
            var wrong = new List<string>();
            var ran = 0;
            foreach (var state in states)
            {
                foreach (var (allow, code, decision, says) in new[]
                         {
                             ("false", 1, "refuse", "::error::لا يُدفَعُ ما لَيسَ بَعدَ ما يَخدِم: الحارِسُ لَم يَقِس"),
                             ("true",  0, "push",   "::warning::تَراجُعٌ مَقصود (allow_rollback=true): الحارِسُ لَم يَقِس"),
                             ("True",  1, "refuse", "::error::لا يُدفَعُ ما لَيسَ بَعدَ ما يَخدِم: الحارِسُ لَم يَقِس"),
                         })
                {
                    ran++;
                    var (c, d, r) = Direction(h, h.C, "", state, allow);
                    if (c != code || d != decision || !r.Contains(says, StringComparison.Ordinal) || !r.Contains($"[{state}]", StringComparison.Ordinal))
                        wrong.Add($"«{state} · allow={allow}»: {c}/{d} «{r}» لا {code}/{decision} «…{says}…[{state}]…»");
                }
            }
            output.WriteLine($"شُغِّلَت {ran} حالَةً ({states.Count} × 3 سَماحات) بِرَقمِ حَيٍّ فارِغ؛ أَخطَأَ الحُكمُ في {wrong.Count}");
            Assert.True(ran == states.Count * 3 && ran >= 12, "أَداةٌ عَمياء: الحالاتُ أَقَلُّ مِمّا كُتِب.");
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }
        finally
        {
            h.Dir.Delete(recursive: true);
        }
    }
}
