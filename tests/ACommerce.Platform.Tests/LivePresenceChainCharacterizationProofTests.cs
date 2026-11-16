using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
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

// ═══ تَوصيفٌ: سِلسِلَةُ لَقطَةِ الحُضورِ في مَسارِها الناجِح — ما لا يَتَغَيَّرُ حينَ يَصيرُ حامِلُ الغَدِ مُستَقِلّاً عَن نَتيجَةِ اليَوم ══════════════════════
// (‏2026-11-13، الموجَة ١٣ W13-6·D-R1·٢ — مُراجَعَةُ المَرحَلَةِ د: M6D-CNT-1؛ ADR-051 §٢٣؛ التَصميم §8 الخُطوَةُ ٤ و«Boot»، القاعِدَتانِ ٣ و١٣).
// **لِماذا هذا المِلَفّ**: الغَدُ اليَومَ يُجَدوِلُه الرابِحُ وَحدَه بَعدَ إيداعِه (`PresenceSnapshot.HandleAsync`)، والرِسالَةُ بِلا سِياسَةِ فَشَل — فَعُطلٌ يُجاوِزُ مُحاوَلاتِ Wolverine
// الفَورِيَّةَ يُميتُ الرِسالَةَ ولا يَبقى لِلغَدِ حامِلٌ حَتّى الإقلاعِ التالي. والتَبديلُ التالي يُجَدوِلُ الغَدَ **قَبلَ** اللَقطَةِ خَلفَ صَفٍّ واحِدٍ لِكُلِّ يَوم، ويُعيدُ الرِسالَةَ
// بِمُهلَة، ويَجعَلُ خُطُواتِ الإقلاعِ مُستَقِلَّة. وهذا المِلَفُّ يُثَبِّتُ قَبلَه **المَسارَ الناجِحَ كَما هُوَ** — ولا يُمَسُّ بَعدَه حَرفاً:
//   (أ) رِسالَةُ اليَومِ عَبرَ الطابورِ المَتين ⇐ مُطالَبَةٌ واحِدَةٌ مُكتَمِلَة، وسُطورُ اليَوم، و**حامِلٌ واحِدٌ لِلغَدِ** «Scheduled» عِندَ بِدايَتِه؛
//   (ب) رِسالَةٌ ثانِيَةٌ لِليَومِ نَفسِه ⇐ تَخسَرُ («claimed=false») ولا تُضيفُ حامِلاً ولا سَطراً ولا مُطالَبَة؛
//   (ج) الإقلاعُ عَلى يَومٍ مُكتَمِل ⇐ «sent=no» فَلا رِسالَةَ لِليَوم، والغَدُ حامِلٌ ثانٍ (كَما يَعُدُّه P12: ‏2 بَعدَ الإقلاعِ الأَوَّل).
// ولا يُثَبِّتُ ما يَتَغَيَّرُ عَمداً: ما يَبقى بَعدَ عُطلٍ في القاعِدَة (صَفّاه في `LivePresenceSnapshotProofTests` مَعَ التَبديل).

