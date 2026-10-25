using System.Text.RegularExpressions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ البَراهينُ الحَيَّةُ تُشَغَّلُ في CI عَلى Postgres الوَظيفَة — ولا تَمُرُّ عَمياء (‏2026-10-25، تَدقيقُ الإطلاق lens-checkers-ci-2، كُبرى) ═══
//
// **المَقيسُ قَبل** (`e7cb90d`): كُلُّ بُرهانٍ حَيٍّ يَبدَأُ بِـ`if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;`،
// و`ci.yml` لا يَضبِطُ `WASAYEL_LIVE_PROOF` ولا قاعِدَة — فَالبَراهينُ الخَمسونَ ونَيِّفٌ «ناجِحَةٌ» في كُلِّ تَشغيلٍ وهي لا تَفحَصُ شَيئاً.
// والطَفرَةُ M11 (إسقاطُ النُسخَةِ المُتَوَقَّعَةِ في `SubscriptionEntitlements.ConsumeAsync`) — مَقيسَةً هُنا عَلى نُسخَةٍ في المُسَوَّدَة:
// الحَقيبَةُ بِلا قاعِدَةٍ ‏4655/0، و`LiveQuotaRaceProofTests` عَلى Postgres مَحَلِّيٍّ فارِغٍ أَحمَرُ في جَولَتَينِ مِن أَربَع
// («فائِزون=2 … Expected: 1 Actual: 2» — إعلانانِ عَلى حِصَّةٍ واحِدَة) وأَخضَرُ في اثنَتَينِ (يَصطَدِمُ الإلحاقانِ بِـ`pk_mt_events_stream_and_version`
// فَيَخسَرُ أَحَدُهُما بِلا النُسخَة). **فَالبُرهانُ يُمسِكُ الطَفرَةَ في نِصفِ الجَولاتِ لا كُلِّها — وفي CI قَبلَ اليَومِ في صِفرِها.**
//
// **وصارَ**: وَظيفَةُ `build-and-test` تَحمِلُ `services: postgres` وخُطوَةَ اختِبارٍ ثانِيَةً بِبيئَتِها وَحدَها (`WASAYEL_LIVE_PROOF=1`،
// سِلسِلَةٌ إلى `localhost`، و`CI_LIVE_PROOF_REQUIRED=1`)، مُرَشَّحَةً عَلى `…Tests.Live` وصَفِّ الجاهِزِيَّةِ الحَيِّ وبِلا `LiveTenantExportProofTests`
// (يَلزَمُه مَتجَرٌ مَملوكٌ لا يَبذُرُه شَيءٌ في قاعِدَةٍ فارِغَة — مَقيسٌ: ‏55 مِن 56 أَخضَرُ عَلى قاعِدَةٍ فارِغَةٍ في 15 ثانِيَة، والأَحمَرُ هُوَ).
// **والصَفّانِ هُنا حارِساها**: الأَوَّلُ يَقرَأُ `ci.yml` فَيَحمَرُّ إن سَقَطَت الخُطوَةُ أَو خِدمَتُها أَو بيئَتُها؛ والثاني يَعمَلُ في الخُطوَةِ نَفسِها
// فَيَحمَرُّ إن دَخَلَها تَشغيلٌ بِلا عَلَمٍ أَو بِلا قاعِدَةٍ تُجيب — فَلا تَعودُ «ناجِحَةً» وهي عَمياء. وفي التَشغيلِ العادِيِّ يَطبَعُ أَنَّه لَيسَ فيها ويَرجِع.

