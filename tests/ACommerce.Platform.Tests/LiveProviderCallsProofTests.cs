using System.Net;
using System.Text;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Brevo;
using ACommerce.Kit.Auth.Providers.Nafath;
using ACommerce.Kit.Auth.Providers.Smtp;
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
//
// ‏(‏2026-11-12، W13-6·١٢ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وبابُ البَريدِ بِقَناتَيه**: `POST /{slug}/auth/email/login` في مَتجَرٍ قَناتُه البَريدُ خَلفَ الوَسيطِ الحَقيقِيّ،
// مَرَّةً بِـBrevo (`AddBrevoEmailChannel` عَلى مُعالِجٍ يُسَجِّل) ومَرَّةً بِـSMTP (`AddSmtpEmailChannel` عَلى خادِمٍ مُزَيَّفٍ في 5200–5249) ⇐ سَطرُ `otp.email.send`
// واحِدٌ بِالمَتجَرِ ومالِكِه ومُعَرِّفُ طَلَبِه مُعَرِّفُ المُحاوَلَة، والمُزَوِّدُ اسمُ القَناة؛ ورَفضُ Brevo ⇐ `send_failed` ونِيَّةٌ مَرفوضَةٌ وصِفرُ سَطر.
//
// ‏(‏2026-11-12، W13-6·١٣ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وصورَةُ المَلَفِّ والإشعارُ الفَوريُّ وبابُ نَفاذ**: `POST /{slug}/me/save` بِصورَةٍ إلى مَخزَنِ S3 المُسَجَّلِ
// عَلى مُستَمِعٍ مُزَيَّف ⇐ سَطرُ `file.upload` واحِدٌ كَمِّيَّتُه البايتاتُ ومَصدَرُه المِفتاحُ مُستَعاراً؛ وبابُ قَبولِ الصَفقَة (`DealNotices`) لِمُشتَرٍ بِاشتِراكَين ⇐ سَطرُ
// `push.send` لِلمَقبولِ بِمُعَرِّفِ الإشعارِ المَكتوبِ وعُنوانِه مُستَعاراً، ونِيَّةٌ مَرفوضَةٌ بِلا سَطرٍ لِلمُنتَهي؛ و`POST /{slug}/auth/nafath/login` ثُمَّ `/verify` ⇐ سَطرُ
// `identity.nafath.start` واحِدٌ ونِيَّةٌ واحِدَة — والرَمزانِ والاستِطلاعُ مُساعِدَةٌ لا نِيَّةَ لَها.
//
// ‏(‏2026-11-12، W13-6·١٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ومُعالِجُ انتِهاءِ الفاتورَة**: `GatewayInvoiceExpiryHandler.Handle` مِن حاوِيَةِ المُضيفِ عَلى فاتورَةٍ انتَهَت —
// يَفتَحُ حامِلَه (`due.TenantSlug`، `_system`) فَتُعَدُّ جَلبَةُ المُهلَةِ وجَلبَتا الإغلاقِ والإلغاءُ عِندَ البَوّابَةِ بِالمَتجَرِ ومالِكِه والدَورِ `_system`؛ والرِسالَةُ المُعادَةُ
// (‏1/5/15 ث) تَجلِبُ ثانِيَةً بِالمُعَرِّفِ نَفسِه فَلا سَطرَ ثانٍ؛ والحامِلُ لا يَتَسَرَّبُ إلى المُنادي.

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

    /// <summary>‏(‏2026-11-12، W13-6·١٣) سُطورُ وَحدَةٍ في مَتجَرٍ بِمُعَرِّفاتِها — لِلوَحَداتِ الَّتي مُعَرِّفُ طَلَبِها جَديدٌ لا يَعرِفُه الاختِبارُ قَبلَ النِداء.</summary>
    private static async Task<List<(string Id, EntryRow Row)>> EntriesOfAppAsync(string app, string unit)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select id from meter.usage_entry where app = @a and unit = @u order by id", conn);
        cmd.Parameters.AddWithValue("a", app);
        cmd.Parameters.AddWithValue("u", unit);
        var ids = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) ids.Add(r.GetString(0));
        var rows = new List<(string, EntryRow)>();
        foreach (var id in ids) rows.Add((id, Assert.Single(await EntriesAsync(id))));
        return rows;
    }

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

    /// <summary>
    /// <para><b>بابُ رَمزِ البَريدِ عَبرَ HTTP بِقَناتَيه</b> (‏2026-11-12، W13-6·١٢): مَتجَرٌ قَناتُه البَريد، والوَسيطُ الحَقيقِيُّ يَفتَحُ الحامِلَ ويَملَؤُه. Brevo يَقبَل ⇐
    /// التَحويلُ إلى التَحَقُّق، والنِيَّةُ مَقبولَة، وسَطرُ <c>otp.email.send</c> واحِدٌ بِالمَتجَرِ ومالِكِه والمِيزَةِ <c>email_otp</c> ومُعَرِّفُ طَلَبِه مُعَرِّفُ المُحاوَلَة؛
    /// وBrevo يَرفُضُ (400) ⇐ <c>send_failed</c> ونِيَّةٌ مَرفوضَةٌ وصِفرُ سَطر. ثُمَّ SMTP عَلى خادِمٍ مُزَيَّف ⇐ السَطرُ نَفسُه والمُزَوِّدُ <c>smtp</c> والرِسالَةُ سُلِّمَت.</para>
    /// </summary>
    [Fact]
    public async Task The_email_code_door_counts_one_accepted_send_on_brevo_and_on_smtp()
    {
        if (!Enabled) return;
        var status = HttpStatusCode.Created;
        var seen = new List<string>();
        var brevo = new Answer((req, _) =>
        {
            lock (seen) seen.Add($"{req.Method} {req.RequestUri} {string.Join(",", req.Headers.Select(h => h.Key))}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        });
        string Email() => $"w13o-{Guid.NewGuid():N}"[..18] + "@wasayel.test";
        async Task<(string Slug, string Owner)> StoreAsync(LiveTemplateDoorHost host)
        {
            var slug = NewSlug();
            var owner = Guid.NewGuid();
            await using var s = host.Store.LightweightSession();
            s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ البَريد", OwnerUserId = owner, AuthChannel = AuthChannels.Email });
            await s.SaveChangesAsync();
            return (slug, owner.ToString("D"));
        }

        await using (var host = LiveTemplateDoorHost.Build(services: s =>
               {
                   s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => brevo));
                   s.AddBrevoEmailChannel(o => { o.ApiKey = "live-proof"; o.From = "no-reply@wasayel.test"; });
               }, throughResolver: true))
        {
            var (slug, owner) = await StoreAsync(host);
            var route = new Dictionary<string, string> { ["slug"] = slug };
            var (code, location) = await host.PostFormAsync("/{slug}/auth/email/login", route, slug, null, ("email", Email()));
            var attempt = Uri.UnescapeDataString(location.Split("attempt=")[1].Split('&')[0]);
            var id = $"{OutboundUnits.OtpEmailSend}:{ProviderNames.Brevo}:{attempt}";
            var rows = await EntriesAsync(id);
            output.WriteLine($"[P12] Brevo ⇒ ({code}, {location}) · النِيَّة {await OutcomeAsync(id)} · {string.Join(" | ", rows)} · المُعالِج {string.Join(", ", seen)}");
            Assert.Equal(302, code);
            Assert.Contains("stage=verify", location, StringComparison.Ordinal);
            Assert.Equal("accepted", await OutcomeAsync(id));
            var row = Assert.Single(rows);
            Assert.Equal((OutboundUnits.OtpEmailSend, slug, owner, "email_otp", ProviderNames.Brevo, UsageLedgerWriter.RequestSource, attempt, 1L, true),
                         (row.Unit, row.App, row.Entrepreneur, row.Feature, row.Provider, row.SourceType, row.SourceId, row.Quantity, row.SameTransactionAsIntent));
            Assert.Equal(new[] { "POST https://api.brevo.com/v3/smtp/email api-key" }, seen);

            status = HttpStatusCode.BadRequest;
            var (refusedCode, refusedLocation) = await host.PostFormAsync("/{slug}/auth/email/login", route, slug, null, ("email", Email()));
            var intents = await WithinAsync(() => IntentsOfAppAsync(slug), l => l.Count == 2 && l.All(i => i.Outcome != "pending"), TimeSpan.FromSeconds(10));
            var refused = intents.Where(i => i.Outcome == "refused").ToList();
            output.WriteLine($"[P12] Brevo 400 ⇒ ({refusedCode}, {refusedLocation}) · نِيّاتُ المَتجَر {string.Join(", ", intents.Select(i => i.Outcome))}");
            Assert.Equal(302, refusedCode);
            Assert.Contains("err=send_failed", refusedLocation, StringComparison.Ordinal);
            Assert.Equal(new[] { "accepted", "refused" }, intents.Select(i => i.Outcome).Order(StringComparer.Ordinal));
            Assert.Empty(await EntriesAsync(Assert.Single(refused).Id));
        }

        await using var smtp = new EmailRequestShapeCharacterizationTests.FakeSmtp();
        await using (var host = LiveTemplateDoorHost.Build(services: s =>
                         s.AddSmtpEmailChannel(o => { o.Host = "127.0.0.1"; o.Port = smtp.Port; o.From = "no-reply@wasayel.test"; }), throughResolver: true))
        {
            var (slug, owner) = await StoreAsync(host);
            var (code, location) = await host.PostFormAsync("/{slug}/auth/email/login", new Dictionary<string, string> { ["slug"] = slug }, slug, null, ("email", Email()));
            var attempt = Uri.UnescapeDataString(location.Split("attempt=")[1].Split('&')[0]);
            var id = $"{OutboundUnits.OtpEmailSend}:{ProviderNames.Smtp}:{attempt}";
            var rows = await EntriesAsync(id);
            output.WriteLine($"[P12] SMTP ⇒ ({code}, {location}) · النِيَّة {await OutcomeAsync(id)} · {string.Join(" | ", rows)} · رَسائِلُ الخادِم {smtp.Messages.Count}");
            Assert.Equal(302, code);
            Assert.Contains("stage=verify", location, StringComparison.Ordinal);
            Assert.Equal("accepted", await OutcomeAsync(id));
            var row = Assert.Single(rows);
            Assert.Equal((OutboundUnits.OtpEmailSend, slug, owner, "email_otp", ProviderNames.Smtp, attempt, true),
                         (row.Unit, row.App, row.Entrepreneur, row.Feature, row.Provider, row.SourceId, row.SameTransactionAsIntent));
            Assert.Single(smtp.Messages);
        }
    }

    // ─── ‏(‏2026-11-12، W13-6·١٣) صورَةُ المَلَفّ، والإشعارُ الفَوريّ، وبابُ نَفاذ ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>صورَةُ المَلَفِّ عَبرَ البابِ الحَقيقِيّ</b>: مَخزَنُ S3 بِتَسجيلِه (<c>AddS3FileStorage</c>) عَلى مُستَمِعٍ مُزَيَّف، والوَسيطُ الحَقيقِيُّ يَفتَحُ الحامِل. الرَفعُ يُقبَل ⇐
    /// سَطرُ <c>file.upload</c> واحِدٌ بِالمَتجَرِ ومالِكِه ودَورِ <c>_none</c> (مَتجَرٌ بِلا أَدوار) والمِيزَةِ <c>files</c> والمُزَوِّدِ <c>s3</c>، <b>كَمِّيَّتُه البايتاتُ
    /// المَرفوعَة</b> ومَصدَرُه <c>file_key</c> بِاسمِ المِفتاحِ المُستَعار — ولا يَحمِلُ المِفتاحَ ولا مُعَرِّفَ صاحِبِه — ونِيَّتُه مَقبولَةٌ في مُعامَلَتِه.</para>
    /// </summary>
    [Fact]
    public async Task The_profile_door_counts_one_upload_with_its_bytes_under_a_pseudonym_of_its_key()
    {
        if (!Enabled) return;
        await using var s3 = new NafathS3PushRequestShapeCharacterizationTests.FakeS3();
        await using var host = LiveTemplateDoorHost.Build(services: s => NafathS3PushRequestShapeCharacterizationTests.AddS3(s, s3), throughResolver: true);
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الرَفع", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var member = new User
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوُ الرَفع", Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            PhoneVerified = true, AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
        };
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(member); await s.SaveChangesAsync(); }
        var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion);
        var png = new byte[1531];
        png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;

        var (status, location) = await host.PostMultipartAsync("/{slug}/me/save", new Dictionary<string, string> { ["slug"] = slug }, slug, token,
            [("fullName", "اسمٌ بَعدَ الرَفع")], ("avatar", "me.png", "image/png", png));
        var key = $"tenants/{slug}/avatars/{member.Id}.png";
        var rows = await EntriesOfAppAsync(slug, OutboundUnits.FileUpload);
        var intents = await IntentsOfAppAsync(slug);
        output.WriteLine($"[P13·رَفع] POST /{slug}/me/save ⇒ ({status}, {location}) · نِيّاتُ المَتجَر {string.Join(", ", intents.Select(i => i.Outcome))} · "
                         + $"{string.Join(" | ", rows.Select(r => $"{r.Id} · {r.Row}"))} · طَلَباتُ المَخزَن {s3.Requests.Count}");
        Assert.Equal((302, $"/{slug}/me"), (status, location));
        var (id, row) = Assert.Single(rows);
        Assert.Equal((OutboundUnits.FileUpload, slug, owner.ToString("D"), MeterSentinels.None, "files", ProviderNames.S3, ProviderCall.FileKeySource,
                      UsageEntryId.Pseudonym(OutboundUnits.FileUpload, slug, key), 1531L, true),
                     (row.Unit, row.App, row.Entrepreneur, row.Role, row.Feature, row.Provider, row.SourceType, row.SourceId, row.Quantity, row.SameTransactionAsIntent));
        Assert.StartsWith($"{OutboundUnits.FileUpload}:{ProviderNames.S3}:", id, StringComparison.Ordinal);
        foreach (var field in new[] { id, row.SourceId })
        {
            Assert.DoesNotContain(member.Id.ToString("D"), field, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("avatars", field, StringComparison.Ordinal);
        }
        Assert.Equal(("accepted", id), (Assert.Single(intents).Outcome, intents[0].Id));
        Assert.Single(s3.Requests);
    }

    /// <summary>مُعالِجُ خِدمَةِ الإشعار: 201 لِعُنوانٍ و410 لِآخَر — يُسَجِّلُ ما طُرِق.</summary>
    private sealed class PushAnswers(string accepted) : HttpMessageHandler
    {
        public List<string> Seen { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Seen) Seen.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(request.RequestUri!.ToString() == accepted ? HttpStatusCode.Created : HttpStatusCode.Gone));
        }
    }

    /// <summary>
    /// <para><b>الإشعارُ الفَوريُّ عَبرَ بابِ قَبولِ الصَفقَة</b> (<c>DealNotices.NotifyAcceptedAsync</c> مِن حاوِيَةِ المُضيف، وخِدمَتُه المُسَجَّلَةُ بِكاتِبِ القالِب): مُشتَرٍ بِاشتِراكَين،
    /// الأَوَّلُ تَقبَلُه الخِدمَةُ والثاني مُنتَهٍ (410). ⇐ سَطرُ <c>push.send</c> واحِدٌ مُعَرِّفُ طَلَبِه <c>{مُعَرِّفُ الإشعارِ المَكتوب}:{اسمُ العُنوانِ المُستَعار}</c> بِالمَتجَرِ
    /// ومالِكِه مِن وَثيقَتِه (لا حامِلَ خارِجَ الطَلَب) والدَورِ <c>_unresolved</c>؛ ونِيَّةٌ مَرفوضَةٌ بِلا سَطرٍ لِلمُنتَهي، والمُنتَهي يُمحى مِن وَثيقَةِ المُشتَري كَما كان.</para>
    /// </summary>
    [Fact]
    public async Task The_accepted_notice_counts_one_push_per_accepted_subscription_under_its_notification_id()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build(throughResolver: true);
        using var a = new NafathS3PushRequestShapeCharacterizationTests.Subscriber();
        using var b = new NafathS3PushRequestShapeCharacterizationTests.Subscriber();
        var answers = new PushAnswers(a.Endpoint);
        NafathS3PushRequestShapeCharacterizationTests.Wire(host.Services.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>(), answers);
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الإشعار", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        await using (var s = host.Store.LightweightSession(slug))
        {
            s.Store(new User { Id = buyer, TenantSlug = slug, FullName = "مُشتَرٍ", PushSubscriptions = [a.Row(), b.Row()] });
            await s.SaveChangesAsync();
        }
        var deal = new ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal
        {
            Id = Guid.NewGuid(), TenantSlug = slug, Pattern = "marketplace", ListingTitle = "سِلعَةُ الإشعار", InitiatorId = buyer, InitiatorName = "مُشتَرٍ",
            CounterpartyId = Guid.NewGuid(), CounterpartyName = "بائِع",
        };
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealNotices>()
                .NotifyAcceptedAsync(slug, deal, "بائِع", new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor()));

        Guid notice;
        await using (var q = host.Store.QuerySession(slug))
            notice = Assert.Single(await q.Query<ACommerce.Kit.Notifications.Notification>().Where(n => n.UserId == buyer).ToListAsync()).Id;
        var accepted = $"{notice:N}:{UsageEntryId.Pseudonym(OutboundUnits.PushSend, slug, a.Endpoint)}";
        var refused = $"{notice:N}:{UsageEntryId.Pseudonym(OutboundUnits.PushSend, slug, b.Endpoint)}";
        var rows = await EntriesOfAppAsync(slug, OutboundUnits.PushSend);
        var intents = await WithinAsync(() => IntentsOfAppAsync(slug), l => l.Count == 2 && l.All(i => i.Outcome != "pending"), TimeSpan.FromSeconds(10));
        User? after;
        await using (var q = host.Store.QuerySession(slug)) after = await q.LoadAsync<User>(buyer);
        output.WriteLine($"[P13·إشعار] ⇒ طُرِق {answers.Seen.Count} · نِيّاتُ المَتجَر {string.Join(", ", intents.Select(i => $"{i.Id}={i.Outcome}"))} · "
                         + $"{string.Join(" | ", rows.Select(r => $"{r.Id} · {r.Row}"))} · اشتِراكاتٌ باقِيَة {after!.PushSubscriptions.Count}");
        Assert.Equal(new[] { a.Endpoint, b.Endpoint }, answers.Seen);
        var (id, row) = Assert.Single(rows);
        Assert.Equal($"{OutboundUnits.PushSend}:{ProviderNames.WebPush}:{accepted}", id);
        Assert.Equal((OutboundUnits.PushSend, slug, owner.ToString("D"), MeterSentinels.Unresolved, "notifications", ProviderNames.WebPush, UsageLedgerWriter.RequestSource,
                      accepted, 1L, true),
                     (row.Unit, row.App, row.Entrepreneur, row.Role, row.Feature, row.Provider, row.SourceType, row.SourceId, row.Quantity, row.SameTransactionAsIntent));
        Assert.Equal(new[] { ($"{OutboundUnits.PushSend}:{ProviderNames.WebPush}:{accepted}", "accepted"), ($"{OutboundUnits.PushSend}:{ProviderNames.WebPush}:{refused}", "refused") }
                         .OrderBy(i => i.Item1, StringComparer.Ordinal),
                     intents.OrderBy(i => i.Id, StringComparer.Ordinal));
        Assert.Equal(new[] { a.Endpoint }, after.PushSubscriptions.Select(p => p.Endpoint));
    }

    /// <summary>
    /// <para><b>بابُ نَفاذ عَبرَ HTTP</b>: <c>AddNafathChannel</c> عَلى مُعالِجٍ يُجيب، ومَتجَرٌ قَناتُه نَفاذ، والوَسيطُ الحَقيقِيّ. البَدءُ (<c>/auth/nafath/login</c>) ثُمَّ التَأكيدُ
    /// (<c>/auth/nafath/verify</c>) — أَربَعَةُ طَلَباتٍ تَبلُغُ نَفاذاً (رَمزٌ وبَدءٌ ثُمَّ رَمزٌ واستِطلاع) ⇐ <b>نِيَّةٌ واحِدَةٌ وسَطرُ <c>identity.nafath.start</c> واحِدٌ</b>
    /// بِالمَتجَرِ ومالِكِه والمِيزَةِ <c>nafath</c> والمُزَوِّدِ <c>nafath</c>: الرَمزانِ والاستِطلاعُ مُساعِدَةٌ لا تُعَدّ.</para>
    /// </summary>
    [Fact]
    public async Task The_nafath_door_counts_the_start_once_and_neither_the_tokens_nor_the_poll()
    {
        if (!Enabled) return;
        var trans = "trans-" + Guid.NewGuid().ToString("N");
        var seen = new List<string>();
        var nafath = new Answer((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            lock (seen) seen.Add($"{req.Method} {path}");
            var json = path switch
            {
                "/oauth/token" => "{\"accessToken\":\"live-token\",\"expiresIn\":3600}",
                "/api/v1/mfa/request" => $"{{\"transId\":\"{trans}\",\"random\":7}}",
                _ => "{\"status\":\"COMPLETED\"}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        });
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.ConfigureHttpClientDefaults(c => c.ConfigurePrimaryHttpMessageHandler(() => nafath));
            s.AddNafathChannel(o => { o.ClientId = "live-client"; o.ClientSecret = "live-secret"; });
        }, throughResolver: true);
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ نَفاذ", OwnerUserId = owner, AuthChannel = AuthChannels.Nafath });
            await s.SaveChangesAsync();
        }
        var route = new Dictionary<string, string> { ["slug"] = slug };
        var nid = "1" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var (startStatus, startLocation) = await host.PostFormAsync("/{slug}/auth/nafath/login", route, slug, null, ("nid", nid));
        var (verifyStatus, verifyLocation) = await host.PostFormAsync("/{slug}/auth/nafath/verify", route, slug, null, ("nid", nid), ("attempt", trans));
        var rows = await EntriesOfAppAsync(slug, OutboundUnits.IdentityNafathStart);
        var intents = await IntentsOfAppAsync(slug);
        output.WriteLine($"[P13·نَفاذ] البَدء ({startStatus}, {startLocation}) · التَأكيد ({verifyStatus}, {verifyLocation}) · نَفاذ رَأى {string.Join(", ", seen)} · "
                         + $"نِيّاتُ المَتجَر {string.Join(", ", intents.Select(i => $"{i.Id}={i.Outcome}"))} · {string.Join(" | ", rows.Select(r => $"{r.Id} · {r.Row}"))}");
        Assert.Equal(302, startStatus);
        Assert.Contains($"stage=verify&attempt={trans}&code=07", startLocation, StringComparison.Ordinal);
        Assert.Equal(302, verifyStatus);
        Assert.DoesNotContain("err=", verifyLocation, StringComparison.Ordinal);
        Assert.Equal(new[] { "POST /oauth/token", "POST /api/v1/mfa/request", "POST /oauth/token", "GET /api/v1/mfa/status" }, seen);
        var (id, row) = Assert.Single(rows);
        Assert.Equal(("accepted", id), (Assert.Single(intents).Outcome, intents[0].Id));
        Assert.Equal((OutboundUnits.IdentityNafathStart, slug, owner.ToString("D"), "nafath", ProviderNames.Nafath, UsageLedgerWriter.RequestSource, 1L, true),
                     (row.Unit, row.App, row.Entrepreneur, row.Feature, row.Provider, row.SourceType, row.Quantity, row.SameTransactionAsIntent));
        Assert.Equal($"{OutboundUnits.IdentityNafathStart}:{ProviderNames.Nafath}:{row.SourceId}", id);
    }

    /// <summary>
    /// <para><b>مُعالِجُ انتِهاءِ الفاتورَةِ يُعَدُّ لِمَتجَرِ رِسالَتِه بِدَورِ <c>_system</c></b> (‏W13-6·١٤؛ التَصميم §5.4 و§6.2): صَفقَتانِ عَلى فاتورَةٍ تَقولُ البَوّابَةُ إنَّها
    /// «expired» ثُمَّ «initiated» لِلمُلغي — المُعالِجُ مِن حاوِيَةِ المُضيفِ (كَما يَبنيه Wolverine) يُلغي الطَلَبَ كُلَّه ويُغلِقُ الفاتورَة. ⇐ أَربَعَةُ سُطور: جَلبُ المُهلَة
    /// (<c>{invoice}:expiry</c>)، وجَلبُ الإغلاقِ لِكُلِّ صَفقَة (<c>{invoice}:close:{deal}</c>)، والإلغاء (<c>{invoice}:close</c>) — كُلُّها بِالمَتجَرِ ومالِكِه (مِن وَثيقَتِه، فَالحامِلُ
    /// بِلا رائِدِ أَعمال) والدَورِ <c>_system</c>. ثُمَّ الرِسالَةُ مُعادَةً ⇐ جَلبَةٌ ثانِيَةٌ بِالمُعَرِّفِ نَفسِه ولا سَطرَ جَديد. والحامِلُ لا يَتَسَرَّبُ إلى المُنادي.</para>
    /// </summary>
    [Fact]
    public async Task The_expiry_handler_counts_its_fetches_and_its_cancel_for_the_message_store_as_the_system()
    {
        if (!Enabled) return;
        var moyasar = new MoyasarScriptedHandler();
        var vault = TenantGatewayEndpointsTests.NewVault();
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.AddSingleton(vault);
            s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => moyasar));
        });
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        // فاتورَةٌ بِمُعَرِّفِها هِيَ: مُعَرِّفُ الطَلَبِ بِلا المَتجَرِ (التَصميم §6.2، COR-4)، ومُعَرِّفاتُ البَوّابَةِ uuid لا تَتَكَرَّرُ بَينَ المَتاجِر — فَلا يُشارِكُ صَفّاً آخَرَ عَلى القاعِدَة.
        var invoice = Guid.NewGuid().ToString("D");
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ المُهلَة", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var binding = TenantGatewayEndpointsTests.PaymentsBinding(vault, sealedFor: slug);
        binding.TenantSlug = slug;
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }
        var ids = new List<Guid>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deals = scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>();
            foreach (var amount in new[] { 100m, 50m })
            {
                var born = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "سِلعَة", amount,
                    ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments.MarkedAtBirth(new()
                    {
                        [ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.AttributeKey] =
                            ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.Card,
                    }, viaGateway: true));
                await deals.AttachRefAsync(slug, born.Id, ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.InvoiceRef, invoice);
                ids.Add(born.Id);
            }
        }
        var listed = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.MetadataFor(ids.ToArray());
        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("expired", 15000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 15000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 15000, metadata: listed, id: invoice));
        var due = new ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue(slug, ids[0]);
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        async Task HandleAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            await ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryHandler.Handle(due,
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments>(),
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealNotices>(), l, CancellationToken.None);
        }

        Assert.Null(MeterScope.Current);
        await HandleAsync();
        Assert.Null(MeterScope.Current);
        var first = (await EntriesOfAppAsync(slug, OutboundUnits.PaymentGatewayInvoiceFetch)).Concat(await EntriesOfAppAsync(slug, OutboundUnits.PaymentGatewayInvoiceCancel)).ToList();
        await HandleAsync();
        var fetches = (await EntriesOfAppAsync(slug, OutboundUnits.PaymentGatewayInvoiceFetch)).Concat(await EntriesOfAppAsync(slug, OutboundUnits.PaymentGatewayInvoiceCancel)).ToList();
        var intents = await IntentsOfAppAsync(slug);
        await AllCancelledAsync(ids);
        output.WriteLine($"[G6·المُهلَة] طَلَباتُ البَوّابَة {moyasar.Requests.Count}: {string.Join(" → ", moyasar.Requests.Select(r => $"{r.Method} {r.RequestUri!.AbsolutePath}"))}");
        output.WriteLine($"[G6·المُهلَة] نِيّات {intents.Count}: {string.Join(", ", intents.Select(i => $"{i.Id}={i.Outcome}"))}");
        foreach (var (id, row) in fetches) output.WriteLine($"[G6·المُهلَة] {id} · {row}");

        string Fetch(string tail) => $"{OutboundUnits.PaymentGatewayInvoiceFetch}:{ProviderNames.StoreGateway}:{invoice}:{tail}";
        var cancel = $"{OutboundUnits.PaymentGatewayInvoiceCancel}:{ProviderNames.StoreGateway}:{invoice}:close";
        Assert.Equal(new[] { "GET", "GET", "GET", "PUT", "GET" }, moyasar.Requests.Select(r => r.Method.Method));
        Assert.Equal(new[] { Fetch($"close:{ids[0]:N}"), Fetch($"close:{ids[1]:N}"), Fetch("expiry"), cancel }.Order(StringComparer.Ordinal),
                     fetches.Select(f => f.Id).Order(StringComparer.Ordinal));
        Assert.Equal(first.Select(f => f.Id).Order(StringComparer.Ordinal), fetches.Select(f => f.Id).Order(StringComparer.Ordinal));
        Assert.All(fetches, f => Assert.Equal((slug, owner.ToString("D"), MeterSentinels.SystemJob, "payments", ProviderNames.StoreGateway, ProviderCall.InvoiceSource,
                                               invoice, 1L, true),
                                              (f.Row.App, f.Row.Entrepreneur, f.Row.Role, f.Row.Feature, f.Row.Provider, f.Row.SourceType, f.Row.SourceId, f.Row.Quantity,
                                               f.Row.SameTransactionAsIntent)));
        Assert.Equal(4, intents.Count);
        Assert.All(intents, i => Assert.Equal("accepted", i.Outcome));

        async Task AllCancelledAsync(List<Guid> dealIds)
        {
            await using var q = host.Store.QuerySession(slug);
            foreach (var id in dealIds)
                Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealStatus.Cancelled,
                             (await q.LoadAsync<ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal>(id))!.Status);
        }
    }

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·١٥ — ADR-051 §١٩؛ التَصميم §6.2) <b>OTO والنَموذَجُ عَلى كاتِبِ المُضيفِ المُرَكَّب</b>: إنشاءُ طَلَبِ شَحنٍ يَرُدُّه OTO ‏401 بِرَمزٍ قَديمٍ ثُمَّ
    /// يَقبَلُه بَعدَ رَمزٍ جَديد — عَمَلِيَّةٌ واحِدَة: سَطرٌ واحِدٌ و<b>النِيَّةُ مَقبولَة</b> (المَرفوضُ لا يَبقى بِجِوارِ سَطرِه — <c>MeterSql.IntentAccept</c>)، بِمَتجَرِه
    /// ومالِكِه ومِيزَتِه <c>delivery</c> ومَصدَرِه الطَلَبِ نَفسِه؛ والرَمزانِ بِلا نِيَّة. ونِداءُ نَموذَجٍ بِـ‏400 يَحمِلُ استِهلاكاً ⇐ سَطرٌ لِلمَنَصَّةِ بِمِيزَتِه
    /// <c>studio.build</c>، و‏401 بِلا استِهلاكٍ ⇐ نِيَّةٌ مَرفوضَةٌ بِلا سَطر.</para>
    /// </summary>
    [Fact]
    public async Task An_OTO_create_refused_then_accepted_is_one_entry_with_an_accepted_intent_and_a_model_call_counts_its_usage()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الشَحن", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var writer = new UsageLedgerWriter(host.Store, host.Services.GetRequiredService<IMemoryCache>(), new CapturedLog());
        var calls = new ProviderCalls(writer);
        var order = Guid.NewGuid().ToString("N");
        var replies = new Queue<HttpStatusCode>([HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.OK, HttpStatusCode.OK]);
        var oto = new Answer((req, _) =>
        {
            var status = replies.Dequeue();
            var body = req.RequestUri!.AbsolutePath == ACommerce.Kit.Delivery.OtoClient.RefreshTokenPath
                ? "{\"access_token\":\"A1\",\"success\":true,\"expires_in\":\"3600\"}" : "{\"success\":true,\"otoId\":540789}";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        });
        var client = new ACommerce.Kit.Delivery.OtoClient(new ProviderHttp<ACommerce.Kit.Delivery.OtoClient>(new HttpClient(oto), calls),
            Microsoft.Extensions.Options.Options.Create(new ACommerce.Kit.Delivery.OtoOptions { Environment = ACommerce.Kit.Delivery.OtoEnvironment.Staging }),
            new ACommerce.Kit.Delivery.OtoTokenCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ACommerce.Kit.Delivery.OtoClient>.Instance);
        var create = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Create(slug, order);

        var created = await client.CreateOrderAsync(create, "RT-1", new ACommerce.Kit.Delivery.OtoOrderDraft(order, ACommerce.Kit.Delivery.OtoClient.CashOnDelivery, 0m, 0m, "",
            new ACommerce.Kit.Delivery.OtoCustomer("w13", "0500000000", "w13", "Riyadh", "SA"), "w13"));

        var id = $"{OutboundUnits.DeliveryOrderCreate}:{ProviderNames.Oto}:{order}";
        var rows = await EntriesAsync(id);
        output.WriteLine($"[G6·OTO] ‏401 ثُمَّ ‏200 ⇒ {created.Ok} · النِيَّة {await OutcomeAsync(id)} · {string.Join(" | ", rows)} · نِيّاتُ المَتجَر {(await IntentsOfAppAsync(slug)).Count}");
        Assert.True(created.Ok);
        Assert.Equal("accepted", await OutcomeAsync(id));
        Assert.Equal(new EntryRow(OutboundUnits.DeliveryOrderCreate, slug, owner.ToString("D"), MeterSentinels.Unresolved, "delivery", ProviderNames.Oto,
                                  ProviderCall.DeliveryOrderSource, order, 1, true), Assert.Single(rows));
        Assert.Equal(new[] { (id, "accepted") }, await IntentsOfAppAsync(slug));

        // ‏(‏2026-11-13، M6C-CNT-1 — ADR-051 §١٩) وإنشاءٌ يَرُدُّه OTO بِـ‏200 و"success": false (رَصيدٌ نَفِدَ، OTO1006) ⇒ العُدَّةُ «oto_refused»، والنِيَّةُ مَرفوضَةٌ وصِفرُ سَطر.
        var broke = Guid.NewGuid().ToString("N");
        var noCredit = new Answer((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath == ACommerce.Kit.Delivery.OtoClient.RefreshTokenPath
                ? "{\"access_token\":\"A2\",\"success\":true,\"expires_in\":\"3600\"}"
                : "{\"success\":false,\"otoErrorCode\":\"OTO1006\",\"otoErrorMessage\":\"Credit is not enough\"}"),
        }));
        var refusedClient = new ACommerce.Kit.Delivery.OtoClient(new ProviderHttp<ACommerce.Kit.Delivery.OtoClient>(new HttpClient(noCredit), calls),
            Microsoft.Extensions.Options.Options.Create(new ACommerce.Kit.Delivery.OtoOptions { Environment = ACommerce.Kit.Delivery.OtoEnvironment.Staging }),
            new ACommerce.Kit.Delivery.OtoTokenCache(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ACommerce.Kit.Delivery.OtoClient>.Instance);
        var refusedCreate = await refusedClient.CreateOrderAsync(ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Create(slug, broke), "RT-2",
            new ACommerce.Kit.Delivery.OtoOrderDraft(broke, ACommerce.Kit.Delivery.OtoClient.CashOnDelivery, 0m, 0m, "",
                new ACommerce.Kit.Delivery.OtoCustomer("w13", "0500000000", "w13", "Riyadh", "SA"), "w13"));
        var brokeId = $"{OutboundUnits.DeliveryOrderCreate}:{ProviderNames.Oto}:{broke}";
        output.WriteLine($"[G6·OTO] ‏200 بِـsuccess:false ⇒ العُدَّة {refusedCreate.Failure?.Code}/{refusedCreate.Failure?.OtoErrorCode} · النِيَّة {await OutcomeAsync(brokeId)} · سُطور {(await EntriesAsync(brokeId)).Count}");
        Assert.Equal((ACommerce.Kit.Delivery.OtoFailures.Refused, "OTO1006"), (refusedCreate.Failure?.Code, refusedCreate.Failure?.OtoErrorCode));
        Assert.Equal(("refused", 0), (await OutcomeAsync(brokeId), (await EntriesAsync(brokeId)).Count));

        // النَموذَج: ‏400 يَحمِلُ استِهلاكاً ⇒ مَقبول، و‏401 بِلا استِهلاكٍ ⇒ مَرفوضٌ بِلا سَطر.
        async Task<string> ModelAsync(HttpStatusCode status, string body)
        {
            var backend = new ACommerce.Templates.Customer.Marketplace.Services.AnthropicBackend(
                new ProviderHttp<ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend>(new HttpClient(new Answer((_, _) =>
                    Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }))), calls),
                new ACommerce.Templates.Customer.Marketplace.Services.AgentProfile("Studio", "anthropic", null, "k", null, null));
            var call = ACommerce.Templates.Customer.Marketplace.Services.ModelCalls.Platform();
            await backend.CallAsync(call, new ACommerce.Templates.Customer.Marketplace.Services.AgentRequest("w13", [], [], "w13-model", MaxTokens: 16), CancellationToken.None);
            return call.By(ProviderNames.Anthropic).Id;   // الخَلفِيَّةُ تَسِمُ النِداءَ بِاسمِها
        }
        var spent = await ModelAsync(HttpStatusCode.BadRequest, "{\"type\":\"error\",\"usage\":{\"input_tokens\":7,\"output_tokens\":0}}");
        var refused = await ModelAsync(HttpStatusCode.Unauthorized, "{\"type\":\"error\"}");
        var model = await EntriesAsync(spent);
        output.WriteLine($"[G6·النَموذَج] ‏400 بِاستِهلاك ⇒ {await OutcomeAsync(spent)} · {string.Join(" | ", model)} · ‏401 بِلا استِهلاك ⇒ {await OutcomeAsync(refused)}");
        Assert.Equal(("accepted", "refused", 0), (await OutcomeAsync(spent), await OutcomeAsync(refused), (await EntriesAsync(refused)).Count));
        Assert.Equal((MeterSentinels.Platform, MeterSentinels.Platform, "studio.build", ProviderNames.Anthropic, 1L, true),
                     (Assert.Single(model).App, model[0].Entrepreneur, model[0].Feature, model[0].Provider, model[0].Quantity, model[0].SameTransactionAsIntent));
    }

    /// <summary>
    /// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-SEC-R2-1 — ADR-051 §١٩) <b>رِسالَةُ PayPal المُزَوَّرَةُ عَلى كاتِبِ المُضيفِ المُرَكَّب</b>: الرُؤوسُ
    /// كَما يَبنيها البابُ (<c>PayPalSurface.HeadersFrom</c> ثُمَّ <c>VerifyCall</c>)، والعُدَّةُ الحَقيقِيَّةُ عَلى PayPal يُجيبُ «FAILURE» بِـ‏200. مُعَرِّفُ مُستَخدِمٍ
    /// في رَأسِ الإرسال ⇐ سَطرٌ مَقبولٌ ونِيَّةٌ مَقبولَة، وحُكمُ G17 عَلى الجَدوَلَينِ صِفر؛ ورَأسٌ طَولُه ‏8192 (فَوقَ حَدِّ فِهرِسِ btree لَو كُتِبَ كَما هُوَ) ⇐ سَطرٌ
    /// مَقبولٌ كَذلِك بِلا سَطرِ <c>outbound-intent-failed</c> ولا <c>outbound-unrecorded</c>.</para>
    /// </summary>
    [Fact]
    public async Task A_forged_paypal_webhook_counts_its_verify_call_without_planting_the_header_and_whatever_its_length()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var user = Guid.NewGuid();   // مُعَرِّفُ مُستَخدِمٍ كَما يَظهَرُ في عُنوانِ صورَتِه العامّ (`tenants/{slug}/avatars/{userId}.{ext}`)
        var log = new CapturedLog();
        var calls = new ProviderCalls(new UsageLedgerWriter(host.Store, host.Services.GetRequiredService<IMemoryCache>(), log));
        var opts = new ACommerce.Kit.Payments.Providers.PayPal.PayPalOptions { ClientId = "c", ClientSecret = "s", Environment = "live", WebhookId = "WH-1" };
        var huge = string.Concat(Enumerable.Range(0, 256).Select(_ => Guid.NewGuid().ToString("N")));
        var rows = new List<LedgerPersonScan.Row>();
        foreach (var header in new[] { user.ToString("D"), huge })
        {
            var paypal = new Answer((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(req.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)
                    ? "{\"access_token\":\"A21AA\",\"expires_in\":32400}" : "{\"verification_status\":\"FAILURE\"}"),
            }));
            var provider = new ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider(Microsoft.Extensions.Options.Options.Create(opts),
                new ProviderHttp<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>(new HttpClient(paypal), calls),
                new ACommerce.Kit.Payments.Providers.PayPal.PayPalTokenCache(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>.Instance);
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionIdHeader] = header;
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionTimeHeader] = "2026-11-13T00:00:00Z";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.CertUrlHeader] = "https://api.paypal.com/cert.pem";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.AuthAlgoHeader] = "SHA256withRSA";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionSigHeader] = "forged==";
            var headers = ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface.HeadersFrom(http.Request);
            var call = ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface.VerifyCall(headers);

            var verified = await new ACommerce.Kit.Payments.Providers.PayPal.PayPalGateway(opts, provider).VerifyWebhookSignatureAsync(call, headers, "{\"id\":\"WH-1\"}");
            var id = call.By(ProviderNames.PayPal).Id;
            var entry = await EntriesAsync(id);
            output.WriteLine($"[G17·PayPal] رَأسٌ بِطولِ {header.Length} ⇒ مُتَحَقَّق {verified} · طَلَباتُ PayPal {paypal.Sent} · النِيَّة {await OutcomeAsync(id)} · سُطور {entry.Count} · {id}");
            Assert.False(verified);
            Assert.Equal(("accepted", 1), (await OutcomeAsync(id), entry.Count));
            Assert.Equal((MeterSentinels.Platform, ProviderNames.PayPal, UsageLedgerWriter.RequestSource, true),
                         (entry[0].App, entry[0].Provider, entry[0].SourceType, entry[0].SameTransactionAsIntent));
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand("select 'usage_entry', id, 'source_id', source_id from meter.usage_entry where id = @id "
                                                     + "union all select 'outbound_intent', id, 'request_id', request_id from meter.outbound_intent where id = @id", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            }
        }

        var leaks = LedgerPersonScan.Leaks(new[] { user.ToString("D") }, rows);
        var failed = log.Lines.Count(l => l.StartsWith("[meter] outbound-intent-failed", StringComparison.Ordinal) || l.StartsWith("[meter] outbound-unrecorded", StringComparison.Ordinal));
        output.WriteLine($"[G17·PayPal] صُفوفٌ مَفحوصَة {rows.Count} · تَسَرُّب {leaks.Count} ({string.Join(", ", leaks)}) · سُطورُ عُطلِ الكاتِب {failed}");
        Assert.Equal(4, rows.Count);
        Assert.Empty(leaks);
        Assert.Equal(0, failed);
    }

    /// <summary>
    /// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-CNT-R2-1 — ADR-051 §١٩) <b>تَشغيلُ المُهلَةِ بَعدَ الاستِئنافِ يُعَدُّ جَلبُه</b>: نِزاعٌ ثُمَّ رِسالَةُ المُهلَةِ
    /// المُجَدوَلَة (جَلبٌ، ولا إلغاء) ثُمَّ استِئنافٌ بَعدَ النافِذَة ثُمَّ رِسالَتُه (جَلبٌ ثانٍ فَإلغاء) ⇐ سَطرا جَلبٍ لِغَرَضِ المُهلَة: <c>{invoice}:expiry</c>
    /// و<c>{invoice}:expiry:resume:{deal:N}:1</c>، كِلاهُما لِلمَتجَرِ ومالِكِه بِدَورِ <c>_system</c>؛ وإعادَةُ الرِسالَةِ نَفسِها ⇐ جَلبَةٌ أُخرى ولا سَطرَ ثالِث.
    /// كانَ الثاني يَحمِلُ مُعَرِّفَ الأَوَّلِ فَيَبتَلِعُه <c>ON CONFLICT (id) DO NOTHING</c> (مَقيسٌ عِندَ المُراجِع: «GET sent 3 · fetch entries 2 · expiry-purpose entries 1»).
    /// وما يَبلُغُ البَوّابَةَ ويَصيرُ إلَيه الطَلَبُ يُثَبِّتُه <c>LiveExpiryResumeRunCharacterizationProofTests</c>.</para>
    /// </summary>
    [Fact]
    public async Task A_resume_after_the_window_counts_its_expiry_fetch_as_a_second_run_and_a_retry_of_it_as_none()
    {
        if (!Enabled) return;
        var moyasar = new MoyasarScriptedHandler();
        var vault = TenantGatewayEndpointsTests.NewVault();
        var bus = new RecordingMessageBus();
        await using var host = LiveTemplateDoorHost.Build(services: s =>
        {
            s.AddSingleton(vault);
            s.AddSingleton<Wolverine.IMessageBus>(bus);
            s.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => moyasar));
        });
        var slug = NewSlug();
        var owner = Guid.NewGuid();
        var invoice = Guid.NewGuid().ToString("D");
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الاستِئناف", OwnerUserId = owner }); await s.SaveChangesAsync(); }
        var binding = TenantGatewayEndpointsTests.PaymentsBinding(vault, sealedFor: slug);
        binding.TenantSlug = slug;
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(binding); await s.SaveChangesAsync(); }
        Guid deal;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deals = scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>();
            deal = (await deals.StartAsync(slug, "marketplace", Guid.NewGuid(), "مُشتَرٍ", null, "سِلعَة", 100m,
                ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments.MarkedAtBirth(new()
                {
                    [ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.AttributeKey] =
                        ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutPayMethods.Card,
                }, viaGateway: true))).Id;
            await deals.AttachRefAsync(slug, deal, ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.InvoiceRef, invoice);
            await deals.AttachRefAsync(slug, deal, ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments.InvoiceExpiresAtRef,
                ACommerce.Kit.Payments.MoyasarClient.ExpiryText(DateTimeOffset.UtcNow.AddMinutes(-5)));
            Assert.True((await deals.DisputeAsync(slug, deal, owner, "صاحِبُ المَتجَر", "نِزاع")).Ok);
        }
        var listed = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPaymentProof.MetadataFor(new[] { deal });
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        async Task HandleAsync(ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue due)
        {
            await using var scope = host.Services.CreateAsyncScope();
            await ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryHandler.Handle(due,
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayPayments>(),
                scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealNotices>(), l, CancellationToken.None);
        }
        string Fetch(string tail) => $"{OutboundUnits.PaymentGatewayInvoiceFetch}:{ProviderNames.StoreGateway}:{invoice}:{tail}";
        async Task<List<(string Id, EntryRow Row)>> ExpiryFetchesAsync()
            => (await EntriesOfAppAsync(slug, OutboundUnits.PaymentGatewayInvoiceFetch)).Where(f => f.Id.StartsWith(Fetch("expiry"), StringComparison.Ordinal)).ToList();

        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice));
        await HandleAsync(new(slug, deal));
        var afterFirst = await ExpiryFetchesAsync();
        await using (var scope = host.Services.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<ACommerce.Templates.Customer.Marketplace.Services.Deals.DealsService>()
                .ResolveDisputeAsync(slug, deal, new ACommerce.Templates.Customer.Marketplace.Services.Deals.DealCanceller(owner, "صاحِبُ المَتجَر", true),
                    ACommerce.Templates.Customer.Marketplace.Services.Deals.DealDisputeResolution.Resume, "استِئناف")).Ok);
        var due = (ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayInvoiceExpiryDue)Assert.Single(bus.Sent).Message;
        moyasar.Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated", 10000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 10000, metadata: listed, id: invoice))
               .Then(HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled", 10000, metadata: listed, id: invoice));
        await HandleAsync(due);
        var afterResume = await ExpiryFetchesAsync();
        await HandleAsync(due);
        var afterRetry = await ExpiryFetchesAsync();
        var gets = moyasar.Requests.Count(r => r.Method == HttpMethod.Get);
        output.WriteLine($"[G6·المُهلَة بَعدَ الاستِئناف] GET {gets} · سُطورُ غَرَضِ المُهلَة: بَعدَ الأَوَّل {afterFirst.Count} · بَعدَ تَشغيلِ الاستِئناف {afterResume.Count} · بَعدَ إعادَتِه {afterRetry.Count}");
        foreach (var (id, row) in afterRetry) output.WriteLine($"[G6·المُهلَة بَعدَ الاستِئناف] {id} · {row}");

        Assert.Equal(4, gets);
        Assert.Equal(new[] { Fetch("expiry") }, afterFirst.Select(f => f.Id));
        Assert.Equal(new[] { Fetch("expiry"), Fetch($"expiry:resume:{deal:N}:1") }.Order(StringComparer.Ordinal), afterResume.Select(f => f.Id).Order(StringComparer.Ordinal));
        Assert.Equal(afterResume.Select(f => f.Id).Order(StringComparer.Ordinal), afterRetry.Select(f => f.Id).Order(StringComparer.Ordinal));
        Assert.All(afterRetry, f => Assert.Equal((slug, owner.ToString("D"), MeterSentinels.SystemJob, ProviderCall.InvoiceSource, invoice, true),
                                                 (f.Row.App, f.Row.Entrepreneur, f.Row.Role, f.Row.SourceType, f.Row.SourceId, f.Row.SameTransactionAsIntent)));
    }
}
