using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Nafath;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Files;
using ACommerce.Kit.Files.Providers.S3;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما يَبلُغُ نَفاذاً ومَخزَنَ الكائِناتِ وخِدمَةَ الإشعارِ — يُثَبَّتُ عِندَ الأَبوابِ قَبلَ أَن تَمُرَّ الثَلاثُ بِالمُستَدعي المَعدود (‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051) ═══
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١٣ يَنزِعُ `HttpClient` مِن `NafathChannel` (رَمزُ OAuth واستِطلاعُ الحالَةِ نِداءانِ مُساعِدانِ لا يُعَدّان،
// وبَدءُ التَحَقُّقِ نِداءٌ مَعدود) ويَقلِبُ `AddHttpClient<INafathChannel, NafathChannel>()` إلى تَسجيلٍ آخَر، ويَلُفُّ أَفعالَ `IAmazonS3` في `S3FileStorage`
// (الرَفعُ مَعدودٌ بِبايتاتِه، والقِراءَةُ والوُجودُ والحَذفُ مُساعِدَة)، وفِعلَ `WebPushClient` في `WebPushService`؛ ويُغَيِّرُ تَوقيعَ `INafathChannel.StartAsync`
// و`IFileStorage.UploadAsync` و`WebPushService.SendAsync`. ووَعدُه أَنّ **ما يَبلُغُ المُزَوِّدَ لا يَتَغَيَّر**: الطَلَبُ بِفِعلِه وعُنوانِه ورُؤوسِه وجِسمِه، وما يَقولُه
// البابُ حينَ يَرفُضُ المُزَوِّدُ أَو يَسقُط. والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يُثَبِّتُه ويُودَعُ وَحدَه، ثُمَّ يُعادُ بَعدَ التَبديلِ **بِلا تَعديلِ حَرف**.
//
// **والالتِقاطُ بِما يَبقى بَعدَ التَبديل** (التَصميم §6.3):
//   • نَفاذ: `ConfigureHttpClientDefaults(… ConfigurePrimaryHttpMessageHandler(() => recorder))` — يَسري عَلى كُلِّ عَميلٍ يَبنيه المَصنَع. والأَبوابُ: بابُ البَدءِ
//     (`AuthHandlers.RequestNafathHandler`) وبابُ التَأكيدِ (`AuthHandlers.VerifyNafathHandler`)، و`IsApprovedAsync` بِاسمِه (تَوقيعُه لا يَتَغَيَّر).
//   • مَخزَنُ الكائِنات: مُستَمِعُ HTTP مَحَلِّيٌّ مُزَيَّفٌ عَلى مَنفَذٍ مِن 5200–5249 يَقومُ مَقامَ R2 (`ServiceURL`)، والعُدَّةُ بِتَسجيلِها (`AddS3FileStorage`). والرَفعُ
//     **بِبابَيه** (`/{slug}/me/save` وصورَتُه، و`/{slug}/listings/create` وصُوَرُه) — وهُما عَلى القاعِدَة، فَصَفّاهُما في الصِنفِ الحَيِّ أَسفَلَ هذا المِلَف
//     (`LiveS3UploadRequestShapeCharacterizationProofTests`)؛ والقِراءَةُ والوُجودُ والحَذفُ بِأَسمائِها (تَواقيعُها لا تَتَغَيَّر).
//   • الإشعار: البابُ `DealNotices.NotifyAcceptedAsync` عَلى عالَمِ وَثائِقَ في الذاكِرَة، والخِدمَةُ بِبانيها القائِم، و`WebPushClient` فيها يُعطى عَميلاً عَلى مُعالِجٍ
//     يُسَجِّل (الحَقلُ يُقرَأُ بِنَوعِه لا بِاسمِه) — فَيُفَكُّ الجِسمُ المُشَفَّرُ (`aesgcm`) بِمِفتاحِ المُشتَرِكِ الخاصّ ويُقارَنُ بايتاً.
//
// ولا يُسَمّي المِلَفُّ نَوعاً يولَدُ في البَند ولا فِعلاً يَتَغَيَّرُ تَوقيعُه. **وبِلا شَبَكَةٍ خارِجِيَّة**، والمُعَرِّفاتُ عَشوائِيَّةٌ لِكُلِّ صَفٍّ فَلا تَتَقاطَعُ الحِصَص.
// والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): كُلُّ صَفٍّ يَعُدُّ ما التَقَطَ ويَفشَلُ إن لَم يَكُن ما يَنتَظِر.

public class NafathS3PushRequestShapeCharacterizationTests(ITestOutputHelper output)
{
    // ═══ نَفاذ ═════════════════════════════════════════════════════════════════════════════════════════════════════

    private const string ClientId = "nafath-characterization-client";
    private const string ClientSecret = "nafath-characterization-secret";

    private static string FreshSlug() => $"nf-ch-{Guid.NewGuid():N}"[..20];

    private static string FreshNid() => "1" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>مَتجَرٌ مَحلولٌ قَناتُه نَفاذ — ما يُعطيه الوَسيطُ لِبابَي نَفاذ.</summary>
    internal sealed class NafathTenant(string slug) : ACommerce.Platform.Shared.ITenantContext
    {
        public string Slug => slug;
        public string Name => slug;
        public string BrandColor => "#000000";
        public string AuthChannel => AuthChannels.Nafath;
        public string TagLine => "";
        public string City => "";
        public bool IsResolved => true;
        public bool HasRoles => false;
    }

    /// <summary>طَلَبٌ كَما بَلَغَ المُعالِجَ الأَوَّل.</summary>
    internal sealed record Seen(string Method, string Uri, string Version, string RequestHeaders, string Authorization, string ContentHeaders, string Body);

    /// <summary>المُعالِجُ الأَوَّل لِنَفاذ: يُجيبُ كُلَّ مَسارٍ بِحالَتِه وجِسمِه بِلا شَبَكَة — أَو يَرمي.</summary>
    internal sealed class NafathRecorder : HttpMessageHandler
    {
        public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode StartStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode PollStatus { get; set; } = HttpStatusCode.OK;
        public string Token { get; set; } = "tok-" + Guid.NewGuid().ToString("N")[..12];
        public string TransId { get; set; } = "trans-" + Guid.NewGuid().ToString("N");
        public int Random { get; set; } = 7;
        public string State { get; set; } = "COMPLETED";
        public Exception? ThrowOnStart { get; set; }
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add(new Seen(
                request.Method.Method, request.RequestUri!.ToString(), request.Version.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)),
                request.Headers.Authorization?.ToString() ?? "",
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                body));
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/oauth/token")
                return Json(TokenStatus, $"{{\"accessToken\":\"{Token}\",\"expiresIn\":3600}}");
            if (path == "/api/v1/mfa/request")
            {
                if (ThrowOnStart is not null) throw ThrowOnStart;
                return Json(StartStatus, StartStatus == HttpStatusCode.OK ? $"{{\"transId\":\"{TransId}\",\"random\":{Random}}}" : "{\"error\":\"refused\"}");
            }
            if (path == "/api/v1/mfa/status")
                return Json(PollStatus, $"{{\"status\":\"{State}\"}}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (ServiceProvider Services, NafathRecorder Recorder) Nafath(Action<NafathOptions>? configure = null)
    {
        var recorder = new NafathRecorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        services.AddNafathChannel(configure ?? (o => { o.ClientId = ClientId; o.ClientSecret = ClientSecret; }));
        return (services.BuildServiceProvider(), recorder);
    }

    private void Print(string tag, IEnumerable<Seen> requests)
    {
        foreach (var r in requests)
            output.WriteLine($"[{tag}] {r.Method} {r.Uri} HTTP/{r.Version} · رُؤوس [{r.RequestHeaders}] · {r.Authorization} · المُحتَوى [{r.ContentHeaders}] · {r.Body}");
    }

    private static string TokenForm
        => $"grant_type=client_credentials&client_id={ClientId}&client_secret={ClientSecret}";

    /// <summary>طَلَبُ الرَمز: <c>POST /oauth/token</c> بِاستِمارَةِ اعتِمادِ العَميلِ وبِلا رَأسِ طَلَب.</summary>
    private static void AssertTokenRequest(Seen seen)
    {
        Assert.Equal(("POST", "https://api.nafath.sa/oauth/token", "1.1", "", ""), (seen.Method, seen.Uri, seen.Version, seen.RequestHeaders, seen.Authorization));
        Assert.Equal("Content-Length: 115,Content-Type: application/x-www-form-urlencoded", seen.ContentHeaders);
        Assert.Equal(TokenForm, seen.Body);
    }

    /// <summary>
    /// <para><b>بابُ البَدء</b> (<c>POST /{slug}/auth/nafath/login</c> ← <c>RequestNafathHandler</c>): طَلَبانِ بِتَرتيبِهِما — الرَمزُ ثُمَّ طَلَبُ التَحَقُّقِ بِـ<c>Bearer</c>
    /// وجِسمِ JSON بِالهُوِيَّةِ والخِدمَة — والمُحاوَلَةُ مُعَرِّفُ نَفاذٍ ورَقمُ العَرضِ بِخانَتَينِ ومُهلَةُ تِسعين.</para>
    /// </summary>
    [Fact]
    public async Task The_start_door_asks_a_token_then_posts_the_mfa_request_with_it()
    {
        var (services, recorder) = Nafath();
        await using var _ = services;
        var nid = FreshNid();
        var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(nid), new NafathTenant(FreshSlug()),
            services.GetRequiredService<INafathChannel>(), CancellationToken.None);

        Print("nafath·start", recorder.Requests);
        output.WriteLine($"[nafath·start] ⇒ {pending}");
        Assert.True(recorder.Requests.Count == 2, $"أَداةٌ عَمياءُ أَو بابٌ يُرسِلُ غَيرَ مَرَّتَين: {recorder.Requests.Count} طَلَباً.");
        AssertTokenRequest(recorder.Requests[0]);
        var start = recorder.Requests[1];
        Assert.Equal(("POST", "https://api.nafath.sa/api/v1/mfa/request", "1.1", "Authorization", $"Bearer {recorder.Token}"),
                     (start.Method, start.Uri, start.Version, start.RequestHeaders, start.Authorization));
        Assert.Equal("Content-Type: application/json; charset=utf-8", start.ContentHeaders);
        Assert.Equal($"{{\"nationalId\":\"{nid}\",\"service\":\"Login\"}}", start.Body);
        Assert.Equal((recorder.TransId, "07", 90), (pending.AttemptId, pending.DisplayCode, pending.AutoVerifyInSeconds));
    }

    /// <summary>
    /// <para><b>استِطلاعُ الحالَة</b>: بابُ التَأكيدِ يَطلُبُ قَناةً جَديدَةً (عابِرَة) فَرَمزاً جَديداً، ثُمَّ <c>GET /api/v1/mfa/status?transId=…</c> بِـ<c>Bearer</c> وبِلا جِسم؛
    /// وحالَةٌ غَيرُ <c>COMPLETED</c> ⇐ البابُ <c>null</c> قَبلَ أَن يَمَسَّ المَخزَن. و<c>COMPLETED</c> ⇐ <c>IsApprovedAsync</c> <c>true</c>؛ وحالَةٌ غَيرُ ناجِحَةٍ ⇐ <c>false</c>؛
    /// ومُحاوَلَةٌ لَم تَبدَأ في هذه العَمَلِيَّةِ ⇐ <c>false</c> بِلا طَلَب.</para>
    /// </summary>
    [Fact]
    public async Task The_status_poll_asks_a_token_then_gets_the_status_and_reads_completed_only()
    {
        var (services, recorder) = Nafath();
        await using var _ = services;
        var slug = FreshSlug();
        var nid = FreshNid();
        var tenant = new NafathTenant(slug);
        var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(nid), tenant, services.GetRequiredService<INafathChannel>(), CancellationToken.None);
        recorder.Requests.Clear();

        recorder.State = "WAITING";
        var world = new DocWorld();
        var waiting = await AuthHandlers.VerifyNafathHandler(new VerifyNafath(pending.AttemptId, nid), tenant,
            services.GetRequiredService<INafathChannel>(), world.Store, CancellationToken.None);
        Print("nafath·poll", recorder.Requests);
        Assert.Null(waiting);
        Assert.Empty(world.Touches);
        Assert.True(recorder.Requests.Count == 2, $"أَداةٌ عَمياء: {recorder.Requests.Count} طَلَباً.");
        AssertTokenRequest(recorder.Requests[0]);
        var poll = recorder.Requests[1];
        Assert.Equal(("GET", $"https://api.nafath.sa/api/v1/mfa/status?transId={Uri.EscapeDataString(pending.AttemptId)}", "1.1", "Authorization", $"Bearer {recorder.Token}", "", ""),
                     (poll.Method, poll.Uri, poll.Version, poll.RequestHeaders, poll.Authorization, poll.ContentHeaders, poll.Body));

        recorder.State = "COMPLETED";
        Assert.True(await services.GetRequiredService<INafathChannel>().IsApprovedAsync(pending.AttemptId, CancellationToken.None));
        recorder.PollStatus = HttpStatusCode.ServiceUnavailable;
        Assert.False(await services.GetRequiredService<INafathChannel>().IsApprovedAsync(pending.AttemptId, CancellationToken.None));
        Assert.Equal(6, recorder.Requests.Count);

        recorder.Requests.Clear();
        Assert.False(await services.GetRequiredService<INafathChannel>().IsApprovedAsync("trans-never-started-" + Guid.NewGuid().ToString("N"), CancellationToken.None));
        Assert.Empty(recorder.Requests);
    }

    /// <summary><b>الرَمزُ في القَناةِ لا في العَمَلِيَّة</b>: القَناةُ نَفسُها تَطلُبُه مَرَّةً لِلبَدءِ والاستِطلاع؛ والتَسجيلُ عابِرٌ — كُلُّ حَلٍّ قَناةٌ جَديدَةٌ ورَمزٌ جَديد.</summary>
    [Fact]
    public async Task The_token_is_cached_per_channel_and_the_channel_is_transient()
    {
        var (services, recorder) = Nafath();
        await using var _ = services;
        var channel = services.GetRequiredService<INafathChannel>();
        var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath(FreshNid()), new NafathTenant(FreshSlug()), channel, CancellationToken.None);
        Assert.True(await channel.IsApprovedAsync(pending.AttemptId, CancellationToken.None));
        Print("nafath·cache", recorder.Requests);
        Assert.Equal(new[] { "/oauth/token", "/api/v1/mfa/request", "/api/v1/mfa/status" }, recorder.Requests.Select(r => new Uri(r.Uri).AbsolutePath));

        Assert.NotSame(services.GetRequiredService<INafathChannel>(), services.GetRequiredService<INafathChannel>());
        Assert.Equal(("NafathChannel", "Nafath"), (channel.GetType().Name, channel.ChannelName));
    }

    /// <summary><b>الرَفضُ والسُقوطُ بِرَسائِلِهِما</b>: طَلَبُ التَحَقُّقِ يُرَدُّ ⇐ «Nafath فَشِل: {الرَمز}»؛ والرَمزُ يُرَدُّ ⇐ «Nafath OAuth فَشِل: {الرَمز}» ولا طَلَبَ بَعدَه؛
    /// واستِثناءُ النَقلِ يَخرُجُ مِن البابِ هُوَ نَفسُه (وبابُ الاستِمارَةِ يُحَوِّلُ كُلَّ ذلكَ إلى <c>send_failed</c>).</summary>
    [Fact]
    public async Task A_refusal_or_a_transport_failure_reaches_the_door_as_before()
    {
        foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            var (services, recorder) = Nafath();
            await using var _ = services;
            recorder.StartStatus = status;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestNafathHandler(new RequestNafath(FreshNid()),
                new NafathTenant(FreshSlug()), services.GetRequiredService<INafathChannel>(), CancellationToken.None));
            output.WriteLine($"[nafath·{(int)status}] {ex.Message} · طَلَبات {recorder.Requests.Count}");
            Assert.Equal(($"Nafath فَشِل: {(int)status}", 2), (ex.Message, recorder.Requests.Count));
        }

        var (tokenServices, tokenRecorder) = Nafath();
        await using (tokenServices)
        {
            tokenRecorder.TokenStatus = HttpStatusCode.Unauthorized;
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestNafathHandler(new RequestNafath(FreshNid()),
                new NafathTenant(FreshSlug()), tokenServices.GetRequiredService<INafathChannel>(), CancellationToken.None));
            output.WriteLine($"[nafath·oauth 401] {ex.Message} · طَلَبات {tokenRecorder.Requests.Count}");
            Assert.Equal(("Nafath OAuth فَشِل: 401", 1), (ex.Message, tokenRecorder.Requests.Count));
        }

        var (resetServices, resetRecorder) = Nafath();
        await using (resetServices)
        {
            var boom = new HttpRequestException("connection reset by peer");
            resetRecorder.ThrowOnStart = boom;
            var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => AuthHandlers.RequestNafathHandler(new RequestNafath(FreshNid()),
                new NafathTenant(FreshSlug()), resetServices.GetRequiredService<INafathChannel>(), CancellationToken.None));
            Assert.Same(boom, thrown);
            Assert.Equal(2, resetRecorder.Requests.Count);
        }
    }

    /// <summary><b>التَسجيلُ</b>: اعتِمادٌ ناقِصٌ يَرمي عِندَ الحَلِّ بِرِسالَتِه.</summary>
    [Fact]
    public async Task The_registration_refuses_missing_credentials_at_resolution()
    {
        foreach (var configure in new Action<NafathOptions>[] { o => o.ClientSecret = ClientSecret, o => o.ClientId = ClientId })
        {
            var (services, _) = Nafath(configure);
            await using var _s = services;
            var ex = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<INafathChannel>());
            Assert.Equal("Nafath ClientId/ClientSecret مَطلوبَة.", ex.Message);
        }
    }

    // ═══ مَخزَنُ الكائِنات: مُستَمِعٌ مَحَلِّيٌّ يَقومُ مَقامَ R2 ════════════════════════════════════════════════════════════

    internal const string Bucket = "wasayel-char";
    internal const string AccessKey = "AKIACHARACTERIZATION";
    internal const string PublicBase = "https://files.wasayel.test";

    /// <summary>طَلَبٌ كَما بَلَغَ مَخزَنَ الكائِناتِ المُزَيَّف — والرُؤوسُ بِأَسمائِها، وما يَتَغَيَّرُ بِالوَقتِ أَو بِالمُحاوَلَةِ يُقرَأُ مِن <see cref="Headers"/>.</summary>
    internal sealed record S3Seen(string Method, string RawUrl, string HeaderNames, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    /// <summary>
    /// <para>مَخزَنُ كائِناتٍ مُزَيَّفٌ عَلى <c>127.0.0.1</c> بِمَنفَذٍ مِن 5200–5249: <c>PUT</c> يَحفَظُ، و<c>HEAD</c>/<c>GET</c> يُجيبانِ مِمّا حُفِظَ أَو 404، و<c>DELETE</c> ‏204 —
    /// أَو حالَةٌ مَفروضَةٌ لِفِعلٍ بِعَينِه. ويُسَجِّلُ كُلَّ طَلَبٍ بِرُؤوسِه وجِسمِه.</para>
    /// </summary>
    internal sealed class FakeS3 : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public int Port { get; }
        public string Url => $"http://127.0.0.1:{Port}";
        public Dictionary<string, HttpStatusCode> Forced { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public List<S3Seen> Requests { get; } = new();

        public FakeS3()
        {
            HttpListener? bound = null;
            for (var port = 5200; port <= 5249 && bound is null; port++)
            {
                var candidate = new HttpListener();
                candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                try
                {
                    using (var probe = new TcpListener(IPAddress.Loopback, port)) { probe.Start(); probe.Stop(); }
                    candidate.Start();
                    bound = candidate;
                    Port = port;
                }
                catch (Exception e) when (e is SocketException or HttpListenerException) { candidate.Close(); }
            }
            _listener = bound ?? throw new InvalidOperationException("لا مَنفَذَ حُرٌّ في 5200–5249 لِمَخزَنِ الكائِناتِ المُزَيَّف.");
            _loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                try { await ServeAsync(ctx); }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        }

        private async Task ServeAsync(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            using var body = new MemoryStream();
            await req.InputStream.CopyToAsync(body);
            var headers = req.Headers.AllKeys.Where(k => k is not null).ToDictionary(k => k!.ToLowerInvariant(), k => req.Headers[k]!, StringComparer.Ordinal);
            var seen = new S3Seen(req.HttpMethod, req.RawUrl ?? "", string.Join(",", headers.Keys.Order(StringComparer.Ordinal)), headers, body.ToArray());
            lock (Requests) Requests.Add(seen);

            var key = Uri.UnescapeDataString((req.Url?.AbsolutePath ?? "/").TrimStart('/'));
            var res = ctx.Response;
            if (Forced.TryGetValue(req.HttpMethod, out var forced))
            {
                res.StatusCode = (int)forced;
                var error = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>Forced{(int)forced}</Code><Message>forced</Message></Error>");
                res.ContentType = "application/xml";
                if (req.HttpMethod != "HEAD") await res.OutputStream.WriteAsync(error);
                res.Close();
                return;
            }
            switch (req.HttpMethod)
            {
                case "PUT":
                    lock (Objects) Objects[key] = Decoded(seen);
                    res.StatusCode = 200;
                    res.AddHeader("ETag", "\"char-etag\"");
                    break;
                case "HEAD":
                case "GET":
                    byte[]? found;
                    lock (Objects) found = Objects.GetValueOrDefault(key);
                    if (found is null) { res.StatusCode = 404; break; }
                    res.StatusCode = 200;
                    res.ContentType = "application/octet-stream";
                    res.ContentLength64 = found.Length;
                    res.AddHeader("ETag", "\"char-etag\"");
                    if (req.HttpMethod == "GET") await res.OutputStream.WriteAsync(found);
                    break;
                case "DELETE":
                    lock (Objects) Objects.Remove(key);
                    res.StatusCode = 204;
                    break;
                default:
                    res.StatusCode = 400;
                    break;
            }
            res.Close();
        }

        /// <summary>الجِسمُ كَما يُحفَظ: إن جاءَ مُقَطَّعاً مُوَقَّعاً (<c>aws-chunked</c>) فَبايتاتُه بِلا تَوقيعاتِ القِطَع.</summary>
        internal static byte[] Decoded(S3Seen seen)
        {
            if (!seen.Headers.TryGetValue("content-encoding", out var enc) || !enc.Contains("aws-chunked", StringComparison.Ordinal)) return seen.Body;
            var payload = new MemoryStream();
            var at = 0;
            while (at < seen.Body.Length)
            {
                var eol = IndexOf(seen.Body, "\r\n"u8, at);
                var head = Encoding.ASCII.GetString(seen.Body, at, eol - at);
                var size = Convert.ToInt32(head.Split(';')[0], 16);
                at = eol + 2;
                if (size == 0) break;
                payload.Write(seen.Body, at, size);
                at += size + 2;
            }
            return payload.ToArray();
        }

        private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int from)
        {
            var i = haystack.AsSpan(from).IndexOf(needle);
            return i < 0 ? haystack.Length : from + i;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { }
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    internal static void AddS3(IServiceCollection services, FakeS3 s3, Action<S3FileStorageOptions>? tweak = null)
        => services.AddS3FileStorage(o =>
        {
            o.Endpoint = s3.Url; o.Bucket = Bucket; o.AccessKeyId = AccessKey; o.SecretAccessKey = "char-secret-0"; o.PublicBaseUrl = PublicBase;
            tweak?.Invoke(o);
        });

    private static ServiceProvider S3Services(FakeS3 s3, Action<S3FileStorageOptions>? tweak = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        AddS3(services, s3, tweak);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// <para><b>شَكلُ الطَلَبِ المُوَقَّع</b> (يُقاسُ لِكُلِّ فِعل): الدَلوُ في المَسار، وتَوقيعُ ‏SigV4 بِنِطاقِ <c>auto/s3</c> ومِفتاحِ الاعتِماد، ورُؤوسُ الـSDK بِأَسمائِها؛
    /// وما يَتَغَيَّرُ بِالوَقتِ (التاريخُ والتَوقيعُ ومُعَرِّفُ النِداءِ ووَكيلُ المُستَخدِم) يُقرَأُ شَكلُه لا قيمَتُه.</para>
    /// </summary>
    internal static void AssertSigned(S3Seen seen, string method, string rawUrl, string headerNames, string signedHeaders)
    {
        Assert.Equal((method, rawUrl, headerNames), (seen.Method, seen.RawUrl, seen.HeaderNames));
        var auth = seen.Headers["authorization"];
        var date = seen.Headers["x-amz-date"];
        Assert.Matches("^[0-9]{8}T[0-9]{6}Z$", date);
        Assert.StartsWith($"AWS4-HMAC-SHA256 Credential={AccessKey}/{date[..8]}/auto/s3/aws4_request, SignedHeaders={signedHeaders}, Signature=", auth, StringComparison.Ordinal);
        Assert.Matches("Signature=[0-9a-f]{64}$", auth);
        Assert.Equal($"127.0.0.1:{seen.Headers["host"].Split(':')[1]}", seen.Headers["host"]);
    }

    private void Print(IEnumerable<S3Seen> requests)
    {
        foreach (var r in requests)
            output.WriteLine($"[s3] {r.Method} {r.RawUrl} · رُؤوس [{r.HeaderNames}] · {string.Join(" · ", r.Headers.Where(h => h.Key is not ("authorization" or "x-amz-date" or "amz-sdk-invocation-id" or "user-agent" or "host")).Select(h => $"{h.Key}={h.Value}"))} · جِسم {r.Body.Length}");
    }

    private const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    /// <summary>
    /// <para><b>الوُجودُ والقِراءَةُ والحَذف</b> (تَواقيعُها لا تَتَغَيَّر): الوُجودُ <c>HEAD</c> ⇐ ‏200 <c>true</c> و404 <c>false</c>؛ والقِراءَةُ <c>GET</c> ⇐ البايتاتُ نَفسُها، و404 ⇐
    /// <c>null</c>؛ والحَذفُ يَسأَلُ الوُجودَ أَوَّلاً ثُمَّ <c>DELETE</c> ⇐ <c>true</c>، وما لَم يَكُن ⇐ <c>HEAD</c> وَحدَه و<c>false</c>. والمِفتاحُ يُطَهَّرُ قَبلَ كُلِّ فِعل.</para>
    /// </summary>
    [Fact]
    public async Task Exists_read_and_delete_reach_the_store_signed_by_path_and_answer_as_before()
    {
        await using var s3 = new FakeS3();
        await using var services = S3Services(s3);
        var files = services.GetRequiredService<IFileStorage>();
        var key = $"tenants/{FreshSlug()}/listings/{Guid.NewGuid()}/0.jpg";
        var bytes = Encoding.UTF8.GetBytes("characterization-object-bytes");
        lock (s3.Objects) s3.Objects[$"{Bucket}/{key}"] = bytes;

        Assert.True(await files.ExistsAsync("/" + key));
        await using (var read = await files.ReadAsync("../" + key))
        {
            using var copy = new MemoryStream();
            await read!.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }
        Assert.True(await files.DeleteAsync(key.Replace('/', '\\')));
        Assert.False(await files.ExistsAsync(key));
        Assert.Null(await files.ReadAsync(key));
        Assert.False(await files.DeleteAsync(key));
        Print(s3.Requests);

        Assert.Equal(new[] { "HEAD", "GET", "HEAD", "DELETE", "HEAD", "GET", "HEAD" }, s3.Requests.Select(r => r.Method));
        Assert.All(s3.Requests, r => Assert.Equal($"/{Bucket}/{key}", r.RawUrl));
        const string names = "amz-sdk-invocation-id,amz-sdk-request,authorization,host,user-agent,x-amz-content-sha256,x-amz-date";
        AssertSigned(s3.Requests[0], "HEAD", $"/{Bucket}/{key}", names, "host;x-amz-content-sha256;x-amz-date");
        AssertSigned(s3.Requests[1], "GET", $"/{Bucket}/{key}", names, "host;x-amz-content-sha256;x-amz-date");
        AssertSigned(s3.Requests[3], "DELETE", $"/{Bucket}/{key}", names, "host;x-amz-content-sha256;x-amz-date");
        Assert.All(s3.Requests, r => Assert.Equal((EmptySha, "attempt=1; max=3", 0), (r.Headers["x-amz-content-sha256"], r.Headers["amz-sdk-request"], r.Body.Length)));
        Assert.Equal($"{PublicBase}/{key}", await files.GetPublicUrlAsync("/../" + key));
        Assert.Equal(7, s3.Requests.Count);
    }

    /// <summary><b>عُطلُ المَخزَنِ في القِراءَة</b>: ‏500 يُعادُ بِسِياسَةِ الـSDK (ثَلاثُ مُحاوَلاتٍ بِرَأسِ <c>amz-sdk-request</c>) ثُمَّ <c>AmazonS3Exception</c> يَخرُجُ كَما هُوَ؛
    /// و403 مُحاوَلَةٌ واحِدَةٌ ويَخرُجُ مِن الوُجودِ كَذلك.</summary>
    [Fact]
    public async Task A_store_failure_on_read_is_retried_by_the_sdk_and_surfaces_unchanged()
    {
        await using var s3 = new FakeS3();
        await using var services = S3Services(s3);
        var files = services.GetRequiredService<IFileStorage>();
        var key = $"tenants/{FreshSlug()}/avatars/{Guid.NewGuid()}.png";

        s3.Forced["GET"] = HttpStatusCode.InternalServerError;
        var read = await Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(() => files.ReadAsync(key));
        output.WriteLine($"[s3·500] {read.GetType().Name} {(int)read.StatusCode} · مُحاوَلات {string.Join(", ", s3.Requests.Select(r => r.Headers["amz-sdk-request"]))}");
        Assert.Equal(HttpStatusCode.InternalServerError, read.StatusCode);
        Assert.Equal(new[] { "attempt=1; max=3", "attempt=2; max=3", "attempt=3; max=3" },
                     s3.Requests.Select(r => r.Headers["amz-sdk-request"]));

        s3.Requests.Clear();
        s3.Forced.Clear();
        s3.Forced["HEAD"] = HttpStatusCode.Forbidden;
        var exists = await Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(() => files.ExistsAsync(key));
        output.WriteLine($"[s3·403] {exists.GetType().Name} {(int)exists.StatusCode} · مُحاوَلات {s3.Requests.Count}");
        Assert.Equal((HttpStatusCode.Forbidden, 1), (exists.StatusCode, s3.Requests.Count));
    }

    /// <summary><b>التَسجيلُ</b>: مُفرَدٌ واحِدٌ بِاسمِ مُزَوِّدِه، ومِفتاحٌ ناقِصٌ يَرمي عِندَ الحَلِّ بِاسمِه.</summary>
    [Fact]
    public async Task The_registration_is_one_singleton_and_names_a_missing_key()
    {
        await using var s3 = new FakeS3();
        await using (var services = S3Services(s3))
        {
            var files = services.GetRequiredService<IFileStorage>();
            Assert.Same(files, services.GetRequiredService<IFileStorage>());
            Assert.Equal(("S3FileStorage", "S3"), (files.GetType().Name, files.ProviderName));
        }
        await using (var missing = S3Services(s3, o => o.PublicBaseUrl = ""))
        {
            var ex = Assert.Throws<FileStorageException>(() => missing.GetRequiredService<IFileStorage>());
            Assert.Equal("مَخزَنُ S3 غَير مُهَيَّأ: `Files:S3:PublicBaseUrl` فارِغ.", ex.Message);
        }
        Assert.Empty(s3.Requests);
    }

    // ═══ الإشعارُ الفَوريّ: Web Push ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>طَلَبٌ كَما بَلَغَ خِدمَةَ الإشعار.</summary>
    internal sealed record PushSeen(string Method, string Uri, string RequestHeaders, IReadOnlyDictionary<string, string> Headers, string ContentHeaders, byte[] Body);

    /// <summary>مُعالِجُ خِدمَةِ الإشعار: يُجيبُ بِحالَةٍ أَو يَرمي، ويُسَجِّل.</summary>
    internal sealed class PushRecorder : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public Exception? Throw { get; set; }
        public List<PushSeen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            lock (Requests) Requests.Add(new PushSeen(request.Method.Method, request.RequestUri!.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.Ordinal),
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                body));
            if (Throw is not null) throw Throw;
            return new HttpResponseMessage(Status);
        }
    }

    /// <summary>سَطرُ لوغٍ كَما كَتَبَته الخِدمَة.</summary>
    internal sealed class CapturedLog<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : $" ⟵ {exception.GetType().Name}: {exception.Message}")}");
        }
    }

    /// <summary>مُشتَرِكُ مُتَصَفِّحٍ حَقيقِيُّ المَفاتيح: زَوجُ P-256 وسِرُّ ‏16 بايتاً — فَيُفَكُّ ما يُرسَلُ إلَيه.</summary>
    internal sealed class Subscriber : IDisposable
    {
        public ECDiffieHellman Key { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public byte[] Public { get; }
        public byte[] Auth { get; } = RandomNumberGenerator.GetBytes(16);
        public string Endpoint { get; } = "https://fcm.googleapis.com/fcm/send/char-" + Guid.NewGuid().ToString("N");

        public Subscriber()
        {
            var q = Key.ExportParameters(false).Q;
            Public = [0x04, .. q.X!, .. q.Y!];
        }

        public PushSubscription Row(string? endpoint = null) => new()
        {
            Endpoint = endpoint ?? Endpoint, P256dh = B64Url(Public), Auth = B64Url(Auth), CreatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        /// <summary>فَكُّ <c>aesgcm</c> (مُسَوَّدَةُ ‏draft-ietf-webpush-encryption-04 الَّتي تُرسِلُها الحُزمَة): ‏ECDH ثُمَّ HKDF بِسِرِّ المُشتَرِك، ثُمَّ مِفتاحُ المُحتَوى
        /// والرَقمُ الوَحيدُ بِالمِلحِ والسِياق، ثُمَّ AES-GCM؛ وأَوَّلُ بايتَينِ طولُ الحَشو.</summary>
        public byte[] Decrypt(byte[] serverPublic, byte[] salt, byte[] body)
        {
            using var server = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] },
            });
            var shared = Key.DeriveRawSecretAgreement(server.PublicKey);
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 32, Auth, "Content-Encoding: auth\0"u8.ToArray());
            byte[] context = [.. "P-256\0"u8, 0, 65, .. Public, 0, 65, .. serverPublic];
            var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, [.. "Content-Encoding: aesgcm\0"u8, .. context]);
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, [.. "Content-Encoding: nonce\0"u8, .. context]);
            var plain = new byte[body.Length - 16];
            using var aes = new AesGcm(cek, 16);
            aes.Decrypt(nonce, body[..^16], body[^16..], plain);
            var pad = (plain[0] << 8) | plain[1];
            return plain[(2 + pad)..];
        }

        public void Dispose() => Key.Dispose();
    }

    internal static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromB64Url(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t + new string('=', (4 - t.Length % 4) % 4));
    }

    /// <summary>
    /// <para>الخِدمَةُ بِبانيها القائِمِ وتَهيِئَةِ مَفاتيحِ VAPID، و<c>WebPushClient</c> فيها (الحَقلُ الوَحيدُ مِن نَوعِه) يُعطى عَميلاً عَلى المُعالِج — حَقلُ
    /// <c>_httpClient</c> في الحُزمَة (‏1.0.13) يُقرَأُ قَبلَ أَيِّ إنشاءٍ داخِلِيّ. وغِيابُ أَيٍّ مِنهُما أَداةٌ عَمياء.</para>
    /// </summary>
    internal static WebPushService Push(PushRecorder recorder, (string Public, string Private) vapid, ILogger<WebPushService> log)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebPush:Subject"] = "mailto:push@wasayel.test",
            ["WebPush:PublicKey"] = vapid.Public,
            ["WebPush:PrivateKey"] = vapid.Private,
        }).Build();
        var push = new WebPushService(cfg, log);
        Wire(push, recorder);
        return push;
    }

    /// <summary>يُعطي <c>WebPushClient</c> الخِدمَةِ عَميلاً عَلى المُعالِج.</summary>
    internal static void Wire(WebPushService push, HttpMessageHandler recorder)
    {
        var field = typeof(WebPushService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(WebPush.WebPushClient));
        var client = (WebPush.WebPushClient)field.GetValue(push)!;
        var inner = typeof(WebPush.WebPushClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("أَداةٌ عَمياء: WebPushClient بِلا _httpClient.");
        inner.SetValue(client, new HttpClient(recorder));
    }

    private static readonly Guid Buyer = Guid.NewGuid();

    private static Deal AcceptedDeal() => new()
    {
        Id = Guid.NewGuid(), Pattern = "marketplace", AmountSar = 150m, ListingTitle = "سِلعَةُ التَوصيف",
        InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Guid.NewGuid(), CounterpartyName = "بائِع",
    };

    private static IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub> Hub()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        return services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
    }

    private sealed record PushRun(string Slug, Deal Deal, DocWorld World, PushRecorder Recorder, CapturedLog<WebPushService> Log, string Payload, (string Public, string Private) Vapid);

    /// <summary>بابُ قَبولِ الصَفقَة (<c>DealNotices.NotifyAcceptedAsync</c>) لِمُشتَرٍ بِاشتِراكاتِه في عالَمِ الوَثائِق — والحُمولَةُ المُتَوَقَّعَةُ بِحَرفِ ما تَبنيه الخِدمَة.</summary>
    private static async Task<PushRun> AcceptedAsync(IReadOnlyList<PushSubscription> subscriptions, Action<PushRecorder>? arrange = null)
    {
        var l = new ACommerce.Platform.I18n.L(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        var vapid = (keys.PublicKey, keys.PrivateKey);
        var recorder = new PushRecorder();
        arrange?.Invoke(recorder);
        var log = new CapturedLog<WebPushService>();
        var push = Push(recorder, vapid, log);
        var world = new DocWorld();
        var slug = FreshSlug();
        var deal = AcceptedDeal();
        world.Put(new User { Id = Buyer, TenantSlug = slug, FullName = "مُشتَرٍ", PushSubscriptions = [.. subscriptions] });
        await new DealNotices(world.Store, Hub(), push).NotifyAcceptedAsync(slug, deal, "بائِعُ التَوصيف", l, CancellationToken.None);
        var payload = JsonSerializer.Serialize(new
        {
            title = l["notifications.deal_accepted.title"],
            body = DealNotices.AcceptedPushBody(deal, "بائِعُ التَوصيف", l),
            url = $"/{slug}/deals/{deal.Id}",
            icon = $"/api/{slug}/icon.svg",
            badge = $"/api/{slug}/icon.svg",
            tag = $"deal-{deal.Id}",
        });
        return new PushRun(slug, deal, world, recorder, log, payload, vapid);
    }

    private void Print(PushRun run)
    {
        foreach (var r in run.Recorder.Requests)
            output.WriteLine($"[push] {r.Method} {r.Uri} · رُؤوس [{r.RequestHeaders}] · المُحتَوى [{r.ContentHeaders}] · جِسم {r.Body.Length}");
        foreach (var line in run.Log.Lines) output.WriteLine($"[push·log] {line}");
    }

    /// <summary>مَحفوظاتُ المُستَخدِمِ بَعدَ البابِ — ما كَتَبَه المَحوُ (أَو لا شَيء).</summary>
    private static IReadOnlyList<User> UserWrites(DocWorld world) => world.Stored.OfType<User>().ToList();

    /// <summary>
    /// <para><b>ما يَبلُغُ خِدمَةَ الإشعارِ بايتاً</b>: <c>POST</c> إلى عُنوانِ الاشتِراكِ بِرُؤوسِ <c>TTL</c> (‏28 يَوماً) و<c>Authorization: WebPush &lt;JWT&gt;</c> (رَأسُه
    /// ES256، ومُطالَبَتُه الجُمهورُ أَصلَ الخِدمَةِ ومَوضوعُ VAPID وانقِضاءٌ بَعدَ اثنَتَي عَشرَةَ ساعَة، وتَوقيعُه يَصِحُّ بِالمِفتاحِ العامّ) و<c>Crypto-Key: dh=…;p256ecdsa=&lt;VAPID&gt;</c>
    /// و<c>Encryption: salt=…</c>، ومُحتَوىً <c>aesgcm</c> ثُمانِيٌّ — يُفَكُّ بِمِفتاحِ المُشتَرِكِ فَيَكونُ الحُمولَةَ بِحَرفِها. و201 ⇐ لا مَحوَ ولا لوغ.</para>
    /// </summary>
    [Fact]
    public async Task The_accepted_notice_reaches_the_push_service_as_an_aesgcm_body_that_decrypts_to_the_payload()
    {
        using var subscriber = new Subscriber();
        var run = await AcceptedAsync([subscriber.Row()]);
        Print(run);
        var seen = Assert.Single(run.Recorder.Requests);
        Assert.Equal(("POST", subscriber.Endpoint, "Authorization,Crypto-Key,Encryption,TTL"), (seen.Method, seen.Uri, seen.RequestHeaders));
        Assert.Equal("2419200", seen.Headers["TTL"]);
        Assert.Equal($"Content-Encoding: aesgcm,Content-Length: {seen.Body.Length},Content-Type: application/octet-stream", seen.ContentHeaders);

        var cryptoKey = seen.Headers["Crypto-Key"].Split(';');
        Assert.Equal(2, cryptoKey.Length);
        Assert.StartsWith("dh=", cryptoKey[0], StringComparison.Ordinal);
        Assert.Equal($"p256ecdsa={run.Vapid.Public}", cryptoKey[1]);
        Assert.StartsWith("salt=", seen.Headers["Encryption"], StringComparison.Ordinal);
        var serverPublic = FromB64Url(cryptoKey[0]["dh=".Length..]);
        var salt = FromB64Url(seen.Headers["Encryption"]["salt=".Length..]);
        Assert.Equal((65, 0x04, 16), (serverPublic.Length, (int)serverPublic[0], salt.Length));

        var plain = Encoding.UTF8.GetString(subscriber.Decrypt(serverPublic, salt, seen.Body));
        output.WriteLine($"[push] المَفكوك ({plain.Length} حَرفاً): {plain}");
        Assert.Equal(run.Payload, plain);
        Assert.Equal(Encoding.UTF8.GetByteCount(run.Payload) + 2 + 16, seen.Body.Length);

        Assert.StartsWith("WebPush ", seen.Headers["Authorization"], StringComparison.Ordinal);
        var jwt = seen.Headers["Authorization"]["WebPush ".Length..].Split('.');
        Assert.Equal(3, jwt.Length);
        Assert.Equal("{\"typ\":\"JWT\",\"alg\":\"ES256\"}", Encoding.UTF8.GetString(FromB64Url(jwt[0])));
        using var claims = JsonDocument.Parse(FromB64Url(jwt[1]));
        Assert.Equal(new[] { "aud", "exp", "sub" }, claims.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("https://fcm.googleapis.com", "mailto:push@wasayel.test"),
                     (claims.RootElement.GetProperty("aud").GetString(), claims.RootElement.GetProperty("sub").GetString()));
        var exp = claims.RootElement.GetProperty("exp").GetInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(exp, 12 * 3600 - 120, 12 * 3600 + 5);
        var vapidPublic = FromB64Url(run.Vapid.Public);
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = vapidPublic[1..33], Y = vapidPublic[33..65] } });
        Assert.True(ecdsa.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), FromB64Url(jwt[2]), HashAlgorithmName.SHA256));

        Assert.Empty(UserWrites(run.World));
        Assert.DoesNotContain(run.Log.Lines, l => l.StartsWith("Warning", StringComparison.Ordinal));
    }

    /// <summary>
    /// <para><b>جَوابُ الخِدمَةِ ومَصيرُ الاشتِراك</b>: ‏410 و404 ⇐ يُمحى الاشتِراكُ مِن قِراءَةٍ جَديدَةٍ بِلا لوغ؛ و400 و429 و500 واستِثناءُ النَقلِ ⇐ يَبقى ويُكتَبُ سَطرُ
    /// تَحذيرٍ واحِدٌ بِاسمِ المُستَخدِمِ والاستِثناء؛ وفي كُلِّها طَلَبٌ واحِدٌ والبابُ يَعودُ بِلا رَمي.</para>
    /// </summary>
    [Theory]
    [InlineData(410, true, "")]
    [InlineData(404, true, "")]
    [InlineData(400, false, "WebPushException")]
    [InlineData(429, false, "WebPushException")]
    [InlineData(500, false, "WebPushException")]
    [InlineData(0, false, "HttpRequestException")]
    public async Task The_push_service_answer_decides_whether_the_subscription_is_erased(int status, bool erased, string logged)
    {
        using var subscriber = new Subscriber();
        var run = await AcceptedAsync([subscriber.Row()], r =>
        {
            if (status == 0) r.Throw = new HttpRequestException("connection reset by peer");
            else r.Status = (HttpStatusCode)status;
        });
        Print(run);
        Assert.Single(run.Recorder.Requests);
        var writes = UserWrites(run.World);
        var warnings = run.Log.Lines.Where(l => l.StartsWith("Warning", StringComparison.Ordinal)).ToList();
        if (erased)
        {
            var user = Assert.Single(writes);
            Assert.Empty(user.PushSubscriptions);
            Assert.Empty(warnings);
        }
        else
        {
            Assert.Empty(writes);
            var line = Assert.Single(warnings);
            Assert.StartsWith($"Warning: Push send failed for user {Buyer} ⟵ {logged}:", line, StringComparison.Ordinal);
        }
    }

    /// <summary><b>الاشتِراكاتُ بِتَرتيبِها</b>: اثنانِ مَقبولانِ ⇐ طَلَبانِ بِتَرتيبِ القائِمَة كُلٌّ يُفَكُّ بِمِفتاحِه؛ وصَفٌّ لا يَقبَلُه البابُ ⇐ لا طَلَبَ ويُمحى؛ وبِلا اشتِراكٍ ⇐ لا شَيء.</summary>
    [Fact]
    public async Task Each_admitted_subscription_is_sent_in_order_and_an_unadmitted_row_is_erased_unsent()
    {
        using var a = new Subscriber();
        using var b = new Subscriber();
        var bad = a.Row("https://evil.example/push/" + Guid.NewGuid().ToString("N"));
        var run = await AcceptedAsync([a.Row(), bad, b.Row()]);
        Print(run);
        Assert.Equal(new[] { a.Endpoint, b.Endpoint }, run.Recorder.Requests.Select(r => r.Uri));
        foreach (var (seen, who) in run.Recorder.Requests.Zip(new[] { a, b }))
        {
            var dh = FromB64Url(seen.Headers["Crypto-Key"].Split(';')[0]["dh=".Length..]);
            var salt = FromB64Url(seen.Headers["Encryption"]["salt=".Length..]);
            Assert.Equal(run.Payload, Encoding.UTF8.GetString(who.Decrypt(dh, salt, seen.Body)));
        }
        var user = Assert.Single(UserWrites(run.World));
        Assert.Equal(new[] { a.Endpoint, b.Endpoint }, user.PushSubscriptions.Select(p => p.Endpoint));

        var none = await AcceptedAsync([]);
        Assert.Empty(none.Recorder.Requests);
        Assert.Empty(UserWrites(none.World));
    }
}

