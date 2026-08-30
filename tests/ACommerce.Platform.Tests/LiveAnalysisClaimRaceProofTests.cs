using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهانُ ذَرِّيَّةِ حَجزِ التَحليل — الجانِبُ الَّذي يَلزَمُه Postgres ──
//
// هذا **لَيسَ اختِبارَ وَحدَة**، ولا يَعمَلُ في التَشغيلِ العاديّ:
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و
// ConnectionStrings__Postgres. نَفسُ عَقدِ LiveQuotaRaceProofTests حَرفاً.
//
// **ولِماذا كُتِبَ**: العِلاجُ يَقومُ عَلى دَعوى — «إدخالُ وَثيقَةٍ
// بِمِفتاحٍ مُكَرَّرٍ يَرتَدُّ مِن Postgres بِـ23505، فَيَفوزُ واحِدٌ مِن
// المُتَوازينَ لا غَير». والدَعوى غَيرُ المُشَغَّلَةِ اِستِنتاج، وهذا
// المِلَفُّ يُشَغِّلُها: إمّا يُثبِتُها أَو يَكسِرُها. (القاعِدَة ١٠:
// الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها.)
//
// **وما يُثبِتُه بِالضَبط**: أَنّ الحَجزَ ذَرِّيّ. أَمّا أَنّ النُقطَةَ
// تَحجُزُ قَبلَ أَن تُطلِق فَمَقيسٌ بُنيَوِيّاً في
// LanguageModelQuotaGateTests — وهُوَ سُؤالٌ آخَر.

