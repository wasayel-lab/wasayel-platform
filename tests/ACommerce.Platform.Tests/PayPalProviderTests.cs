using System.Net;
using System.Text.Json;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مُزَوِّدُ PayPal — ما يُرسَل بِالضَبط، وما يُخَزَّن ═════════════════
//
// **ولا حِسابَ PayPal في هذِه الجَولَة ولا يُطلَب**: مُعالِجٌ وَهمِيٌّ
// يَلتَقِط الطَلَبَ فَيُقاس **ما كُنّا سَنُرسِلُه** — وهُوَ المَوضِعُ
// الَّذي يَنكَسِر صامِتاً (نُقطَةٌ خاطِئَةٌ تَرُدّ ‏404، ورَأسٌ ناقِصٌ
// يَرُدّ ‏401 بِلا تَوضيح، وجِسمٌ مُعادُ تَسَلسُلُه يُفشِل تَحَقُّقاً
// صَحيحاً). نَفسُ نَمَط `BrevoEmailChannelTests` حَرفاً.
//
// **والبُرهانُ الحَيُّ دَينٌ مُعلَن** يُسَدَّد يَومَ يَضَعُ المالِكُ
// أَسرارَه — والخُطُواتُ مُرَقَّمَةٌ في `docs/DEPLOY.md` §٢·ج.

/// <summary>مُعالِجٌ يَلتَقِط كُلَّ الطَلَبات ويَرُدُّ رُدوداً
/// مُرَتَّبَة. يُعَدّ <b>كَم مَرَّةً</b> نودِيَ مَسارُ الرَمز — وهذا
/// هُوَ الفَرقُ بَينَ «الرَمزُ مُخَزَّن» و«يَبدو مُخَزَّناً».</summary>
file sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();
    public int TokenCalls { get; private set; }

    public ScriptedHandler Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    /// <summary>رَمزٌ صالِحٌ لِثَماني ساعات — الشَكلُ الَّذي تَرُدُّه
    /// PayPal فِعلاً.</summary>
    public ScriptedHandler ThenToken(string token = "A21AA", int expiresIn = 32400)
        => Then(HttpStatusCode.OK,
            $"{{\"access_token\":\"{token}\",\"token_type\":\"Bearer\",\"expires_in\":{expiresIn}}}");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        if (request.RequestUri!.AbsolutePath == PayPalPaymentProvider.TokenPath) TokenCalls++;

        var (status, body) = _replies.Count > 0
            ? _replies.Dequeue()
            : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}

public class PayPalProviderTests
{
    private static PayPalOptions Opts(
        string environment = PayPalEnvironment.Live,
        string webhookId = "WH-TEST",
        string secret = "very-secret") => new()
    {
        ClientId = "AY-client",
        ClientSecret = secret,
        Environment = environment,
        WebhookId = webhookId,
        TimeoutSeconds = 5,
    };

    private static PayPalPaymentProvider Provider(
        HttpMessageHandler handler, PayPalOptions? opts = null, PayPalTokenCache? cache = null)
        => new(Options.Create(opts ?? Opts()), new HttpClient(handler),
               cache ?? new PayPalTokenCache(), NullLogger<PayPalPaymentProvider>.Instance);

    // ─── البيئَة: مَعجَمٌ مُغلَق، والمَجهولُ إغلاق ────────────────────

    [Theory]
    [InlineData("live", PayPalEnvironment.LiveBaseUrl)]
    [InlineData("LIVE", PayPalEnvironment.LiveBaseUrl)]
    [InlineData("sandbox", PayPalEnvironment.SandboxBaseUrl)]
    [InlineData(" Sandbox ", PayPalEnvironment.SandboxBaseUrl)]
    public void KnownEnvironments_MapToTheirHost(string value, string expected)
        => Assert.Equal(expected, PayPalEnvironment.BaseUrlFor(value));

    /// <summary>قيمَةٌ ثالِثَةٌ (أَو فارِغَة) <b>لَيسَت افتِراضاً بَل
    /// إغلاقاً</b> — مُضيفٌ يُخمَّن يَعني إمّا نِداءَ اختِبارٍ يُظَنُّ
    /// حَقيقِيّاً أَو العَكس.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("production")]
    [InlineData("test")]
    public void UnknownEnvironment_HasNoHost_AndIsNotConfigured(string? value)
    {
        Assert.Null(PayPalEnvironment.BaseUrlFor(value));
        Assert.False(PayPalEnvironment.IsConfigured(Opts(environment: value ?? "")));
    }

    [Fact]
    public void Configuration_IsIncomplete_WithoutCredentials()
    {
        Assert.False(PayPalEnvironment.IsConfigured(null));
        Assert.False(PayPalEnvironment.IsConfigured(new PayPalOptions()));
        Assert.True(PayPalEnvironment.IsConfigured(Opts()));
    }

