using ACommerce.Templates.Customer.Marketplace.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — مَفاتيحُ VAPID إعدادٌ إنتاجيٌّ لا يُسَمّيه دَليلُ النَشر، وبِدونِها تَكسِرُ كُلُّ إعادَةِ تَشغيلٍ الاشتِراكاتِ القائِمَة (تَدقيقُ الإطلاق lens-docs-vs-code-1) ═══
//
// **المَقيس** (‏2026-10-25، عِندَ `2d20144`):
//   ١. ‏`WebPushService` يَقرَأُ `WebPush:Subject/PublicKey/PrivateKey`؛ وغِيابُ المِفتاحَينِ يُوَلِّدُ زَوجاً **في كُلِّ إنشاء** — والخِدمَةُ
//      `AddSingleton` فَإنشاءٌ واحِدٌ في كُلِّ عَمَلِيَّة: كُلُّ إقلاعٍ مِفتاحٌ عامٌّ جَديدٌ يُقَدِّمُه `/api/push/vapid-key`.
//   ٢. والمُتَصَفِّحُ لا يَشتَرِكُ مِن جَديد: `subscribeToPush` في `App.razor` يَأخُذُ `getSubscription()` القائِمَ (المَصنوعَ بِالمِفتاحِ السابِق)
//      ويُعيدُ إرسالَه؛ والمُرسِلُ لا يَمحو صَفّاً إلّا عَلى ‏404/410 — فَيَبقى الصَفُّ ولا يَصِلُ إشعارٌ، ولا يَقولُ ذلكَ شَيء.
//   ٣. وسَطرُ التَحذيرِ يَنصَحُ بِحِفظِها في `appsettings.Local.json` — **ولا يَقرَأُ المُضيفُ ذلكَ المِلَفّ** (لا `AddJsonFile` في
//      `Program.cs`)، ولا يَطبَعُ المِفتاحَ الخاصَّ فَلا يُحفَظُ الزَوجُ أَصلاً. والمُهَيَّأُ لا يَكتُبُ سَطراً.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفّانِ `Today_…` يَنقَلِبانِ بِأَسمائِهِما، والثَلاثَةُ الدائِمَةُ تَبقى حَرفاً.
//
// **العِلاج** (‏2026-10-25): سَطرُ الإقلاعِ بِرَمزِه `WebPushService.BootLogPrefix` — `configured` أَو `ephemeral` ومَعَه أَسماءُ المُتَغَيِّراتِ
// الثَلاثَةِ ودَليلُ النَشر (§٢ الخُطوَة ٦) لا المِلَفُّ المَيِّت؛ والكُتلَةُ `webpush_keys` في بَوّابَةِ الإقلاعِ تَقرَؤُه فَتُنَبِّهُ ولا تُفشِل؛
// وجَدوَلُ الدَليلِ يُسَمّي المُتَغَيِّراتِ بِحَرفِها ومَعَه أَمرُ تَوليدِ الزَوجِ — مَقيساً بِتَشغيلِه هُنا. والتَوليدُ عِندَ الغِيابِ كَما كان.
// الصَفّانِ انقَلَبا بِأَسمائِهِما (`The_missing_keys_warning_names_…` و`A_configured_service_logs_…`)، والثَلاثَةُ الدائِمَةُ كَما أُودِعَت.
public class WebPushVapidKeysTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private const string Tpl = "ACommerce.Templates.Customer.Marketplace";

    internal sealed class CapturingLogger : ILogger<WebPushService>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Add((logLevel, formatter(state, exception)));
    }

    private static IConfiguration Config(IDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static (WebPushService Service, CapturingLogger Log) Build(IDictionary<string, string?> values)
    {
        var log = new CapturingLogger();
        return (new WebPushService(Config(values), log), log);
    }

    private static readonly Dictionary<string, string?> NoKeys = new();

    // ─── الدائِمَة ──────────────────────────────────────────────────────────────────────────────

    /// <summary><b>بِلا مِفتاحَينِ زَوجٌ جَديدٌ في كُلِّ إنشاء</b> — وهذا ما يَجعَلُ كُلَّ إقلاعٍ مِفتاحاً عامّاً آخَر. يَبقى بِحَرفِه بَعدَ
    /// العِلاج: لا يُحفَظُ مِفتاحٌ في القاعِدَةِ ولا يُكتَبُ عَلى قُرص — العِلاجُ أَن يُضبَطَ ويُقال، لا أَن يَتَذَكَّرَه الخادِم.</summary>
    [Fact]
    public void Missing_keys_generate_a_fresh_pair_on_every_construction_so_every_restart_serves_another_public_key()
    {
        var (a, _) = Build(NoKeys);
        var (b, _) = Build(NoKeys);
        Assert.False(a.IsConfigured);
        Assert.False(b.IsConfigured);
        Assert.False(string.IsNullOrEmpty(a.PublicKey));
        Assert.NotEqual(a.PublicKey, b.PublicKey);
        // والخِدمَةُ وَحيدَةٌ في العَمَلِيَّة — إنشاءٌ واحِدٌ لِكُلِّ إقلاع.
        var mte = Read("libs", "templates", Tpl, "MarketplaceTemplateExtensions.cs");
        Assert.Contains("services.AddSingleton<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>();", mte, StringComparison.Ordinal);
    }

    /// <summary>والمَضبوطُ يُقَدَّمُ كَما كُتِب — مِن أَيِّ مَصدَرِ تَهيئَة، ومِنه مُتَغَيِّرُ البيئَة <c>WebPush__PublicKey</c>.</summary>
    [Fact]
    public void Configured_keys_are_served_as_given()
    {
        var pair = WebPush.VapidHelper.GenerateVapidKeys();
        var (svc, _) = Build(new Dictionary<string, string?>
        {
            ["WebPush:Subject"] = "mailto:owner@example.invalid",
            ["WebPush:PublicKey"] = pair.PublicKey,
            ["WebPush:PrivateKey"] = pair.PrivateKey,
        });
        Assert.True(svc.IsConfigured);
        Assert.Equal(pair.PublicKey, svc.PublicKey);
    }

    /// <summary><b>والمُتَصَفِّحُ يُعيدُ الاشتِراكَ القَديم، والمُرسِلُ لا يَمحوه إلّا عَلى ‏404/410</b> — فَمِفتاحٌ تَبَدَّلَ لا يُرى. يَبقى بِحَرفِه.</summary>
    [Fact]
    public void The_browser_reposts_its_old_subscription_and_the_sender_drops_a_row_only_on_404_or_410()
    {
        var app = Read("libs", "templates", Tpl, "Components", "App.razor");
        var subscribe = app[app.IndexOf("async function subscribeToPush(reg)", StringComparison.Ordinal)..];
        subscribe = subscribe[..subscribe.IndexOf("window.acEnableNotifications = async function", StringComparison.Ordinal)];
        Assert.Contains("var sub = await reg.pushManager.getSubscription();", subscribe, StringComparison.Ordinal);
        Assert.Contains("if (!sub) {", subscribe, StringComparison.Ordinal);
        Assert.Contains("'/push/subscribe'", subscribe, StringComparison.Ordinal);

        var svc = Read("libs", "templates", Tpl, "Services", "WebPushService.cs");
        Assert.Contains("ex.StatusCode == System.Net.HttpStatusCode.Gone ||", svc, StringComparison.Ordinal);
        Assert.Contains("ex.StatusCode == System.Net.HttpStatusCode.NotFound)", svc, StringComparison.Ordinal);
        Assert.Contains("_log.LogWarning(ex, \"Push send failed for user {UserId}\", userId);", svc, StringComparison.Ordinal);
    }

    // ─── انقَلَبا بِأَسمائِهِما (كانا `Today_the_missing_keys_warning_points_at_a_file_the_host_never_reads` و`Today_a_configured_service_logs_nothing`) ───

    /// <summary>التَحذيرُ يُسَمّي مُتَغَيِّراتِ البيئَةِ الثَلاثَةَ بِحَرفِها ودَليلَ النَشر، ويَبدَأُ بِرَمزِ سَطرِ الإقلاع — لا مِلَفّاً لا يَقرَؤُه
    /// المُضيف. ولا يَطبَعُ المِفتاحَ الخاصّ.</summary>
    [Fact]
    public void The_missing_keys_warning_names_the_three_environment_variables_and_the_deploy_section()
    {
        var (svc, log) = Build(NoKeys);
        var warning = Assert.Single(log.Lines);
        output.WriteLine($"{warning.Level} · {warning.Text}");
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.StartsWith(WebPushService.BootLogPrefix + WebPushService.EphemeralValue, warning.Text, StringComparison.Ordinal);
        foreach (var name in new[] { WebPushService.SubjectEnvVar, WebPushService.PublicKeyEnvVar, WebPushService.PrivateKeyEnvVar })
            Assert.Contains(name, warning.Text, StringComparison.Ordinal);
        Assert.Contains("docs/DEPLOY.md §2 step 6", warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("appsettings.Local.json", warning.Text, StringComparison.Ordinal);
        Assert.Contains(svc.PublicKey, warning.Text, StringComparison.Ordinal);
        var program = Read("apps", "V1.App", "Program.cs");
        Assert.DoesNotContain("AddJsonFile", program, StringComparison.Ordinal);
    }

    /// <summary>والمُهَيَّأُ يَكتُبُ سَطرَه — فَيُفَرِّقُ سِجِلُّ الإقلاعِ بَينَ «مَضبوط» و«لَم يُنشَأ بَعد». ولا مِفتاحَ فيه.</summary>
    [Fact]
    public void A_configured_service_logs_its_boot_line_and_no_key()
    {
        var pair = WebPush.VapidHelper.GenerateVapidKeys();
        var (_, log) = Build(new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = pair.PublicKey,
            ["WebPush:PrivateKey"] = pair.PrivateKey,
        });
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal(WebPushService.BootLogPrefix + WebPushService.ConfiguredValue, line.Text);
        Assert.DoesNotContain(pair.PrivateKey, line.Text, StringComparison.Ordinal);
    }

    // ─── الدَليلُ والبَوّابَة ────────────────────────────────────────────────────────────────────

    /// <summary>المُتَغَيِّراتُ الثَلاثَةُ كَما يُتَرجِمُها .NET إلى المَفاتيحِ الَّتي تَقرَؤُها الخِدمَة، ودَليلُ النَشرِ يُسَمّيها في صُفوفِ جَدوَلِه
    /// والخاصُّ سِرّاً.</summary>
    [Fact]
    public void The_deploy_guide_names_the_three_variables_as_the_service_reads_them()
    {
        Assert.Equal("WebPush:Subject", WebPushService.SubjectEnvVar.Replace("__", ":", StringComparison.Ordinal));
        Assert.Equal("WebPush:PublicKey", WebPushService.PublicKeyEnvVar.Replace("__", ":", StringComparison.Ordinal));
        Assert.Equal("WebPush:PrivateKey", WebPushService.PrivateKeyEnvVar.Replace("__", ":", StringComparison.Ordinal));
        var svc = Read("libs", "templates", Tpl, "Services", "WebPushService.cs");
        foreach (var read in new[] { "cfg.GetSection(\"WebPush\")", "section[\"Subject\"]", "section[\"PublicKey\"]", "section[\"PrivateKey\"]" })
            Assert.Contains(read, svc, StringComparison.Ordinal);

        var deploy = Read("docs", "DEPLOY.md");
        foreach (var name in new[] { WebPushService.SubjectEnvVar, WebPushService.PublicKeyEnvVar, WebPushService.PrivateKeyEnvVar })
            Assert.Contains($"| `{name}` |", deploy, StringComparison.Ordinal);
        var privateRow = deploy[deploy.IndexOf($"| `{WebPushService.PrivateKeyEnvVar}` |", StringComparison.Ordinal)..];
        privateRow = privateRow[..privateRow.IndexOf('\n')];
        Assert.EndsWith("| **Secret** |", privateRow.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("lens-docs-vs-code-1", deploy, StringComparison.Ordinal);
    }

    private const string RecipeStart = "```bash\n# ‏VAPID — زَوجٌ واحِدٌ عَلى P-256";

    private static string Recipe()
    {
        var deploy = Read("docs", "DEPLOY.md");
        var a = deploy.IndexOf(RecipeStart, StringComparison.Ordinal);
        Assert.True(a > 0, "أَمرُ تَوليدِ زَوجِ VAPID غائِبٌ عَن دَليلِ النَشر — الأَداةُ عَمياء.");
        Assert.Equal(a, deploy.LastIndexOf(RecipeStart, StringComparison.Ordinal));
        var body = deploy[(a + "```bash\n".Length)..];
        return body[..body.IndexOf("\n```", StringComparison.Ordinal)];
    }

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }

    /// <summary>
    /// <para><b>أَمرُ الدَليلِ يُشَغَّلُ بِحَرفِه</b> (القاعِدَة ١٦: لا يُكتَبُ أَمرٌ لَم يُقَس): يُطبَعُ سَطرانِ بِاسمَي المُتَغَيِّرَين، والزَوجُ
    /// بِطولَيه (‏65 و‏32 بايتاً)، و<c>WebPushService</c> يُقَدِّمُ العامَّ كَما هو، والخاصُّ يَختِمُ ما يَتَحَقَّقُ مِنه العامّ — زَوجٌ واحِدٌ لا
    /// مِفتاحانِ مُتَفَرِّقان. وزَوجٌ مَخلوطٌ (عامٌّ مِن تَشغيلٍ وخاصٌّ مِن آخَر) يُحمِرُّ التَحَقُّق — العَيِّنَةُ المَحقونَة.</para>
    /// </summary>
    [Fact]
    public void The_documented_recipe_yields_a_pair_the_service_serves_and_that_signs_and_verifies()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — لا bash مَعلومَ المَوضِع؛ يُقاسُ في CI (‏ubuntu)."); return; }
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var recipe = Recipe();
        Assert.Contains("openssl ecparam -name prime256v1", recipe, StringComparison.Ordinal);

        (string Pub, string Priv) RunOnce()
        {
            var dir = Directory.CreateTempSubdirectory("wasayel-vapid-");
            try
            {
                var script = Path.Combine(dir.FullName, "recipe.sh");
                File.WriteAllText(script, "set -euo pipefail\n" + recipe + "\n", new System.Text.UTF8Encoding(false));
                var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script })
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                Assert.True(p.WaitForExit(30_000), "الأَمرُ لَم يَنتَهِ في ثَلاثينَ ثانِيَة.");
                Assert.True(p.ExitCode == 0, $"الأَمرُ خَرَجَ بِـ{p.ExitCode}: {stderr}");
                var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                Assert.Equal(2, lines.Length);
                Assert.StartsWith(WebPushService.PublicKeyEnvVar + "=", lines[0], StringComparison.Ordinal);
                Assert.StartsWith(WebPushService.PrivateKeyEnvVar + "=", lines[1], StringComparison.Ordinal);
                return (lines[0][(WebPushService.PublicKeyEnvVar.Length + 1)..], lines[1][(WebPushService.PrivateKeyEnvVar.Length + 1)..]);
            }
            finally { dir.Delete(recursive: true); }
        }

        var (pub, priv) = RunOnce();
        output.WriteLine($"العامّ: {pub.Length} مِحرَفاً · الخاصّ: {priv.Length} مِحرَفاً (لا يُطبَع)");
        Assert.Equal(87, pub.Length);
        Assert.Equal(43, priv.Length);
        var q = FromBase64Url(pub);
        var d = FromBase64Url(priv);
        Assert.Equal(65, q.Length);
        Assert.Equal(0x04, q[0]);
        Assert.Equal(32, d.Length);

        var (svc, _) = Build(new Dictionary<string, string?>
        {
            ["WebPush:Subject"] = "mailto:owner@example.invalid",
            ["WebPush:PublicKey"] = pub,
            ["WebPush:PrivateKey"] = priv,
        });
        Assert.True(svc.IsConfigured);
        Assert.Equal(pub, svc.PublicKey);

        static bool Signs(byte[] d, byte[] q)
        {
            var curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256;
            var point = new System.Security.Cryptography.ECPoint { X = q[1..33], Y = q[33..65] };
            var data = "wasayel-vapid-probe"u8.ToArray();
            byte[] sig;
            try
            {
                using var signer = System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters { Curve = curve, D = d, Q = point });
                sig = signer.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256);
            }
            catch (System.Security.Cryptography.CryptographicException) { return false; }
            using var verifier = System.Security.Cryptography.ECDsa.Create(new System.Security.Cryptography.ECParameters { Curve = curve, Q = point });
            return verifier.VerifyData(data, sig, System.Security.Cryptography.HashAlgorithmName.SHA256);
        }

        Assert.True(Signs(d, q), "الخاصُّ لا يَختِمُ ما يَتَحَقَّقُ مِنه العامّ — الأَمرُ لا يُخرِجُ زَوجاً.");
        // والعَيِّنَةُ المَحقونَة: خاصٌّ مِن تَشغيلٍ آخَرَ مَعَ عامِّ الأَوَّل — المُتَحَقِّقُ يَحمَرّ.
        var (_, otherPriv) = RunOnce();
        Assert.False(Signs(FromBase64Url(otherPriv), q), "المُتَحَقِّقُ أَعمى: زَوجٌ مَخلوطٌ مَرَّ.");
    }

    private const string GateStart = "# ─── بِدايَةُ webpush_keys ───";
    private const string GateEnd = "# ─── نِهايَةُ webpush_keys ───";

    private (int Code, string Text) RunGateBlock(string log)
    {
        var gate = Read("scripts", "verify-production-boot.sh");
        var a = gate.IndexOf(GateStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ webpush_keys غائِبَة.");
        Assert.Equal(a, gate.LastIndexOf(GateStart, StringComparison.Ordinal));
        var b = gate.IndexOf(GateEnd, a, StringComparison.Ordinal);
        Assert.True(b > a);
        var fn = gate[a..(b + GateEnd.Length)];
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-webpush-keys-");
        try
        {
            var logPath = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(logPath, log, new System.Text.UTF8Encoding(false));
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + fn + "\nwebpush_keys \"$1\"\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, logPath })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000));
            output.WriteLine($"→ {p.ExitCode} · {text.Trim()}");
            return (p.ExitCode, text.Trim());
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary>سَطرُ السِجِلِّ كَما تَكتُبُه الخِدمَةُ نَفسُها — لا مَنسوخاً.</summary>
    private static string LoggedBy(IDictionary<string, string?> values, string level)
    {
        var (_, log) = Build(values);
        return $"[06:48:23 {level}] " + Assert.Single(log.Lines).Text + "\n";
    }

    /// <summary>البَوّابَةُ تَطلُبُ <c>/api/push/vapid-key</c> (فَتُنشَأُ الخِدمَة) وتُنادي الكُتلَةَ مَرَّةً واحِدَةً قَبلَ الحُكم.</summary>
    [Fact]
    public void The_boot_gate_constructs_the_service_through_its_route_table_and_calls_the_block_once()
    {
        var gate = Read("scripts", "verify-production-boot.sh");
        Assert.Contains("\"GET|/api/push/vapid-key|200\"", gate, StringComparison.Ordinal);
        Assert.Equal(1, gate.Split("\nwebpush_keys \"$APP_LOG\"").Length - 1);
        Assert.True(gate.IndexOf("\nwebpush_keys \"$APP_LOG\"", StringComparison.Ordinal)
                    < gate.IndexOf("if [ \"$ROUTE_FAILED\" -ne 0 ]; then", StringComparison.Ordinal),
            "الكُتلَةُ تُنادى بَعدَ الحُكم — لا تُطبَعُ حينَ يَسقُطُ مَسار.");
        var mte = Read("libs", "templates", Tpl, "MarketplaceTemplateExtensions.cs");
        Assert.Contains("app.MapGet(\"/api/push/vapid-key\",", mte, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_gate_names_the_missing_vapid_keys_and_keeps_its_exit_code()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (code, text) = RunGateBlock("[06:48:20 INF] Now listening on: http://127.0.0.1:5171\n" + LoggedBy(NoKeys, "WRN"));
        Assert.Equal(0, code);
        Assert.StartsWith("⚠ مَفاتيحُ VAPID غائِبَة — ephemeral", text, StringComparison.Ordinal);
        foreach (var name in new[] { WebPushService.SubjectEnvVar, WebPushService.PublicKeyEnvVar, WebPushService.PrivateKeyEnvVar })
            Assert.Contains(name, text, StringComparison.Ordinal);
        Assert.Contains("DEPLOY.md §٢ الخُطوَة ٦", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_gate_says_configured_without_a_warning_when_the_keys_are_set()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var pair = WebPush.VapidHelper.GenerateVapidKeys();
        var (code, text) = RunGateBlock(LoggedBy(new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = pair.PublicKey,
            ["WebPush:PrivateKey"] = pair.PrivateKey,
        }, "INF"));
        Assert.Equal(0, code);
        Assert.StartsWith("مَفاتيحُ VAPID: مَضبوطَة", text, StringComparison.Ordinal);
        Assert.DoesNotContain("⚠", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_boot_gate_says_unmeasured_when_the_service_wrote_no_line()
    {
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — يُقاسُ في CI (‏ubuntu)."); return; }
        var (code, text) = RunGateBlock("[06:48:20 INF] Now listening on: http://127.0.0.1:5171\n");
        Assert.Equal(0, code);
        Assert.StartsWith("⚠ مَفاتيحُ VAPID: لَم تُقَس", text, StringComparison.Ordinal);
    }
}
