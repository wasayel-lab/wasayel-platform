using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ─── بُرهانُ ذَهابٍ وإيابٍ لِقياسِ الاستِهلاك — الجانِبُ الَّذي يَلزَمُه
//     Postgres ────────────────────────────────────────────────────────────
//
// هذا **لَيسَ اختِبارَ وَحدَة**، ولا يَعمَلُ في التَشغيلِ العاديّ:
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و
// ConnectionStrings__Postgres. نَفسُ عَقدِ LiveAnalysisClaimRaceProofTests
// حَرفاً — ومِنه نُسِخَ تَركيبُ المَخزَن.
//
// **ولِماذا كُتِبَ — والكِلفَةُ مَقيسَة**: مَوجَةُ القياسِ الأولى شَحَنَت
// واحِداً وعِشرينَ فَحصاً **كُلُّها في الذاكِرَة**، فَمَسارُ
// الكِتابَةِ/القِراءَةِ الوَحيدُ لِلميزَةِ لَم يُشَغَّل قَطُّ عَلى
// قاعِدَةٍ حَقيقِيَّة. وأَخفى ذلك عَطَباً مانِعاً: `ReadModelUsageAsync`
// كانَت تَرمي
// `ArgumentException: Cannot write DateTime with Kind=UTC to PostgreSQL
// type 'timestamp without time zone'` لِأَيِّ `sinceUtc` بِـKind=Utc —
// وهُوَ ما يُمَرِّرُه **كُلُّ مُستَدعٍ طَبيعيّ**. أَي أَنّ الكِتابَةَ
// تَعمَلُ والقِراءَةَ مَكسورَة، والحُزمَةُ خَضراء.
//
// **والأَخطَرُ أَنّ الكِتابَةَ مُصَمَّمَةٌ عَلى الابتِلاعِ** (`القياسُ
// مُراقِبٌ لا حارِس`، `StudioTier.RecordModelCallAsync`) — فَلَو رَفَضَت
// Marten الوَثيقَةَ لَما قالَ أَحَدٌ شَيئاً ولَبَقِيَت الحُزمَةُ خَضراءَ
// وهي لا تَقيسُ شَيئاً. ولِذلك **لا يُوثَقُ بِأَنّ القياسَ يَقيسُ حَتّى
// يَمُرَّ صَفٌّ واحِدٌ إلى القاعِدَةِ ويَرجِعَ مِنها** (القاعِدَة ١٠:
// الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها).

