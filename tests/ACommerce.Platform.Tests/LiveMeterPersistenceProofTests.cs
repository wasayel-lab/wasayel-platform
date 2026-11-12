using System.Net;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
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

    // ═══ ‏(‏2026-11-12، W13-6·٨) وَحَداتُ الوَثائِقِ الأَربَعُ بِأَبوابِها الحَقيقِيَّة، وحُرّاسُ الاستوديو والإدارَةِ يَملَؤونَ الحامِلَ بَعدَ `await` قِراءَتِهِم ═══
    // (التَصميم §3.4 و§5.2 و§5.4 و§13 P1). المُضيفُ المُصَغَّرُ لا يُشَغِّلُ وَسيطَ المُستَأجِر، فَيَفتَحُ الصَفُّ الحامِلَ قَبلَ كُلِّ بابٍ كَما يَفتَحُه الوَسيطُ في
    // أَوَّلِ سَطرٍ (`MeterScope.Open`) — وعَلى `/studio` و`/admin` لا يُملأُ (مَحجوزان: صَفُّ G16)، وعَلى مَسارِ المَتجَرِ يُملأُ كَما يَملَؤُه لِمَتجَرٍ بِلا أَدوار.
    // والبابُ يُنادى في الجَريانِ نَفسِه، فَما يَكتُبُه حارِسُه في الحامِلِ يُقرَأُ بَعدَه.

    private sealed record DocRow(string Unit, string App, string Entrepreneur, string Role, string SourceType, string SourceId, bool SameTransaction);

    /// <summary>سُطورُ وَحدَةٍ لِمَصدَرٍ واحِد، ومَعَ كُلٍّ: <c>writer_xid</c> = <c>xmin</c> صَفِّ الوَثيقَةِ لَحظَةَ القِراءَة (D2) — يُقرَأُ قَبلَ أَيِّ تَحديثٍ لَها.</summary>
    private static async Task<List<DocRow>> DocEntriesAsync(string unit, string sourceId, string table, object docId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select e.unit, e.app, e.entrepreneur, e.role, e.source_type, e.source_id, "
            + $"coalesce(e.writer_xid::text = (select d.xmin::text from platform.{table} d where d.id = @d), false) "
            + "from meter.usage_entry e where e.unit = @u and e.source_id = @s order by e.id", conn);
        cmd.Parameters.AddWithValue("u", unit);
        cmd.Parameters.AddWithValue("s", sourceId);
        cmd.Parameters.AddWithValue("d", docId);
        var rows = new List<DocRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new DocRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetBoolean(6)));
        return rows;
    }

    private static async Task<long> UnitEntriesAsync(string unit, string sourceId)
        => Convert.ToInt64(await ScalarAsync("select count(*) from meter.usage_entry where unit = @u and source_id = @s", ("u", unit), ("s", sourceId)));

    /// <summary><b>التَحديثُ يُقاسُ عَلى وَثيقَةٍ بِلا سَطر</b> (دَرسُ M6A-CNT-1): المُعَرِّفُ حَتمِيٌّ و<c>ON CONFLICT DO NOTHING</c> يَبتَلِعُ تَحديثاً عُدَّ وِلادَةً ما دامَ
    /// سَطرُ الوِلادَةِ قائِماً — فَيُحذَفُ السَطرُ كَأَنّ الوَثيقَةَ وُلِدَت قَبلَ المُستَمِع، ثُمَّ يُحَدَّث، فَصِفرٌ هُوَ الحُكم.</summary>
    private static Task ForgetBirthAsync(string unit, string sourceId)
        => ScalarAsync("delete from meter.usage_entry where unit = @u and source_id = @s", ("u", unit), ("s", sourceId));

    private static string NewPhone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary><b><c>studio.account.create</c> وبابا <c>store.create</c></b>: الدُخولُ الأَوَّلُ إلى الاستوديو بِبابَيه سَطرٌ واحِدٌ عَلى <c>_platform</c> رائِدُ أَعمالِه
    /// الحِسابُ نَفسُه، والدُخولُ الثاني تَحديثٌ لا يُعَدّ؛ و<c>POST /admin/tenants/create</c> بِمُشرِفِ المَنَصَّةِ سَطرُ <c>store.create</c> مالِكُه المُنشِئُ
    /// مِن الوَثيقَةِ ودَورُه <c>_platform_admin</c> — يَضَعُه <c>PlatformAdminGuard</c> في الحامِلِ ولا يَدَّعي مَتجَراً — وبِغَيرِ مُشرِفٍ <c>403</c> وحامِلٌ فارِغٌ
    /// ولا سَطر؛ وبابُ الاستوديو سَطرٌ بِمالِكِه؛ وكُلٌّ في مُعامَلَةِ وَثيقَتِه؛ و<c>Store</c> مَتجَرٍ قائِمٍ لا يُعَدّ.</summary>
    [Fact]
    public async Task Studio_sign_up_and_both_store_doors_are_one_entry_each_in_the_documents_transaction_and_the_admin_guard_fills_only_the_role()
    {
        if (!Enabled) return;
        var sms = new LiveContactNoticeProofTests.CaptureSms();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IOtpChannel>(sms));
        var none = new Dictionary<string, string>();

        // ١ · حِسابُ الاستوديو: الدُخولُ الأَوَّلُ وِلادَة، والثاني تَحديثٌ (LastLoginAt).
        var phone = NewPhone();
        async Task<(int Status, string Location)> SignInAsync()
        {
            MeterScope.Open();
            Assert.Equal(302, (await host.PostStudioFormAsync("/studio/auth/login", none, null, ("phone", phone))).Status);
            MeterScope.Open();
            return await host.PostStudioFormAsync("/studio/auth/verify", none, null, ("method", "phone"), ("phone", phone), ("code", sms.Code!));
        }
        var first = await SignInAsync();
        StudioUser account;
        await using (var q = host.Store.QuerySession(StudioAuth.Tenant))
            account = Assert.Single(await q.Query<StudioUser>().Where(u => u.Phone == PhoneNumber.Canonical(phone)).ToListAsync());
        var accountId = account.Id.ToString("D");
        var signUp = await DocEntriesAsync(MeterRules.StudioAccountCreate, accountId, "mt_doc_studiouser", account.Id);
        await ForgetBirthAsync(MeterRules.StudioAccountCreate, accountId);
        var second = await SignInAsync();
        var afterSecond = await UnitEntriesAsync(MeterRules.StudioAccountCreate, accountId);

        // ٢ · بابُ المُشرِف: مُشرِفٌ يُقبَلُ ويُنشِئ، وغَيرُ مُشرِفٍ يُرَدّ.
        var admin = await LiveStoreBirthOnceCharacterizationTests.UserAsync(host, consented: false, platformAdmin: true);
        var plain = await LiveStoreBirthOnceCharacterizationTests.UserAsync(host, consented: false);
        var owner = await LiveStoreBirthOnceCharacterizationTests.UserAsync(host);
        var adminSlug = LiveStoreBirthOnceCharacterizationTests.NewSlug("w13m8a-");
        var refusedSlug = LiveStoreBirthOnceCharacterizationTests.NewSlug("w13m8r-");
        var studioSlug = LiveStoreBirthOnceCharacterizationTests.NewSlug("w13m8s-");
        try
        {
            var adminHolder = MeterScope.Open();
            var created = await LiveStoreBirthOnceCharacterizationTests.AdminCreateAsync(host, admin, adminSlug, "مَتجَرُ المُشرِف");
            var adminView = adminHolder.View();
            var byAdmin = await DocEntriesAsync(MeterRules.StoreCreate, adminSlug, "mt_doc_tenant", adminSlug);

            var refusedHolder = MeterScope.Open();
            var refused = await LiveStoreBirthOnceCharacterizationTests.AdminCreateAsync(host, plain, refusedSlug, "مَرفوض");
            var refusedView = refusedHolder.View();
            var refusedRows = await UnitEntriesAsync(MeterRules.StoreCreate, refusedSlug);

            // ٣ · بابُ الاستوديو: المالِكُ مِن الوَثيقَة، ولا حارِسَ يَملَأُ الدَورَ عَلى /studio/new.
            MeterScope.Open();
            var built = await LiveStoreBirthOnceCharacterizationTests.StudioCreateAsync(host, owner, studioSlug, "مَتجَرُ الاستوديو");
            var byStudio = await DocEntriesAsync(MeterRules.StoreCreate, studioSlug, "mt_doc_tenant", studioSlug);

            // ٤ · Store مَتجَرٍ قائِم (بِلا سَطر): تَحديثٌ لا وِلادَة.
            await ForgetBirthAsync(MeterRules.StoreCreate, studioSlug);
            MeterScope.Open();
            await using (var s = host.Store.LightweightSession())
            {
                var t = (await s.LoadAsync<Tenant>(studioSlug))!;
                t.Name = "اسمٌ جَديد";
                s.Store(t);
                await s.SaveChangesAsync();
            }
            var afterUpdate = await UnitEntriesAsync(MeterRules.StoreCreate, studioSlug);

            output.WriteLine($"[P1·وَثيقَة] الاستوديو: الدُخولُ ⇒ {first.Status} {first.Location} · «{string.Join(" | ", signUp)}» · الثاني (بَعدَ حَذفِ سَطرِ الوِلادَة) ⇒ {second.Status} · سُطور {afterSecond}");
            output.WriteLine($"[P1·وَثيقَة] المُشرِف ⇒ {created.Status} {created.Location} · الحامِل {adminView} · «{string.Join(" | ", byAdmin)}» · غَيرُ المُشرِف ⇒ {refused.Status} · الحامِل {refusedView} · سُطور {refusedRows}");
            output.WriteLine($"[P1·وَثيقَة] بابُ الاستوديو ⇒ {built.Status} {built.Location} · «{string.Join(" | ", byStudio)}» · بَعدَ Store المَتجَرِ بِلا سَطر {afterUpdate}");

            Assert.Equal(302, first.Status);
            Assert.DoesNotContain("err=", first.Location, StringComparison.Ordinal);
            Assert.Equal(new DocRow(MeterRules.StudioAccountCreate, MeterSentinels.Platform, accountId, MeterSentinels.Unresolved, MeterRules.StudioUserSource, accountId, true),
                         Assert.Single(signUp));
            Assert.Equal(302, second.Status);
            Assert.Equal(0L, afterSecond);

            Assert.Equal((302, "/admin"), created);
            Assert.Equal(new MeterScopeView(null, null, MeterSentinels.PlatformAdmin, false), adminView);
            Assert.Equal(new DocRow(MeterRules.StoreCreate, adminSlug, admin.User.Id.ToString("D"), MeterSentinels.PlatformAdmin, MeterRules.TenantSource, adminSlug, true),
                         Assert.Single(byAdmin));
            Assert.Equal(403, refused.Status);
            Assert.Equal(new MeterScopeView(null, null, null, false), refusedView);
            Assert.Equal(0L, refusedRows);

            Assert.Equal((302, $"/studio/apps/{studioSlug}?built=1"), built);
            Assert.Equal(new DocRow(MeterRules.StoreCreate, studioSlug, owner.User.Id.ToString("D"), MeterSentinels.Unresolved, MeterRules.TenantSource, studioSlug, true),
                         Assert.Single(byStudio));
            Assert.Equal(0L, afterUpdate);
        }
        finally { await LiveStoreBirthOnceCharacterizationTests.CleanAsync(host, [admin, plain, owner], adminSlug, refusedSlug, studioSlug); }
    }

    /// <summary><b>حُرّاسُ الاستوديو والإدارَةِ يَملَؤونَ الحامِلَ بَعدَ <c>await</c> قِراءَتِهِم عَلى أَبوابِهِم الحَقيقِيَّة</b> (التَصميم §5.4، COR-1): <c>StudioOwnsAsync</c>
    /// لِلمالِكِ (المَتجَرُ ومالِكُه و<c>_entrepreneur</c>) ولا شَيءَ لِغَيرِه؛ و<c>TenantAdminGuard</c> لِلمالِكِ مِن الاستوديو (<c>_entrepreneur</c>) ولِعُضوٍ دَورُه
    /// الفَعّالُ يَحمِلُ <c>tenant.manage</c> (دَورُ المَتجَرِ الَّذي قَرَأَه) ولا شَيءَ لِعُضوٍ بِلا الصَلاحِيَّة.</summary>
    [Fact]
    public async Task The_studio_owner_check_and_the_store_admin_guard_fill_the_holder_after_their_await_on_their_real_doors()
    {
        if (!Enabled) return;
        await using var host = LiveStoreBirthOnceCharacterizationTests.HostWithPhone();
        var owner = await LiveStoreBirthOnceCharacterizationTests.UserAsync(host);
        var other = await LiveStoreBirthOnceCharacterizationTests.UserAsync(host);
        var slug = LiveStoreBirthOnceCharacterizationTests.NewSlug("w13m8g-");
        try
        {
            Assert.Equal((302, $"/studio/apps/{slug}?built=1"), await LiveStoreBirthOnceCharacterizationTests.StudioCreateAsync(host, owner, slug, "مَتجَرُ الحُرّاس"));
            var adminRole = ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find("tenant_admin")!, sortOrder: 0);
            var plainRole = ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find("customer")!, sortOrder: 1);
            await using (var s = host.Store.LightweightSession())
            {
                var t = (await s.LoadAsync<Tenant>(slug))!;
                t.Roles.Add(adminRole);
                t.Roles.Add(plainRole);
                s.Store(t);
                await s.SaveChangesAsync();
            }
            var manager = new User { Id = Guid.NewGuid(), Phone = NewPhone(), FullName = "مُديرُ المَتجَر", ActiveRole = adminRole.Slug, Role = adminRole.Slug };
            var member = new User { Id = Guid.NewGuid(), Phone = NewPhone(), FullName = "عُضو", ActiveRole = plainRole.Slug, Role = plainRole.Slug };
            await using (var s = host.Store.LightweightSession(slug)) { s.Store(manager); s.Store(member); await s.SaveChangesAsync(); }

            var studioRoute = new Dictionary<string, string> { ["slug"] = slug };
            var grant = new Dictionary<string, string> { ["slug"] = slug, ["userId"] = Guid.NewGuid().ToString() };
            const string regions = "/studio/apps/{slug}/regions/save";
            const string grantAdmin = "/admin/tenants/{slug}/users/{userId:guid}/grant-admin";

            var h1 = MeterScope.Open();
            var r1 = await host.PostStudioFormAsync(regions, studioRoute, owner.Token, ("regions", "الرِياض"));
            var h2 = MeterScope.Open();
            var r2 = await host.PostStudioFormAsync(regions, studioRoute, other.Token, ("regions", "الرِياض"));
            var h3 = MeterScope.Open();
            var r3 = await host.PostStudioFormAsync(grantAdmin, grant, owner.Token);
            var h4 = MeterScope.Open();
            var r4 = await host.PostFormAsync(grantAdmin, grant, slug, AuthHandlers.MakeToken(manager.Id, slug, manager.SessionVersion));
            var h5 = MeterScope.Open();
            var r5 = await host.PostFormAsync(grantAdmin, grant, slug, AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion));
            var ownerId = owner.User.Id.ToString("D");
            output.WriteLine($"[G16·باب] StudioOwnsAsync: المالِك ⇒ {r1.Status} {r1.Location} · {h1.View()} | غَيرُه ⇒ {r2.Status} {r2.Location} · {h2.View()}");
            output.WriteLine($"[G16·باب] TenantAdminGuard: المالِك ⇒ {r3.Status} {r3.Location} · {h3.View()} | المُدير ({adminRole.Slug}) ⇒ {r4.Status} {r4.Location} · {h4.View()} | العُضو ⇒ {r5.Status} · {h5.View()}");

            Assert.NotEqual("/studio", r1.Location);
            Assert.Equal(new MeterScopeView(slug, ownerId, MeterSentinels.Entrepreneur, false), h1.View());
            Assert.Equal((302, "/studio"), r2);
            Assert.Equal(new MeterScopeView(null, null, null, false), h2.View());
            Assert.Equal(302, r3.Status);
            Assert.Equal(new MeterScopeView(slug, ownerId, MeterSentinels.Entrepreneur, false), h3.View());
            Assert.Equal(302, r4.Status);
            Assert.Equal(new MeterScopeView(slug, ownerId, adminRole.Slug, false), h4.View());
            Assert.Equal(403, r5.Status);
            Assert.Equal(new MeterScopeView(null, null, null, false), h5.View());
        }
        finally { await LiveStoreBirthOnceCharacterizationTests.CleanAsync(host, [owner, other], slug); }
    }

    /// <summary><b><c>member.join</c> و<c>review.create</c></b>: الانضِمامُ بِبابَي الجَوّالِ سَطرٌ واحِدٌ في مَتجَرِه — <b>مَصدَرُه اسمٌ مُستَعارٌ</b> لا مُعَرِّفُ العُضو —
    /// والدُخولُ الثاني تَحديثٌ لا يُعَدّ؛ والتَقييمُ بِالخِدمَةِ الحَقيقِيَّةِ سَطرٌ بِمُعَرِّفِه، والرَدُّ عَلَيه (<c>Store</c>) لا يُعَدّ؛ وكُلٌّ في مُعامَلَةِ وَثيقَتِه؛ ولا سَطرَ
    /// في المَتجَرِ يَحمِلُ مُعَرِّفَ العُضو (G17).</summary>
    [Fact]
    public async Task A_member_join_and_a_review_are_one_entry_each_in_their_documents_transaction_and_the_member_id_never_enters_the_ledger()
    {
        if (!Enabled) return;
        var sms = new LiveContactNoticeProofTests.CaptureSms();
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddSingleton<IOtpChannel>(sms));
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        var tenant = new Tenant { Id = slug, Name = "مَتجَرُ الانضِمام", OwnerUserId = owner };
        await using (var s = host.Store.LightweightSession()) { s.Insert(tenant); await s.SaveChangesAsync(); }
        void AsStore(HttpContext ctx) => ctx.SetTenant(tenant.Id, tenant.Name, tenant.BrandColor, tenant.AuthChannel, tenant.TagLine, tenant.City, hasRoles: false);
        // ‏ما يَضَعُه الوَسيطُ لِمَتجَرٍ بِلا أَدوار (صَفّاه في G16 وG5): المَتجَرُ ومالِكُه و`_none`.
        void Resolve() => MeterScope.Open().Fill(slug, entrepreneur: MeterScope.OwnerOf(owner), role: MeterSentinels.None);
        var route = new Dictionary<string, string> { ["slug"] = slug };
        var phone = NewPhone();
        async Task<string> JoinAsync()
        {
            Resolve();
            var sent = await host.PostFormOnHostAsync("/{slug}/auth/phone/login", route, null, AsStore, ("phone", phone));
            Assert.Equal(302, sent.Status);
            Resolve();
            var verified = await host.PostFormOnHostAsync("/{slug}/auth/phone/verify", route, null, AsStore, ("phone", phone), ("code", sms.Code!));
            Assert.Equal(302, verified.Status);
            Assert.DoesNotContain("err=", verified.Location, StringComparison.Ordinal);
            return verified.Location;
        }
        var joined = await JoinAsync();
        User member;
        await using (var q = host.Store.QuerySession(slug))
            member = Assert.Single(await q.Query<User>().Where(u => u.Phone == PhoneNumber.Canonical(phone)).ToListAsync());
        var pseudonym = UsageEntryId.Pseudonym(MeterRules.MemberJoin, slug, member.Id.ToString("D"));
        var join = await DocEntriesAsync(MeterRules.MemberJoin, pseudonym, "mt_doc_user", member.Id);
        // ‏G17 عَلى سُطورِ المَتجَرِ وهِيَ قائِمَة: لا مُعَرِّفَ عُضوٍ فيها بِصيغَتَيه.
        async Task<long> LeaksAsync() => Convert.ToInt64(await ScalarAsync(
            "select count(*) from meter.usage_entry where app = @a and (position(@d in id) > 0 or position(@d in source_id) > 0 or position(@n in id) > 0 or position(@n in source_id) > 0)",
            ("a", slug), ("d", member.Id.ToString("D")), ("n", member.Id.ToString("N"))));
        var unitsAtBirth = await ScalarAsync("select string_agg(unit || ' ' || n, ' · ' order by unit) from (select unit, count(*) n from meter.usage_entry where app = @a group by unit) t", ("a", slug));
        var leaks = await LeaksAsync();
        await ForgetBirthAsync(MeterRules.MemberJoin, pseudonym);
        await JoinAsync();
        var afterSecond = await UnitEntriesAsync(MeterRules.MemberJoin, pseudonym);

        Resolve();
        await using var scope = host.Services.CreateAsyncScope();
        var reviews = scope.ServiceProvider.GetRequiredService<ACommerce.Kit.Reviews.ReviewsService>();
        var review = await reviews.SubmitAsync(slug, Guid.NewGuid(), "بائِعُ العَدّ", member.Id, "عُضوُ العَدّ", 5, "تَقييمُ العَدّ");
        var reviewId = review.Id.ToString("D");
        var rated = await DocEntriesAsync(MeterRules.ReviewCreate, reviewId, "mt_doc_review", review.Id);
        leaks += await LeaksAsync();
        await ForgetBirthAsync(MeterRules.ReviewCreate, reviewId);
        Resolve();
        await reviews.RespondAsync(slug, review.Id, "شُكراً");
        var afterRespond = await UnitEntriesAsync(MeterRules.ReviewCreate, reviewId);

        var perUnit = await ScalarAsync("select string_agg(unit || ' ' || n, ' · ' order by unit) from (select unit, count(*) n from meter.usage_entry where app = @a group by unit) t", ("a", slug));
        output.WriteLine($"[P1·وَثيقَة] الانضِمام ⇒ {joined} · «{string.Join(" | ", join)}» · الثاني (بِلا سَطر) {afterSecond} · التَقييم «{string.Join(" | ", rated)}» · بَعدَ الرَدِّ (بِلا سَطر) {afterRespond}");
        output.WriteLine($"[P1·وَثيقَة] لِكُلِّ وَحدَةٍ في /{slug} عِندَ الانضِمام: {unitsAtBirth} · وفي آخِرِه: {perUnit} · سُطورٌ تَحمِلُ مُعَرِّفَ العُضو {leaks}");

        var ownerId = owner.ToString("D");
        Assert.Equal(new DocRow(MeterRules.MemberJoin, slug, ownerId, MeterSentinels.None, MeterRules.UserSource, pseudonym, true), Assert.Single(join));
        Assert.Equal(0L, afterSecond);
        Assert.Equal(new DocRow(MeterRules.ReviewCreate, slug, ownerId, MeterSentinels.None, MeterRules.ReviewSource, reviewId, true), Assert.Single(rated));
        Assert.Equal(0L, afterRespond);
        Assert.Equal("member.join 1 · store.create 1", unitsAtBirth);
        Assert.Equal("store.create 1", perUnit);
        Assert.Equal(0L, leaks);
    }

    // ═══ ‏(‏2026-11-12، W13-6·٩) `chat.message` بِبابِ الإرسالِ الحَقيقِيّ — بَعدَ أَن صارَ `Store` الرِسالَةِ `Insert` خَلفَ تَوصيفِه (09859e2) ═══
    // (التَصميم §3.4 و§5.2 و§13 P1). الحامِلُ يَفتَحُه الصَفُّ قَبلَ كُلِّ بابٍ كَما يَفتَحُه الوَسيطُ لِمَتجَرٍ بِلا أَدوار (صَفّا G16 وG5).

    /// <summary><b><c>chat.message</c></b>: كُلُّ إرسالٍ بِالبابِ سَطرٌ واحِدٌ في مَتجَرِه بِمُعَرِّفِ الرِسالَة — رائِدُ أَعمالِه مالِكُ المَتجَرِ مِن الحامِل — و<c>writer_xid</c>
    /// = <c>xmin</c> صَفِّ الرِسالَة؛ والإرسالُ الثاني بِالجِسمِ نَفسِه سَطرٌ ثانٍ (النَقرَتانِ رِسالَتان)؛ والجِسمُ الفارِغُ لا رِسالَةَ ولا سَطر؛ و<c>Store</c> رِسالَةٍ قائِمَةٍ
    /// (بِلا سَطرِ وِلادَة) صِفر؛ ولا سَطرَ يَحمِلُ مُعَرِّفَ المُرسِلِ ولا المُحادَثَة.</summary>
    [Fact]
    public async Task Each_sent_message_is_one_chat_message_entry_in_the_messages_transaction_and_a_store_of_an_existing_message_is_none()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ المُحادَثَة", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        User Member(string name) => new()
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = NewPhone(), PhoneVerified = true,
            AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
        };
        var buyer = Member("مُشتَري العَدّ");
        var seller = Member("بائِعُ العَدّ");
        var conv = new ACommerce.Kit.Chat.Conversation
        {
            Id = Guid.NewGuid(), OwnerId = buyer.Id, OwnerName = buyer.FullName, PartnerId = seller.Id, PartnerName = seller.FullName, LastAt = DateTime.UtcNow,
        };
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(buyer, seller); s.Store(conv); await s.SaveChangesAsync(); }
        void Resolve() => MeterScope.Open().Fill(slug, entrepreneur: MeterScope.OwnerOf(owner), role: MeterSentinels.None);
        var route = new Dictionary<string, string> { ["slug"] = slug, ["conversationId"] = conv.Id.ToString() };
        var token = AuthHandlers.MakeToken(buyer.Id, slug, buyer.SessionVersion);
        async Task<List<ACommerce.Kit.Chat.Message>> MessagesAsync()
        {
            await using var q = host.Store.QuerySession(slug);
            return [.. (await q.Query<ACommerce.Kit.Chat.Message>().ToListAsync()).OrderBy(m => m.SentAt)];
        }
        async Task<string> PerUnitAsync() => Convert.ToString(await ScalarAsync(
            "select coalesce(string_agg(unit || ' ' || n, ' · ' order by unit), '') from (select unit, count(*) n from meter.usage_entry where app = @a group by unit) t", ("a", slug))) ?? "";

        Resolve();
        var first = await host.PostFormAsync("/{slug}/chats/{conversationId:guid}/send", route, slug, token, ("body", "هَل ما زالَ مُتاحاً؟"));
        var one = Assert.Single(await MessagesAsync());
        var firstRows = await DocEntriesAsync(MeterRules.ChatMessage, one.Id.ToString("D"), "mt_doc_message", one.Id);
        Resolve();
        var second = await host.PostFormAsync("/{slug}/chats/{conversationId:guid}/send", route, slug, token, ("body", "هَل ما زالَ مُتاحاً؟"));
        Resolve();
        var blank = await host.PostFormAsync("/{slug}/chats/{conversationId:guid}/send", route, slug, token, ("body", "   "));
        var messages = await MessagesAsync();
        var two = messages[^1];
        var secondRows = await DocEntriesAsync(MeterRules.ChatMessage, two.Id.ToString("D"), "mt_doc_message", two.Id);
        var afterSends = await PerUnitAsync();
        var leaks = Convert.ToInt64(await ScalarAsync(
            "select count(*) from meter.usage_entry where app = @a and (position(@b in id) > 0 or position(@b in source_id) > 0 or position(@c in id) > 0 or position(@c in source_id) > 0)",
            ("a", slug), ("b", buyer.Id.ToString("D")), ("c", conv.Id.ToString("D"))));

        // ‏Store رِسالَةٍ قائِمَةٍ بِلا سَطرِ وِلادَة: تَحديثٌ لا وِلادَة (دَرسُ M6A-CNT-1).
        await ForgetBirthAsync(MeterRules.ChatMessage, one.Id.ToString("D"));
        Resolve();
        await using (var s = host.Store.LightweightSession(slug))
        {
            var stored = (await s.LoadAsync<ACommerce.Kit.Chat.Message>(one.Id))!;
            stored.Body = "—";
            s.Store(stored);
            await s.SaveChangesAsync();
        }
        var afterUpdate = await UnitEntriesAsync(MeterRules.ChatMessage, one.Id.ToString("D"));

        output.WriteLine($"[P1·رِسالَة] الإرسال ⇒ {first} · «{string.Join(" | ", firstRows)}» · الثاني ⇒ {second} · «{string.Join(" | ", secondRows)}» · الفارِغ ⇒ {blank}");
        output.WriteLine($"[P1·رِسالَة] رَسائِل {messages.Count} · لِكُلِّ وَحدَةٍ في /{slug}: {afterSends} · سُطورٌ تَحمِلُ مُعَرِّفَ المُرسِلِ أَو المُحادَثَة {leaks} · بَعدَ Store رِسالَةٍ بِلا سَطر {afterUpdate}");

        var back = (302, $"/{slug}/chats/{conv.Id}");
        Assert.Equal([back, back, back], new[] { first, second, blank });
        Assert.Equal(2, messages.Count);
        Assert.NotEqual(one.Id, two.Id);
        var ownerId = owner.ToString("D");
        Assert.Equal(new DocRow(MeterRules.ChatMessage, slug, ownerId, MeterSentinels.None, MeterRules.MessageSource, one.Id.ToString("D"), true), Assert.Single(firstRows));
        Assert.Equal(new DocRow(MeterRules.ChatMessage, slug, ownerId, MeterSentinels.None, MeterRules.MessageSource, two.Id.ToString("D"), true), Assert.Single(secondRows));
        Assert.Equal("chat.message 2 · member.join 2 · store.create 1", afterSends);   // العُضوانِ وُلِدا في هذا الصَفِّ بِلا نُسخَة
        Assert.Equal(0L, leaks);
        Assert.Equal(0L, afterUpdate);
    }

    // ═══ ‏(‏2026-11-12، W13-6·١٠) الدَورُ الفَعّالُ في الحامِل — حارِسا الصَلاحِيَّةِ عَبرَ الوَسيطِ الحَقيقِيّ ═══════════════════════════════════════════════
    // (التَصميم §5.4 و§13؛ ADR-051 §١٧). المُضيفُ بِـ`throughResolver`: كُلُّ بابٍ يَمُرُّ أَوَّلاً بِـ`TenantResolverMiddleware` الحَقيقِيّ، فَالحامِلُ يَفتَحُه
    // ويَملَؤُه هُوَ (المَتجَرُ ومالِكُه، والدَورُ لِمَن يَحسِبُه في مَتجَرٍ بِأَدوار) — لا الصَفّ.

    /// <summary>
    /// <para><b><c>offer.submit</c> بِدَورِ مُقَدِّمِه، و<c>listing.create</c> بِالدَورِ الفَعّالِ لا المُخَزَّن</b>: بابُ العَرضِ الحَقيقِيُّ (<c>HasPermissionAsync</c>) لِعَميلٍ
    /// ثُمَّ لِمُديرِ المَتجَر ⇐ سَطرُ كُلِّ عَرضٍ دَورُه <c>customer</c> ثُمَّ <c>tenant_admin</c> ورائِدُ أَعمالِه المالِكُ و<c>writer_xid</c> = <c>xmin</c> حَدَثِه؛
    /// وتاجِرٌ بِلا الصَلاحِيَّةِ <c>?err=forbidden</c> ولا عَرضَ ولا سَطر.</para>
    /// <para>و<c>PermissionFilter</c> الحَقيقِيُّ داخِلَ الوَسيطِ الحَقيقِيِّ وبَعدَه كِتابَةُ النُقطَة (بَدءُ مَجرى الإعلان): التاجِرُ ⇐ <c>vendor</c>، والعَميلُ عَلى
    /// <c>/{slug}/r/vendor/…</c> ⇐ <c>vendor</c> (الفَعّالُ لا المُخَزَّن)، والعَميلُ بِلا رابِطٍ <c>403</c> والحامِلُ بِلا دَورٍ ولا إعلان؛ ومَتجَرٌ بِلا أَدوارٍ <c>_none</c>
    /// مِن الوَسيطِ كَما كان (المُرَشِّحُ يَمُرُّ قَبلَ أَن يَحسِب).</para>
    /// </summary>
    [Fact]
    public async Task The_permission_guards_write_the_effective_role_they_accept_into_the_holder_the_real_resolver_opened()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build(throughResolver: true);
        var (roled, zero) = (NewSlug(), NewSlug());
        var owner = Guid.NewGuid();
        ACommerce.Kit.Roles.Role Instantiate(string role, int order)
            => ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find(role)!, sortOrder: order);
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = roled, Name = "مَتجَرُ الدَورِ الفَعّال", OwnerUserId = owner,
                                  Roles = [Instantiate("vendor", 0), Instantiate("customer", 1), Instantiate("tenant_admin", 2)] });
            s.Insert(new Tenant { Id = zero, Name = "مَتجَرٌ بِلا أَدوار", OwnerUserId = owner });
            await s.SaveChangesAsync();
        }
        User Member(string role) => new() { Id = Guid.NewGuid(), TenantSlug = roled, FullName = "عُضوُ الدَور", Phone = NewPhone(), ActiveRole = role, Role = role };
        var (seller, buyer, manager) = (Member("vendor"), Member("customer"), Member("tenant_admin"));
        var listingId = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(roled))
        {
            s.Store(seller, buyer, manager);
            s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listingId,
                new ACommerce.Kit.Listings.ListingCreated(listingId, roled, "إعلانُ الدَور", null, 120m, "general", null, null,
                                                         new() { ["owner_id"] = seller.Id.ToString() }, DateTime.UtcNow));
            await s.SaveChangesAsync();
        }
        var ownerId = owner.ToString("D");

        // ١ · بابُ العَرضِ الحَقيقِيّ.
        async Task<((int, string) Door, List<EventEntry> Rows)> OfferAsync(User u)
        {
            var door = await host.PostFormAsync("/{slug}/listings/{id:guid}/offers",
                new Dictionary<string, string> { ["slug"] = roled, ["id"] = listingId.ToString() }, roled, AuthHandlers.MakeToken(u.Id, roled, u.SessionVersion),
                ("price", "110"), ("message", "عَرضُ الدَور"), ("lat", "24.7136"), ("lng", "46.6753"), ("ttl_minutes", "15"));
            var offers = (await ScalarAsync("select coalesce(string_agg(id::text, ','), '') from platform.mt_doc_offer where tenant_id = @t and data->>'OffererId' = @u",
                ("t", roled), ("u", u.Id.ToString())))?.ToString() ?? "";
            var rows = new List<EventEntry>();
            foreach (var id in offers.Split(',', StringSplitOptions.RemoveEmptyEntries)) rows.AddRange(await StreamEntriesAsync(Guid.Parse(id)));
            output.WriteLine($"[P1·دَور] العَرضُ مِن {u.ActiveRole} ⇒ {door.Status} {door.Location} · "
                             + (rows.Count == 0 ? "لا سَطر" : string.Join(" | ", rows.Select(r => $"{r.Unit} role={r.Role} entrepreneur={r.Entrepreneur} writer_xid {r.WriterXid} = xmin {r.EventXmin}"))));
            return (door, rows);
        }
        var byBuyer = await OfferAsync(buyer);
        var byManager = await OfferAsync(manager);
        var bySeller = await OfferAsync(seller);

        // ٢ · مُرَشِّحُ النَشرِ الحَقيقِيُّ داخِلَ الوَسيطِ الحَقيقِيّ، وبَعدَه كِتابَةُ النُقطَة.
        async Task<(int Status, MeterScopeView? Holder, List<EventEntry> Rows)> PublishAsync(string slug, User u, string path)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            ctx.Request.Method = "POST";
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("localhost");
            ctx.Request.Path = path;
            ctx.Request.ContentType = "application/x-www-form-urlencoded";
            ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
            var status = 0;
            MeterScopeView? holder = null;
            Guid? born = null;
            var resolver = ActivatorUtilities.CreateInstance<TenantResolverMiddleware>(scope.ServiceProvider, (RequestDelegate)(async c =>
            {
                c.Items[ACommerce.Templates.Customer.Marketplace.Gates.GateKeys.SlugItem] = slug;   // ‏ما يَضَعُه `AuthFilter` قَبلَ المُرَشِّح
                c.Items[ACommerce.Templates.Customer.Marketplace.Gates.GateKeys.UserId] = u.Id;
                var result = await new ACommerce.Templates.Customer.Marketplace.Gates.PermissionFilter("listing.create").InvokeAsync(
                    new DefaultEndpointFilterInvocationContext(c), async _ =>
                    {
                        var id = Guid.NewGuid();
                        await using var s = host.Store.LightweightSession(slug);
                        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(id, new ACommerce.Kit.Listings.ListingCreated(id, slug, "نَشرُ الدَور", null, 1m, "general",
                            null, null, new() { ["owner_id"] = u.Id.ToString() }, DateTime.UtcNow));
                        await s.SaveChangesAsync();
                        born = id;
                        return Results.Ok();
                    });
                holder = MeterScope.View();
                status = (result as IStatusCodeHttpResult)?.StatusCode ?? 0;
            }));
            await resolver.InvokeAsync(ctx);
            List<EventEntry> rows = born is { } b ? await StreamEntriesAsync(b) : [];
            output.WriteLine($"[P1·دَور] النَشرُ {path} مِن {(u.ActiveRole.Length == 0 ? "—" : u.ActiveRole)} ⇒ {status} · الحامِل {holder} · "
                             + (rows.Count == 0 ? "لا سَطر" : string.Join(" | ", rows.Select(r => $"{r.Unit} role={r.Role} entrepreneur={r.Entrepreneur} writer_xid {r.WriterXid} = xmin {r.EventXmin}"))));
            return (status, holder, rows);
        }
        var vendorPublish = await PublishAsync(roled, seller, $"/{roled}/listings/create");
        var linkPublish = await PublishAsync(roled, buyer, $"/{roled}/r/vendor/listings/create");
        var refused = await PublishAsync(roled, buyer, $"/{roled}/listings/create");
        var zeroPublish = await PublishAsync(zero, new User { Id = Guid.NewGuid(), ActiveRole = "" }, $"/{zero}/listings/create");

        var listingBack = $"/{roled}/listings/{listingId}";
        Assert.Equal((302, $"{listingBack}?offer=submitted"), byBuyer.Door);
        Assert.Equal((302, $"{listingBack}?offer=submitted"), byManager.Door);
        Assert.Equal((302, $"{listingBack}?err=forbidden"), bySeller.Door);
        var b1 = Assert.Single(byBuyer.Rows);
        var m1 = Assert.Single(byManager.Rows);
        Assert.Equal((MeterRules.OfferSubmit, roled, ownerId, "customer"), (b1.Unit, b1.App, b1.Entrepreneur, b1.Role));
        Assert.Equal((MeterRules.OfferSubmit, roled, ownerId, "tenant_admin"), (m1.Unit, m1.App, m1.Entrepreneur, m1.Role));
        Assert.All(new[] { b1, m1 }, r => Assert.Equal(r.EventXmin, r.WriterXid));
        Assert.Empty(bySeller.Rows);

        Assert.Equal((200, new MeterScopeView(roled, ownerId, "vendor", false)), (vendorPublish.Status, vendorPublish.Holder));
        Assert.Equal((MeterRules.ListingCreate, ownerId, "vendor"), (Assert.Single(vendorPublish.Rows).Unit, vendorPublish.Rows[0].Entrepreneur, vendorPublish.Rows[0].Role));
        Assert.Equal((200, new MeterScopeView(roled, ownerId, "vendor", false)), (linkPublish.Status, linkPublish.Holder));
        Assert.Equal("vendor", Assert.Single(linkPublish.Rows).Role);
        Assert.Equal((403, new MeterScopeView(roled, ownerId, null, false)), (refused.Status, refused.Holder));
        Assert.Empty(refused.Rows);
        Assert.Equal((200, new MeterScopeView(zero, ownerId, MeterSentinels.None, false)), (zeroPublish.Status, zeroPublish.Holder));
        Assert.Equal(MeterSentinels.None, Assert.Single(zeroPublish.Rows).Role);
    }

    // ═══ ‏(‏2026-11-12، W13-6·B-R1·1 — مُراجَعَةُ المَرحَلَةِ ب · M6B-CNT-1) `_none` ما قَرَّرَه الحارِسُ لا ما خَبَّأَه الوَسيط ═══════════════════════════════
    // (ADR-051 §١٧ وسَطرُه المُؤَرَّخ). الوَسيطُ يَحسِبُ `_none` مِن `Tenant.Roles` المُخَزَّنَةِ في ذاكِرَةِ خَمسِ دَقائِق، والحارِسانِ يُقَرِّرانِ عَلى المُجَسَّدَةِ الطازَجَة
    // (وفَوقَها المُؤَلَّفَةُ المُعتَمَدَة)؛ فَدَورُ الوَسيطِ مُؤَقَّتٌ يَحِلُّ مَحَلَّه دَورُ الحارِسِ المَقبول — وهذانِ الصَفّانِ يَحمَرّانِ عِندَ `c11e7b0` (مَقيس).

    /// <summary>النَشرُ عَبرَ الوَسيطِ الحَقيقِيِّ ثُمَّ <c>PermissionFilter("listing.create")</c> الحَقيقِيِّ ثُمَّ بَدءُ مَجرى الإعلان — بِحاوِيَةِ المُضيف، فَذاكِرَةُ الوَسيطِ
    /// واحِدَةٌ بَينَ الطَلَبات (شَكلُ صَفِّ البَند ١٠ بِعَينِه).</summary>
    private async Task<(int Status, MeterScopeView? Holder, List<EventEntry> Rows, bool ResolverSawRoles)> PublishThroughResolverAsync(
        LiveTemplateDoorHost host, string slug, User u, string tag)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = "POST";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = $"/{slug}/listings/create";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
        var status = 0;
        MeterScopeView? holder = null;
        Guid? born = null;
        var sawRoles = false;
        var resolver = ActivatorUtilities.CreateInstance<TenantResolverMiddleware>(scope.ServiceProvider, (RequestDelegate)(async c =>
        {
            // ‏ما رَآه الوَسيطُ في وَثيقَتِه (ذاكِرَتُه): `hasRoles` الَّذي يَضَعُه `SetTenant` مِن `Tenant.Roles` المُخَزَّنَة.
            sawRoles = c.Items[TenantKeys.HasRoles] is true;
            c.Items[ACommerce.Templates.Customer.Marketplace.Gates.GateKeys.SlugItem] = slug;   // ‏ما يَضَعُه `AuthFilter` قَبلَ المُرَشِّح
            c.Items[ACommerce.Templates.Customer.Marketplace.Gates.GateKeys.UserId] = u.Id;
            var result = await new ACommerce.Templates.Customer.Marketplace.Gates.PermissionFilter("listing.create").InvokeAsync(
                new DefaultEndpointFilterInvocationContext(c), async _ =>
                {
                    var id = Guid.NewGuid();
                    await using var s = host.Store.LightweightSession(slug);
                    s.Events.StartStream<ACommerce.Kit.Listings.Listing>(id, new ACommerce.Kit.Listings.ListingCreated(id, slug, "نَشرُ " + tag, null, 1m, "general",
                        null, null, new() { ["owner_id"] = u.Id.ToString() }, DateTime.UtcNow));
                    await s.SaveChangesAsync();
                    born = id;
                    return Results.Ok();
                });
            holder = MeterScope.View();
            status = (result as IStatusCodeHttpResult)?.StatusCode ?? 0;
        }));
        await resolver.InvokeAsync(ctx);
        List<EventEntry> rows = born is { } b ? await StreamEntriesAsync(b) : [];
        output.WriteLine($"[P1·{tag}] النَشرُ مِن {(u.ActiveRole.Length == 0 ? "—" : u.ActiveRole)} ⇒ {status} · الوَسيطُ رَأى أَدواراً مُخَزَّنَة {sawRoles} · الحامِل {holder} · "
                         + (rows.Count == 0 ? "لا سَطر" : string.Join(" | ", rows.Select(r => $"{r.Unit} role={r.Role} entrepreneur={r.Entrepreneur} writer_xid {r.WriterXid} = xmin {r.EventXmin}"))));
        return (status, holder, rows, sawRoles);
    }

    /// <summary>
    /// <para><b>مَتجَرٌ بِلا أَدوارٍ مُخَزَّنَةٍ ودَورُه المُؤَلَّفُ المُعتَمَدُ «خَيّاط»</b> (‏<c>TenantRoleDefinition</c> كَما يَكتُبُها الوَكيل — <c>DefineRoleAsync</c>
    /// يَقتَرِحُه لِأَيِّ مَتجَر): الحارِسُ يَقبَلُ النَشرَ والعَرضَ بِـ«خَيّاط»، فَسَطرا <c>listing.create</c> و<c>offer.submit</c> دَورُهُما <c>khayyat</c> — لا <c>_none</c>
    /// الوَسيطِ الَّذي يَرى <c>Tenant.Roles</c> فارِغَة. والنَشرُ بِـ<c>PermissionFilter</c> الحَقيقِيِّ داخِلَ الوَسيطِ الحَقيقِيّ، والعَرضُ بِبابِه المُسَجَّلِ
    /// (<c>HasPermissionAsync</c>) عَبرَ الوَسيطِ نَفسِه (<c>throughResolver</c>).</para>
    /// </summary>
    [Fact]
    public async Task In_a_store_whose_only_roles_are_authored_the_publish_and_the_offer_carry_the_authored_role_the_guards_accepted_not_the_resolvers_none()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build(throughResolver: true);
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        const string authored = "khayyat";
        // ‏«خَيّاط» بِصَلاحِيّاتِه، والرابِعَةُ `offer.submit` بَدَلَ `chat.respond` — فَالدَورُ الواحِدُ يَبلُغُ الحارِسَين.
        var json = TenantRoleDefinitionToolTests.KhayyatJson.Replace("\"chat.respond\"", "\"offer.submit\"", StringComparison.Ordinal);
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = slug, Name = "مَتجَرٌ أَدوارُه مُؤَلَّفَة", OwnerUserId = owner });
            await s.SaveChangesAsync();
        }
        User Member(string role) => new() { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوُ الدَورِ المُؤَلَّف", Phone = NewPhone(), ActiveRole = role, Role = role };
        var (maker, seller) = (Member(authored), Member(authored));
        var listingId = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Store(new ACommerce.Kit.Roles.TenantRoleDefinition
            {
                Id = authored, Slug = authored, DefinitionJson = json, Status = ACommerce.Kit.Roles.TenantRoleStatuses.Approved,
                CreatedBy = "live-proof", CreatedAt = DateTime.UtcNow, DecidedBy = "live-proof", DecidedAt = DateTime.UtcNow,
            });
            s.Store(maker, seller);
            s.Events.StartStream<ACommerce.Kit.Listings.Listing>(listingId,
                new ACommerce.Kit.Listings.ListingCreated(listingId, slug, "إعلانُ الدَورِ المُؤَلَّف", null, 120m, "general", null, null,
                                                         new() { ["owner_id"] = seller.Id.ToString() }, DateTime.UtcNow));
            await s.SaveChangesAsync();
        }
        Tenant? stored;
        await using (var q = host.Store.QuerySession()) stored = await q.LoadAsync<Tenant>(slug);
        var materialized = (await ACommerce.Templates.Customer.Marketplace.Services.TenantRoleService.ReadUncachedAsync(host.Store, slug)).Materialize(stored!.Roles);
        output.WriteLine($"[P1·مُؤَلَّف] أَدوارٌ مُخَزَّنَة {stored.Roles.Count} · مُجَسَّدَة {materialized.Count}: "
                         + string.Join(",", materialized.Select(r => $"{r.Slug}[{string.Join(" ", r.Permissions)}]")));
        Assert.Empty(stored.Roles);
        Assert.Equal(authored, Assert.Single(materialized).Slug);
        var ownerId = owner.ToString("D");

        var publish = await PublishThroughResolverAsync(host, slug, maker, "مُؤَلَّف");

        var door = await host.PostFormAsync("/{slug}/listings/{id:guid}/offers",
            new Dictionary<string, string> { ["slug"] = slug, ["id"] = listingId.ToString() }, slug, AuthHandlers.MakeToken(maker.Id, slug, maker.SessionVersion),
            ("price", "110"), ("message", "عَرضُ الدَورِ المُؤَلَّف"), ("lat", "24.7136"), ("lng", "46.6753"), ("ttl_minutes", "15"));
        var offers = (await ScalarAsync("select coalesce(string_agg(id::text, ','), '') from platform.mt_doc_offer where tenant_id = @t and data->>'OffererId' = @u",
            ("t", slug), ("u", maker.Id.ToString())))?.ToString() ?? "";
        var offerRows = new List<EventEntry>();
        foreach (var id in offers.Split(',', StringSplitOptions.RemoveEmptyEntries)) offerRows.AddRange(await StreamEntriesAsync(Guid.Parse(id)));
        output.WriteLine($"[P1·مُؤَلَّف] العَرضُ مِن {authored} ⇒ {door.Status} {door.Location} · "
                         + (offerRows.Count == 0 ? "لا سَطر" : string.Join(" | ", offerRows.Select(r => $"{r.Unit} role={r.Role} entrepreneur={r.Entrepreneur} writer_xid {r.WriterXid} = xmin {r.EventXmin}"))));

        Assert.False(publish.ResolverSawRoles);
        Assert.Equal((200, new MeterScopeView(slug, ownerId, authored, false)), (publish.Status, publish.Holder));
        var listed = Assert.Single(publish.Rows);
        Assert.Equal((MeterRules.ListingCreate, slug, ownerId, authored), (listed.Unit, listed.App, listed.Entrepreneur, listed.Role));
        Assert.Equal((302, $"/{slug}/listings/{listingId}?offer=submitted"), door);
        var offered = Assert.Single(offerRows);
        Assert.Equal((MeterRules.OfferSubmit, slug, ownerId, authored), (offered.Unit, offered.App, offered.Entrepreneur, offered.Role));
        Assert.All(new[] { listed, offered }, r => Assert.Equal(r.EventXmin, r.WriterXid));
    }

    /// <summary>
    /// <para><b>تَحتَ ذاكِرَةِ الوَسيطِ يُقَرِّرُ الحارِسُ الدَورَ بِالاتِّجاهَين</b>: ذاكِرَةُ الخَمسِ دَقائِقِ لا يُخليها حِفظُ <c>Tenant.Roles</c> (تَعليقُ الوَسيطِ نَفسُه)،
    /// والحارِسُ يَقرَأُ طازَجاً — (١) مَتجَرٌ خُبِّئَ بِلا أَدوارٍ ثُمَّ حُفِظَ أَوَّلُ دَورٍ فيه: التاجِرُ يَنشُرُ بِـ<c>vendor</c> فَالسَطرُ <c>vendor</c> لا <c>_none</c>
    /// الذاكِرَة؛ (٢) مَتجَرٌ خُبِّئَ بِأَدوارٍ ثُمَّ أُزيلَ آخِرُها: العُضوُ يَنشُرُ (صَلاحِيّاتُ العُضوِ تَمُرُّ بِلا أَدوار) فَالسَطرُ <c>_none</c> الَّذي قَرَّرَه الحارِس
    /// لا <c>_unresolved</c>، والعَرضُ بِبابِه المُسَجَّلِ (<c>HasPermissionAsync</c>) عَبرَ الوَسيطِ نَفسِه كَذلك. والطَلَبُ الأَوَّلُ في كُلٍّ يُثبِتُ أَنّ الذاكِرَةَ امتَلَأَت
    /// بِما قَبلَ التَغيير، و«الوَسيطُ رَأى أَدواراً مُخَزَّنَة» في الثاني يُثبِتُ أَنّها قَديمَة.</para>
    /// </summary>
    [Fact]
    public async Task Under_the_resolvers_cached_store_the_permission_guard_decides_the_role_when_the_first_role_arrives_and_when_the_last_one_leaves()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build(throughResolver: true);
        var (first, last) = (NewSlug(), NewSlug());
        var owner = Guid.NewGuid();
        ACommerce.Kit.Roles.Role Instantiate(string role, int order)
            => ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(ACommerce.Kit.Roles.RoleCatalog.Find(role)!, sortOrder: order);
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = first, Name = "مَتجَرٌ يَأتيه أَوَّلُ دَور", OwnerUserId = owner });
            s.Insert(new Tenant { Id = last, Name = "مَتجَرٌ يَترُكُه آخِرُ دَور", OwnerUserId = owner,
                                  Roles = [Instantiate("vendor", 0), Instantiate("customer", 1)] });
            await s.SaveChangesAsync();
        }
        User Member(string tenant, string role) => new() { Id = Guid.NewGuid(), TenantSlug = tenant, FullName = "عُضوُ الذاكِرَة", Phone = NewPhone(), ActiveRole = role, Role = role };
        var (firstVendor, lastVendor, lastCustomer) = (Member(first, "vendor"), Member(last, "vendor"), Member(last, "customer"));
        foreach (var (tenant, u) in new[] { (first, firstVendor), (last, lastVendor), (last, lastCustomer) })
        {
            await using var s = host.Store.LightweightSession(tenant);
            s.Store(u);
            await s.SaveChangesAsync();
        }
        var lastListing = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession(last))
        {
            s.Events.StartStream<ACommerce.Kit.Listings.Listing>(lastListing,
                new ACommerce.Kit.Listings.ListingCreated(lastListing, last, "إعلانُ الذاكِرَة", null, 120m, "general", null, null,
                                                         new() { ["owner_id"] = lastVendor.Id.ToString() }, DateTime.UtcNow));
            await s.SaveChangesAsync();
        }
        async Task SetRolesAsync(string slug, List<ACommerce.Kit.Roles.Role> roles)
        {
            await using var s = host.Store.LightweightSession();
            var t = await s.LoadAsync<Tenant>(slug);
            t!.Roles = roles;
            s.Store(t);
            await s.SaveChangesAsync();
        }
        var ownerId = owner.ToString("D");

        // (١) يُخَبَّأُ بِلا أَدوار ثُمَّ يَأتيه أَوَّلُ دَور.
        var before1 = await PublishThroughResolverAsync(host, first, firstVendor, "الذاكِرَة·قَبل");
        await SetRolesAsync(first, [Instantiate("vendor", 0), Instantiate("customer", 1)]);
        var after1 = await PublishThroughResolverAsync(host, first, firstVendor, "الذاكِرَة·أَوَّلُ دَور");

        // (٢) يُخَبَّأُ بِأَدوار ثُمَّ يَترُكُه آخِرُها.
        var before2 = await PublishThroughResolverAsync(host, last, lastVendor, "الذاكِرَة·قَبل");
        await SetRolesAsync(last, []);
        var after2 = await PublishThroughResolverAsync(host, last, lastCustomer, "الذاكِرَة·آخِرُ دَور");
        var offerDoor = await host.PostFormAsync("/{slug}/listings/{id:guid}/offers",
            new Dictionary<string, string> { ["slug"] = last, ["id"] = lastListing.ToString() }, last, AuthHandlers.MakeToken(lastCustomer.Id, last, lastCustomer.SessionVersion),
            ("price", "110"), ("message", "عَرضُ الذاكِرَة"), ("lat", "24.7136"), ("lng", "46.6753"), ("ttl_minutes", "15"));
        var offerIds = (await ScalarAsync("select coalesce(string_agg(id::text, ','), '') from platform.mt_doc_offer where tenant_id = @t and data->>'OffererId' = @u",
            ("t", last), ("u", lastCustomer.Id.ToString())))?.ToString() ?? "";
        var offerRows = new List<EventEntry>();
        foreach (var id in offerIds.Split(',', StringSplitOptions.RemoveEmptyEntries)) offerRows.AddRange(await StreamEntriesAsync(Guid.Parse(id)));
        output.WriteLine($"[P1·الذاكِرَة·آخِرُ دَور] العَرضُ مِن customer ⇒ {offerDoor.Status} {offerDoor.Location} · "
                         + (offerRows.Count == 0 ? "لا سَطر" : string.Join(" | ", offerRows.Select(r => $"{r.Unit} role={r.Role} entrepreneur={r.Entrepreneur} writer_xid {r.WriterXid} = xmin {r.EventXmin}"))));

        // ‏الذاكِرَةُ قَديمَةٌ في الطَلَبِ الثاني مِن كُلٍّ (الوَسيطُ يَرى ما قَبلَ التَغيير) — وإلّا فَالصَفُّ لا يَقيسُ ما يَدَّعيه.
        Assert.Equal((false, false, true, true), (before1.ResolverSawRoles, after1.ResolverSawRoles, before2.ResolverSawRoles, after2.ResolverSawRoles));
        Assert.Equal((200, new MeterScopeView(first, ownerId, MeterSentinels.None, false)), (before1.Status, before1.Holder));
        Assert.Equal((200, new MeterScopeView(first, ownerId, "vendor", false)), (after1.Status, after1.Holder));
        Assert.Equal("vendor", Assert.Single(after1.Rows).Role);
        Assert.Equal((200, new MeterScopeView(last, ownerId, "vendor", false)), (before2.Status, before2.Holder));
        Assert.Equal((200, new MeterScopeView(last, ownerId, MeterSentinels.None, false)), (after2.Status, after2.Holder));
        Assert.Equal(MeterSentinels.None, Assert.Single(after2.Rows).Role);
        Assert.Equal((302, $"/{last}/listings/{lastListing}?offer=submitted"), offerDoor);
        var offered = Assert.Single(offerRows);
        Assert.Equal((MeterRules.OfferSubmit, ownerId, MeterSentinels.None), (offered.Unit, offered.Entrepreneur, offered.Role));
        Assert.Equal(offered.EventXmin, offered.WriterXid);
    }
}
