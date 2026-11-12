using System.Net;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Twilio;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G6 · P11 — الصادِرُ يُعَدُّ عَلى القاعِدَة: نِيَّةٌ قَبلَ النِداء، والمَقبولُ سَطرٌ ونَتيجَةٌ في مُعامَلَةٍ واحِدَة (‏2026-11-12، W13-6·١١ — ADR-051 §١٨) ═══
//
// صُفوفُ الكاتِبِ (`UsageLedgerWriter`، كاتِبُ القالِبِ نَفسُه عَلى مَخزَنِ المُضيفِ المُرَكَّب) بِمُعالِجٍ يُجيبُ بِلا شَبَكَة: المَقبولُ ⇐ سَطرٌ واحِدٌ و`writer_xid`ه
// مُعَرِّفُ مُعامَلَةِ قَبولِ النِيَّة؛ والرَفضُ ⇐ لا سَطرَ ونِيَّةٌ مَرفوضَة؛ والقَبولُ ثُمَّ المُهلَة ⇐ لا سَطرَ ونِيَّةٌ مَجهولَةٌ وسَطرُ لوغٍ واحِد؛ والمَجهولُ ثُمَّ المَقبولُ
// بِالمُعَرِّفِ نَفسِه ⇐ سَطرٌ واحِد؛ والطَلَبُ نَفسُه تَحتَ حامِلَين ⇐ سَطرٌ واحِد؛ وقاعِدَةٌ لا تُجيبُ ⇐ النِداءُ يَمضي. ثُمَّ **P11 عَبرَ البابِ الحَقيقِيّ**:
// `POST /{slug}/auth/phone/login` خَلفَ وَسيطِ المُستَأجِرِ الحَقيقِيِّ وTwilio المُسَجَّلِ بِـ`AddTwilioSmsChannel` عَلى مُعالِجٍ يُسَجِّل — سَطرُ `otp.sms.send` بِالمَتجَرِ
// ورائِدِ أَعمالِه؛ والقَبولُ بَعدَ مُهلَةِ `OtpSendGuard` ⇐ `send_failed` ونِيَّةٌ مَجهولَةٌ وصِفرُ سَطر.

