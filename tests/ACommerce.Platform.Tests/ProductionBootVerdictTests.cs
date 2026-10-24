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
}
