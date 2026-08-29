using System.Net;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Subscriptions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ حُرّاسُ مالِ Paddle سُلوكِيّاً — النُقطَةُ تُشَغَّل، لا تُقرَأ ═════
//
// **ونَفسُ حُجَّةِ `PayPalEndpointBehaviourTests` حَرفاً**: حارِسٌ
// يُفحَص بِقِراءَةِ نَصِّ المَصدَرِ **لا يُحمِرُّ عِندَ نَزعِ `!`**.
// فَما هُنا يُشَغِّل المُوَجِّهَ والبَوّابَةَ والقارِئَ والكاتِبَ
// والمُودِعَ فِعلاً، ويَقرَأُ **الوَثيقَةَ المُخَزَّنَة** لا سَطراً في
// مِلَفّ.
//
// **وعالَمُ الوَثائِقِ مُشتَرَكٌ لا مَنسوخ** (`EndpointDocWorld.cs`):
// نَفسُ `DocWorld` ونَفسُ `MartenProxy` اللَذَينِ يَقيسانِ مَسارَ
// PayPal — فَـ«صِفرُ كِتابَة» تَعني الشَيءَ نَفسَه في المِلَفَّين.
//
// **ودَينٌ مُعلَنٌ يُقالُ بِاسمِه**: جَلسَةُ الاختِبارِ تُنَفِّذ
// `LoadAsync`/`Store`/`Insert`/`SaveChangesAsync` **ولا تُنَفِّذ
// `Query`**. فَفَرعُ «تَسوِيَةٌ بِلا `custom_data` ⇒ تُبحَثُ
// بِمُعَرِّفِ المُعامَلَة» في `PaddleFlow.FindTransactionAsync`
// **غَيرُ مَفحوصٍ هُنا** — يَفحَصُه القَرارُ النَقِيُّ بِـ
// `record: null`، وسَطراهُ في التَركيبِ يُسَدَّدانِ يَومَ يوجَد
// حِسابُ Paddle حَقيقيّ.

// ─── مُعالِجُ Paddle الوَهمِيّ — ويَعُدُّ ما لَم يُرسَل ────────────────

/// <summary><b>يُسَجِّلُ كُلَّ طَلَبٍ خارِج</b>، فَ«رُدَّت بِلا نِداءِ
/// Paddle» تَصيرُ عَدَداً لا دَعوى.</summary>
file sealed class PaddleCalls : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

    public List<string> Paths { get; } = new();

    public PaddleCalls Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.OK, "{}");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

// ─── المُضيفُ المُصَغَّر — نَفسُ النِقاطِ ونَفسُ المُوَجِّه ────────────

