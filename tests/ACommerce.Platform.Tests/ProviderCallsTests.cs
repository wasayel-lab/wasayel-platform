using System.Net;
using ACommerce.Kit.Auth.Providers.Smtp;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G6 — المُستَدعي المَعدود: نِيَّةٌ قَبلَ النِداء، ونَتيجَةٌ مِن ثَلاثٍ بَعدَه، والنِداءُ كَما هُوَ (‏2026-11-12، W13-6·١١ — ADR-051 §١٨؛ التَصميم §6.2) ═══
//
// صُفوفُ الدَلالَةِ بِلا قاعِدَة: كاتِبٌ في الذاكِرَةِ يُسَجِّلُ ما يُنادى بِه وبِأَيِّ تَرتيب، و`ProviderHttp` عَلى مُعالِجٍ يُجيبُ بِلا شَبَكَة. وصُفوفُ القاعِدَةِ
// (السَطرُ والنِيَّةُ في مُعامَلَةٍ واحِدَة، والمَجهولُ ثُمَّ المَقبولُ، والطَلَبُ نَفسُه تَحتَ حامِلَين، وبابُ الرَمزِ عَبرَ HTTP) في `LiveProviderCallsProofTests`.

public class ProviderCallsTests(ITestOutputHelper output)
{
    /// <summary>كاتِبٌ في الذاكِرَة: ما نودِيَ بِه بِتَرتيبِه — ويَرمي إن طُلِب.</summary>
    internal sealed class MemoryRecorder : OutboundRecorder
    {
        public List<string> Log { get; } = new();
        public bool ThrowOnOpen { get; init; }
        public bool ThrowOnDecide { get; init; }

        public override Task OpenAsync(ProviderCall call, CancellationToken ct)
        {
            lock (Log) Log.Add($"open {call.Id}");
            return ThrowOnOpen ? Task.FromException(new InvalidOperationException("intent store down")) : Task.CompletedTask;
        }

