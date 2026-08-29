using System.Net;
using System.Reflection;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ حُرّاسُ المالِ سُلوكِيَّةً — النُقطَةُ تُشَغَّل، لا تُقرَأ ═════════
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ — مَقيسَة**: لَم يَكُن في
// `tests/` كُلِّها **ولا اختِبارٌ واحِدٌ يُشَغِّل نُقطَة**: صِفرُ
// `WebApplicationFactory`، صِفرُ `TestServer`، صِفرُ `HttpClient` نَحوَ
// نُقطَةٍ عِندَنا. فَكُلُّ «حُرّاسِ» مَسارِ الدَفعِ كانَت تَقرَأ **نَصَّ
// المَصدَر**:
//
//   · `TheOverwriteGuard_…` تَفحَص `Contains("PayPalOrderPolicy.IsOverwritable")`
//     وتَرتيبَ فِهرِسَين ⇒ **نَزعُ `!` وَحدَه يَقلِبُ الحارِسَ وتَبقى
//     خَضراء**.
//   · `TheCaptureCall_NeverClaimsTheMoneyArrived` تَفحَص وُجودَ رَمزٍ
//     وغِيابَ آخَر ⇒ كِتابَةُ `order.Status = "captured";` **بِحَرفِيَّةٍ
//     نَصِّيَّة** تَمُرّ.
//   · `TheButtonAndTheEndpoint_ReadTheSameRule` تَفحَص وُجودَ الرَمزَين
//     لا حُكمَهُما ⇒ **قَلبُ الشَرطَينِ مَعاً يَمُرّ**.
//
// **ولِماذا مُضيفٌ مُصَغَّرٌ لا `Microsoft.AspNetCore.Mvc.Testing`** —
// قَرارٌ بِقِياسٍ لا بِذَوق: ‏`WebApplicationFactory` وُجِدَت لِتُقلِعَ
// `Program` التَطبيقِ بِحَقنِه كامِلاً، و`apps/V1.App` تُقلِع
// **Marten + Wolverine + Redis + SignalR** — أَي **قاعِدَةَ بَياناتٍ
// حَقيقِيَّة** في بَوّابَةٍ يَجِبُ أَن تَخضَرَّ بِلا شَبَكَة. ونِقاطُنا
// تُسَجَّل بِدالَّةِ امتِدادٍ واحِدَةٍ تَأخُذ `IEndpointRouteBuilder`
// (‏`MapPayPalBilling`)، فَمُضيفٌ يَحمِلُها وَحدَها **يُشَغِّل نَفسَ
// المُوَجِّهِ ونَفسَ رَبطِ الوُسَطاءِ ونَفسَ قِراءَةِ النَموذَج** —
// بِصِفرِ حُزَمٍ جَديدَة، وبِنَفسِ نَمَطِ
// `LiveOutboxTenantProofTests.BuildApp` حَرفاً.
//
// **والوَثائِقُ في الذاكِرَة لا في Postgres**: ‏`DispatchProxy` يُوَلِّد
// `IDocumentSession`/`IDocumentStore` يُجيبانِ عَن `LoadAsync`/`Store`/
// `Insert`/`SaveChangesAsync` **ويَرمِيانِ عِندَ كُلِّ عُضوٍ آخَر**.
// والرَميُ مَقصود: مَسارٌ يَعتَمِدُ على شَيءٍ لَم نُنَفِّذه **يَحمَرُّ
// بِصَوتٍ** بَدَلَ أَن يَمُرَّ صامِتاً — وهذا هُوَ الفَرقُ بَينَ فاحِصٍ
// وأَداةٍ عَمياء (القاعِدَة ١٠).

// **وعالَمُ الوَثائِقِ (`DocWorld`/`MartenProxy`) خَرَجَ إلى
// `EndpointDocWorld.cs`** يَومَ وَصَلَ مُزَوِّدٌ ثانٍ بِنَفسِ
// الحاجَةِ بِالضَبط — نَقلُ نِطاقٍ بِصِفرِ تَغييرٍ في السُلوك، لا
// تَجريدٌ جَديد. والبَديلُ كانَ نَسخَ ‏128 سَطراً مِن أَداةِ قِياسٍ
// تَنجَرِفُ نُسخَتاها (القاعِدَة ٢).