public class LiveAnalysisClaimRaceProofTests
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    /// <summary>سِلسِلَةُ الاتِّصالِ ومَعَها مُهَلٌ أَطوَل — حاسوبُ Neon
    /// يَنام، فَأَوَّلُ اتِّصالٍ بَعدَ نَومٍ يَتَجاوَزُ المُهلَةَ
    /// الافتِراضِيَّةَ أَثناءَ SASL فَيَبدو البُرهانُ كاسِراً وهُوَ لَم
    /// يَبدَأ.</summary>
    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    /// <summary>نَفسُ تَركيبِ Marten الَّذي في <c>HostingExtensions</c>
    /// بِقَدرِ ما يَلزَمُ هذَينِ النَوعَين: مُخَطَّطُ
    /// <c>platform</c>، وكُلُّ الوَثائِقِ مُتَعَدِّدَةُ الإيجار.
    /// البُرهانُ لا يَصِحُّ عَلى تَركيبٍ آخَر.</summary>
    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task The_claim_is_atomic_and_the_race_has_exactly_one_winner()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var evidence = new StringBuilder();
        void Log(string line)
        {
            var stamped = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}  {line}";
            evidence.AppendLine(stamped);
            Console.WriteLine(stamped);
        }

        using var store = BuildStore();
        const string tenant = FeasibilityAnalysisService.IncubatorTenant;

        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession(tenant))
        {
            s.Store(new IncubatorSession
            {
                Id = id, OwnerUserId = Guid.NewGuid(), OwnerName = "بُرهان",
                Status = IncubatorStatus.PatternSuggested,
                UpdatedAt = DateTime.UtcNow,
            });
            await s.SaveChangesAsync();
        }
        Log($"[seed] session={id} status=PatternSuggested runs=0");

        try
        {
            // ═══ ١. التَوازي: عِشرونَ حاجِزاً عَلى نَفسِ المُعَرِّف ═══
            //
            // كُلُّ واحِدٍ يَبني خِدمَتَه بِنَفسِه (كَما يَفعَلُ نِطاقُ
            // DI في الطَلَب)، فَلا جَلسَةَ مُشتَرَكَةً تُخفي السِباق.
            const int racers = 20;
            var tasks = Enumerable.Range(0, racers).Select(_ => Task.Run(async () =>
            {
                var svc = new FeasibilityAnalysisService(
                    store, new NullBackendProvider(), new FeasibilityPromptBuilder(new SaudiDataProvider()));
                return await svc.TryClaimAnalysisAsync(id);
            })).ToArray();

            var outcomes = await Task.WhenAll(tasks);

            var claimed = outcomes.Count(o => o == FeasibilityAnalysisService.ClaimOutcome.Claimed);
            var lost = outcomes.Count(o => o == FeasibilityAnalysisService.ClaimOutcome.LostRace);
            var running = outcomes.Count(o => o == FeasibilityAnalysisService.ClaimOutcome.AlreadyRunning);

            Log($"[race] racers={racers} claimed={claimed} lost={lost} alreadyRunning={running}");

            // **فائِزٌ واحِدٌ بِالضَبط** — وهذا هُوَ البُرهانُ كُلُّه.
            Assert.Equal(1, claimed);
            Assert.Equal(racers, claimed + lost + running);

            // ولا حاجِزَ فاشِلٌ خَلَّفَ أَثَراً: عَدّادُ التَشغيلاتِ
            // ارتَفَعَ **واحِداً** لا عِشرين.
            await using (var q = store.QuerySession(tenant))
            {
                var after = await q.LoadAsync<IncubatorSession>(id);
                Log($"[after] status={after!.Status} runs={after.AnalysisRuns}");
                Assert.Equal(IncubatorStatus.Analyzing, after.Status);
                Assert.Equal(1, after.AnalysisRuns);

                var claim = await q.LoadAsync<AnalysisRunClaim>(
                    FeasibilityAnalysisService.ClaimId(id, 0));
                Assert.NotNull(claim);
                Log($"[claim] id={claim!.Id} attempt={claim.Attempt}");
            }

            // ═══ ١-ب. ومِفتاحُ الفَرادَةِ نَفسُه يُقاس ═════════════════
            //
            // **ولِماذا لا يَكفي ما قَبلَه**: القياسُ أَعلاه أَعطى
            // `lost=0` — أَي أَنّ التِسعَةَ عَشَرَ الخاسِرينَ ارتَدّوا
            // مِن فَحصِ الحالَةِ المُبَكِّرِ لِأَنّ كِتابَةَ الفائِزِ
            // سَبَقَت قِراءَتَهُم. فَالفَرعُ الَّذي يَقومُ عَلَيه
            // العِلاجُ — اصطِدامُ المِفتاحِ في Postgres — **لَم
            // يُنَفَّذ**. و«فائِزٌ واحِد» بِلا تَنفيذِ ذلكَ الفَرعِ
            // يُثبِتُ نافِذَةً ضَيِّقَةً لا يُثبِتُ إغلاقَها.
            //
            // فَيُجبَرُ الفَرعُ: مِفتاحُ التَشغيلَةِ مَوجودٌ سَلَفاً،
            // والجَلسَةُ لَيسَت `Analyzing` — فَلا سَبيلَ لِلرَفضِ
            // إلّا مِن قاعِدَةِ البَياناتِ نَفسِها.
            var forced = Guid.NewGuid();
            await using (var s = store.LightweightSession(tenant))
            {
                s.Store(new IncubatorSession
                {
                    Id = forced, OwnerUserId = Guid.NewGuid(), OwnerName = "بُرهان",
                    Status = IncubatorStatus.Completed, UpdatedAt = DateTime.UtcNow,
                });
                s.Insert(new AnalysisRunClaim
                {
                    Id = FeasibilityAnalysisService.ClaimId(forced, 0),
                    SessionId = forced, Attempt = 0,
                });
                await s.SaveChangesAsync();
            }

            var svcForced = new FeasibilityAnalysisService(
                store, new NullBackendProvider(), new FeasibilityPromptBuilder(new SaudiDataProvider()));
            var collided = await svcForced.TryClaimAnalysisAsync(forced);
            Log($"[forced-collision] outcome={collided}");
            Assert.Equal(FeasibilityAnalysisService.ClaimOutcome.LostRace, collided);

            // والخاسِرُ ارتَدَّ بِمُعامَلَتِه **كامِلَةً**: لا حالَةٌ
            // قُلِبَت ولا عَدّادٌ ارتَفَع. وهذا هُوَ الفَرقُ بَينَ
            // «رَفضٍ» و«رَفضٍ نَظيف».
            await using (var q = store.QuerySession(tenant))
            {
                var after = await q.LoadAsync<IncubatorSession>(forced);
                Log($"[forced-after] status={after!.Status} runs={after.AnalysisRuns}");
                Assert.Equal(IncubatorStatus.Completed, after.Status);
                Assert.Equal(0, after.AnalysisRuns);
            }

            await using (var s = store.LightweightSession(tenant))
            {
                s.Delete<IncubatorSession>(forced);
                s.Delete<AnalysisRunClaim>(FeasibilityAnalysisService.ClaimId(forced, 0));
                await s.SaveChangesAsync();
            }

            // ═══ ٢. وجَلسَةٌ قَيدَ التَحليلِ لا تُعادُ إطلاقاً ═══════
            var svcAgain = new FeasibilityAnalysisService(
                store, new NullBackendProvider(), new FeasibilityPromptBuilder(new SaudiDataProvider()));
            var again = await svcAgain.TryClaimAnalysisAsync(id);
            Log($"[repeat] outcome={again}");
            Assert.Equal(FeasibilityAnalysisService.ClaimOutcome.AlreadyRunning, again);

            // ═══ ٣. واكتَمَلَت: تُحجَزُ ثانِيَةً بِمِفتاحٍ ثانٍ ══════
            await using (var s = store.LightweightSession(tenant))
            {
                var doc = await s.LoadAsync<IncubatorSession>(id);
                doc!.Status = IncubatorStatus.Completed;
                s.Store(doc);
                await s.SaveChangesAsync();
            }

            var second = await svcAgain.TryClaimAnalysisAsync(id);
            Log($"[reclaim-after-complete] outcome={second}");
            Assert.Equal(FeasibilityAnalysisService.ClaimOutcome.Claimed, second);

            await using (var q = store.QuerySession(tenant))
            {
                var after = await q.LoadAsync<IncubatorSession>(id);
                Log($"[after#2] runs={after!.AnalysisRuns}");
                Assert.Equal(2, after.AnalysisRuns);
                Assert.NotNull(await q.LoadAsync<AnalysisRunClaim>(
                    FeasibilityAnalysisService.ClaimId(id, 1)));
            }

            // ═══ ٤. ولا جَلسَةَ لا وُجودَ لَها تُحجَز ═══════════════
            var missing = await svcAgain.TryClaimAnalysisAsync(Guid.NewGuid());
            Log($"[missing] outcome={missing}");
            Assert.Equal(FeasibilityAnalysisService.ClaimOutcome.NotFound, missing);
        }
        finally
        {
            // البُرهانُ لا يَترُكُ أَثَراً في القاعِدَة.
            await using var s = store.LightweightSession(tenant);
            s.Delete<IncubatorSession>(id);
            s.Delete<AnalysisRunClaim>(FeasibilityAnalysisService.ClaimId(id, 0));
            s.Delete<AnalysisRunClaim>(FeasibilityAnalysisService.ClaimId(id, 1));
            await s.SaveChangesAsync();
            Console.WriteLine(evidence.ToString());
        }
    }

    /// <summary>خَلفِيَّةٌ لا تُنادى — الحَجزُ لا يَلمِسُ نَموذَجَ
    /// لُغَةٍ، وهذا نِصفُ المَقصود: <b>الحَجزُ يَقَعُ قَبلَ أَيِّ
    /// إنفاق</b>. فَلَو نودِيَت لَانفَجَرَت.</summary>
    private sealed class NullBackendProvider : ACommerce.Templates.Customer.Marketplace.Services.IAgentBackendProvider
    {
        public ACommerce.Templates.Customer.Marketplace.Services.AgentProfile ProfileFor(string agentName)
            => new("proof", "proof", null, "", "proof", null);
        public ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend For(string agentName)
            => new ExplodingBackend();
        public string ModelFor(string agentName) => "proof";
    }

    private sealed class ExplodingBackend : ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend
    {
        public string ProviderName => "proof";
        public string DefaultModel => "proof";
        public bool IsConfigured => false;
        public string Endpoint => "proof://never-called/";
        public Task<ACommerce.Templates.Customer.Marketplace.Services.AgentBackendResponse> CallAsync(
            ACommerce.Templates.Customer.Marketplace.Services.AgentRequest req, CancellationToken ct)
            => throw new InvalidOperationException("الحَجزُ نادى نَموذَجَ لُغَة — وهذا ما يَمنَعُه.");
    }
}
