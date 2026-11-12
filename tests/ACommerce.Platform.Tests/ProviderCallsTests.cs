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
}