// ─── مُعالِجُ PayPal الوَهمِيّ — ويَعُدُّ ما لَم يُرسَل ───────────────

/// <summary><b>يُسَجِّلُ كُلَّ طَلَبٍ خارِج</b>، فَ«رُدَّت بِلا نِداءِ
/// PayPal» تَصيرُ عَدَداً لا دَعوى.</summary>
file sealed class PayPalCalls : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

    public List<string> Paths { get; } = new();

    public PayPalCalls Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    public PayPalCalls ThenToken()
        => Then(HttpStatusCode.OK, "{\"access_token\":\"A21AA\",\"expires_in\":32400}");

    /// <summary>رَدُّ نُقطَةِ التَحَقُّقِ مِن التَوقيع.</summary>
    public PayPalCalls ThenVerify(bool ok)
        => Then(HttpStatusCode.OK,
            $"{{\"verification_status\":\"{(ok ? "SUCCESS" : "FAILURE")}\"}}");

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.OK, "{}");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}

// ─── المُضيفُ المُصَغَّر — نَفسُ النِقاطِ ونَفسُ المُوَجِّه ────────────

file sealed class PayPalHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }
    public DocWorld World { get; }
    public PayPalCalls PayPal { get; }

    private PayPalHost(WebApplication app, HttpClient client, DocWorld world, PayPalCalls paypal)
    {
        _app = app; Client = client; World = world; PayPal = paypal;
    }

    private static PayPalOptions Opts() => new()
    {
        ClientId = "AY-client", ClientSecret = "very-secret",
        Environment = PayPalEnvironment.Live, WebhookId = "WH-TEST", TimeoutSeconds = 5,
    };

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<PayPalHost> StartAsync(DocWorld world, PayPalCalls paypal)
    {
        var gateway = new PayPalGateway(
            Opts(),
            new PayPalPaymentProvider(
                Options.Create(Opts()), new HttpClient(paypal),
                new PayPalTokenCache(), NullLogger<PayPalPaymentProvider>.Instance));

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(world.Store);
        builder.Services.AddScoped(_ => world.Session);
        builder.Services.AddSingleton(
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(world.Store));
        builder.Services.AddScoped<StudioAuth>();
        builder.Services.AddSingleton(gateway);

        var port = FreePort();
        var app = builder.Build();
        app.Urls.Add($"http://127.0.0.1:{port}");
        app.MapPayPalBilling();
        await app.StartAsync();

        // **ولا مُتابَعَةَ تَحويل**: نُقطَةُ النَموذَجِ تَرُدُّ ‏302 إلى
        // شاشَةِ الباقَة، ومُتابَعَتُها تُخفي **رَمزَ الرَفضِ نَفسَه**
        // الَّذي يُفحَص.
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };

        return new PayPalHost(app, client, world, paypal);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

// ─── البُرهان ─────────────────────────────────────────────────────────

public class PayPalEndpointBehaviourTests
{
    private static readonly DateTime Now = new(2026, 08, 24, 12, 00, 00, DateTimeKind.Utc);

    private const string Slug      = "ejar";
    private const string Reference = "wsl-ejar-abc123";

    private static TenantPlan Plan() => new()
    {
        Id = Slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(10), GraceDays = 14,
    };

    private static PayPalOrderRecord Order(string status) => new()
    {
        Id = Reference, TenantSlug = Slug, PlanId = "manual",
        Amount = 49m, Currency = "USD", Days = 30,
        OrderId = "5O190127TN364715T",
        ApproveUrl = "https://www.paypal.com/checkoutnow?token=5O1",
        Status = status, CreatedAt = Now, At = Now,
    };

