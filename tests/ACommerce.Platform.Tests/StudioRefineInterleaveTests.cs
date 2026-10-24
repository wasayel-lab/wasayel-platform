using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَحسينانِ مُتَداخِلانِ لِقِسمَينِ مُختَلِفَينِ يُضَيِّعانِ أَحَدَهُما — تَوصيفُ «اليَوم» (‏2026-10-24، تَيّارُ S16 البَند B) ═══
//
// **المَقروءُ في الكود** (`1c72873`، `FeasibilityAnalysisService.RefineSectionAsync`): الدِراسَةُ تُحَمَّلُ في أَوَّلِه (`s`)، ثُمَّ يُنادى المُزَوِّدُ
// (حَتّى ‏420 ثانِيَة — `AgentCallWait.Background`)، ثُمَّ `MergeSection(s.AnalysisJson, …)` عَلى **تِلكَ اللَقطَة**، ثُمَّ تُعادُ قِراءَةُ الدِراسَةِ
// (`fresh`) ويُكتَبُ عَلَيها المَدموجُ مِن `s`. فَتَحسينانِ لِقِسمَينِ مُختَلِفَينِ يَجريانِ مَعاً (النُقطَةُ تُجيزُه: كُلٌّ بِوَحدَتِه، ولا حَجزَ كَما
// لِـ`/analyze`) يَنتَهي آخِرُهُما بِإعادَةِ قِسمِ الآخَرِ إلى قيمَتِه القَديمَة — وَحدَةُ تَحسينٍ ونِداءُ مُزَوِّدٍ بِلا أَثَر. وبَعدَ `389c689`
// لا يَبلُغُه إلّا المالِك، فَلَم يَعُد كِتابَةً عابِرَةً لِلمُستَخدِمين.
//
// هذا المِلَفُّ يُوَصِّفُ «اليَوم» (القاعِدَة ٣) قَبلَ التَبديل: صَفٌّ نَصِّيٌّ يَجري في CI، وبُرهانٌ حَيٌّ (‏Postgres مَحَلِّيّ، مُبَوَّبٌ
// بِـWASAYEL_LIVE_PROOF=1) يُشَغِّلُ الخِدمَةَ نَفسَها مَرَّتَينِ مُتَداخِلَتَينِ بِخَلفِيَّةٍ مُبَوَّبَةٍ تَحبِسُ كُلَّ نِداءٍ حَتّى يُطلَق.
// وصَفّا «Today_» يُقلَبانِ بِأَسمائِهِما في كوميتِ الإصلاح.
//
// ‏(‏2026-10-24، كوميتُ الإصلاح — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): قُلِبَ الصَفّانِ بِأَسمائِهِما — الدَمجُ صارَ في الدِراسَةِ المُعادَةِ بَعدَ
// النِداء (`fresh.AnalysisJson`)، والبُرهانُ الحَيُّ يَرى القِسمَينِ جَديدَين. والنافِذَةُ الباقِيَةُ رِحلَةُ قاعِدَةٍ بَينَ القِراءَةِ والحِفظ.

