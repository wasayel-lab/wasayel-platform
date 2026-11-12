using System.Net;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G5 · P1 — سَطرُ `deal.create` يُكتَبُ في مُعامَلَةِ الصَفقَةِ نَفسِها، ويَتَراجَعُ مَعَها، ولا يَتَضاعَف، ورائِدُ أَعمالِه مِن المُحَلِّلِ الحَقيقِيّ ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §5 و§13 P1). المُضيفُ تَركيبُ `AddPlatformHost` + `AddCustomerMarketplaceTemplate` بِعَينِه
// (`LiveTemplateDoorHost`) فَالمُستَمِعُ هُوَ ما يُسَجِّلُه القالِب. وصُفوفُ المُضيفِ المُصَغَّرِ تُثبِتُ **المَتجَرَ وَحدَه** — لا يَمُرُّ فيه وَسيطُ المُستَأجِر
// (`LiveTemplateDoorHost` يُنادي جِسمَ البابِ مُباشَرَة)؛ ورائِدُ الأَعمالِ يُثبِتُه **صَفُّ الأُنبوبِ الحَقيقِيّ** (شَكلُ `StoreHostPipeline`،
// `StoreHostRequestTests.cs:277-317`): ‏Kestrel + `UsePlatformMultiTenancy` + المَخزَنُ المُرَكَّب + `DealsService.StartAsync` الحَقيقِيّ (RULES-03).