public class LiveProofJobTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary><c>null</c> إن كانَت البيئَةُ بيئَةَ بُرهانٍ حَيٍّ مُبصِر، أَو لَيسَت مُطالَبَةً بِذلك؛ وإلّا رَمزُ العَمى:
    /// <c>flag_off</c> (المُطالَبَةُ مَضبوطَةٌ والعَلَمُ لا) · <c>no_connection</c> (لا سِلسِلَة).</summary>
    internal static string? Blindness(Func<string, string?> env) =>
        env("CI_LIVE_PROOF_REQUIRED") != "1" ? null
        : env("WASAYEL_LIVE_PROOF") != "1" ? "flag_off"
        : string.IsNullOrWhiteSpace(env("ConnectionStrings__Postgres")) ? "no_connection"
        : null;

    [Fact]
    public void Every_blindness_code_goes_red_on_its_injected_environment()
    {
        Func<string, string?> Env(params (string K, string V)[] kv) => k => kv.FirstOrDefault(p => p.K == k).V;
        var cases = new (string Name, Func<string, string?> Env, string? Code)[]
        {
            ("خارِجَ الخُطوَةِ الحَيَّة", Env(), null),
            ("مُطالَبَةٌ بِلا عَلَم", Env(("CI_LIVE_PROOF_REQUIRED", "1"), ("ConnectionStrings__Postgres", "Host=localhost")), "flag_off"),
            ("مُطالَبَةٌ وعَلَمٌ بِغَيرِ 1", Env(("CI_LIVE_PROOF_REQUIRED", "1"), ("WASAYEL_LIVE_PROOF", "true"), ("ConnectionStrings__Postgres", "Host=localhost")), "flag_off"),
            ("مُطالَبَةٌ بِلا سِلسِلَة", Env(("CI_LIVE_PROOF_REQUIRED", "1"), ("WASAYEL_LIVE_PROOF", "1")), "no_connection"),
            ("مُبصِرَة", Env(("CI_LIVE_PROOF_REQUIRED", "1"), ("WASAYEL_LIVE_PROOF", "1"), ("ConnectionStrings__Postgres", "Host=localhost")), null),
        };
        var wrong = cases.Where(c => Blindness(c.Env) != c.Code).Select(c => $"«{c.Name}»: {Blindness(c.Env) ?? "null"} لا {c.Code ?? "null"}").ToList();
        foreach (var c in cases) output.WriteLine($"«{c.Name}» → {Blindness(c.Env) ?? "مُبصِرَةٌ أَو غَيرُ مُطالَبَة"}");
        output.WriteLine($"حُقِنَ {cases.Length} بيئَةً؛ أَخطَأَ الفاحِصُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary><b>في الخُطوَةِ الحَيَّةِ: العَلَمُ مَضبوطٌ والقاعِدَةُ تُجيب</b> — فَالبَراهينُ بَعدَه لا تَرجِعُ مِن حارِسِها. وخارِجَها يَطبَعُ ذلكَ ويَرجِع.</summary>
    [Fact]
    public async Task The_live_step_reaches_its_database()
    {
        if (Environment.GetEnvironmentVariable("CI_LIVE_PROOF_REQUIRED") != "1")
        {
            output.WriteLine("لَيسَ في الخُطوَةِ الحَيَّة (CI_LIVE_PROOF_REQUIRED غائِب) — لا قاعِدَةَ تُطلَب.");
            return;
        }
        var blind = Blindness(Environment.GetEnvironmentVariable);
        Assert.True(blind is null, $"الخُطوَةُ الحَيَّةُ عَمياء ({blind}) — كُلُّ بُرهانٍ بَعدَها يَرجِعُ مِن حارِسِه أَخضَر.");

        await using var conn = new NpgsqlConnection(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select 1", conn);
        var one = await cmd.ExecuteScalarAsync();
        output.WriteLine($"القاعِدَةُ أَجابَت: select 1 = {one} · الخادِم {conn.PostgreSqlVersion}");
        Assert.Equal(1, Convert.ToInt32(one));
    }

    // ─── الخُطوَةُ في ci.yml ────────────────────────────────────────────────────────────────────────────────

    /// <summary>خُروقُ «البَراهينُ الحَيَّةُ في CI» — الأَسطُرُ المُعَلَّقَةُ لا تُعَدّ. الرُموز: <c>no_live_step</c> · <c>no_sentinel</c>
    /// (الخُطوَةُ بِلا <c>CI_LIVE_PROOF_REQUIRED</c>) · <c>connection_not_local</c> · <c>filter_misses_live</c> · <c>no_postgres_service</c>
    /// (وَظيفَتُها بِلا خِدمَةِ postgres).</summary>
    internal static IReadOnlyList<string> LiveStepBreaches(string workflow)
    {
        var lines = workflow.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select((text, i) => (Text: text, I: i)).Where(l => !l.Text.TrimStart().StartsWith('#')).ToList();
        var flag = lines.FirstOrDefault(l => Regex.IsMatch(l.Text, @"^\s+WASAYEL_LIVE_PROOF:\s*""?1""?\s*$"));
        if (flag.Text is null) return ["no_live_step"];

        var jobStarts = lines.Where(l => Regex.IsMatch(l.Text, @"^  [\w-]+:\s*$")).Select(l => l.I).ToList();
        var jobFrom = jobStarts.Where(j => j < flag.I).DefaultIfEmpty(-1).Max();
        var jobTo = jobStarts.Where(j => j > flag.I).DefaultIfEmpty(int.MaxValue).Min();
        var stepStarts = lines.Where(l => l.I > jobFrom && l.I < jobTo && Regex.IsMatch(l.Text, @"^      - ")).Select(l => l.I).ToList();
        var stepFrom = stepStarts.Where(s => s < flag.I).DefaultIfEmpty(jobFrom).Max();
        var stepTo = stepStarts.Where(s => s > flag.I).DefaultIfEmpty(jobTo).Min();
        var step = lines.Where(l => l.I >= stepFrom && l.I < stepTo).Select(l => l.Text).ToList();
        var job = lines.Where(l => l.I > jobFrom && l.I < jobTo).Select(l => l.Text).ToList();

        var breaches = new List<string>();
        if (!step.Any(l => Regex.IsMatch(l, @"^\s+CI_LIVE_PROOF_REQUIRED:\s*""?1""?\s*$"))) breaches.Add("no_sentinel");
        if (!step.Any(l => Regex.IsMatch(l, @"^\s+ConnectionStrings__Postgres:.*Host=localhost;"))) breaches.Add("connection_not_local");
        if (!step.Any(l => l.Contains("FullyQualifiedName~ACommerce.Platform.Tests.Live", StringComparison.Ordinal))) breaches.Add("filter_misses_live");
        // ‏كُتلَةُ `services:` في الوَظيفَة: أَسطُرُها بَعدَها ما دامَت أَعمَقَ مِن مِفتاحِها.
        var services = job.FindIndex(l => Regex.IsMatch(l, @"^    services:\s*$"));
        var block = services < 0 ? [] : job.Skip(services + 1).TakeWhile(l => l.Trim().Length == 0 || l.StartsWith("      ", StringComparison.Ordinal)).ToList();
        if (!block.Any(l => Regex.IsMatch(l, @"^\s+image:\s*postgres:\d+"))) breaches.Add("no_postgres_service");
        return breaches;
    }

    private static string Ci() =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Ci_runs_the_live_proofs_on_its_own_postgres_and_requires_them_to_see_it()
    {
        var ci = Ci();
        Assert.True(ci.Length > 2000, "أَداةٌ عَمياء: ci.yml لَم يُقرَأ.");
        var breaches = LiveStepBreaches(ci);
        var filter = Regex.Match(ci, @"--filter ""(?<f>[^""]*Tests\.Live[^""]*)""");
        output.WriteLine($"الخُطوَةُ الحَيَّة: خُروق {breaches.Count}{(breaches.Count > 0 ? " — " + string.Join(", ", breaches) : "")} · المُرَشِّح: {filter.Groups["f"].Value}");
        Assert.Empty(breaches);
        // ‏والمُستَثنى مُسَمّىً بِسَبَبِه لا مَسكوتٌ عَنه، والصَفُّ الحَيُّ خارِجَ عَقدِ الأَسماءِ داخِلُه.
        Assert.Contains("FullyQualifiedName!~LiveTenantExportProofTests", filter.Groups["f"].Value, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~ReadinessEndpointTests.Live_database_answers_200_ready", filter.Groups["f"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_step_checker_can_actually_go_red()
    {
        var ci = Ci();
        Assert.Empty(LiveStepBreaches(ci));
        static string Once(string text, string from, string to)
        {
            var at = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, $"عَيِّنَةُ الحَقنِ غائِبَة: {from}");
            return text[..at] + to + text[(at + from.Length)..];
        }
        var cases = new (string Name, string Text, string Code)[]
        {
            ("لا خُطوَةَ حَيَّة", Once(ci, "WASAYEL_LIVE_PROOF: \"1\"", "WASAYEL_LIVE_PROOF: \"0\""), "no_live_step"),
            ("الخُطوَةُ بِلا حارِسِها", Once(ci, "CI_LIVE_PROOF_REQUIRED: \"1\"", "CI_LIVE_PROOF_REQUIRED: \"0\""), "no_sentinel"),
            ("قاعِدَةٌ بَعيدَة", Once(ci, "Host=localhost;Port=5432;", "Host=ep-x.neon.tech;"), "connection_not_local"),
            ("مُرَشِّحٌ لا يَبلُغُ الحَيّ", Once(ci, "FullyQualifiedName~ACommerce.Platform.Tests.Live&", "FullyQualifiedName~ACommerce.Platform.Tests.Unit&"), "filter_misses_live"),
            ("بِلا خِدمَة", Once(ci, "image: postgres:", "image: redis:"), "no_postgres_service"),
        };
        var missed = new List<string>();
        foreach (var c in cases)
        {
            var got = LiveStepBreaches(c.Text);
            output.WriteLine($"«{c.Name}» → {string.Join(", ", got)}");
            if (!got.Contains(c.Code)) missed.Add($"«{c.Name}»: لا {c.Code} ({string.Join(", ", got)})");
        }
        output.WriteLine($"حُقِنَ {cases.Length} شَكلاً؛ فاتَ الفاحِصَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }
}
