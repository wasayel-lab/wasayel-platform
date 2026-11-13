using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Theme;
using ACommerce.Platform.Flows;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G12 — لَقطَةُ الحُضورِ اليَومِيَّةُ حَيَّة: مُطالَبَةٌ واحِدَةٌ لِكُلِّ يَوم، ومُعامَلَةٌ واحِدَة، والمُتَأَخِّرَةُ تُرفَض، والوَحدَةُ مِن التَعريفات ══════════════
// (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣؛ التَصميم §8 و§12 G12 و§13 P12؛ COR-10). **لِماذا حَيّ** (القاعِدَة ١٤): الدَعوى في القاعِدَةِ نَفسِها —
// المُطالَبَةُ بِمِفتاحِها الفَريد، ومُعامَلَةٌ خامٌ تَحمِلُها والسُطورَ و`completed_at`، وطابورُ Wolverine المَتينُ يَحفَظُ الغَدَ مُجَدوَلاً. وكُلُّ صَفٍّ عَلى قاعِدَةٍ
// جَديدَةٍ يُنشِئُها ويَحذِفُها (`{القاعِدَة}_ps_{8}`): المُطالَبَةُ لِكُلِّ يَومٍ عَلى القاعِدَةِ كُلِّها، فَلا تَتَقاسَمُ الصُفوفُ يَوماً.
//
// **والتَركيبُ تَركيبُ المُضيفِ في ما يَخُصُّ الدَعوى**: `AddCustomerMarketplaceTemplate` نَفسُها (المُستَمِعُ وميزَةُ الدَفتَر) وMarten كَما يُرَكِّبُه
// `HostingExtensions`، و`PresenceSnapshot.Configure` بِعَينِه كَما يُناديه `Program.cs`. والصَفُّ الأَخيرُ (P12) يُقلِعُ التَطبيقَ المَبنِيَّ نَفسَه مَرَّتَين —
// `ApplicationStarted` في `Program.cs` لا نُسخَةٌ مِنه.
//
// **والأَيّامُ في الصُفوفِ المُباشِرَةِ أَيّامٌ مُسَمّاةٌ لا اليَوم** (`TakeAsync(day, …)` يَأخُذُ اليَومَ وَسيطاً): ما يَقرَأُه «اليَوم» — رَفضُ المُتَأَخِّرَةِ والإقلاع —
// يُقاسُ بِالساعَةِ الحَقيقِيَّةِ في صَفَّيه.

