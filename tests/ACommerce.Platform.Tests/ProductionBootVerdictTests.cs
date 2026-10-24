using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Payments.Providers.Paddle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَوّابَةُ الإقلاعِ تَقولُ «عِندَ المُشَغِّل» (3) بِاسمِه، و«كَسرٌ» (1) لِما سِواه — مُشَغَّلَةً لا مَقروءَة (‏P3-2) ═══
//
// **المَقيسُ قَبل** (‏2026-10-12): التَمييزُ سَطرانِ في فَرعِ مَوتِ الإقلاعِ (سِلسِلَةٌ غائِبَة، سِرُّ تَوقيعٍ مَرفوض) وكُلُّ ما سِواهُما
// ‏1. فَنَفادُ حِصَّةِ Neon قُرِئَ كَسراً (و‑٥ في DECISIONS-PENDING: «صنّفت هذا ‏1 … وهو في الحقيقة ‏3»)، وكَذلِكَ بيئَةُ مُزَوِّدٍ مَكتوبَةٌ
// خَطَأً. وصارَ التَمييزُ دالَّةً واحِدَةً `boot_verdict` بَينَ عَلامَتَين في `verify-production-boot.sh` — **تُقطَعُ هُنا كَما هِيَ
// وتُشَغَّلُ بِـbash** عَلى سِجِلٍّ يُصنَعُ مِن **الاستِثناءِ الحَقيقِيِّ نَفسِه**: رِسالَةُ `AddPaddleBilling`/`AddOtoShipping` بِقيمَةٍ
// خارِجَ المَعجَم، و`PostgresException`/`NpgsqlException` بِرَمزِها — بِصيغَةِ «Unhandled exception. » الَّتي يَطبَعُها .NET (مَقيسَةٌ
// مِن إقلاعٍ إنتاجيٍّ فِعلِيٍّ: «Npgsql.PostgresException (0x80004005): 53300: …» و«SqlState: 53300»). لِكُلِّ سَبَبٍ موجِبٌ وسالِب.
// **وعَلى Windows لا bash مَعلومَ المَوضِع** فَيُطبَعُ ذلكَ ويُرجَع — وCI (‏ubuntu) يُشَغِّلُه دائِماً (نَسَقُ `ProviderVaultDeploymentTests`).