public class LiveModelUsageMeteringProofTests(ITestOutputHelper output)
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
    /// بِقَدرِ ما يَلزَمُ هذا النَوع: مُخَطَّطُ <c>platform</c>، وكُلُّ
    /// الوَثائِقِ مُتَعَدِّدَةُ الإيجار. البُرهانُ لا يَصِحُّ عَلى
    /// تَركيبٍ آخَر.</summary>
    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    /// <summary>
    /// <para><b>صَفٌّ يُكتَبُ ثُمَّ يُقرَأُ بِالتَجميعِ مِن Postgres
    /// حَقيقيّ</b> — والأَربَعَةُ مُنفَصِلَةً، والفاشِلَةُ مَعدودَةً،
    /// و«غَيرُ المُسَعَّرِ» و«غَيرُ المَقيسِ» عَدّادَينِ مُتَمايِزَين.</para>
    ///
    /// <para><b>والوَسيطُ يُمَرَّرُ بِـ<c>Kind=Utc</c> عَمداً</b>: هذا
    /// بِعَينِه ما كانَ يَرمي، وهُوَ ما يُمَرِّرُه أَيُّ مُستَدعٍ
    /// طَبيعيٍّ (‏اسمُ الوَسيطِ <c>sinceUtc</c>، والحَقلُ يُملَأُ
    /// بِـ<c>DateTime.UtcNow</c>).</para>
    /// </summary>
    [Fact]
    public async Task A_metering_line_survives_a_round_trip_through_postgres()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var evidence = new StringBuilder();
        void Log(string line) { evidence.AppendLine(line); output.WriteLine(line); }

        await using var store = BuildStore();
        var tier = new StudioTierService(store);

        // مُستَخدِمٌ عَشوائيٌّ يَخُصُّ هذا التَشغيلَ وَحدَه، فَلا
        // يَختَلِطُ بِصُفوفٍ أُخرى ولا يَعتَمِدُ البُرهانُ عَلى خُلُوِّ
        // القاعِدَة.
        var user  = Guid.NewGuid();
        var since = DateTime.UtcNow.AddMinutes(-5);   // ‏Kind=Utc — المَوضِعُ الَّذي كانَ يَرمي

        var lines = new[]
        {
            ModelCallRecord.For("_incubator", user, "anthropic", "claude-sonnet-4-6",
                ModelCallOperation.Analyze, new AgentUsage(111, 22, 33, 44), success: true),

            // مُحاوَلَةٌ فاشِلَةٌ **بِاستِهلاكٍ مَقروء** — تُنفِقُ
            // وتُسَجَّل.
            ModelCallRecord.For("_incubator", user, "anthropic", "claude-sonnet-4-6",
                ModelCallOperation.Analyze, new AgentUsage(9, 8, 7, 6), success: false),

            // وفَشَلٌ **بِلا استِهلاكٍ مَقروء** (‏401 بِلا جِسم) — «لَم
            // يُقَس» لا «صِفر».
            ModelCallRecord.For("_incubator", user, "gemini", "gemini-2.0-flash",
                ModelCallOperation.Refine, usage: null, success: false),
        };

        try
        {
            foreach (var l in lines) await tier.RecordModelCallAsync(l);
            Log($"كُتِبَت {lines.Length} سُطورٍ لِلمُستَخدِم {user:N}.");

            // ═══ القِراءَةُ التَجميعِيَّة — الطَرَفُ الَّذي كانَ
            //     يَرمي ═════════════════════════════════════════════
            var t = await tier.ReadModelUsageAsync(user, since);

            Log($"رَجَعَ: calls={t.Calls} failures={t.Failures} unmeasured={t.UnmeasuredCalls} "
              + $"uncosted={t.UncostedCalls} in={t.InputTokens} out={t.OutputTokens} "
              + $"cw={t.CacheWriteTokens} cr={t.CacheReadTokens} cost={t.CostUsd}");

            // ١) الصُفوفُ الثَلاثَةُ وَصَلَت ورَجَعَت.
            Assert.Equal(3, t.Calls);

            // ٢) والفَشَلُ لا يُسقِطُ السَطر.
            Assert.Equal(2, t.Failures);

            // ٣) و«لَم يُقَس» مُتَمايِزٌ عَن «قيسَ فَكانَ صِفراً».
            Assert.Equal(1, t.UnmeasuredCalls);

            // ٤) والأَربَعَةُ مُنفَصِلَةٌ فِعلاً بَعدَ الذَهابِ
            //    والإياب — لا مَجموعَةً في رَقَم.
            Assert.Equal(120, t.InputTokens);       // ‏111 + 9 + 0
            Assert.Equal(30,  t.OutputTokens);      // ‏22  + 8 + 0
            Assert.Equal(40,  t.CacheWriteTokens);  // ‏33  + 7 + 0
            Assert.Equal(50,  t.CacheReadTokens);   // ‏44  + 6 + 0

            // ٥) ولا سِعرَ في الجَدوَلِ بَعد — فَالثَلاثَةُ بِلا كِلفَة،
            //    والمَبلَغُ صِفرٌ **مَقروناً بِعَدَدِ ما لَم يُسَعَّر**.
            Assert.Equal(3, t.UncostedCalls);
            Assert.Equal(0m, t.CostUsd);

            // ٦) وتَرشيحُ المُستَخدِمِ يَعمَل: مُستَخدِمٌ آخَرُ لا يَرى
            //    شَيئاً مِن هذِه الصُفوف.
            var other = await tier.ReadModelUsageAsync(Guid.NewGuid(), since);
            Assert.Equal(0, other.Calls);

            // ٧) وحَدُّ المُدَّةِ يَعمَل: بِدايَةٌ في المُستَقبَلِ
            //    تُرجِعُ صِفراً — وإلّا كانَ الشَرطُ مُهمَلاً والفَحصُ
            //    أَعمى عَنه.
            var future = await tier.ReadModelUsageAsync(user, DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(0, future.Calls);

            Log("‏٧/٧ — الكِتابَةُ والقِراءَةُ والتَرشيحُ والمُدَّةُ كُلُّها تَعمَل.");
        }
        finally
        {
            // تَنظيفٌ: البُرهانُ لا يَترُكُ أَثَراً في قاعِدَةٍ حَيَّة.
            await using var s = store.LightweightSession(StudioAuth.Tenant);
            foreach (var l in lines) s.Delete<ModelCallRecord>(l.Id);
            await s.SaveChangesAsync();
        }
    }
}