    /// <summary>الرُؤوسُ الخَمسَةُ كامِلَةً — <c>IsComplete</c> تَقبَل.</summary>
    private static HttpRequestMessage Signed(string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, PayPalEndpoints.WebhookPath)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation(PayPalWebhookHeaders.TransmissionIdHeader, "TX-1");
        req.Headers.TryAddWithoutValidation(PayPalWebhookHeaders.TransmissionTimeHeader,
            "2026-08-24T12:00:00Z");
        req.Headers.TryAddWithoutValidation(PayPalWebhookHeaders.CertUrlHeader,
            "https://api.paypal.com/cert.pem");
        req.Headers.TryAddWithoutValidation(PayPalWebhookHeaders.AuthAlgoHeader, "SHA256withRSA");
        req.Headers.TryAddWithoutValidation(PayPalWebhookHeaders.TransmissionSigHeader, "sig==");
        return req;
    }

    private static string ApprovalReversedBody(string eventId) =>
        $$$"""
        {"id":"{{{eventId}}}","event_type":"CHECKOUT.PAYMENT-APPROVAL.REVERSED",
         "resource":{"order_id":"5O190127TN364715T",
                     "purchase_units":[{"custom_id":"{{{Reference}}}"}]}}
        """;

    private static string RefundedBody(string eventId) =>
        $$$"""
        {"id":"{{{eventId}}}","event_type":"PAYMENT.CAPTURE.REFUNDED",
         "resource":{"id":"REFUND-9","status":"COMPLETED","custom_id":"{{{Reference}}}",
                     "amount":{"value":"49.00","currency_code":"USD"},
                     "links":[{"rel":"up","href":"https://api.paypal.com/v2/payments/captures/3C6"}]}}
        """;

    // ═══ ١. الرِسالَةُ بِلا تَوقيعٍ — تُرَدُّ **قَبلَ** أَيِّ لَمسَة ════

    /// <summary>
    /// <para><b>رِسالَةٌ بِلا رُؤوسِ تَوقيعٍ تُرَدُّ ‏400 ولا تُلمَسُ
    /// الجَلسَةُ إطلاقاً.</b></para>
    ///
    /// <para><b>و«صِفرُ كِتابَة» هُنا مَقيسٌ بِأَقوى صيغَةٍ مُمكِنَة</b>:
    /// لَيسَ «لَم تُخَزَّن وَثيقَة» بَل <b>«لَم يُنادَ عُضوٌ واحِدٌ على
    /// الجَلسَة»</b> — ‏<c>Members</c> فارِغَة. فَلا يَبقى بابٌ خَلفِيٌّ
    /// يَقرَأُ أَو يَكتُبُ قَبلَ البَوّابَة.</para>
    ///
    /// <para><b>ولا نِداءَ إلى PayPal أَيضاً</b>: التَحَقُّقُ يَقصُر
    /// عِندَ رَأسٍ ناقِصٍ بِلا شَبَكَة — فَرَسائِلُ عابِثٍ لا تُحَوَّل
    /// إلى نِداءاتٍ خارِجَةٍ عَلى حِسابِنا.</para>
    /// </summary>
    [Fact]
    public async Task AWebhookWithNoSignature_IsRefused_WithoutTouchingTheSession()
    {
        var world = new DocWorld().Put(Plan()).Put(Order(PayPalOrderStatuses.Captured));
        await using var host = await PayPalHost.StartAsync(world, new PayPalCalls());

        var response = await host.Client.PostAsync(
            PayPalEndpoints.WebhookPath,
            new StringContent(RefundedBody("WH-NOSIG"), System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paypal_signature_headers_missing", await response.Content.ReadAsStringAsync());

        Assert.Empty(world.Touches);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(PayPalOrderStatuses.Captured, world.Read<PayPalOrderRecord>(Reference)!.Status);
        Assert.Empty(host.PayPal.Paths);
    }

    /// <summary>ورُؤوسٌ كامِلَةٌ بِتَوقيعٍ تَرُدُّه PayPal
    /// <c>FAILURE</c> — نَفسُ النَتيجَة، وبِطَريقٍ آخَر: <b>التَوقيعُ
    /// يُفحَصُ فِعلاً لا شَكلُ الرُؤوسِ وَحدَه</b>.</summary>
    [Fact]
    public async Task AWebhookWithABadSignature_IsRefused_WithoutTouchingTheSession()
    {
        var world = new DocWorld().Put(Plan()).Put(Order(PayPalOrderStatuses.Captured));
        var calls = new PayPalCalls().ThenToken().ThenVerify(false);
        await using var host = await PayPalHost.StartAsync(world, calls);

        var response = await host.Client.SendAsync(Signed(RefundedBody("WH-BADSIG")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paypal_signature_invalid", await response.Content.ReadAsStringAsync());

        Assert.Empty(world.Touches);
        Assert.Equal(0, world.SaveCalls);
        Assert.Contains(PayPalPaymentProvider.VerifySignaturePath, host.PayPal.Paths);
    }

    // ═══ ٢. نَقضُ المُوافَقَةِ ثُمَّ الاستِرداد — عَبرَ الشَبَكَة ══════

    /// <summary>
    /// <para><b>الحاجِبُ الأَوَّلُ مُبَرهَناً مِن طَرَفِ الشَبَكَة</b>:
    /// رِسالَتانِ مُوَثَّقَتانِ تَدخُلانِ النُقطَةَ بِالتَرتيبِ الَّذي
    /// يَقَع فِعلاً — <c>CHECKOUT.PAYMENT-APPROVAL.REVERSED</c> على طَلَبٍ
    /// مَقبوض، ثُمَّ <c>PAYMENT.CAPTURE.REFUNDED</c> بِمَبلَغٍ مُطابِق.</para>
    ///
    /// <para><b>وما كانَ يَقَع قَبلَ الإصلاح</b>: الأولى تَكتُب
    /// <c>reversed</c> بِلا مَساسٍ بِالباقَة، فَتَجِدُ الثانِيَةُ حارِسَ
    /// السَحبِ يَشتَرِط <c>captured</c> — <b>المالُ يَعودُ والأَيّامُ
    /// تَبقى</b>.</para>
    ///
    /// <para><b>والفَرقُ عَن نَظيرِها النَقِيّ</b>: تِلكَ تُنادي
    /// <c>Decide</c>، وهذِه تُثبِتُ أَنّ <b>المُوَجِّهَ والبَوّابَةَ
    /// والقارِئَ والكاتِبَ والمُودِعَ</b> مَوصولونَ فِعلاً — وأَنّ
    /// <c>ExpiresAt</c> المُخَزَّنَ تَحَرَّكَ بِالأَيّامِ نَفسِها.</para>
    /// </summary>
    [Fact]
    public async Task AnApprovalReversalThenARefund_WithdrawsTheDaysItGranted()
    {
        var plan  = Plan();
        var world = new DocWorld().Put(plan).Put(Order(PayPalOrderStatuses.Captured));
        var granted = plan.ExpiresAt;

        var calls = new PayPalCalls()
            .ThenToken().ThenVerify(true)   // نَقضُ المُوافَقَة
            .ThenVerify(true);              // الاسترداد
        await using var host = await PayPalHost.StartAsync(world, calls);

        // ‏١) نَقضُ المُوافَقَة ⇒ ‏200 بِلا كِتابَة، والحالَةُ كَما هي.
        var stray = await host.Client.SendAsync(Signed(ApprovalReversedBody("WH-APR-1")));
        Assert.Equal(HttpStatusCode.OK, stray.StatusCode);
        Assert.Equal(0, world.Wrote<PayPalOrderRecord>());
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(PayPalOrderStatuses.Captured, world.Read<PayPalOrderRecord>(Reference)!.Status);
        Assert.Equal(granted, world.Read<TenantPlan>(Slug)!.ExpiresAt);

        // ‏٢) الاستِردادُ يَجِدُ البابَ مَفتوحاً ⇒ تُسحَبُ الثَلاثونَ يَوماً.
        var refund = await host.Client.SendAsync(Signed(RefundedBody("WH-REF-1")));
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Contains(nameof(PayPalOrderAction.Withdraw), await refund.Content.ReadAsStringAsync());

        Assert.Equal(granted.AddDays(-30), world.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(PayPalOrderStatuses.Reversed, world.Read<PayPalOrderRecord>(Reference)!.Status);
        Assert.True(world.SaveCalls >= 1);
    }

    // ═══ ٣. رابِطُ دَفعٍ فَوقَ طَلَبٍ مَقبوض — يُرَدُّ بِلا نِداءِ PayPal ══

    /// <summary>مِفتاحُ وَثيقَةِ الطَلَبِ كَما تَحسُبُه النُقطَةُ
    /// نَفسُها — <b>مِن مَوضِعٍ واحِدٍ لا مِن سِلسِلَةٍ مَنسوخَة</b>.</summary>
    private static string ReferenceFor(TenantPlan plan)
        => PayPalOrderPolicy.Reference(PayPalOrderPolicy.ReadDraft(
            Slug, plan.PlanId, "49", "USD", "30", "اشتِراكُ شَهر",
            PayPalOrderPolicy.CycleOf(plan)));

    private static FormUrlEncodedContent OrderForm() => new(new Dictionary<string, string>
    {
        ["amount"] = "49", ["currency"] = "USD",
        ["days"] = "30", ["description"] = "اشتِراكُ شَهر",
    });

    private static StudioUser Admin(Guid id) => new() { Id = id, IsPlatformAdmin = true };

    private static void SignIn(HttpClient client, Guid userId)
        => client.DefaultRequestHeaders.Add(
            "Cookie", $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(userId, StudioAuth.Tenant)}");

    /// <summary>
    /// <para><b>الحاجِبُ الثاني مُبَرهَناً سُلوكِيّاً</b>: وَثيقَةُ طَلَبٍ
    /// مَقبوضَةٌ قائِمَةٌ على نَفسِ المَرجِع ⇒ النُقطَةُ تَرُدُّ
    /// <c>paypal_order_settled</c> و<b>لا يَخرُجُ طَلَبٌ واحِدٌ إلى
    /// PayPal</b>.</para>
    ///
    /// <para><b>ولِماذا هذا يَقيسُ ما لَم يَقِسهُ سَلَفُه</b>: النَصِّيُّ
    /// كانَ يَفحَص وُجودَ الاسمِ وتَرتيبَ فِهرِسَين، <b>فَنَزعُ <c>!</c>
    /// وَحدَه يَقلِبُ الحارِسَ وتَبقى الخُضرَة</b>. وهذا يَقرَأُ الرَدَّ
    /// وعَدَّادَ الطَلَباتِ الخارِجَة، فَقَلبُ الشَرطِ يُخرِج طَلَباً
    /// إلى PayPal <b>ويُحمِرّ</b>.</para>
    ///
    /// <para><b>والتَرتيبُ جُزءٌ مِن الشَرطِ</b>: صِفرُ نِداءٍ يَعني أَنّ
    /// الحارِسَ سَبَقَ <c>CreateOrderAsync</c> — وإلّا فُتِحَ طَلَبٌ
    /// عِندَ PayPal ثُمَّ رُفِضَ حِفظُه، فَيَبقى في حِسابِ التاجِرِ
    /// طَلَبٌ مُعَلَّقٌ لا وَثيقَةَ لَه عِندَنا.</para>
    /// </summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_OverASettledOrder_AnswersWithoutCallingPayPal()
    {
        var plan  = Plan();
        var admin = Guid.NewGuid();

        var settled = Order(PayPalOrderStatuses.Captured);
        settled.Id = ReferenceFor(plan);

        var world = new DocWorld().Put(plan).Put(settled).Put(Admin(admin));
        await using var host = await PayPalHost.StartAsync(world, new PayPalCalls());
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paypal-order", OrderForm());

        // **والعَدّادُ أَوَّلاً عَمداً**: قَلبُ الحارِسِ يُخرِجُ طَلَباً
        // إلى PayPal، فَيُقالُ ذلكَ بِاسمِه بَدَلَ أَن يَظهَرَ ‏500
        // غامِضَةً مِن رَدٍّ لَم يُنتَظَر.
        Assert.Empty(host.PayPal.Paths);
        Assert.Equal(0, world.Wrote<PayPalOrderRecord>());
        Assert.Equal(0, world.SaveCalls);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(PayPalOrderSurface.OrderSettled, response.Headers.Location!.ToString());
    }

    /// <summary>
    /// <para><b>والطَرَفُ المُقابِلُ في نَفسِ المِلَفّ</b>: بِلا وَثيقَةِ
    /// طَلَبٍ قائِمَةٍ يَمُرُّ الطَلَبُ إلى PayPal ويُخَزَّنُ الرابِط.
    /// <b>بِدونِ هذا الطَرَف، اختِبارٌ يُعطي «لَم تُنادَ PayPal» لا
    /// يُمَيَّزُ عَن نُقطَةٍ لا تُنادي PayPal أَبَداً</b>
    /// (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_OverAFreshCycle_DoesCallPayPalAndStoresTheLink()
    {
        var plan  = Plan();
        var admin = Guid.NewGuid();

        var calls = new PayPalCalls()
            .ThenToken()
            .Then(HttpStatusCode.Created,
                """
                {"id":"5O1","status":"PAYER_ACTION_REQUIRED",
                 "links":[{"rel":"payer-action","href":"https://www.paypal.com/checkoutnow?token=5O1"}]}
                """);

        var world = new DocWorld().Put(plan).Put(Admin(admin));
        await using var host = await PayPalHost.StartAsync(world, calls);
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paypal-order", OrderForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("saved=1", response.Headers.Location!.ToString());

        Assert.Contains(PayPalPaymentProvider.OrdersPath, host.PayPal.Paths);
        Assert.Equal(1, world.Wrote<PayPalOrderRecord>());

        var written = world.Read<PayPalOrderRecord>(ReferenceFor(plan))!;
        Assert.Equal("5O1", written.OrderId);
        Assert.Equal(PayPalOrderStatuses.Created, written.Status);
        Assert.Equal(30, written.Days);
    }

    /// <summary>
    /// <para><b>وطَلَبٌ مَجهولٌ يُرَدُّ ‏403 قَبلَ قِراءَةِ حَقلٍ
    /// واحِد</b> (القاعِدَة ٦) — <b>ولا يُلمَسُ PayPal</b>. والحارِسُ
    /// في الجِسمِ لا في التَوقيع، فَنِسيانُه لا يُرى بِالعَين: هذا
    /// يَراه.</para>
    /// </summary>
    [Fact]
    public async Task ThePaymentLinkEndpoint_RefusesAnAnonymousRequest_BeforeReadingAnything()
    {
        var world = new DocWorld().Put(Plan());
        await using var host = await PayPalHost.StartAsync(world, new PayPalCalls());

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paypal-order", OrderForm());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.PayPal.Paths);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PayPalOrderRecord>());
    }

    // ═══ ٤. «التَقِط الآن» — الكَلِمَةُ الَّتي لا يَملِكُها ═════════════

    private static FormUrlEncodedContent CaptureForm(string reference) => new(
        new Dictionary<string, string> { [PayPalOrderSurface.ReferenceField] = reference });

    private const string CapturedReply =
        """
        {"id":"5O190127TN364715T","status":"COMPLETED",
         "purchase_units":[{"payments":{"captures":[
            {"id":"3C6","status":"COMPLETED",
             "seller_receivable_breakdown":{"net_amount":{"value":"46.30"}}}]}}]}
        """;

    /// <summary>
    /// <para><b>نِداءُ الالتِقاطِ لا يَدَّعي وُصولَ المال — مُبَرهَناً
    /// بِالوَثيقَةِ المُخَزَّنَة لا بِنَصِّ المَصدَر.</b></para>
    ///
    /// <para><b>وسَلَفُه كانَ يَقرَأُ المَصدَرَ بِعَيبَين</b>: يَشتَرِط
    /// وُجودَ رَمزٍ وغِيابَ آخَر — فَـ<c>order.Status = "captured";</c>
    /// <b>بِحَرفِيَّةٍ نَصِّيَّة</b> تَمُرُّ خَضراء — <b>ويَقتَطِعُ إلى
    /// آخِرِ المِلَفِّ لا آخِرِ الدالَّة</b>، فَذِكرُ الكَلِمَةِ في
    /// دالَّةٍ لاحِقَةٍ لا صِلَةَ لَها يُحمِرُّه بِلا عَطَب. وهذا
    /// يَقرَأُ <b>ما كُتِبَ في الوَثيقَة</b>: لا نَصَّ ولا اقتِطاع.</para>
    ///
    /// <para><b>ولِماذا تَهُمّ</b>: لَو كُتِبَت <c>captured</c> بِلا
    /// تَمديد، لَسَحَبَ استِردادٌ لاحِقٌ أَيّاماً <b>لَم تُمنَح</b>.</para>
    /// </summary>
    [Fact]
    public async Task TheManualCaptureEndpoint_WritesApprovedNeverCaptured_AndLeavesATrail()
    {
        var admin = Guid.NewGuid();
        var order = Order(PayPalOrderStatuses.Approved);
        var world = new DocWorld().Put(Plan()).Put(order).Put(Admin(admin));

        var calls = new PayPalCalls().ThenToken().Then(HttpStatusCode.Created, CapturedReply);
        await using var host = await PayPalHost.StartAsync(world, calls);
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paypal-capture", CaptureForm(Reference));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(PayPalPaymentProvider.CapturePathFor("5O190127TN364715T"), host.PayPal.Paths);

        var written = world.Read<PayPalOrderRecord>(Reference)!;
        Assert.Equal(PayPalOrderStatuses.Approved, written.Status);
        Assert.NotEqual(PayPalOrderStatuses.Captured, written.Status);
        Assert.Equal("3C6", written.CaptureId);

        // ‏«لا قَرارَ إداريٌّ بِلا أَثَر» — والأَثَرُ وَثيقَةٌ مُخَزَّنَةٌ
        // لا سَطرٌ في المَصدَر.
        var trail = Assert.Single(world.Stored.OfType<
            ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>());
        Assert.Equal(
            ACommerce.Templates.Customer.Marketplace.Services.Subscriptions
                     .PayPalBillingService.CaptureAuditAction,
            trail.Action);
        Assert.Equal(Slug, trail.Scope);
    }

    /// <summary>
    /// <para><b>ونَفسُ الشَرطِ الَّذي يَرسُمُ الزِرَّ يَحرُسُ
    /// النُقطَة</b>: طَلَبٌ ثُبِّتَ لَه مُعَرِّفُ التِقاطٍ سَلَفاً
    /// يُرَدُّ بِالعَرَبِيَّةِ مِن عِندِنا — <b>ولا تُنادى PayPal
    /// فَتَرُدَّ <c>ORDER_ALREADY_CAPTURED</c> إنجِليزِيّاً
    /// خامّاً</b>.</para>
    ///
    /// <para><b>وسَلَفُه كانَ يَفحَص وُجودَ الرَمزَينِ لا حُكمَهُما</b> —
    /// فَقَلبُ الشَرطَينِ مَعاً يَمُرّ. وهذا يَقرَأُ الرَدَّ وعَدّادَ
    /// الطَلَباتِ الخارِجَة.</para>
    /// </summary>
    [Fact]
    public async Task TheManualCaptureEndpoint_RefusesAnOrderAlreadyCaptured_WithoutCallingPayPal()
    {
        var admin = Guid.NewGuid();
        var order = Order(PayPalOrderStatuses.Approved);
        order.CaptureId = "3C6";   // نِداءٌ ناجِحٌ سابِقٌ ثَبَّتَه

        var world = new DocWorld().Put(Plan()).Put(order).Put(Admin(admin));
        await using var host = await PayPalHost.StartAsync(world, new PayPalCalls());
        SignIn(host.Client, admin);

        var response = await host.Client.PostAsync(
            $"/admin/tenants/{Slug}/plan/paypal-capture", CaptureForm(Reference));

        Assert.Empty(host.PayPal.Paths);
        Assert.Equal(0, world.SaveCalls);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(PayPalOrderSurface.CaptureNotAllowed, response.Headers.Location!.ToString());
    }
}