        public override Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance)
        {
            lock (Log) Log.Add($"decide {call.Id} {acceptance.Outcome} {acceptance.Quantity}");
            if (ThrowOnDecide) throw new InvalidOperationException("ledger down");
            return Task.CompletedTask;
        }
    }

    /// <summary>مُعالِجٌ يُجيبُ بِحالَةٍ أَو يَرمي أَو يَنتَظِرُ حَتّى يُلغى.</summary>
    private sealed class Answer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Sent;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Sent);
            return respond(request, ct);
        }
    }

    private static readonly ProviderCall Call = ProviderCall.ForChannel(OutboundUnits.OtpSmsSend, "w13-calls", "attempt-1").By(ProviderNames.Twilio);

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://provider.invalid/send");

    [Fact]
    public void The_call_id_is_unit_provider_request_without_the_app_and_the_channel_stamps_the_provider()
    {
        var call = ProviderCall.ForChannel(OutboundUnits.OtpSmsSend, "store-a", "r1");
        Assert.Equal(MeterSentinels.None, call.Provider);
        Assert.Equal("otp.sms.send:twilio:r1", call.By(ProviderNames.Twilio).Id);
        Assert.Equal(call.By(ProviderNames.Twilio).Id, (call with { App = "store-b" }).By(ProviderNames.Twilio).Id);
        Assert.Equal(32, ProviderCall.FreshRequestId().Length);
        Assert.NotEqual(ProviderCall.FreshRequestId(), ProviderCall.FreshRequestId());
    }

    [Theory]
    [InlineData(200, ProviderOutcome.Accepted)]
    [InlineData(201, ProviderOutcome.Accepted)]
    [InlineData(204, ProviderOutcome.Accepted)]
    [InlineData(302, ProviderOutcome.Unknown)]
    [InlineData(400, ProviderOutcome.Refused)]
    [InlineData(404, ProviderOutcome.Refused)]
    [InlineData(429, ProviderOutcome.Refused)]
    [InlineData(500, ProviderOutcome.Unknown)]
    [InlineData(503, ProviderOutcome.Unknown)]
    public void The_outcome_is_read_from_the_status(int status, ProviderOutcome expected)
        => Assert.Equal(expected, ProviderHttp<ProviderCallsTests>.Classify(new HttpResponseMessage((HttpStatusCode)status)).Outcome);

    /// <summary><b>قُبِل</b>: النِيَّةُ قَبلَ الإرسال، والنَتيجَةُ بَعدَه بِكَمِّيَّةٍ واحِدَة، والجَوابُ يَعودُ كَما هُوَ. و<b>رُفِض</b> و<b>مَجهول</b>: الجَوابُ يَعودُ ولا يُرمى —
    /// فَتَحويلُ العُدَّةِ لِأَخطائِها (<c>Twilio فَشِل: …</c>) لا يَتَغَيَّر.</summary>
    [Theory]
    [InlineData(201, "Accepted")]
    [InlineData(400, "Refused")]
    [InlineData(503, "Unknown")]
    public async Task The_intent_opens_before_the_send_and_the_outcome_follows_the_answer(int status, string outcome)
    {
        var recorder = new MemoryRecorder();
        var handler = new Answer((_, _) =>
        {
            lock (recorder.Log) recorder.Log.Add("send");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        });
        var http = new ProviderHttp<ProviderCallsTests>(new HttpClient(handler), new ProviderCalls(recorder));
        using var response = await http.SendAsync(Call, Request(), CancellationToken.None);
        output.WriteLine($"[G6] {status} ⇒ {string.Join(" → ", recorder.Log)}");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.True(http.IsRecording);
        Assert.Equal(new[] { $"open {Call.Id}", "send", $"decide {Call.Id} {outcome} 1" }, recorder.Log);
    }

    /// <summary><b>استِثناءٌ بَعدَ الإرسالِ أَو إلغاءٌ</b> (ومِنه مُهلَةُ <c>OtpSendGuard</c> بَعدَ أَن قَبِلَ المُزَوِّد): مَجهولٌ، والاستِثناءُ نَفسُه يُعادُ رَميُه.</summary>
    [Fact]
    public async Task An_exception_or_a_cancellation_is_unknown_and_rethrown_unchanged()
    {
        var recorder = new MemoryRecorder();
        var boom = new HttpRequestException("connection reset after dispatch");
        var failing = new ProviderHttp<ProviderCallsTests>(new HttpClient(new Answer((_, _) => Task.FromException<HttpResponseMessage>(boom))),
            new ProviderCalls(recorder));
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => failing.SendAsync(Call, Request(), CancellationToken.None));
        Assert.Same(boom, thrown);

        using var cts = new CancellationTokenSource();
        var accepted = new TaskCompletionSource();
        var late = new ProviderHttp<ProviderCallsTests>(new HttpClient(new Answer(async (_, ct) =>
        {
            accepted.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        })), new ProviderCalls(recorder));
        var pending = late.SendAsync(Call, Request(), cts.Token);
        await accepted.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        output.WriteLine($"[G6] {string.Join(" · ", recorder.Log)}");
        Assert.Equal(new[] { $"open {Call.Id}", $"decide {Call.Id} Unknown 1", $"open {Call.Id}", $"decide {Call.Id} Unknown 1" }, recorder.Log);
    }

    /// <summary><b>العَدُّ لا يُفشِلُ نِداءً</b> (التَصميم §6.2؛ ADR-051 §٤): كاتِبٌ يَرمي عِندَ النِيَّةِ وعِندَ النَتيجَةِ — والإرسالُ يَقَعُ والجَوابُ يَعود.</summary>
    [Fact]
    public async Task An_intent_or_outcome_write_failure_never_fails_the_call()
    {
        var recorder = new MemoryRecorder { ThrowOnOpen = true, ThrowOnDecide = true };
        var handler = new Answer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)));
        var http = new ProviderHttp<ProviderCallsTests>(new HttpClient(handler), new ProviderCalls(recorder));
        using var response = await http.SendAsync(Call, Request(), CancellationToken.None);
        output.WriteLine($"[G6] الكاتِبُ يَرمي مَرَّتَين ⇒ {(int)response.StatusCode} · أُرسِلَ {handler.Sent} · {string.Join(" · ", recorder.Log)}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, handler.Sent);
        Assert.Equal(new[] { $"open {Call.Id}", $"decide {Call.Id} Accepted 1" }, recorder.Log);
    }

    /// <summary><b>بِلا كاتِبٍ لا عَدّ</b>: تَركيبٌ لا يُسَجِّلُ القالِبَ يُرسِلُ ولا يُسَجِّل، ويَقولُ ذلكَ <see cref="ProviderCalls.IsRecording"/> — ما يَقرَؤُه G6d.</summary>
    [Fact]
    public async Task Without_a_recorder_the_call_is_sent_and_the_invoker_says_it_is_not_recording()
    {
        var handler = new Answer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var http = new ProviderHttp<ProviderCallsTests>(new HttpClient(handler), new ProviderCalls(null));
        using var response = await http.SendAsync(Call, Request(), CancellationToken.None);
        Assert.Equal((HttpStatusCode.OK, 1, false), (response.StatusCode, handler.Sent, http.IsRecording));
    }

    /// <summary>تَصنيفُ جَوابٍ يَرمي (عُدَّةُ SDK بِقَبولِها هِيَ) لا يُفشِلُ النِداءَ الَّذي نَجَح: مَجهولٌ ويَعودُ الجَواب.</summary>
    [Fact]
    public async Task A_throwing_acceptance_is_unknown_and_the_result_returns()
    {
        var recorder = new MemoryRecorder();
        var result = await new ProviderCalls(recorder).InvokeAsync(Call, _ => Task.FromResult(7),
            _ => throw new FormatException("unreadable answer"), CancellationToken.None);
        Assert.Equal(7, result);
        Assert.Equal(new[] { $"open {Call.Id}", $"decide {Call.Id} Unknown 1" }, recorder.Log);
    }

    // ─── عُدَّةُ SDK: أَفعالُ MailKit داخِلَ المُستَدعي (‏2026-11-12، W13-6·١٢ — التَصميم §6.2 «SMTP → SendAsync returned») ─────────────────────

    /// <summary>كاتِبٌ يَسأَلُ الخادِمَ المُزَيَّفَ عِندَ النِيَّةِ: كَم اتِّصالاً رَأى قَبلَها — فَـ«النِيَّةُ قَبلَ النِداء» مَقيسَةٌ عَلى السِلكِ لا عَلى تَرتيبِ السُطور.</summary>
    private sealed class WireRecorder(EmailRequestShapeCharacterizationTests.FakeSmtp smtp) : OutboundRecorder
    {
        public List<string> Log { get; } = new();

        public override Task OpenAsync(ProviderCall call, CancellationToken ct)
        {
            lock (Log) Log.Add($"open {call.Id} · اتِّصالات {smtp.Connections} · رَسائِل {smtp.Messages.Count}");
            return Task.CompletedTask;
        }

        public override Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance)
        {
            lock (Log) Log.Add($"decide {call.Id} {acceptance.Outcome} {acceptance.Quantity} · رَسائِل {smtp.Messages.Count}");
            return Task.CompletedTask;
        }
    }

    private static SmtpEmailChannel Smtp(EmailRequestShapeCharacterizationTests.FakeSmtp smtp, OutboundRecorder recorder, int timeoutSeconds = 10)
        => new(Options.Create(new SmtpEmailOptions { Host = "127.0.0.1", Port = smtp.Port, From = "no-reply@wasayel.test", TimeoutSeconds = timeoutSeconds }),
            new ProviderCalls(recorder), NullLogger<SmtpEmailChannel>.Instance);

    private static readonly ProviderCall Mail = ProviderCall.ForChannel(OutboundUnits.OtpEmailSend, "w13-calls", "attempt-mail");

    /// <summary><b>قُبِلَ حينَ عادَ الإرسال</b>: النِيَّةُ قَبلَ أَيِّ اتِّصال، والقَبولُ بَعدَ أَن سَلَّمَ الخادِمُ الرِسالَة، والقَناةُ تَسِمُ النِداءَ <c>smtp</c>.</summary>
    [Fact]
    public async Task Smtp_is_accepted_when_the_send_returned_and_the_intent_precedes_the_connection()
    {
        await using var smtp = new EmailRequestShapeCharacterizationTests.FakeSmtp();
        var recorder = new WireRecorder(smtp);

        await Smtp(smtp, recorder).SendOtpAsync(Mail, "owner@wasayel.test", "483920", CancellationToken.None);

        var id = $"{OutboundUnits.OtpEmailSend}:{ProviderNames.Smtp}:attempt-mail";
        output.WriteLine($"[G6] SMTP ⇒ {string.Join(" → ", recorder.Log)}");
        Assert.Equal(new[] { $"open {id} · اتِّصالات 0 · رَسائِل 0", $"decide {id} Accepted 1 · رَسائِل 1" }, recorder.Log);
    }

    /// <summary><b>رَفضُ المُستَلِمِ وصَمتُ الخادِم</b>: الاستِثناءُ بَعدَ الإرسالِ مَجهولٌ عَلى النِيَّة (لا سَطر)، ويُعادُ رَميُه فَتُحَوِّلُه القَناةُ كَما كانَت.</summary>
    [Fact]
    public async Task Smtp_refusal_and_silence_are_unknown_and_the_channel_still_says_why()
    {
        var id = $"{OutboundUnits.OtpEmailSend}:{ProviderNames.Smtp}:attempt-mail";
        await using (var refusing = new EmailRequestShapeCharacterizationTests.FakeSmtp(rejectRecipient: true))
        {
            var recorder = new WireRecorder(refusing);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Smtp(refusing, recorder).SendOtpAsync(Mail, "owner@wasayel.test", "483920", CancellationToken.None));
            output.WriteLine($"[G6] SMTP 550 ⇒ {ex.Message} · {string.Join(" → ", recorder.Log)}");
            Assert.Equal("SMTP فَشِل الإرسال: 5.1.1 Mailbox unavailable", ex.Message);
            Assert.Equal(new[] { $"open {id} · اتِّصالات 0 · رَسائِل 0", $"decide {id} Unknown 1 · رَسائِل 0" }, recorder.Log);
        }
        await using (var silent = new EmailRequestShapeCharacterizationTests.FakeSmtp(silent: true))
        {
            var recorder = new WireRecorder(silent);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Smtp(silent, recorder, timeoutSeconds: 1).SendOtpAsync(Mail, "owner@wasayel.test", "483920", CancellationToken.None));
            output.WriteLine($"[G6] SMTP صامِت ⇒ {ex.Message} · {string.Join(" → ", recorder.Log)}");
            Assert.Equal("SMTP " + ACommerce.Kit.Auth.OtpSendGuard.TimeoutMessage(TimeSpan.FromSeconds(1)), ex.Message);
            Assert.Equal(new[] { $"open {id} · اتِّصالات 0 · رَسائِل 0", $"decide {id} Unknown 1 · رَسائِل 0" }, recorder.Log);
        }
    }

    // ─── النِداءُ المُساعِد، ونَفاذ، ومَخزَنُ الكائِنات، والإشعار (‏2026-11-12، W13-6·١٣ — التَصميم §3.4، §6.2) ─────────────────────────────────────

    /// <summary>كاتِبٌ يَحفَظُ النِداءَ نَفسَه (مُعَرِّفَه ومَصدَرَه) والنَتيجَة — ويَسأَلُ السِلكَ عِندَ النِيَّة كَم طَلَباً سَبَقَها.</summary>
    internal sealed class CallRecorder(Func<int>? wire = null) : OutboundRecorder
    {
        public List<string> Log { get; } = new();
        public List<(ProviderCall Call, ProviderAcceptance? Acceptance)> Calls { get; } = new();

        public override Task OpenAsync(ProviderCall call, CancellationToken ct)
        {
            lock (Log) { Log.Add($"open {call.Id}{(wire is null ? "" : $" · سِلك {wire()}")}"); Calls.Add((call, null)); }
            return Task.CompletedTask;
        }

        public override Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance)
        {
            lock (Log) { Log.Add($"decide {call.Id} {acceptance.Outcome} {acceptance.Quantity}{(wire is null ? "" : $" · سِلك {wire()}")}"); Calls.Add((call, acceptance)); }
            return Task.CompletedTask;
        }
    }

    /// <summary><b>المُساعِدُ يَمُرُّ ولا يُعَدّ</b>: لا نِيَّةَ ولا نَتيجَة، والجَوابُ والاستِثناءُ كَما هُما؛ والسَبَبُ مِن القائِمَةِ المُغلَقَةِ (تُطبَع) وما خارِجَها يَرمي عِندَ البِناء.</summary>
    [Fact]
    public async Task A_helper_call_is_sent_unrecorded_and_its_reason_comes_from_the_closed_list()
    {
        output.WriteLine($"[G6] أَسبابُ المُساعِد {ProviderHelperReasons.All.Count}: {string.Join(", ", ProviderHelperReasons.All)}");
        Assert.Equal(new[] { "oauth_token", "status_poll", "storage_read", "storage_delete" }, ProviderHelperReasons.All);
        var bad = Assert.Throws<ArgumentException>(() => new ProviderHelper(ProviderNames.Nafath, "metadata_tag"));
        Assert.Contains("metadata_tag", bad.Message, StringComparison.Ordinal);

        var recorder = new MemoryRecorder();
        var calls = new ProviderCalls(recorder);
        var helper = new ProviderHelper(ProviderNames.S3, ProviderHelperReasons.StorageRead);
        Assert.Equal(7, await calls.HelperAsync(helper, _ => Task.FromResult(7), CancellationToken.None));
        var boom = new InvalidOperationException("store down");
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => calls.HelperAsync<int>(helper, _ => throw boom, CancellationToken.None)));
        var handler = new Answer((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using (var response = await new ProviderHttp<ProviderCallsTests>(new HttpClient(handler), calls)
                   .SendHelperAsync(new ProviderHelper(ProviderNames.Nafath, ProviderHelperReasons.OauthToken), Request(), CancellationToken.None))
            Assert.Equal((HttpStatusCode.Forbidden, 1), (response.StatusCode, handler.Sent));
        Assert.Empty(recorder.Log);
    }

    private static readonly ProviderCall NafathStart = ProviderCall.ForChannel(OutboundUnits.IdentityNafathStart, "w13-calls", "nafath-start-1");

    private static ACommerce.Kit.Auth.Providers.Nafath.NafathChannel Nafath(HttpMessageHandler handler, OutboundRecorder recorder)
        => new(Options.Create(new ACommerce.Kit.Auth.Providers.Nafath.NafathOptions { ClientId = "c", ClientSecret = "s" }),
            new ProviderHttp<ACommerce.Kit.Auth.Providers.Nafath.NafathChannel>(new HttpClient(handler), new ProviderCalls(recorder)),
            NullLogger<ACommerce.Kit.Auth.Providers.Nafath.NafathChannel>.Instance);

    /// <summary>
    /// <para><b>نَفاذ</b>: الرَمزُ مُساعِدٌ قَبلَ النِيَّة، ثُمَّ النِيَّةُ فَطَلَبُ التَحَقُّقِ فَالنَتيجَة (والقَناةُ تَسِمُ النِداءَ <c>nafath</c>)؛ والاستِطلاعُ بَعدَه مُساعِدٌ
    /// لا نِيَّةَ لَه؛ وطَلَبٌ مَرفوضٌ (400) ⇐ نَتيجَةٌ مَرفوضَةٌ والقَناةُ تَقولُ «Nafath فَشِل: 400» كَما كانَت.</para>
    /// </summary>
    [Theory]
    [InlineData(200, "Accepted")]
    [InlineData(400, "Refused")]
    public async Task Nafath_counts_the_start_and_leaves_the_token_and_the_poll_uncounted(int status, string outcome)
    {
        var recorder = new MemoryRecorder();
        var trans = "trans-" + Guid.NewGuid().ToString("N");
        var handler = new Answer((req, _) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            lock (recorder.Log) recorder.Log.Add($"send {path}");
            var (code, json) = path switch
            {
                "/oauth/token" => (HttpStatusCode.OK, "{\"accessToken\":\"t\",\"expiresIn\":3600}"),
                "/api/v1/mfa/request" => ((HttpStatusCode)status, $"{{\"transId\":\"{trans}\",\"random\":7}}"),
                _ => (HttpStatusCode.OK, "{\"status\":\"COMPLETED\"}"),
            };
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        });
        var channel = Nafath(handler, recorder);
        var id = $"{OutboundUnits.IdentityNafathStart}:{ProviderNames.Nafath}:nafath-start-1";
        if (status == 200)
        {
            var started = await channel.StartAsync(NafathStart, "1012345678", CancellationToken.None);
            Assert.True(await channel.IsApprovedAsync(started.AttemptId, CancellationToken.None));
        }
        else
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.StartAsync(NafathStart, "1012345678", CancellationToken.None));
            Assert.Equal("Nafath فَشِل: 400", ex.Message);
        }
        output.WriteLine($"[G6] نَفاذ {status} ⇒ {string.Join(" → ", recorder.Log)}");
        var expected = new List<string> { "send /oauth/token", $"open {id}", "send /api/v1/mfa/request", $"decide {id} {outcome} 1" };
        if (status == 200) expected.Add("send /api/v1/mfa/status");
        Assert.Equal(expected, recorder.Log);
    }

    private static ACommerce.Kit.Files.Providers.S3.S3FileStorage S3(NafathS3PushRequestShapeCharacterizationTests.FakeS3 s3, OutboundRecorder recorder)
        => new(Options.Create(new ACommerce.Kit.Files.Providers.S3.S3FileStorageOptions
            {
                Endpoint = s3.Url, Bucket = NafathS3PushRequestShapeCharacterizationTests.Bucket, AccessKeyId = NafathS3PushRequestShapeCharacterizationTests.AccessKey,
                SecretAccessKey = "calls-secret", PublicBaseUrl = NafathS3PushRequestShapeCharacterizationTests.PublicBase,
            }),
            new ProviderCalls(recorder), NullLogger<ACommerce.Kit.Files.Providers.S3.S3FileStorage>.Instance);

    /// <summary>
    /// <para><b>مَخزَنُ الكائِنات</b>: الرَفعُ نِيَّةٌ قَبلَ أَيِّ <c>PUT</c> ثُمَّ مَقبولٌ بِكَمِّيَّةِ البايتاتِ بَعدَه، والنِداءُ مَوسومٌ <c>s3</c> ومَصدَرُه المِفتاحُ
    /// اسماً مُستَعاراً؛ وعُطلُ المَخزَنِ (500) ⇐ نِيَّةٌ واحِدَةٌ لِثَلاثِ مُحاوَلاتِ الـSDK ونَتيجَةٌ مَجهولَة، والبابُ يَرى <c>FileStorageException</c> كَما كان؛
    /// والوُجودُ والقِراءَةُ والحَذفُ لا نِيَّةَ لَها.</para>
    /// </summary>
    [Fact]
    public async Task S3_counts_the_put_with_its_bytes_once_per_upload_and_leaves_reads_and_deletes_uncounted()
    {
        await using var s3 = new NafathS3PushRequestShapeCharacterizationTests.FakeS3();
        var recorder = new CallRecorder(() => s3.Requests.Count);
        using var storage = S3(s3, recorder);
        var key = $"tenants/w13-calls/avatars/{Guid.NewGuid()}.png";
        var bytes = new byte[1531];
        var call = ProviderCall.ForChannel(OutboundUnits.FileUpload, "w13-calls", "upload-1");
        await storage.UploadAsync(call, key, new MemoryStream(bytes), "image/png");
        var id = $"{OutboundUnits.FileUpload}:{ProviderNames.S3}:upload-1";
        output.WriteLine($"[G6] S3 ⇒ {string.Join(" → ", recorder.Log)}");
        Assert.Equal(new[] { $"open {id} · سِلك 0", $"decide {id} Accepted 1531 · سِلك 1" }, recorder.Log);
        var decided = recorder.Calls[^1].Call;
        Assert.Equal((ProviderCall.FileKeySource, UsageEntryId.Pseudonym(OutboundUnits.FileUpload, "w13-calls", key)), (decided.SourceType, decided.SourceId));

        Assert.True(await storage.ExistsAsync(key));
        Assert.NotNull(await storage.ReadAsync(key));
        Assert.True(await storage.DeleteAsync(key));
        Assert.Equal(2, recorder.Log.Count);

        recorder.Log.Clear();
        s3.Requests.Clear();
        s3.Forced["PUT"] = HttpStatusCode.InternalServerError;
        var failed = ProviderCall.ForChannel(OutboundUnits.FileUpload, "w13-calls", "upload-2");
        var ex = await Assert.ThrowsAsync<ACommerce.Kit.Files.FileStorageException>(() => storage.UploadAsync(failed, key, new MemoryStream(bytes), "image/png"));
        var failedId = $"{OutboundUnits.FileUpload}:{ProviderNames.S3}:upload-2";
        output.WriteLine($"[G6] S3 500 ⇒ {ex.GetType().Name} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal(new[] { $"open {failedId} · سِلك 0", $"decide {failedId} Unknown 1 · سِلك 3" }, recorder.Log);
    }

    /// <summary>
    /// <para><b>الإشعار</b>: سَطرٌ لِكُلِّ اشتِراكٍ بِمُعَرِّفِ <c>{notificationId}:{Pseudonym(endpoint)}</c> — نِيَّةٌ قَبلَ الطَلَب، ومَقبولٌ (201) أَو مَرفوضٌ (410، والاشتِراكُ يُمحى
    /// كَما كان) أَو مَجهولٌ (500، ويُكتَبُ سَطرُ التَحذيرِ كَما كان)؛ والإشعارُ نَفسُه لِلاشتِراكِ نَفسِه يُعطي المُعَرِّفَ نَفسَه.</para>
    /// </summary>
    [Theory]
    [InlineData(201, "Accepted")]
    [InlineData(410, "Refused")]
    [InlineData(500, "Unknown")]
    public async Task Push_counts_each_subscription_under_the_notification_id_and_the_endpoint_pseudonym(int status, string outcome)
    {
        using var a = new NafathS3PushRequestShapeCharacterizationTests.Subscriber();
        var push = new NafathS3PushRequestShapeCharacterizationTests.PushRecorder { Status = (HttpStatusCode)status };
        var recorder = new CallRecorder(() => push.Requests.Count);
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        var cfg = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(), new Dictionary<string, string?>
        {
            ["WebPush:PublicKey"] = keys.PublicKey, ["WebPush:PrivateKey"] = keys.PrivateKey,
        }).Build();
        var service = new ACommerce.Templates.Customer.Marketplace.Services.WebPushService(cfg,
            NullLogger<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>.Instance, new ProviderCalls(recorder));
        NafathS3PushRequestShapeCharacterizationTests.Wire(service, push);
        var world = new DocWorld();
        var user = Guid.NewGuid();
        var notice = Guid.NewGuid();
        world.Put(new ACommerce.Kit.Auth.User { Id = user, TenantSlug = "w13-calls", PushSubscriptions = [a.Row()] });
        await service.SendAsync(world.Store, "w13-calls", user, notice, "عُنوان", "مَتن");

        var id = $"{OutboundUnits.PushSend}:{ProviderNames.WebPush}:{notice:N}:{UsageEntryId.Pseudonym(OutboundUnits.PushSend, "w13-calls", a.Endpoint)}";
        output.WriteLine($"[G6] الإشعار {status} ⇒ {string.Join(" → ", recorder.Log)}");
        Assert.Equal(new[] { $"open {id} · سِلك 0", $"decide {id} {outcome} 1 · سِلك 1" }, recorder.Log);
        Assert.Equal(status == 410, world.Stored.OfType<ACommerce.Kit.Auth.User>().Any(u => u.PushSubscriptions.Count == 0));
        Assert.DoesNotContain(a.Endpoint, id, StringComparison.Ordinal);
    }
}
