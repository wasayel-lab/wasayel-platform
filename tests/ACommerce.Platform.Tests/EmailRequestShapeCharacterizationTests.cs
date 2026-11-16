using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Brevo;
using ACommerce.Kit.Auth.Providers.Smtp;
using ACommerce.Kit.Auth.Server;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شَكلُ ما يَبلُغُ Brevo وخادِمَ SMTP — يُثَبَّتُ عَلى مُستَوى القالِبِ قَبلَ أَن يَمُرّا بِالمُستَدعي المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١٢ — ADR-051) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١٢ يَنزِعُ `HttpClient` مِن `BrevoEmailChannel` ويُمَرِّرُ إرسالَها بِالمُستَدعي المَعدود، ويَنقُلُ رَأسَ
// `api-key` مِن `DefaultRequestHeaders` في الباني إلى الطَلَب، ويَقلِبُ `AddHttpClient<IEmailOtpChannel, BrevoEmailChannel>` إلى تَسجيلٍ آخَر؛ ويَلُفُّ أَفعالَ
// MailKit في `SmtpEmailChannel` (الاتِّصالُ والاعتِمادُ والإرسالُ والانصِراف) بِمُستَدعٍ يَعُدّ. ووَعدُه أَنّ **ما يَبلُغُ المُزَوِّدَ لا يَتَغَيَّرُ بايتاً**:
// الطَلَبُ (الفِعلُ والعُنوانُ والإصدارُ والرُؤوسُ والجِسم) وحِوارُ SMTP (الأَوامِرُ بِتَرتيبِها والرِسالَةُ بِرُؤوسِها وأَجزائِها)، وما يَرميه البابُ حينَ يَرفُضُ المُزَوِّد
// أَو يَصمُت. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يُثَبِّتُه ويُودَعُ وَحدَه، ثُمَّ يُعادُ تَشغيلُه بَعدَ التَبديلِ **بِلا تَعديلِ حَرف**.
//
// **والالتِقاطُ بِما يَبقى بَعدَ التَبديل** (التَصميم §6.3): Brevo بِـ`ConfigureHttpClientDefaults(… ConfigurePrimaryHttpMessageHandler(() => recorder))` — يَسري
// عَلى كُلِّ عَميلٍ يَبنيه المَصنَع؛ وSMTP بِخادِمٍ مَحَلِّيٍّ مُزَيَّفٍ عَلى مَنفَذٍ مِن 5200–5249 يُجيبُ بِحِوارٍ ثابِتٍ ويُسَجِّلُ ما يَسمَع. ولا يُسَمّي المِلَفُّ
// بانيَ قَناةٍ ولا نَوعاً يولَدُ في البَند **ولا يُنادي فِعلاً مِن أَفعالِ القَناةِ مُباشَرَةً** (تَوقيعاها يَتَغَيَّرانِ في التَبديل: `ProviderCall` أَوَّلاً): التَسجيلانِ
// (`AddBrevoEmailChannel` و`AddSmtpEmailChannel`)، والأَبوابُ الثَلاثَة — بابُ المَتجَر (`AuthHandlers.RequestEmailOtpHandler`) وبابُ الاستوديو
// (`StudioAuth.SendEmailCodeAsync`) وإشعارُ رَبطِ الوَسيلَة (`AccountContacts.NoticeLinkedAsync` عَلى عالَمِ وَثائِقَ في الذاكِرَة) — والقَناةُ بِاسمِها وتَلميحِها.
//
// **وبِلا قاعِدَةٍ ولا شَبَكَةٍ خارِجِيَّة**، والعَناوينُ عَشوائِيَّةٌ لِكُلِّ صَفٍّ فَلا تَتَقاطَعُ حِصَّةُ الإرسال. والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠):
// كُلُّ صَفٍّ يَعُدُّ الطَلَباتِ أَو الاتِّصالاتِ ويَفشَلُ إن لَم تَكُن واحِداً.

public class EmailRequestShapeCharacterizationTests(ITestOutputHelper output)
{
    private const string ApiKey = "xkeysib-characterization-0";
    private const string From = "no-reply@wasayel.test";
    private const string FromName = "وَسايِل";

    private static string FreshSlug() => $"em-ch-{Guid.NewGuid():N}"[..20];

    private static string FreshEmail() => $"owner-{Guid.NewGuid():N}"[..20] + "@wasayel.test";

    private static readonly Regex Code = new("^[1-9][0-9]{5}$", RegexOptions.CultureInvariant);

    // ─── Brevo: المُعالِجُ الأَوَّل ─────────────────────────────────────────────────────────────