public class StudioRefineInterleaveTests(ITestOutputHelper output)
{
    internal static string RefineService()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Incubator", "FeasibilityAnalysisService.cs");
        var svc = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var start = svc.IndexOf("public async Task RefineSectionAsync(", StringComparison.Ordinal);
        Assert.True(start > 0, "RefineSectionAsync غائِبَة.");
        var end = svc.IndexOf("internal static string ExtractSection(", start, StringComparison.Ordinal);
        Assert.True(end > start, "لَم يُعثَر عَلى ما بَعدَ RefineSectionAsync — القَصُّ فاشِل.");
        return svc[start..end];
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، تَيّارُ S16 البَند B)</b> — كانَ
    /// <c>Today_refine_merges_into_the_snapshot_read_before_the_provider_call_and_writes_it_over_the_reloaded_study</c> (‏<c>fc0fc70</c>:
    /// «النِداء 1889 · الدَمجُ عَلى s 2533 · إعادَةُ القِراءَة 2707 · الكِتابَةُ عَلى fresh 2783»). والآن: النِداءُ ← إعادَةُ القِراءَة ← الدَمجُ في
    /// <c>fresh.AnalysisJson</c> ← الكِتابَةُ عَلَيها؛ ولا دَمجَ في <c>s</c> أَصلاً، ونِداءُ دَمجٍ واحِد.</summary>
    [Fact]
    public void Refine_merges_into_the_study_reloaded_after_the_provider_call()
    {
        var body = RefineService();
        var call = body.IndexOf("await _backend.CallAsync(req, ct)", StringComparison.Ordinal);
        var reload = body.IndexOf("var fresh = await session.LoadAsync<IncubatorSession>(id, ct)", StringComparison.Ordinal);
        var merge = body.IndexOf("MergeSection(fresh.AnalysisJson ?? s.AnalysisJson, newJson, sectionKey)", StringComparison.Ordinal);
        var write = body.IndexOf("fresh.AnalysisJson = merged;", StringComparison.Ordinal);
        output.WriteLine($"النِداء {call} · إعادَةُ القِراءَة {reload} · الدَمجُ في fresh {merge} · الكِتابَةُ عَلى fresh {write}");
        Assert.True(call > 0 && reload > call && merge > reload && write > merge,
            "التَرتيبُ لَيسَ: النِداءُ ← إعادَةُ القِراءَة ← الدَمجُ في fresh ← الكِتابَة.");
        Assert.DoesNotContain("MergeSection(s.AnalysisJson", body, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, @"\bMergeSection\("));
    }
}

// ─── البُرهانُ الحَيّ — يَلزَمُه Postgres، ويَتَخَطّى نَفسَه بِدونِه (عَقدُ LiveAnalysisClaimRaceProofTests حَرفاً) ───

