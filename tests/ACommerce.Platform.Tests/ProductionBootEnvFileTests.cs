using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — مِلَفُّ البَوّابَةِ `scripts/.prod-gate.env` يَغلِبُ البيئَة، فَالخُطوَةُ ١ مِن §٤‑د لا تَضمَنُ أَنّ البَوّابَةَ تُقلِعُ عَلى الفَرعِ المُرَحَّل (تَدقيقُ الإطلاق lens-docs-vs-code-2) ═══
//
// **المَقيس** (‏2026-10-25، عِندَ `199e879`):
//   ١. الخُطوَةُ ١ مِن `docs/DEPLOY.md` §٤‑د تُصَدِّرُ `ConnectionStrings__Postgres` لِلفَرعِ المَنسوخِ ثُمَّ تُشَغِّلُ `verify-production-boot.sh`؛
//      والبَوّابَةُ في «١) التَهيئَة» تُصدِرُ المِلَفَّ بِـ`set -a; source` **فَوقَ** البيئَة: مِلَفٌّ قَديمٌ يُسَمّي فَرعاً آخَرَ يُقلِعُ عَلَيه وتَخضَرّ —
//      وإعادَةُ الشَكّاك: «gate sees: Host=from-file-conn exit=0».
//   ٢. وبِلا مِلَفٍّ لا تَحمِلُ الخُطوَةُ `ACOMMERCE_AUTH_SECRET` — والبَوّابَةُ تَشتَرِطُه — فَتَخرُجُ ‏3 (الصَفُّ الدائِمُ أَدناه).
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفُّ `Today_…` يَنقَلِبُ بِاسمِه، والدائِمانِ يَبقَيانِ حَرفاً.
//
// **العِلاج** (‏2026-10-25): الكُتلَةُ `env_file` — تُحفَظُ قيمَةُ البيئَةِ غَيرُ الفارِغَةِ لِكُلِّ اسمٍ يُسنِدُه المِلَفّ، ويُصدَرُ المِلَفّ، ثُمَّ تُعادُ؛
// ويُقالُ مَصدَرُ المُتَغَيِّرَينِ الإلزامِيَّينِ بِاسمِهِما. انقَلَبَ الصَفُّ إلى `The_environment_wins_over_the_env_file_…`، والكُتلَةُ القَديمَةُ
// بِحَرفِها عَيِّنَةٌ مَحقونَةٌ يَحمَرُّ عَلَيها المُتَحَقِّق (القاعِدَة ١٠)، والدائِمانِ كَما أُودِعا.
public class ProductionBootEnvFileTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string BlockStart = "# ═══ ١) التَهيئَة — تُقرَأ، وما يَنقُصُ يُقالُ بِاسمِه";
    private const string BlockEnd = "MISSING_VARS=\"\"\n";

    private static string Gate()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "verify-production-boot.sh"));
        Assert.True(text.Length > 10_000, "أَداةٌ عَمياء: verify-production-boot.sh لَم يُقرَأ.");
        return text;
    }

    /// <summary>كُتلَةُ «١) التَهيئَة» بِحَرفِها — مِن عُنوانِها إلى أَوَّلِ فَحصٍ لِلغائِب.</summary>
    internal static string ConfigBlock()
    {
        var gate = Gate();
        var a = gate.IndexOf(BlockStart, StringComparison.Ordinal);
        Assert.True(a > 0, "عُنوانُ «١) التَهيئَة» غائِبٌ عَن البَوّابَة.");
        Assert.Equal(a, gate.LastIndexOf(BlockStart, StringComparison.Ordinal));
        var b = gate.IndexOf(BlockEnd, a, StringComparison.Ordinal);
        Assert.True(b > a, "‏`MISSING_VARS=\"\"` لا يَلي «١) التَهيئَة».");
        return gate[a..b];
    }

    /// <summary>يُشَغِّلُ الكُتلَةَ بِـbash بِبيئَةٍ مَعلومَة ومِلَفٍّ اختِيارِيّ، ويُرجِعُ ما تَراه البَوّابَةُ بَعدَها.</summary>
    private (string Conn, string Secret, string Text) Run(string block, IDictionary<string, string> env, string? file)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-gate-env-");
        try
        {
            var envFile = Path.Combine(dir.FullName, ".prod-gate.env");
            if (file is not null) File.WriteAllText(envFile, file, new UTF8Encoding(false));
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script,
                "set -uo pipefail\nsay() { echo \"  $*\"; }\nENV_FILE=\"$1\"\n" + block +
                "\nprintf 'conn=%s\\nsecret=%s\\n' \"${ConnectionStrings__Postgres:-}\" \"${ACOMMERCE_AUTH_SECRET:-}\"\n",
                new UTF8Encoding(false));
            var psi = new ProcessStartInfo("/bin/bash", new[] { script, envFile })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Environment.Remove("ConnectionStrings__Postgres");
            psi.Environment.Remove("ACOMMERCE_AUTH_SECRET");
            foreach (var (k, v) in env) psi.Environment[k] = v;
            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000), "الكُتلَةُ لَم تَنتَهِ في عَشرِ ثَوانٍ.");
            Assert.Equal(0, p.ExitCode);
            output.WriteLine(text.Trim());
            string Value(string key) => text.Split('\n').Single(l => l.StartsWith(key + "=", StringComparison.Ordinal))[(key.Length + 1)..];
            return (Value("conn"), Value("secret"), text);
        }
        finally { dir.Delete(recursive: true); }
    }

    private const string FileBody =
        "ConnectionStrings__Postgres=\"Host=from-file-branch;Database=x\"\n" +
        "ACOMMERCE_AUTH_SECRET=\"file-secret-00000000000000000000000000000000\"\n";

    /// <summary>بِلا مِلَفٍّ تُقرَأُ البيئَةُ كَما هي. يَبقى بِحَرفِه.</summary>
    [Fact]
    public void Without_a_file_the_gate_reads_the_environment_as_it_is()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (conn, secret, _) = Run(ConfigBlock(),
            new Dictionary<string, string> { ["ConnectionStrings__Postgres"] = "Host=from-env-branch;Database=x" }, file: null);
        Assert.Equal("Host=from-env-branch;Database=x", conn);
        Assert.Equal("", secret);
    }

    /// <summary>والمِلَفُّ يَملَأُ ما لَم تَضبِطه البيئَة — الخُطوَةُ ١ بِلا سِرٍّ تَأخُذُه مِنه. يَبقى بِحَرفِه.</summary>
    [Fact]
    public void A_file_fills_a_variable_the_environment_left_unset()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (_, secret, _) = Run(ConfigBlock(),
            new Dictionary<string, string> { ["ConnectionStrings__Postgres"] = "Host=from-env-branch;Database=x" }, FileBody);
        Assert.Equal("file-secret-00000000000000000000000000000000", secret);
    }

    // ─── انقَلَبَ بِاسمِه (كانَ `Today_the_env_file_overrides_an_exported_connection_string`) ───

    /// <summary>سِلسِلَةٌ صُدِّرَت لِلفَرعِ المُرَحَّلِ تَبقى ولَو سَمّى المِلَفُّ فَرعاً آخَر — والسِرُّ الغائِبُ عَن البيئَةِ مِن المِلَفّ، والبَوّابَةُ
    /// تَقولُ مَصدَرَ كُلٍّ بِاسمِه ولا تَطبَعُ قيمَة.</summary>
    [Fact]
    public void The_environment_wins_over_the_env_file_and_the_gate_says_where_each_came_from()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (conn, secret, text) = Run(ConfigBlock(),
            new Dictionary<string, string> { ["ConnectionStrings__Postgres"] = "Host=from-env-branch;Database=x" }, FileBody);
        Assert.Equal("Host=from-env-branch;Database=x", conn);
        Assert.Equal("file-secret-00000000000000000000000000000000", secret);
        Assert.Contains("ConnectionStrings__Postgres: مِن البيئَة (المِلَفُّ لا يَغلِبُها)", text, StringComparison.Ordinal);
        Assert.Contains("ACOMMERCE_AUTH_SECRET: مِن المِلَفّ", text, StringComparison.Ordinal);
        var printed = text[..text.IndexOf("conn=", StringComparison.Ordinal)];
        Assert.DoesNotContain("from-env-branch", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("from-file-branch", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("file-secret", printed, StringComparison.Ordinal);
    }

    /// <summary>وقيمَةٌ صُدِّرَت فارِغَةً غائِبَةٌ — كَما يَعُدُّها فَحصُ الغائِبِ بَعدَ الكُتلَة (<c>-n</c>) — فَيَملَؤُها المِلَفّ.</summary>
    [Fact]
    public void An_empty_exported_value_is_absent_and_the_file_fills_it()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (conn, _, _) = Run(ConfigBlock(),
            new Dictionary<string, string> { ["ConnectionStrings__Postgres"] = "" }, FileBody);
        Assert.Equal("Host=from-file-branch;Database=x", conn);
        Assert.Contains("[ -n \"${ConnectionStrings__Postgres:-}\" ] || MISSING_VARS=", Gate(), StringComparison.Ordinal);
    }

    /// <summary>والكُتلَةُ كَما كانَت قَبلَ العِلاجِ بِحَرفِها — عَيِّنَةٌ مَحقونَةٌ: المُتَحَقِّقُ نَفسُه يَرى المِلَفَّ غالِباً عَلَيها، فَهُوَ لَيسَ أَعمى.</summary>
    [Fact]
    public void The_precedence_check_goes_red_on_the_block_it_was_written_for()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        const string before =
            "if [ -f \"$ENV_FILE\" ]; then\n" +
            "    say \"تَهيئَةٌ مَحَلِّيَّة: scripts/.prod-gate.env — تُقرَأ (ولا تُطبَع)\"\n" +
            "    set -a\n    # shellcheck disable=SC1090\n    source \"$ENV_FILE\"\n    set +a\n" +
            "else\n    say \"لا مِلَفَّ scripts/.prod-gate.env — تُقرَأُ المُتَغَيِّراتُ مِن البيئَة\"\nfi\n";
        Assert.DoesNotContain(before, ConfigBlock(), StringComparison.Ordinal);
        var (conn, _, _) = Run(before,
            new Dictionary<string, string> { ["ConnectionStrings__Postgres"] = "Host=from-env-branch;Database=x" }, FileBody);
        Assert.Equal("Host=from-file-branch;Database=x", conn);
    }

    /// <summary>ودَليلُ النَشرِ يَقولُ القاعِدَةَ في «التَهيئَة» وفي الخُطوَةِ ١ مِن §٤‑د، والخُطوَةُ تُصَدِّرُ السِرَّ الَّذي تَشتَرِطُه البَوّابَة.</summary>
    [Fact]
    public void The_deploy_guide_says_the_environment_wins_and_step_one_exports_both_mandatory_variables()
    {
        var deploy = File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md"));
        Assert.Contains("lens-docs-vs-code-2", deploy, StringComparison.Ordinal);
        Assert.Contains("البيئَةُ تَغلِبُ المِلَفّ", deploy, StringComparison.Ordinal);
        var step = deploy[deploy.IndexOf("# ١) فَرعُ Neon مَنسوخٌ مِن الإنتاجِ الآن", StringComparison.Ordinal)..];
        step = step[..step.IndexOf("./scripts/verify-production-boot.sh", StringComparison.Ordinal)];
        Assert.Contains("export ConnectionStrings__Postgres=", step, StringComparison.Ordinal);
        Assert.Contains("export ACOMMERCE_AUTH_SECRET=\"$(openssl rand -hex 32)\"", step, StringComparison.Ordinal);
    }
}