[Collection(LiveProofCollection.Name)]
public class LiveProviderCallsProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string Cs => LiveDealConcurrencyProofTests.ConnectionString!;

    private static string NewSlug() => "w13o-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>لوغُ الكاتِبِ في الذاكِرَة — المُضيفُ يُسَلِّمُ اللوغَ لـSerilog، فَيُعطى الكاتِبُ هُنا مُسَجِّلاً يُقرَأ.</summary>
    private sealed class CapturedLog : ILogger<UsageLedgerWriter>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) lock (Lines) Lines.Add(formatter(state, exception));
        }
    }

    private sealed class Answer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Sent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return respond(request, ct);
        }
    }

    private static Answer Status(HttpStatusCode status) => new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    private sealed record EntryRow(string Unit, string App, string Entrepreneur, string Role, string Feature, string Provider, string SourceType,
                                   string SourceId, long Quantity, bool SameTransactionAsIntent);

    private static async Task<List<EntryRow>> EntriesAsync(string id)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select e.unit, e.app, e.entrepreneur, e.role, e.feature, e.provider, e.source_type, e.source_id, e.quantity, "
            + "coalesce((select e.writer_xid::xid = i.xmin from meter.outbound_intent i where i.id = e.id), false) "
            + "from meter.usage_entry e where e.id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        var rows = new List<EntryRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new EntryRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                                  r.GetString(7), r.GetInt64(8), r.GetBoolean(9)));
        return rows;
    }

    private static async Task<string?> OutcomeAsync(string id)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select outcome from meter.outbound_intent where id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private static async Task<List<(string Id, string Outcome)>> IntentsOfAppAsync(string app)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select id, outcome from meter.outbound_intent where app = @a order by created_at, id", conn);
        cmd.Parameters.AddWithValue("a", app);
        var rows = new List<(string, string)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add((r.GetString(0), r.GetString(1)));
        return rows;
    }

    private static async Task<T> WithinAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        var value = await read();
        while (!done(value) && DateTime.UtcNow < until) { await Task.Delay(100); value = await read(); }
        return value;
    }

    private static ProviderCall Call(string app, string? request = null)
        => ProviderCall.ForChannel(OutboundUnits.OtpSmsSend, app, request ?? ProviderCall.FreshRequestId()).By(ProviderNames.Twilio);

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://provider.invalid/Messages.json");

    /// <summary><b>الكاتِبُ عَلى مَخزَنِ المُضيفِ المُرَكَّب</b>: المَقبولُ والمَرفوضُ والقَبولُ بَعدَ المُهلَة، والمَجهولُ ثُمَّ المَقبولُ، والطَلَبُ نَفسُه تَحتَ حامِلَين،
    /// ومالِكُ المَتجَرِ مِن وَثيقَتِه بِلا حامِل، و<c>_platform</c> رائِدُ أَعمالِ نَفسِه.</summary>
    [Fact]
    public async Task The_writer_counts_accepted_calls_in_one_transaction_and_keeps_refused_and_unknown_on_the_intent()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الصادِر", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var log = new CapturedLog();
        var writer = new UsageLedgerWriter(host.Store, host.Services.GetRequiredService<IMemoryCache>(), log);
        Assert.IsType<UsageLedgerWriter>(host.Services.GetRequiredService<OutboundRecorder>());
        var calls = new ProviderCalls(writer);
        ProviderHttp<LiveProviderCallsProofTests> Http(HttpMessageHandler h) => new(new HttpClient(h), calls);

        // ١ · قُبِل ⇒ سَطرٌ واحِدٌ في مُعامَلَةِ قَبولِ النِيَّة، بِمالِكِ المَتجَرِ مِن وَثيقَتِه (بِلا حامِل) والدَورُ _unresolved.
        var accepted = Call(slug);
        using (var resp = await Http(Status(HttpStatusCode.Created)).SendAsync(accepted, Request(), CancellationToken.None))
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var one = await EntriesAsync(accepted.Id);
        output.WriteLine($"[P11] قُبِل ⇒ {await OutcomeAsync(accepted.Id)} · {string.Join(" | ", one)}");
        Assert.Equal("accepted", await OutcomeAsync(accepted.Id));
        Assert.Equal(new EntryRow(OutboundUnits.OtpSmsSend, slug, owner.ToString("D"), MeterSentinels.Unresolved, "sms_otp", ProviderNames.Twilio,
                                  UsageLedgerWriter.RequestSource, accepted.RequestId, 1, true), Assert.Single(one));

        // ٢ · رُفِض (4xx) ⇒ لا سَطرَ والنِيَّةُ مَرفوضَة، والجَوابُ يَعودُ كَما هُوَ.
        var refused = Call(slug);
        using (var resp = await Http(Status(HttpStatusCode.BadRequest)).SendAsync(refused, Request(), CancellationToken.None))
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(("refused", 0), (await OutcomeAsync(refused.Id), (await EntriesAsync(refused.Id)).Count));

        // ٣ · قَبِلَ المُزَوِّدُ ثُمَّ انقَضَت المُهلَة ⇒ مَجهولٌ وصِفرُ سَطرٍ وسَطرُ لوغٍ واحِد.
        var late = Call(slug);
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Http(new Answer(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new HttpResponseMessage(HttpStatusCode.Created);
            })).SendAsync(late, Request(), cts.Token));
        var unknownLines = log.Lines.Count(l => l.StartsWith($"[meter] outbound-unknown unit={OutboundUnits.OtpSmsSend} app={slug} request={late.RequestId}", StringComparison.Ordinal));
        Assert.Equal(("unknown", 0, 1), (await OutcomeAsync(late.Id), (await EntriesAsync(late.Id)).Count, unknownLines));

        // ٤ · المَجهولُ ثُمَّ المَقبولُ بِالمُعَرِّفِ نَفسِه ⇒ سَطرٌ واحِدٌ والنِيَّةُ مَقبولَة؛ وإعادَةُ المَقبولِ لا تُضاعِف، والمَجهولُ بَعدَه لا يَكتُبُ فَوقَه.
        using (var resp = await Http(Status(HttpStatusCode.Created)).SendAsync(late, Request(), CancellationToken.None)) { }
        using (var resp = await Http(Status(HttpStatusCode.Created)).SendAsync(late, Request(), CancellationToken.None)) { }
        using (var resp = await Http(Status(HttpStatusCode.ServiceUnavailable)).SendAsync(late, Request(), CancellationToken.None)) { }
        Assert.Equal(("accepted", 1), (await OutcomeAsync(late.Id), (await EntriesAsync(late.Id)).Count));

        // ٥ · الطَلَبُ نَفسُه تَحتَ حامِلَينِ (مَتجَرانِ في المِفتاحِ سَواء) ⇒ سَطرٌ واحِدٌ بِالأَوَّل، ورائِدُ أَعمالِه ودَورُه مِن حامِلِه.
        var shared = ProviderCall.FreshRequestId();
        var holderA = MeterScope.Open();
        holderA.Fill(slug, entrepreneur: "holder-entrepreneur", role: "vendor");
        using (var resp = await Http(Status(HttpStatusCode.OK)).SendAsync(Call(slug, shared), Request(), CancellationToken.None)) { }
        var holderB = MeterScope.Open();
        holderB.Fill("w13o-other", entrepreneur: "other-entrepreneur", role: "customer");
        using (var resp = await Http(Status(HttpStatusCode.OK)).SendAsync(Call("w13o-other", shared), Request(), CancellationToken.None)) { }
        var twice = await EntriesAsync(Call(slug, shared).Id);
        output.WriteLine($"[P11] حامِلان ⇒ {string.Join(" | ", twice)}");
        var row = Assert.Single(twice);
        Assert.Equal((slug, "holder-entrepreneur", "vendor"), (row.App, row.Entrepreneur, row.Role));

        // ٦ · المَنَصَّةُ رائِدُ أَعمالِ نَفسِها.
        var platform = Call(MeterSentinels.Platform);
        using (var resp = await Http(Status(HttpStatusCode.Created)).SendAsync(platform, Request(), CancellationToken.None)) { }
        Assert.Equal((MeterSentinels.Platform, MeterSentinels.Platform), (Assert.Single(await EntriesAsync(platform.Id)).App, (await EntriesAsync(platform.Id))[0].Entrepreneur));
        output.WriteLine($"[P11] الكاتِب: سُطورُ لوغِه {log.Lines.Count} ({string.Join(" · ", log.Lines)})");
    }

    /// <summary><b>العَدُّ لا يُفشِلُ نِداءً</b> (G6): كاتِبٌ عَلى قاعِدَةٍ لا وُجودَ لَها — النِيَّةُ تَتَعَذَّرُ والنَتيجَةُ تَتَعَذَّرُ مَرَّتَين، والنِداءُ يَمضي ويَعودُ جَوابُه،
    /// وسَطرا <c>outbound-intent-failed</c> و<c>outbound-unrecorded</c> يُقالانِ بِاسمِهِما.</summary>
    [Fact]
    public async Task An_unreachable_ledger_never_fails_the_call_and_says_so_by_name()
    {
        if (!Enabled) return;
        var b = new NpgsqlConnectionStringBuilder(Cs) { Database = "wasayel_w13_m6_absent_" + Guid.NewGuid().ToString("N")[..8] };
        // المَخزَنُ بِميزَةِ الدَفتَرِ كَما يُسَجِّلُها القالِب — فَالعُطلُ قاعِدَةٌ غائِبَة (3D000) لا ميزَةٌ غَيرُ مُسَجَّلَة.
        await using var absent = DocumentStore.For(o =>
        {
            o.Connection(b.ConnectionString);
            o.DatabaseSchemaName = "platform";
            o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
        });
        var log = new CapturedLog();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = Status(HttpStatusCode.Created);
        var http = new ProviderHttp<LiveProviderCallsProofTests>(new HttpClient(handler), new ProviderCalls(new UsageLedgerWriter(absent, cache, log)));
        var call = Call(NewSlug());
        using var resp = await http.SendAsync(call, Request(), CancellationToken.None);
        output.WriteLine($"[G6] قاعِدَةٌ غائِبَة ⇒ {(int)resp.StatusCode} · أُرسِلَ {handler.Sent} · {string.Join(" · ", log.Lines)}");
        Assert.Equal((HttpStatusCode.Created, 1), (resp.StatusCode, handler.Sent));
        Assert.Contains(log.Lines, l => l.StartsWith($"[meter] outbound-intent-failed unit={call.Unit} app={call.App} request={call.RequestId}: PostgresException", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.StartsWith($"[meter] outbound-unrecorded unit={call.Unit} app={call.App} request={call.RequestId} outcome=Accepted", StringComparison.Ordinal));
    }

    /// <summary><b>P11 — بابُ الرَمزِ الحَقيقِيُّ عَبرَ HTTP</b>: Twilio بِتَسجيلِه (<c>AddTwilioSmsChannel</c>) عَلى مُعالِجٍ يُسَجِّل، والوَسيطُ الحَقيقِيّ يَفتَحُ الحامِلَ
    /// ويَملَؤُه. الطَلَبُ يُقبَل ⇐ التَحويلُ إلى التَحَقُّق، والنِيَّةُ مَقبولَة، وسَطرُ <c>otp.sms.send</c> واحِدٌ بِالمَتجَرِ ومالِكِه ومُعَرِّفُ طَلَبِه مُعَرِّفُ المُحاوَلَة.
    /// ثُمَّ يُقبَلُ الطَلَبُ بَعدَ مُهلَةِ <c>OtpSendGuard</c> ⇐ <c>send_failed</c>، والنِيَّةُ مَجهولَة، وصِفرُ سَطر.</summary>
    [Fact]
    public async Task The_phone_code_door_counts_one_accepted_send_and_leaves_a_late_acceptance_unknown()
    {
        if (!Enabled) return;
        var delay = TimeSpan.Zero;
        var seen = new List<string>();
        var recorder = new Answer(async (req, ct) =>
        {
            lock (seen) seen.Add($"{req.Method} {req.RequestUri!.AbsolutePath} {req.Headers.Authorization?.Scheme}");
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"sid\":\"SM0\"}", Encoding.UTF8, "application/json") };
        });
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
            s.AddTwilioSmsChannel(o => { o.AccountSid = "AC0"; o.AuthToken = "live-proof"; o.FromNumber = "+15005550006"; });
        }, throughResolver: true);
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الرَمز", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var route = new Dictionary<string, string> { ["slug"] = slug };
        string Phone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var (status, location) = await host.PostFormAsync("/{slug}/auth/phone/login", route, slug, null, ("phone", Phone()));
        var attempt = Uri.UnescapeDataString(location.Split("attempt=")[1].Split('&')[0]);
        var id = $"{OutboundUnits.OtpSmsSend}:{ProviderNames.Twilio}:{attempt}";
        var rows = await EntriesAsync(id);
        output.WriteLine($"[P11] الباب ⇒ ({status}, {location}) · النِيَّة {await OutcomeAsync(id)} · {string.Join(" | ", rows)} · المُعالِج {string.Join(", ", seen)}");
        Assert.Equal(302, status);
        Assert.Contains("stage=verify", location, StringComparison.Ordinal);
        Assert.Equal("accepted", await OutcomeAsync(id));
        var row = Assert.Single(rows);
        Assert.Equal((OutboundUnits.OtpSmsSend, slug, owner.ToString("D"), "sms_otp", ProviderNames.Twilio, attempt, true),
                     (row.Unit, row.App, row.Entrepreneur, row.Feature, row.Provider, row.SourceId, row.SameTransactionAsIntent));

        delay = OtpSendGuard.PipelineTimeout + TimeSpan.FromSeconds(1);
        var (lateStatus, lateLocation) = await host.PostFormAsync("/{slug}/auth/phone/login", route, slug, null, ("phone", Phone()));
        var intents = await WithinAsync(() => IntentsOfAppAsync(slug), l => l.Count == 2 && l.All(i => i.Outcome != "pending"), TimeSpan.FromSeconds(10));
        var unknown = intents.Where(i => i.Outcome == "unknown").ToList();
        output.WriteLine($"[P11] القَبولُ بَعدَ المُهلَة ⇒ ({lateStatus}, {lateLocation}) · نِيّاتُ المَتجَر {string.Join(", ", intents.Select(i => i.Outcome))} · طَلَباتُ المُعالِج {seen.Count}");
        Assert.Equal(302, lateStatus);
        Assert.Contains("err=send_failed", lateLocation, StringComparison.Ordinal);
        Assert.Equal(new[] { "accepted", "unknown" }, intents.Select(i => i.Outcome).Order(StringComparer.Ordinal));
        Assert.Empty(await EntriesAsync(Assert.Single(unknown).Id));
        Assert.Equal(2, seen.Count);
        Assert.All(seen, r => Assert.Equal("POST /2010-04-01/Accounts/AC0/Messages.json Basic", r));
    }
}