    /// <summary>طَلَبٌ كَما بَلَغَ المُعالِجَ الأَوَّل.</summary>
    internal sealed record Seen(string Method, string Uri, string Version, string RequestHeaders, string ApiKeyValue, string ContentHeaders, string Body);

    /// <summary>المُعالِجُ الأَوَّل: يَلتَقِطُ ويُجيبُ بِلا شَبَكَة — أَو يَرمي، أَو يَصمُتُ حَتّى يُلغى.</summary>
    internal sealed class Recorder : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public Exception? Throw { get; set; }
        public bool Silent { get; set; }
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var seen = new Seen(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Version.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)),
                request.Headers.TryGetValues(BrevoEmailChannel.ApiKeyHeader, out var key) ? string.Join("|", key) : "",
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                body);
            lock (Requests) Requests.Add(seen);
            if (Throw is not null) throw Throw;
            if (Silent) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"messageId\":\"<0@smtp-relay.mailin.fr>\"}", Encoding.UTF8, "application/json") };
        }
    }

    private static (ServiceProvider Services, Recorder Recorder) Brevo(Action<BrevoEmailOptions>? configure = null)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        services.AddBrevoEmailChannel(configure ?? (o => { o.ApiKey = ApiKey; o.From = From; o.FromName = FromName; }));
        return (services.BuildServiceProvider(), recorder);
    }

    private Seen Single(Recorder recorder)
    {
        foreach (var r in recorder.Requests)
            output.WriteLine($"[brevo] {r.Method} {r.Uri} HTTP/{r.Version} · رُؤوس [{r.RequestHeaders}] · المُحتَوى [{r.ContentHeaders}] · {r.Body.Length} حَرفاً");
        Assert.True(recorder.Requests.Count == 1, $"أَداةٌ عَمياءُ أَو بابٌ يُرسِلُ غَيرَ مَرَّة: {recorder.Requests.Count} طَلَباً.");
        return recorder.Requests[0];
    }

    /// <summary>الجِسمُ بايتاً: الحُقولُ بِتَرتيبِها كَما يُسَلسِلُها <c>System.Text.Json</c> بِإعدادِ الوِب، والمُرسِلُ بِاسمِه، والرَمزُ في النَصِّ والـHTML.</summary>
    private static string ExpectedBody(string to, string subject, string text, string? html)
    {
        var payload = new Dictionary<string, object>
        {
            ["sender"] = new Dictionary<string, string> { ["email"] = From, ["name"] = FromName },
            ["to"] = new[] { new Dictionary<string, string> { ["email"] = to } },
            ["subject"] = subject,
            ["textContent"] = text,
        };
        if (html is not null) payload["htmlContent"] = html;
        return JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
    }

    private static string OtpSubject => LocaleCatalog.Text(LocaleCatalog.Arabic, OtpEmailMessage.SubjectKey);

    /// <summary>
    /// <para><b>الإشعارُ بِبابِه</b> (مُستَهلِكُ <c>SendNoticeAsync</c>): رَبطُ جَوّالٍ بِحِسابٍ بَريدُه مُتَحَقَّقٌ مِنه ⇐ <c>AccountContacts.NoticeLinkedAsync</c> يَكتُبُ
    /// الإشعارَ وسَطرَ التَدقيقِ في عالَمِ الوَثائِقِ ثُمَّ يُرسِلُ إلى البَريدِ عُنوانَ القامُوسِ ومَتنَه بِالجَوّالِ مُقَنَّعاً. يُعيدُ ما حَمَلَ الرِسالَةَ والعُنوانَ والنَصَّ المُتَوَقَّعَين.</para>
    /// </summary>
    private static async Task<(string Carried, string Subject, string Text)> NoticeAsync(IEmailOtpChannel channel, string email)
    {
        var world = new DocWorld();
        var user = new User { Id = Guid.NewGuid(), Email = email, EmailVerified = true };
        var phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var carried = await AccountContacts.NoticeLinkedAsync(world.Store, FreshSlug(), user, ContactLink.Phone, phone, DateTime.UtcNow,
            phoneChannel: null, emailChannel: channel, CancellationToken.None);
        var subject = LocaleCatalog.Find(LocaleCatalog.Arabic, AccountContacts.AddedTitleKey)!;
        var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, LocaleCatalog.Find(LocaleCatalog.Arabic, AccountContacts.AddedBodyKey)!,
            ContactLink.Mask(ContactLink.Phone, phone));
        return (carried, subject, text);
    }

    /// <summary>الشَكلُ الثابِت: <c>POST</c> إلى نُقطَةِ البَريدِ التَعامُلِيّ، ورَأسُ <c>api-key</c> وَحدَه بِالمِفتاح، ومُحتَوى JSON.</summary>
    private static void AssertShape(Seen seen)
    {
        Assert.Equal("POST", seen.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", seen.Uri);
        Assert.Equal("1.1", seen.Version);
        Assert.Equal("api-key", seen.RequestHeaders);
        Assert.Equal(ApiKey, seen.ApiKeyValue);
        // مَقيسٌ لا مُخَمَّن: مُحتَوى JSON لا يَحسِبُ طولَه (يُسَلسَلُ حينَ يُرسَل)، فَرَأسُ المُحتَوى نَوعُه وَحدَه.
        Assert.Equal("Content-Type: application/json; charset=utf-8", seen.ContentHeaders);
    }

    /// <summary>جِسمُ الرَمز: الرَمزُ يُستَخرَجُ مِن نَصِّه ثُمَّ يُقارَنُ الجِسمُ كُلُّه بايتاً.</summary>
    private static string AssertOtpBody(Seen seen, string to)
    {
        using var doc = JsonDocument.Parse(seen.Body);
        Assert.Equal(new[] { "sender", "to", "subject", "textContent", "htmlContent" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        var text = doc.RootElement.GetProperty("textContent").GetString()!;
        var code = Regex.Match(text, "[0-9]{6}").Value;
        Assert.Matches(Code, code);
        Assert.Equal(ExpectedBody(to, OtpSubject, OtpEmailMessage.Text(code), OtpEmailMessage.Html(code)), seen.Body);
        return code;
    }

    /// <summary><b>بابُ المَتجَرِ عَبرَ Brevo</b> (<c>POST /{slug}/auth/email/login</c> ← <c>RequestEmailOtpHandler</c>): العُنوانُ يُطَبَّعُ قَبلَ كُلِّ شَيء، والطَلَبُ
    /// واحِدٌ بِشَكلِه، والجِسمُ رِسالَةُ <c>OtpEmailMessage</c> بِعُنوانِ القامُوس؛ والنَتيجَةُ مُعَرِّفُ مُحاوَلَةٍ وتَلميحٌ بِالبَريد.</summary>
    [Fact]
    public async Task Brevo_store_door_posts_one_json_message_with_the_api_key_header()
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        var channel = services.GetRequiredService<IEmailOtpChannel>();
        var email = FreshEmail();

        var result = await AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp("  " + email.ToUpperInvariant() + " "),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), channel, CancellationToken.None);

        var seen = Single(recorder);
        AssertShape(seen);
        AssertOtpBody(seen, email);
        Assert.Equal(32, result.AttemptId.Length);
        Assert.Equal("", result.DisplayCode);
        Assert.Equal($"أَرسَلنا الكود إلى {email}", result.Hint);
    }

    /// <summary><b>بابُ الاستوديو عَبرَ Brevo</b> (<c>StudioAuth.SendEmailCodeAsync</c>): الطَلَبُ نَفسُه بِحَرفِه، ولا تَلميحَ مَعَ مُزَوِّدٍ فِعلِيّ.</summary>
    [Fact]
    public async Task Brevo_studio_door_posts_the_same_message()
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        var email = FreshEmail();

        var hint = await StudioAuth.SendEmailCodeAsync(services.GetRequiredService<IEmailOtpChannel>(), email);

        var seen = Single(recorder);
        AssertShape(seen);
        AssertOtpBody(seen, email);
        Assert.Null(hint);
    }

    /// <summary><b>الإشعارُ عَبرَ Brevo</b> (<c>SendNoticeAsync</c> بِبابِه — إشعارُ رَبطِ الوَسيلَة؛ ومُستَهلِكُه الآخَرُ بَريدُ صاحِبِ المَتجَر): الشَكلُ نَفسُه، والعُنوانُ والنَصُّ
    /// كَما كُتِبا، ولا <c>htmlContent</c>؛ واسمُ مُرسِلٍ فارِغٌ يُحذَفُ مِن الجِسم.</summary>
    [Fact]
    public async Task Brevo_notice_posts_its_subject_and_text_without_html()
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        var email = FreshEmail();

        var (carried, subject, text) = await NoticeAsync(services.GetRequiredService<IEmailOtpChannel>(), email);

        Assert.Equal("email", carried);
        var seen = Single(recorder);
        AssertShape(seen);
        Assert.Equal(ExpectedBody(email, subject, text, null), seen.Body);

        var (bare, bareRecorder) = Brevo(o => { o.ApiKey = ApiKey; o.From = From; o.FromName = "  "; });
        await using var __ = bare;
        var unnamed = await NoticeAsync(bare.GetRequiredService<IEmailOtpChannel>(), email);
        Assert.Equal("email", unnamed.Carried);
        Assert.Equal(ExpectedBody(email, unnamed.Subject, unnamed.Text, null).Replace($",\"name\":{JsonSerializer.Serialize(FromName, JsonSerializerOptions.Web)}", ""),
            Single(bareRecorder).Body);
    }

    /// <summary><b>رَدُّ Brevo بِخَطَإٍ يَرمي بِرِسالَةِ القَناة</b> عِندَ البابِ — «Brevo فَشِل: {الرَمز}» لِلرَفضِ ولِخَطَإ الخادِمِ مَعاً، بَعدَ طَلَبٍ واحِدٍ بِالشَكلِ نَفسِه.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Brevo_refusal_or_server_error_throws_the_channels_message_at_the_door(HttpStatusCode status)
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        recorder.Status = status;
        var email = FreshEmail();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(email),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), services.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None));

        output.WriteLine($"[brevo] {(int)status} ⇒ {ex.GetType().Name}: {ex.Message}");
        Assert.Equal($"Brevo فَشِل: {(int)status}", ex.Message);
        Assert.Null(ex.InnerException);
        AssertShape(Single(recorder));
    }

    /// <summary><b>انقِطاعُ الشَبَكَةِ وصَمتُها</b> عِندَ البابِ: استِثناءُ HTTP يَصيرُ «Brevo فَشِل الإرسال: …» بِأَصلِه داخِلَه، وصَمتٌ يَتَجاوَزُ نافِذَةَ القَناةِ (ثانِيَة،
    /// دونَ نافِذَةِ الأُنبوب) يَصيرُ رِسالَةَ المُهلَةِ بِاسمِها — كِلاهُما بَعدَ طَلَبٍ واحِد.</summary>
    [Fact]
    public async Task Brevo_transport_failure_and_silence_throw_named_messages()
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        var boom = new HttpRequestException("connection reset by peer");
        recorder.Throw = boom;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(FreshEmail()),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), services.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None));
        Assert.Equal("Brevo فَشِل الإرسال: connection reset by peer", ex.Message);
        Assert.Same(boom, ex.InnerException);
        AssertShape(Single(recorder));

        var (slow, slowRecorder) = Brevo(o => { o.ApiKey = ApiKey; o.From = From; o.FromName = FromName; o.TimeoutSeconds = 1; });
        await using var __ = slow;
        slowRecorder.Silent = true;
        var late = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(FreshEmail()),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), slow.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None));
        output.WriteLine($"[brevo] صَمت ⇒ {late.Message}");
        Assert.Equal("Brevo " + OtpSendGuard.TimeoutMessage(TimeSpan.FromSeconds(1)), late.Message);
        AssertShape(Single(slowRecorder));
    }

    /// <summary><b>التَسجيلُ عَبرَ Brevo</b>: <c>IEmailOtpChannel</c> عابِرٌ يُحَلُّ <c>BrevoEmailChannel</c> باسمِه وبِلا تَلميح، وإعدادٌ ناقِصٌ يَرمي عِندَ الحَلِّ بِرِسالَتِه.</summary>
    [Fact]
    public async Task Brevo_registration_resolves_a_transient_channel_and_refuses_missing_settings()
    {
        var (services, recorder) = Brevo();
        await using var _ = services;
        var first = services.GetRequiredService<IEmailOtpChannel>();
        var second = services.GetRequiredService<IEmailOtpChannel>();
        Assert.IsType<BrevoEmailChannel>(first);
        Assert.NotSame(first, second);
        Assert.Equal(("Brevo", (string?)null), (first.ChannelName, first.DevHintCode));
        Assert.Empty(recorder.Requests);

        foreach (var (missing, message) in new (Action<BrevoEmailOptions>, string)[]
                 {
                     (o => { o.From = From; }, "Brevo ApiKey غَير مُعَرَّف (Auth:Email:ApiKey)."),
                     (o => { o.ApiKey = ApiKey; }, "Brevo From غَير مُعَرَّف (Auth:Email:From)."),
                 })
        {
            var (bare, _) = Brevo(missing);
            await using var __ = bare;
            Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => bare.GetRequiredService<IEmailOtpChannel>()).Message);
        }
        output.WriteLine("[brevo] التَسجيل: عابِرٌ · BrevoEmailChannel · إعدادانِ ناقِصانِ يَرمِيانِ بِرِسالَتِهِما");
    }

    // ─── SMTP: خادِمٌ مَحَلِّيٌّ مُزَيَّف ─────────────────────────────────────────────────────────

    /// <summary>
    /// <para>خادِمُ SMTP مُزَيَّفٌ عَلى <c>127.0.0.1</c> بِمَنفَذٍ مِن 5200–5249: يُحَيّي، ويُعلِنُ <c>AUTH PLAIN</c>، ويَقبَلُ كُلَّ أَمرٍ بِجَوابِه الثابِت، ويَحفَظُ
    /// الأَوامِرَ بِتَرتيبِها والرِسالَةَ بِحَرفِها. و<see cref="RejectRecipient"/> يَرُدُّ المُستَلِمَ بِـ550، و<see cref="Silent"/> يَقبَلُ الاتِّصالَ ولا يُحَيّي.</para>
    /// </summary>
    internal sealed class FakeSmtp : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public int Port { get; }
        public bool RejectRecipient { get; init; }
        public bool Silent { get; init; }
        public int Connections;
        public List<string> Commands { get; } = new();
        public List<string> Messages { get; } = new();

        public FakeSmtp(bool rejectRecipient = false, bool silent = false)
        {
            RejectRecipient = rejectRecipient;
            Silent = silent;
            TcpListener? bound = null;
            for (var port = 5200; port <= 5249 && bound is null; port++)
            {
                var candidate = new TcpListener(IPAddress.Loopback, port);
                try { candidate.Start(); bound = candidate; Port = port; }
                catch (SocketException) { }
            }
            _listener = bound ?? throw new InvalidOperationException("لا مَنفَذَ حُرٌّ في 5200–5249 لِخادِمِ SMTP المُزَيَّف.");
            _loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                Interlocked.Increment(ref Connections);
                _ = Task.Run(() => ServeAsync(tcp));
            }
        }

        private async Task ServeAsync(TcpClient tcp)
        {
            using var _ = tcp;
            try
            {
                var stream = tcp.GetStream();
                var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                async Task Say(string line) { var b = Encoding.ASCII.GetBytes(line + "\r\n"); await stream.WriteAsync(b, _stop.Token); }
                if (Silent) { await Task.Delay(Timeout.Infinite, _stop.Token); return; }
                await Say("220 fake.local ESMTP");
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    lock (Commands) Commands.Add(line);
                    var verb = line.Split(' ')[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO": await Say("250-fake.local"); await Say("250 AUTH PLAIN"); break;
                        case "AUTH":
                            if (line.Split(' ').Length == 2) { await Say("334 "); if (await reader.ReadLineAsync(_stop.Token) is { } response) lock (Commands) Commands.Add(response); }
                            await Say("235 2.7.0 Authentication successful");
                            break;
                        case "MAIL": await Say("250 2.1.0 Sender OK"); break;
                        case "RCPT": await Say(RejectRecipient ? "550 5.1.1 Mailbox unavailable" : "250 2.1.5 Recipient OK"); break;
                        case "DATA":
                            await Say("354 Start mail input; end with <CRLF>.<CRLF>");
                            var data = new StringBuilder();
                            while (await reader.ReadLineAsync(_stop.Token) is { } d && d != ".") data.Append(d).Append("\r\n");
                            lock (Messages) Messages.Add(data.ToString());
                            await Say("250 2.0.0 Queued");
                            break;
                        case "RSET": await Say("250 2.0.0 Reset"); break;
                        case "QUIT": await Say("221 2.0.0 Bye"); return;
                        default: await Say("502 5.5.2 Command not recognized"); break;
                    }
                }
            }
            catch { /* الاختِبارُ انتَهى أَو العَميلُ قَطَع */ }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await Task.WhenAny(_loop, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    private static ServiceProvider Smtp(int port, Action<SmtpEmailOptions>? more = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSmtpEmailChannel(o =>
        {
            o.Host = "127.0.0.1"; o.Port = port; o.From = From; o.FromName = FromName;
            more?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    /// <summary>الأَوامِرُ كَما سَمِعَها الخادِم — ووَسيطُ <c>EHLO</c> اسمُ الآلَةِ أَو عُنوانُها فَيُوَحَّد.</summary>
    private List<string> Transcript(FakeSmtp smtp)
    {
        List<string> lines;
        lock (smtp.Commands) lines = smtp.Commands.Select(c => c.StartsWith("EHLO ", StringComparison.Ordinal) ? "EHLO <local>" : c).ToList();
        output.WriteLine($"[smtp] اتِّصالات {smtp.Connections} · {string.Join(" ⏎ ", lines)}");
        return lines;
    }

    /// <summary>الرِسالَةُ بِحَرفِها مَعَ ما لا يَثبُت مُقَنَّعاً — التاريخُ ومُعَرِّفُ الرِسالَةِ والحُدودُ والمُستَلِمُ العَشوائِيّ، وكُتَلُ base64 بِعَدَدِ أَسطُرِها
    /// وطولِها (مَحتواها المَفكوكُ يُقارَنُ حَرفاً في <see cref="AssertOtpMessage"/>) — وما سِواه بايتاً: الرُؤوسُ بِتَرتيبِها وتَرميزُها وبِنيَةُ الأَجزاء.</summary>
    private string Masked(FakeSmtp smtp, string to)
    {
        Assert.True(smtp.Messages.Count == 1, $"أَداةٌ عَمياءُ أَو قَناةٌ تُرسِلُ غَيرَ مَرَّة: {smtp.Messages.Count} رِسالَة.");
        var raw = smtp.Messages[0];
        var boundaries = Regex.Matches(raw, "boundary=\"([^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        var masked = Regex.Replace(raw, "^Date: [^\r\n]*", "Date: <date>", RegexOptions.Multiline);
        masked = Regex.Replace(masked, "^Message-Id: <[^>\r\n]+>", "Message-Id: <id>", RegexOptions.Multiline);
        for (var i = 0; i < boundaries.Count; i++) masked = masked.Replace(boundaries[i], $"<boundary{i}>", StringComparison.Ordinal);
        masked = masked.Replace(to, "<to>", StringComparison.Ordinal);
        masked = Regex.Replace(masked, @"(?<=\r\n\r\n)(?:[A-Za-z0-9+/=]+\r\n)+(?=\r\n--|\z)", m =>
        {
            var lines = m.Value.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            return $"<base64 {string.Join("+", lines.Select(l => l.Length).GroupBy(n => n).Select(g => g.Count() == 1 ? $"{g.Key}" : $"{g.Key}×{g.Count()}"))}>\r\n";
        });
        output.WriteLine($"[smtp] الرِسالَة ({raw.Length} حَرفاً، حُدود {boundaries.Count}):\n{masked.Replace("\r\n", "⏎\n")}");
        return masked;
    }

    private static MimeMessage Parse(FakeSmtp smtp) => MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(smtp.Messages[0])));

    /// <summary>رِسالَةُ الرَمزِ مَقروءَة: المُرسِلُ بِاسمِه، والمُستَلِم، والعُنوانُ مِن القامُوس، وجُزءا النَصِّ والـHTML رِسالَةُ <c>OtpEmailMessage</c> بِرَمزِها.</summary>
    private static string AssertOtpMessage(MimeMessage message, string to)
    {
        Assert.Equal($"\"{FromName}\" <{From}>", message.From.ToString());
        Assert.Equal(to, message.To.Mailboxes.Single().Address);
        Assert.Equal(OtpSubject, message.Subject);
        var code = Regex.Match(message.TextBody ?? "", "[0-9]{6}").Value;
        Assert.Matches(Code, code);
        Assert.Equal(OtpEmailMessage.Text(code), message.TextBody);
        Assert.Equal(OtpEmailMessage.Html(code), message.HtmlBody);
        return code;
    }

    /// <summary>الحِوارُ الثابِتُ بِلا اعتِماد: <c>EHLO</c> ثُمَّ المُرسِلُ والمُستَلِمُ والبَيانُ والانصِراف.</summary>
    private static string[] Dialogue(string to) => ["EHLO <local>", $"MAIL FROM:<{From}>", $"RCPT TO:<{to}>", "DATA", "QUIT"];

    /// <summary><b>بابُ المَتجَرِ عَبرَ SMTP</b>: اتِّصالٌ واحِدٌ بِحِوارِه الثابِت (بِلا اعتِمادٍ حينَ لا مُستَخدِم)، والرِسالَةُ بِرُؤوسِها وأَجزائِها بايتاً عَدا ما لا يَثبُت،
    /// ومَقروءَةً رِسالَةُ الرَمز؛ والنَتيجَةُ مُعَرِّفُ مُحاوَلَةٍ وتَلميحٌ بِالبَريد.</summary>
    [Fact]
    public async Task Smtp_store_door_speaks_one_dialogue_and_delivers_the_otp_message()
    {
        await using var smtp = new FakeSmtp();
        await using var services = Smtp(smtp.Port);
        var email = FreshEmail();

        var result = await AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(email),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), services.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None);

        Assert.Equal(Dialogue(email), Transcript(smtp));
        Assert.Equal(1, smtp.Connections);
        AssertOtpMessage(Parse(smtp), email);
        Assert.Equal(OtpTemplate, Masked(smtp, email));
        Assert.Equal((32, "", $"أَرسَلنا الكود إلى {email}"), (result.AttemptId.Length, result.DisplayCode, result.Hint));
    }

    /// <summary><b>بابُ الاستوديو عَبرَ SMTP، بِمُستَخدِمٍ وكَلِمَةِ سِرّ</b>: الحِوارُ نَفسُه ومَعَه <c>AUTH PLAIN</c> بِالاعتِمادِ المُرَمَّزِ قَبلَ المُرسِل، والرِسالَةُ نَفسُها.</summary>
    [Fact]
    public async Task Smtp_studio_door_authenticates_then_delivers_the_same_message()
    {
        await using var smtp = new FakeSmtp();
        await using var services = Smtp(smtp.Port, o => { o.Username = "mailer"; o.Password = "characterization-secret"; });
        var email = FreshEmail();

        var hint = await StudioAuth.SendEmailCodeAsync(services.GetRequiredService<IEmailOtpChannel>(), email);

        var plain = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0mailer\0characterization-secret"));
        var expected = Dialogue(email).ToList();
        expected.Insert(1, $"AUTH PLAIN {plain}");
        Assert.Equal(expected, Transcript(smtp));
        AssertOtpMessage(Parse(smtp), email);
        Assert.Equal(OtpTemplate, Masked(smtp, email));
        Assert.Null(hint);
    }

    /// <summary><b>الإشعارُ عَبرَ SMTP</b> (بِبابِه): الحِوارُ نَفسُه، والرِسالَةُ جُزءُ نَصٍّ واحِدٌ بِلا HTML بِعُنوانِها ونَصِّها.</summary>
    [Fact]
    public async Task Smtp_notice_delivers_one_plain_text_part()
    {
        await using var smtp = new FakeSmtp();
        await using var services = Smtp(smtp.Port);
        var email = FreshEmail();

        var (carried, subject, text) = await NoticeAsync(services.GetRequiredService<IEmailOtpChannel>(), email);

        Assert.Equal("email", carried);
        Assert.Equal(Dialogue(email), Transcript(smtp));
        var message = Parse(smtp);
        Assert.Equal((subject, text, (string?)null), (message.Subject, message.TextBody, message.HtmlBody));
        Assert.Equal(NoticeTemplate, Masked(smtp, email));
    }

    /// <summary><b>رَفضُ المُستَلِمِ يَرمي بِرِسالَةِ القَناة</b> عِندَ البابِ — «SMTP فَشِل الإرسال: {جَوابُ الخادِم}» وأَصلُه خَطَأُ أَمرِ SMTP، ولا رِسالَةَ تُسَلَّم.</summary>
    [Fact]
    public async Task Smtp_refused_recipient_throws_the_channels_message_at_the_door()
    {
        await using var smtp = new FakeSmtp(rejectRecipient: true);
        await using var services = Smtp(smtp.Port);
        var email = FreshEmail();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(email),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), services.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None));

        output.WriteLine($"[smtp] 550 ⇒ {ex.Message} · أَصلُه {ex.InnerException?.GetType().Name}");
        Assert.Equal("SMTP فَشِل الإرسال: 5.1.1 Mailbox unavailable", ex.Message);
        Assert.Equal("SmtpCommandException", ex.InnerException?.GetType().Name);
        Assert.Equal(RefusedDialogue(email), Transcript(smtp));
        Assert.Empty(smtp.Messages);
    }

    /// <summary><b>خادِمٌ يَقبَلُ ولا يُحَيّي</b>: يُقطَعُ عِندَ البابِ في نافِذَةِ القَناةِ بِرِسالَةِ المُهلَةِ بِاسمِها — ولا أَمرَ يُرسَل.</summary>
    [Fact]
    public async Task Smtp_silent_server_is_cut_with_the_named_timeout()
    {
        await using var smtp = new FakeSmtp(silent: true);
        await using var services = Smtp(smtp.Port, o => o.TimeoutSeconds = 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestEmailOtpHandler(new RequestEmailOtp(FreshEmail()),
            new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), services.GetRequiredService<IEmailOtpChannel>(), CancellationToken.None));

        output.WriteLine($"[smtp] صَمت ⇒ {ex.Message}");
        Assert.Equal("SMTP " + OtpSendGuard.TimeoutMessage(TimeSpan.FromSeconds(1)), ex.Message);
        Assert.Equal(1, smtp.Connections);
        Assert.Empty(Transcript(smtp));
    }

    /// <summary><b>التَسجيلُ عَبرَ SMTP</b>: <c>IEmailOtpChannel</c> مُفرَدٌ يُحَلُّ <c>SmtpEmailChannel</c> باسمِه وبِلا تَلميح، وإعدادٌ ناقِصٌ يَرمي عِندَ الحَلِّ بِرِسالَتِه.</summary>
    [Fact]
    public async Task Smtp_registration_resolves_a_singleton_channel_and_refuses_missing_settings()
    {
        await using var services = Smtp(5200);
        var first = services.GetRequiredService<IEmailOtpChannel>();
        Assert.IsType<SmtpEmailChannel>(first);
        Assert.Same(first, services.GetRequiredService<IEmailOtpChannel>());
        Assert.Equal(("Smtp", (string?)null), (first.ChannelName, first.DevHintCode));

        foreach (var (missing, message) in new (Action<SmtpEmailOptions>, string)[]
                 {
                     (o => o.Host = "", "SMTP Host غَير مُعَرَّف (Auth:Email:Host)."),
                     (o => o.From = "", "SMTP From غَير مُعَرَّف (Auth:Email:From)."),
                 })
        {
            await using var bare = Smtp(5200, missing);
            Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => bare.GetRequiredService<IEmailOtpChannel>()).Message);
        }
        output.WriteLine("[smtp] التَسجيل: مُفرَدٌ · SmtpEmailChannel · إعدادانِ ناقِصانِ يَرمِيانِ بِرِسالَتِهِما");
    }

    // ─── المُثَبَّتُ بِحَرفِه — مَقيسٌ عِندَ التَشغيلِ الأَوَّلِ عَلى الكودِ قَبلَ التَبديل ────────────────────

    /// <summary>المُستَلِمُ المَرفوضُ يَتبَعُه <c>RSET</c> مِن MailKit، ولا <c>DATA</c> ولا <c>QUIT</c> (العَميلُ يُغلَقُ بِلا انصِراف).</summary>
    private static string[] RefusedDialogue(string to) => ["EHLO <local>", $"MAIL FROM:<{From}>", $"RCPT TO:<{to}>", "RSET"];

    /// <summary>رِسالَةُ الرَمز: الرُؤوسُ بِتَرتيبِها (المُرسِلُ والعُنوانُ بِتَرميزِ RFC 2047)، وجُزءانِ بَديلانِ بِـbase64 — النَصُّ ثَلاثَةُ أَسطُرٍ والـHTML سَبعَة.</summary>
    private static readonly string OtpTemplate = string.Join("\r\n",
    [
        "From: =?utf-8?b?2YjZjtiz2KfZitmQ2YQ=?= <no-reply@wasayel.test>",
        "Date: <date>",
        "Subject: =?utf-8?b?2LHZjtmF2LIg2KfZhNiq2Y7Zkdit2Y7ZgtmP2ZHZgg==?=",
        "Message-Id: <id>",
        "To: <to>",
        "MIME-Version: 1.0",
        "Content-Type: multipart/alternative; boundary=\"<boundary0>\"",
        "",
        "--<boundary0>",
        "Content-Type: text/plain; charset=utf-8",
        "Content-Transfer-Encoding: base64",
        "",
        "<base64 76×2+56>",
        "",
        "--<boundary0>",
        "Content-Type: text/html; charset=utf-8",
        "Content-Transfer-Encoding: base64",
        "",
        "<base64 76×7>",
        "",
        "--<boundary0>--",
        "",
    ]);

    /// <summary>رِسالَةُ الإشعار: الرُؤوسُ نَفسُها بِعُنوانِ القامُوسِ مَطوِيّاً عَلى سَطرَين، وجُزءُ نَصٍّ واحِدٌ بِـbase64 بِلا أَجزاءٍ بَديلَة.</summary>
    private static readonly string NoticeTemplate = string.Join("\r\n",
    [
        "From: =?utf-8?b?2YjZjtiz2KfZitmQ2YQ=?= <no-reply@wasayel.test>",
        "Date: <date>",
        "Subject: =?utf-8?b?2KPZj9i22YrZgdmO2Kog2YjZjtiz2YrZhNmO2KnZjyDYr9mP2K7ZiNmE2Y0=?=",
        " =?utf-8?b?INil2YTZiSDYrdmQ2LPYp9io2ZDZgw==?=",
        "Message-Id: <id>",
        "To: <to>",
        "MIME-Version: 1.0",
        "Content-Type: text/plain; charset=utf-8",
        "Content-Transfer-Encoding: base64",
        "",
        "<base64 76×3+64>",
        "",
    ]);
}
