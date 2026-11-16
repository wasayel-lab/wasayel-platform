using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ «أَتُجيبُ القاعِدَة؟» — `/health/ready` أُختُ `/health` لا بَديلُها (‏2026-10-12، موجزُ الفجوات P3-4) ══════════════
//
// **العِلَّةُ المَقيسَة**: V1.App بِوَضعِ Production، والقاعِدَةُ تُغلَقُ تَحتَه — `/` ‏500، و`/health` ‏200 بِبَصمَةٍ صَحيحَة
// (سيناريو و‑٥). و`/health` لا يُمَسّ: هُوِيَّةٌ وحَياة بِنَصِّ ‏ADR-019 §٥، ووَظيفَةُ النَشرِ تَستَطلِعُه.
//
// **وما يُقاسُ هُنا**: (١) الحُكمُ المُخَزَّنُ بِساعَةٍ يَدَوِيَّةٍ واستِطلاعٍ يَعُدُّ نَفسَه — مَرَّةً في النافِذَة، وواحِدٌ في
// الطَيَران، والسُقوطُ مُخَزَّنٌ كَالنَجاح، والمُعَلَّقُ يُقطَع، والمُنصَرِفُ لا يَكتُبُ حُكماً؛ (٢) عَلى HTTP حَقيقيّ بِسِلسِلَةٍ
// إلى مَنفَذٍ مُغلَق: ‏503 مَعَ `Retry-After` و`/health` ‏200 في اللَحظَةِ نَفسِها؛ (٣) الأَخضَرُ عَلى Postgres حَيّ بِعَقدِ
// `Live*ProofTests`؛ (٤) المَسارُ وسَبَبُه، والتَركيبُ، والبَوّابَةُ الإنتاجِيَّة.

file sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

file sealed class ReadyHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly DocumentStore _store;

    public HttpClient Client { get; }

    private ReadyHost(WebApplication app, DocumentStore store, HttpClient client)
    {
        _app = app;
        _store = store;
        Client = client;
    }

    internal static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>مُضيفٌ يَحمِلُ <c>/health</c> و<c>/health/ready</c> عَلى مَخزَنِ Marten بِالسِلسِلَةِ المُعطاة.</summary>
    public static async Task<ReadyHost> StartAsync(string connectionString)
    {
        for (var attempt = 1; ; attempt++)
        {
            var store = DocumentStore.For(o =>
            {
                o.Connection(connectionString);
                o.DatabaseSchemaName = "platform";
            });

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDocumentStore>(store);

            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.MapBuildIdentity("1.0.0+" + new string('a', 40), new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero));
            app.MapReadiness();

            try
            {
                await app.StartAsync();
            }
            catch (IOException) when (attempt < 5)
            {
                await app.DisposeAsync();
                store.Dispose();
                continue;
            }

            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(30),
            };
            return new ReadyHost(app, store, client);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _store.Dispose();
    }
}

