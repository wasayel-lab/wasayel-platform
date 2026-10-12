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

    [Fact]
    public void The_death_branch_and_the_route_branch_both_ask_the_one_verdict()
    {
        var gate = Gate();
        Assert.Equal(1, Regex.Matches(gate, @"^boot_verdict\(\) \{", RegexOptions.Multiline).Count);
        Assert.Equal(2, Regex.Matches(gate, Regex.Escape("boot_verdict \"$APP_LOG\"")).Count);
        Assert.Contains("        boot_verdict \"$APP_LOG\"\n        exit $?", gate, StringComparison.Ordinal);
        Assert.Contains("    exit \"$verdict\"", gate, StringComparison.Ordinal);
        Assert.True(gate.IndexOf(End, StringComparison.Ordinal) < gate.IndexOf("boot_verdict \"$APP_LOG\"", StringComparison.Ordinal),
            "الدالَّةُ تُعَرَّفُ بَعدَ أَوَّلِ نِداء.");
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
}
