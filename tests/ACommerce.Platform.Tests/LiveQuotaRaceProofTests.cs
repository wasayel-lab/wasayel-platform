using System.Text;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Subscriptions;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بُرهان الذَرِّيَّة والسِباق — الجانِب الَّذي يَلزَمُه قاعِدَة بَيانات ──
//
// هذا **ليسَ اختِبار وَحدَة**، ولا يَعمَل في التَشغيل العادِيّ: يَتَخَطّى
// نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres.
// نَفس عَقد LiveRoleDefinitionProofTests حَرفاً.
//
// **ولِماذا كُتِبَ**: تَصميم هذه الطَبَقَة وَصَفَ سُلوك
// Append(stream, expectedVersion, …) عِندَ التَضارُب ووَسَمَه صَراحَةً
// «[استِنتاج] — لَم أُشَغِّلُه». الاستِنتاج غَير المُشَغَّل دَعوى، وهذا
// المِلَفّ يُشَغِّلُه: إمّا يُثبِتُه أَو يَكسِرُه.

public class LiveQuotaRaceProofTests
{
    private const string TenantSlug = "hissa-demo";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    /// <summary>نَفس تَركيب Marten الَّذي في <c>HostingExtensions</c> —
    /// إيجار مُقتَرِن لِلأَحداث، وإسقاط <c>Inline</c> لِلاشتِراك
    /// والإعلان. البُرهان لا يَصِحّ عَلى تَركيب آخَر.</summary>
    /// <summary>سِلسِلَة الاتِّصال ومَعَها مُهَل أَطوَل. قِيسَ: حاسوب
    /// Neon يَنام، فَأَوَّل اتِّصال بَعدَ نَومٍ يَتَجاوَز المُهلَة
    /// الافتِراضِيَّة أَثناء SASL — فَيَبدو البُرهانُ كاسِراً وهو لَم
    /// يَبدَأ. المُهلَة لَيسَت تَجميلاً: بِدونِها تَفشَل الأَداة لا
    /// المَفحوص.</summary>
    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<Plan>().Identity(x => x.Id);
        o.Projections.Snapshot<Subscription>(SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    /// <summary>يَبذُر خُطَّةً واشتِراكاً بِحِصَّة مُعطاة، ويُرجِع
    /// (المُستَخدِم، مُعَرِّف الاشتِراك).</summary>
    private static async Task<(Guid UserId, Guid SubId)> SeedAsync(
        IDocumentStore store, int quota)
    {
        var userId = Guid.NewGuid();
        var subId  = Guid.NewGuid();
        var planId = $"proof-{quota}-{Guid.NewGuid():N}"[..24];

        await using var s = store.LightweightSession(TenantSlug);
        s.Store(new Plan
        {
            Id = planId, Name = "بُرهان", Price = 0,
            ListingsQuota = quota, DaysPeriod = 30, IsActive = true
        });
        s.Events.StartStream<Subscription>(subId,
            new SubscriptionCreated(subId, userId, planId, quota, 30, DateTime.UtcNow));
        await s.SaveChangesAsync();
        return (userId, subId);
    }

    /// <summary>
    /// <para><b>مُحاكاة مَسار إنشاء الإعلان بِشَكلِه الحَقيقيّ</b>:
    /// جَلسَة واحِدَة، استِهلاك ثُمَّ فَتح تَيار الإعلان، ثُمَّ
    /// <c>SaveChangesAsync</c> واحِدَة. هذا هو بِعَينِه ما يَفعَلُه
    /// <c>MTE.cs</c> — ولِذلك يَصلُح بُرهاناً.</para>
    /// </summary>
    private static async Task<(bool Allowed, Guid? ListingId, Exception? Conflict)>
        PublishAsync(IDocumentStore store, IEntitlements ents, Guid userId)
    {
        var listingId = Guid.NewGuid();
        await using var s = store.LightweightSession(TenantSlug);

        var gate = await ents.ConsumeAsync(
            s, TenantSlug, userId, CapabilityCatalog.ListingCreate);
        if (!gate.Allowed) return (false, null, null);

        s.Events.StartStream<Listing>(listingId, new ListingCreated(
            listingId, TenantSlug, "إعلان بُرهان", null, 10m, "misc",
            null, null, new Dictionary<string, string>(), DateTime.UtcNow));

        try
        {
            await s.SaveChangesAsync();
            return (true, listingId, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex);
        }
    }

    [Fact]
    public async Task Quota_is_consumed_atomically_and_the_race_has_exactly_one_winner()
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
        var ents = new SubscriptionEntitlements(store);

        // ═══ ١. التَسَلسُل: حِصَّة واحِدَة، إعلانان ═══════════════════
        var (userId, subId) = await SeedAsync(store, quota: 1);
        Log($"[seed] user={userId} sub={subId} quota=1");

        var first = await PublishAsync(store, ents, userId);
        Log($"[publish#1] allowed={first.Allowed} listing={first.ListingId}");
        Assert.True(first.Allowed);
        Assert.Null(first.Conflict);

        await using (var q = store.QuerySession(TenantSlug))
        {
            var sub = await q.LoadAsync<Subscription>(subId);
            Log($"[after#1] QuotaRemaining={sub!.QuotaRemaining}");
            Assert.Equal(0, sub.QuotaRemaining);      // نَقَصَ واحِداً بِالضَبط
        }

        var second = await PublishAsync(store, ents, userId);
        Log($"[publish#2] allowed={second.Allowed} (مُتَوَقَّع: مَنع)");
        Assert.False(second.Allowed);
        Assert.Null(second.Conflict);                 // مَنع صَريح لا فَشَل غامِض
        Assert.Null(second.ListingId);                // ولا تَيار إعلان كُتِبَ

        await using (var q = store.QuerySession(TenantSlug))
        {
            var events = await q.Events.FetchStreamAsync(subId);
            var consumed = events.Count(e => e.Data is QuotaConsumed);
            Log($"[stream] أَحداث={events.Count} QuotaConsumed={consumed}");
            Assert.Equal(1, consumed);                // حَدَث واحِد لا اثنان
        }

        // ═══ ٢. السِباق: مُستَخدِمانِ عَلى آخِر وَحدَة ═════════════════
        //
        // نَفس المُستَخدِم، اشتِراك بِحِصَّة واحِدَة، وطَلَبانِ يَنطَلِقانِ
        // مَعاً. القِراءَتانِ تَريانِ QuotaRemaining=1 كِلتاهُما — وهذه هي
        // نافِذَة السِباق بِعَينِها. الفائِز واحِد لِأَنّ الإلحاق
        // بِنُسخَة مُتَوَقَّعَة يَجعَل الخاسِر يَفشَل عِندَ الحِفظ.
        var (raceUser, raceSub) = await SeedAsync(store, quota: 1);
        Log($"[race/seed] user={raceUser} sub={raceSub} quota=1");

        // **التَشابُك مَصنوع لا مَرجُوّ**: إطلاقُ مَهَمَّتَين مَعاً لا
        // يَضمَن تَداخُلاً — قِيسَ في أَوَّل جَولَة: تَسَلسَلَتا،
        // فَرَأَت الثانِيَة الرَصيدَ صِفراً ومُنِعَت، ولَم يُختَبَر
        // شَرطُ النُسخَة أَصلاً. فَبُرهانٌ يَمُرّ بِلا أَن يَمَسّ ما
        // يَدَّعي فَحصَه بُرهانٌ كاذِب.
        //
        // الحاجِز يَجعَل **كِلتَيهِما تَقرَآنِ الرَصيدَ (‏١) وتُلحِقانِ
        // الحَدَث قَبل أَن تَحفَظ أَيٌّ مِنهُما**. هذه هي نافِذَة
        // السِباق بِعَينِها، مَفتوحَةً عَمداً.
        var consumedA = new TaskCompletionSource();
        var consumedB = new TaskCompletionSource();

        async Task<(bool Allowed, Guid? ListingId, Exception? Conflict, int Seen)> Racer(
            TaskCompletionSource mine, TaskCompletionSource other)
        {
            var listingId = Guid.NewGuid();
            await using var s = store.LightweightSession(TenantSlug);

            var gate = await ents.ConsumeAsync(
                s, TenantSlug, raceUser, CapabilityCatalog.ListingCreate);

            mine.SetResult();
            await other.Task;          // ← لا حِفظَ قَبل أَن تَقرَأ الأُخرى

            if (!gate.Allowed) return (false, null, null, gate.Remaining);

            s.Events.StartStream<Listing>(listingId, new ListingCreated(
                listingId, TenantSlug, "إعلان سِباق", null, 10m, "misc",
                null, null, new Dictionary<string, string>(), DateTime.UtcNow));

            try
            {
                await s.SaveChangesAsync();
                return (true, listingId, null, gate.Remaining);
            }
            catch (Exception ex)
            {
                return (false, null, ex, gate.Remaining);
            }
        }

        var t1 = Racer(consumedA, consumedB);
        var t2 = Racer(consumedB, consumedA);
        var results = await Task.WhenAll(t1, t2);

        // البُرهان أَنّ النافِذَة فُتِحَت فِعلاً: كِلتاهُما رَأَت
        // السَماح. لَو تَسَلسَلَتا لَكانَت إحداهُما مَمنوعَة، ولَما
        // مَسَّ الاختِبارُ شَرطَ النُسخَة.
        Log($"[race/window] كِلتاهُما سُمِحَ لَها؟ " +
            $"{results.All(r => r.Conflict is not null || r.Allowed)}");

        foreach (var (i, r) in results.Select((r, i) => (i + 1, r)))
            Log($"[race#{i}] allowed={r.Allowed} seenRemaining={r.Seen} " +
                $"listing={r.ListingId} conflict={r.Conflict?.GetType().Name ?? "—"}");

        var winners = results.Count(r => r.Allowed);
        var losers  = results.Where(r => !r.Allowed).ToArray();

        Log($"[race] فائِزون={winners} خاسِرون={losers.Length}");
        Assert.Equal(1, winners);                     // واحِد يَنجَح

        // ═══ الحُكم عَلى الاستِنتاج ═══════════════════════════════════
        // كِلتاهُما اجتازَت الفَحص (‏seenRemaining=0 لِكِلتَيهِما تَعني
        // أَنَّهُما رَأَتا رَصيداً واحِداً وأَنقَصَتاه)، فَالخاسِر لا
        // يُمكِن أَن يَكون مَمنوعاً — لا بُدَّ أَن يَكون **فاشِلاً
        // بِتَضارُب نُسخَة عِندَ الحِفظ**. وهذا هو الاستِنتاج الَّذي
        // وَسَمَه التَصميم «غَير مُشَغَّل»: إمّا يُثبَت هُنا أَو يُكسَر.
        var loser = Assert.Single(losers);
        Log($"[race/loser] النَوع={loser.Conflict?.GetType().FullName ?? "(مَنع صَريح — النافِذَة لَم تُفتَح)"}");
        Log($"[race/loser] الرِسالَة={loser.Conflict?.Message ?? "—"}");

        Assert.NotNull(loser.Conflict);               // ← البُرهان: تَضارُب لا مَنع
        Assert.Null(loser.ListingId);                 // ولا إعلان لَه

        // والمُرَشِّح الَّذي يَقرَؤُه المَسار الحَيّ يَعرِف هذا الفَشَل
        // بِعَينِه — وإلّا ارتَدَّ خَمسُمِئَة بَدَل إعادَة مُحاوَلَة.
        Log($"[race/loser] يُصَنَّف تَضارُبَ نُسخَة؟ {IsStreamVersionConflictProbe(loser.Conflict!)}");
        Assert.True(IsStreamVersionConflictProbe(loser.Conflict!),
            "الخاسِر فَشِلَ بِنَوعٍ لا يُصَنِّفُه المَسار الحَيّ تَضارُبَ نُسخَة — " +
            "فَسَيَرتَدّ خَمسُمِئَة بَدَل أَن يُعيد المُحاوَلَة. " +
            $"النَوع: {loser.Conflict!.GetType().FullName}");

        // ═══ ٣. الحُكم: لا رَصيد سالِب، ولا إعلان يَتيم ══════════════
        await using (var q = store.QuerySession(TenantSlug))
        {
            var sub = await q.LoadAsync<Subscription>(raceSub);
            var events = await q.Events.FetchStreamAsync(raceSub);
            var consumed = events.Count(e => e.Data is QuotaConsumed);

            Log($"[race/after] QuotaRemaining={sub!.QuotaRemaining} QuotaConsumed={consumed}");

            Assert.Equal(0, sub.QuotaRemaining);      // ‏0 لا ‎-1‎
            Assert.Equal(1, consumed);                // حَدَث واحِد لا اثنان

            var listings = results.Where(r => r.ListingId is not null)
                                  .Select(r => r.ListingId!.Value).ToArray();
            Assert.Single(listings);
            var written = await q.Events.FetchStreamAsync(listings[0]);
            Log($"[race/listing] {listings[0]} أَحداث={written.Count}");
            Assert.NotEmpty(written);                 // الفائِز كُتِبَ فِعلاً
        }

        var path = Environment.GetEnvironmentVariable("WASAYEL_PROOF_OUT");
        if (!string.IsNullOrEmpty(path))
            await File.WriteAllTextAsync(path, evidence.ToString());
    }

    /// <summary><b>نُسخَة مِن مُصَنِّف المَسار الحَيّ</b>
    /// (<c>MTE.IsStreamVersionConflict</c>، وهو <c>private</c>). وُجودُها
    /// هُنا يَجعَل البُرهان يَفحَص ما يَفحَصُه الإنتاج: لَو رَمى Marten
    /// نَوعاً لا يُصَنِّفُه ذاكَ لَاِرتَدَّ المَسارُ الحَيُّ خَمسَمِئَة
    /// بَدَل إعادَة المُحاوَلَة — وهذا الاختِبارُ يُحَمِّر حينَئِذٍ.</summary>
    private static bool IsStreamVersionConflictProbe(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var name = e.GetType().Name;
            if (name is "EventStreamUnexpectedMaxEventIdException"
                     or "ConcurrencyException"
                     or "StreamLockedException")
                return true;
            if (e is Npgsql.PostgresException { SqlState: "23505" })
                return true;
        }
        return false;
    }
}
