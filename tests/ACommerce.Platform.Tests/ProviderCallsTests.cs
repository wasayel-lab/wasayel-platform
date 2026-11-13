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
        // ‏(‏2026-11-12، W13-6·١٤) سَبعٌ: ودَخَلَ وَسمُ الفاتورَةِ ومُنتَجُ الكاتالوجِ وعُضوُ الواجِهَةِ بِلا مُنادٍ مَعَ مُنادِيها — فَالسالِبُ سَبَبٌ لا مُنادِيَ لَه.
        Assert.Equal(new[] { "oauth_token", "status_poll", "storage_read", "storage_delete", "metadata_tag", "catalog_product", "uncalled_member" }, ProviderHelperReasons.All);
        var bad = Assert.Throws<ArgumentException>(() => new ProviderHelper(ProviderNames.Nafath, "invented_reason"));
        Assert.Contains("invented_reason", bad.Message, StringComparison.Ordinal);

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

    // ═══ (‏2026-11-12، W13-6·١٤ — التَصميم §6.2) بَوّابَةُ المَتجَرِ وفَوتَرَةُ الاشتِراك ═══════════════════════════════════════════════════════════

    /// <summary>مُعالِجٌ يُجيبُ بِالرُدودِ المُرَتَّبَةِ ويُسَجِّلُ ما طُلِب.</summary>
    private sealed class Script(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new(replies);
        public List<string> Seen { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Seen) Seen.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.OK, "{}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private const string Store = "w13-calls";

    private static ACommerce.Kit.Payments.MoyasarClient Gateway(HttpMessageHandler handler, OutboundRecorder recorder)
        => new(new ProviderHttp<ACommerce.Kit.Payments.MoyasarClient>(new HttpClient(handler), new ProviderCalls(recorder)),
               NullLogger<ACommerce.Kit.Payments.MoyasarClient>.Instance);

    /// <summary>المُعَرِّفاتُ المَقبولَةُ بِتَرتيبِها — والسُطورُ مِنها مُعَرِّفاتُها المُمَيَّزَة (<c>ON CONFLICT (id) DO NOTHING</c>).</summary>
    private static List<string> Accepted(MemoryRecorder recorder)
        => recorder.Log.Where(l => l.StartsWith("decide ", StringComparison.Ordinal) && l.EndsWith(" Accepted 1", StringComparison.Ordinal))
                       .Select(l => l.Split(' ')[1]).ToList();

    /// <summary>
    /// <para><b>الجَلبُ بِغَرَضِه وزِنادِه</b> (التَصميم §6.2؛ صُفوفُ G6): الفاتورَةُ نَفسُها تُجلَبُ لِلتَحَقُّقِ ولِلإشعارِ ⇐ <b>سَطران</b>؛ والمُهلَةُ تُعادُ ثَلاثاً
    /// (‏1/5/15 ث) ⇐ <b>سَطرٌ واحِد</b>؛ والإغلاقُ بَعدَ إلغاءِ صَفقَتَينِ ⇐ سَطران؛ والإلغاءُ عِندَ البَوّابَةِ <c>{invoiceId}:close</c>. والمَصدَرُ الفاتورَة، والمُزَوِّدُ تَسِمُه العُدَّة.</para>
    /// </summary>
    [Fact]
    public async Task The_same_invoice_fetched_under_verify_and_notice_is_two_entries_and_an_expiry_retried_three_times_is_one()
    {
        const string invoice = MoyasarClientTests.InvoiceId;
        var recorder = new MemoryRecorder();
        var script = new Script(Enumerable.Repeat((HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("initiated")), 7)
                                          .Append((HttpStatusCode.OK, MoyasarClientTests.InvoiceJson("canceled"))).ToArray());
        var client = Gateway(script, recorder);
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var verify = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Fetch(Store, invoice, "verify", ProviderCall.FreshRequestId());
        var notice = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Fetch(Store, invoice, "notice", "pay-1");
        var expiry = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Fetch(Store, invoice, "expiry", trigger: null);
        var close = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Cancel(Store, invoice);

        Assert.True((await client.FetchInvoiceAsync(verify, MoyasarClientTests.Key, invoice)).Ok);
        Assert.True((await client.FetchInvoiceAsync(notice, MoyasarClientTests.Key, invoice)).Ok);
        for (var i = 0; i < 3; i++) Assert.True((await client.FetchInvoiceAsync(expiry, MoyasarClientTests.Key, invoice)).Ok);
        foreach (var deal in new[] { a, b })
            Assert.True((await client.FetchInvoiceAsync(ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Fetch(Store, invoice, "close", deal.ToString("N")),
                                                        MoyasarClientTests.Key, invoice)).Ok);
        Assert.True((await client.CancelInvoiceAsync(close, MoyasarClientTests.Key, invoice)).Ok);

        var accepted = Accepted(recorder);
        var entries = accepted.Distinct(StringComparer.Ordinal).ToList();
        string Fetch(string tail) => $"{OutboundUnits.PaymentGatewayInvoiceFetch}:{ProviderNames.StoreGateway}:{invoice}:{tail}";
        output.WriteLine($"[G6] الجَلب: طَلَبات {script.Seen.Count} · مَقبول {accepted.Count} · سُطور {entries.Count}: {string.Join(" | ", entries)}");
        Assert.Equal(8, accepted.Count);
        Assert.Equal(new[]
        {
            Fetch($"verify:{verify.RequestId.Split(':')[2]}"), Fetch("notice:pay-1"), Fetch("expiry"),
            Fetch($"close:{a:N}"), Fetch($"close:{b:N}"),
            $"{OutboundUnits.PaymentGatewayInvoiceCancel}:{ProviderNames.StoreGateway}:{invoice}:close",
        }, entries);
        Assert.Equal(3, accepted.Count(id => id == Fetch("expiry")));
        Assert.Equal((ProviderCall.InvoiceSource, invoice, ProviderCall.InvoiceSource, invoice), (verify.SourceType, verify.SourceId, close.SourceType, close.SourceId));
    }

    /// <summary>
    /// <para><b>زِنادُ تَشغيلِ المُهلَة</b> (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-CNT-R2-1 — ADR-051 §١٩): صَفقَةٌ لَم تُستَأنَف ⇐ <c>null</c> فَالجَلبُ
    /// <c>{invoice}:expiry</c> كَما كان (والإعاداتُ الثَلاثُ أَعلاه سَطرٌ واحِد)؛ واستِئنافٌ ⇐ <c>resume:{deal:N}:1</c>، وثانٍ ⇐ <c>:2</c>؛ وصَفقَتانِ مُستَأنَفَتانِ عَلى فاتورَةٍ
    /// واحِدَةٍ ⇐ زِنادان — وسَطرُ «نِزاع» وَحدَه لا يُغَيِّرُه.</para>
    /// </summary>
    [Fact]
    public void Each_expiry_run_after_a_resume_has_its_own_trigger_and_a_run_without_one_keeps_the_old_id()
    {
        static ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal Deal(params string[] actions)
        {
            var d = new ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal { Id = Guid.NewGuid() };
            foreach (var a in actions)
                d.Timeline.Add(new(default, default, a, null, null, null, DateTime.UtcNow));
            return d;
        }
        const string disputed = "disputed";
        var resumed = ACommerce.Templates.Customer.Marketplace.Services.Deals.DealEvent.DisputeResumed;
        var never = Deal("created", disputed);
        var once = Deal("created", disputed, resumed);
        var twice = Deal("created", disputed, resumed, disputed, resumed);
        var sibling = Deal("created", disputed, resumed);
        string? Run(ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal d)
            => ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.ExpiryRun(d);
        string Id(ACommerce.Templates.Customer.Marketplace.Services.Deals.Deal d)
            => ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Fetch(Store, "inv-1", "expiry", Run(d)).By(ProviderNames.StoreGateway).Id;
        output.WriteLine($"[G6·المُهلَة] بِلا استِئناف ⇒ {Id(never)} · مَرَّة ⇒ {Id(once)} · مَرَّتان ⇒ {Id(twice)} · أُختٌ ⇒ {Id(sibling)}");
        Assert.Null(Run(never));
        Assert.Equal($"{OutboundUnits.PaymentGatewayInvoiceFetch}:{ProviderNames.StoreGateway}:inv-1:expiry", Id(never));
        Assert.Equal($"resume:{once.Id:N}:1", Run(once));
        Assert.Equal($"resume:{twice.Id:N}:2", Run(twice));
        Assert.Equal(4, new[] { Id(never), Id(once), Id(twice), Id(sibling) }.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Id(once), Id(once));
    }

    /// <summary><b>الإنشاءُ يُعَدُّ عِندَ ‏2xx الـ<c>POST</c> ووَسمُه مُساعِد</b>: ‏<c>POST</c> ‏201 ثُمَّ <c>PUT</c> ‏503 ⇐ نِيَّةٌ واحِدَةٌ وسَطرُ إنشاءٍ واحِد، ولا نِيَّةَ لِلوَسم، والرَمزُ
    /// رَمزُ الوَسمِ كَما كان (<c>gateway_unreachable</c>)؛ ومَصدَرُه أُولى صَفَقاتِ الفاتورَة.</summary>
    [Fact]
    public async Task A_create_whose_metadata_put_fails_is_one_create_entry_and_no_intent_for_the_tag()
    {
        var recorder = new MemoryRecorder();
        var script = new Script((HttpStatusCode.Created, MoyasarClientTests.InvoiceJson("initiated", metadata: new Dictionary<string, string>())),
                                (HttpStatusCode.ServiceUnavailable, "{\"type\":\"api_error\"}"));
        var deal = Guid.NewGuid();
        var create = ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.Create(Store, deal);
        var draft = new ACommerce.Kit.Payments.MoyasarInvoiceDraft(15000, "طَلَب", "https://store.example/r", "https://store.example/r",
            new Dictionary<string, string> { ["deal_1"] = deal.ToString("N") });

        var res = await Gateway(script, recorder).CreateInvoiceAsync(create, MoyasarClientTests.Key, draft);

        var id = $"{OutboundUnits.PaymentGatewayInvoiceCreate}:{ProviderNames.StoreGateway}:{create.RequestId}";
        output.WriteLine($"[G6] الإنشاءُ ثُمَّ وَسمٌ ساقِط ⇒ {res.Failure?.Code} · {string.Join(" → ", script.Seen)} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal(ACommerce.Kit.Payments.MoyasarFailures.Unreachable, res.Failure!.Code);
        Assert.Equal(new[] { "POST /v1/invoices", $"PUT /v1/invoices/{MoyasarClientTests.InvoiceId}" }, script.Seen);
        Assert.Equal(new[] { $"open {id}", $"decide {id} Accepted 1" }, recorder.Log);
        Assert.Equal((ACommerce.Templates.Customer.Marketplace.Services.Metering.MeterRules.DealSource, deal.ToString("D")), (create.SourceType, create.SourceId));
    }

    /// <summary>
    /// <para><b>PayPal</b>: الرَمزُ ومُنتَجُ الكاتالوجِ مُساعِدانِ بِلا نِيَّة، والخُطَّةُ تُعَدُّ بِمِفتاحِ PayPal الَّذي تُرسِلُه العُدَّة (<c>PayPal-Request-Id</c> المُشتَقُّ مِن
    /// المُنتَج) أَيّاً كانَ مُعَرِّفُ المُنادي؛ والرابِطُ بِمِفتاحِه، وعُضوُ الواجِهَةِ بِلا <c>ProviderCall</c> يُعَدُّ لِمَتجَرِ طَلَبِه بِالمِفتاحِ نَفسِه؛ والتَحَقُّقُ مِن التَوقيعِ
    /// بِمُعَرِّفِ الإرسال؛ وعُضوا الواجِهَةِ بِلا مُنادٍ يَمُرّانِ ولا يُعَدّان.</para>
    /// </summary>
    [Fact]
    public async Task PayPal_counts_the_plan_under_its_own_request_id_and_leaves_the_token_and_the_product_uncounted()
    {
        var recorder = new MemoryRecorder();
        var script = new Script(
            (HttpStatusCode.OK, "{\"access_token\":\"A21AA\",\"expires_in\":32400}"),
            (HttpStatusCode.Created, "{\"id\":\"PROD-1\"}"), (HttpStatusCode.Created, "{\"id\":\"P-1\"}"),
            (HttpStatusCode.Created, "{\"id\":\"I-1\",\"status\":\"APPROVAL_PENDING\"}"),
            (HttpStatusCode.Created, "{\"id\":\"I-2\",\"status\":\"APPROVAL_PENDING\"}"),
            (HttpStatusCode.OK, "{\"verification_status\":\"SUCCESS\"}"),
            (HttpStatusCode.OK, "{}"), (HttpStatusCode.OK, "{}"));
        var opts = new ACommerce.Kit.Payments.Providers.PayPal.PayPalOptions { ClientId = "c", ClientSecret = "s", Environment = "live", WebhookId = "WH-1" };
        var provider = new ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider(Options.Create(opts),
            new ProviderHttp<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>(new HttpClient(script), new ProviderCalls(recorder)),
            new ACommerce.Kit.Payments.Providers.PayPal.PayPalTokenCache(), NullLogger<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>.Instance);
        var gateway = new ACommerce.Kit.Payments.Providers.PayPal.PayPalGateway(opts, provider);
        var draft = ACommerce.Kit.Payments.Providers.PayPal.PayPalCatalogPolicy.ReadDraft("manual", "باقَة", "49", "USD", "MONTH");

        await gateway.CreateCatalogPlanAsync(ProviderCall.ForChannel(OutboundUnits.BillingPayPalPlanCreate, MeterSentinels.Platform, "caller-id"), draft);
        await gateway.CreateSubscriptionAsync(ProviderCall.ForChannel(OutboundUnits.BillingPayPalLinkCreate, Store, "wsl-s-1"), "P-1", Store, "wsl-s-1");
        await ((ACommerce.Kit.Payments.IPaymentProvider)provider).CreateSubscriptionAsync(new ACommerce.Kit.Payments.SubscriptionRequest(Store, "P-1", 0m, ""), "wsl-s-2");
        Assert.True(await gateway.VerifyWebhookSignatureAsync(ProviderCall.ForChannel(OutboundUnits.BillingPayPalWebhookVerify, MeterSentinels.Platform, "TX-1"),
            new ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders("TX-1", "2026-11-12T00:00:00Z", "https://api.paypal.com/cert.pem", "SHA256withRSA", "sig=="), "{}"));
        await provider.CancelSubscriptionAsync("I-1");
        await provider.GetInvoiceAsync("CAP-1");

        var plan = $"{OutboundUnits.BillingPayPalPlanCreate}:{ProviderNames.PayPal}:{ACommerce.Kit.Payments.Providers.PayPal.PayPalCatalogPolicy.PlanRequestId("PROD-1", draft)}";
        output.WriteLine($"[G6] PayPal ⇒ {string.Join(" → ", script.Seen)} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal(8, script.Seen.Count);
        Assert.Equal(new[]
        {
            $"open {plan}", $"decide {plan} Accepted 1",
            $"open {OutboundUnits.BillingPayPalLinkCreate}:{ProviderNames.PayPal}:wsl-s-1", $"decide {OutboundUnits.BillingPayPalLinkCreate}:{ProviderNames.PayPal}:wsl-s-1 Accepted 1",
            $"open {OutboundUnits.BillingPayPalLinkCreate}:{ProviderNames.PayPal}:wsl-s-2", $"decide {OutboundUnits.BillingPayPalLinkCreate}:{ProviderNames.PayPal}:wsl-s-2 Accepted 1",
            $"open {OutboundUnits.BillingPayPalWebhookVerify}:{ProviderNames.PayPal}:TX-1", $"decide {OutboundUnits.BillingPayPalWebhookVerify}:{ProviderNames.PayPal}:TX-1 Accepted 1",
        }, recorder.Log);
    }

    /// <summary>
    /// <para><b>رِسالَةُ PayPal المُزَوَّرَةُ لا تَزرَعُ رَأسَها في الدَفتَر</b> (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-SEC-R2-1 — ADR-051 §١٩): البابُ مُعفًى
    /// مِن بَوّابَةِ المَصدَر، والرَأسُ <c>paypal-transmission-id</c> يَكتُبُه مَجهول، وPayPal يُجيبُ التَوقيعَ الفاسِدَ بِـ‏200 و«FAILURE» فَيُقبَلُ النِداءُ ويُعَدّ.
    /// الرُؤوسُ تُبنى كَما يَبنيها البابُ (<c>PayPalSurface.HeadersFrom</c> ثُمَّ <c>PayPalSurface.VerifyCall</c>) والعُدَّةُ الحَقيقِيَّة: مُعَرِّفُ مُستَخدِمٍ في الرَأسِ
    /// ⇐ سَطرٌ مَقبولٌ وحُكمُ G17 (<c>LedgerPersonScan.Leaks</c>) عَلى مُعَرِّفِ السَطرِ ومَصدَرِه ومُعَرِّفِ النِيَّةِ وطَلَبِها صِفر؛ والهاتِفُ نَصّاً كَذلِك؛ ورَأسٌ
    /// طَولُه ‏4096 ⇐ مُعَرِّفٌ بِطولِ اسمٍ مُستَعار؛ والإرسالُ نَفسُه مُعاداً ⇐ المُعَرِّفُ نَفسُه (سَطرٌ واحِد).</para>
    /// </summary>
    [Fact]
    public async Task A_forged_paypal_webhook_never_plants_its_transmission_header_in_the_ledger()
    {
        var user = Guid.NewGuid();
        var phone = "+966551234567";
        var huge = string.Concat(Enumerable.Range(0, 128).Select(_ => Guid.NewGuid().ToString("N")));
        var opts = new ACommerce.Kit.Payments.Providers.PayPal.PayPalOptions { ClientId = "c", ClientSecret = "s", Environment = "live", WebhookId = "WH-1" };
        var rows = new List<LedgerPersonScan.Row>();
        var ids = new List<string>();
        foreach (var header in new[] { user.ToString("D"), user.ToString("N"), phone, huge, user.ToString("D") })
        {
            var recorder = new MemoryRecorder();
            var script = new Script((HttpStatusCode.OK, "{\"access_token\":\"A21AA\",\"expires_in\":32400}"), (HttpStatusCode.OK, "{\"verification_status\":\"FAILURE\"}"));
            var provider = new ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider(Options.Create(opts),
                new ProviderHttp<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>(new HttpClient(script), new ProviderCalls(recorder)),
                new ACommerce.Kit.Payments.Providers.PayPal.PayPalTokenCache(), NullLogger<ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider>.Instance);
            var gateway = new ACommerce.Kit.Payments.Providers.PayPal.PayPalGateway(opts, provider);
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionIdHeader] = header;
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionTimeHeader] = "2026-11-13T00:00:00Z";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.CertUrlHeader] = "https://api.paypal.com/cert.pem";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.AuthAlgoHeader] = "SHA256withRSA";
            http.Request.Headers[ACommerce.Kit.Payments.Providers.PayPal.PayPalWebhookHeaders.TransmissionSigHeader] = "forged==";
            var headers = ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface.HeadersFrom(http.Request);
            var call = ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface.VerifyCall(headers);

            Assert.False(await gateway.VerifyWebhookSignatureAsync(call, headers, "{\"id\":\"WH-1\"}"));
            var id = $"{OutboundUnits.BillingPayPalWebhookVerify}:{ProviderNames.PayPal}:{call.RequestId}";
            Assert.Equal(new[] { $"open {id}", $"decide {id} Accepted 1" }, recorder.Log);
            output.WriteLine($"[G17] رَأسٌ بِطولِ {header.Length} ⇒ {id} ({id.Length})");
            Assert.DoesNotContain(header, id, StringComparison.OrdinalIgnoreCase);
            Assert.Matches("^[0-9a-f]{32}$", call.RequestId);
            ids.Add(id);
            // الصُفوفُ كَما يَكتُبُها الكاتِب: السَطرُ بِمَصدَرِه (الطَلَبُ حينَ لا `About`) والنِيَّةُ بِطَلَبِها.
            rows.Add(new("usage_entry", id, "source_id", call.SourceId ?? call.RequestId));
            rows.Add(new("outbound_intent", id, "request_id", call.RequestId));
        }

        var leaks = LedgerPersonScan.Leaks(new[] { user.ToString("D") }, rows);
        output.WriteLine($"[G17] صُفوفٌ مَفحوصَة {rows.Count} · تَسَرُّب {leaks.Count}");
        Assert.Equal(10, rows.Count);
        Assert.Empty(leaks);
        Assert.DoesNotContain(rows, r => r.Value.Contains(phone, StringComparison.Ordinal) || r.Id.Contains(phone, StringComparison.Ordinal));
        Assert.Equal(ids[0], ids[4]);
        Assert.Equal(4, ids.Distinct().Count());
    }

    /// <summary><b>Paddle</b>: المُعامَلَةُ تُعَدُّ بِمَرجِعِنا — ‏201 مَقبولَة و‏400 مَرفوضَة والجَوابُ يَعودُ كَما كان (رِسالَةُ Paddle في النَتيجَة).</summary>
    [Theory]
    [InlineData(201, "Accepted")]
    [InlineData(400, "Refused")]
    public async Task Paddle_counts_the_transaction_under_our_reference(int status, string outcome)
    {
        var recorder = new MemoryRecorder();
        var script = new Script(((HttpStatusCode)status, status == 201
            ? "{\"data\":{\"id\":\"txn_01\",\"status\":\"ready\",\"checkout\":{\"url\":\"https://pay.example/c?_ptxn=txn_01\"}}}"
            : "{\"error\":{\"code\":\"bad_request\"}}"));
        var provider = new ACommerce.Kit.Payments.Providers.Paddle.PaddlePaymentProvider(
            Options.Create(new ACommerce.Kit.Payments.Providers.Paddle.PaddleOptions { ApiKey = "k", Environment = "live" }),
            new ProviderHttp<ACommerce.Kit.Payments.Providers.Paddle.PaddlePaymentProvider>(new HttpClient(script), new ProviderCalls(recorder)),
            NullLogger<ACommerce.Kit.Payments.Providers.Paddle.PaddlePaymentProvider>.Instance);
        var draft = ACommerce.Kit.Payments.Providers.Paddle.PaddleTransactionPolicy.ReadDraft(Store, "manual", "49", "USD", "30", "شَهر", "");
        var reference = ACommerce.Kit.Payments.Providers.Paddle.PaddleTransactionPolicy.Reference(draft);

        var result = await provider.CreateTransactionAsync(ProviderCall.ForChannel(OutboundUnits.BillingPaddleTransactionCreate, Store, reference), draft, reference);

        var id = $"{OutboundUnits.BillingPaddleTransactionCreate}:{ProviderNames.Paddle}:{reference}";
        output.WriteLine($"[G6] Paddle {status} ⇒ {result.FailureReason ?? result.TransactionId} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal(new[] { $"open {id}", $"decide {id} {outcome} 1" }, recorder.Log);
        Assert.Equal(status == 201, result.FailureReason is null);
    }

    // ═══ (‏2026-11-12، W13-6·١٥ — التَصميم §6.2) OTO ونِداءُ النَموذَج ═══════════════════════════════════════════════════════════════════

    private static ACommerce.Kit.Delivery.OtoClient Oto(HttpMessageHandler handler, OutboundRecorder recorder)
        => new(new ProviderHttp<ACommerce.Kit.Delivery.OtoClient>(new HttpClient(handler), new ProviderCalls(recorder)),
               Options.Create(new ACommerce.Kit.Delivery.OtoOptions { Environment = ACommerce.Kit.Delivery.OtoEnvironment.Staging }),
               new ACommerce.Kit.Delivery.OtoTokenCache(), NullLogger<ACommerce.Kit.Delivery.OtoClient>.Instance);

    private const string OtoToken = "{\"access_token\":\"A1\",\"success\":true,\"expires_in\":\"3600\"}";
    private const string OtoOrder = "0f8fad5bd9cb469fa165705ed7eb6e2a";

    private static ACommerce.Kit.Delivery.OtoOrderDraft OtoDraft() => new(OtoOrder, ACommerce.Kit.Delivery.OtoClient.CashOnDelivery, 0m, 0m, "",
        new ACommerce.Kit.Delivery.OtoCustomer("w13", "0500000000", "w13", "Riyadh", "SA"), "w13");

    /// <summary>
    /// <para><b>OTO</b> (التَصميم §6.2؛ صُفوفُ G6): الطَلَبُ نَفسُه يُسأَلُ عَنه عِندَ الشَحنِ (<c>check</c>) وبَعدَ الإشعارِ (<c>notice</c>) ⇐ <b>سَطران</b>، وإعادَةُ OTO لِلإشعارِ
    /// نَفسِه ⇐ سَطرٌ واحِد، والاستِردادُ (<c>recover</c>) سَطرٌ ثالِث؛ والإنشاءُ بِمُعَرِّفِ طَلَبِنا مَرَّتَينِ ⇐ سَطرٌ واحِد؛ والوارِدُ بِمُعَرِّفٍ جَديد؛ والبائِعُ بِمُعَرِّفِ سَطرِ
    /// أَثَرِه. ومُبادَلَتا الرَمزِ (البائِعُ والسوق) وقارِئُ الخِياراتِ بِلا مُنادٍ يَمُرّانِ بِلا نِيَّة. والمَصدَرُ الطَلَبُ نَفسُه، والمُزَوِّدُ تَسِمُه العُدَّة.</para>
    /// </summary>
    [Fact]
    public async Task OTO_counts_one_order_status_under_check_and_notice_as_two_the_create_by_our_order_and_leaves_the_tokens_uncounted()
    {
        var recorder = new MemoryRecorder();
        var tracking = "{\"success\":true,\"orderId\":\"" + OtoOrder + "\",\"otoId\":\"540789\",\"status\":\"searchingDriver\"}";
        var script = new Script((HttpStatusCode.OK, OtoToken),
            (HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}"), (HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}"),
            (HttpStatusCode.OK, tracking), (HttpStatusCode.OK, tracking), (HttpStatusCode.OK, tracking), (HttpStatusCode.OK, tracking),
            (HttpStatusCode.OK, "{\"success\": true, \"id\": \"59\"}"),
            (HttpStatusCode.OK, OtoToken), (HttpStatusCode.OK, OtoRouteHandler.RegisterAnswer),
            (HttpStatusCode.OK, "{\"success\":true,\"deliveryCompany\":[]}"));
        var client = Oto(script, recorder);
        var create = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Create(Store, OtoOrder);
        var check = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Status(Store, OtoOrder,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Check, ProviderCall.FreshRequestId());
        var notice = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Status(Store, OtoOrder,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Notice, "delivered:1595941360328");
        var recover = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Status(Store, OtoOrder,
            ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Recover, ProviderCall.FreshRequestId());
        var webhook = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Webhook(Store);
        var audit = Guid.NewGuid();
        var vendor = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Vendor(Store, audit);

        for (var i = 0; i < 2; i++) Assert.True((await client.CreateOrderAsync(create, "RT-1", OtoDraft())).Ok);
        Assert.True((await client.GetOrderStatusAsync(check, "RT-1", OtoOrder)).Ok);
        for (var i = 0; i < 2; i++) Assert.True((await client.GetOrderStatusAsync(notice, "RT-1", OtoOrder)).Ok);
        Assert.True((await client.GetOrderStatusAsync(recover, "RT-1", OtoOrder)).Ok);
        Assert.True((await client.RegisterWebhookAsync(webhook, "RT-1", "https://wasayel.example/s/shipping/oto/order-status", "w13-secret")).Ok);
        Assert.True((await client.RegisterVendorAsync(vendor, "MKT-1",
            new ACommerce.Kit.Delivery.OtoVendorRegistration("SA", "Riyadh", "v@example.com", "966500000000", "F", "L", false, "w13"))).Ok);
        Assert.True((await client.CheckOtoDeliveryFeeAsync("RT-1", new ACommerce.Kit.Delivery.OtoFeeQuery("Riyadh", "Jeddah", 1m))).Ok);

        string Id(string unit, string request) => $"{unit}:{ProviderNames.Oto}:{request}";
        var accepted = Accepted(recorder);
        var entries = accepted.Distinct(StringComparer.Ordinal).ToList();
        var statuses = entries.Count(e => e.StartsWith(OutboundUnits.DeliveryOrderStatus + ":", StringComparison.Ordinal));
        output.WriteLine($"[G6] OTO: طَلَبات {script.Seen.Count} · مَقبول {accepted.Count} · سُطور {entries.Count} (حالات {statuses}): {string.Join(" | ", entries)}");
        Assert.Equal(11, script.Seen.Count);
        Assert.Equal(8, accepted.Count);
        Assert.Equal(new[]
        {
            Id(OutboundUnits.DeliveryOrderCreate, OtoOrder),
            Id(OutboundUnits.DeliveryOrderStatus, $"{OtoOrder}:check:{check.RequestId.Split(':')[2]}"),
            Id(OutboundUnits.DeliveryOrderStatus, $"{OtoOrder}:notice:delivered:1595941360328"),
            Id(OutboundUnits.DeliveryOrderStatus, $"{OtoOrder}:recover:{recover.RequestId.Split(':')[2]}"),
            Id(OutboundUnits.DeliveryWebhookRegister, webhook.RequestId),
            Id(OutboundUnits.DeliveryVendorRegister, audit.ToString("N")),
        }, entries);
        Assert.Equal(16, recorder.Log.Count);
        Assert.Equal((ProviderCall.DeliveryOrderSource, OtoOrder, ProviderCall.DeliveryOrderSource, OtoOrder), (create.SourceType, create.SourceId, check.SourceType, check.SourceId));
        Assert.Null(webhook.SourceType);
    }

    /// <summary><b>‏401 ثُمَّ رَمزٌ جَديدٌ عَمَلِيَّةٌ واحِدَة</b>: الطَلَبُ الأَوَّلُ مَرفوضٌ بِرَمزٍ قَديم، والعُدَّةُ تُعيدُه مَرَّةً بِالنِداءِ نَفسِه فَيُقبَل — النِيَّةُ نَفسُها مَرفوضَةً ثُمَّ مَقبولَة
    /// (والقاعِدَةُ تَرفَعُها مَقبولَةً — <c>MeterSql.IntentAccept</c>، صَفُّه الحَيُّ في <c>LiveProviderCallsProofTests</c>)، والرَمزانِ بِلا نِيَّة.</summary>
    [Fact]
    public async Task An_OTO_401_retried_with_a_fresh_token_is_one_operation_refused_then_accepted()
    {
        var recorder = new MemoryRecorder();
        var script = new Script((HttpStatusCode.OK, OtoToken), (HttpStatusCode.Unauthorized, "{}"), (HttpStatusCode.OK, OtoToken),
            (HttpStatusCode.OK, "{\"success\":true,\"otoId\":540789}"));
        var create = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Create(Store, OtoOrder);

        var res = await Oto(script, recorder).CreateOrderAsync(create, "RT-1", OtoDraft());

        var id = $"{OutboundUnits.DeliveryOrderCreate}:{ProviderNames.Oto}:{OtoOrder}";
        output.WriteLine($"[G6] OTO 401 ⇒ {string.Join(" → ", script.Seen)} · {string.Join(" → ", recorder.Log)}");
        Assert.True(res.Ok);
        Assert.Equal(4, script.Seen.Count);
        Assert.Equal(new[] { $"open {id}", $"decide {id} Refused 1", $"open {id}", $"decide {id} Accepted 1" }, recorder.Log);
    }

    /// <summary>
    /// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-CNT-1 — ADR-051 §١٩) <b>جَوابُ OTO يُحكَمُ بِجِسمِه كَما تَحكُمُ العُدَّة</b>: ‏200 بِـ<c>"success": false</c>
    /// (رَصيدٌ نَفِدَ، OTO1006) مَرفوضٌ ولا سَطر — كانَ مَقبولاً بِحالَتِه وَحدَها والعُدَّةُ تَقولُ «oto_refused»؛ و‏200 بِنَجاحٍ مَقبول؛ و‏400 مَرفوض؛ و‏503 مَجهول؛
    /// و‏200 لا يُقرَأُ جِسمُه مَقبول (المُزَوِّدُ قَبِل، والعُدَّةُ تَقولُ «oto_unreadable»).</para>
    /// </summary>
    [Theory]
    [InlineData(200, "{\"success\":false,\"otoErrorCode\":\"OTO1006\",\"otoErrorMessage\":\"Credit is not enough\"}", "Refused", false)]
    [InlineData(200, "{\"success\":true,\"otoId\":540789}", "Accepted", true)]
    [InlineData(400, "{\"success\":false,\"otoErrorCode\":\"OTO1001\"}", "Refused", false)]
    [InlineData(503, "<html/>", "Unknown", false)]
    [InlineData(200, "not json", "Accepted", false)]
    public async Task An_OTO_answer_is_judged_by_its_body_as_the_kit_judges_it(int status, string body, string outcome, bool ok)
    {
        var recorder = new MemoryRecorder();
        var script = new Script((HttpStatusCode.OK, OtoToken), ((HttpStatusCode)status, body));
        var create = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Create(Store, OtoOrder);

        var res = await Oto(script, recorder).CreateOrderAsync(create, "RT-1", OtoDraft());

        var id = $"{OutboundUnits.DeliveryOrderCreate}:{ProviderNames.Oto}:{OtoOrder}";
        output.WriteLine($"[G6·OTO] {status} {body} ⇒ العُدَّة {(res.Ok ? "ok" : res.Failure!.Code)} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal(ok, res.Ok);
        Assert.Equal(new[] { $"open {id}", $"decide {id} {outcome} 1" }, recorder.Log);
    }

    /// <summary>‏(‏2026-11-13، M6C-CNT-1) <b>تَسجيلٌ واحِدٌ سَطرٌ واحِد</b>: تَحديثُ الوارِدِ (PUT) يَرُدُّه OTO بِـ‏200 و<c>"success": false</c> فَيُعيدُه البابُ إنشاءً (POST — <c>VendorShipping</c>)
    /// ⇐ المَرفوضُ بِلا سَطرٍ والإنشاءُ سَطر؛ وكانَ الاثنانِ مَقبولَين بِحالَتِهِما فَيُعَدُّ التَسجيلُ مَرَّتَين.</summary>
    [Fact]
    public async Task A_refused_webhook_update_and_its_recreate_are_one_accepted_registration()
    {
        var recorder = new MemoryRecorder();
        var script = new Script((HttpStatusCode.OK, OtoToken), (HttpStatusCode.OK, "{\"success\": false, \"otoErrorCode\": \"OTO1010\"}"),
            (HttpStatusCode.OK, "{\"success\": true, \"id\": \"60\", \"message\": \"webhook has been created\"}"));
        var client = Oto(script, recorder);
        var update = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Webhook(Store);
        var recreate = ACommerce.Templates.Customer.Marketplace.Services.Deals.ShippingCalls.Webhook(Store);

        var put = await client.RegisterWebhookAsync(update, "RT-1", "https://wasayel.example/x/shipping/oto/order-status", "s1", "59");
        var post = await client.RegisterWebhookAsync(recreate, "RT-1", "https://wasayel.example/x/shipping/oto/order-status", "s1");

        output.WriteLine($"[G6·OTO] PUT مَردود ثُمَّ POST ⇒ {put.Failure?.Code} · {post.Value} · {string.Join(" → ", script.Seen)} · {string.Join(" → ", recorder.Log)}");
        Assert.Equal((ACommerce.Kit.Delivery.OtoFailures.Refused, "60"), (put.Failure?.Code, post.Value));
        Assert.Equal(new[] { "Refused", "Accepted" }, recorder.Log.Where(l => l.StartsWith("decide ", StringComparison.Ordinal)).Select(l => l.Split(' ')[2]));
    }

    private static ACommerce.Templates.Customer.Marketplace.Services.AgentRequest ModelRequest() => new("w13", [], [], "w13-model", MaxTokens: 16);

    /// <summary>
    /// <para><b>نِداءُ النَموذَجِ يُقبَلُ حينَ يُقرَأُ استِهلاكُه</b> (التَصميم §6.2: «model → usage measured, not ‹no error›»): ‏200 بِاستِهلاكٍ مَقبول؛ و<b>‏400 بِاستِهلاكٍ مَقبولٌ</b> —
    /// التَوكِناتُ أُنفِقَت وإن جاءَ الجَوابُ خَطَأً؛ و‏401 بِلا استِهلاكٍ مَرفوض؛ و‏200 بِلا استِهلاكٍ و‏503 مَجهولان. و<b><c>ModelCallRecord</c> لا يَتَغَيَّر</b>: سَطرُه يُبنى مِن
    /// جَوابِ الخَلفِيَّةِ كَما كان، فَخَطَأُ ‏400 «لَم يُقَس» عِندَه وإن عَدَّه الدَفتَر.</para>
    /// </summary>
    [Theory]
    [InlineData(200, true, "Accepted", true)]
    [InlineData(400, true, "Accepted", false)]
    [InlineData(401, false, "Refused", false)]
    [InlineData(200, false, "Unknown", false)]
    [InlineData(503, false, "Unknown", false)]
    public async Task A_model_call_is_accepted_when_its_usage_is_read_even_beside_an_error(int status, bool usage, string outcome, bool measuredInRecord)
    {
        var recorder = new MemoryRecorder();
        var body = (status, usage) switch
        {
            (200, true) => "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}",
            (_, true) => "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\"},\"usage\":{\"input_tokens\":7,\"output_tokens\":0}}",
            (200, false) => "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}",
            _ => "{\"type\":\"error\"}",
        };
        var backend = new ACommerce.Templates.Customer.Marketplace.Services.AnthropicBackend(
            new ProviderHttp<ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend>(new HttpClient(new Script(((HttpStatusCode)status, body))), new ProviderCalls(recorder)),
            new ACommerce.Templates.Customer.Marketplace.Services.AgentProfile("Studio", "anthropic", null, "k", null, null));
        var call = ACommerce.Templates.Customer.Marketplace.Services.ModelCalls.Platform();

        var resp = await backend.CallAsync(call, ModelRequest(), CancellationToken.None);

        var id = $"{OutboundUnits.ModelCall}:{ProviderNames.Anthropic}:{call.RequestId}";
        var line = ACommerce.Templates.Customer.Marketplace.Services.Metering.ModelCallRecord.For(
            MeterSentinels.Platform, null, backend.ProviderName, "w13-model", "build", resp.Usage, resp.Error is null);
        output.WriteLine($"[G6] النَموذَج {status} (استِهلاكٌ في الجِسم: {usage}) ⇒ {string.Join(" → ", recorder.Log)} · ModelCallRecord.UsageMeasured={line.UsageMeasured}");
        Assert.Equal(new[] { $"open {id}", $"decide {id} {outcome} 1" }, recorder.Log);
        Assert.Equal(MeterSentinels.Platform, call.App);
        Assert.Equal(measuredInRecord, line.UsageMeasured);
    }

    /// <summary><b>كُلُّ خَلفِيَّةٍ تَسِمُ نِداءَها بِاسمِها</b> — Anthropic وGemini ومُتَوافِقُ Chat Completions (اسمٌ واحِدٌ لِكُلِّ عُنوانِه)؛ والأَخُ بِلا <c>ProviderCall</c>
    /// يُعَدُّ لِمَتجَرٍ غَيرِ مَعروفٍ (<c>_unresolved</c>) فَيُرى ولا يَضيع.</summary>
    [Fact]
    public async Task Each_model_backend_stamps_its_provider_and_the_ProviderCall_less_overload_counts_as_unresolved()
    {
        var log = new List<(string Id, string App)>();
        var recorder = new AppRecorder(log);
        var usage = "{\"usage\":{\"input_tokens\":1,\"output_tokens\":1},\"usageMetadata\":{\"promptTokenCount\":1,\"candidatesTokenCount\":1},"
                    + "\"content\":[],\"candidates\":[],\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
        ProviderHttp<ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend> Http()
            => new(new HttpClient(new Script((HttpStatusCode.OK, usage))), new ProviderCalls(recorder));
        var profile = new ACommerce.Templates.Customer.Marketplace.Services.AgentProfile("Studio", "x", "https://model-compat.test/", "k", null, "groq");
        var backends = new ACommerce.Templates.Customer.Marketplace.Services.IAgentBackend[]
        {
            new ACommerce.Templates.Customer.Marketplace.Services.AnthropicBackend(Http(), profile),
            new ACommerce.Templates.Customer.Marketplace.Services.GeminiBackend(Http(), profile),
            new ACommerce.Templates.Customer.Marketplace.Services.OpenAIBackend(Http(), profile),
        };
        foreach (var b in backends) await b.CallAsync(ACommerce.Templates.Customer.Marketplace.Services.ModelCalls.Platform(), ModelRequest(), CancellationToken.None);
        await backends[0].CallAsync(ModelRequest(), CancellationToken.None);

        output.WriteLine($"[G6] الخَلفِيّات ⇒ {string.Join(" | ", log.Select(l => $"{l.Id}@{l.App}"))}");
        Assert.Equal(new[] { ProviderNames.Anthropic, ProviderNames.Gemini, ProviderNames.OpenAiCompatible, ProviderNames.Anthropic },
                     log.Select(l => l.Id.Split(':')[1]));
        Assert.All(log, l => Assert.StartsWith(OutboundUnits.ModelCall + ":", l.Id, StringComparison.Ordinal));
        Assert.Equal(new[] { MeterSentinels.Platform, MeterSentinels.Platform, MeterSentinels.Platform, MeterSentinels.Unresolved }, log.Select(l => l.App));
    }

    /// <summary>كاتِبٌ يُسَجِّلُ مُعَرِّفَ النِيَّةِ ومَتجَرَها.</summary>
    private sealed class AppRecorder(List<(string Id, string App)> log) : OutboundRecorder
    {
        public override Task OpenAsync(ProviderCall call, CancellationToken ct)
        {
            lock (log) log.Add((call.Id, call.App));
            return Task.CompletedTask;
        }

        public override Task DecideAsync(ProviderCall call, ProviderAcceptance acceptance) => Task.CompletedTask;
    }
}