    /// <summary><b>شَرطانِ لا شَرطٌ واحِد</b>: الاعتِمادُ يَكفي
    /// لِإنشاءِ رابِط، واستِقبالُ رِسالَةٍ يَحتاج
    /// <c>WebhookId</c> فَوقَه.</summary>
    [Fact]
    public void WebhookVerification_NeedsTheWebhookId_OnTopOfCredentials()
    {
        Assert.True(PayPalEnvironment.IsConfigured(Opts(webhookId: "")));
        Assert.False(PayPalEnvironment.CanVerifyWebhooks(Opts(webhookId: "")));
        Assert.True(PayPalEnvironment.CanVerifyWebhooks(Opts()));
    }

    [Fact]
    public void EnvVarNames_AreTheKeysWithDoubleUnderscore()
    {
        Assert.Equal("Payments__PayPal__ClientId",
            PayPalEnvironment.EnvVarName(PayPalEnvironment.ClientIdKey));
        Assert.Equal("Payments__PayPal__WebhookId",
            PayPalEnvironment.EnvVarName(PayPalEnvironment.WebhookIdKey));
    }

    // ─── التَركيب: خِياراتٌ ناقِصَةٌ تُغلِق عِندَ الإقلاعِ لا عِندَ الطَلَب ──

    [Fact]
    public void MissingClientId_FailsFast_NamingTheConfigurationKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Provider(new ScriptedHandler(), new PayPalOptions { ClientSecret = "s", Environment = "live" }));
        Assert.Contains(PayPalEnvironment.ClientIdKey, ex.Message);
    }

    [Fact]
    public void UnknownEnvironment_FailsFast_NamingTheConfigurationKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Provider(new ScriptedHandler(), Opts(environment: "production")));
        Assert.Contains(PayPalEnvironment.EnvironmentKey, ex.Message);
        Assert.Contains("sandbox", ex.Message);
    }

    /// <summary>ورِسالَةُ الخَطَإ <b>لا تَحمِل السِرّ</b>. رِسالَةٌ
    /// تُكتَب في لوغٍ مُشتَرَكٍ فيها سِرٌّ هي تَسريب.</summary>
    [Fact]
    public void FailureMessage_NeverCarriesTheClientSecret()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Provider(new ScriptedHandler(), Opts(environment: "nope", secret: "xsecret-super")));
        Assert.DoesNotContain("xsecret-super", ex.Message);
    }

    [Fact]
    public void SandboxAndLive_ChooseDifferentHosts()
    {
        Assert.Equal(PayPalEnvironment.LiveBaseUrl,
            Provider(new ScriptedHandler(), Opts()).BaseUrl);
        Assert.Equal(PayPalEnvironment.SandboxBaseUrl,
            Provider(new ScriptedHandler(), Opts(environment: "sandbox")).BaseUrl);
    }

    // ─── الرَمز: يُطلَب مَرَّةً ويُخَزَّن ─────────────────────────────

    [Fact]
    public async Task Token_IsRequestedWithClientCredentials_AndBasicAuth()
    {
        var handler = new ScriptedHandler().ThenToken("TOKEN-1");
        var token = await Provider(handler).AccessTokenAsync();

        Assert.Equal("TOKEN-1", token);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + PayPalPaymentProvider.TokenPath,
            req.RequestUri!.ToString());
        Assert.Equal("Basic", req.Headers.Authorization!.Scheme);
        Assert.Contains("grant_type=client_credentials", handler.Bodies[0]);
    }

    /// <summary><b>النِداءُ الثاني لا يَطلُب رَمزاً</b> — والعَدّادُ
    /// هُوَ البُرهان. ولَو كانَ التَخزينُ داخِلَ المُزَوِّدِ نَفسِه
    /// (وهُوَ عابِرٌ بِتَسجيلِ <c>AddHttpClient</c>) لَما خَزَّنَ
    /// شَيئاً، ولَما شَكا أَحَد.</summary>
    [Fact]
    public async Task Token_IsCachedAcrossCalls_AndAcrossProviderInstances()
    {
        var cache = new PayPalTokenCache();
        var handler = new ScriptedHandler()
            .ThenToken("TOKEN-1")
            .Then(HttpStatusCode.OK, "{\"id\":\"I-1\",\"status\":\"APPROVAL_PENDING\"}");

        var first = await Provider(handler, cache: cache).AccessTokenAsync();

        // نُسخَةٌ ثانِيَةٌ مِن المُزَوِّد — كَما يَحدُث في الطَلَبِ التالي.
        var second = await Provider(handler, cache: cache).AccessTokenAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(1, cache.FetchCount);
    }

    /// <summary>الهامِشُ يُطرَح فِعلاً: رَمزٌ عُمرُه أَقَلُّ مِن
    /// الهامِشِ لا يُخَزَّن صالِحاً لِلَحظَةٍ قادِمَة.</summary>
    [Fact]
    public async Task Token_ExpiresBeforeItsAnnouncedLifetime()
    {
        var cache = new PayPalTokenCache();
        var now = DateTimeOffset.UnixEpoch;

        await cache.GetAsync(_ => Task.FromResult(("T", 120)), now);

        Assert.NotNull(cache.Cached(now.AddSeconds(59)));
        // ‏120 − 60 هامِشاً = 60 ثانِيَة صالِحَة، لا ‏120.
        Assert.Null(cache.Cached(now.AddSeconds(61)));
    }

    [Fact]
    public async Task Token_IsFetchedOnce_UnderConcurrentCallers()
    {
        var cache = new PayPalTokenCache();
        var now = DateTimeOffset.UnixEpoch;
        var gate = new TaskCompletionSource();

        async Task<(string, int)> Slow(CancellationToken _)
        {
            await gate.Task;
            return ("T", 3600);
        }

        var a = cache.GetAsync(Slow, now);
        var b = cache.GetAsync(Slow, now);
        gate.SetResult();
        await Task.WhenAll(a, b);

        Assert.Equal(1, cache.FetchCount);
    }

    // ─── الاشتِراك: سلاجُ المَتجَرِ في custom_id ──────────────────────

    [Fact]
    public async Task CreateSubscription_SendsThePlanId_AndTheTenantSlugAsCustomId()
    {
        var handler = new ScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.Created,
                """
                {"id":"I-SUB1","status":"APPROVAL_PENDING",
                 "links":[{"rel":"self","href":"https://x/self"},
                          {"rel":"approve","href":"https://www.paypal.com/webapps/billing/subscriptions?ba_token=X"}]}
                """);

        var result = await Provider(handler).CreateSubscriptionAsync(
            new SubscriptionRequest("ejar", "P-PLAN-9", 0m, ""), "key-1");

        Assert.Equal("I-SUB1", result.SubscriptionId);
        Assert.False(result.IsActive);
        Assert.Equal("https://www.paypal.com/webapps/billing/subscriptions?ba_token=X", result.ApproveUrl);

        var sub = handler.Requests[1];
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + PayPalPaymentProvider.SubscriptionsPath,
            sub.RequestUri!.ToString());
        Assert.Equal("Bearer", sub.Headers.Authorization!.Scheme);
        Assert.Equal("key-1", Assert.Single(sub.Headers.GetValues("PayPal-Request-Id")));

        using var body = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.Equal("P-PLAN-9", body.RootElement.GetProperty("plan_id").GetString());
        Assert.Equal("ejar", body.RootElement.GetProperty("custom_id").GetString());
    }

    [Fact]
    public async Task CreateSubscription_ReportsFailure_WithoutThrowing()
    {
        var handler = new ScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.UnprocessableEntity, "{\"name\":\"INVALID_REQUEST\"}");

        var result = await Provider(handler).CreateSubscriptionAsync(
            new SubscriptionRequest("ejar", "P-BAD", 0m, ""), "key-1");

        Assert.Equal("", result.SubscriptionId);
        Assert.False(result.IsActive);
        Assert.Contains("422", result.FailureReason);
        Assert.Null(result.ApproveUrl);
    }

    [Fact]
    public async Task CancelSubscription_PostsToTheCancelPath()
    {
        var handler = new ScriptedHandler().ThenToken().Then(HttpStatusCode.NoContent, "");
        Assert.True(await Provider(handler).CancelSubscriptionAsync("I-SUB1"));
        Assert.EndsWith("/v1/billing/subscriptions/I-SUB1/cancel",
            handler.Requests[1].RequestUri!.AbsolutePath);
    }

    // ─── التَحَقُّق مِن التَوقيع ──────────────────────────────────────

    private static readonly PayPalWebhookHeaders FullHeaders = new(
        "tx-1", "2026-08-24T10:00:00Z", "https://api.paypal.com/cert.pem", "SHA256withRSA", "sig==");

    [Fact]
    public async Task Verify_PostsTheWebhookId_AndEmbedsTheRawBodyVerbatim()
    {
        // مَسافاتٌ وتَرتيبٌ مَقصودانِ: لَو أُعيدَ تَسَلسُلُ الجِسمِ
        // لَتَغَيَّرا — ولَفَشِلَ تَحَقُّقٌ صَحيح.
        const string raw = "{\"id\":\"WH-1\",  \"event_type\":\"BILLING.SUBSCRIPTION.ACTIVATED\"}";
        var handler = new ScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"verification_status\":\"SUCCESS\"}");

        Assert.True(await Provider(handler).VerifyWebhookSignatureAsync(FullHeaders, raw));

        Assert.EndsWith(PayPalPaymentProvider.VerifySignaturePath,
            handler.Requests[1].RequestUri!.AbsolutePath);

        var sent = handler.Bodies[1]!;
        Assert.Contains(raw, sent);                       // بِبايتاتِه لا مُعادَ تَسَلسُلُه
        Assert.Contains("\"webhook_id\":\"WH-TEST\"", sent);
        Assert.Contains("\"transmission_sig\":\"sig==\"", sent);
        Assert.Contains("\"auth_algo\":\"SHA256withRSA\"", sent);
    }

    [Fact]
    public async Task Verify_IsFalse_WhenPayPalDoesNotSaySuccess()
    {
        var handler = new ScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"verification_status\":\"FAILURE\"}");
        Assert.False(await Provider(handler).VerifyWebhookSignatureAsync(FullHeaders, "{\"id\":\"1\"}"));
    }

    /// <summary><b>غِيابُ <c>WebhookId</c> يُغلِق قَبلَ أَيّ نِداء</b> —
    /// ولا يُرسَل طَلَبُ تَحَقُّقٍ ناقِصٌ لِيَرُدَّ PayPal رَفضاً
    /// غامِضاً. صِفرُ طَلَبٍ هُوَ البُرهان.</summary>
    [Fact]
    public async Task Verify_IsFalse_AndSilent_WhenTheWebhookIdIsMissing()
    {
        var handler = new ScriptedHandler().ThenToken();
        var provider = Provider(handler, Opts(webhookId: ""));

        Assert.False(await provider.VerifyWebhookSignatureAsync(FullHeaders, "{\"id\":\"1\"}"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Verify_IsFalse_AndSilent_WhenASignatureHeaderIsMissing()
    {
        var handler = new ScriptedHandler().ThenToken();
        var provider = Provider(handler);

        Assert.False(await provider.VerifyWebhookSignatureAsync(
            FullHeaders with { TransmissionSig = "" }, "{\"id\":\"1\"}"));
        Assert.Empty(handler.Requests);
    }

    /// <summary>عُطلُ شَبَكَةٍ عِندَ التَحَقُّقِ <b>لَيسَ قَبولاً</b>.</summary>
    [Fact]
    public async Task Verify_IsFalse_WhenTheNetworkThrows()
    {
        var handler = new ThrowingHandler();
        Assert.False(await Provider(handler).VerifyWebhookSignatureAsync(FullHeaders, "{\"id\":\"1\"}"));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }

    // ─── ما لا يُنَفَّذ: يَرمي ويُسَمّي البَديل ───────────────────────

    [Fact]
    public async Task ShopperPayments_Throw_NamingMoyasarAndNoon()
    {
        var p = Provider(new ScriptedHandler());
        var req = new PaymentRequest(10m, "d", "c", "0500000000");

        var a = await Assert.ThrowsAsync<NotSupportedException>(() => p.AuthorizeAsync(req, "k"));
        var c = await Assert.ThrowsAsync<NotSupportedException>(() => p.CaptureAsync("x"));
        var r = await Assert.ThrowsAsync<NotSupportedException>(() => p.RefundAsync("x", 1m, "why"));

        foreach (var ex in new[] { a, c, r })
        {
            Assert.Contains("Moyasar", ex.Message);
            Assert.Contains("Noon", ex.Message);
        }
    }

    [Fact]
    public void ProviderName_IsPayPal_AndItImplementsTheSharedContract()
    {
        Assert.Equal("PayPal", Provider(new ScriptedHandler()).ProviderName);
        Assert.True(typeof(IPaymentProvider).IsAssignableFrom(typeof(PayPalPaymentProvider)));
    }

    // ─── البابُ حينَ لا مُزَوِّدَ مُسَجَّلاً — «لا» بِلا انفِجار ──────

    [Fact]
    public async Task Gateway_WithoutAProvider_SaysNo_AndNeverThrows()
    {
        var gateway = new PayPalGateway(new PayPalOptions(), provider: null);

        Assert.False(gateway.IsConfigured);
        Assert.False(gateway.CanVerifyWebhooks);
        Assert.False(await gateway.VerifyWebhookSignatureAsync(FullHeaders, "{}"));

        var result = await gateway.CreateSubscriptionAsync("P-1", "ejar", "k");
        Assert.Equal("", result.SubscriptionId);
        Assert.Null(result.ApproveUrl);
        Assert.NotNull(result.FailureReason);
    }
}