[Collection(LiveProofCollection.Name)]
public class LivePresenceChainCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string Store = "pc-amal";
    private static readonly Guid Owner = Guid.Parse("3c9e1a7b-5d2f-4e8a-9b6c-1f0d2e3a4b5c");

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

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

    private static async Task AdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(string cs, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>صُفوفُ صُندوقِ الرِسالَة: الحالَةُ ووَقتُ التَنفيذِ ويَومُ الرِسالَةِ مِن حَقلِها (<c>Day</c>).</summary>
    private static async Task<List<(string Status, DateTimeOffset? At, string Day)>> EnvelopesAsync(string cs)
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
            var day = System.Text.RegularExpressions.Regex.Match(body, @"""day""\s*:\s*""(\d{4}-\d{2}-\d{2})""", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
            rows.Add((r.GetString(0), r.IsDBNull(1) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(1), DateTimeKind.Utc)), day));
        }
        return rows;
    }

    private static string Show(IEnumerable<(string Status, DateTimeOffset? At, string Day)> rows)
        => "[" + string.Join(", ", rows.Select(e => $"{e.Day} {e.Status}{(e.At is { } at ? "@" + at.ToString("yyyy-MM-ddTHH:mmZ", System.Globalization.CultureInfo.InvariantCulture) : "")}")) + "]";

    /// <summary>المُضيفُ بِتَركيبِ القالِبِ وMarten كَما يُرَكِّبُه <c>HostingExtensions</c>، و<see cref="PresenceSnapshot.Configure"/> بِعَينِه كَما يُناديه <c>Program.cs</c>.</summary>
    private static WebApplication BuildApp(string cs, Captured logs)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration[ProviderVault.KekConfigKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.Logging.AddProvider(logs);
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

    private static async Task<bool> UntilAsync(Func<Task<bool>> done, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            if (await done()) return true;
            await Task.Delay(250);
        }
        return await done();
    }

    [Fact]
    public async Task A_day_taken_through_the_durable_queue_carries_tomorrow_once_and_a_duplicate_or_a_boot_add_only_what_they_always_did()
    {
        if (!Enabled) return;
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        b.Database = $"{stem}_pc_{Guid.NewGuid().ToString("N")[..8]}";
        var cs = b.ConnectionString;
        await AdminAsync($"create database \"{b.Database}\"");
        output.WriteLine($"[char·chain] قاعِدَةٌ جَديدَة: {b.Database}");
        var logs = new Captured();
        try
        {
            var app = BuildApp(cs, logs);
            await app.StartAsync();
            try
            {
                var store = app.Services.GetRequiredService<IDocumentStore>();
                output.WriteLine($"[char·chain] {(await MeterBoot.EnsureAsync(store)).Line}");
                await using (var s = store.LightweightSession())
                {
                    s.Insert(new Tenant { Id = Store, Name = "أَمَل", OwnerUserId = Owner, AuthChannel = "phone",
                                          Roles = [RoleCatalog.InstantiateRole(RoleCatalog.Find("tenant_admin")!, 0), RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!, 1)] });
                    await s.SaveChangesAsync();
                }

                var today = PresenceSnapshot.Today();
                var (todayKey, tomorrowKey) = (PresenceReader.DayKey(today), PresenceReader.DayKey(today.AddDays(1)));
                Task<long> Claims() => CountAsync(cs, "select count(*) from meter.presence_claim where day = @d and completed_at is not null", ("d", today));
                Task<long> Rows() => CountAsync(cs, "select count(*) from meter.usage_entry where unit like 'presence.%' and id like '%:' || @k || ':%'", ("k", todayKey));
                async Task<int> Tomorrow() => (await EnvelopesAsync(cs)).Count(e => e.Status == "Scheduled" && e.Day == tomorrowKey);
                async Task<int> TodayScheduled() => (await EnvelopesAsync(cs)).Count(e => e.Status == "Scheduled" && e.Day == todayKey);
                int Lines(string tag) => logs.Lines.Count(l => l.Contains(tag, StringComparison.Ordinal));
                async Task SendTodayAsync()
                {
                    using var scope = app.Services.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new TakePresenceSnapshot(today));
                }

                // ‏(أ) رِسالَةُ اليَومِ عَبرَ الطابورِ المَتين.
                await SendTodayAsync();
                var settledA = await UntilAsync(async () => await Claims() == 1 && await Tomorrow() >= 1, TimeSpan.FromSeconds(90));
                await Task.Delay(TimeSpan.FromSeconds(2));
                var (claimsA, rowsA, tomorrowA, todayA) = (await Claims(), await Rows(), await Tomorrow(), await TodayScheduled());
                var envelopesA = await EnvelopesAsync(cs);
                output.WriteLine($"[char·chain] (أ) اليَوم {todayKey}: سَكَن {settledA} · مُطالَبات {claimsA} · سُطور {rowsA} · حامِلُ الغَد {tomorrowA} · الصُندوق {Show(envelopesA)} · "
                                 + $"«{string.Join(" | ", logs.Lines.Where(l => l.StartsWith("[meter] presence day=", StringComparison.Ordinal)))}»");
                Assert.True(settledA, "رِسالَةُ اليَومِ لَم تُؤخَذ أَو لَم تَحمِل غَدَها.");
                Assert.Equal(1L, claimsA);
                Assert.True(rowsA >= 3, $"أَداةٌ عَمياء: {rowsA} سَطرَ حُضورٍ لِليَوم.");
                Assert.Equal(1, tomorrowA);
                Assert.Equal(0, todayA);
                Assert.All(envelopesA.Where(e => e.Day == tomorrowKey), e => Assert.Equal(PresenceSnapshot.StartOf(today.AddDays(1)), e.At));
                Assert.Equal(1, Lines($"[meter] presence day={todayKey} claimed=true"));

                // ‏(ب) رِسالَةٌ ثانِيَةٌ لِليَومِ نَفسِه: تَخسَرُ ولا تُضيفُ شَيئاً.
                await SendTodayAsync();
                var settledB = await UntilAsync(() => Task.FromResult(Lines($"[meter] presence day={todayKey} claimed=false") == 1), TimeSpan.FromSeconds(60));
                await Task.Delay(TimeSpan.FromSeconds(2));
                var (claimsB, rowsB, tomorrowB, todayB) = (await Claims(), await Rows(), await Tomorrow(), await TodayScheduled());
                output.WriteLine($"[char·chain] (ب) رِسالَةٌ ثانِيَة: سَكَن {settledB} · مُطالَبات {claimsB} · سُطور {rowsB} · حامِلُ الغَد {tomorrowB} · الصُندوق {Show(await EnvelopesAsync(cs))}");
                Assert.True(settledB, "الرِسالَةُ الثانِيَةُ لَم تُعالَج.");
                Assert.Equal((1L, rowsA, 1, 0), (claimsB, rowsB, tomorrowB, todayB));

                // ‏(ج) الإقلاعُ عَلى يَومٍ مُكتَمِل: لا رِسالَةَ لِليَوم، والغَدُ حامِلٌ ثانٍ.
                var todayEnvelopes = (await EnvelopesAsync(cs)).Count(e => e.Day == todayKey);
                await PresenceSnapshot.ScheduleAtBootAsync(app.Services);
                var settledC = await UntilAsync(async () => await Tomorrow() == 2, TimeSpan.FromSeconds(30));
                await Task.Delay(TimeSpan.FromSeconds(2));
                var boot = logs.Lines.Where(l => l.StartsWith("[meter] presence-boot", StringComparison.Ordinal)).ToList();
                var (claimsC, tomorrowC, todayC) = (await Claims(), await Tomorrow(), (await EnvelopesAsync(cs)).Count(e => e.Day == todayKey));
                output.WriteLine($"[char·chain] (ج) الإقلاع: «{string.Join(" | ", boot)}» · مُطالَبات {claimsC} · حامِلُ الغَد {tomorrowC} · صُفوفُ اليَوم {todayEnvelopes} ← {todayC} · الصُندوق {Show(await EnvelopesAsync(cs))}");
                Assert.True(settledC, "الإقلاعُ لَم يُجَدوِل الغَد.");
                Assert.Contains(boot, l => l.Contains($"today={todayKey} sent=no", StringComparison.Ordinal));
                Assert.Equal((1L, 2, todayEnvelopes), (claimsC, tomorrowC, todayC));
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
            await AdminAsync($"drop database if exists \"{b.Database}\" with (force)");
            output.WriteLine($"[char·chain] حُذِفَت {b.Database}");
        }
    }
}