// ═══ الرَفعُ بِبابَيه عَلى القاعِدَة — صورَةُ المَلَفِّ الشَخصِيّ وصُوَرُ الإعلان (‏2026-11-12، W13-6·١٣) ═══════════════════════════════════════════
//
// البابانِ اللَذانِ يُنادِيانِ `IFileStorage.UploadAsync` في وَقتِ التَشغيل (`/{slug}/me/save` و`/{slug}/listings/create`) يَحتاجانِ المَخزَنَ والجَلسَةَ والاستِحقاق،
// فَيُشَغَّلانِ عَلى المُضيفِ المُرَكَّبِ (`LiveTemplateDoorHost`) بِمَخزَنِ S3 المُسَجَّلِ بِـ`AddS3FileStorage` وعُنوانُه المُستَمِعُ المُزَيَّف. والمُثَبَّت: ما بَلَغَ المَخزَنَ
// (الفِعلُ والمَسارُ والرُؤوسُ والبايتات)، والرابِطُ الَّذي حُفِظَ، وما يَفعَلُه البابُ حينَ يَسقُطُ المَخزَن.

[Collection(LiveProofCollection.Name)]
public class LiveS3UploadRequestShapeCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string NewSlug() => "w13s-" + Guid.NewGuid().ToString("N")[..8];

    private static string NewPhone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>بايتاتُ صورَةٍ ثابِتَةٌ بِطولٍ مَعلوم — تَبدَأُ بِتَوقيعِ نَوعِها.</summary>
    private static byte[] Image(byte[] magic, int length)
    {
        var bytes = new byte[length];
        magic.CopyTo(bytes, 0);
        for (var i = magic.Length; i < length; i++) bytes[i] = (byte)(i * 31 % 251);
        return bytes;
    }


    private void Print(NafathS3PushRequestShapeCharacterizationTests.FakeS3 s3)
    {
        foreach (var r in s3.Requests)
            output.WriteLine($"[s3·put] {r.Method} {r.RawUrl} · رُؤوس [{r.HeaderNames}] · {string.Join(" · ", r.Headers.Where(h => h.Key is not ("authorization" or "x-amz-date" or "amz-sdk-invocation-id" or "user-agent" or "host")).Select(h => $"{h.Key}={h.Value}"))} · "
                             + $"{System.Text.RegularExpressions.Regex.Replace(r.Headers["authorization"], "[0-9a-f]{64}", "…")} · جِسم {r.Body.Length}");
    }

    private static async Task<(LiveTemplateDoorHost Host, string Slug, User Member, string Token)> StoreAsync(NafathS3PushRequestShapeCharacterizationTests.FakeS3 s3)
    {
        var host = LiveTemplateDoorHost.Build(services: s => NafathS3PushRequestShapeCharacterizationTests.AddS3(s, s3), throughResolver: true);
        var slug = NewSlug();
        await using (var s = host.Store.LightweightSession()) { s.Insert(new Tenant { Id = slug, Name = "مَتجَرُ الرَفع", OwnerUserId = Guid.NewGuid() }); await s.SaveChangesAsync(); }
        var member = new User
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوُ الرَفع", Phone = NewPhone(), PhoneVerified = true,
            AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
        };
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(member); await s.SaveChangesAsync(); }
        return (host, slug, member, AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion));
    }

    private static async Task<User> ReloadAsync(LiveTemplateDoorHost host, string slug, Guid id)
    {
        await using var q = host.Store.QuerySession(slug);
        return (await q.LoadAsync<User>(id))!;
    }

    /// <summary>
    /// <para><b>صورَةُ المَلَفِّ الشَخصِيّ</b> (<c>POST /{slug}/me/save</c>): <c>PUT</c> واحِدٌ إلى <c>/{bucket}/tenants/{slug}/avatars/{userId}.{ext}</c> (<c>jpeg</c> ⇐
    /// <c>jpg</c>) بِنَوعِ الصورَةِ وطولِها وبايتاتِها، والرابِطُ المَحفوظُ مِن الجَذرِ العامّ، والاسمُ يُحفَظ، والتَحويلُ إلى <c>/me</c>. ثُمَّ مَخزَنٌ يُجيبُ 500 ⇐ تُعادُ
    /// المُحاوَلَةُ بِسِياسَةِ الـSDK، والاسمُ يُحفَظُ والصورَةُ لا، والتَحويلُ نَفسُه.</para>
    /// </summary>
    [Fact]
    public async Task The_profile_door_puts_one_signed_object_per_avatar_and_keeps_the_name_when_the_store_fails()
    {
        if (!Enabled) return;
        await using var s3 = new NafathS3PushRequestShapeCharacterizationTests.FakeS3();
        var (host, slug, member, token) = await StoreAsync(s3);
        await using var _ = host;
        var route = new Dictionary<string, string> { ["slug"] = slug };
        var png = Image([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], 1531);

        var saved = await host.PostMultipartAsync("/{slug}/me/save", route, slug, token, [("fullName", "اسمٌ بَعدَ الرَفع")], ("avatar", "me.png", "image/png", png));
        Print(s3);
        var key = $"tenants/{slug}/avatars/{member.Id}.png";
        var user = await ReloadAsync(host, slug, member.Id);
        var named = LiveProofRedaction.Of(user.FullName);
        var linked = user.AvatarUrl == $"{NafathS3PushRequestShapeCharacterizationTests.PublicBase}/{key}";
        output.WriteLine($"[s3·avatar] ⇒ {saved} · الاسم {named} · الرابِطُ مِن الجَذرِ العامِّ بِمِفتاحِه {linked}");
        Assert.Equal((302, $"/{slug}/me"), saved);
        var put = Assert.Single(s3.Requests);
        AssertPut(put, key, "image/png", png);
        Assert.Equal(("اسمٌ بَعدَ الرَفع", $"{NafathS3PushRequestShapeCharacterizationTests.PublicBase}/{key}"), (user.FullName, user.AvatarUrl));

        s3.Requests.Clear();
        var jpeg = Image([0xFF, 0xD8, 0xFF, 0xE0], 777);
        await host.PostMultipartAsync("/{slug}/me/save", route, slug, token, [("fullName", "اسمٌ ثانٍ")], ("avatar", "me.jpeg", "image/jpeg", jpeg));
        AssertPut(Assert.Single(s3.Requests), $"tenants/{slug}/avatars/{member.Id}.jpg", "image/jpeg", jpeg);

        s3.Requests.Clear();
        s3.Forced["PUT"] = HttpStatusCode.InternalServerError;
        var failed = await host.PostMultipartAsync("/{slug}/me/save", route, slug, token, [("fullName", "اسمٌ رَغمَ المَخزَن")], ("avatar", "me.png", "image/png", png));
        var after = await ReloadAsync(host, slug, member.Id);
        var renamed = LiveProofRedaction.Of(after.FullName);
        var kept = after.AvatarUrl?.EndsWith(".jpg", StringComparison.Ordinal);
        output.WriteLine($"[s3·avatar·500] ⇒ {failed} · مُحاوَلات {string.Join(", ", s3.Requests.Select(r => r.Headers["amz-sdk-request"]))} · الاسم {renamed} · الرابِطُ السابِقُ باقٍ {kept}");
        Assert.Equal((302, $"/{slug}/me"), failed);
        Assert.Equal(new[] { "attempt=1; max=3", "attempt=2; max=3", "attempt=3; max=3" },
                     s3.Requests.Select(r => r.Headers["amz-sdk-request"]));
        Assert.All(s3.Requests, r => AssertPut(r, key, "image/png", png, attemptHeader: false));
        Assert.Equal(("اسمٌ رَغمَ المَخزَن", $"{NafathS3PushRequestShapeCharacterizationTests.PublicBase}/tenants/{slug}/avatars/{member.Id}.jpg"), (after.FullName, after.AvatarUrl));
    }

    /// <summary>
    /// <para><b>صُوَرُ الإعلان</b> (<c>POST /{slug}/listings/create</c>): <c>PUT</c> لِكُلِّ صورَةٍ بِتَرتيبِها إلى <c>/{bucket}/tenants/{slug}/listings/{id}/{n}.{ext}</c> بِبايتاتِها،
    /// والإعلانُ يَحمِلُ روابِطَها مِن الجَذرِ العامِّ بِالتَرتيبِ نَفسِه.</para>
    /// </summary>
    [Fact]
    public async Task The_publish_door_puts_each_photo_in_order_and_the_listing_carries_their_public_links()
    {
        if (!Enabled) return;
        await using var s3 = new NafathS3PushRequestShapeCharacterizationTests.FakeS3();
        var (host, slug, _, token) = await StoreAsync(s3);
        await using var _h = host;
        var jpg = Image([0xFF, 0xD8, 0xFF, 0xE0], 2048);
        var webp = Image([0x52, 0x49, 0x46, 0x46], 513);
        var created = await host.PostMultipartAsync("/{slug}/listings/create", new Dictionary<string, string> { ["slug"] = slug }, slug, token,
            [("title", "إعلانٌ بِصورَتَين"), ("category", "general"), ("price", "120"), ("description", "إعلانُ الرَفع")],
            ("photos", "a.jpg", "image/jpeg", jpg), ("photos", "b.webp", "image/webp", webp));
        Print(s3);
        Assert.Equal(302, created.Status);
        var id = Guid.Parse(created.Location[(created.Location.LastIndexOf('/') + 1)..]);
        Assert.Equal($"/{slug}/listings/{id}", created.Location);
        Assert.Equal(2, s3.Requests.Count);
        AssertPut(s3.Requests[0], $"tenants/{slug}/listings/{id}/0.jpg", "image/jpeg", jpg);
        AssertPut(s3.Requests[1], $"tenants/{slug}/listings/{id}/1.webp", "image/webp", webp);

        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select data->'MediaUrls' from platform.mt_doc_listing where id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        var media = JsonSerializer.Deserialize<string[]>((string)(await cmd.ExecuteScalarAsync())!);
        output.WriteLine($"[s3·photos] ⇒ {created} · {string.Join(", ", media!)}");
        Assert.Equal(new[] { $"{NafathS3PushRequestShapeCharacterizationTests.PublicBase}/tenants/{slug}/listings/{id}/0.jpg",
                             $"{NafathS3PushRequestShapeCharacterizationTests.PublicBase}/tenants/{slug}/listings/{id}/1.webp" }, media);
    }

    /// <summary>
    /// <para>شَكلُ الـ<c>PUT</c> (مَقيسٌ لا مُخَمَّن): الدَلوُ والمِفتاحُ في المَسار، ورُؤوسُ الـSDK بِأَسمائِها، ونَوعُ الصورَة، و<b>الجِسمُ مُقَطَّعٌ مُوَقَّع</b> —
    /// <c>content-encoding: aws-chunked</c> و<c>x-amz-content-sha256: STREAMING-AWS4-HMAC-SHA256-PAYLOAD</c> وطولُ البايتاتِ في <c>x-amz-decoded-content-length</c>
    /// و<c>expect: 100-continue</c>: قِطعَةٌ واحِدَةٌ بِطولِ البايتاتِ ثُمَّ قِطعَةٌ صِفرِيَّة، لِكُلٍّ تَوقيعُها، فَطولُ الجِسمِ البايتاتُ وَ‏174 — والبايتاتُ نَفسُها بَعدَ الفَكّ.</para>
    /// </summary>
    private static void AssertPut(NafathS3PushRequestShapeCharacterizationTests.S3Seen put, string key, string contentType, byte[] bytes, bool attemptHeader = true)
    {
        NafathS3PushRequestShapeCharacterizationTests.AssertSigned(put, "PUT", $"/{NafathS3PushRequestShapeCharacterizationTests.Bucket}/{key}",
            "amz-sdk-invocation-id,amz-sdk-request,authorization,content-encoding,content-length,content-type,expect,host,user-agent,x-amz-content-sha256,x-amz-date,x-amz-decoded-content-length",
            "content-encoding;content-length;content-type;host;x-amz-content-sha256;x-amz-date;x-amz-decoded-content-length");
        var length = bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal((contentType, "aws-chunked", "STREAMING-AWS4-HMAC-SHA256-PAYLOAD", length, (bytes.Length + 174).ToString(System.Globalization.CultureInfo.InvariantCulture), "100-continue"),
                     (put.Headers["content-type"], put.Headers["content-encoding"], put.Headers["x-amz-content-sha256"], put.Headers["x-amz-decoded-content-length"],
                      put.Headers["content-length"], put.Headers["expect"]));
        if (attemptHeader) Assert.Equal("attempt=1; max=3", put.Headers["amz-sdk-request"]);
        var text = Encoding.Latin1.GetString(put.Body);
        Assert.Matches($"^{bytes.Length:X};chunk-signature=[0-9a-f]{{64}}\\r\\n", text);
        Assert.Matches("\\r\\n0;chunk-signature=[0-9a-f]{64}\\r\\n\\r\\n$", text);
        Assert.Equal(bytes, NafathS3PushRequestShapeCharacterizationTests.FakeS3.Decoded(put));
    }
}
