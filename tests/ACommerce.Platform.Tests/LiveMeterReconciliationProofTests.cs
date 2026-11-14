using System.Diagnostics;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G13 · P7 — المُطابَقَةُ حَيَّة: نَظيفَةٌ لِكُلِّ وَحدَة، وكُلُّ عَيبٍ مَحقونٍ يُحَمِّرُها بِما سَمّاه التَصميم (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9 و§13 P7) ═══
//
// **لِماذا حَيّ** (القاعِدَة ١٤): الدَعوى في القاعِدَةِ نَفسِها — مَصدَرٌ كَتَبَه المُستَمِعُ في مُعامَلَتِه، ونِيَّةٌ وسَطرُها، ومَجاميعُ المُجَمِّعِ المَبنِيّ، ولَقطَةُ الحُضور،
// وما يَكتُبُه مَخزَنٌ بِلا مُستَمِعٍ (كَالمُستَورِد). وكُلُّ صَفٍّ عَلى قاعِدَةٍ جَديدَةٍ يُنشِئُها ويَحذِفُها (`{القاعِدَة}_mr_{8}`): المُطابَقَةُ تَقرَأُ القاعِدَةَ كُلَّها،
// فَلا تَتَقاسَمُها مَعَ صُفوفٍ أُخرى.
//
// **والتَركيبُ تَركيبُ التَطبيق** (`AddPlatformHost` + `AddCustomerMarketplaceTemplate` — ما يُرَكِّبُه السكربتُ نَفسُه)، والكِتابَةُ بِجَلَساتِه فَالمُستَمِعُ هُوَ ما يُسَجِّلُه
// القالِب. والحُكمُ بِالدالَّةِ الَّتي يُناديها السكربتُ وصَفحَةُ المَتجَر (`MeterReconciliation.RunAsync`)؛ والسكربتُ نَفسُه يُشَغَّلُ بِـ`dotnet run` عَلى النَظيفِ
// وعَلى حَقنٍ وعَلى صِفرِ المَصادِر — فَرَمزُ خُروجِه مَقيسٌ لا مُفتَرَض.
//
// **وتَرتيبُ الحَقن**: عُيوبُ الحِفظِ والصادِرِ قَبلَ أَن يَعمَلَ المُجَمِّع (فَلا يَختَلِطُ «ناقِصٌ» بِـ«مَجموعٌ لا يُطابِق»)، ثُمَّ المُجَمِّعُ المَبنِيُّ `--once`، ثُمَّ حَقنُ
// المَجموع؛ وكُلُّ حَقنٍ يُعادُ إلى حالِه (السَطرُ بِأَعمِدَتِه كُلِّها، `writer_xid` مَعَها) والنَظيفُ يُقاسُ بَعدَه.
[Collection(LiveProofCollection.Name)]
public class LiveMeterReconciliationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private const string A = "mr-amal";
    private const string B = "mr-bahr";
    private const string C = "mr-mustawrad";
    private const string D = "mr-bila-mustami";

    // ─── القاعِدَة ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Db : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Cs { get; init; }
        public required WebApplication App { get; init; }
        public IDocumentStore Store => App.Services.GetRequiredService<IDocumentStore>();

        /// <summary>قاعِدَةٌ جَديدَةٌ ومُضيفٌ بِتَركيبِ التَطبيق عَلَيها — لا يُقلِعُ الخادِم؛ المَخزَنُ وَحدَه يُستَعمَل.</summary>
        public static async Task<Db> CreateAsync(string tag)
        {
            var b = new NpgsqlConnectionStringBuilder(BaseCs);
            var stem = (b.Database ?? "wasayel").ToLowerInvariant();
            if (stem.Length > 48) stem = stem[..48];
            b.Database = $"{stem}_{tag}_{Guid.NewGuid().ToString("N")[..8]}";
            await ExecAsync(BaseCs, $"create database \"{b.Database}\"");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Configuration["ConnectionStrings:Postgres"] = b.ConnectionString.TrimEnd(';') + ";Timeout=60;Command Timeout=120";
            builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
            ACommerce.Platform.Hosting.HostingExtensions.AddPlatformHost(builder);
            ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.AddCustomerMarketplaceTemplate(builder.Services);
            return new Db { Name = b.Database, Cs = b.ConnectionString, App = builder.Build() };
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await ExecAsync(BaseCs, $"drop database if exists \"{Name}\" with (force)");
        }
    }

    private static async Task ExecAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>سُطورٌ تُحذَفُ لِحَقنٍ ثُمَّ تُعادُ بِأَعمِدَتِها كُلِّها — <c>writer_xid</c> و<c>generation</c> و<c>occurred_at</c> مَعَها، فَالدَفتَرُ بَعدَ الإعادَةِ هُوَ نَفسُه.</summary>
    private static async Task<IAsyncDisposable> RemovedAsync(string cs, string table, string where, params (string Name, object Value)[] ps)
    {
        var backup = "meter.mr_backup_" + Guid.NewGuid().ToString("N")[..8];
        await ExecAsync(cs, $"create table {backup} as select * from {table} where {where}", ps);
        await ExecAsync(cs, $"delete from {table} where {where}", ps);
        return new Restore(cs, table, backup);
    }

    private sealed class Restore(string cs, string table, string backup) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await ExecAsync(cs, $"insert into {table} select * from {backup}");
            await ExecAsync(cs, $"drop table {backup}");
        }
    }

    /// <summary>‏(W13-6·E-R2·١) صُفوفٌ تُبَدَّلُ لِحَقنٍ ثُمَّ تُعادُ بِأَعمِدَتِها كُلِّها — والشَرطُ لا يَمَسُّ العَمودَ المُبَدَّل، فَما يُحذَفُ عِندَ الإعادَةِ هُوَ ما بُدِّلَ بِعَينِه.</summary>
    private static async Task<IAsyncDisposable> AlteredAsync(string cs, string table, string set, string where, params (string Name, object Value)[] ps)
    {
        var backup = "meter.mr_backup_" + Guid.NewGuid().ToString("N")[..8];
        await ExecAsync(cs, $"create table {backup} as select * from {table} where {where}", ps);
        await ExecAsync(cs, $"update {table} set {set} where {where}", ps);
        return new Revert(cs, table, where, ps, backup);
    }

    private sealed class Revert(string cs, string table, string where, (string Name, object Value)[] ps, string backup) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await ExecAsync(cs, $"delete from {table} where {where}", ps);
            await ExecAsync(cs, $"insert into {table} select * from {backup}");
            await ExecAsync(cs, $"drop table {backup}");
        }
    }

    /// <summary>بُعدٌ مِن أَبعادِ عَدَمِ التَطابُقِ بِاسمِ عَمودِه في <c>agg_daily</c>.</summary>
    private static string Dimension(MeterAggregateMismatch m, string column) => column switch
    {
        "entrepreneur" => m.Entrepreneur,
        "role" => m.Role,
        "feature" => m.Feature,
        "provider" => m.Provider,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "عَمودٌ لا يُقارَن"),
    };

    private MeterReconciliationReport Print(string label, MeterReconciliationReport r)
    {
        foreach (var line in r.Lines()) output.WriteLine($"[G13·{label}] {line}");
        return r;
    }

    private static MeterUnitCheck Unit(MeterReconciliationReport r, string unit) => r.Units.Single(u => u.Unit == unit);
    private static MeterIntentCheck Intent(MeterReconciliationReport r, string unit) => r.Intents.Single(u => u.Unit == unit);

    // ─── السكربتُ والمُجَمِّعُ المَبنِيّان ───────────────────────────────────────────────────────────────────────

    /// <summary><c>dotnet run scripts/meter-reconcile.cs</c> عَلى القاعِدَةِ المُعطاة — رَمزُ خُروجِه وما طَبَعَه.</summary>
    private (int Code, string[] Lines) Script(string cs, string label)
    {
        var script = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", "meter-reconcile.cs");
        Assert.True(File.Exists(script), $"مَصدَرٌ مَفقود: {script}");
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ThemeZeroEquivalenceTests.RepoRoot,
        };
        foreach (var a in new[] { "run", script }) psi.ArgumentList.Add(a);
        psi.Environment["ConnectionStrings__Postgres"] = cs;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        psi.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        psi.Environment["UseSharedCompilation"] = "false";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(TimeSpan.FromMinutes(6)), "السكربتُ لَم يَنتَهِ في سِتِّ دَقائِق.");
        var lines = (stdout.Result + stderr.Result).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines.Where(l => l.Contains(MeterReconciliation.LogTag, StringComparison.Ordinal) || l.Contains('✗')))
            output.WriteLine($"[G13·script {label}] {l}");
        output.WriteLine($"[G13·script {label}] exit {p.ExitCode}");
        return (p.ExitCode, lines);
    }

    /// <summary>المُجَمِّعُ المَبنِيّ (<c>apps/V1.Meter</c>) بِتَهيِئَةِ الاختِبار — <c>--once</c>.</summary>
    private async Task AggregateAsync(string cs)
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var dll = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.Meter", "bin", tfm.Parent!.Name, tfm.Name, "V1.Meter.dll");
        Assert.True(File.Exists(dll), $"المُجَمِّعُ غَيرُ مَبنِيّ: {dll}");
        var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("--once");
        psi.Environment["ConnectionStrings__Postgres"] = cs;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await p.WaitForExitAsync(limit.Token);
        foreach (var l in (stdout.Result + stderr.Result).Split('\n', StringSplitOptions.RemoveEmptyEntries)) output.WriteLine($"[G13·agg] {l}");
        Assert.Equal(0, p.ExitCode);
    }

    // ─── المَصادِر ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <para>مَتجَران بِمالِكٍ واحِد (<see cref="A"/>: صَفقَتان وإعلانٌ ورِسالَةٌ وتَقييمٌ وعُضو، و<see cref="B"/>: ثَلاثُ صَفَقات) وحِسابُ استوديو — كُلُّها بِجَلَساتِ
    /// التَطبيقِ فَالمُستَمِعُ يَعُدُّها؛ وسِجِلُّ مَناطِقٍ تَكتُبُه المَنَصَّةُ في <see cref="A"/> (<c>ImportedRecord</c> بِجَدوَلِ <c>DiscoveryRegions</c> — لا يَجعَلُه مُستَورَداً)؛
    /// ونِيّاتُ <c>otp.sms.send</c> بِنَتائِجِها الخَمس بِكاتِبِ الصادِرِ نَفسِه؛ ولَقطَةُ حُضورِ اليَوم؛ ومَتجَرٌ مُستَورَدٌ <see cref="C"/> يَكتُبُه مَخزَنٌ بِلا مُستَمِع
    /// كَما يَكتُبُه <c>TargetWriter</c> (المَتجَرُ وعُضوٌ وإعلانٌ وسِجِلُّ جَدوَلٍ خامّ).</para>
    /// </summary>
    private async Task<(ProviderCall Accepted, Guid DealOfA)> SeedAsync(Db db)
    {
        var store = db.Store;
        var owner = Guid.NewGuid();
        await using (var g = store.LightweightSession())
        {
            g.Insert(new Tenant { Id = A, Name = "مَتجَرُ المُطابَقَة أ", OwnerUserId = owner });
            g.Insert(new Tenant { Id = B, Name = "مَتجَرُ المُطابَقَة ب", OwnerUserId = owner });
            g.Store(new StudioUser { Id = owner });
            await g.SaveChangesAsync();
        }
        var dealOfA = Guid.Empty;
        foreach (var (slug, n) in new[] { (A, 2), (B, 3) })
        {
            await using var s = store.LightweightSession(slug);
            for (var i = 0; i < n; i++)
            {
                var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = owner, CounterpartyId = Guid.NewGuid() };
                if (slug == A && i == 0) dealOfA = deal.Id;
                s.Store(deal);
            }
            await s.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(A))
        {
            var listing = Guid.NewGuid();
            s.Events.StartStream<Listing>(listing, new ListingCreated(listing, A, "إعلانُ المُطابَقَة", null, 1m, "general", null, null, [], DateTime.UtcNow));
            s.Insert(new Message { Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), SenderId = owner, Body = "رِسالَةُ المُطابَقَة" });
            s.Insert(new Review { Id = Guid.NewGuid(), TenantSlug = A, AuthorId = owner, TargetUserId = Guid.NewGuid(), Rating = 5 });
            s.Store(new User { Id = Guid.NewGuid(), TenantSlug = A });
            s.Store(new ImportedRecord { Id = RegionsSaveService.Table + "/1", Table = RegionsSaveService.Table, SourceId = "1", ImportedAt = DateTime.UtcNow });
            await s.SaveChangesAsync();
        }

        var writer = new UsageLedgerWriter(store, new MemoryCache(new MemoryCacheOptions()), NullLogger<UsageLedgerWriter>.Instance);
        ProviderCall Otp(string kind) => new("otp.sms.send", "twilio", A, $"{kind}-{Guid.NewGuid():N}");
        var accepted = Otp("accepted");
        foreach (var (call, outcome) in new[] { (accepted, ProviderOutcome.Accepted), (Otp("refused"), ProviderOutcome.Refused), (Otp("unknown"), ProviderOutcome.Unknown) })
        {
            await writer.OpenAsync(call, CancellationToken.None);
            await writer.DecideAsync(call, new ProviderAcceptance(outcome));
        }
        var stale = Otp("stale");
        await writer.OpenAsync(stale, CancellationToken.None);
        await ExecAsync(db.Cs, "update meter.outbound_intent set created_at = created_at - interval '2 hours' where id = @id", ("id", stale.Id));
        await writer.OpenAsync(Otp("pending"), CancellationToken.None);

        var taken = await new PresenceSnapshot(store, NullLogger<PresenceSnapshot>.Instance).TakeAsync(PresenceSnapshot.Today(), PresenceDefinitions.Platform);
        output.WriteLine($"[G13] {taken.Line}");

        // ‏المُستَورِد: مَخزَنٌ بِإعدادِ `TargetWriter.InitAsync` (لا مُستَمِع)، ولا يُنشِئُ ولا يُغَيِّرُ مُخَطَّطاً — الجَداوِلُ أَنشَأَها التَطبيق.
        using (var importer = DetachedStore(db.Cs))
        {
            await using (var g = importer.LightweightSession())
            {
                g.Store(new Tenant { Id = C, Name = "مَتجَرٌ مُستَورَد", OwnerUserId = owner });
                await g.SaveChangesAsync();
            }
            await using var s = importer.LightweightSession(C);
            var listing = Guid.NewGuid();
            s.Events.StartStream<Listing>(listing, new ListingCreated(listing, C, "إعلانٌ مُستَورَد", null, 1m, "general", null, null, [], DateTime.UtcNow));
            s.Store(new User { Id = Guid.NewGuid(), TenantSlug = C });
            s.Store(new ImportedRecord { Id = "Reports/1", Table = "Reports", SourceId = "1", ImportedAt = DateTime.UtcNow });
            await s.SaveChangesAsync();
        }
        return (accepted, dealOfA);
    }

    /// <summary>مَخزَنٌ بِلا مُستَمِعِ العَدّ — إعدادُ المُستَورِدِ نَفسُه (<c>TargetWriter.InitAsync</c>)، بِلا إنشاءِ مُخَطَّط.</summary>
    private static DocumentStore DetachedStore(string cs) => DocumentStore.For(o =>
    {
        o.Connection(cs);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);
        o.Schema.For<ImportedRecord>().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.None;
    });

    // ─── الصُفوف ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_clean_ledger_reconciles_per_unit_and_each_injected_defect_turns_the_run_red()
    {
        if (!Enabled) return;
        await using var db = await Db.CreateAsync("mr");
        var store = db.Store;
        output.WriteLine($"[G13] {db.Name}: {(await MeterBoot.EnsureAsync(store)).Line}");
        var (accepted, dealOfA) = await SeedAsync(db);

        // ١ · نَظيف: لِكُلِّ وَحدَةٍ ما قورِن، والصادِرُ بِنَتائِجِه، والحُضورُ يَومُه، والمُستَورَدُ مُستَثنًى بِاسمِه، ولا مُؤَشِّر — خُروج 0.
        var clean = Print("clean", await MeterReconciliation.RunAsync(store));
        Assert.Equal(0, clean.ExitCode);
        Assert.Equal((5, 0, 0, 0), (Unit(clean, "deal.create").Compared, Unit(clean, "deal.create").Missing, Unit(clean, "deal.create").Orphans, Unit(clean, "deal.create").ZeroEntryStores.Count));
        Assert.Equal((2, 1), (Unit(clean, "store.create").Compared, Unit(clean, "store.create").Excluded));
        Assert.Equal((1, 1), (Unit(clean, "listing.create").Compared, Unit(clean, "listing.create").Excluded));
        Assert.Equal((1, 1), (Unit(clean, "member.join").Compared, Unit(clean, "member.join").Excluded));
        foreach (var u in new[] { "studio.account.create", "review.create", "chat.message" }) Assert.Equal(1, Unit(clean, u).Compared);
        Assert.Equal([C], clean.ImporterTenants);
        var otp = Intent(clean, "otp.sms.send");
        Assert.Equal((1, 0, 0, 1, 1, 1, 1), (otp.Accepted, otp.Missing, otp.Orphans, otp.Unrecorded, otp.Pending, otp.Unknown, otp.Refused));
        Assert.Equal((1, 1, 0, 0), (clean.Presence.Days, clean.Presence.Completed, clean.Presence.AbsentDays.Count, clean.Presence.Orphans));
        Assert.True(clean.PresenceRows.Sum(r => r.Entries) > 0);
        Assert.Null(clean.Cursor);
        Assert.Contains(clean.Lines(), l => l == $"{MeterReconciliation.LogTag} exclusions · seed 0 · importer tenants 1 ({C})");

        var script = Script(db.Cs, "clean");
        Assert.Equal(0, script.Code);
        Assert.Contains(script.Lines, l => l.Contains("deal.create · compared 5 · missing 0 · orphans 0 · zero-entry stores 0", StringComparison.Ordinal));
        Assert.Contains(script.Lines, l => l.Contains("otp.sms.send · intents 5 · accepted 1 · missing 0 · orphans 0 · unrecorded 1 · pending 1 · unknown 1 · refused 1", StringComparison.Ordinal));
        Assert.Contains(script.Lines, l => l.Contains($"importer tenants 1 ({C})", StringComparison.Ordinal));

        // ٢ · سَطرٌ مَحذوف ⇐ «missing 1 (deal …)»، خُروج 1 — بِالدالَّةِ وبِالسكربت.
        var dealEntry = UsageEntryId.For("deal.create", A, dealOfA.ToString("D"));
        await using (await RemovedAsync(db.Cs, "meter.usage_entry", "id = @id", ("id", dealEntry)))
        {
            var r = Print("missing", await MeterReconciliation.RunAsync(store));
            Assert.Equal((1, 0, 1), (Unit(r, "deal.create").Missing, Unit(r, "deal.create").Orphans, r.ExitCode));
            Assert.Equal([dealEntry], Unit(r, "deal.create").MissingIds);
            var s = Script(db.Cs, "missing");
            Assert.Equal(1, s.Code);
            Assert.Contains(s.Lines, l => l.Contains($"missing {dealEntry}", StringComparison.Ordinal));
        }
        Assert.Equal(0, Print("restored", await MeterReconciliation.RunAsync(store)).ExitCode);

        // ٣ · سَطرٌ بِلا مَصدَر ⇐ «orphans 1»، خُروج 1.
        var ghost = UsageEntryId.For("deal.create", A, Guid.NewGuid().ToString("D"));
        await ExecAsync(db.Cs, "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation) "
                               + "values (@id, 'deal.create', 1, @app, '_unowned', '_none', 'deal_status', '_none', 'deal', @id, 1)", ("id", ghost), ("app", A));
        var orphan = Print("orphan", await MeterReconciliation.RunAsync(store));
        Assert.Equal((0, 1, 1), (Unit(orphan, "deal.create").Missing, Unit(orphan, "deal.create").Orphans, orphan.ExitCode));
        Assert.Equal([ghost], Unit(orphan, "deal.create").OrphanIds);
        await ExecAsync(db.Cs, "delete from meter.usage_entry where id = @id", ("id", ghost));

        // ٤ · كُلُّ سُطورِ مَتجَرٍ مَحذوفَة ⇐ «missing N · zero-entry stores 1» لِكُلِّ وَحدَةٍ لَه فيها مَصدَر، خُروج 1.
        await using (await RemovedAsync(db.Cs, "meter.usage_entry", "app = @app and unit not like 'presence.%'", ("app", B)))
        {
            var r = Print("store-wiped", await MeterReconciliation.RunAsync(store));
            Assert.Equal((3, 1), (Unit(r, "deal.create").Missing, Unit(r, "deal.create").ZeroEntryStores.Count));
            Assert.Equal([B], Unit(r, "deal.create").ZeroEntryStores);
            Assert.Equal(1, Unit(r, "store.create").Missing);
            Assert.Equal([B], Unit(r, "store.create").ZeroEntryStores);
            Assert.Equal(1, r.ExitCode);
        }

        // ٥ · مَتجَرٌ أُدرِجَ بِمَخزَنٍ بِلا مُستَمِع ⇐ «missing 1 (store.create …)»، خُروج 1.
        using (var detached = DetachedStore(db.Cs))
        await using (var g = detached.LightweightSession())
        {
            g.Insert(new Tenant { Id = D, Name = "مَتجَرٌ بِلا مُستَمِع" });
            await g.SaveChangesAsync();
        }
        var unheard = Print("listener-detached", await MeterReconciliation.RunAsync(store));
        Assert.Equal((1, 1), (Unit(unheard, "store.create").Missing, unheard.ExitCode));
        Assert.Equal([D], Unit(unheard, "store.create").ZeroEntryStores);
        Assert.Equal([UsageEntryId.For("store.create", D, D)], Unit(unheard, "store.create").MissingIds);
        await ExecAsync(db.Cs, "delete from platform.mt_doc_tenant where id = @id", ("id", D));

        // ٦ · سَطرُ نِيَّةٍ مَقبولَةٍ مَحذوف ⇐ «missing 1 (otp.sms.send …)»، خُروج 1.
        await using (await RemovedAsync(db.Cs, "meter.usage_entry", "id = @id", ("id", accepted.Id)))
        {
            var r = Print("outbound-missing", await MeterReconciliation.RunAsync(store));
            Assert.Equal((1, 0, 1), (Intent(r, "otp.sms.send").Missing, Intent(r, "otp.sms.send").Orphans, r.ExitCode));
            Assert.Equal([accepted.Id], Intent(r, "otp.sms.send").MissingIds);
        }

        // ٧ · المُجَمِّعُ المَبنِيّ ⇐ المَجاميعُ تُطابِقُ الدَفتَرَ حَتّى المُؤَشِّر، خُروج 0؛ وصَفُّ agg_daily مَحذوف ⇐ عَدَمُ تَطابُقٍ عَلى (يَومِه، مَتجَرِه، وَحدَتِه)، خُروج 1.
        await AggregateAsync(db.Cs);
        var aggregated = Print("aggregated", await MeterReconciliation.RunAsync(store));
        Assert.NotNull(aggregated.Cursor);
        Assert.True(aggregated.AggregateRows > 0, "المُجَمِّعُ لَم يَكتُب مَجموعاً.");
        Assert.Equal((0, 0), (aggregated.Mismatches.Count, aggregated.ExitCode));
        await using (await RemovedAsync(db.Cs, "meter.agg_daily", "app = @app and unit = 'deal.create'", ("app", B)))
        {
            var r = Print("agg-row-gone", await MeterReconciliation.RunAsync(store));
            var m = Assert.Single(r.Mismatches);
            Assert.Equal((B, "deal.create", 3L, 0L), (m.App, m.Unit, m.LedgerEntries, m.DailyEntries));
            Assert.Equal(1, r.ExitCode);
        }
        Assert.Equal(0, Print("agg-restored", await MeterReconciliation.RunAsync(store)).ExitCode);

        // ‏٧-ب (‏2026-11-14، مُراجَعَةُ إغلاقِ المَرحَلَةِ هـ الثانِيَة · W13-6·E-R2·١ — M6E-CNT-R2-1) · صَفُّ agg_daily مَنسوبٌ إلى غَيرِ صاحِبِه ⇐ عَدَمانِ عَلى مَفاتيحِه السَبعَة،
        // خُروج 1 — لِكُلِّ بُعدٍ لا تَراه (يَوم، مَتجَر، وَحدَة): رائِدُ الأَعمالِ والدَورُ والمِيزَةُ والمُزَوِّد. والعَدُّ والكَمِّيَّةُ كَما هُما، فَالمُقارَنَةُ بِالثَلاثَةِ نَظيفَةٌ عَلَيه.
        Assert.Equal(1, await CountAsync(db.Cs, "select count(*) from meter.agg_daily where app = @app and unit = 'deal.create'", ("app", B)));
        foreach (var column in MeterSql.AggDailyKey.Except(["day", "app", "unit"]))
        {
            await using (await AlteredAsync(db.Cs, "meter.agg_daily", $"{column} = {column} || '-x'", "app = @app and unit = 'deal.create'", ("app", B)))
            {
                var r = Print($"agg-{column}", await MeterReconciliation.RunAsync(store));
                Assert.Equal((2, 1), (r.Mismatches.Count, r.ExitCode));
                var ledgerSide = Assert.Single(r.Mismatches, m => m.DailyEntries == 0);
                var dailySide = Assert.Single(r.Mismatches, m => m.LedgerEntries == 0);
                Assert.Equal((B, "deal.create", 3L, 3L), (ledgerSide.App, ledgerSide.Unit, ledgerSide.LedgerEntries, dailySide.DailyEntries));
                Assert.Equal(Dimension(ledgerSide, column) + "-x", Dimension(dailySide, column));
                Assert.Contains(r.Lines(), l => l.Contains($" · {column} {Dimension(dailySide, column)} · ", StringComparison.Ordinal) && l.EndsWith("ledger 0/0 · agg_daily 3/3", StringComparison.Ordinal));
            }
        }
        Assert.Equal(0, Print("agg-dims-restored", await MeterReconciliation.RunAsync(store)).ExitCode);

        // ٨ · رِسالَةٌ مَبذورَة ⇐ لا عَيب، و«seed 1» يُطبَع.
        using (MeterScope.Seed())
        await using (var s = store.LightweightSession(A))
        {
            s.Insert(new Message { Id = Guid.NewGuid(), ConversationId = Guid.NewGuid(), SenderId = Guid.NewGuid(), Body = "رِسالَةٌ مَبذورَة" });
            await s.SaveChangesAsync();
        }
        var seeded = Print("seeded", await MeterReconciliation.RunAsync(store));
        Assert.Equal((2, 0, 0, 1), (Unit(seeded, "chat.message").Compared, Unit(seeded, "chat.message").Missing, Unit(seeded, "chat.message").Orphans, Unit(seeded, "chat.message").Seed));
        Assert.Equal((0, 1), (seeded.ExitCode, seeded.Seed));
        Assert.Contains(seeded.Lines(), l => l.StartsWith($"{MeterReconciliation.LogTag} exclusions · seed 1 (chat.message 1)", StringComparison.Ordinal));

        // ‏وصَفحَةُ المَتجَر بِالدالَّةِ نَفسِها مَحصورَةً بِه: مَصادِرُه وحدَه، ونِيّاتُه وحدَه.
        var page = Print("store-page", await MeterReconciliation.RunAsync(store, A));
        Assert.Equal((2, 0), (Unit(page, "deal.create").Compared, page.ExitCode));
        Assert.Equal(0, Unit(page, "studio.account.create").Compared);
        Assert.Equal(5, Intent(page, "otp.sms.send").Intents);
        Assert.Empty(page.ImporterTenants);
        Assert.Equal(0, await CountAsync(db.Cs, "select count(*) from pg_tables where schemaname = 'meter' and tablename like 'mr_backup_%'"));
    }

    [Fact]
    public async Task Zero_sources_compared_is_a_blind_run_and_a_database_without_the_ledger_is_not_reconciled_and_not_created()
    {
        if (!Enabled) return;
        await using var db = await Db.CreateAsync("mr");
        var store = db.Store;

        var absent = Print("no-ledger", await MeterReconciliation.RunAsync(store));
        Assert.Equal((MeterReconcileState.NoLedger, 1), (absent.State, absent.ExitCode));
        Assert.Equal(0, await CountAsync(db.Cs, "select count(*) from pg_namespace where nspname = 'meter'"));

        output.WriteLine($"[G13] {db.Name}: {(await MeterBoot.EnsureAsync(store)).Line}");
        var blind = Print("zero-sources", await MeterReconciliation.RunAsync(store));
        Assert.Equal((MeterReconcileState.Data, 0, 0, 1), (blind.State, blind.Compared, blind.Defects, blind.ExitCode));
        Assert.Contains("0 sources compared — a blind run", blind.Lines()[^1], StringComparison.Ordinal);

        var script = Script(db.Cs, "zero-sources");
        Assert.Equal(1, script.Code);
        Assert.Contains(script.Lines, l => l.Contains("result · sources compared 0 · defects 0 → exit 1 (0 sources compared — a blind run)", StringComparison.Ordinal));
    }
}