file sealed class PaddleHost : IAsyncDisposable
{
    public const string Secret = "pdl_ntfset_0000000000000000000000000";
    public const string ApiKey = "pdl_apikey_1111111111111111111111111";
    public const string Token  = "live_token_2222";
    public const string PayPage = "https://wasayel.example/billing/paddle/checkout.html";

    private readonly WebApplication _app;

    public HttpClient Client { get; }
    public DocWorld World { get; }
    public PaddleCalls Paddle { get; }

    private PaddleHost(WebApplication app, HttpClient client, DocWorld world, PaddleCalls paddle)
    {
        _app = app; Client = client; World = world; Paddle = paddle;
    }

    public static PaddleOptions Opts(
        string secret = Secret, string token = Token, string link = PayPage) => new()
    {
        Environment = PaddleEnvironment.Live, ApiKey = ApiKey,
        WebhookSecret = secret, ClientToken = token, DefaultPaymentLink = link,
        TimeoutSeconds = 5,
    };

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<PaddleHost> StartAsync(
        DocWorld world, PaddleCalls paddle, PaddleOptions? options = null)
    {
        var opts = options ?? Opts();
        var gateway = new PaddleGateway(
            opts,
            new PaddlePaymentProvider(
                Options.Create(opts), new HttpClient(paddle),
                NullLogger<PaddlePaymentProvider>.Instance));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(world.Store);
        builder.Services.AddScoped(_ => world.Session);
        builder.Services.AddSingleton(
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(world.Store));
        builder.Services.AddScoped<StudioAuth>();
        builder.Services.AddScoped<L>();
        builder.Services.AddSingleton(gateway);

        var port = FreePort();
        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapPaddleBilling();
        await app.StartAsync();

        // **ولا مُتابَعَةَ تَحويل**: نُقطَةُ النَموذَجِ تَرُدُّ ‏302 إلى
        // شاشَةِ الباقَة، ومُتابَعَتُها تُخفي **رَمزَ الرَفضِ نَفسَه**
        // الَّذي يُفحَص.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };

        return new PaddleHost(app, client, world, paddle);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

// ─── البُرهان ─────────────────────────────────────────────────────────

public class PaddleEndpointBehaviourTests
{
    private const string Slug = "ejar";

    private static TenantPlan Plan() => new()
    {
        Id = Slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = DateTime.UtcNow.AddDays(-20), ExpiresAt = DateTime.UtcNow.AddDays(10),
        GraceDays = 14,
    };

    private static PaddleTransactionRecord Record(
        string reference, string status = PaddleTransactionStatuses.Created,
        string minor = "4900", string currency = "USD") => new()
    {
        Id = reference, TenantSlug = Slug, PlanId = "manual",
        Amount = 49m, AmountMinor = minor, Currency = currency, Days = 30,
        TransactionId = "txn_01j", Status = status,
        CheckoutUrl = PaddleHost.PayPage + "?_ptxn=txn_01j",
        CreatedAt = DateTime.UtcNow, At = DateTime.UtcNow,
    };

    private const string Reference = "wsl-pd-ejar-testref0001";

    // ─── جِسمُ الرِسالَةِ ورَأسُها ───────────────────────────────────

    /// <summary><b>وكُتلَةُ المَجاميعِ كامِلَةٌ كَما تُرسِلُها
    /// Paddle</b>: نُرسِلُ سِعراً شامِلاً لِلضَريبَة
    /// (<c>tax_mode: internal</c>) فَالمَفوتَرُ هُوَ <c>total</c>،
    /// و<c>subtotal</c> ما بَقِيَ بَعدَ نَزعِها.
    /// و<b>المُقارَنَةُ على <c>total</c></b> — تَعريفٌ واحِدٌ لا
    /// اثنان (‏<c>docs/ADR-010</c>).</summary>
    private static string CompletedBody(
        string eventId = "evt_1", string status = "completed",
        string total = "4900", string currency = "USD", string reference = Reference)
        => $$$"""
        {
          "event_id": "{{{eventId}}}",
          "event_type": "transaction.completed",
          "data": {
            "id": "txn_01j",
            "status": "{{{status}}}",
            "currency_code": "{{{currency}}}",
            "custom_data": { "wasayel_ref": "{{{reference}}}" },
            "details": { "totals": {
              "subtotal": "{{{total}}}", "tax": "0",
              "total": "{{{total}}}", "grand_total": "{{{total}}}"
            } }
          }
        }
        """;

    private static string RefundBody(string eventId = "evt_ref", string reference = Reference)
        => $$$"""
        {
          "event_id": "{{{eventId}}}",
          "event_type": "adjustment.updated",
          "data": {
            "id": "adj_01j",
            "transaction_id": "txn_01j",
            "action": "refund",
            "status": "approved",
            "currency_code": "USD",
            "custom_data": { "wasayel_ref": "{{{reference}}}" }
          }
        }
        """;

    /// <summary><b>رِسالَةٌ مُوَقَّعَةٌ بِالسِرِّ الصَحيحِ وبِزَمَنِ
    /// الآن</b> — والتَوقيعُ يُحسَبُ بِنَفسِ الدالَّةِ الَّتي
    /// تَتَحَقَّقُ مِنه، فَما يُقاسُ هُوَ الوَصلُ لا الحِساب.</summary>
    private static HttpRequestMessage Signed(
        string body, string secret = PaddleHost.Secret, long? at = null)
    {
        var ts  = at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var req = new HttpRequestMessage(HttpMethod.Post, PaddleEndpoints.WebhookPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation(
            PaddleSignature.Header, $"ts={ts};h1={PaddleWebhookGuard.SignHex(secret, ts, body)}");
        return req;
    }

    // ═══ ١. الرِسالَةُ بِلا تَوقيعٍ صَحيح — تُرَدُّ قَبلَ أَيِّ لَمسَة ══

    /// <summary>
    /// <para><b>رِسالَةٌ بِلا رَأسِ تَوقيعٍ تُرَدُّ ‏400 ولا تُلمَسُ
    /// الجَلسَةُ إطلاقاً.</b></para>
    ///
    /// <para><b>و«صِفرُ كِتابَة» مَقيسٌ بِأَقوى صيغَةٍ مُمكِنَة</b>:
    /// لَيسَ «لَم تُخَزَّن وَثيقَة» بَل <b>«لَم يُنادَ عُضوٌ واحِدٌ
    /// على الجَلسَة»</b>. فَلا يَبقى بابٌ خَلفِيٌّ يَقرَأُ أَو
    /// يَكتُبُ قَبلَ البَوّابَة.</para>
    /// </summary>
    [Fact]
    public async Task AWebhookWithNoSignature_IsRefused_WithoutTouchingTheSession()
    {
        var world = new DocWorld().Put(Plan()).Put(Record(Reference));
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var response = await host.Client.PostAsync(
            PaddleEndpoints.WebhookPath,
            new StringContent(CompletedBody(), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paddle_signature_header_missing", await response.Content.ReadAsStringAsync());

        Assert.Empty(world.Touches);
        Assert.Equal(0, world.SaveCalls);
        Assert.Empty(host.Paddle.Paths);
        Assert.Equal(PaddleTransactionStatuses.Created,
            world.Read<PaddleTransactionRecord>(Reference)!.Status);
    }

    /// <summary><b>ورِسالَةٌ مُوَقَّعَةٌ بِمِفتاحِ الـAPI بَدَلَ سِرِّ
    /// الوِجهَة</b> — وهُوَ العَطَبُ الأَوَّلُ المُتَوَقَّع، فَكِلاهُما
    /// يَبدَأ <c>pdl_</c>. تُرَدُّ ‏400 بِصِفرِ لَمسَة.</summary>
    [Fact]
    public async Task AWebhookSignedWithTheWrongSecret_IsRefused_WithoutTouchingTheSession()
    {
        var world = new DocWorld().Put(Plan()).Put(Record(Reference));
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var response = await host.Client.SendAsync(
            Signed(CompletedBody(), secret: PaddleHost.ApiKey));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paddle_signature_invalid", await response.Content.ReadAsStringAsync());

        Assert.Empty(world.Touches);
        Assert.Equal(0, world.SaveCalls);
    }

    /// <summary><b>ورِسالَةٌ صَحيحَةُ التَوقيعِ أُعيدَ لَعِبُها بَعدَ
    /// دَقيقَة</b> — التَسامُحُ خَمسُ ثَوانٍ، فَتُرَدُّ ‏400 بِصِفرِ
    /// لَمسَة. <b>وهذا حارِسُ إعادَةِ اللَعِبِ الأَوَّل</b>،
    /// و`event_id` هُوَ الثاني.</summary>
    [Fact]
    public async Task AReplayedButValidlySignedWebhook_IsRefused_WhenItsTimestampIsStale()
    {
        var world = new DocWorld().Put(Plan()).Put(Record(Reference));
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var stale = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var response = await host.Client.SendAsync(Signed(CompletedBody(), at: stale));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paddle_signature_stale", await response.Content.ReadAsStringAsync());
        Assert.Empty(world.Touches);
    }

    /// <summary><b>وبِلا سِرِّ وِجهَةٍ مَضبوطٍ يُغلَقُ البابُ بِلا
    /// نِداءِ شَبَكَةٍ واحِد</b> — فَشَلٌ مُغلَق: لا يُقرَأُ الجِسمُ
    /// ولا تُلمَسُ الجَلسَة، ورَمزُ الرَفضِ يَقول «لا سِرّ» لا
    /// «تَوقيعٌ فاشِل».</summary>
    [Fact]
    public async Task WithNoWebhookSecretConfigured_TheEndpointRefuses_WithoutNetworkOrSession()
    {
        var world = new DocWorld().Put(Plan()).Put(Record(Reference));
        await using var host = await PaddleHost.StartAsync(
            world, new PaddleCalls(), PaddleHost.Opts(secret: ""));

        var response = await host.Client.SendAsync(Signed(CompletedBody()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paddle_not_configured", await response.Content.ReadAsStringAsync());

        Assert.Empty(world.Touches);
        Assert.Equal(0, world.SaveCalls);
        Assert.Empty(host.Paddle.Paths);
    }

    // ═══ ٢. ما لا يَعني وُصولَ المال لا يُمَدِّد ══════════════════════

    /// <summary>الطَرَفُ المُوجِبُ أَوَّلاً — <b>وبِدونِه لا يُمَيَّزُ
    /// «لَم يُمَدِّد» عَن «نُقطَةٍ لا تُمَدِّد أَبَداً»</b>
    /// (القاعِدَة ١٠).</summary>
    [Fact]
    public async Task AVerifiedCompletedEvent_MovesTheStoredExpiryByTheStoredDays()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(CompletedBody()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.Extend), await response.Content.ReadAsStringAsync());

        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(PaddleTransactionStatuses.Completed,
            world.Read<PaddleTransactionRecord>(Reference)!.Status);
        Assert.True(world.SaveCalls >= 1);

        // **وسِجِلُّ مَرَّة-واحِدَةٍ في نَفسِ المُعامَلَة** — وهُوَ
        // الباعِثُ المُشتَرَكُ نَفسُه، لا كاتِبٌ ثانٍ (القاعِدَة ٨).
        Assert.Equal(1, world.Wrote<PayPalWebhookRecord>());
    }

    /// <summary><b>واسمُ المُزَوِّدِ يُكتَبُ بِاسمِه</b> — سَطرُ
    /// تَدقيقٍ يَنسِبُ دَفعَةَ بِطاقَةٍ إلى PayPal سَطرٌ يَكذِب.</summary>
    [Fact]
    public async Task TheExtension_IsAttributedToPaddle_NotToItsNeighbour()
    {
        var world = new DocWorld().Put(Plan()).Put(Record(Reference));
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        await host.Client.SendAsync(Signed(CompletedBody()));

        Assert.StartsWith("paddle · ", world.Read<TenantPlan>(Slug)!.SetBy);

        var trail = world.Stored
            .OfType<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>()
            .Single();
        Assert.Equal(
            ACommerce.Templates.Customer.Marketplace.Services.Subscriptions
                     .PaddleBillingService.ExtendAuditAction,
            trail.Action);
        Assert.Equal(Slug, trail.Scope);
    }

    /// <summary><b>حَدَثٌ لا يَعني وُصولَ المال لا يُمَدِّد</b> —
    /// «أُنشِئَت» و«جاهِزَة» و«فَشِلَ الدَفع» كُلُّها تُتَجاهَل
    /// بِهُدوءٍ بِـ‏200، ولا تُحَرِّكُ تاريخاً ولا تُودِع.</summary>
    [Theory]
    [InlineData("transaction.created")]
    [InlineData("transaction.ready")]
    [InlineData("transaction.payment_failed")]
    [InlineData("customer.updated")]
    public async Task AnEventThatDoesNotMeanTheMoneyArrived_ExtendsNothing(string type)
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        var body = CompletedBody().Replace("transaction.completed", type, StringComparison.Ordinal);
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.Ignored), await response.Content.ReadAsStringAsync());

        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PayPalWebhookRecord>());
    }

    /// <summary><b>واسمُ الحَدَثِ دَعوى والحَقلُ واقِعَة</b>: رِسالَةٌ
    /// اسمُها «اكتَمَلَت» و<c>data.status</c> غَيرُ ذلك — لا
    /// تَمديد.</summary>
    [Fact]
    public async Task AnEventNamedCompleted_WithAnotherStatus_ExtendsNothing()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(CompletedBody(status: "billed")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.StatusNotCompleted),
            await response.Content.ReadAsStringAsync());
        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(0, world.SaveCalls);
    }

    // ═══ ٣. المالُ يُقارَنُ بِالمَحفوظ ═══════════════════════════════

    /// <summary>
    /// <para><b>دَفعٌ بِمَبلَغٍ أَقَلَّ لا يُمَدِّد</b> — والتاريخُ
    /// المُخَزَّنُ لا يَتَحَرَّكُ يَوماً.</para>
    ///
    /// <para><b>والرَدُّ ‏503 لا ‏200، وهذا تَبَدُّلٌ مَقصود</b>:
    /// الكَمِّيَّةُ مَحبوسَةٌ ‏1..1 والسِعرُ مُثَبَّتٌ في المُعامَلَة،
    /// فَالدافِعُ <b>لا يَملِكُ</b> أَن يَدفَعَ أَقَلّ — وعَدَمُ
    /// التَطابُقِ عِلَّتُه عِندَنا (ضَريبَةٌ أَو خَصمٌ أَو رَصيد)
    /// و<b>يُشفى بِالإعادَة</b>. ورَدُّ ‏200 كانَ يُوقِفُ إعادَةَ
    /// Paddle فَيَصيرُ القَبضُ بِلا تَمديدٍ <b>نِهائِيّاً</b>
    /// (‏<c>docs/ADR-010</c>).</para>
    /// </summary>
    [Fact]
    public async Task ALowerAmount_ExtendsNothing()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(CompletedBody(total: "100")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.AmountMismatch),
            await response.Content.ReadAsStringAsync());

        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PayPalWebhookRecord>());
    }

    /// <summary><b>وعُملَةٌ مُختَلِفَةٌ لا تُمَدِّد</b> ولَو تَطابَقَ
    /// الرَقَم.</summary>
    [Fact]
    public async Task ADifferentCurrency_ExtendsNothing()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(CompletedBody(currency: "EUR")));

        Assert.Contains(nameof(PaddleAction.AmountMismatch),
            await response.Content.ReadAsStringAsync());
        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(0, world.SaveCalls);
    }

    // ═══ ٤. التَكرارُ لا يُمَدِّد — بِمِفتاحَينِ لا واحِد ═════════════

    /// <summary><b>نَفسُ <c>event_id</c> مَرَّتَين ⇒ تَمديدٌ
    /// واحِد.</b> والمِفتاحُ وَثيقَةٌ مُخَزَّنَةٌ في نَفسِ مُعامَلَةِ
    /// التَمديد، فَلا نافِذَةَ يَقَع فيها أَحَدُهُما دونَ
    /// الآخَر.</summary>
    [Fact]
    public async Task TheSameEventIdTwice_ExtendsOnce()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var first = await host.Client.SendAsync(Signed(CompletedBody("evt_dup")));
        Assert.Contains(nameof(PaddleAction.Extend), await first.Content.ReadAsStringAsync());
        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);

        var second = await host.Client.SendAsync(Signed(CompletedBody("evt_dup")));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains(nameof(PaddleAction.Replay), await second.Content.ReadAsStringAsync());

        // **التاريخُ كَما هُوَ بَعدَ الثانِيَة** — لا يَوماً واحِداً زائِداً.
        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(1, world.Wrote<PayPalWebhookRecord>());
    }

    /// <summary><b>ومُعَرِّفُ حَدَثٍ آخَرَ على مُعامَلَةٍ اكتَمَلَت لا
    /// يُمَدِّدُ ثانِيَةً</b> — وهذا هُوَ المِفتاحُ الثاني: سِجِلُّ
    /// مَرَّة-واحِدَةٍ وَحدَه كانَ سَيُمَرِّرُها.</summary>
    [Fact]
    public async Task ASecondCompletedEvent_WithADifferentEventId_ExtendsNothing()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        await host.Client.SendAsync(Signed(CompletedBody("evt_a")));
        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);

        var again = await host.Client.SendAsync(Signed(CompletedBody("evt_b")));
        Assert.Contains(nameof(PaddleAction.Replay), await again.Content.ReadAsStringAsync());
        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);
    }

    // ═══ ٥. المَرجِعُ المَجهول ════════════════════════════════════════

    /// <summary>
    /// <para><b>مَرجِعٌ لا وَثيقَةَ لَه ⇒ صِفرُ كِتابَةٍ ورَدٌّ غَيرُ
    /// ‏2xx.</b> و‏503 لا ‏200 عَمداً: <b>الإعادَةُ تَشفي هذا
    /// الفَرع</b> — يُنشِئُ المُشرِفُ الوَثيقَةَ فَتُطَبَّقُ رِسالَةٌ
    /// لاحِقَةٌ مِن تِلقاءِ نَفسِها. ورَدُّ ‏200 يُلغي تِلكَ
    /// الشَبَكَةَ ويَقول «طُبِّقَت» وهي لَم تُطَبَّق.</para>
    /// </summary>
    [Fact]
    public async Task AnUnknownReference_WritesNothing_AndAsksForRedelivery()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan);
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(
            Signed(CompletedBody(reference: "wsl-pd-nobody-0000")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.UnknownReference),
            await response.Content.ReadAsStringAsync());

        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PaddleTransactionRecord>());
        Assert.Equal(0, world.Wrote<PayPalWebhookRecord>());
        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
    }

    // ═══ ٦. الاسترداد يَسحَبُ ما مُنِح ════════════════════════════════

    /// <summary>
    /// <para><b>دَفعٌ ثُمَّ استِردادٌ مُعتَمَد ⇒ التاريخُ يَعودُ إلى
    /// ما كان.</b> وهذا هُوَ العَطَبُ الَّذي كَتَبَ ‏ADR-007 في
    /// مَسارِ PayPal: <b>المالُ يَعودُ والأَيّامُ تَبقى</b> — يُقاسُ
    /// هُنا مِن طَرَفِ الشَبَكَةِ بِرِسالَتَينِ مُتَتالِيَتَين.</para>
    /// </summary>
    [Fact]
    public async Task APaymentThenAnApprovedRefund_LeavesTheExpiryWhereItStarted()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        await host.Client.SendAsync(Signed(CompletedBody("evt_pay")));
        Assert.Equal(before.AddDays(30), world.Read<TenantPlan>(Slug)!.ExpiresAt);

        var refund = await host.Client.SendAsync(Signed(RefundBody("evt_back")));
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Contains(nameof(PaddleAction.Withdraw), await refund.Content.ReadAsStringAsync());

        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(PaddleTransactionStatuses.Refunded,
            world.Read<PaddleTransactionRecord>(Reference)!.Status);
    }

    /// <summary><b>ولا يُسحَبُ ما لَم يُمنَح</b>: استِردادٌ على
    /// مُعامَلَةٍ لَم تُدفَع يُعَلِّمُها ولا يَمَسُّ تاريخاً —
    /// وإلّا صودِرَت مُدَّةٌ اشتُرِيَت بِمُعامَلَةٍ أُخرى.</summary>
    [Fact]
    public async Task ARefundOnAnUnpaidTransaction_MovesNoDate()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Record(Reference));
        var before = plan.ExpiresAt;

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        var response = await host.Client.SendAsync(Signed(RefundBody()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PaddleAction.MarkTransaction),
            await response.Content.ReadAsStringAsync());
        Assert.Equal(before, world.Read<TenantPlan>(Slug)!.ExpiresAt);
    }

    // ═══ ٧. نُقطَةُ الرابِط — الحارِسُ قَبلَ أَوَّلِ كِتابَة ═════════

    private static FormUrlEncodedContent LinkForm() => new(new Dictionary<string, string>
    {
        ["amount"] = "49", ["currency"] = "USD",
        ["days"] = "30", ["description"] = "اشتِراكُ شَهر",
    });

    private static string ReferenceFor(TenantPlan plan)
        => PaddleTransactionPolicy.Reference(PaddleTransactionPolicy.ReadDraft(
            Slug, plan.PlanId, "49", "USD", "30", "اشتِراكُ شَهر",
            PaddleTransactionPolicy.CycleOf(plan)));

    private static StudioUser Admin(Guid id) => new() { Id = id, IsPlatformAdmin = true };

    private static void SignIn(HttpClient client, Guid userId)
        => client.DefaultRequestHeaders.Add(
            "Cookie", $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(userId, StudioAuth.Tenant)}");

    /// <summary>
    /// <para><b>طَلَبٌ مِن غَيرِ مُشرِفٍ يُرَدُّ ‏403 قَبلَ قِراءَةِ
    /// حَقلٍ واحِد</b> (القاعِدَة ٦) — <b>ولا تُلمَسُ Paddle</b>.
    /// والحارِسُ في الجِسمِ لا في التَوقيع، فَنِسيانُه لا يُرى
    /// بِالعَين: هذا يَراه.</para>
    /// </summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_RefusesANonAdmin_BeforeTheFirstWrite()
    {
        var world = new DocWorld().Put(Plan());
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paddle-link", LinkForm());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Paddle.Paths);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PaddleTransactionRecord>());
    }

    /// <summary><b>ومُشرِفٌ بِتَهيئَةٍ ناقِصَةٍ يُرَدُّ بِلا نِداءِ
    /// شَبَكَة</b>: صَفحَةُ دَفعٍ غائِبَةٌ تَعني رابِطاً لا يُفتَح،
    /// <b>ومَدخَلٌ يَضُرّ أَسوَأُ مِن غِيابِ مَدخَل</b>.</summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_WithNoPaymentPageConfigured_RefusesWithoutNetwork()
    {
        var admin = Guid.NewGuid();
        var world = new DocWorld().Put(Plan()).Put(Admin(admin));
        await using var host = await PaddleHost.StartAsync(
            world, new PaddleCalls(), PaddleHost.Opts(link: ""));
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paddle-link", LinkForm());

        Assert.Empty(host.Paddle.Paths);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(PaddleSurface.Unavailable, response.Headers.Location!.ToString());
    }

    /// <summary>الطَرَفُ المُوجِب: مُشرِفٌ ومُزَوِّدٌ مُهَيَّأٌ ⇒
    /// <b>يُنادى <c>/transactions</c> فِعلاً</b> وتُخَزَّنُ الوَثيقَةُ
    /// بِرابِطِها. <b>وبِدونِه لا يُمَيَّزُ «لَم تُنادَ Paddle» عَن
    /// نُقطَةٍ لا تُنادي Paddle أَبَداً</b> (القاعِدَة ١٠).</summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_ForAnAdmin_CallsPaddleAndStoresTheLink()
    {
        var plan  = Plan();
        var admin = Guid.NewGuid();
        var world = new DocWorld().Put(plan).Put(Admin(admin));

        var calls = new PaddleCalls().Then(HttpStatusCode.Created,
            """
            {"data":{"id":"txn_new","status":"ready",
                     "checkout":{"url":"https://wasayel.example/billing/paddle/checkout.html?_ptxn=txn_new"}}}
            """);

        await using var host = await PaddleHost.StartAsync(world, calls);
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paddle-link", LinkForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("saved=1", response.Headers.Location!.ToString());
        Assert.Contains(PaddlePaymentProvider.TransactionsPath, host.Paddle.Paths);

        var written = world.Read<PaddleTransactionRecord>(ReferenceFor(plan))!;
        Assert.Equal("txn_new", written.TransactionId);
        Assert.Equal(PaddleTransactionStatuses.Created, written.Status);
        Assert.Equal(30, written.Days);
        Assert.Equal("4900", written.AmountMinor);
        Assert.Contains("_ptxn=txn_new", written.CheckoutUrl);
        Assert.Contains("ref=", written.CheckoutUrl);
    }

    /// <summary><b>ولا يُدهَسُ سِجِلُّ دَفعٍ مَضى</b>: مُعامَلَةٌ
    /// اكتَمَلَت على نَفسِ المَرجِعِ ⇒ رَفضٌ <b>بِلا نِداءِ
    /// Paddle</b>. والتَرتيبُ جُزءٌ مِن الشَرط: صِفرُ نِداءٍ يَعني
    /// أَنّ الحارِسَ سَبَقَ الإنشاء — وإلّا فُتِحَت مُعامَلَةٌ عِندَ
    /// Paddle ثُمَّ رُفِضَ حِفظُها.</summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_OverASettledTransaction_AnswersWithoutCallingPaddle()
    {
        var plan  = Plan();
        var admin = Guid.NewGuid();

        var settled = Record(ReferenceFor(plan), PaddleTransactionStatuses.Completed);
        var world = new DocWorld().Put(plan).Put(settled).Put(Admin(admin));

        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paddle-link", LinkForm());

        Assert.Empty(host.Paddle.Paths);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(PaddleSurface.AlreadySettled, response.Headers.Location!.ToString());
    }

    // ═══ ٨. نُقطَةُ إعدادِ صَفحَةِ الدَفع — لا سِرَّ يَمُرُّ مِنها ════

    /// <summary>
    /// <para><b>اختِبارٌ سالِب</b>: جِسمُ <c>config.json</c> يَحمِل
    /// رَمزَ العَميلِ (عَلَنيٌّ بِالتَصميم) و<b>لا يَحمِل مِفتاحَ
    /// الـAPI ولا سِرَّ التَوقيعِ إطلاقاً</b>.</para>
    ///
    /// <para><b>ولِماذا يُفحَصُ ما لا يوجَد</b>: النُقطَةُ تَقرَأُ
    /// <c>PaddleOptions</c> كامِلَةً، وسَطرٌ واحِدٌ يُضاف يَوماً
    /// (<c>options.ApiKey</c> لِلتَشخيص) يُسَرِّبُ المِفتاحَ إلى كُلِّ
    /// مُتَصَفِّح. وهذا الاختِبارُ هُوَ ما يُحمِرُّ.</para>
    /// </summary>
    [Fact]
    public async Task TheCheckoutConfig_CarriesThePublicTokenOnly_NeverASecret()
    {
        var world = new DocWorld();
        await using var host = await PaddleHost.StartAsync(world, new PaddleCalls());

        var body = await host.Client.GetStringAsync(PaddleEndpoints.ConfigPath);

        Assert.Contains(PaddleHost.Token, body);
        Assert.DoesNotContain(PaddleHost.Secret, body);
        Assert.DoesNotContain(PaddleHost.ApiKey, body);
        Assert.DoesNotContain("pdl_ntfset_", body);
        Assert.DoesNotContain("pdl_apikey_", body);

        // ونُصوصُ الصَفحَةِ تَصِلُ مِن القامُوسِ — فَالصَفحَةُ الساكِنَةُ
        // لا تَكتُبُ عَرَبِيَّةً في مِلَفِّها (القاعِدَة ١١).
        Assert.Contains(ACommerce.Platform.I18n.LocaleCatalog.Text(
            "ar", "billing.paddle.checkout_title"), body);
        Assert.Equal(0, world.SaveCalls);
    }

    /// <summary><b>وبِلا تَهيئَةٍ لا يُرسَل رَمزٌ إطلاقاً</b> —
    /// <c>enabled=false</c> وسِلسِلَةٌ فارِغَة، فَالصَفحَةُ تَقول
    /// «تَعَذَّرَ» ولا تُهَيِّئ <c>paddle.js</c> بِرَمزٍ نِصفِ
    /// مَضبوط.</summary>
    [Fact]
    public async Task TheCheckoutConfig_SendsNoTokenAtAll_WhenTheProviderCannotSell()
    {
        var world = new DocWorld();
        await using var host = await PaddleHost.StartAsync(
            world, new PaddleCalls(), PaddleHost.Opts(secret: ""));

        var body = await host.Client.GetStringAsync(PaddleEndpoints.ConfigPath);

        Assert.Contains("\"enabled\":false", body);
        Assert.DoesNotContain(PaddleHost.Token, body);
    }
}
