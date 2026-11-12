using System.Net;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
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

    // ─── صَفُّ الأُنبوبِ الحَقيقِيّ ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class Pipeline : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        private Pipeline(WebApplication app, HttpClient client) { _app = app; Client = client; }

        public static async Task<Pipeline> StartAsync(IDocumentStore store, DealsService deals)
        {
            for (var attempt = 1; ; attempt++)
            {
                var builder = WebApplication.CreateSlimBuilder();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton(new SubdomainTenancyOptions());
                builder.Services.AddSingleton(store);
                builder.Services.AddMemoryCache();
                using var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                l.Start(); var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
                var app = builder.Build();
                app.Urls.Add($"http://127.0.0.1:{port}");
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