[Collection(LiveProofCollection.Name)]
public class LiveRefineInterleaveProofTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";
    private static string? ConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    internal const string OldRisks = "old-risks";
    internal const string OldMarket = "old-marketSizing";

    /// <summary>يُشَغِّلُ تَحسينَينِ مُتَداخِلَين: كِلاهُما يَقرَأُ الدِراسَةَ ويَبلُغُ المُزَوِّد، ثُمَّ يُطلَقُ <c>marketSizing</c> فَيَحفَظ، ثُمَّ
    /// <c>risks</c> — ويُرجِعُ القِسمَينِ كَما في القاعِدَةِ بَعدَهُما.</summary>
    internal static async Task<(string? Risks, string? Market, int Calls)> InterleaveAsync(Action<string> log)
    {
        using var store = BuildStore();
        const string tenant = FeasibilityAnalysisService.IncubatorTenant;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await using (var s = store.LightweightSession(tenant))
        {
            s.Store(new IncubatorSession
            {
                Id = id, OwnerUserId = owner, OwnerName = "بُرهان",
                Status = IncubatorStatus.Completed,
                AnalysisJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["summary"] = "s", ["risks"] = OldRisks, ["marketSizing"] = OldMarket,
                }),
                UpdatedAt = DateTime.UtcNow,
            });
            await s.SaveChangesAsync();
        }
        log($"[seed] session={id} risks={OldRisks} marketSizing={OldMarket}");
        var backend = new GatedBackend();
        try
        {
            FeasibilityAnalysisService Service() => new(store, new GatedProvider(backend), new FeasibilityPromptBuilder(new SaudiDataProvider()));
            var risks = Task.Run(() => Service().RefineSectionAsync(id, "risks", "أَعِد"));
            var market = Task.Run(() => Service().RefineSectionAsync(id, "marketSizing", "أَعِد"));
            await backend.EnteredAsync("risks");
            await backend.EnteredAsync("marketSizing");
            log("[interleave] كِلاهُما قَرَأَ الدِراسَةَ وبَلَغَ المُزَوِّد");
            backend.Release("marketSizing");
            await market.WaitAsync(TimeSpan.FromSeconds(60));
            log("[interleave] marketSizing حُفِظ");
            backend.Release("risks");
            await risks.WaitAsync(TimeSpan.FromSeconds(60));
            log("[interleave] risks حُفِظ");

            await using var q = store.QuerySession(tenant);
            var after = await q.LoadAsync<IncubatorSession>(id);
            using var doc = JsonDocument.Parse(after!.AnalysisJson!);
            var r = doc.RootElement.GetProperty("risks").GetString();
            var m = doc.RootElement.GetProperty("marketSizing").GetString();
            log($"[after] risks={r} marketSizing={m} نِداءاتُ المُزَوِّد={backend.Calls}");
            return (r, m, backend.Calls);
        }
        finally
        {
            await using var s = store.LightweightSession(tenant);
            s.Delete<IncubatorSession>(id);
            await s.SaveChangesAsync();
            await using var m = store.LightweightSession(StudioAuth.Tenant);
            m.DeleteWhere<ACommerce.Templates.Customer.Marketplace.Services.Metering.ModelCallRecord>(x => x.UserId == owner);
            await m.SaveChangesAsync();
        }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، تَيّارُ S16 البَند B)</b> — كانَ
    /// <c>Today_two_interleaved_refines_of_different_sections_end_with_the_first_saved_section_restored</c> (‏<c>fc0fc70</c>، مَقيساً:
    /// «risks=new-risks marketSizing=old-marketSizing نِداءاتُ المُزَوِّد=2»). والآن القِسمانِ جَديدان بِالنِداءَينِ نَفسَيهِما.</summary>
    [Fact]
    public async Task Two_interleaved_refines_of_different_sections_keep_both_new_sections()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;
        var evidence = new StringBuilder();
        var (risks, market, calls) = await InterleaveAsync(l => { evidence.AppendLine(l); Console.WriteLine(l); });
        Assert.Equal(2, calls);
        Assert.Equal("new-risks", risks);
        Assert.Equal("new-marketSizing", market);
    }

    internal sealed class GatedProvider(GatedBackend backend) : IAgentBackendProvider
    {
        public AgentProfile ProfileFor(string agentName) => new("proof", "proof", null, "", "proof", null);
        public IAgentBackend For(string agentName) => backend;
        public string ModelFor(string agentName) => "proof";
    }

    /// <summary>خَلفِيَّةٌ تَحبِسُ كُلَّ نِداءٍ حَتّى يُطلَقَ قِسمُه، وتُجيبُ <c>{"قِسم": "new-قِسم"}</c> — ولا شَبَكَة.</summary>
    internal sealed class GatedBackend : IAgentBackend
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _entered = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _release = new();
        private int _calls;
        public int Calls => _calls;
        private TaskCompletionSource Gate(ConcurrentDictionary<string, TaskCompletionSource> d, string k)
            => d.GetOrAdd(k, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        public Task EnteredAsync(string section) => Gate(_entered, section).Task.WaitAsync(TimeSpan.FromSeconds(60));
        public void Release(string section) => Gate(_release, section).TrySetResult();

        public string ProviderName => "proof";
        public string DefaultModel => "proof";
        public bool IsConfigured => true;
        public string Endpoint => "proof://gated/";

        public async Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            var text = req.Messages[0].Text ?? "";
            var section = StudioRefineSection.All.Single(k => text.Contains($"## القِسم الحاليّ ({k})", StringComparison.Ordinal));
            Gate(_entered, section).TrySetResult();
            await Gate(_release, section).Task.WaitAsync(TimeSpan.FromSeconds(60), ct);
            return new AgentBackendResponse($"{{\"{section}\": \"new-{section}\"}}", null, null);
        }
    }
}