[Collection(LiveProofCollection.Name)]
public class LiveMeterPersistenceProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string Cs => LiveDealConcurrencyProofTests.ConnectionString!;

    private static string NewSlug() => "w13m-" + Guid.NewGuid().ToString("N")[..8];

    private sealed record Row(string Id, string Unit, string App, string Entrepreneur, string Role, string Feature, string Provider,
                              string SourceType, string SourceId, int Generation, string WriterXid);

    private static async Task<List<Row>> EntriesForAsync(Guid dealId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, unit, app, entrepreneur, role, feature, provider, source_type, source_id, generation, writer_xid::text "
            + "from meter.usage_entry where source_id = @s order by id", conn);
        cmd.Parameters.AddWithValue("s", dealId.ToString("D"));
        var rows = new List<Row>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new Row(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                             r.GetString(7), r.GetString(8), r.GetInt32(9), r.GetString(10)));
        return rows;
    }

    private static async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<long> DealRowsAsync(Guid id)
        => Convert.ToInt64(await ScalarAsync("select count(*) from platform.mt_doc_deal where id = @id", ("id", id)));

    private static Task<Deal> StartAsync(DealsService deals, string slug, Guid initiator, Guid? id = null)
        => deals.StartAsync(slug, "marketplace", initiator, "مُشتَري العَدّ", null, "صَفقَةُ بُرهانِ العَدّ", 0m, id: id);

    /// <summary><b>المُضيفُ المُصَغَّر</b>: سَطرٌ واحِدٌ بِالمَتجَرِ مِن الصَفقَة، ومُعامَلَتُه مُعامَلَةُ صَفِّ الصَفقَة (‏<c>writer_xid = xmin</c>، D2)؛
    /// والتَحديثُ لا يُعَدّ؛ والإرسالُ الثاني بِالمُعَرِّفِ نَفسِه يُرَدُّ ولا يُضاعِف؛ وإعادَةُ الجُملَةِ نَفسِها في حِفظٍ ناجِحٍ لا تَرمي ولا تُضاعِف (D4).</summary>
    [Fact]
    public async Task On_the_door_host_one_entry_per_new_deal_in_the_deals_own_transaction_and_never_twice()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
        var slug = NewSlug();

        var deal = await StartAsync(deals, slug, Guid.NewGuid());
        var e = Assert.Single(await EntriesForAsync(deal.Id));
        output.WriteLine($"[P1] المُضيفُ المُصَغَّر: {e}");
        Assert.Equal((MeterUnitCatalog.Default.Find("deal.create")!.Code, slug, $"deal.create:{slug}:{deal.Id:D}"), (e.Unit, e.App, e.Id));
        Assert.Equal(("deal_status", MeterSentinels.None, "deal"), (e.Feature, e.Provider, e.SourceType));
        Assert.True(e.Generation >= 1, $"جيلٌ {e.Generation}");

        var sameTx = await ScalarAsync(
            "select (select writer_xid::text from meter.usage_entry where source_id = @s) = (select xmin::text from platform.mt_doc_deal where id = @id)",
            ("s", deal.Id.ToString("D")), ("id", deal.Id));
        output.WriteLine($"[P1] writer_xid = xmin صَفِّ الصَفقَة → {sameTx}");
        Assert.Equal(true, sameTx);

        // التَحديثُ لا يُعَدّ (D6).
        await using (var s = host.Store.LightweightSession(slug))
        {
            var loaded = (await s.LoadAsync<Deal>(deal.Id))!;
            loaded.ListingTitle += " — مُحَدَّثَة";
            s.Store(loaded);
            await s.SaveChangesAsync();
        }
        Assert.Single(await EntriesForAsync(deal.Id));

        // الإرسالُ الثاني بِالمُعَرِّفِ نَفسِه (نَقرَتانِ عَلى سَلَّةٍ واحِدَة): يُرَدُّ بِالتَزامُنِ التَفاؤُلِيّ، والسَطرُ واحِد.
        var again = await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(deals, slug, Guid.NewGuid(), deal.Id));
        output.WriteLine($"[P1] الإرسالُ الثاني: {again.GetType().FullName}");
        Assert.Contains("Concurrency", again.GetType().Name, StringComparison.Ordinal);
        Assert.Single(await EntriesForAsync(deal.Id));

        // ‏D4: الجُملَةُ نَفسُها بِالمُعَرِّفِ نَفسِه في حِفظٍ ناجِح — لا تَرمي، ولا صَفَّ ثانٍ.
        var other = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.QueueSqlCommand(MeterSql.Insert, e.Id, e.Unit, 1L, e.App, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId, 0);
            s.Store(new Deal { Id = other, TenantSlug = slug, ListingTitle = "صَفقَةٌ مُجاوِرَة" });
            await s.SaveChangesAsync();
        }
        Assert.Single(await EntriesForAsync(deal.Id));
        Assert.Single(await EntriesForAsync(other));
        output.WriteLine($"[P1] لِكُلِّ وَحدَة: deal.create — صَفَقاتٌ وُلِدَت 2 · سُطور {(await EntriesForAsync(deal.Id)).Count + (await EntriesForAsync(other)).Count}");
    }

    /// <summary>
    /// <para><b>التَحديثُ لا يُعَدّ — عَلى صَفقَةٍ بِلا سَطر</b> (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · M6A-CNT-1): صَفُّ التَحديثِ أَعلاه يُحَدِّثُ صَفقَةً سَطرُ
    /// وِلادَتِها قائِم، فَلَو عَدَّ المُستَمِعُ كُلَّ صَفقَةٍ مُخَزَّنَةٍ وِلادَةً لَابتَلَعَ <c>on conflict (id) do nothing</c> السَطرَ الثاني وبَقِيَ الصَفُّ أَخضَر —
    /// مَقيسٌ بِحَقنِ <c>new PendingDocument(d, true)</c>: كُلُّ صُفوفِ العَدِّ خَضراء. ويَومَ النَشرِ كُلُّ صَفقَةٍ في الإنتاجِ قَبلَ العَدّ (بِلا سَطر)، فَتَقَدُّمُها
    /// أَو إلحاقُ مَرجِعٍ بِها أَو مَحوُ طَرَفِها كانَ يَكتُبُ لَها <c>deal.create</c> ولا يَحمَرُّ شَيء.</para>
    /// <para>فَالصَفقَةُ هُنا بِلا سَطرٍ (يُحذَفُ سَطرُها كَما لَو وُلِدَت قَبلَ المُستَمِع)، ثُمَّ تُحَدَّثُ بِالأَشكالِ الثَلاثَةِ الَّتي يَكتُبُ بِها الكودُ صَفقَةً قائِمَة:
    /// <c>Load → Store</c>، و<c>Query → Store</c> (شَكلُ <c>AccountErasure</c>)، و<c>DealsService.AttachRefAsync</c> الحَقيقِيّ — وكُلُّ حِفظٍ يُرى نُسخَةً جَديدَةً في
    /// <c>mt_version</c> (لا أَداةَ عَمياء)، والسُطورُ لِمَصدَرِها صِفرٌ بَعدَ كُلٍّ.</para>
    /// </summary>
    [Fact]
    public async Task Updating_a_deal_born_before_the_meter_writes_no_entry_through_load_query_or_attach_ref()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
        var slug = NewSlug();

        var deal = await StartAsync(deals, slug, Guid.NewGuid());
        Assert.Single(await EntriesForAsync(deal.Id));
        await ScalarAsync("delete from meter.usage_entry where source_id = @s", ("s", deal.Id.ToString("D")));
        Assert.Empty(await EntriesForAsync(deal.Id));
        async Task<string> VersionAsync()
            => Convert.ToString(await ScalarAsync("select mt_version::text from platform.mt_doc_deal where id = @id", ("id", deal.Id))) ?? "";
        var v0 = await VersionAsync();

        await using (var s = host.Store.LightweightSession(slug))
        {
            var loaded = (await s.LoadAsync<Deal>(deal.Id))!;
            loaded.ListingTitle += " — Load";
            s.Store(loaded);
            await s.SaveChangesAsync();
        }
        var (vLoad, afterLoad) = (await VersionAsync(), (await EntriesForAsync(deal.Id)).Count);

        int queried;
        await using (var s = host.Store.LightweightSession(slug))
        {
            var rows = await s.Query<Deal>().Where(d => d.Id == deal.Id).ToListAsync();
            queried = rows.Count;
            foreach (var d in rows) { d.ListingTitle += " — Query"; s.Store(d); }
            await s.SaveChangesAsync();
        }
        var (vQuery, afterQuery) = (await VersionAsync(), (await EntriesForAsync(deal.Id)).Count);

        var attached = await deals.AttachRefAsync(slug, deal.Id, "meter_proof", "pre-meter");
        var (vAttach, afterAttach) = (await VersionAsync(), (await EntriesForAsync(deal.Id)).Count);

        output.WriteLine($"[P1·pre-meter] mt_version {v0} → Load {vLoad} → Query {vQuery} (صُفوف {queried}) → AttachRef {vAttach} · "
                         + $"سُطورُ deal.create لِمَصدَرِها: Load {afterLoad} · Query {afterQuery} · AttachRef {afterAttach}");
        Assert.Equal(1, queried);
        Assert.Equal("pre-meter", attached?.Refs.GetValueOrDefault("meter_proof"));
        Assert.True(new[] { v0, vLoad, vQuery, vAttach }.Distinct(StringComparer.Ordinal).Count() == 4, $"حِفظٌ لَم يُكتَب: {v0} · {vLoad} · {vQuery} · {vAttach}");
        Assert.Equal((0, 0, 0), (afterLoad, afterQuery, afterAttach));
    }

    /// <summary><b>التَراجُع</b> (D3): حِفظٌ يَفشَلُ بَعدَ أَن رَحَّلَ المُستَمِعُ سَطرَه لا يَترُكُ صَفقَةً ولا سَطراً.</summary>
    [Fact]
    public async Task A_failed_save_leaves_neither_the_deal_nor_its_entry()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = NewSlug();
        var existing = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Store(new Deal { Id = existing, TenantSlug = slug });
            await s.SaveChangesAsync();
        }
        Assert.Single(await EntriesForAsync(existing));

        var born = Guid.NewGuid();
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var s = host.Store.LightweightSession(slug);
            s.Store(new Deal { Id = born, TenantSlug = slug });
            s.Insert(new Deal { Id = existing, TenantSlug = slug });
            await s.SaveChangesAsync();
        });
        output.WriteLine($"[P1] التَراجُع: {ex.GetType().Name} · صَفقَة {await DealRowsAsync(born)} · سُطور {(await EntriesForAsync(born)).Count}");
        Assert.Equal(0, await DealRowsAsync(born));
        Assert.Empty(await EntriesForAsync(born));
        Assert.Single(await EntriesForAsync(existing));
    }

    // ─── ‏(‏2026-11-12، W13-6·٧) وَحَداتُ الأَحداث ──────────────────────────────────────────────────────────────────────────

    private sealed record EventEntry(string StreamEvents, string EventId, string EventXmin, string Id, string Unit, string App, string Feature,
                                     string SourceType, string SourceId, string WriterXid, string Entrepreneur, string Role);

    /// <summary>سُطورُ الدَفتَرِ لِأَحداثِ مَجرىً واحِد — بِالرَبطِ عَلى مُعَرِّفِ الحَدَثِ في <c>mt_events</c>، فَالمَصدَرُ يُقرَأُ مِن الحَدَثِ نَفسِه لا مِن نُسخَةٍ مِنه.</summary>
    private static async Task<List<EventEntry>> StreamEntriesAsync(Guid streamId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select (select count(*) from platform.mt_events where stream_id = @s)::text, ev.id::text, ev.xmin::text, e.id, e.unit, e.app, e.feature, "
            + "e.source_type, e.source_id, e.writer_xid::text, e.entrepreneur, e.role "
            + "from platform.mt_events ev join meter.usage_entry e on e.source_id = ev.id::text where ev.stream_id = @s order by e.id", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        var rows = new List<EventEntry>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new EventEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                                    r.GetString(7), r.GetString(8), r.GetString(9), r.GetString(10), r.GetString(11)));
        return rows;
    }

    private static async Task<long> StreamEventsAsync(Guid streamId)
        => Convert.ToInt64(await ScalarAsync("select count(*) from platform.mt_events where stream_id = @s", ("s", streamId)));

    /// <summary>
    /// <para><b>G5 لِوَحَداتِ الأَحداثِ الأَربَع</b> (‏2026-11-12، W13-6·٧؛ التَصميم §3.4 و§5.2 و§5.3): عَلى المَخزَنِ المُرَكَّبِ (المُستَمِعُ كَما يُسَجِّلُه القالِب)
    /// يُكتَبُ كُلُّ مَوضِعٍ بِشَكلِه — الإعلانُ بِصُوَرِه (<c>MTE:1301</c>)، والعَرض (<c>MTE:1595</c>)، والتَذكِرَة (<c>MTE:1139</c>)، والاشتِراكُ بِالخِدمَةِ الحَقيقِيَّة
    /// <c>PlanSubscribeService.SubscribeAsync</c> — فَلِكُلِّ مَجرىً يَبدَأ <b>سَطرٌ واحِد</b>: مُعَرِّفُه <c>{unit}:{slug}:{mt_events.id}</c>، والمَتجَرُ مُستَأجِرُ المَجرى،
    /// و<c>writer_xid</c> السَطرِ = <c>xmin</c> صَفِّ الحَدَث (مُعامَلَتُه نَفسُها، D2)، واللَقطَةُ المُباشِرَةُ في الحِفظِ نَفسِه لا سَطرَ لَها.</para>
    /// <para>ثُمَّ <b>ما لا يُعَدّ وما لا يُضاعَف</b>: الإلحاقُ بِمَجرىً قائِم (شاراتٌ، وحَدَثُ وِلادَةٍ يُلحَقُ ولا يَبدَأ) صِفر؛ وبِدءُ المَجرى نَفسِه ثانِيَةً يَرتَدُّ
    /// بِتَصادُمِ المُعَرِّف ولا سَطرَ ثانٍ؛ وحِفظٌ يَرتَدُّ لا يَترُكُ حَدَثاً ولا سَطراً لِمَجرىً بَدَأَ فيه؛ وإعادَةُ جُملَةِ السَطرِ نَفسِها في حِفظٍ ناجِحٍ لا تُضاعِف (D4).
    /// وكُلُّ خُطوَةٍ تَرى أَحداثَ المَجرى تَنمو أَو لا تَنمو — فَلا أَداةَ عَمياء.</para>
    /// </summary>
    [Fact]
    public async Task Each_event_unit_is_one_entry_per_started_stream_in_the_events_own_transaction_and_never_twice()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = NewSlug();
        var at = DateTime.UtcNow;
        var member = Guid.NewGuid();
        var (listingId, offerId, ticketId, subscriptionId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var planId = "w13m-" + Guid.NewGuid().ToString("N")[..8];

        async Task SaveAsync(Action<IDocumentSession> write)
        {
            await using var s = host.Store.LightweightSession(slug);
            write(s);
            await s.SaveChangesAsync();
        }

        await SaveAsync(s => s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listingId,
            new ACommerce.Kit.Listings.ListingCreated(listingId, slug, "إعلانُ العَدّ", null, 1m, "general", null, null, new() { ["owner_id"] = member.ToString() }, at),
            new ACommerce.Kit.Listings.ListingMediaSet(listingId, [$"tenants/{slug}/listings/{listingId}/0.jpg"], at)));
        await SaveAsync(s => s.Events.StartStream<ACommerce.Kit.Offers.Offer>(offerId,
            new ACommerce.Kit.Offers.OfferSubmitted(offerId, listingId, member, "عارِضُ العَدّ", 1m, null, 24.7, 46.6, at.AddMinutes(15), at)));
        await SaveAsync(s => s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ticketId,
            new ACommerce.Kit.Support.TicketCreated(ticketId, member, "كاتِبُ العَدّ", "تَذكِرَةُ العَدّ", "سُؤال", at)));
        await SaveAsync(s => s.Insert(new ACommerce.Kit.Subscriptions.Plan { Id = planId, Name = "باقَةُ العَدّ", ListingsQuota = 3, DaysPeriod = 30 }));
        await using (var s = host.Store.LightweightSession(slug))
        {
            var outcome = await ACommerce.Templates.Customer.Marketplace.Services.Subscriptions.PlanSubscribeService.SubscribeAsync(
                s, planId, member, paymentProviderConfigured: false, at, subscriptionId);
            Assert.True(outcome.Granted, $"الباقَةُ لَم تُمنَح: {outcome.Route} {outcome.Refusal}");
            await s.SaveChangesAsync();
        }

        var streams = new (Guid Stream, string Unit, string Snapshot)[]
        {
            (listingId, "listing.create", "mt_doc_listing"), (offerId, "offer.submit", "mt_doc_offer"),
            (ticketId, "ticket.open", "mt_doc_ticket"), (subscriptionId, "member_plan.subscribe", "mt_doc_subscription"),
        };
        var born = new Dictionary<Guid, EventEntry>();
        foreach (var (stream, unit, snapshot) in streams)
        {
            var e = Assert.Single(await StreamEntriesAsync(stream));
            var snapXmin = Convert.ToString(await ScalarAsync($"select xmin::text from platform.{snapshot} where id = @id", ("id", stream)));
            output.WriteLine($"[P1·حَدَث] {unit}: أَحداثُ المَجرى {e.StreamEvents} · سُطور 1 · {e.Id} · writer_xid {e.WriterXid} = xmin الحَدَث {e.EventXmin} "
                             + $"= xmin اللَقطَة {snapXmin} · entrepreneur={e.Entrepreneur} · role={e.Role}");
            Assert.Equal((unit, slug, $"{unit}:{slug}:{e.EventId}", MeterUnitCatalog.Default.Find(unit)!.Feature, "event", e.EventId),
                         (e.Unit, e.App, e.Id, e.Feature, e.SourceType, e.SourceId));
            Assert.Equal(e.EventXmin, e.WriterXid);
            Assert.Equal(e.EventXmin, snapXmin);
            Assert.DoesNotContain(member.ToString("D"), e.Id + " " + e.SourceId, StringComparison.OrdinalIgnoreCase);
            born[stream] = e;
        }
        Assert.Equal("2", born[listingId].StreamEvents);

        // الإلحاقُ بِمَجرىً قائِمٍ لا يُعَدّ — ولا حَدَثُ وِلادَةٍ يُلحَقُ ولا يَبدَأ.
        await SaveAsync(s =>
        {
            s.Events.Append(listingId, new ACommerce.Kit.Listings.ListingFlagsSet(listingId, true, true, at));
            s.Events.Append(listingId, new ACommerce.Kit.Listings.ListingCreated(listingId, slug, "مُكَرَّر", null, 1m, "general", null, null, [], at));
            s.Events.Append(ticketId, new ACommerce.Kit.Support.TicketReplied(ticketId, Guid.NewGuid(), "كاتِبُ العَدّ", false, "رَدّ", at));
        });
        var (listingEvents, ticketEvents) = (await StreamEventsAsync(listingId), await StreamEventsAsync(ticketId));
        output.WriteLine($"[P1·حَدَث] الإلحاق: أَحداثُ الإعلان 2 ← {listingEvents} · أَحداثُ التَذكِرَة 1 ← {ticketEvents} · سُطورُهُما "
                         + $"{(await StreamEntriesAsync(listingId)).Count} و{(await StreamEntriesAsync(ticketId)).Count}");
        Assert.Equal((4L, 2L), (listingEvents, ticketEvents));
        Assert.Equal(born[listingId], Assert.Single(await StreamEntriesAsync(listingId)) with { StreamEvents = born[listingId].StreamEvents });
        Assert.Single(await StreamEntriesAsync(ticketId));

        // الإعادَة: بِدءُ المَجرى نَفسِه ثانِيَةً (نَقرَتانِ بِمُعَرِّفٍ واحِد) يَرتَدُّ بِتَصادُمِ المُعَرِّف، والسَطرُ واحِد.
        var again = await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(s => s.Events.StartStream<ACommerce.Kit.Offers.Offer>(offerId,
            new ACommerce.Kit.Offers.OfferSubmitted(offerId, listingId, member, "عارِضُ العَدّ", 1m, null, 24.7, 46.6, at.AddMinutes(15), at))));
        output.WriteLine($"[P1·حَدَث] بِدءُ المَجرى ثانِيَةً: {again.GetType().Name} · أَحداثُ العَرض {await StreamEventsAsync(offerId)} · سُطورُه {(await StreamEntriesAsync(offerId)).Count}");
        Assert.Contains("ExistingStreamIdCollision", again.GetType().Name, StringComparison.Ordinal);
        Assert.Equal(1L, await StreamEventsAsync(offerId));
        Assert.Single(await StreamEntriesAsync(offerId));

        // التَراجُع: مَجرىً جَديدٌ يَبدَأُ في حِفظٍ يَرتَدّ ⇐ لا حَدَثَ ولا سَطر.
        var rolledBack = Guid.NewGuid();
        var failed = await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(s =>
        {
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(rolledBack,
                new ACommerce.Kit.Support.TicketCreated(rolledBack, member, "كاتِبُ العَدّ", "تَذكِرَةٌ تَرتَدّ", "سُؤال", at));
            s.Events.StartStream<ACommerce.Kit.Offers.Offer>(offerId,
                new ACommerce.Kit.Offers.OfferSubmitted(offerId, listingId, member, "عارِضُ العَدّ", 1m, null, 24.7, 46.6, at.AddMinutes(15), at));
        }));
        var rolledEntries = Convert.ToInt64(await ScalarAsync("select count(*) from meter.usage_entry where app = @a and unit = 'ticket.open'", ("a", slug)));
        output.WriteLine($"[P1·حَدَث] التَراجُع: {failed.GetType().Name} · أَحداثُ التَذكِرَةِ المُرتَدَّة {await StreamEventsAsync(rolledBack)} · سُطورُ ticket.open لِلمَتجَر {rolledEntries}");
        Assert.Equal(0L, await StreamEventsAsync(rolledBack));
        Assert.Equal(1L, rolledEntries);

        // ‏D4 لِلحَدَث: جُملَةُ السَطرِ نَفسُها في حِفظٍ ناجِحٍ مَعَ وِلادَةِ تَذكِرَةٍ أُخرى — لا تَرمي ولا تُضاعِف، والتَذكِرَةُ الجَديدَةُ سَطرُها واحِد.
        var l = born[listingId];
        var next = Guid.NewGuid();
        await SaveAsync(s =>
        {
            s.QueueSqlCommand(MeterSql.Insert, l.Id, l.Unit, 1L, l.App, l.Entrepreneur, l.Role, l.Feature, MeterSentinels.None, l.SourceType, l.SourceId, 0);
            s.Events.StartStream<ACommerce.Kit.Support.Ticket>(next,
                new ACommerce.Kit.Support.TicketCreated(next, member, "كاتِبُ العَدّ", "تَذكِرَةٌ ثانِيَة", "سُؤال", at));
        });
        Assert.Single(await StreamEntriesAsync(listingId));
        Assert.Single(await StreamEntriesAsync(next));
        var perUnit = await ScalarAsync("select string_agg(unit || ' ' || n, ' · ' order by unit) from (select unit, count(*) n from meter.usage_entry where app = @a group by unit) t", ("a", slug));
        output.WriteLine($"[P1·حَدَث] لِكُلِّ وَحدَةٍ في /{slug}: {perUnit} — مَجارٍ بَدَأَت 5 (إعلان · عَرض · تَذكِرَتان · اشتِراك)");
        Assert.Equal("listing.create 1 · member_plan.subscribe 1 · offer.submit 1 · ticket.open 2", perUnit);
    }

    // ─── صَفُّ الأُنبوبِ الحَقيقِيّ ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class Pipeline : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        private Pipeline(WebApplication app, HttpClient client) { _app = app; Client = client; }

        /// <param name="baseDomain"><c>null</c> = وَضعُ المَسار (النِطاقُ فارِغ). ومُهَيَّأً (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ الجَولَةُ الثانِيَة · M6A-SEC-R2-1)
        /// يُرَكَّبُ الأُنبوبُ كَما يُرَكِّبُه <c>UsePlatformHost</c>: مُقَدِّمَةُ الطَلَبِ (<c>UsePlatformRequestFront</c> — الحاقِنُ بَينَ حارِسِ المُضيفِ وفَحصِ
        /// <c>Origin</c>) ثُمَّ الحالّ، وقَوالِبُ صَفَحاتِ الدَورِ مِن حاوِيَةِ القالِبِ (<paramref name="rolePages"/>) كَما يُسَجِّلُها.</param>
        public static async Task<Pipeline> StartAsync(IDocumentStore store, DealsService deals, string? baseDomain = null, RolePageTemplates? rolePages = null)
        {
            for (var attempt = 1; ; attempt++)
            {
                var builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton(baseDomain is null ? new SubdomainTenancyOptions() : new SubdomainTenancyOptions { BaseDomain = baseDomain });
                if (rolePages is not null) builder.Services.AddSingleton(rolePages);
                builder.Services.AddSingleton(store);
                builder.Services.AddMemoryCache();
                using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
                var app = builder.Build();
                app.Urls.Add($"http://127.0.0.1:{port}");
                if (baseDomain is not null) app.UsePlatformRequestFront();
                app.UsePlatformMultiTenancy();
                // ‏جِسمُ الطَلَبِ بَعدَ الوَسيط: المَتجَرُ الَّذي حَلَّه، ووِلادَةُ صَفقَةٍ بِالخِدمَةِ الحَقيقِيَّة.
                app.Run(async ctx =>
                {
                    // ‏مَسارٌ لا يَحُلُّه الوَسيطُ يَرمي (500) فَيَحمَرُّ الصَفُّ — لا جَوابَ يُشبِهُ النَجاح.
                    var slug = ctx.Items[TenantKeys.Slug] as string ?? throw new InvalidOperationException("الوَسيطُ لَم يَحُلَّ مَتجَراً.");
                    var deal = await deals.StartAsync(slug, "marketplace", Guid.NewGuid(), "مُشتَري الأُنبوب", null, "صَفقَةُ الأُنبوب", 0m);
                    await ctx.Response.WriteAsync(deal.Id.ToString("D"));
                });
                try { await app.StartAsync(); }
                catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
                return new Pipeline(app, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(60) });
            }
        }

        /// <summary>طَلَبُ كِتابَةٍ بِرَأسِ <c>Host</c> — كَما يُرسِلُ المُتَصَفِّحُ «اطلُب» عَلى مُضيفِ المَتجَرِ أَو مُضيفِ الدَور.</summary>
        public async Task<HttpResponseMessage> PostAsync(string host, string path)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent([]) };
            req.Headers.Host = host;
            return await Client.SendAsync(req);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary><b>رائِدُ الأَعمالِ مِن المُحَلِّلِ الحَقيقِيّ</b> (G5، RULES-03): طَلَبٌ عَبرَ Kestrel إلى مَتجَرٍ ← الوَسيطُ يَفتَحُ الحامِلَ ويَملَؤُه مِن
    /// وَثيقَتِه ← <c>DealsService.StartAsync</c> ← السَطر: المَتجَرُ = الـslug، ورائِدُ الأَعمالِ = <c>Tenant.OwnerUserId</c>، والدَورُ <c>_none</c> لِمَتجَرٍ بِلا
    /// أَدوار. ومَتجَرٌ بِلا مالِكٍ <c>_unowned</c>، ومَتجَرٌ بِأَدوارٍ دَورُه <c>_unresolved</c> حَتّى يَحسِبَه البَند ١٠ — لا دَورُ مُضيف. وكُلُّ طَلَبٍ بِحامِلِه:
    /// المَتجَرُ ب لا يَرِثُ مالِكَ أ.</summary>
    [Fact]
    public async Task Through_the_real_resolver_pipeline_the_entrepreneur_is_the_store_owner()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();

        var (owned, unowned, roled) = (NewSlug(), NewSlug(), NewSlug());
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = owned, Name = "مَتجَرُ الأُنبوب", OwnerUserId = owner });
            s.Insert(new Tenant { Id = unowned, Name = "مَتجَرٌ بِلا مالِك", OwnerUserId = Guid.Empty });
            s.Insert(new Tenant { Id = roled, Name = "مَتجَرٌ بِأَدوار", OwnerUserId = owner, Roles = [new ACommerce.Kit.Roles.Role { Slug = "vendor", CatalogSlug = "vendor" }] });
            await s.SaveChangesAsync();
        }

        await using var pipe = await Pipeline.StartAsync(host.Store, deals);
        async Task<Row> OrderAsync(string slug)
        {
            using var res = await pipe.Client.GetAsync($"/{slug}/meter-proof");
            var body = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{slug}: {(int)res.StatusCode} {body}");
            var row = Assert.Single(await EntriesForAsync(Guid.Parse(body)));
            output.WriteLine($"[P1] الأُنبوبُ الحَقيقِيّ /{slug}: app={row.App} · entrepreneur={row.Entrepreneur} · role={row.Role} · generation={row.Generation}");
            return row;
        }

        var a = await OrderAsync(owned);
        Assert.Equal((owned, owner.ToString("D"), MeterSentinels.None), (a.App, a.Entrepreneur, a.Role));
        var b = await OrderAsync(unowned);
        Assert.Equal((unowned, MeterSentinels.Unowned, MeterSentinels.None), (b.App, b.Entrepreneur, b.Role));
        var c = await OrderAsync(roled);
        Assert.Equal((roled, owner.ToString("D"), MeterSentinels.Unresolved), (c.App, c.Entrepreneur, c.Role));
        var again = await OrderAsync(owned);
        Assert.Equal((owned, owner.ToString("D")), (again.App, again.Entrepreneur));
    }

    /// <summary>
    /// <para><b>وَضعُ المُضيف: المُحَلِّلُ يَملَأُ الحامِلَ عَلى مُضيفِ المَتجَرِ ومُضيفِ الدَور — ودَورُ المُضيفِ لا يَدخُلُه</b> (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ الجَولَةُ
    /// الثانِيَة · M6A-SEC-R2-1؛ ADR-051 §١١ «ولا دَورَ المُضيف»). الصَفُّ أَعلاه يُرَكِّبُ النِطاقَ فارِغاً فَلا يَقيسُ إلّا المَسار — وحَقنانِ في الوَسيطِ
    /// بَقِيا أَخضَرَينِ عِندَ المُراجِع: دَورُ المُضيفِ في الحامِل، ومُضيفُ المَتجَرِ بِلا مَلء. وهُنا الأُنبوبُ كَما يُرَكِّبُه <c>Program</c> بِـ
    /// <c>MultiTenancy__BaseDomain=localhost</c>، ومَتجَرٌ يَحمِلُ دَورَ الكَتالوجِ <c>vendor</c> ولَه مالِكٌ حَقيقِيّ:</para>
    /// <para>• <c>{slug}.localhost</c> ⇐ رائِدُ الأَعمالِ = <c>Tenant.OwnerUserId</c> (لا <c>_unresolved</c>)؛
    /// • <c>{slug}-vendor.localhost</c> ⇐ الدَورُ <b>لَيسَ</b> <c>vendor</c> (<c>_unresolved</c> حَتّى يَحسِبَه البَند ١٠)، ورائِدُ الأَعمالِ المالِكُ نَفسُه.
    /// والصَفقَةُ تولَدُ عَلى مُضيفِ الدَورِ فِعلاً (مَقيسٌ عِندَ المُراجِع: <c>/listings/{id}/deal</c> عَلى <c>ejar-vendor.localhost</c> ⇐ 302 إلى الصَفَقات)،
    /// فَمَلءُ الحامِلِ بِدَورِ المُضيفِ يَنسِبُ عَمَلِيَّةَ المُشتَري إلى البائِع — ويَسبِقُ الدَورَ الفَعّالَ (أَوَّلُ كاتِبٍ يَفوز).</para>
    /// </summary>
    [Fact]
    public async Task On_store_and_role_hosts_the_resolver_fills_the_owner_and_never_the_host_role()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();

        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ المُضيف", OwnerUserId = owner,
                                  Roles = [new ACommerce.Kit.Roles.Role { Slug = "vendor", CatalogSlug = "vendor" }] });
            await s.SaveChangesAsync();
        }
        // ‏المُضيفانِ يُحَلّانِ كَما يَحُلُّهُما الوَسيط — فَلا يَخضَرُّ الصَفُّ عَلى مُضيفٍ لا يَبلُغُ الحالّ.
        var storeHost = $"{slug}.localhost";
        var roleHost = $"{slug}-vendor.localhost";
        Assert.Equal(slug, TenantResolverMiddleware.StoreSlugFromHost(storeHost, "localhost"));
        Assert.Equal<(string, string)?>((slug, "vendor"), TenantResolverMiddleware.RoleFromHost(roleHost, "localhost"));

        await using var pipe = await Pipeline.StartAsync(host.Store, deals, baseDomain: "localhost",
                                                         rolePages: host.Services.GetRequiredService<RolePageTemplates>());
        async Task<Row> OrderOnAsync(string hostName)
        {
            using var res = await pipe.PostAsync(hostName, "/meter-proof");
            var body = await res.Content.ReadAsStringAsync();
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{hostName}: {(int)res.StatusCode} {body}");
            var row = Assert.Single(await EntriesForAsync(Guid.Parse(body)));
            output.WriteLine($"[P1] وَضعُ المُضيف {hostName}: app={row.App} · entrepreneur={row.Entrepreneur} · role={row.Role} · generation={row.Generation}");
            return row;
        }

        var onStore = await OrderOnAsync(storeHost);
        Assert.Equal((slug, owner.ToString("D"), MeterSentinels.Unresolved), (onStore.App, onStore.Entrepreneur, onStore.Role));
        var onRole = await OrderOnAsync(roleHost);
        Assert.NotEqual("vendor", onRole.Role);
        Assert.Equal((slug, owner.ToString("D"), MeterSentinels.Unresolved), (onRole.App, onRole.Entrepreneur, onRole.Role));
    }

    /// <summary><b>G17 حَيّاً</b>: لا مُعَرِّفَ سَطرٍ ولا مَصدَرَ يَحمِلُ مُعَرِّفَ مُستَخدِمٍ مَخزون — عَلى الدَفتَرِ كُلِّه، بَعدَ صَفقَةٍ مُبادِرُها مُستَخدِمٌ حَقيقِيّ.</summary>
    [Fact]
    public async Task No_entry_id_or_source_carries_a_stored_user_id()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
        var slug = NewSlug();
        var member = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999) };
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Insert(member);
            await s.SaveChangesAsync();
        }
        await StartAsync(deals, slug, member.Id);

        var users = new List<string>();
        var entries = new List<(string Id, string Source)>();
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand("select id::text from platform.mt_doc_user", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) users.Add(r.GetString(0));
            await using (var cmd = new NpgsqlCommand("select id, source_id from meter.usage_entry", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) entries.Add((r.GetString(0), r.GetString(1)));
        }
        var leaks = entries.Where(e => users.Any(u => e.Id.Contains(u, StringComparison.OrdinalIgnoreCase)
                                                    || e.Source.Contains(u, StringComparison.OrdinalIgnoreCase)
                                                    || e.Id.Contains(u.Replace("-", ""), StringComparison.OrdinalIgnoreCase))).ToList();
        output.WriteLine($"[G17] سُطورٌ مَفحوصَة {entries.Count} · مُعَرِّفاتُ مُستَخدِمين {users.Count} · تَسَرُّب {leaks.Count}");
        Assert.True(entries.Count > 0 && users.Count > 0, $"أَداةٌ عَمياء: {entries.Count} سَطراً و{users.Count} مُستَخدِماً.");
        Assert.Contains(member.Id.ToString("D"), users);
        Assert.Empty(leaks);
    }
}