public class ReadinessEndpointTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(parts)));
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {Path.Combine(parts)} لَم يُقرَأ.");
        return text;
    }

    /// <summary>سِلسِلَةٌ إلى مَنفَذٍ مَحَلِّيٍّ لا يَستَمِعُ عَلَيه أَحَد — رَفضٌ فَوريٌّ بِلا شَبَكَةٍ ولا انتِظار.</summary>
    private static string Unreachable() =>
        $"Host=127.0.0.1;Port={ReadyHost.FreePort()};Database=nope;Username=nope;Password=nope;Timeout=3;Pooling=false";

    // ─── ١) الحُكمُ المُخَزَّن ───────────────────────────────────────────

    [Fact]
    public async Task One_probe_answers_every_caller_inside_the_window()
    {
        var clock = new ManualClock();
        var verdicts = new ReadinessVerdicts(TimeSpan.FromSeconds(5), clock);
        var probes = 0;
        Task<bool> Probe(CancellationToken _) { probes++; return Task.FromResult(true); }

        for (var i = 0; i < 20; i++)
            Assert.True(await verdicts.ReadyAsync(Probe, null, CancellationToken.None));
        Assert.Equal(1, probes);

        clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.True(await verdicts.ReadyAsync(Probe, null, CancellationToken.None));
        Assert.Equal(1, probes);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(await verdicts.ReadyAsync(Probe, null, CancellationToken.None));
        Assert.Equal(2, probes);
    }

    /// <summary><b>والسُقوطُ يُخَزَّنُ كَالنَجاح</b> — وإلّا صارَ كُلُّ استِطلاعٍ في الانقِطاعِ مُحاوَلَةَ اتِّصالٍ جَديدَةً عَلى
    /// قاعِدَةٍ تَختَنِق: النُقطَةُ العامَّةُ بابٌ عَلى Neon في أَسوَأِ لَحظَة.</summary>
    [Fact]
    public async Task An_outage_is_measured_once_per_window_not_once_per_caller()
    {
        var clock = new ManualClock();
        var verdicts = new ReadinessVerdicts(TimeSpan.FromSeconds(5), clock);
        var probes = 0;
        var logged = new List<Exception>();
        Task<bool> Probe(CancellationToken _) { probes++; throw new InvalidOperationException("53300"); }

        for (var i = 0; i < 20; i++)
            Assert.False(await verdicts.ReadyAsync(Probe, logged.Add, CancellationToken.None));

        Assert.Equal(1, probes);
        Assert.Single(logged);
        Assert.Equal("53300", logged[0].Message);
    }

    [Fact]
    public async Task A_probe_that_answers_false_is_not_ready()
    {
        var verdicts = new ReadinessVerdicts(TimeSpan.FromSeconds(5), new ManualClock());
        Assert.False(await verdicts.ReadyAsync(_ => Task.FromResult(false), null, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_callers_share_one_probe_in_flight()
    {
        var verdicts = new ReadinessVerdicts(TimeSpan.FromSeconds(5), new ManualClock());
        var probes = 0;
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Probe(CancellationToken _) { Interlocked.Increment(ref probes); return await release.Task; }

        var callers = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => verdicts.ReadyAsync(Probe, null, CancellationToken.None)))
            .ToArray();
        await Task.Delay(200);
        release.SetResult(true);

        Assert.All(await Task.WhenAll(callers), Assert.True);
        Assert.Equal(1, probes);
    }

    /// <summary>استِطلاعٌ مُعَلَّقٌ يُقطَعُ عِندَ النافِذَة — مُراقِبٌ يَنتَظِرُ ‏15 ثانِيَةً (مُهلَةُ Npgsql الافتِراضِيَّة) حُكمٌ
    /// مُتَأَخِّرٌ لا حُكم.</summary>
    [Fact]
    public async Task A_hanging_probe_is_cut_at_the_window_and_reads_not_ready()
    {
        var verdicts = new ReadinessVerdicts(TimeSpan.FromMilliseconds(300), TimeProvider.System);
        var clock = Stopwatch.StartNew();

        var ready = await verdicts.ReadyAsync(
            async ct => { await Task.Delay(Timeout.Infinite, ct); return true; }, null, CancellationToken.None);

        Assert.False(ready);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"القَطعُ تَأَخَّرَ: {clock.Elapsed}.");
    }

    /// <summary>سائِلٌ انصَرَفَ لا يَكتُبُ حُكماً — انصِرافُه لا يَقولُ شَيئاً عَن القاعِدَة، والتالي يَستَطلِع.</summary>
    [Fact]
    public async Task A_caller_that_leaves_writes_no_verdict()
    {
        var verdicts = new ReadinessVerdicts(TimeSpan.FromSeconds(5), new ManualClock());
        var probes = 0;
        using var gone = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verdicts.ReadyAsync(
            async ct => { probes++; gone.Cancel(); await Task.Delay(Timeout.Infinite, ct); return true; },
            null, gone.Token));

        Assert.True(await verdicts.ReadyAsync(_ => { probes++; return Task.FromResult(true); }, null, CancellationToken.None));
        Assert.Equal(2, probes);
    }

    // ─── ٢) عَلى HTTP — قاعِدَةٌ لا تُجيب ───────────────────────────────────

    /// <summary><b>سيناريو و‑٥ مَقيساً عَلى مُضيفٍ واحِد</b>: <c>/health</c> ‏200 بِبَصمَتِه، و<c>/health/ready</c> ‏503 مَعَ
    /// <c>Retry-After</c> و<c>no-store</c> وجِسمٍ يَقولُ ذلك — في اللَحظَةِ نَفسِها.</summary>
    [Fact]
    public async Task An_unreachable_database_answers_503_while_health_stays_200()
    {
        await using var host = await ReadyHost.StartAsync(Unreachable());

        using var ready = await host.Client.GetAsync(Readiness.Path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("5", ready.Headers.RetryAfter?.ToString());
        Assert.Contains("no-store", ready.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        Assert.Equal("application/json", ready.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"ready\":false}", await ready.Content.ReadAsStringAsync());

        using var health = await host.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("\"commit\":\"" + new string('a', 40) + "\"", await health.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Head_answers_the_same_verdict()
    {
        await using var host = await ReadyHost.StartAsync(Unreachable());
        using var res = await host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Readiness.Path));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("5", res.Headers.RetryAfter?.ToString());
    }

    // ─── ٣) الأَخضَر — Postgres حَيّ ───────────────────────────────────────

    [Fact]
    public async Task Live_database_answers_200_ready()
    {
        if (!LiveDealConcurrencyProofTests.Enabled || string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString))
            return;

        await using var host = await ReadyHost.StartAsync(LiveDealConcurrencyProofTests.ConnectionString!);
        using var res = await host.Client.GetAsync(Readiness.Path);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("{\"ready\":true}", await res.Content.ReadAsStringAsync());
        Assert.Null(res.Headers.RetryAfter);
        Assert.Contains("no-store", res.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
    }

    // ─── ٤) المَسارُ وسَبَبُه، والتَركيب، والبَوّابَة ────────────────────────

    /// <summary><b>لِماذا <c>/health/ready</c> لا <c>/ready</c> الَّذي رَسَمَه ‏ADR-019 §٥</b> — مَقيساً بِالدالَّةِ الَّتي
    /// يُناديها الوَسيط: المَقطَعُ المَحجوزُ لا يُسأَلُ عَنه، و<c>ready</c> يُسأَلُ عَنه القاعِدَةَ قَبلَ النُقطَة.</summary>
    [Fact]
    public void The_path_sits_under_the_reserved_health_segment_so_no_tenant_lookup_precedes_it()
    {
        Assert.Equal("/health/ready", Readiness.Path);
        Assert.Contains("health", ReservedTenantSlugs.All);
        Assert.Null(TenantResolverMiddleware.SlugFromPath(Readiness.Path));
        Assert.Equal("ready", TenantResolverMiddleware.SlugFromPath("/ready"));
    }

    /// <summary><b>والتَسجيلُ يَكتُبُ المَسارَ حَرفاً فَتَراهُ الماسِحاتُ</b> — مَقيس: كُتِبَ أَوَّلاً بِالثابِتِ فَمَرَّ عَدّادُ النَزيفِ
    /// وحارِسُ الكِتابَةِ وحَقنُ المَخزَنِ خُضراً <b>والنُقطَةُ غَيرُ مَرئِيَّةٍ لَها</b> (تَقرَأُ <c>.Map…("…"</c>). والآنَ تُعَدُّ —
    /// وهذا يَشتَرِطُ أَنَّها مَعدودَة، لا أَنَّ النَصَّ يُشبِهُ ذلك.</summary>
    [Fact]
    public void The_registration_spells_the_path_so_the_endpoint_scanners_count_it()
    {
        var src = Read("libs", "core", "ACommerce.Platform.Hosting", "Readiness.cs");
        Assert.Contains($"MapMethods(\"{Readiness.Path}\"", src, StringComparison.Ordinal);

        var seen = WriteEndpointGuardTests.AllMinimalApiEndpoints().ToList();
        Assert.True(seen.Count >= 99, $"أَداةٌ عَمياء: {seen.Count} نُقطَة.");
        Assert.Single(seen, e => e.Route == Readiness.Path
                                 && e.File == "libs/core/ACommerce.Platform.Hosting/Readiness.cs");
    }

    [Fact]
    public void Program_maps_readiness_next_to_the_build_identity()
    {
        var program = Read("apps", "V1.App", "Program.cs");
        var identity = program.IndexOf("app.MapBuildIdentity(", StringComparison.Ordinal);
        var ready = program.IndexOf("app.MapReadiness();", StringComparison.Ordinal);
        var razor = program.IndexOf("app.MapRazorComponents<App>()", StringComparison.Ordinal);
        Assert.True(identity > 0 && ready > identity && razor > ready,
            $"التَرتيب: MapBuildIdentity@{identity} ثُمَّ MapReadiness@{ready} ثُمَّ MapRazorComponents@{razor}.");
    }

    /// <summary><b>لا جَلسَةَ ولا كِتابَة</b>: المَخزَنُ يُقرَأُ عِندَ التَركيبِ لا وَسيطاً لِلامدا، فَلا يَراهُ
    /// <c>EndpointStoreInjectionTests</c> — وهذا الفَحصُ يَحمِلُ عَنه ما يَحرُسُه: لا جَلسَةَ تُفتَحُ ولا وَثيقَةَ تُكتَب،
    /// و<c>select 1</c> وَحدَه. <b>و<c>/health</c> لا يَلمِسُ شَيئاً مِن ذلك</b> — <c>BuildIdentity.cs</c> بِلا Marten.</summary>
    [Fact]
    public void The_readiness_file_opens_no_session_and_writes_nothing()
    {
        var src = Read("libs", "core", "ACommerce.Platform.Hosting", "Readiness.cs");
        var code = string.Join('\n', src.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        foreach (var forbidden in new[]
                 {
                     "QuerySession(", "LightweightSession(", "IdentitySession(", "DirtyTrackedSession(", "OpenSession(",
                     "IDocumentSession", "SaveChanges", "Events.", ".Store(", "insert ", "update ", "delete ",
                 })
            Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);

        Assert.Single(Regex.Matches(code, "\"select 1\""));

        var identity = Read("libs", "core", "ACommerce.Platform.Hosting", "BuildIdentity.cs");
        Assert.DoesNotContain("using Marten", identity, StringComparison.Ordinal);
    }

    /// <summary>والأُنبوبُ الكامِلُ يُقاسُ في بَوّابَةِ الإقلاع: الرَمزُ <b>والجِسمُ</b> والرَأسُ — دَرسُ <c>/health</c> المَقيس
    /// (المَسارُ المُبتَلَعُ يَرُدُّ ‏200 بِصَفحَة).</summary>
    [Fact]
    public void The_production_boot_gate_measures_readiness_on_the_whole_pipeline()
    {
        var gate = Read("scripts", "verify-production-boot.sh");

        Assert.Contains("\"GET|/health/ready|200\"", gate, StringComparison.Ordinal);
        Assert.Contains("\"HEAD|/health/ready|200\"", gate, StringComparison.Ordinal);
        Assert.Contains("[\"GET|/health/ready\"]='\"ready\":true'", gate, StringComparison.Ordinal);
        Assert.Contains("[\"HEAD|/health/ready\"]='no-store;application/json'", gate, StringComparison.Ordinal);

        // ‏و`/health` كَما كانَ — لا صَفَّ أُزيلَ لِيُفسِحَ.
        Assert.Contains("\"GET|/health|200\"", gate, StringComparison.Ordinal);
        Assert.Contains("\"HEAD|/health|200\"", gate, StringComparison.Ordinal);
    }

    /// <summary>ووَظيفَةُ النَشرِ تَستَطلِعُ <c>/health</c> كَما هُوَ — الجاهِزِيَّةُ لا تَدخُلُ بَوّابَةً تَحكُمُ عَلى «أَيُّ بِناءٍ
    /// يَخدِم»: انقِطاعُ Neon لَحظَةَ النَشرِ يُحمِرُها عَلى بِناءٍ سَليم.</summary>
    [Fact]
    public void The_deploy_workflow_still_probes_liveness_not_readiness()
    {
        var deploy = Read(".github", "workflows", "deploy-hf.yml");
        Assert.Contains("/health", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("/health/ready", deploy, StringComparison.Ordinal);
    }
}