[Collection(LiveProofCollection.Name)]
public class LivePresenceSnapshotProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string StoreA = "ps-amal";
    private const string StoreS = "ps-mawquf";
    private const string StoreC = "ps-bila";
    private static readonly Guid OwnerA = Guid.Parse("8d2a5c1e-0f4b-4c6d-9a7e-3b1f2c4d5e6f");
    private static readonly Guid Vendor1 = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Vendor2 = Guid.Parse("66666666-7777-4888-9999-aaaaaaaaaaaa");

    private static (string Name, string ConnectionString) FreshDatabase(string tag)
    {
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        b.Database = $"{stem}_{tag}_{Guid.NewGuid().ToString("N")[..8]}";
        return (b.Database, b.ConnectionString);
    }

    private static async Task AdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>سِجِلٌّ يَلتَقِطُ كُلَّ سَطرٍ يُكتَب — لِيُقرَأَ <c>[meter] presence-late</c> بِحَرفِه.</summary>
    private sealed class Captured : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Lines);
        public void Dispose() { }

        private sealed class Sink(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) lines.Enqueue(formatter(state, exception));
            }
        }
    }

    /// <summary>المُضيفُ بِتَركيبِ القالِبِ وتَركيبِ اللَقطَةِ نَفسِه (<see cref="PresenceSnapshot.Configure"/>)، عَلى قاعِدَتِه.</summary>
    private static WebApplication BuildApp(string cs, Captured? logs = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration[ProviderVault.KekConfigKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (logs is not null) builder.Logging.AddProvider(logs);
        builder.Services.AddMarten(o =>
            {
                o.Connection(cs.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
                o.DatabaseSchemaName = "platform";
                o.Policies.AllDocumentsAreMultiTenanted();
                o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
                o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
                o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.Host.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Policies.AutoApplyTransactions();
        });
        builder.Services.AddSignalR();
        builder.Services.AddCustomerMarketplaceTemplate();
        builder.Services.AddSingleton<IPaymentProvider>(new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        builder.Services.AddHttpClient(nameof(MoyasarClient));
        Wolverine.HostBuilderExtensions.ConfigureWolverine(builder.Services, PresenceSnapshot.Configure);

        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{FreePort()}");
        return app;
    }

    /// <summary>
    /// <para>المَتاجِرُ الثَلاثَة: <see cref="StoreA"/> بِكُلِّ ما يُعَدّ — أَدوارُ كاتالوجٍ ثَلاثَةٌ ودَورٌ مُؤَلَّفٌ مُعتَمَد، وثيمانِ وباقَةٌ مُعتَمَدَة (ومُعَلَّقٌ لا يُعَدّ)،
    /// ورَبطُ ميسّر ورَبطٌ لا مِترَ لَه ومَدخَلا بائِعَينِ عِندَ OTO ومَدخَلٌ مَسحوب، ومِفتاحانِ نَشِطانِ ومَسحوبٌ، وقَناةُ الجَوّال؛ و<see cref="StoreS"/> مُعَلَّقٌ
    /// بِأَدوارِه ورَبطِه ومِفتاحِه؛ و<see cref="StoreC"/> بِلا مالِكٍ ولا شَيءَ إلّا قَناةَ البَريد.</para>
    /// </summary>
    private static async Task SeedAsync(IDocumentStore store)
    {
        Role Instance(string slug, int order) => RoleCatalog.InstantiateRole(RoleCatalog.Find(slug)!, order);
        await using (var s = store.LightweightSession())
        {
            s.Insert(new Tenant { Id = StoreA, Name = "أَمَل", OwnerUserId = OwnerA, AuthChannel = "phone", Roles = [Instance("tenant_admin", 0), Instance("vendor", 1), Instance("customer", 2)] });
            s.Insert(new Tenant { Id = StoreS, Name = "مَوقوف", OwnerUserId = OwnerA, AuthChannel = "phone", IsSuspended = true, Roles = [Instance("vendor", 0)] });
            s.Insert(new Tenant { Id = StoreC, Name = "بِلا", OwnerUserId = Guid.Empty, AuthChannel = "email" });
            foreach (var (id, tenant, status) in new[] { ("k1", StoreA, ApiKeyDocument.StatusActive), ("k2", StoreA, ApiKeyDocument.StatusActive),
                                                         ("k3", StoreA, ApiKeyDocument.StatusRevoked), ("k4", StoreS, ApiKeyDocument.StatusActive) })
                s.Insert(new ApiKeyDocument { Id = $"ps-{id}-{Guid.NewGuid():N}", TenantSlug = tenant, Name = id, Status = status });
            await s.SaveChangesAsync();
        }
        await using (var a = store.LightweightSession(StoreA))
        {
            a.Store(new TenantRoleDefinition { Id = "khayyat", Slug = "khayyat", DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson, Status = ApprovalFlow.Approved, CreatedBy = "t" });
            a.Store(new TenantThemeDefinition { Id = "t1", Slug = "t1", DefinitionJson = "{}", Status = ApprovalFlow.Approved, CreatedBy = "t" });
            a.Store(new TenantThemeDefinition { Id = "t2", Slug = "t2", DefinitionJson = "{}", Status = ApprovalFlow.Approved, CreatedBy = "t" });
            a.Store(new TenantThemeDefinition { Id = "t3", Slug = "t3", DefinitionJson = "{}", Status = ApprovalFlow.Pending, CreatedBy = "t" });
            a.Store(new TenantPlanDefinition { Id = "p1", Slug = "p1", DefinitionJson = "{}", Status = ApprovalFlow.Approved, CreatedBy = "t" });
            a.Store(new TenantProviderBinding { Id = ProviderCapabilities.Payments, Slug = ProviderCapabilities.Payments, TenantSlug = StoreA, ProviderSlug = "moyasar_api", BoundBy = "t" });
            a.Store(new TenantProviderBinding { Id = ProviderCapabilities.Files, Slug = ProviderCapabilities.Files, TenantSlug = StoreA, ProviderSlug = "local_files", BoundBy = "t" });
            foreach (var (vendor, status) in new[] { (Vendor1, TenantProviderBinding.StatusActive), (Vendor2, TenantProviderBinding.StatusActive),
                                                     (Guid.Parse("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff"), TenantProviderBinding.StatusRevoked) })
                a.Store(new TenantProviderBinding { Id = TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, vendor), Slug = ProviderCapabilities.Delivery,
                                                    TenantSlug = StoreA, ProviderSlug = "oto_vendor_account", Status = status, BoundBy = "t" });
            await a.SaveChangesAsync();
        }
        await using (var z = store.LightweightSession(StoreS))
        {
            z.Store(new TenantProviderBinding { Id = ProviderCapabilities.Payments, Slug = ProviderCapabilities.Payments, TenantSlug = StoreS, ProviderSlug = "moyasar_api", BoundBy = "t" });
            await z.SaveChangesAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(string cs, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<string>> RowsAsync(string cs, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        var rows = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add(Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "");
        return rows;
    }

    /// <summary>سُطورُ يَومٍ واحِدٍ بِمَتجَرِها ووَحدَتِها وكَمِّيَّتِها — مِن مُعَرِّفِ السَطر (<c>{unit}:{app}:{day}:{subject}</c>).</summary>
    private static Task<List<string>> DayRowsAsync(string cs, DateOnly day)
        => RowsAsync(cs, "select app || ' ' || unit || ' ' || role || ' ' || provider || ' ' || feature || ' ' || quantity from meter.usage_entry "
                         + "where unit like 'presence.%' and id like '%:' || @d || ':%' order by app collate \"C\", unit collate \"C\", role collate \"C\", provider collate \"C\"", ("d", PresenceReader.DayKey(day)));

    private static Task<long> ClaimsAsync(string cs, DateOnly day)
        => ScalarAsync<long>(cs, "select count(*) from meter.presence_claim where day = @d", ("d", day));

    /// <summary>كُلُّ صَفٍّ عَلى قاعِدَةٍ جَديدَةٍ ومُضيفٍ مُقلِعٍ بِبَذرِه — ويُحذَفانِ بَعدَه.</summary>
    private async Task WithHostAsync(string tag, Func<WebApplication, IDocumentStore, string, Task> body, Captured? logs = null)
    {
        var (database, cs) = FreshDatabase(tag);
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[G12] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            var app = BuildApp(cs, logs);
            await app.StartAsync();
            try
            {
                var store = app.Services.GetRequiredService<IDocumentStore>();
                var boot = await MeterBoot.EnsureAsync(store);
                output.WriteLine($"[G12] {boot.Line}");
                await SeedAsync(store);
                await body(app, store, cs);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[G12] حُذِفَت {database}");
        }
    }

    private void Print(string label, IEnumerable<string> rows)
    {
        var list = rows.ToList();
        output.WriteLine($"[G12] {label}: {list.Count} سَطراً");
        foreach (var r in list) output.WriteLine($"[G12]     {r}");
    }

    // ─── ١. مُطالَبَةٌ واحِدَةٌ لِكُلِّ يَوم، والمَتاجِرُ × الوَحَدات، ومُعامَلَةٌ واحِدَة ───────────────────────────────────

    [Fact]
    public async Task One_claim_per_day_one_transaction_and_every_store_by_unit_from_stored_truth()
    {
        if (!Enabled) return;
        await WithHostAsync("ps", async (app, store, cs) =>
        {
            var day = new DateOnly(2030, 1, 5);
            var snapshot = new PresenceSnapshot(store, app.Services.GetRequiredService<ILogger<PresenceSnapshot>>());
            var first = await snapshot.TakeAsync(day, PresenceDefinitions.Platform);
            var second = await snapshot.TakeAsync(day, PresenceDefinitions.Platform);
            output.WriteLine($"[G12] الأُولى: {first.Line}");
            output.WriteLine($"[G12] الثانِيَة: {second.Line}");
            var rows = await DayRowsAsync(cs, day);
            Print($"المَتاجِرُ × الوَحَدات ليَوم {PresenceReader.DayKey(day)} (مَتجَر وَحدَة دَور مُزَوِّد مِيزَة كَمِّيَّة)", rows);
            var claims = await ClaimsAsync(cs, day);
            var completed = await ScalarAsync<long>(cs, "select count(*) from meter.presence_claim where day = @d and completed_at is not null", ("d", day));
            // ‏مُعامَلَةٌ واحِدَة: مُعَرِّفُ مُعامَلَةِ كُلِّ سَطرٍ = مُعَرِّفُ المُعامَلَةِ الَّتي كَتَبَت المُطالَبَة (xmin).
            var xids = await RowsAsync(cs, "select distinct e.writer_xid::xid::text || '=' || c.xmin::text from meter.usage_entry e, meter.presence_claim c "
                                           + "where c.day = @d and e.id like '%:' || @k || ':%'", ("d", day), ("k", PresenceReader.DayKey(day)));
            var occurred = await RowsAsync(cs, "select distinct to_char((occurred_at at time zone '" + MeterSql.DayZone + "'), 'YYYY-MM-DD HH24:MI') || ' · gen ' || generation "
                                               + "from meter.usage_entry where unit like 'presence.%'");
            output.WriteLine($"[G12] مُطالَبات {claims} · مُكتَمِلَة {completed} · مُعامَلات [{string.Join(", ", xids)}] · يَومُ السُطورِ وجيلُها [{string.Join(", ", occurred)}]");

            Assert.True(first.Claimed);
            Assert.False(second.Claimed);
            Assert.Empty(second.Entries);
            Assert.Equal((1L, 1L), (claims, completed));
            Assert.Equal(first.Entries.Count, rows.Count);
            Assert.True(rows.Count >= 12, $"أَداةٌ عَمياء: {rows.Count} سَطرَ حُضور.");
            var xid = Assert.Single(xids);
            Assert.Equal(xid.Split('=')[0], xid.Split('=')[1]);
            Assert.Equal(new[] { "2030-01-05 00:00 · gen 1" }, occurred);
            Assert.Equal(new[]
            {
                $"{StoreA} presence.api_key _system _none api.call 2",
                $"{StoreA} presence.auth_channel _system _none auth 1",
                $"{StoreA} presence.authored_role _system _none roles 1",
                $"{StoreA} presence.authored_theme _system _none theme 2",
                $"{StoreA} presence.member_plan _system _none subscriptions 1",
                $"{StoreA} presence.provider_binding _system moyasar_api payments 1",
                $"{StoreA} presence.role customer _none roles 1",
                $"{StoreA} presence.role khayyat _none roles 1",
                $"{StoreA} presence.role tenant_admin _none roles 1",
                $"{StoreA} presence.role vendor _none roles 1",
                $"{StoreA} presence.store _system _none tenants 1",
                $"{StoreA} presence.vendor_binding _system oto_vendor_account delivery 2",
                $"{StoreC} presence.auth_channel _system _none auth 1",
                $"{StoreC} presence.store _system _none tenants 1",
            }, rows);
            // ‏رائِدُ الأَعمال: مالِكُ المَتجَرِ أَو `_unowned`؛ والمُعَلَّقُ لا سَطرَ لَه؛ ومُعَرِّفُ البائِعِ لا يَدخُلُ الدَفتَر.
            var owners = await RowsAsync(cs, "select distinct app || '=' || entrepreneur from meter.usage_entry where unit like 'presence.%'");
            var suspended = await ScalarAsync<long>(cs, "select count(*) from meter.usage_entry where app = @a and unit like 'presence.%'", ("a", StoreS));
            var leaks = await ScalarAsync<long>(cs, "select count(*) from meter.usage_entry where id ilike any(@v) or source_id ilike any(@v)",
                ("v", new[] { $"%{Vendor1:D}%", $"%{Vendor2:D}%", $"%{Vendor1:N}%", $"%{Vendor2:N}%" }));
            output.WriteLine($"[G12] رُوّادُ الأَعمال [{string.Join(", ", owners)}] · سُطورُ المُعَلَّق {suspended} · مُعَرِّفُ بائِعٍ في الدَفتَر {leaks}");
            Assert.Equal(new[] { $"{StoreA}={OwnerA:D}", $"{StoreC}={MeterSentinels.Unowned}" }, owners.Order(StringComparer.Ordinal));
            Assert.Equal(0, suspended);
            Assert.Equal(0, leaks);
        });
    }

    // ─── ٢. مُعالِجانِ مُتَسابِقان: يَكتُبُ واحِد، والثاني يَقِفُ عَلى المِفتاحِ ثُمَّ يَخسَر ─────────────────────────────

    [Fact]
    public async Task Two_handlers_racing_one_writes_and_the_second_waits_on_the_claim_then_loses()
    {
        if (!Enabled) return;
        await WithHostAsync("ps", async (app, store, cs) =>
        {
            var log = app.Services.GetRequiredService<ILogger<PresenceSnapshot>>();
            // (أ) مَعاً، بِلا تَرتيب: رابِحٌ واحِد.
            var raceDay = new DateOnly(2030, 2, 1);
            var race = await Task.WhenAll(new PresenceSnapshot(store, log).TakeAsync(raceDay, PresenceDefinitions.Platform),
                                          new PresenceSnapshot(store, log).TakeAsync(raceDay, PresenceDefinitions.Platform));
            var raceRows = (await DayRowsAsync(cs, raceDay)).Count;
            output.WriteLine($"[G12] السِباق: {string.Join(" | ", race.Select(r => r.Line))} · سُطورُ اليَوم {raceRows} · مُطالَبات {await ClaimsAsync(cs, raceDay)}");
            Assert.Equal(1, race.Count(r => r.Claimed));
            Assert.Equal(race.Single(r => r.Claimed).Entries.Count, raceRows);
            Assert.Equal(1, await ClaimsAsync(cs, raceDay));

            // (ب) والوُقوفُ مَقيس: مُعامَلَةٌ أُخرى تُمسِكُ مُطالَبَةَ اليَومِ ولَم تُودِع — المُعالِجُ يَقِفُ عَلَيها؛ تُودِعُ ⇐ يَخسَرُ بِلا سَطر.
            foreach (var (day, commit) in new[] { (new DateOnly(2030, 2, 2), true), (new DateOnly(2030, 2, 3), false) })
            {
                await using var holder = new NpgsqlConnection(cs);
                await holder.OpenAsync();
                await using var tx = await holder.BeginTransactionAsync();
                await using (var claim = new NpgsqlCommand(MeterSql.PresenceClaimInsert, holder, tx))
                {
                    claim.Parameters.AddWithValue("day", day);
                    Assert.NotNull(await claim.ExecuteScalarAsync());
                }
                var waiting = new PresenceSnapshot(store, log).TakeAsync(day, PresenceDefinitions.Platform);
                var blocked = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(2))) != waiting;
                if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
                var taken = await waiting.WaitAsync(TimeSpan.FromSeconds(60));
                var rows = (await DayRowsAsync(cs, day)).Count;
                output.WriteLine($"[G12] مُطالَبَةٌ مُمسَكَة ({(commit ? "تُودَع" : "تَتَراجَع")}): وَقَفَ المُعالِج {blocked} · {taken.Line} · سُطور {rows} · مُطالَبات {await ClaimsAsync(cs, day)}");
                Assert.True(blocked, "المُعالِجُ لَم يَقِف عَلى المُطالَبَةِ المُمسَكَة — المِفتاحُ الفَريدُ لا يَحرُسُ اليَوم.");
                Assert.Equal(!commit, taken.Claimed);
                Assert.Equal(commit ? 0 : taken.Entries.Count, rows);
                Assert.True(commit || rows > 0, "الرابِحُ بَعدَ التَراجُعِ بِلا سَطر.");
                Assert.Equal(1, await ClaimsAsync(cs, day));
            }
        });
    }

    // ─── ٣. عُطلٌ في وَسَطِ المُعامَلَة: لا سَطرَ ولا مُطالَبَة، والإعادَةُ تَكتُب ─────────────────────────────────────

    [Fact]
    public async Task A_crash_mid_transaction_leaves_no_entry_and_no_claim_and_the_retry_writes()
    {
        if (!Enabled) return;
        await WithHostAsync("ps", async (app, store, cs) =>
        {
            var day = new DateOnly(2030, 3, 1);
            // ‏العُطلُ مَحقونٌ في القاعِدَة: سَطرُ دَورٍ يَرمي — بَعدَ المُطالَبَةِ وسَطرِ المَتجَرِ في المُعامَلَةِ نَفسِها.
            await ExecAsync(cs, "create function meter.g12_crash() returns trigger language plpgsql as $$ begin "
                                + "if new.unit = 'presence.role' then raise exception 'g12 injected crash'; end if; return new; end $$; "
                                + "create trigger g12_crash before insert on meter.usage_entry for each row execute function meter.g12_crash()");
            var snapshot = new PresenceSnapshot(store, app.Services.GetRequiredService<ILogger<PresenceSnapshot>>());
            var crash = await Record.ExceptionAsync(() => snapshot.TakeAsync(day, PresenceDefinitions.Platform));
            var afterCrash = ((await DayRowsAsync(cs, day)).Count, await ClaimsAsync(cs, day));
            await ExecAsync(cs, "drop trigger g12_crash on meter.usage_entry; drop function meter.g12_crash()");
            var retry = await snapshot.TakeAsync(day, PresenceDefinitions.Platform);
            var afterRetry = ((await DayRowsAsync(cs, day)).Count, await ClaimsAsync(cs, day));
            output.WriteLine($"[G12] العُطل: {crash?.GetType().Name} «{crash?.Message.Split('\n')[0]}» ⇐ سُطور {afterCrash.Item1} · مُطالَبات {afterCrash.Item2} · الإعادَة: {retry.Line} ⇐ سُطور {afterRetry.Item1} · مُطالَبات {afterRetry.Item2}");
            Assert.IsType<PostgresException>(crash);
            Assert.Equal((0, 0L), afterCrash);
            Assert.True(retry.Claimed);
            Assert.Equal((retry.Entries.Count, 1L), afterRetry);
            Assert.True(retry.Entries.Count > 0);
        });
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── ٤. المِترُ في نُسخَةٍ مِن التَعريفِ يُغَيِّرُ ما يُعَدّ ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_meter_changed_in_a_copied_provider_or_role_definition_changes_what_the_snapshot_counts()
    {
        if (!Enabled) return;
        await WithHostAsync("ps", async (app, store, cs) =>
        {
            var log = app.Services.GetRequiredService<ILogger<PresenceSnapshot>>();
            // ‏نُسخَةُ ميسّر بِحُضورٍ «none» — والمُصادِقُ يَقبَلُها (none جائِزٌ في كُلِّ خانَة).
            var moyasar = ProviderCatalog.Find("moyasar_api")!;
            var quiet = moyasar with { Meter = new ProviderMeterDefinition { Presence = ProviderMeterPresences.None } };
            Assert.Empty(ProviderMeterValidator.Validate(quiet));
            var providers = new PresenceDefinitions([.. ProviderCatalog.Definitions.Select(d => d.Slug == moyasar.Slug ? quiet : d)], PresenceDefinitions.Platform.RoleMeter);
            // ‏ونُسخَةُ دَورِ البائِعِ بِمِترٍ «none» — والمُصادِقُ يَقبَلُها.
            var vendor = RoleCatalog.FindDefinition("vendor")! with { Meter = RoleMeterUnits.None };
            Assert.Empty(RoleMeterValidator.Validate(vendor));
            var roles = new PresenceDefinitions(ProviderCatalog.Definitions, (set, slug) => slug == vendor.Slug ? vendor.Meter! : set.MeterOf(slug));

            var (asIs, noBinding, noVendorRole) = (new DateOnly(2030, 4, 1), new DateOnly(2030, 4, 2), new DateOnly(2030, 4, 3));
            await new PresenceSnapshot(store, log).TakeAsync(asIs, PresenceDefinitions.Platform);
            await new PresenceSnapshot(store, log).TakeAsync(noBinding, providers);
            await new PresenceSnapshot(store, log).TakeAsync(noVendorRole, roles);
            async Task<long> Count(DateOnly d, string unit, string? role = null)
                => (await DayRowsAsync(cs, d)).Count(r => r.Split(' ')[1] == unit && (role is null || r.Split(' ')[2] == role));
            var line = $"provider_binding: كَما هُوَ {await Count(asIs, PresenceUnits.ProviderBinding)} · ميسّر none {await Count(noBinding, PresenceUnits.ProviderBinding)} — "
                       + $"presence.role لِلبائِع: كَما هُوَ {await Count(asIs, PresenceUnits.Role, "vendor")} · البائِعُ none {await Count(noVendorRole, PresenceUnits.Role, "vendor")} · "
                       + $"أَدوارٌ أُخرى {await Count(noVendorRole, PresenceUnits.Role)}";
            output.WriteLine($"[G12] {line}");
            Assert.Equal(1, await Count(asIs, PresenceUnits.ProviderBinding));
            Assert.Equal(0, await Count(noBinding, PresenceUnits.ProviderBinding));
            Assert.Equal(1, await Count(asIs, PresenceUnits.Role, "vendor"));
            Assert.Equal(0, await Count(noVendorRole, PresenceUnits.Role, "vendor"));
            Assert.Equal(3, await Count(noVendorRole, PresenceUnits.Role));
            Assert.Equal((await DayRowsAsync(cs, asIs)).Count - 1, (await DayRowsAsync(cs, noBinding)).Count);
        });
    }

    // ─── ٥. الرِسالَةُ المُتَأَخِّرَةُ عَبرَ الطابورِ المَتين: لا سَطرَ ولا مُطالَبَة، وسَطرُها، واليَومُ يُؤخَذُ الآن ───────────────────

    private static async Task<List<(string Status, DateTimeOffset? At, string Body)>> EnvelopesAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select status, execution_time, body from platform.wolverine_incoming_envelopes where message_type like @type order by status, execution_time nulls first", conn);
        cmd.Parameters.AddWithValue("type", "%" + nameof(TakePresenceSnapshot) + "%");
        var rows = new List<(string, DateTimeOffset?, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var body = System.Text.Encoding.UTF8.GetString((byte[])r.GetValue(2));
            // ‏يَومُ الرِسالَةِ مِن حَقلِها (`Day`) لا أَوَّلُ تاريخٍ في الغِلاف — رُؤوسُه تَحمِلُ وَقتَ الإرسال.
            var day = System.Text.RegularExpressions.Regex.Match(body, @"""day""\s*:\s*""(\d{4}-\d{2}-\d{2})""", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            rows.Add((r.GetString(0), r.IsDBNull(1) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)), day));
        }
        return rows;
    }

    private static string Show(IEnumerable<(string Status, DateTimeOffset? At, string Body)> rows)
        => "[" + string.Join(", ", rows.Select(e => $"{e.Body} {e.Status}{(e.At is { } at ? "@" + at.ToString("yyyy-MM-ddTHH:mmZ", System.Globalization.CultureInfo.InvariantCulture) : "")}")) + "]";

    [Fact]
    public async Task Yesterdays_message_writes_nothing_says_presence_late_and_today_is_taken_now()
    {
        if (!Enabled) return;
        var logs = new Captured();
        await WithHostAsync("ps", async (app, store, cs) =>
        {
            var today = PresenceSnapshot.Today();
            var yesterday = today.AddDays(-1);
            using (var scope = app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new TakePresenceSnapshot(yesterday));

            var deadline = Stopwatch.StartNew();
            var todayDone = false;
            while (!todayDone && deadline.Elapsed < TimeSpan.FromSeconds(90))
            {
                todayDone = await ScalarAsync<long>(cs, "select count(*) from meter.presence_claim where day = @d and completed_at is not null", ("d", today)) == 1;
                if (!todayDone) await Task.Delay(500);
            }
            // ‏والغَدُ يُجَدوَلُ بَعدَ إيداعِ اليَوم — في الطابورِ المَتين.
            var envelopes = await EnvelopesAsync(cs);
            for (var i = 0; i < 40 && !envelopes.Any(e => e.Status == "Scheduled"); i++) { await Task.Delay(250); envelopes = await EnvelopesAsync(cs); }
            var late = logs.Lines.Where(l => l.Contains("[meter] presence-late", StringComparison.Ordinal)).ToList();
            var taken = logs.Lines.Where(l => l.StartsWith("[meter] presence day=", StringComparison.Ordinal)).ToList();
            var yesterdayRows = (await DayRowsAsync(cs, yesterday)).Count;
            var todayRows = (await DayRowsAsync(cs, today)).Count;
            output.WriteLine($"[G12] أَمس {PresenceReader.DayKey(yesterday)} · اليَوم {PresenceReader.DayKey(today)} — اكتَمَلَ اليَومُ بَعدَ {deadline.Elapsed.TotalSeconds:0.0}ث: {todayDone}");
            output.WriteLine($"[G12] سُطورُ السِجِلّ: «{string.Join(" | ", late)}» · «{string.Join(" | ", taken)}»");
            output.WriteLine($"[G12] سُطورُ أَمس {yesterdayRows} · مُطالَبَةُ أَمس {await ClaimsAsync(cs, yesterday)} · سُطورُ اليَوم {todayRows} · صُفوفُ الصُندوق {Show(envelopes)}");

            Assert.True(todayDone, "اليَومُ لَم يُؤخَذ بَعدَ رَفضِ المُتَأَخِّرَة.");
            Assert.Equal(0, yesterdayRows);
            Assert.Equal(0, await ClaimsAsync(cs, yesterday));
            Assert.Contains(late, l => l.Contains($"day={PresenceReader.DayKey(yesterday)}", StringComparison.Ordinal));
            Assert.Contains(taken, l => l.Contains($"day={PresenceReader.DayKey(today)} claimed=true", StringComparison.Ordinal));
            Assert.True(todayRows >= 12, $"أَداةٌ عَمياء: {todayRows} سَطراً لِليَوم.");
            var next = Assert.Single(envelopes, e => e.Status == "Scheduled");
            Assert.Equal(PresenceReader.DayKey(today.AddDays(1)), next.Body);
            Assert.Equal(PresenceSnapshot.StartOf(today.AddDays(1)), next.At);
        }, logs);
    }

    // ─── ٦. P12 — التَطبيقُ المَبنِيُّ يُقلِعُ مَرَّتَين: مُطالَبَةٌ واحِدَةٌ لِليَوم، وصُفوفُ الصُندوقِ قَبلُ وبَعد ────────────────────────────

    private static string AppDll()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "bin", tfm.Parent!.Name, tfm.Name, "V1.App.dll");
    }

    /// <summary>إقلاعٌ واحِدٌ لِلتَطبيقِ المَبنِيّ حَتّى <c>/health/ready</c> ثُمَّ حَتّى يَصدُقَ <paramref name="settled"/> (أَو تِسعونَ ثانِيَة) — ويُطفَأُ بِمُعَرِّفِ عَمَلِيَّتِه.</summary>
    private static async Task<(IReadOnlyList<string> Log, bool Settled)> BootAsync(string dll, string cs, Func<Task<bool>> settled)
    {
        var lines = new List<string>();
        Process? app = null;
        var ok = false;
        try
        {
            var port = FreePort();
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add($"--urls=http://127.0.0.1:{port}");
            psi.Environment["ConnectionStrings__Postgres"] = cs;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            psi.Environment["PLATFORM_ADMIN_PHONE"] = "0500000001";
            psi.Environment["TEST_DATA_SEED"] = "1";
            psi.Environment["APPEARANCE_BASELINE_SEED"] = "1";
            app = Process.Start(psi)!;
            app.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            app.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
            app.BeginOutputReadLine();
            app.BeginErrorReadLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var ready = false;
            for (var i = 0; i < 300 && !ready && !app.HasExited; i++)
            {
                try { ready = (await http.GetStringAsync($"http://127.0.0.1:{port}/health/ready")).Contains("\"ready\":true", StringComparison.Ordinal); }
                catch (Exception) { /* لَم يَستَمِع بَعد */ }
                if (!ready) await Task.Delay(1000);
            }
            string Log() { lock (lines) return string.Join("\n", lines.TakeLast(40)); }
            Assert.True(ready, $"التَطبيقُ لَم يَجهَز (خَرَجَ: {app.HasExited}):\n{Log()}");
            for (var i = 0; i < 180 && !ok && !app.HasExited; i++)
            {
                ok = await settled();
                if (!ok) await Task.Delay(500);
            }
        }
        finally
        {
            if (app is { HasExited: false }) { app.Kill(entireProcessTree: true); await app.WaitForExitAsync(); }
            app?.Dispose();
            NpgsqlConnection.ClearAllPools();
        }
        lock (lines) return ([.. lines], ok);
    }

    private static IEnumerable<string> MeterLines(IEnumerable<string> log, string tag)
        => log.Where(l => l.Contains(tag, StringComparison.Ordinal)).Select(l => l[l.IndexOf(tag, StringComparison.Ordinal)..].Trim());

    [Fact]
    public async Task P12_the_built_app_booted_twice_takes_today_once_and_schedules_tomorrow_at_each_boot()
    {
        if (!Enabled) return;
        var dll = AppDll();
        Assert.True(File.Exists(dll), $"التَطبيقُ غَيرُ مَبنِيّ: {dll} — يُبنى الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml).");
        var (database, cs) = FreshDatabase("p12");
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[P12] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            var today = PresenceSnapshot.Today();
            var (todayKey, tomorrowKey) = (PresenceReader.DayKey(today), PresenceReader.DayKey(today.AddDays(1)));
            async Task<int> Count(string status, string day) => (await EnvelopesAsync(cs)).Count(e => e.Status == status && e.Body == day);
            async Task<bool> Exists() => await ScalarAsync<bool>(cs, "select to_regclass('meter.presence_claim') is not null and to_regclass('platform.wolverine_incoming_envelopes') is not null");

            var before = await ScalarAsync<bool>(cs, "select to_regclass('platform.wolverine_incoming_envelopes') is not null");
            output.WriteLine($"[P12] صُفوفُ الصُندوقِ قَبلَ الإقلاع: {(before ? Show(await EnvelopesAsync(cs)) : "لا جَدوَل — قاعِدَةٌ فارِغَة")}");

            // ‏الإقلاعُ الأَوَّل: اليَومُ يُرسَلُ ويُؤخَذ، والغَدُ يُجَدوَلُ مَرَّتَين — مِن الإقلاعِ ومِن إيداعِ اليَوم.
            var (cold, coldSettled) = await BootAsync(dll, cs, async () => await Exists()
                && await ScalarAsync<long>(cs, "select count(*) from meter.presence_claim where completed_at is not null") == 1
                && await Count("Scheduled", tomorrowKey) == 2);
            var afterCold = await EnvelopesAsync(cs);
            var rowsCold = await RowsAsync(cs, "select app || ' ' || string_agg(unit || ' ' || n, ' · ' order by unit) from (select app, unit, sum(quantity) n from meter.usage_entry "
                                               + "where unit like 'presence.%' group by app, unit) t group by app order by app");
            var entriesCold = await ScalarAsync<long>(cs, "select count(*) from meter.usage_entry where unit like 'presence.%'");
            output.WriteLine($"[P12] الإقلاعُ الأَوَّل: سَكَن {coldSettled} · {string.Join(" | ", MeterLines(cold, "[meter] boot").Concat(MeterLines(cold, "[meter] presence")))}");
            Print2("[P12] المَتاجِرُ × الوَحَدات (مَجموعُ الكَمِّيَّة)", rowsCold);
            output.WriteLine($"[P12] صُفوفُ الصُندوقِ بَعدَ الإقلاعِ الأَوَّل: {Show(afterCold)}");

            // ‏الإقلاعُ الثاني عَلى القاعِدَةِ نَفسِها: اليَومُ مُكتَمِلٌ فَلا يُرسَل، والغَدُ يُجَدوَلُ مَرَّةً ثالِثَة.
            var (warm, warmSettled) = await BootAsync(dll, cs, async () => await Count("Scheduled", tomorrowKey) == 3);
            var afterWarm = await EnvelopesAsync(cs);
            var entriesWarm = await ScalarAsync<long>(cs, "select count(*) from meter.usage_entry where unit like 'presence.%'");
            var claims = await RowsAsync(cs, "select day::text || ' ' || (completed_at is not null)::text from meter.presence_claim order by day");
            var stores = await ScalarAsync<long>(cs, "select count(*) from platform.mt_doc_tenant where not coalesce((data->>'IsSuspended')::boolean, false)");
            var storeRows = await ScalarAsync<long>(cs, "select count(*) from meter.usage_entry where unit = 'presence.store'");
            output.WriteLine($"[P12] الإقلاعُ الثاني: سَكَن {warmSettled} · {string.Join(" | ", MeterLines(warm, "[meter] boot").Concat(MeterLines(warm, "[meter] presence")))}");
            output.WriteLine($"[P12] صُفوفُ الصُندوقِ بَعدَ الإقلاعِ الثاني: {Show(afterWarm)}");
            output.WriteLine($"[P12] المُطالَبات [{string.Join(", ", claims)}] · سُطورُ الحُضور {entriesCold} ← {entriesWarm} · presence.store {storeRows} لِمَتاجِرَ غَيرِ مُعَلَّقَة {stores}");

            Assert.True(coldSettled && warmSettled, $"لَم يَسكُن الإقلاع: الأَوَّل {coldSettled} · الثاني {warmSettled}");
            Assert.Equal(new[] { $"{todayKey} true" }, claims);
            Assert.Equal(entriesCold, entriesWarm);
            Assert.True(stores >= 3, $"أَداةٌ عَمياء: {stores} مَتجَراً مَبذوراً.");
            Assert.Equal(stores, storeRows);
            Assert.Equal(1, afterWarm.Count(e => e.Body == todayKey));
            Assert.Equal(3, afterWarm.Count(e => e.Body == tomorrowKey && e.Status == "Scheduled"));
            Assert.All(afterWarm.Where(e => e.Body == tomorrowKey), e => Assert.Equal(PresenceSnapshot.StartOf(today.AddDays(1)), e.At));
            Assert.Contains(MeterLines(cold, "[meter] presence-boot"), l => l.Contains($"today={todayKey} sent=yes", StringComparison.Ordinal));
            Assert.Contains(MeterLines(warm, "[meter] presence-boot"), l => l.Contains($"today={todayKey} sent=no", StringComparison.Ordinal));
            Assert.Contains(MeterLines(cold, "[meter] presence day="), l => l.Contains($"day={todayKey} claimed=true", StringComparison.Ordinal));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[P12] حُذِفَت {database}");
        }
    }

    private void Print2(string label, IReadOnlyList<string> rows)
    {
        output.WriteLine($"{label}: {rows.Count} مَتجَراً");
        foreach (var r in rows) output.WriteLine($"[P12]     {r}");
    }

    // ─── ٧. القِراءَةُ لا تُنشِئُ جَدوَلاً (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ د الأولى · W13-6·D-R1·٣؛ ADR-051 §٢٣ وسَطرُه المُؤَرَّخ) ──────────────────────────
    // سُؤالُ Marten عَن نَوعٍ يُنشِئُ جَدوَلَه (`AutoCreate`)، فَكانَت لَقطَةُ الإقلاعِ تُنشِئُ جَداوِلَ تَعريفاتٍ فارِغَةً عَلى قاعِدَةٍ جَديدَة — وبَصمَةُ البَذرِ
    // (`LiveMeterSeededFootprintCharacterizationProofTests`) تَحمَرُّ إن سَبَقَت اللَقطَةُ إطفاءَ التَطبيق: سِباقٌ لا حُكم. هُنا بِلا سِباق: قاعِدَةٌ بِمَتجَرٍ واحِدٍ ولا جَدوَلَ تَعريف.

    [Fact]
    public async Task A_snapshot_on_a_database_without_definition_tables_counts_them_as_none_and_creates_no_table()
    {
        if (!Enabled) return;
        var (database, cs) = FreshDatabase("ps");
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[G12·٧] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            var app = BuildApp(cs);
            await app.StartAsync();
            try
            {
                var store = app.Services.GetRequiredService<IDocumentStore>();
                output.WriteLine($"[G12·٧] {(await MeterBoot.EnsureAsync(store)).Line}");
                await using (var s = store.LightweightSession())
                {
                    s.Insert(new Tenant { Id = StoreC, Name = "بِلا", OwnerUserId = Guid.Empty, AuthChannel = "email" });
                    await s.SaveChangesAsync();
                }
                var definitions = new[] { "mt_doc_apikeydocument", "mt_doc_tenantroledefinition", "mt_doc_tenantthemedefinition", "mt_doc_tenantplandefinition", "mt_doc_tenantproviderbinding" };
                Task<List<string>> Present() => RowsAsync(cs, "select n from unnest(@n) n where to_regclass('platform.' || n) is not null order by n", ("n", definitions));
                var before = await Present();
                var day = new DateOnly(2030, 7, 1);
                var taken = await new PresenceSnapshot(store, app.Services.GetRequiredService<ILogger<PresenceSnapshot>>()).TakeAsync(day, PresenceDefinitions.Platform);
                var after = await Present();
                var rows = await DayRowsAsync(cs, day);
                output.WriteLine($"[G12·٧] جَداوِلُ التَعريفاتِ قَبلَ اللَقطَة [{string.Join(", ", before)}] · بَعدَها [{string.Join(", ", after)}] · {taken.Line}");
                Print($"سُطورُ {PresenceReader.DayKey(day)}", rows);
                Assert.Empty(before);
                Assert.True(taken.Claimed);
                Assert.Empty(after);
                Assert.Equal(new[] { $"{StoreC} presence.auth_channel _system _none auth 1", $"{StoreC} presence.store _system _none tenants 1" }, rows);
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[G12·٧] حُذِفَت {database}");
        }
    }
}