public class ProductionBootVerdictTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string Start = "# ─── بِدايَةُ boot_verdict ───";
    private const string End = "# ─── نِهايَةُ boot_verdict ───";

    private static string Gate()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "verify-production-boot.sh"));
        Assert.True(text.Length > 10_000, "أَداةٌ عَمياء: verify-production-boot.sh لَم يُقرَأ.");
        return text;
    }

    private static string Slice(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(a > 0, $"عَلامَةٌ غائِبَة: {start}");
        Assert.Equal(a, text.LastIndexOf(start, StringComparison.Ordinal));
        var b = text.IndexOf(end, a, StringComparison.Ordinal);
        Assert.True(b > a, $"عَلامَةُ النِهايَةِ غائِبَة: {end}");
        return text[a..(b + end.Length)];
    }

    private static string VerdictFunction() => Slice(Gate(), Start, End);

    private static (int Code, string Line) RunBash(string body, params string[] args)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash — لا تُشَغَّلُ الدالَّةُ بِدونِها.");
        var dir = Directory.CreateTempSubdirectory("wasayel-boot-verdict-");
        try
        {
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + body + "\n", new UTF8Encoding(false));
            var psi = new ProcessStartInfo("/bin/bash", new[] { script }.Concat(args))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000), "الدالَّةُ لَم تَنتَهِ في عَشرِ ثَوانٍ.");
            return (p.ExitCode, text.Trim());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>يُشَغِّلُ <c>boot_verdict</c> عَلى سِجِلٍّ مَصنوع — ويُرجِعُ رَمزَها وسَطرَ تَشخيصِها.</summary>
    private (int Code, string Line) Verdict(string log)
    {
        var dir = Directory.CreateTempSubdirectory("wasayel-boot-log-");
        try
        {
            var path = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(path, log, new UTF8Encoding(false));
            var result = RunBash(VerdictFunction() + "\nboot_verdict \"$1\"", path);
            output.WriteLine($"→ {result.Code} · {result.Line}");
            return result;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>ما يَطبَعُه .NET عِندَ استِثناءٍ لا يُلتَقَطُ في <c>Program.cs</c>.</summary>
    private static string Unhandled(Exception ex) => "Unhandled exception. " + ex + "\n";

    private static IConfiguration Config(string key, string value)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

    /// <summary>الرِسالَةُ الحَقيقِيَّةُ الَّتي يَرمي بِها الإقلاعُ لِبيئَةٍ مَكتوبَةٍ خَطَأً — مِن الدالَّةِ نَفسِها لا مَنسوخَة.</summary>
    private static (string Var, string Log) Misspelt(string which) => which switch
    {
        "paddle" => (PaddleEnvironment.EnvVarName(PaddleEnvironment.EnvironmentKey),
            Unhandled(Assert.Throws<InvalidOperationException>(() =>
                new ServiceCollection().AddPaddleBilling(Config(PaddleEnvironment.EnvironmentKey, "sanbdox"))))),
        "oto" => (OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey),
            Unhandled(Assert.Throws<InvalidOperationException>(() =>
                new ServiceCollection().AddOtoShipping(Config(OtoEnvironment.EnvironmentKey, "stagign"))))),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private static bool NoBash(ITestOutputHelper output)
    {
        if (!OperatingSystem.IsWindows()) return false;
        output.WriteLine("Windows — لا bash مَعلومَ المَوضِع؛ يُقاسُ هذا الصَفُّ في CI (‏ubuntu).");
        return true;
    }

    // ─── ١. بيئَةُ مُزَوِّدٍ خارِجَ مَعجَمِها — تَهيئَةٌ بِاسمِ المُتَغَيِّر ──────────────────────────────

    [Theory]
    [InlineData("paddle")]
    [InlineData("oto")]
    public void A_misspelt_provider_environment_is_configuration_named_by_its_variable(string which)
    {
        if (NoBash(output)) return;
        var (name, log) = Misspelt(which);
        var (code, line) = Verdict(log);
        Assert.Equal(3, code);
        Assert.Contains("تَهيئَةٌ ناقِصَة", line, StringComparison.Ordinal);
        Assert.Contains(name, line, StringComparison.Ordinal);
    }

    /// <summary>والمَعجَمُ مُغلَق: مُتَغَيِّرٌ ثالِثٌ بِالصيغَةِ نَفسِها لا يُقرَأُ تَهيئَةً — حَتّى يُضافَ بِقَرار.</summary>
    [Fact]
    public void An_environment_variable_outside_the_lexicon_is_not_read_as_configuration()
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(
            "Unhandled exception. System.InvalidOperationException: ‏Payments__PayPal__Environment قيمَتُه «x» وهي خارِجَ «a»/«b».\n");
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    /// <summary>واسما المُتَغَيِّرَينِ في البَوّابَةِ اسماهُما في الكودِ حَرفاً — فَإعادَةُ تَسمِيَةِ مِفتاحٍ تُحمِّرُ هذا الصَفَّ
    /// قَبلَ أَن تَصيرَ البَوّابَةُ عَمياءَ عَنه.</summary>
    [Fact]
    public void The_gate_names_the_two_variables_exactly_as_the_code_does()
    {
        var f = VerdictFunction();
        Assert.Contains($"({OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey)}|"
                      + $"{PaddleEnvironment.EnvVarName(PaddleEnvironment.EnvironmentKey)}) قيمَتُه «", f, StringComparison.Ordinal);
    }

    // ─── ٢. القاعِدَةُ لا تَخدِم — رَمزُ SqlState مِن مَعجَمٍ مُغلَق ──────────────────────────────────────

    [Theory]
    [InlineData("53000", "Your account or project has exceeded the compute time quota. Upgrade your plan to increase limits.")]
    [InlineData("53300", "too many connections for role \"gate_probe\"")]
    [InlineData("28P01", "password authentication failed for user \"owner\"")]
    [InlineData("28000", "no pg_hba.conf entry for host")]
    [InlineData("3D000", "database \"neondb_x\" does not exist")]
    [InlineData("08000", "connection exception")]
    [InlineData("08001", "could not establish connection")]
    [InlineData("08003", "connection does not exist")]
    [InlineData("08004", "server rejected the connection")]
    [InlineData("08006", "connection failure")]
    [InlineData("57P03", "the database system is starting up")]
    public void A_database_that_does_not_serve_is_the_operators_named_by_its_sqlstate(string sqlState, string message)
    {
        if (NoBash(output)) return;
        var log = Unhandled(new PostgresException(message, "FATAL", "FATAL", sqlState));
        var (code, line) = Verdict(log);
        Assert.Equal(3, code);
        Assert.Contains("القاعِدَةُ لا تَخدِم", line, StringComparison.Ordinal);
        Assert.Contains($"SqlState {sqlState}", line, StringComparison.Ordinal);
    }

    /// <summary>والسالِب: رَمزٌ يُسَبِّبُه الكود — جَدوَلٌ غائِب، تَفَرُّدٌ مَخروق، تَزامُن، قيمَةٌ لا تُحَوَّل — كَسرٌ لا تَهيئَة.</summary>
    [Theory]
    [InlineData("42P01", "relation \"platform.mt_doc_x\" does not exist")]
    [InlineData("23505", "duplicate key value violates unique constraint")]
    [InlineData("40001", "could not serialize access due to concurrent update")]
    [InlineData("22P02", "invalid input syntax for type uuid")]
    public void A_sqlstate_the_code_causes_is_code_breakage(string sqlState, string message)
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(Unhandled(new PostgresException(message, "ERROR", "ERROR", sqlState)));
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_database_is_the_operators()
    {
        if (NoBash(output)) return;
        var ex = new NpgsqlException("Failed to connect to 127.0.0.1:5999", new SocketException((int)SocketError.ConnectionRefused));
        var (code, line) = Verdict(Unhandled(ex));
        Assert.Equal(3, code);
        Assert.Contains("القاعِدَةُ لا تُبلَغ", line, StringComparison.Ordinal);
    }

    // ─── ٣. ما كانَ قَبلَها يَبقى كَما هُوَ، والكَسرُ كَسر ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Unhandled exception. System.InvalidOperationException: Postgres connection string missing\n", "ConnectionStrings__Postgres")]
    [InlineData("Unhandled exception. System.InvalidOperationException: auth_secret_default: …\n", "auth_secret_default")]
    [InlineData("Unhandled exception. System.IO.IOException: Failed to bind to address http://127.0.0.1:5171: address already in use.\n", "المَنفَذُ مَشغول")]
    public void The_named_operator_causes_are_configuration(string log, string named)
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(log);
        Assert.Equal(3, code);
        Assert.Contains(named, line, StringComparison.Ordinal);
    }

    /// <summary><b>الحادِثَةُ الَّتي كَتَبَت البَوّابَة</b> (‏DEPLOY.md §٢·و) — كَسرٌ في الكود، بِحَرفِ استِثنائِها. وسِجِلٌّ فارِغٌ كَذلِك.</summary>
    [Theory]
    [InlineData("Unhandled exception. System.InvalidOperationException: BindAsync method found on TenantProviderService with incorrect format. Must be a static method with format ValueTask<TenantProviderService> BindAsync(HttpContext context, ParameterInfo parameter)\n")]
    [InlineData("")]
    public void The_BindAsync_incident_and_an_empty_log_are_code_breakage(string log)
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(log);
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    // ─── ٤. الفَرعانِ يَسأَلانِ الدالَّةَ نَفسَها ───────────────────────────────────────────────────────

    /// <summary>(‏2026-10-12، الجَولَة ١ — كانَ اسمُه <c>The_death_branch_and_the_route_branch_both_ask_the_one_verdict</c> ويَشتَرِطُ
    /// نِداءَينِ عَلى <c>$APP_LOG</c> كُلِّه، وهذا ما أَسقَطَ كَسراً بَعدَ سَطرٍ عابِرٍ في 3 — قُلِبَ بِقَرارٍ مَرئِيّ): فَرعُ المَوتِ يَسأَلُ
    /// السِجِلَّ (والدالَّةُ تُضَيِّقُه إلى الاستِثناءِ القاتِل)، وفَرعُ المَساراتِ يَسأَلُ <c>route_verdict</c> بِجَوابِ البَوّابَةِ نَفسِها.</summary>
    [Fact]
    public void The_death_branch_asks_the_log_and_the_route_branch_asks_the_gates_own_readiness_measure()
    {
        var gate = Gate();
        Assert.Single(Regex.Matches(gate, @"^boot_verdict\(\) \{", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(gate, @"^route_verdict\(\) \{", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(gate, @"^readiness_answer\(\) \{", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(gate, Regex.Escape("boot_verdict \"$APP_LOG\"")));
        Assert.Contains("        boot_verdict \"$APP_LOG\"\n        exit $?", gate, StringComparison.Ordinal);
        Assert.Contains("    verdict_line=\"$(route_verdict \"$APP_LOG\" \"$READY_ANSWER\")\"", gate, StringComparison.Ordinal);
        Assert.Contains("    exit \"$verdict\"", gate, StringComparison.Ordinal);
        Assert.Contains("boot_verdict <(grep -F '[readiness]' \"$log\"", RouteFunction(), StringComparison.Ordinal);
        Assert.True(gate.IndexOf(RouteEnd, StringComparison.Ordinal) < gate.IndexOf("boot_verdict \"$APP_LOG\"", StringComparison.Ordinal),
            "الدالَّتانِ تُعَرَّفانِ بَعدَ أَوَّلِ نِداء.");
    }

    // ─── ٥. المَنفَذُ الحُرُّ بِلا ss ولا netstat — `/proc/net/tcp` مُشَغَّلاً عَلى مَقبِسٍ حَقيقِيّ ─────────────

    private static string ProbeFunction()
    {
        var gate = Gate();
        var m = Regex.Match(gate, @"^port_in_use_proc\(\) \{.*?^\}", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(m.Success, "port_in_use_proc غائِبَةٌ عَن البَوّابَة.");
        return m.Value;
    }

    [Fact]
    public void The_proc_net_tcp_probe_sees_a_listening_socket_and_not_a_free_port_under_pipefail()
    {
        if (NoBash(output)) return;
        if (!File.Exists("/proc/net/tcp"))
        {
            output.WriteLine("لا /proc/net/tcp هُنا (لَيسَ لينكس) — يُقاسُ في CI (‏ubuntu).");
            return;
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var busy = ((IPEndPoint)listener.LocalEndpoint).Port;

        int free;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            free = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        var f = ProbeFunction();
        var (busyCode, _) = RunBash(f + "\nport_in_use_proc \"$1\"", busy.ToString());
        var (freeCode, _) = RunBash(f + "\nport_in_use_proc \"$1\"", free.ToString());
        output.WriteLine($"مَشغول {busy} → {busyCode} · حُرّ {free} → {freeCode}");
        Assert.Equal(0, busyCode);
        Assert.Equal(1, freeCode);
    }

    /// <summary>والأَداةُ تُقاسُ بَعدَ الإقلاعِ في كُلِّ تَشغيل (القاعِدَة ١٠): البَوّابَةُ تَسأَلُها عَن مَنفَذِ التَطبيقِ وهُوَ يُجيب.</summary>
    [Fact]
    public void The_gate_measures_its_port_tool_on_the_apps_own_port_after_boot()
    {
        var gate = Gate();
        var answered = gate.IndexOf("say \"    ✓ أَجابَ بَعدَ ${WAITED}ث\"", StringComparison.Ordinal);
        var check = gate.IndexOf("if port_in_use \"$PORT\"; then", StringComparison.Ordinal);
        Assert.True(answered > 0 && check > answered, "لا فَحصَ لِأَداةِ المَنفَذِ بَعدَ أَن أَجابَ التَطبيق.");
        Assert.Contains("elif [ -r /proc/net/tcp ]; then PORT_TOOL=\"/proc/net/tcp\"", gate, StringComparison.Ordinal);
    }

    // ─── ٦. وCI تُشَغِّلُها حينَ يوجَدُ سِرُّها، وغِيابُه لا يُفشِل ───────────────────────────────────────

    [Fact]
    public void Ci_runs_the_gate_behind_its_secret_and_its_absence_never_fails()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));
        var job = ci[ci.IndexOf("  production-boot:", StringComparison.Ordinal)..];
        Assert.True(job.Length > 500, "وَظيفَةُ production-boot غائِبَةٌ عَن ci.yml.");

        Assert.Contains("BOOT_GATE_CONNECTION_STRING: ${{ secrets.BOOT_GATE_CONNECTION_STRING }}", job, StringComparison.Ordinal);
        Assert.Contains("if: env.BOOT_GATE_CONNECTION_STRING == ''", job, StringComparison.Ordinal);
        Assert.Contains("echo \"::notice::", job, StringComparison.Ordinal);
        Assert.Contains("if: env.BOOT_GATE_CONNECTION_STRING != ''", job, StringComparison.Ordinal);
        Assert.Contains("./scripts/verify-production-boot.sh || code=$?", job, StringComparison.Ordinal);

        // ‏3 تَحذيرٌ بِلا خُروج، وما سِواها مِن غَيرِ الصِفرِ يَخرُجُ بِرَمزِه.
        var three = Regex.Match(job, @"^\s+3\) (?<arm>.*?);;", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(three.Success, "لا فَرعَ لِرَمزِ 3.");
        Assert.Contains("::warning::", three.Groups["arm"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("exit", three.Groups["arm"].Value, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^\s+\*\) .*?exit ""\$code"" ;;", RegexOptions.Multiline | RegexOptions.Singleline), job);

        // ‏والسِرُّ مُسَمّىً في جَدوَلِ GitHub في DEPLOY.md — فَرعاً لا إنتاجاً.
        var deploy = File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md"));
        Assert.Contains("| Secrets | Repository secret (اختِياريّ، ‏2026-10-12) | `BOOT_GATE_CONNECTION_STRING` |", deploy, StringComparison.Ordinal);
    }

    /// <summary><b>وتَشغيلٌ واحِدٌ عَلى فَرعِ البَوّابَةِ في كُلِّ لَحظَة</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ N): الوَظيفَةُ تُقلِعُ Production
    /// (‏CreateOrUpdate وبَذر) عَلى الفَرعِ الواحِدِ في كُلِّ دَفعَةٍ وطَلَبِ دَمج، وتَشغيلانِ مُتَزامِنانِ يَتَسابَقانِ عَلى نَوعِ وَثيقَةٍ جَديدٍ (‏23505 ⇒ 1).
    /// فَمَجموعَةُ تَزامُنٍ في الوَظيفَةِ نَفسِها بِلا إلغاءٍ لِلجاري، ولا مَجموعَةَ عَلى مُستَوى سَيرِ العَمَلِ تُلغيها؛ و§٢·و يُسَمّي الفَرعَ حالَةً مُشتَرَكَة.</summary>
    [Fact]
    public void Ci_runs_one_boot_gate_at_a_time_on_the_shared_branch_and_never_cancels_a_running_one()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var jobs = Regex.Matches(ci, @"^  (?<name>[\w-]+):\s*$", RegexOptions.Multiline).ToList();
        var names = jobs.Select(m => m.Groups["name"].Value).ToArray();
        output.WriteLine($"وَظائِفُ ci.yml: {string.Join(", ", names)}");
        Assert.Contains("production-boot", names);
        var at = jobs.FindIndex(m => m.Groups["name"].Value == "production-boot");
        var body = ci[jobs[at].Index..(at + 1 < jobs.Count ? jobs[at + 1].Index : ci.Length)];

        Assert.Contains("\n    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(ci, @"^\s*concurrency:", RegexOptions.Multiline));                 // ‏لا مَجموعَةَ ثانِيَةٌ تُغَطّيها
        Assert.DoesNotMatch(new Regex(@"cancel-in-progress:\s*true"), ci);

        var deploy = File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md"));
        var start = deploy.IndexOf("## ٢·و.", StringComparison.Ordinal);
        var end = deploy.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "§٢·و غائِب.");
        var section = deploy[start..end];
        Assert.Contains("### فَرعُ البَوّابَةِ في CI حالَةٌ مُشتَرَكَة", section, StringComparison.Ordinal);
        Assert.Contains("`concurrency: { group: production-boot, cancel-in-progress: false }`", section, StringComparison.Ordinal);
    }

    /// <summary><b>ومُنتَظِرُ <c>main</c> لا يُلغيه وافِدٌ أَحدَث — لِأَنّ تَشغيلَ <c>main</c> المُلغى لا يُنشَرُ أَبَداً</b> (‏2026-10-24 — الجَولَةُ ١
    /// لِمُراجَعَةِ تَيّارِ S13، عَلى <c>e2170c6</c>): بِالافتِراضِ (<c>queue: single</c>) يَنتَظِرُ في المَجموعَةِ واحِد، وكُلُّ وافِدٍ أَحدَثَ يُلغيه؛
    /// والوَظيفَةُ تَعمَلُ لِلدَفعِ ولِطَلَبِ الدَمجِ في المَجموعَةِ نَفسِها، فَأَوَّلُ طَلَبِ دَمجٍ بَعدَ دَفعَةٍ تَنتَظِرُ يُلغيها — و<c>deploy-hf.yml</c>
    /// لا يَنشُرُ إلّا عَلى <c>conclusion == 'success'</c>. فَيُقرَأُ هُنا الطَرَفانِ مَعاً: المَجموعَةُ مِن جِسمِ الوَظيفَةِ بِـ<see cref="BootQueueRefusal"/>،
    /// وشَرطُ النَشرِ مِن <c>deploy-hf.yml</c> — فَإن سَقَطَ أَحَدُهُما سَقَطَ سَبَبُ الآخَر، ويُقالُ ذلكَ هُنا لا يُستَنتَج.</summary>
    [Fact]
    public void A_waiting_main_boot_run_is_never_replaced_by_a_newer_one_because_a_cancelled_main_run_never_deploys()
    {
        var ci = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var body = JobBody(ci, "production-boot");
        var refusal = BootQueueRefusal(body, out var keys);
        output.WriteLine($"concurrency في production-boot: {string.Join(" · ", keys.Select(k => $"{k.Key}={k.Value}"))}");
        Assert.True(keys.Count > 0 || refusal is "no_concurrency" or "concurrency_not_a_block", "أَداةٌ عَمياء: صِفرُ مِفتاحٍ في كُتلَةِ concurrency.");
        Assert.True(refusal is null, $"مَجموعَةُ production-boot تُلغي جارِياً أَو مُنتَظِراً ({refusal}) — وتَشغيلُ main المُلغى لا يُنشَر.");

        // ‏ولِماذا يَلزَمُ «لا يُلغى المُنتَظِر»: الوَظيفَةُ تَعمَلُ لِلحَدَثَين في المَجموعَةِ نَفسِها، والنَشرُ يَتبَعُ نَجاحَ دَفعَةٍ وَحدَه.
        Assert.Contains("\non:\n  push:\n    branches: [main]\n  pull_request:\n    branches: [main]\n", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("if:", body.Split("\n    steps:")[0], StringComparison.Ordinal);   // ‏لا شَرطَ عَلى الوَظيفَةِ يُخرِجُ طَلَبَ الدَمج
        var deploy = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy-hf.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var mirror = JobBody(deploy, "mirror");
        var gate = Regex.Match(mirror, @"^    if: >-\n(?<cond>(?:      .*\n)+)", RegexOptions.Multiline);
        Assert.True(gate.Success, "شَرطُ وَظيفَةِ mirror في deploy-hf.yml غائِب.");
        var cond = Regex.Replace(gate.Groups["cond"].Value, @"\s+", " ").Trim();
        output.WriteLine($"شَرطُ النَشر: {cond}");
        Assert.Equal("github.event_name == 'workflow_dispatch' || (github.event.workflow_run.conclusion == 'success' && github.event.workflow_run.event == 'push')", cond);
        Assert.Contains("workflows: [\"CI\"]\n    types: [completed]\n    branches: [main]", deploy, StringComparison.Ordinal);

        // ‏و§٢·و يَقولُه بِمُلحَقٍ مُؤَرَّخ: المَجموعَةُ، وما لا يُنشَر، والعِلاج.
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md"));
        var start = doc.IndexOf("### فَرعُ البَوّابَةِ في CI حالَةٌ مُشتَرَكَة", StringComparison.Ordinal);
        var end = doc.IndexOf("\n## ", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "قِسمُ «فَرعُ البَوّابَةِ في CI حالَةٌ مُشتَرَكَة» غائِب.");
        var section = doc[start..end];
        Assert.Contains("**فَالمَجموعَةُ صارَت `queue: max`**", section, StringComparison.Ordinal);
        Assert.Contains("`conclusion == 'success'`", section, StringComparison.Ordinal);
        Assert.Contains("**إعادَةُ تَشغيلِ CI لِذلكَ الكوميت**", section, StringComparison.Ordinal);
    }

    /// <summary>جِسمُ الوَظيفَةِ <paramref name="name"/> في سَيرِ عَمَلٍ — مِن سَطرِها إلى سَطرِ الوَظيفَةِ التالِيَةِ أَو آخِرِ المِلَفّ.</summary>
    private static string JobBody(string workflow, string name)
    {
        var jobs = Regex.Matches(workflow, @"^  (?<name>[\w-]+):\s*$", RegexOptions.Multiline).ToList();
        var at = jobs.FindIndex(m => m.Groups["name"].Value == name);
        Assert.True(at >= 0, $"الوَظيفَةُ {name} غائِبَة — وَظائِفُ المِلَفّ: {string.Join(", ", jobs.Select(m => m.Groups["name"].Value))}.");
        return workflow[jobs[at].Index..(at + 1 < jobs.Count ? jobs[at + 1].Index : workflow.Length)];
    }

    /// <summary><b>أَتُبقي مَجموعَةُ <c>production-boot</c> الجاريَ والمُنتَظِرَ مَعاً؟</b> — <c>null</c> إن أَبقَتهُما، وإلّا رَمزُ الرَفض. تُقرَأُ كُتلَةُ
    /// <c>concurrency:</c> الَّتي تَلي الوَظيفَةَ مُباشَرَةً (أَربَعُ مَسافات) سَطراً سَطراً بِلا تَعليقاتِها: <c>group</c> بِحَرفِه، و<c>cancel-in-progress</c>
    /// ‏<c>false</c> بِحَرفِه (لا تَعبيرَ يُقلَبُ في فَرعٍ ما، ولا غِيابٌ يُترَكُ لِلافتِراض)، و<c>queue</c> ‏<c>max</c> — وغِيابُه هُوَ الافتِراضُ <c>single</c> الَّذي يُلغي المُنتَظِر.
    /// ومِفتاحٌ لا يَعرِفُه المَعجَمُ يُرَدّ: لا يُستَنتَجُ ما يَفعَلُه.</summary>
    internal static string? BootQueueRefusal(string jobBody, out Dictionary<string, string> keys)
    {
        keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = jobBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var at = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^    concurrency:\s*(#.*)?$"));
        if (at < 0) return lines.Any(l => Regex.IsMatch(l, @"^    concurrency:")) ? "concurrency_not_a_block" : "no_concurrency";
        for (var i = at + 1; i < lines.Length; i++)
        {
            var line = Regex.Replace(lines[i], @"\s+#.*$", "");
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var m = Regex.Match(line, @"^      (?<k>[\w-]+):\s*(?<v>\S.*?)\s*$");
            if (!m.Success) { if (line.StartsWith("      ", StringComparison.Ordinal)) return "unreadable_line"; break; }
            if (!keys.TryAdd(m.Groups["k"].Value, m.Groups["v"].Value)) return "duplicate_key";
        }
        if (keys.Keys.FirstOrDefault(k => k is not ("group" or "cancel-in-progress" or "queue")) is { } unknown) return $"unknown_key:{unknown}";
        if (keys.GetValueOrDefault("group") != "production-boot") return "group";
        if (!keys.TryGetValue("cancel-in-progress", out var cancel)) return "cancel_unstated";
        if (cancel != "false") return "cancels_running";
        return keys.GetValueOrDefault("queue") == "max" ? null : "replaces_waiting";
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ طابورِ البَوّابَةِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): كُتلَةُ اليَومِ تُقبَل، وكُلُّ شَكلٍ يُلغي جارِياً أَو مُنتَظِراً
    /// يُرَدُّ بِرَمزِه — ومِنها كُتلَةُ <c>e2170c6</c> بِحَرفِها (بِلا <c>queue</c>) الَّتي مَرَّت صَفَّها.</summary>
    [Fact]
    public void The_boot_queue_checker_can_actually_go_red()
    {
        const string Head = "  production-boot:\n    runs-on: ubuntu-latest\n";
        const string Tail = "    env:\n      X: y\n    steps:\n      - run: true\n";
        var accepted = new[]
        {
            Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n      queue: max\n" + Tail,
            Head + "    concurrency:\n      queue: max   # ‏تَعليق\n      group: production-boot\n      cancel-in-progress: false\n" + Tail,
        };
        var rejected = new (string Body, string Code)[]
        {
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n" + Tail, "replaces_waiting"),   // ‏e2170c6
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n      queue: single\n" + Tail, "replaces_waiting"),
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: true\n      queue: max\n" + Tail, "cancels_running"),
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: ${{ github.event_name == 'pull_request' }}\n      queue: max\n" + Tail, "cancels_running"),
            (Head + "    concurrency:\n      group: production-boot\n      queue: max\n" + Tail, "cancel_unstated"),
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n      - queue: max\n" + Tail, "unreadable_line"),
            (Head + "    concurrency:\n      group: production-boot-${{ github.event_name }}\n      cancel-in-progress: false\n      queue: max\n" + Tail, "group"),
            (Head + "    concurrency:\n      group: production-boot\n      cancel-in-progress: false\n      queue: max\n      qeueu: max\n" + Tail, "unknown_key:qeueu"),
            (Head + "    concurrency:\n      group: production-boot\n      queue: single\n      cancel-in-progress: false\n      queue: max\n" + Tail, "duplicate_key"),
            (Head + "    concurrency: production-boot\n" + Tail, "concurrency_not_a_block"),
            (Head + Tail, "no_concurrency"),
        };
        var wrong = accepted.Where(b => BootQueueRefusal(b, out _) is not null).Select(b => $"رُفِضَ حَيٌّ ({BootQueueRefusal(b, out _)}): {b}")
            .Concat(rejected.Where(r => BootQueueRefusal(r.Body, out _) != r.Code)
                .Select(r => $"رَمزُه {BootQueueRefusal(r.Body, out _) ?? "قُبِل"} لا {r.Code}"))
            .ToList();
        foreach (var r in rejected) output.WriteLine($"مَرفوضٌ ({BootQueueRefusal(r.Body, out _)})");
        var codes = rejected.Select(r => r.Code).Distinct().Count();
        output.WriteLine($"حُقِنَ {accepted.Length} مَقبولاً و{rejected.Length} مَرفوضاً بِـ{codes} رَمزاً.");
        Assert.True(accepted.Length >= 2 && rejected.Length >= 11 && codes >= 9, "أَداةٌ عَمياء: العَيِّنَةُ أَصغَرُ مِمّا كُتِب.");
        Assert.True(wrong.Count == 0, $"أَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary><b>وتَشغيلُ <c>main</c> المُلغى لا يُعادُ إلّا وهُوَ رَأسُ <c>main</c> — لِأَنّ <c>deploy-hf.yml</c> يَدفَعُ أَيَّ رَقمٍ يُعطاه</b> (‏2026-10-24 —
    /// الجَولَةُ ٢ لِمُراجَعَةِ تَيّارِ S13، عَلى <c>c69cdb8</c>): عِلاجُ «أَعِد تَشغيلَ CI لِذلكَ الكوميت» في مُلحَقِ §٢·و كانَ بِلا شَرط، والنَشرُ يَستَنسِخُ
    /// <c>workflow_run.head_sha</c> ويَدفَعُه بِـ<c>--force</c> بِلا سُؤالٍ عَن الرَأس — فَإعادَةُ كوميتٍ أَقدَمَ بَعدَ نَشرِ أَحدَثَ تَراجُعٌ صامِت. فَيُقرَأُ هُنا
    /// الطَرَفانِ بِـ<see cref="RerunConditionRefusal"/>: الشَرطُ بَعدَ العِلاجِ في §٢·و بِأَمرِه، و<c>deploy-hf.yml</c> عَلى الحالِ الَّذي يَصِفُه الشَرط.</summary>
    [Fact]
    public void A_cancelled_main_run_is_re_run_only_while_it_is_still_mains_tip_because_deploy_hf_pushes_any_sha_it_is_given()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "DEPLOY.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = doc.IndexOf("### فَرعُ البَوّابَةِ في CI حالَةٌ مُشتَرَكَة", StringComparison.Ordinal);
        var end = start < 0 ? -1 : doc.IndexOf("\n## ", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "قِسمُ «فَرعُ البَوّابَةِ في CI حالَةٌ مُشتَرَكَة» غائِب.");
        var deploy = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "deploy-hf.yml"));

        var refusal = RerunConditionRefusal(doc[start..end], deploy, out var codeLines, out var tokens);
        output.WriteLine($"deploy-hf.yml: {codeLines} سَطراً غَيرَ مُعَلَّقٍ مَقروءاً · مُفرَداتُ الحارِسِ المَفحوصَة {GuardTokens.Length}: {string.Join(" ", GuardTokens)}"
            + $" · وُجِدَ مِنها {tokens.Count}{(tokens.Count > 0 ? ": " + string.Join(", ", tokens) : "")}");
        output.WriteLine($"قِسمُ §٢·و: {end - start} مِحرَفاً · الحُكم: {refusal ?? "مَقبول"}");
        Assert.True(codeLines > 0, "أَداةٌ عَمياء: صِفرُ سَطرٍ مِن deploy-hf.yml.");
        Assert.True(refusal is null, $"عِلاجُ «أَعِد تَشغيلَ CI» في §٢·و لا يَشتَرِطُ رَأسَ main، أَو deploy-hf.yml تَغَيَّرَ عَمّا يَصِفُه الشَرط ({refusal}).");
    }

    private const string RerunRemedy = "**إعادَةُ تَشغيلِ CI لِذلكَ الكوميت**";
    private const string RerunCondition = "لا تَكونُ إلّا ورَأسُ `main` هُوَ ذلكَ الكوميتُ نَفسُه";
    private const string TipCommand = "`git ls-remote origin refs/heads/main`";
    private const string NewerTipRefused = "**فَلا يُعادُ أَبَداً**";
    private const string GuardAbsence = "**ولا يُغلِقُهُما إلّا حارِسٌ في `deploy-hf.yml`**";

    /// <summary>مُفرَداتُ ما يَحتاجُه حارِسُ «أَهُوَ رَأسُ <c>main</c>؟» أَو «أَهُوَ سَلَفُ ما يَعمَل؟» في git أَو في API ‏GitHub — مَعجَمٌ مُغلَق. وحارِسٌ يُكتَبُ
    /// بِغَيرِها لا يُرى، ويُقالُ ذلكَ هُنا لا يُخفى: الصَفُّ سِلكُ تَعَثُّرٍ بَينَ الوَثيقَةِ والكود، لا بُرهانُ غِياب.</summary>
    private static readonly string[] GuardTokens =
        ["merge-base", "is-ancestor", "ls-remote", "origin/main", "rev-list", "refs/remotes/", "FETCH_HEAD", "branches/main", "/commits/main", "git fetch"];

    /// <summary><b>أَيَشتَرِطُ §٢·و رَأسَ <c>main</c> لِإعادَةِ تَشغيلٍ مُلغىً، ولا يُكَذِّبُه <c>deploy-hf.yml</c>؟</b> — <c>null</c> إن نَعَم، وإلّا رَمزُ الرَفض.
    /// مِن <c>deploy-hf.yml</c> تُقرَأُ الأَسطُرُ غَيرُ المُعَلَّقَةِ وَحدَها: الفَرضِيَّتانِ (الاستِنساخُ مِن <c>workflow_run.head_sha</c>، والدَفعُ بِـ<c>--force</c>)
    /// بِحَرفِهِما، وصِفرُ مُفرَدَةٍ مِن <see cref="GuardTokens"/> — فَإن وُجِدَ حارِسٌ صارَ الشَرطُ وَصفاً لِما لَيسَ قائِماً، ويُلحَقُ بِقَرار. ومِن القِسم: العِلاجُ،
    /// ثُمَّ بَعدَه الشَرطُ، ثُمَّ بَعدَ الشَرطِ أَمرُ الرَأسِ و«لا يُعادُ أَبَداً» وتَسمِيَةُ الحارِسِ الغائِب — بِحَرفِها وتَشكيلِها نَسَقَ الصُفوفِ المُجاوِرَة.</summary>
    internal static string? RerunConditionRefusal(string section, string deployWorkflow, out int codeLines, out List<string> tokens)
    {
        var code = deployWorkflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#')).ToList();
        codeLines = code.Count;
        tokens = GuardTokens.Where(t => code.Any(l => l.Contains(t, StringComparison.Ordinal))).ToList();
        if (codeLines == 0) return "deploy_unread";
        if (!code.Any(l => l.Contains("|| github.event.workflow_run.head_sha", StringComparison.Ordinal))
            || !code.Any(l => l.Contains("git push --force", StringComparison.Ordinal))) return "premise_changed";
        if (tokens.Count > 0) return "deploy_guard_unacknowledged";

        var remedy = section.IndexOf(RerunRemedy, StringComparison.Ordinal);
        if (remedy < 0) return "no_remedy";
        var condition = section.IndexOf(RerunCondition, remedy, StringComparison.Ordinal);
        if (condition < 0) return "remedy_unconditioned";
        if (section.IndexOf(TipCommand, condition, StringComparison.Ordinal) < 0) return "no_tip_command";
        if (section.IndexOf(NewerTipRefused, condition, StringComparison.Ordinal) < 0) return "newer_tip_not_refused";
        return section.IndexOf(GuardAbsence, condition, StringComparison.Ordinal) < 0 ? "guard_absence_unsaid" : null;
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ شَرطِ الإعادَةِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): قِسمٌ وسَيرُ نَشرٍ بِشَكلِ اليَومِ يُقبَلان، وكُلُّ سُقوطٍ يُرَدُّ بِرَمزِه —
    /// ومِنها مُلحَقُ <c>c69cdb8</c> بِشَكلِه (العِلاجُ بِلا شَرط) الَّذي مَرَّ صَفَّه.</summary>
    [Fact]
    public void The_rerun_condition_checker_can_actually_go_red()
    {
        const string Remedy = "- والعِلاجُ " + RerunRemedy + " (‏Re-run all jobs).\n";
        const string Rest = " · " + TipCommand + " · " + NewerTipRefused + " · " + GuardAbsence + "\n";
        const string Section = "### قِسم\n" + Remedy + "(مُلحَق) **و«…» أَعلاه " + RerunCondition + "**:" + Rest;
        const string Deploy = "on:\n  workflow_run:\n    steps:\n      - uses: actions/checkout@v4\n        with:\n          ref: >-\n"
            + "            ${{ github.event_name == 'workflow_dispatch'\n                && inputs.ref\n                || github.event.workflow_run.head_sha }}\n"
            + "      - run: |\n          timeout 300 git push --force \\\n            \"https://x@huggingface.co/spaces/a/b.git\" \"HEAD:refs/heads/main\"\n";
        var accepted = new (string Section, string Deploy)[]
        {
            (Section, Deploy),
            (Section, "# ‏لا merge-base ولا ls-remote هُنا — تَعليقٌ لا حارِس\n" + Deploy),
        };
        var rejected = new (string Section, string Deploy, string Code)[]
        {
            ("### قِسم\n" + Remedy, Deploy, "remedy_unconditioned"),                                                     // ‏c69cdb8
            ("### قِسم\n(قَبلَ العِلاج) " + RerunCondition + Rest + Remedy, Deploy, "remedy_unconditioned"),
            ("### قِسم\n- لا عِلاج.\n", Deploy, "no_remedy"),
            ("### قِسم\n" + Remedy + RerunCondition + " · " + NewerTipRefused + " · " + GuardAbsence + "\n", Deploy, "no_tip_command"),
            ("### قِسم\n" + TipCommand + "\n" + Remedy + RerunCondition + " · " + NewerTipRefused + " · " + GuardAbsence + "\n", Deploy, "no_tip_command"),
            ("### قِسم\n" + Remedy + RerunCondition + " · " + TipCommand + " · " + GuardAbsence + "\n", Deploy, "newer_tip_not_refused"),
            ("### قِسم\n" + Remedy + RerunCondition + " · " + TipCommand + " · " + NewerTipRefused + "\n", Deploy, "guard_absence_unsaid"),
            (Section, Deploy + "      - run: git merge-base --is-ancestor \"$sha\" \"$live\" || exit 0\n", "deploy_guard_unacknowledged"),
            (Section, Deploy + "      - run: '[ \"$(git ls-remote origin main | cut -f1)\" = \"$sha\" ]'\n", "deploy_guard_unacknowledged"),
            (Section, Deploy.Replace("git push --force", "git push", StringComparison.Ordinal), "premise_changed"),
            (Section, Deploy.Replace("|| github.event.workflow_run.head_sha", "|| 'main'", StringComparison.Ordinal), "premise_changed"),
            (Section, "# ‏تَعليقٌ وَحدَه\n\n", "deploy_unread"),
        };
        var wrong = accepted.Where(a => RerunConditionRefusal(a.Section, a.Deploy, out _, out _) is not null)
            .Select(a => $"رُفِضَ حَيٌّ ({RerunConditionRefusal(a.Section, a.Deploy, out _, out _)})")
            .Concat(rejected.Where(r => RerunConditionRefusal(r.Section, r.Deploy, out _, out _) != r.Code)
                .Select(r => $"رَمزُه {RerunConditionRefusal(r.Section, r.Deploy, out _, out _) ?? "قُبِل"} لا {r.Code}"))
            .ToList();
        foreach (var r in rejected) output.WriteLine($"مَرفوضٌ ({RerunConditionRefusal(r.Section, r.Deploy, out _, out _)})");
        var codes = rejected.Select(r => r.Code).Distinct().Count();
        output.WriteLine($"حُقِنَ {accepted.Length} مَقبولاً و{rejected.Length} مَرفوضاً بِـ{codes} رَمزاً.");
        Assert.True(accepted.Length >= 2 && rejected.Length >= 12 && codes >= 8, "أَداةٌ عَمياء: العَيِّنَةُ أَصغَرُ مِمّا كُتِب.");
        Assert.True(wrong.Count == 0, $"أَخطَأَ الفاحِصُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }

    // ─── ٧. تَوصيفُ «اليَوم» ثُمَّ قَلبُه — الحُكمُ مِن الاستِثناءِ القاتِلِ ومِن قِياسِ البَوّابَةِ لا مِن أَوَّلِ سَطرٍ في السِجِلّ ─────
    //    (مُراجَعَةُ تَيّارِ S10، الجَولَة ١)
    //
    // **المَقيسُ قَبل** (‏`1267794`، صُفوفُ «Today_» الثَلاثَة): `boot_verdict` تَأخُذُ أَوَّلَ SqlState وأَيَّ «Failed to connect» في
    // السِجِلِّ كُلِّه، وفَرعُ المَساراتِ يُناديها عَلى `$APP_LOG` كُلِّه، وصِنفُ 08 بِنَجمَةٍ يَضُمُّ ‏08P01 — فَكَسرٌ في الكودِ بَعدَ سَطرِ
    // قاعِدَةٍ عابِرٍ يُقرَأُ 3، وفي CI ‏`::warning::` فَيُنشَر. **وقُلِبَت بِأَسمائِها هُنا**: فَرعُ المَوتِ يُقرَأُ مِن الاستِثناءِ الَّذي
    // قَتَلَ العَمَلِيَّة، وفَرعُ المَساراتِ مِن جَوابِ `GET /health/ready` في التَشغيلِ نَفسِه (`route_verdict`)، وصِنفُ 08 بِرُموزِه الخَمسَة.
    //
    // **(‏2026-10-24 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه، مُراجَعَةُ الموجَة ٤: «قُلِبَت بِأَسمائِها» تَصدُقُ عَلى اثنَينِ مِن الثَلاثَةِ لا عَلى
    // الثَلاثَة.)** صَفُّ المَساراتِ في `1267794` (`Today_a_route_failure_after_an_earlier_transient_connect_line_reads_as_the_operators`) كانَ
    // يُنادي `boot_verdict` عَلى السِجِلِّ مُباشَرَةً لا فَرعَ المَسارات — والعِلاجُ (`1187f83`) نَقَلَ فَرعَ المَساراتِ إلى `route_verdict` ولَم
    // يُغَيِّر حُكمَ `boot_verdict` عَلى سِجِلٍّ بِلا «Unhandled exception.»: مَقيساً اليَومَ بِالدالَّةِ مَقطوعَةً مِن البَوّابَةِ عِندَ `1267794`
    // و`1187f83` والرَأس، سِجِلُّ ذلك الصَفِّ بِحَرفِه ⇒ ‏3 «القاعِدَةُ لا تُبلَغ» في الثَلاثَة. فَالصَفُّ القَديمُ لَو بَقِيَ لَبَقِيَ أَخضَرَ بَعدَ
    // العِلاج، وخَلَفُه `A_route_failure_while_readiness_answered_ready_is_code_breakage_whatever_the_log_holds` صَفٌّ جَديدٌ يَسأَلُ
    // `route_verdict` لا الصَفُّ نَفسُه مَقلوباً. والقَلبُ بِمَعناه (القاعِدَة ٣) لِصَفَّي ‏08P01 والمَوتِ وَحدَهُما. وبُرهانُ «قَبلُ/بَعدُ» لِفَرعِ
    // المَساراتِ اثنان: الصَفُّ البِنيَوِيُّ `The_death_branch_asks_the_log_and_the_route_branch_asks_the_gates_own_readiness_measure` (كانَ
    // يَشتَرِطُ نِداءَينِ عَلى `$APP_LOG` كُلِّه فَقُلِبَ بِقَرارٍ مَرئِيّ)، والتَشغيلُ الحَيُّ في رِسالَةِ `1187f83` (مُستَأجِرٌ غَيرُ مَوجودٍ والقاعِدَةُ
    // تَخدِمُ ⇒ 1، والقاعِدَةُ تُعادُ تَسمِيَتُها بَعدَ «أَجابَ» ⇒ 3).

    private const string RouteStart = "# ─── بِدايَةُ route_verdict ───";
    private const string RouteEnd = "# ─── نِهايَةُ route_verdict ───";

    private static string RouteFunction() => Slice(Gate(), RouteStart, RouteEnd);

    /// <summary>يُشَغِّلُ <c>route_verdict</c> (ومَعَها <c>boot_verdict</c> الَّتي تُناديها) عَلى سِجِلٍّ مَصنوعٍ وجَوابِ جاهِزِيَّة.</summary>
    private (int Code, string Line) RouteVerdict(string log, string readyAnswer)
    {
        var dir = Directory.CreateTempSubdirectory("wasayel-route-log-");
        try
        {
            var path = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(path, log, new UTF8Encoding(false));
            var result = RunBash(VerdictFunction() + "\n" + RouteFunction() + "\nroute_verdict \"$1\" \"$2\"", path, readyAnswer);
            output.WriteLine($"[{readyAnswer}] → {result.Code} · {result.Line}");
            return result;
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary><b>سَطرُ قِياسِ الجاهِزِيَّةِ كَما يَكتُبُه <c>Readiness.cs</c> نَفسُه</b> — القالَبُ يُقرَأُ مِن المَصدَرِ لا يُنسَخ، ويُصَيَّرُ
    /// بِالاستِثناءِ الحَقيقيِّ كَما صَيَّرَه Serilog في السِجِلِّ الحَيِّ (‏«[22:43:50 WRN] [readiness] القاعِدَةُ لا تُجيب: PostgresException:
    /// 55000: …»). فَإن تَغَيَّرَ القالَبُ أَو وَسمُه احمَرَّت الصُفوفُ الَّتي تَقرَؤُه بِأَسمائِها.</summary>
    private static string ReadinessLine(Exception ex)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "libs", "core", "ACommerce.Platform.Hosting", "Readiness.cs"));
        var m = Regex.Match(source, @"log\.LogWarning\(""(?<template>\[readiness\][^""]*)"", ex\.GetType\(\)\.Name, ex\.Message\)");
        Assert.True(m.Success, "قالَبُ سَطرِ الجاهِزِيَّةِ في Readiness.cs غائِبٌ أَو تَغَيَّرَ شَكلُه — والبَوّابَةُ تَقرَؤُه بِوَسمِه.");
        var rendered = m.Groups["template"].Value
            .Replace("{Type}", ex.GetType().Name, StringComparison.Ordinal)
            .Replace("{Message}", ex.Message, StringComparison.Ordinal);
        return "[12:00:00 WRN] " + rendered + "\n";
    }

    /// <summary>سَطرُ قاعِدَةٍ عابِرٌ (تَحذيرٌ أُعيدَت بَعدَه المُحاوَلَة) ثُمَّ سُقوطُ مَسارٍ بِاستِثناءٍ مِن الكود — سِجِلُّ المُراجَعَةِ بِحَرفِه.</summary>
    private const string TransientConnectThenCodeFailure =
        "[WRN] Npgsql.NpgsqlException (0x80004005): Failed to connect to 10.1.2.3:5432 (transient, retried…)\n"
        + "HTTP GET / responded 500\n"
        + "System.NullReferenceException: Object reference not set to an instance of an object.\n";

    /// <summary>كانَ <c>Today_a_route_failure_after_an_earlier_transient_connect_line_reads_as_the_operators</c> (‏3): البَوّابَةُ قاسَت
    /// القاعِدَةَ تَخدِمُ في هذا التَشغيل، فَسُقوطُ المَسارِ مِن الكود — وسَطرُ «Failed to connect» قَبلَه لا يُقرَأ.</summary>
    [Fact]
    public void A_route_failure_while_readiness_answered_ready_is_code_breakage_whatever_the_log_holds()
    {
        if (NoBash(output)) return;
        var (code, line) = RouteVerdict(TransientConnectThenCodeFailure, "ready");
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
        Assert.DoesNotContain("القاعِدَةُ لا تُبلَغ", line, StringComparison.Ordinal);
    }

    /// <summary>كانَ <c>Today_sqlstate_08P01_protocol_violation_reads_as_a_server_that_does_not_accept_connections</c> (‏3): صِنفُ 08
    /// بِرُموزِه الخَمسَة، وما سِواها مِنه شَكٌّ — ‏08P01 رِسالَةٌ لا يَفهَمُها الخادِم، و‏08007 نَتيجَةُ مُعامَلَةٍ مَجهولَة.</summary>
    [Theory]
    [InlineData("08P01", "bind message has 70000 parameter formats but 0 parameters")]
    [InlineData("08007", "transaction resolution unknown")]
    public void Class_08_codes_outside_the_named_five_are_code_breakage(string sqlState, string message)
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(Unhandled(new PostgresException(message, "ERROR", "ERROR", sqlState)));
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    /// <summary>كانَ <c>Today_a_boot_death_by_a_code_exception_after_an_earlier_transient_connect_line_reads_as_the_operators</c> (‏3):
    /// المَوتُ يُقرَأُ مِن الاستِثناءِ الَّذي قَتَلَ العَمَلِيَّة.</summary>
    [Fact]
    public void A_boot_death_is_read_from_the_exception_that_killed_it_not_from_an_earlier_transient_connect_line()
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(
            "[WRN] Npgsql.NpgsqlException (0x80004005): Failed to connect to 10.1.2.3:5432 (transient, retried…)\n"
            + Unhandled(new NullReferenceException("Object reference not set to an instance of an object.")));
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    /// <summary>والاتِّجاهُ الآخَرُ لِلتَضييقِ نَفسِه: سَطرٌ سابِقٌ بِرَمزٍ مِن الكود (‏42P01 مُلتَقَطٌ مُسَجَّل) لا يَحجُبُ سَبَبَ المَوتِ
    /// الحَقيقيّ — كانَت الدالَّةُ تَأخُذُ أَوَّلَ SqlState في السِجِلِّ فَتَقرَأُ هذا 1.</summary>
    [Fact]
    public void A_boot_death_by_a_database_that_does_not_serve_is_the_operators_whatever_sqlstate_an_earlier_line_logged()
    {
        if (NoBash(output)) return;
        var (code, line) = Verdict(
            "[WRN] [seed] handled: Npgsql.PostgresException (0x80004005): 42P01: relation \"x\" does not exist\n    SqlState: 42P01\n"
            + Unhandled(new PostgresException("too many connections for role \"gate_probe\"", "FATAL", "FATAL", "53300")));
        Assert.Equal(3, code);
        Assert.Contains("SqlState 53300", line, StringComparison.Ordinal);
    }

    /// <summary><b>القاعِدَةُ قيسَت ساقِطَةً بِنُقطَتِها</b> (‏503 ‏<c>"ready":false</c>) وسَطرُ قِياسِها يُسَمّي سَبَباً مِن المَعجَم ⇒ 3 —
    /// و‑٥ بِعَينِه (‏53000)، والقاعِدَةُ الَّتي لا تُبلَغ.</summary>
    [Fact]
    public void A_route_failure_while_readiness_measured_the_database_down_is_the_operators_named_by_the_measures_own_line()
    {
        if (NoBash(output)) return;
        var quota = RouteVerdict(
            ReadinessLine(new PostgresException(
                "Your account or project has exceeded the compute time quota. Upgrade your plan to increase limits.", "FATAL", "FATAL", "53000"))
            + "[12:00:01 ERR] An unhandled exception has occurred while executing the request.\n",
            "not_ready");
        Assert.Equal(3, quota.Code);
        Assert.Contains("SqlState 53000", quota.Line, StringComparison.Ordinal);

        var unreachable = RouteVerdict(
            ReadinessLine(new NpgsqlException("Failed to connect to 127.0.0.1:5999", new SocketException((int)SocketError.ConnectionRefused))),
            "not_ready");
        Assert.Equal(3, unreachable.Code);
        Assert.Contains("القاعِدَةُ لا تُبلَغ", unreachable.Line, StringComparison.Ordinal);
    }

    /// <summary>والسالِب: النُقطَةُ قاسَت «غَيرُ جاهِز» وسَطرُ قِياسِها رَمزٌ مِن الكود (استِعلامُها مَكسور) ⇒ 1 — وسَطرُ «Failed to connect»
    /// عابِرٌ في مَوضِعٍ آخَرَ مِن السِجِلِّ لا يُقرَأ: سُطورُ <c>[readiness]</c> وَحدَها.</summary>
    [Fact]
    public void A_readiness_measure_failing_by_a_code_sqlstate_is_code_breakage_whatever_else_the_log_holds()
    {
        if (NoBash(output)) return;
        var (code, line) = RouteVerdict(
            TransientConnectThenCodeFailure
            + ReadinessLine(new PostgresException("relation \"probe\" does not exist", "ERROR", "ERROR", "42P01")),
            "not_ready");
        Assert.Equal(1, code);
        Assert.Contains("كَسرٌ في الكود", line, StringComparison.Ordinal);
    }

    /// <summary>ولا قِياسَ ⇒ لا «عِندَ المُشَغِّل»: لَم تُسأَل النُقطَة، أَو ابتَلَعَتها صَفحَة (‏200 بِلا جَوابِها)، أَو ‏500، أَو لا رَدّ —
    /// ولَو كانَ في السِجِلِّ كُلُّ سُطورِ المَعجَم.</summary>
    [Theory]
    [InlineData("unasked")]
    [InlineData("200")]
    [InlineData("500")]
    [InlineData("000")]
    public void A_route_failure_without_the_gates_own_not_ready_measure_is_code_breakage(string readyAnswer)
    {
        if (NoBash(output)) return;
        var everyCause = TransientConnectThenCodeFailure
            + ReadinessLine(new PostgresException("quota", "FATAL", "FATAL", "53000"))
            + Unhandled(new PostgresException("too many connections", "FATAL", "FATAL", "53300"));
        var (code, line) = RouteVerdict(everyCause, readyAnswer);
        Assert.Equal(1, code);
        Assert.Contains("لَم يَقِس القاعِدَة", line, StringComparison.Ordinal);
    }

    /// <summary>والجَوابُ يُحفَظُ مِن جَدوَلِ المَساراتِ نَفسِه — <b>مُشَغَّلاً لا مَقروءاً</b>: <c>readiness_answer</c> تُقطَعُ وتُسأَلُ بِرَمزٍ
    /// وجِسمٍ حَقيقِيَّين؛ جَوابا <c>Readiness.cs</c> وَحدَهُما حالان، وما سِواهُما يُعادُ رَمزُه (والمُختَلِطُ — ‏503 بِـ<c>true</c> — لَيسَ قِياساً).
    /// والحَلقَةُ تُناديها لِـ<c>GET /health/ready</c> وَحدَه، و«لَم يُسأَل» قَبلَها — ووَسمُ السَطرِ الَّذي تَقرَؤُه البَوّابَةُ وَسمُ النُقطَةِ حَرفاً.</summary>
    [Theory]
    [InlineData("200", "{\"ready\":true}", "ready")]
    [InlineData("503", "{\"ready\":false}", "not_ready")]
    [InlineData("200", "<!DOCTYPE html><html dir=\"rtl\">…</html>", "200")]
    [InlineData("503", "{\"ready\":true}", "503")]
    [InlineData("200", "{\"ready\":false}", "200")]
    [InlineData("500", "", "500")]
    [InlineData("", "", "000")]
    public void The_gate_keeps_the_readiness_answer_from_its_own_route_table_and_greps_the_endpoints_own_tag(
        string actual, string body, string expected)
    {
        var gate = Gate();
        Assert.Contains("    \"GET|/health/ready|200\"", gate, StringComparison.Ordinal);
        var init = gate.IndexOf("READY_ANSWER=\"unasked\"", StringComparison.Ordinal);
        var loop = gate.IndexOf("for entry in \"${ROUTES[@]}\"; do", StringComparison.Ordinal);
        Assert.True(init > 0 && loop > init, "READY_ANSWER لا يَبدَأُ «لَم يُسأَل» قَبلَ حَلقَةِ المَسارات.");
        Assert.Contains("    if [ \"$key\" = \"GET|/health/ready\" ]; then\n        READY_ANSWER=\"$(readiness_answer \"$actual\" \"$body_file\")\"",
            gate, StringComparison.Ordinal);

        var readiness = File.ReadAllText(Path.Combine(RepoRoot, "libs", "core", "ACommerce.Platform.Hosting", "Readiness.cs"));
        Assert.Contains("ex => log.LogWarning(\"[readiness] ", readiness, StringComparison.Ordinal);
        Assert.Contains("new { ready = true }", readiness, StringComparison.Ordinal);
        Assert.Contains("new { ready = false }, statusCode: StatusCodes.Status503ServiceUnavailable", readiness, StringComparison.Ordinal);

        if (NoBash(output)) return;
        var dir = Directory.CreateTempSubdirectory("wasayel-ready-body-");
        try
        {
            var path = Path.Combine(dir.FullName, "body.txt");
            File.WriteAllText(path, body, new UTF8Encoding(false));
            var (code, line) = RunBash(RouteFunction() + "\nreadiness_answer \"$1\" \"$2\"", actual, path);
            output.WriteLine($"{(actual.Length == 0 ? "<لا رَدّ>" : actual)} · {body} → {line}");
            Assert.Equal(0, code);
            Assert.Equal(expected, line);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>وسَطرُ المَسارِ السّاقِطِ يَقولُ رَمزَه أَوَّلاً — مَقيسٌ حَيّاً: ‏<c>/health/ready</c> بِـ‏503 كانَ يُطبَعُ «الرَمزُ صَحيحٌ … المَسارُ
    /// مُبتَلَع» لِأَنّ فَحصَ الجِسمِ سُئِلَ قَبلَ الرَمز. فَالرَمزُ أَوَّلاً، والجِسمُ والرَأسُ حينَ يَصِحّ.</summary>
    [Fact]
    public void A_failed_route_names_its_wrong_status_before_any_body_or_header_reason()
    {
        var gate = Gate();
        var status = gate.IndexOf("        if [ \"$actual\" != \"$expected\" ]; then", StringComparison.Ordinal);
        var body = gate.IndexOf("        elif [ \"$body_ok\" != \"1\" ]; then", StringComparison.Ordinal);
        var header = gate.IndexOf("        elif [ \"$hdr_ok\" != \"1\" ]; then", StringComparison.Ordinal);
        Assert.True(status > 0 && body > status && header > body,
            $"تَرتيبُ أَسبابِ السُقوطِ: الرَمز {status} · الجِسم {body} · الرَأس {header} — الرَمزُ يُسأَلُ أَوَّلاً.");
        Assert.Contains("(المُتَوَقَّع %s)", gate[status..body], StringComparison.Ordinal);
    }

    /// <summary><b>ولا فَرعَ في طابِعِ السُقوطِ يَجعَلُه حارِسُه غَيرَ مَبلوغ</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ K): كُتلَةُ
    /// السُقوطِ لا تُدخَلُ إلّا والرَمزُ أَو الجِسمُ أَو الرَأسُ خاطِئ، والثَلاثَةُ مَسؤولَةٌ بِالتَرتيب — فَكانَ <c>else</c> رابِعٌ يُكَرِّرُ
    /// «(المُتَوَقَّع %s)» لا يُبلَغ. يُقرَأُ حارِسُ الكُتلَةِ بِحَرفِه، وسِلسِلَتُها ثَلاثَةُ فُروعٍ لا رابِعَ لَها، و«(المُتَوَقَّع %s)» مَرَّةً واحِدَة.</summary>
    [Fact]
    public void The_route_failure_printer_has_no_branch_its_guard_makes_unreachable()
    {
        var gate = Gate();
        var guard = gate.IndexOf("    if [ \"$actual\" = \"$expected\" ] && [ \"$body_ok\" = \"1\" ] && [ \"$hdr_ok\" = \"1\" ]; then", StringComparison.Ordinal);
        var failure = gate.IndexOf("\n    else\n", guard, StringComparison.Ordinal);
        var status = gate.IndexOf("        if [ \"$actual\" != \"$expected\" ]; then", failure, StringComparison.Ordinal);
        var snippet = gate.IndexOf("        echo \"      ─── مَقطَعٌ مِن الجِسم ───\"", status, StringComparison.Ordinal);
        Assert.True(guard > 0 && failure > guard && status > failure && snippet > status,
            $"كُتلَةُ السُقوط: الحارِس {guard} · else {failure} · الرَمز {status} · المَقطَع {snippet} — الأَداةُ عَمياء.");
        var chain = gate[status..snippet];
        var branches = Regex.Matches(chain, @"^        (if|elif|else|fi)\b", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        output.WriteLine($"فُروعُ طابِعِ السُقوط: {string.Join(" ", branches)}");
        Assert.Equal(new[] { "if", "elif", "elif", "fi" }, branches);
        Assert.Single(Regex.Matches(chain, Regex.Escape("(المُتَوَقَّع %s)")));
    }
}
