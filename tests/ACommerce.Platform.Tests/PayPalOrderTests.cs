using System.Net;
using System.Text.Json;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبُ الدَفعِ المَرِن — ما يُرسَل، وما يُمَدِّد ═════════════════
//
// **ولا حِسابَ PayPal في هذِه الجَولَة ولا يُطلَب**: مُعالِجٌ وَهمِيٌّ
// يَلتَقِط الطَلَبَ فَيُقاس **ما كُنّا سَنُرسِلُه**، والقَرارُ كُلُّه
// دَوالُّ نَقِيَّة. نَفسُ نَمَطِ `PayPalProviderTests` و
// `PayPalBillingPolicyTests` حَرفاً.
//
// **والمِحوَرُ الَّذي تَدور عَلَيه هذِه المِلَفّ**: قاعِدَةُ التَأكيد —
// **يُمَدِّدُ الباقَةَ حَدَثٌ واحِدٌ لا غَير**، وبِخَمسَةِ شُروطٍ
// مُجتَمِعَة. وكُلُّ فَرعٍ لا يُمَدِّد لَه اختِبارٌ **سالِبٌ** صَريح،
// لِأَنّ العَطَبَ المُكلِفَ هُنا لَيسَ «لَم يُمَدَّد» بَل «مُدِّدَ بِلا
// مال».

/// <summary>مُعالِجٌ يَلتَقِط كُلَّ الطَلَبات ويَرُدُّ رُدوداً
/// مُرَتَّبَة — نُسخَةٌ مُستَقِلَّةٌ لِأَنّ نَظيرَه في
/// <c>PayPalProviderTests</c> مَحصورٌ بِمِلَفِّه
/// (<c>file sealed</c>).</summary>
file sealed class OrderHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public OrderHandler Then(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    public OrderHandler ThenToken()
        => Then(HttpStatusCode.OK, "{\"access_token\":\"A21AA\",\"expires_in\":32400}");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _replies.Count > 0 ? _replies.Dequeue() : (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(body) };
    }
}

public class PayPalOrderTests
{
    private static readonly DateTime Now = new(2026, 08, 24, 12, 00, 00, DateTimeKind.Utc);

    private static PayPalOptions Opts() => new()
    {
        ClientId = "AY-client", ClientSecret = "very-secret",
        Environment = PayPalEnvironment.Live, WebhookId = "WH-TEST", TimeoutSeconds = 5,
    };

    private static PayPalPaymentProvider Provider(HttpMessageHandler handler)
        => new(Options.Create(Opts()), new HttpClient(handler),
               new PayPalTokenCache(), NullLogger<PayPalPaymentProvider>.Instance);

    private static PayPalOrderDraft Draft(
        string slug = "ejar", decimal amount = 49m, string currency = "USD",
        int days = 30, string description = "اشتِراكُ شَهر")
        => new(slug, "manual", amount, currency, days, description);

    /// <summary>باقَةُ شَهرٍ سارِيَة: تَنتَهي بَعدَ ‏10 أَيّام.</summary>
    private static TenantPlan Plan(string slug = "ejar") => new()
    {
        Id = slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(10), GraceDays = 14,
    };

    private static PayPalOrderRecord Order(
        string status = PayPalOrderStatuses.Created,
        decimal amount = 49m, string currency = "USD", int days = 30)
        => new()
        {
            Id = "wsl-ejar-abc123", TenantSlug = "ejar", PlanId = "manual",
            Amount = amount, Currency = currency, Days = days,
            OrderId = "5O190127TN364715T", ApproveUrl = "https://www.paypal.com/checkoutnow?token=5O1",
            Status = status, CreatedAt = Now, At = Now,
        };

    // ═══ ١. إنشاءُ الطَلَب — ما يُرسَل بِالضَبط ═══════════════════════

    [Fact]
    public async Task CreateOrder_PostsToOrdersPath_WithBearerAndTheIdempotencyHeader()
    {
        var handler = new OrderHandler()
            .ThenToken()
            .Then(HttpStatusCode.Created,
                """
                {"id":"5O1","status":"PAYER_ACTION_REQUIRED",
                 "links":[{"rel":"self","href":"https://x/self"},
                          {"rel":"payer-action","href":"https://www.paypal.com/checkoutnow?token=5O1"}]}
                """);

        var draft = Draft();
        var result = await Provider(handler).CreateOrderAsync(
            draft, PayPalOrderPolicy.Reference(draft), "https://wasayel.test",
            PayPalOrderPolicy.OrderRequestId(draft));

        Assert.Equal("5O1", result.OrderId);
        Assert.Equal("https://www.paypal.com/checkoutnow?token=5O1", result.ApproveUrl);
        Assert.Null(result.FailureReason);

        var req = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(PayPalEnvironment.LiveBaseUrl + PayPalPaymentProvider.OrdersPath,
            req.RequestUri!.ToString());
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal(PayPalOrderPolicy.OrderRequestId(draft),
            Assert.Single(req.Headers.GetValues(PayPalPaymentProvider.RequestIdHeader)));
    }

    /// <summary><b>المَبلَغُ نَصٌّ لا رَقَم</b> — نَمَطُ PayPal يَشتَرِط
    /// ذلك حَرفاً. ومَرجِعُنا في <c>custom_id</c> و<c>reference_id</c>
    /// مَعاً، فَهُوَ الرِباطُ الوَحيدُ بَينَ دافِعٍ في PayPal ومَتجَرٍ
    /// عِندَنا.</summary>
    [Fact]
    public async Task CreateOrder_SendsTheAmountAsAString_AndOurReferenceInCustomId()
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created, "{\"id\":\"5O1\"}");
        var draft = Draft(amount: 49m);
        var reference = PayPalOrderPolicy.Reference(draft);

        await Provider(handler).CreateOrderAsync(
            draft, reference, "https://wasayel.test", "k");

        using var body = JsonDocument.Parse(handler.Bodies[1]!);
        var root = body.RootElement;

        Assert.Equal(PayPalOrderPolicy.Intent, root.GetProperty("intent").GetString());

        var unit = root.GetProperty("purchase_units")[0];
        var amount = unit.GetProperty("amount");
        Assert.Equal(JsonValueKind.String, amount.GetProperty("value").ValueKind);
        Assert.Equal("49.00", amount.GetProperty("value").GetString());
        Assert.Equal("USD", amount.GetProperty("currency_code").GetString());
        Assert.Equal(reference, unit.GetProperty("custom_id").GetString());
        Assert.Equal(reference, unit.GetProperty("reference_id").GetString());
        Assert.Equal("اشتِراكُ شَهر", unit.GetProperty("description").GetString());
        Assert.Equal(PayPalOrderPolicy.SoftDescriptor, unit.GetProperty("soft_descriptor").GetString());
    }

    /// <summary><b>سِياقُ التَجرِبَةِ لا الكُتلَتانِ المَهجورَتان</b>:
    /// <c>application_context</c> و<c>payer</c> مَوسومَتانِ
    /// <c>DEPRECATED</c> بِالكامِل مُنذُ ‏2.9. و<c>breakdown</c> غائِبٌ
    /// لِأَنَّه يَجِب أَن يَتَوازَن وإلّا ‏422، و<c>invoice_id</c> غائِبٌ
    /// لِأَنّ مَرجِعَنا حَتميٌّ فَيَرتَدُّ بِـ<c>DUPLICATE_INVOICE_ID</c>
    /// في الدَورَةِ التالِيَة.</summary>
    [Fact]
    public async Task CreateOrder_UsesExperienceContext_AndOmitsTheDeprecatedAndTrapFields()
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created, "{\"id\":\"5O1\"}");
        var draft = Draft();
        var reference = PayPalOrderPolicy.Reference(draft);

        await Provider(handler).CreateOrderAsync(draft, reference, "https://wasayel.test/", "k");

        using var body = JsonDocument.Parse(handler.Bodies[1]!);
        var root = body.RootElement;

        Assert.False(root.TryGetProperty("application_context", out _));
        Assert.False(root.TryGetProperty("payer", out _));

        var unit = root.GetProperty("purchase_units")[0];
        Assert.False(unit.GetProperty("amount").TryGetProperty("breakdown", out _));
        Assert.False(unit.TryGetProperty("invoice_id", out _));

        var ctx = root.GetProperty("payment_source").GetProperty("paypal")
                      .GetProperty("experience_context");
        Assert.Equal(PayPalOrderPolicy.UserAction, ctx.GetProperty("user_action").GetString());
        Assert.Equal(PayPalOrderPolicy.PaymentMethodPreference,
            ctx.GetProperty("payment_method_preference").GetString());
        Assert.Equal(PayPalOrderPolicy.ShippingPreference,
            ctx.GetProperty("shipping_preference").GetString());
        Assert.Equal(PayPalOrderPolicy.Locale, ctx.GetProperty("locale").GetString());

        // ورابِطا العَودَةِ والإلغاءِ يَحمِلانِ مَرجِعَنا — فَلا تُقرَأُ
        // صَفحَةُ العَودَةِ مِن `token` الَّذي تُلحِقُه PayPal.
        Assert.Equal($"https://wasayel.test{PayPalOrderPolicy.ReturnPath}?ref={Uri.EscapeDataString(reference)}",
            ctx.GetProperty("return_url").GetString());
        Assert.Equal($"https://wasayel.test{PayPalOrderPolicy.CancelPath}?ref={Uri.EscapeDataString(reference)}",
            ctx.GetProperty("cancel_url").GetString());
    }

    /// <summary>وَصفٌ فارِغٌ لا يُرسَل — حَقلٌ اختِياريٌّ يُملَأُ بِنَصٍّ
    /// مُخترَعٍ بَياناتُ صَفقَةٍ لا تُخترَع.</summary>
    [Fact]
    public async Task CreateOrder_OmitsAnEmptyDescription()
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created, "{\"id\":\"5O1\"}");
        var draft = Draft(description: "   ");

        await Provider(handler).CreateOrderAsync(
            draft, PayPalOrderPolicy.Reference(draft), "https://x", "k");

        using var body = JsonDocument.Parse(handler.Bodies[1]!);
        Assert.False(body.RootElement.GetProperty("purchase_units")[0]
            .TryGetProperty("description", out _));
    }

    // ─── الرابِطُ يُستَخرَج مِن الحَقلِ الصَحيح ────────────────────────

    /// <summary><b>وَسمانِ لا واحِد.</b> طَلَبٌ فيه
    /// <c>experience_context</c> — وهُوَ شَكلُنا — يَرُدّ
    /// <c>payer-action</c> و<c>approve</c> غائِبٌ أَصلاً؛ وطَلَبٌ بِلا
    /// <c>payment_source</c> يَرُدّ <c>approve</c>. والوَثائِقُ نَفسُها
    /// غَيرُ مُتَّسِقَة، فَلا يُبنى الكودُ على اسمٍ واحِد.</summary>
    [Theory]
    [InlineData("payer-action")]
    [InlineData("approve")]
    [InlineData("APPROVE")]
    public async Task CreateOrder_TakesTheHostedLink_UnderEitherRel(string rel)
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created,
            $$"""
            {"id":"5O1","links":[{"rel":"self","href":"https://x/self"},
                                 {"rel":"{{rel}}","href":"https://www.paypal.com/checkoutnow?token=5O1"}]}
            """);

        var result = await Provider(handler).CreateOrderAsync(Draft(), "r", "https://x", "k");
        Assert.Equal("https://www.paypal.com/checkoutnow?token=5O1", result.ApproveUrl);
    }

    [Fact]
    public async Task CreateOrder_HasNoLink_WhenPayPalSendsNone()
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created,
            "{\"id\":\"5O1\",\"links\":[{\"rel\":\"self\",\"href\":\"https://x/self\"}]}");

        var result = await Provider(handler).CreateOrderAsync(Draft(), "r", "https://x", "k");
        Assert.Null(result.ApproveUrl);

        // ورابِطٌ لَم تُعِدهُ PayPal **لا يُخَزَّن** — ولا يُصنَع رابِطٌ
        // فارِغٌ يَنقُرُه رائِدُ الأَعمالِ فَلا يَصِل شَيئاً.
        Assert.False(PayPalBillingService.SaveOrder(
            session: null!, PayPalOrderSurface.RecordFor(Draft(), "r", result, "by", Now)));
    }

    [Fact]
    public async Task CreateOrder_ReportsFailure_WithoutThrowing_NamingPayPalsCode()
    {
        var handler = new OrderHandler().ThenToken()
            .Then(HttpStatusCode.UnprocessableEntity,
                "{\"name\":\"UNPROCESSABLE_ENTITY\",\"details\":[{\"issue\":\"CURRENCY_NOT_SUPPORTED\"}]}");

        var result = await Provider(handler).CreateOrderAsync(Draft(), "r", "https://x", "k");

        Assert.Equal("", result.OrderId);
        Assert.Null(result.ApproveUrl);
        Assert.Contains("422", result.FailureReason);
        Assert.Contains("CURRENCY_NOT_SUPPORTED", result.FailureReason);
        // ولا سِرَّ في رِسالَةِ خَطَإ — جِسمُ رَدِّ PayPal وَحدَه.
        Assert.DoesNotContain("very-secret", result.FailureReason);
    }

    // ═══ ٢. مَرَّة-واحِدَة: حَتمِيَّةٌ لا زَمَنِيَّة ═══════════════════

    /// <summary><b>نَقرَتانِ لا تُنشِئانِ طَلَبَي دَفع</b> — والمِفتاحُ
    /// مُشتَقٌّ مِن المُدخَلاتِ وَحدَها، لا مِن زَمَنٍ ولا
    /// عَشوائيَّة.</summary>
    [Fact]
    public void TheIdempotencyKey_IsDerivedFromTheInputsAlone()
    {
        Assert.Equal(PayPalOrderPolicy.OrderRequestId(Draft()),
                     PayPalOrderPolicy.OrderRequestId(Draft()));
        Assert.Equal(PayPalOrderPolicy.Reference(Draft()), PayPalOrderPolicy.Reference(Draft()));
    }

    /// <summary>وحَقلٌ يَتَغَيَّر ⇒ مِفتاحٌ آخَر ⇒ طَلَبٌ جَديدٌ حينَ
    /// يُرادُ فِعلاً.</summary>
    [Fact]
    public void EveryFieldOfTheDraft_ChangesTheKey()
    {
        var baseline = PayPalOrderPolicy.OrderRequestId(Draft());
        var variants = new[]
        {
            PayPalOrderPolicy.OrderRequestId(Draft(slug: "other")),
            PayPalOrderPolicy.OrderRequestId(Draft(amount: 50m)),
            PayPalOrderPolicy.OrderRequestId(Draft(currency: "EUR")),
            PayPalOrderPolicy.OrderRequestId(Draft(days: 31)),
            PayPalOrderPolicy.OrderRequestId(Draft(description: "آخَر")),
        };

        Assert.All(variants, v => Assert.NotEqual(baseline, v));
        Assert.Equal(variants.Length, variants.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary><b>وحَدّانِ لا واحِد</b>: المُخَطَّطُ يَقول ‏108
    /// ودَليلُ الـidempotency العامُّ يَذكُر ‏38 — والمِفتاحُ تَحتَهُما
    /// مَعاً. و<c>custom_id</c> سَقفُه ‏255.</summary>
    [Fact]
    public void TheKeys_FitEveryPublishedLimit()
    {
        Assert.InRange(PayPalOrderPolicy.OrderRequestId(Draft()).Length, 1, 38);
        Assert.InRange(PayPalOrderPolicy.CaptureRequestId("wsl-ejar-abc").Length, 1, 38);
        Assert.InRange(PayPalOrderPolicy.Reference(Draft()).Length, 1, 255);
        Assert.Contains("ejar", PayPalOrderPolicy.Reference(Draft()));
    }

    /// <summary><b>ومِفتاحُ الالتِقاطِ ثابِتٌ عَبرَ كُلِّ إعادَةِ
    /// مُحاوَلَة</b> — وهُوَ ما يَمنَع التِقاطَ المَبلَغِ مَرَّتَينِ
    /// بِنَصِّ تَوجيهِ PayPal.</summary>
    [Fact]
    public void TheCaptureKey_IsStableForTheSameOrder_AndDiffersAcrossOrders()
    {
        Assert.Equal(PayPalOrderPolicy.CaptureRequestId("wsl-a-1"),
                     PayPalOrderPolicy.CaptureRequestId("wsl-a-1"));
        Assert.NotEqual(PayPalOrderPolicy.CaptureRequestId("wsl-a-1"),
                        PayPalOrderPolicy.CaptureRequestId("wsl-a-2"));
    }

    /// <summary>
    /// <para><b>والعَطَبُ القائِمُ في مَسارِ الاشتِراكِ أُصلِح</b>:
    /// <c>PayPalSurface.LinkKey</c> كانَ <c>plan-link:{slug}:{HHmm}</c> —
    /// مِفتاحُ مَرَّة-واحِدَةٍ يَحمِلُ الساعَة، فَنَقرَتانِ في
    /// دَقيقَتَينِ مُختَلِفَتَينِ تُنشِئانِ اشتِراكَين.</para>
    /// </summary>
    [Fact]
    public void TheSubscriptionLinkKey_NoLongerCarriesTheClock()
    {
        var a = PayPalSurface.LinkKey("ejar", "P-9XYZ");
        var b = PayPalSurface.LinkKey("ejar", "P-9XYZ");

        Assert.Equal(a, b);
        Assert.NotEqual(a, PayPalSurface.LinkKey("ejar", "P-OTHER"));
        Assert.DoesNotContain(Now.ToString("yyyyMMdd"), a);
        Assert.InRange(a.Length, 1, 38);
    }

    // ═══ ٣. الالتِقاط ═════════════════════════════════════════════════

    [Fact]
    public async Task Capture_PostsAnEmptyBody_WithPreferRepresentation_AndTheSameRequestId()
    {
        var handler = new OrderHandler().ThenToken().Then(HttpStatusCode.Created,
            """
            {"id":"5O1","status":"COMPLETED","purchase_units":[{"payments":{"captures":[
              {"id":"3C6","status":"COMPLETED","amount":{"currency_code":"USD","value":"49.00"},
               "seller_receivable_breakdown":{"net_amount":{"currency_code":"USD","value":"46.30"}}}]}}]}
            """);

        var key = PayPalOrderPolicy.CaptureRequestId("wsl-ejar-abc");
        var result = await Provider(handler).CaptureOrderAsync("5O1", key);

        Assert.Equal("3C6", result.CaptureId);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("46.30", result.NetAmount);
        Assert.Null(result.FailureReason);

        var req = handler.Requests[1];
        Assert.EndsWith(PayPalPaymentProvider.CapturePathFor("5O1"), req.RequestUri!.AbsolutePath);
        Assert.Equal("{}", handler.Bodies[1]);
        Assert.Equal(key, Assert.Single(req.Headers.GetValues(PayPalPaymentProvider.RequestIdHeader)));
        Assert.Equal(PayPalPaymentProvider.PreferRepresentation,
            Assert.Single(req.Headers.GetValues(PayPalPaymentProvider.PreferHeader)));
    }

    [Fact]
    public async Task Capture_ReportsFailure_WithoutThrowing()
    {
        var handler = new OrderHandler().ThenToken()
            .Then(HttpStatusCode.Conflict, "{\"name\":\"ORDER_ALREADY_CAPTURED\"}");

        var result = await Provider(handler).CaptureOrderAsync("5O1", "k");
        Assert.Contains("409", result.FailureReason);
        Assert.Contains("ORDER_ALREADY_CAPTURED", result.FailureReason);
    }

    // ═══ ٤. القِراءَة — مَسارُ الحُقولِ يَتَبَدَّلُ بَينَ الشَكلَين ════

    private const string CaptureCompletedJson =
        """
        {"id":"WH-CAP-1","event_type":"PAYMENT.CAPTURE.COMPLETED",
         "resource":{"id":"3C6","status":"COMPLETED","custom_id":"wsl-ejar-abc123",
                     "amount":{"currency_code":"USD","value":"49.00"},
                     "seller_receivable_breakdown":{"net_amount":{"currency_code":"USD","value":"46.30"}},
                     "supplementary_data":{"related_ids":{"order_id":"5O190127TN364715T"}},
                     "links":[{"rel":"up","href":"https://api-m.paypal.com/v2/checkout/orders/5O190127TN364715T"}]}}
        """;

    [Fact]
    public void Parse_ReadsTheCaptureShape_FromTheRoot()
    {
        var e = PayPalOrderBillingPolicy.Parse(CaptureCompletedJson);

        Assert.NotNull(e);
        Assert.Equal("WH-CAP-1", e!.EventId);
        Assert.Equal(PayPalOrderEventTypes.CaptureCompleted, e.EventType);
        Assert.Equal("wsl-ejar-abc123", e.Reference);
        Assert.Equal("3C6", e.CaptureId);
        Assert.Equal("5O190127TN364715T", e.OrderId);
        Assert.Equal("COMPLETED", e.ResourceStatus);
        Assert.Equal("49.00", e.Amount);
        Assert.Equal("USD", e.Currency);
        Assert.Equal("46.30", e.NetAmount);
    }

    /// <summary>وشَكلُ الطَلَبِ يَحمِلُ المَرجِعَ <b>داخِلَ
    /// مَصفوفَة</b> — وقِراءَةُ الجَذرِ وَحدَه تَجعَلُ نِصفَ الأَحداثِ
    /// «مَرجِعاً مَجهولاً».</summary>
    [Fact]
    public void Parse_ReadsTheOrderShape_FromInsideThePurchaseUnit()
    {
        var e = PayPalOrderBillingPolicy.Parse(
            """
            {"id":"WH-ORD-1","event_type":"CHECKOUT.ORDER.APPROVED",
             "resource":{"id":"5O190127TN364715T","status":"APPROVED",
                         "purchase_units":[{"reference_id":"wsl-ejar-abc123",
                                            "custom_id":"wsl-ejar-abc123"}]}}
            """);

        Assert.Equal("wsl-ejar-abc123", e!.Reference);
        Assert.Equal("5O190127TN364715T", e.OrderId);
        Assert.Equal("APPROVED", e.ResourceStatus);
    }

    /// <summary>و<c>PAYMENT-APPROVAL.REVERSED</c> تُسَمّي مُعَرِّفَ
    /// الطَلَبِ <c>order_id</c> لا <c>id</c> — اسمٌ مُختَلِفٌ في
    /// عائِلَةٍ واحِدَة.</summary>
    [Fact]
    public void Parse_ReadsTheOrderId_UnderItsOtherName()
    {
        var e = PayPalOrderBillingPolicy.Parse(
            """
            {"id":"WH-REV-1","event_type":"CHECKOUT.PAYMENT-APPROVAL.REVERSED",
             "resource":{"order_id":"5O190127TN364715T",
                         "purchase_units":[{"custom_id":"wsl-ejar-abc123"}]}}
            """);

        Assert.Equal("5O190127TN364715T", e!.OrderId);
    }

    /// <summary><b>والاستِردادُ مَورِدُه كائِنُ Refund لا Capture</b>:
    /// <c>resource.id</c> مُعَرِّفُ استِرداد، والمِفتاحُ الصالِحُ
    /// <c>links[rel=up]</c>.</summary>
    [Fact]
    public void Parse_ReadsTheCaptureId_FromTheUpLink_OnARefund()
    {
        var e = PayPalOrderBillingPolicy.Parse(
            """
            {"id":"WH-REF-1","event_type":"PAYMENT.CAPTURE.REFUNDED",
             "resource":{"id":"1JU08902781691411","status":"COMPLETED",
                         "amount":{"currency_code":"USD","value":"49.00"},
                         "links":[{"rel":"up","href":"https://api-m.paypal.com/v2/payments/captures/3C6"}]}}
            """);

        Assert.Equal("3C6", e!.UpCaptureId);
        Assert.Null(e.Reference);
    }

    /// <summary><b>وأَحداثُ الاشتِراكاتِ تَمُرُّ مِن هُنا بِلا لَمس</b> —
    /// <c>null</c> تَعني «لَيسَ حَدَثَ طَلَب»، فَيَنزِل الجِسمُ إلى
    /// المَسارِ القائِمِ بِلا تَغييرِ حَرف.</summary>
    [Theory]
    [InlineData(PayPalEventTypes.SubscriptionActivated)]
    [InlineData(PayPalEventTypes.PaymentSaleCompleted)]
    [InlineData(PayPalEventTypes.SubscriptionCancelled)]
    [InlineData(PayPalEventTypes.SubscriptionSuspended)]
    [InlineData("CHECKOUT.ORDER.COMPLETED")]
    public void Parse_LeavesEveryNonOrderEvent_ToTheSubscriptionsPath(string type)
        => Assert.Null(PayPalOrderBillingPolicy.Parse(
            "{\"id\":\"WH-1\",\"event_type\":\"" + type + "\",\"resource\":{\"custom_id\":\"ejar\"}}"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"}")]
    [InlineData("{\"id\":\"WH-1\"}")]
    public void Parse_ReturnsNull_AndNeverThrows(string body)
        => Assert.Null(PayPalOrderBillingPolicy.Parse(body));

    // ═══ ٥. قاعِدَةُ التَأكيد — حَدَثٌ واحِدٌ يُمَدِّد ═════════════════

    private static PayPalOrderEvent Event(
        string type = PayPalOrderEventTypes.CaptureCompleted,
        string id = "WH-CAP-1", string? reference = "wsl-ejar-abc123",
        string? status = "COMPLETED", string? amount = "49.00", string? currency = "USD",
        string? upCapture = null)
        => new(id, type, reference, "5O190127TN364715T", "3C6", upCapture,
               status, null, amount, currency, "46.30");

    /// <summary><b>★ الحَدَثُ الوَحيدُ الَّذي يُمَدِّد</b> — وبِعَدَدِ
    /// الأَيّامِ المَحفوظِ في وَثيقَةِ الدَفع، لا بِرَقَمٍ تَقولُه
    /// PayPal ولا بِواحِدٍ يُحسَب.</summary>
    [Fact]
    public void CaptureCompleted_ExtendsByTheStoredDays()
    {
        var plan = Plan();
        var d = PayPalOrderBillingPolicy.Decide(Event(), Order(days: 30), plan, false, Now);

        Assert.Equal(PayPalOrderAction.Extend, d.Action);
        Assert.True(d.TouchesPlan);
        // المِرساةُ `max(الآن, ExpiresAt)` — مَن جَدَّدَ مُبَكِّراً لا
        // يُصادَر ما تَبَقّى لَه.
        Assert.Equal(plan.ExpiresAt.AddDays(30), d.NewExpiresAt);
        Assert.Equal(PayPalOrderStatuses.Captured, d.OrderStatus);
    }

    /// <summary>ومَن عادَ بَعدَ انقِطاعٍ لا يُشتَرى لَه ماضٍ مَضى.</summary>
    [Fact]
    public void CaptureCompleted_AnchorsOnToday_WhenThePlanAlreadyLapsed()
    {
        var plan = Plan();
        plan.ExpiresAt = Now.AddDays(-60);

        var d = PayPalOrderBillingPolicy.Decide(Event(), Order(days: 30), plan, false, Now);
        Assert.Equal(Now.AddDays(30), d.NewExpiresAt);
    }

    /// <summary><b>مُوافَقَةٌ لا مال.</b> نَصُّ PayPal: «‏Listen for this
    /// webhook and <b>then capture the payment</b>» — وهي حالَةٌ
    /// <b>تَنتَهي صَلاحِيَّتُها</b>: طَلَبٌ لا يُلتَقَط تُلغيه PayPal
    /// وتُعيدُ المالَ بَعدَ نافِذَتِه. فَتَمديدٌ مَبنيٌّ عَلَيها يُعطي
    /// باقَةً لِمالٍ اُستُرِدّ.</summary>
    [Fact]
    public void OrderApproved_NeverExtends_ItOnlyTriggersTheCapture()
    {
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.OrderApproved, status: "APPROVED"),
            Order(), Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.Capture, d.Action);
        Assert.False(d.TouchesPlan);
    }

    /// <summary><b>مَمنوعٌ بِنَصٍّ صَريح</b>: «‏Do not fulfill the order
    /// until payment completion is successful».</summary>
    [Fact]
    public void CapturePending_NeverExtends()
    {
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.CapturePending, status: "PENDING"),
            Order(), Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.MarkOrder, d.Action);
        Assert.False(d.TouchesPlan);
        Assert.Equal(PayPalOrderStatuses.Pending, d.OrderStatus);
    }

    [Fact]
    public void CaptureDenied_NeverExtends_AndNeverTouchesThePlan()
    {
        var plan = Plan();
        var before = plan.ExpiresAt;
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.CaptureDenied, status: "DECLINED"),
            Order(), plan, false, Now);

        Assert.Equal(PayPalOrderAction.MarkOrder, d.Action);
        Assert.False(d.TouchesPlan);
        Assert.Equal(PayPalOrderStatuses.Denied, d.OrderStatus);
        Assert.Equal(before, plan.ExpiresAt);
    }

    /// <summary><b>اسمُ الحَدَثِ دَعوى، والحَقلُ واقِعَة.</b> حَدَثٌ
    /// اسمُه <c>COMPLETED</c> و<c>resource.status</c> يَقول غَيرَ ذلك
    /// <b>لا يُمَدِّد</b> — وهذا شَرطٌ ثانٍ مُستَقِلٌّ عَن الاسم.</summary>
    [Theory]
    [InlineData("PENDING")]
    [InlineData("DECLINED")]
    [InlineData("FAILED")]
    [InlineData(null)]
    public void AnEventNamedCompleted_WithAnotherResourceStatus_NeverExtends(string? status)
    {
        var d = PayPalOrderBillingPolicy.Decide(Event(status: status), Order(), Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.StatusNotCompleted, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>دَفعٌ بِمَبلَغٍ أَقَلَّ لا يُمَدِّد.</b> والمَبلَغُ
    /// يُقارَنُ ولا يُفتَرَض.</summary>
    [Theory]
    [InlineData("10.00", "USD")]     // أَقَلّ
    [InlineData("99.00", "USD")]     // أَكثَر — مُعامَلَةٌ لَيسَت هذِه
    [InlineData("49.00", "EUR")]     // عُملَةٌ أُخرى
    [InlineData("لا رَقَم", "USD")]   // نَصٌّ غَيرُ مَقروء
    [InlineData(null, null)]
    public void AMismatchedAmountOrCurrency_NeverExtends(string? amount, string? currency)
    {
        var d = PayPalOrderBillingPolicy.Decide(
            Event(amount: amount, currency: currency), Order(amount: 49m, currency: "USD"),
            Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.AmountMismatch, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>مَرجِعٌ لا وَثيقَةَ لَه ⇒ صِفرُ كِتابَةٍ وسَطرُ
    /// خَطَإ.</b> ولا تُخترَعُ لَه وَثيقَة.</summary>
    [Fact]
    public void AnUnknownReference_WritesNothing_AndIsLoggedAsAnError()
    {
        var e = Event(reference: "wsl-nobody-000");
        var d = PayPalOrderBillingPolicy.Decide(e, order: null, Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.UnknownReference, d.Action);
        Assert.False(d.Writes);
        Assert.Contains("wsl-nobody-000", d.ReasonAr);

        var log = new CapturingLogger();
        var result = PayPalOrderSurface.NoWrite(log, e, d);

        Assert.Single(log.Lines.Where(l => l.Level == Microsoft.Extensions.Logging.LogLevel.Error));
        // ‏200 لا خَطَأ: الرِسالَةُ فُهِمَت وقَرارُنا أَلّا نَفعَل —
        // ورَدُّ خَطَإٍ يَجعَل PayPal تُعيدُها ‏25 مَرَّةً في ثَلاثَةِ
        // أَيّام.
        Assert.Equal(StatusCodes.Status200OK,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>ومَرجِعٌ لَه وَثيقَةُ دَفعٍ ولا وَثيقَةَ باقَةٍ
    /// لِمَتجَرِه — لا كِتابَة كَذلك.</summary>
    [Fact]
    public void AReferenceWithoutAPlanDocument_WritesNothing()
    {
        var d = PayPalOrderBillingPolicy.Decide(Event(), Order(), plan: null, false, Now);

        Assert.Equal(PayPalOrderAction.UnknownTenant, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>وتَكرارُ الحَدَثِ لا يُمَدِّد ثانِيَةً</b> — ‏PayPal
    /// تُعيد الإرسالَ ‏25 مَرَّةً خِلالَ ثَلاثَةِ أَيّام.</summary>
    [Fact]
    public void TheSameEventTwice_ExtendsExactlyOnce()
    {
        var plan = Plan();
        var order = Order();
        var e = Event();

        var first = PayPalOrderBillingPolicy.Decide(e, order, plan, alreadySeen: false, Now);
        Assert.Equal(PayPalOrderAction.Extend, first.Action);
        PayPalBillingPolicy.Apply(plan, new PayPalWebhookEvent(e.EventId, e.EventType, "ejar", null, null),
            new PayPalBillingDecision(PayPalBillingAction.Extend, first.NewExpiresAt, first.ReasonAr), Now);
        var afterFirst = plan.ExpiresAt;

        var second = PayPalOrderBillingPolicy.Decide(e, order, plan, alreadySeen: true, Now);

        Assert.Equal(PayPalOrderAction.Replay, second.Action);
        Assert.False(second.Writes);
        Assert.Equal(afterFirst, plan.ExpiresAt);
    }

    // ═══ ٦. ما يَسحَب ═════════════════════════════════════════════════

    [Theory]
    [InlineData(PayPalOrderEventTypes.CaptureRefunded)]
    [InlineData(PayPalOrderEventTypes.CaptureReversed)]
    public void ARefundOrReversal_WithdrawsExactlyTheDaysItGranted(string type)
    {
        var plan = Plan();
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: type, reference: null, upCapture: "3C6"),
            Order(status: PayPalOrderStatuses.Captured, days: 30), plan, false, Now);

        Assert.Equal(PayPalOrderAction.Withdraw, d.Action);
        Assert.Equal(plan.ExpiresAt.AddDays(-30), d.NewExpiresAt);

        PayPalBillingPolicy.Apply(plan,
            new PayPalWebhookEvent("WH-REF", type, "ejar", null, null),
            new PayPalBillingDecision(PayPalBillingAction.Withdraw, d.NewExpiresAt, d.ReasonAr), Now);

        Assert.Equal(Now.AddDays(-20), plan.ExpiresAt);
        // **ولا يُطفَأُ مَتجَرٌ بِسَحب**: الحالَةُ نِيَّةُ المُشرِفِ
        // ولا تُمَسّ، والإخفاءُ يَقَع مِن الوَقتِ وَحدَه.
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);
    }

    /// <summary><b>ولا يُسحَبُ ما لَم يُمنَح</b>: طَلَبٌ لَم يَبلُغ
    /// <c>captured</c> لَم يُحَرِّك تاريخاً، فَسَحبُه يُصادِر مُدَّةً
    /// اشتُرِيَت بِطَلَبٍ آخَر.</summary>
    [Fact]
    public void ARefund_OnAnOrderThatNeverGranted_MarksOnly()
    {
        var plan = Plan();
        var before = plan.ExpiresAt;
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.CaptureRefunded),
            Order(status: PayPalOrderStatuses.Denied), plan, false, Now);

        Assert.Equal(PayPalOrderAction.MarkOrder, d.Action);
        Assert.False(d.TouchesPlan);
        Assert.Equal(before, plan.ExpiresAt);
    }

    /// <summary><b>وإيقافُ المُشرِفِ اليَدَوِيُّ فَوقَ كُلِّ دَفعَة</b>:
    /// مَن أُوقِفَ لِسَبَبٍ لا يُعيدُه دَفعُ مالٍ وَحدَه — وتَرتيبُ
    /// فُروعِ <c>Derive</c> يَضمَنُه بِلا سَطرٍ جَديد.</summary>
    [Fact]
    public void APaidOrder_NeverResurrectsAManuallyStoppedPlan()
    {
        var plan = Plan();
        plan.Status = PlatformPlanStatuses.Stopped;
        var e = Event();

        var d = PayPalOrderBillingPolicy.Decide(e, Order(), plan, false, Now);
        PayPalBillingPolicy.Apply(plan,
            new PayPalWebhookEvent(e.EventId, e.EventType, "ejar", null, null),
            new PayPalBillingDecision(PayPalBillingAction.Extend, d.NewExpiresAt, d.ReasonAr), Now);

        Assert.Equal(PlatformPlanStatuses.Stopped, plan.Status);
        Assert.Equal(TenantPlanState.Suspended, TenantPlanPolicy.Derive(plan, Now));
    }

    /// <summary>والتَمديدُ يُبقي الباقَةَ صالِحَةً بِمَعجَمِ المُصادِقِ
    /// القائِم — فَلا يُنتِج هذا المَسارُ وَثيقَةً تَرفُضُها شاشَةُ
    /// المُشرِف.</summary>
    [Fact]
    public void AnExtendedPlan_StaysValidByTheExistingValidator()
    {
        var plan = Plan();
        var d = PayPalOrderBillingPolicy.Decide(Event(), Order(), plan, false, Now);
        PayPalBillingPolicy.Apply(plan,
            new PayPalWebhookEvent("WH-1", PayPalOrderEventTypes.CaptureCompleted, "ejar", null, null),
            new PayPalBillingDecision(PayPalBillingAction.Extend, d.NewExpiresAt, d.ReasonAr), Now);

        Assert.True(TenantPlanPolicy.IsValid(plan));
    }

    // ═══ ٧. «صِفرُ كِتابَة» — بُرهانٌ بِنيَوِيٌّ لا مُلاحَظَة ═════════

    /// <summary>
    /// <para><b>قَرارٌ لا يَكتُب لا يَلمِس الجَلسَةَ إطلاقاً</b> —
    /// والبُرهان: تُمَرَّرُ الجَلسَةُ <c>null</c>. فَلَو لَمَسَتها
    /// الخِدمَةُ لَانفَجَرَت. وهذا أَقوى مِن «فَحَصنا القاعِدَةَ فَلَم
    /// نَجِد صَفّاً»: ذاكَ يَفحَص نَتيجَةً، وهذا يَفحَص أَنّ الطَريقَ
    /// نَفسَه مَقطوع.</para>
    /// </summary>
    [Theory]
    [InlineData(PayPalOrderAction.Replay)]
    [InlineData(PayPalOrderAction.Ignored)]
    [InlineData(PayPalOrderAction.UnknownReference)]
    [InlineData(PayPalOrderAction.StatusNotCompleted)]
    [InlineData(PayPalOrderAction.AmountMismatch)]
    [InlineData(PayPalOrderAction.UnknownTenant)]
    public void ANonWritingDecision_NeverTouchesTheSession(PayPalOrderAction action)
    {
        var decision = new PayPalOrderDecision(action, default, "", "—");
        Assert.False(decision.Writes);

        Assert.False(PayPalBillingService.ApplyOrder(
            session: null!, Plan(), Order(), Event(), decision, Now));
    }

    /// <summary>وقَرارٌ يَكتُب بِلا وَثيقَةِ دَفعٍ لا يَلمِسُها
    /// كَذلك — لا شَيءَ يُخترَع لِيُخَزَّن.</summary>
    [Fact]
    public void AWritingDecision_WithoutAnOrder_StillTouchesNothing()
    {
        var decision = new PayPalOrderDecision(
            PayPalOrderAction.Extend, Now.AddDays(30), PayPalOrderStatuses.Captured, "—");

        Assert.True(decision.Writes);
        Assert.False(PayPalBillingService.ApplyOrder(
            session: null!, Plan(), order: null, Event(), decision, Now));
    }

    // ═══ ٨. البَوّابَة: تَوقيعٌ فاشِلٌ ⇒ رَدٌّ بِلا كِتابَة ═══════════

    /// <summary><b>البابُ نَفسُه لا بابٌ ثانٍ</b>: مَسارُ الطَلَبات
    /// يَمُرُّ بِـ<c>PayPalBillingPolicy.Gate</c> بِعَينِها — رابِطٌ
    /// واحِدٌ ومُعَرِّفُ Webhook واحِد. وثَلاثٌ مِن أَربَعِ حالاتِها
    /// رَفض.</summary>
    [Theory]
    [InlineData(PayPalWebhookGate.NotConfigured)]
    [InlineData(PayPalWebhookGate.HeadersMissing)]
    [InlineData(PayPalWebhookGate.SignatureInvalid)]
    public void ARejectedGate_Answers400_AndTheOrderPathIsNeverReached(PayPalWebhookGate gate)
    {
        var result = PayPalSurface.Rejected(
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, gate);

        Assert.Equal(StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>وتَرتيبُ النُقطَةِ مَقيسٌ نَصِّيّاً: <b>البَوّابَةُ
    /// تَسبِق تَفريعَ مَسارِ الطَلَبات</b>، فَلا يُقرَأُ الجِسمُ
    /// كَبَياناتٍ قَبلَ أَن يَقولَ PayPal إنَّها مِنها.</summary>
    [Fact]
    public void TheWebhookEndpoint_GatesBeforeItBranchesIntoTheOrderPath()
    {
        var source = EndpointSource();
        var gate   = source.IndexOf("PayPalBillingPolicy.Gate", StringComparison.Ordinal);
        var branch = source.IndexOf("PayPalOrderBillingPolicy.Parse", StringComparison.Ordinal);

        Assert.True(gate > 0 && branch > 0, "أَداة عَمياء: لَم يُوجَد أَحَدُ الرَمزَين.");
        Assert.True(gate < branch,
            "تَفريعُ مَسارِ الطَلَباتِ يَسبِق البَوّابَة — الجِسمُ يُقرَأُ قَبلَ التَحَقُّق.");
    }

    // ═══ ٩. غَيرُ المُشرِفِ يُرفَض قَبلَ أَوَّلِ كِتابَة ══════════════

    /// <summary><b>التَخويلُ يَسبِق تَحَقُّقَ الحُقول</b> (القاعِدَة ٦)
    /// — وإلّا صارَ خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة. مَقيسٌ
    /// نَصِّيّاً على جِسمِ كُلِّ نُقطَةٍ جَديدَة.</summary>
    [Theory]
    [InlineData("/admin/tenants/{slug}/plan/paypal-order")]
    [InlineData("/admin/tenants/{slug}/plan/paypal-capture")]
    public void TheAdminOrderEndpoints_GuardBeforeReadingOrWritingAnything(string route)
    {
        var body = EndpointBody(route);

        var guard = body.IndexOf("PlatformAdminGuard.EvaluateAsync", StringComparison.Ordinal);
        Assert.True(guard >= 0, $"لا حارِسَ في جِسم «{route}».");

        foreach (var marker in new[] { "req.Form", "session.Store", "SaveChangesAsync",
                                       "CreateOrderAsync", "CaptureOrderAsync" })
        {
            var at = body.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            Assert.True(guard < at,
                $"«{marker}» يَسبِق الحارِسَ في جِسم «{route}».");
        }

        // والرَفضُ ‏403 صَريح، لا تَحويلٌ يَبدو نَجاحاً.
        Assert.Contains("Status403Forbidden", body);
    }

    // ═══ ١٠. البَوّابَةُ الحُقول: SAR مَرفوضَةٌ بِرِسالَةٍ تَقترِح USD ══

    [Fact]
    public void SAR_IsRejected_WithAMessageThatNamesTheAlternative()
    {
        var v = Assert.Single(PayPalOrderPolicy.Validate(Draft(currency: "SAR")));

        Assert.Equal(PayPalOrderPolicy.CurrencyUnsupported, v.Code);
        Assert.Contains("SAR", v.MessageAr);
        Assert.Contains(PayPalCurrencies.Default, v.MessageAr);
        Assert.False(PayPalOrderPolicy.IsValid(Draft(currency: "SAR")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PayPalOrderPolicy.MaxDays + 1)]
    public void ADurationOutsideTheDeclaredRange_IsRejected(int days)
        => Assert.Contains(PayPalOrderPolicy.DaysOutOfRange,
            PayPalOrderPolicy.Validate(Draft(days: days)).Select(v => v.Code));

    [Fact]
    public void AZeroAmount_IsRejected_AndSoIsAMissingPlan()
    {
        Assert.Contains(PayPalOrderPolicy.AmountNotPositive,
            PayPalOrderPolicy.Validate(Draft(amount: 0m)).Select(v => v.Code));

        Assert.Contains(PayPalOrderPolicy.PlanMissing,
            PayPalOrderPolicy.Validate(new PayPalOrderDraft("ejar", "", 49m, "USD", 30, ""))
                .Select(v => v.Code));
    }

    /// <summary><b>والحُقولُ تُقرَأُ ولا تُخمَّن</b>: رَقَمٌ غَيرُ
    /// مَقروءٍ يَسقُط إلى صِفرٍ <b>فَيَرتَدُّ بِخَرقٍ يُسَمّيه</b>، ولا
    /// «شَهرٌ افتِراضيّ» يُخترَع. والعُملَةُ الغائِبَةُ وَحدَها تَرتَدُّ
    /// إلى الافتِراضِ المَقيس.</summary>
    [Fact]
    public void ReadDraft_FallsToZero_AndOnlyTheCurrencyHasADefault()
    {
        var d = PayPalOrderPolicy.ReadDraft("EJAR", "manual", "كَذا", null, "", null);

        Assert.Equal("ejar", d.NormalizedSlug);
        Assert.Equal(0m, d.Amount);
        Assert.Equal(0, d.Days);
        Assert.Equal(PayPalCurrencies.Default, d.NormalizedCurrency);
        Assert.False(PayPalOrderPolicy.IsValid(d));
    }

    /// <summary>والوَصفُ يُقَصُّ عِندَ ‏127 — <b>والقَصُّ يَنعَكِس في
    /// الاستِجابَة</b>، فَقَصُّه عِندَنا يَجعَل ما نُرسِلُه هُوَ ما
    /// يُخَزَّن.</summary>
    [Fact]
    public void TheDescription_IsTrimmedAtTheLengthPayPalActuallyShows()
    {
        var long_ = new string('ب', PayPalOrderPolicy.MaxDescriptionLength + 40);
        Assert.Equal(PayPalOrderPolicy.MaxDescriptionLength,
            Draft(description: long_).TrimmedDescription.Length);
        Assert.Contains(PayPalOrderPolicy.DescriptionTooLong,
            PayPalOrderPolicy.Validate(Draft(description: long_)).Select(v => v.Code));
    }

    // ═══ ١١. المَعجَمُ مُغلَق، ولا يُخلَط بِمَعجَمِ الاشتِراكات ═══════

    [Fact]
    public void TheTwoVocabularies_DoNotOverlap()
    {
        Assert.Equal(7, PayPalOrderEventTypes.All.Count);
        Assert.Equal(4, PayPalEventTypes.All.Count);
        Assert.Empty(PayPalOrderEventTypes.All.Intersect(PayPalEventTypes.All, StringComparer.Ordinal));

        Assert.All(PayPalEventTypes.All, t => Assert.False(PayPalOrderEventTypes.Handles(t)));
        Assert.All(PayPalOrderEventTypes.All, t =>
        {
            Assert.False(PayPalEventTypes.Extends(t));
            Assert.False(PayPalEventTypes.StopsRenewal(t));
        });
    }

    [Fact]
    public void EveryOrderStatus_IsDistinct_AndOnlyThreeAwaitCapture()
    {
        Assert.Equal(PayPalOrderStatuses.All.Count,
            PayPalOrderStatuses.All.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal(3, PayPalOrderStatuses.All.Count(PayPalOrderStatuses.AwaitsCapture));
        Assert.False(PayPalOrderStatuses.AwaitsCapture(PayPalOrderStatuses.Captured));
    }

    // ─── أَدَوات ─────────────────────────────────────────────────────

    private const string EndpointsFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PayPalEndpoints.cs";

    private static string EndpointSource()
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, EndpointsFile));

    /// <summary>جِسمُ نُقطَةٍ بِعَينِها — مِن <c>MapPost("route"</c> إلى
    /// <c>).DisableAntiforgery()</c>. قَصٌّ نَصِّيٌّ كَما يَفعَل عَدّادُ
    /// النَزيف، فَلا حاجَةَ إلى تَشغيلِ الخادِمِ لِيُقاسَ
    /// التَرتيب.</summary>
    private static string EndpointBody(string route)
    {
        var source = EndpointSource();
        var start = source.IndexOf($"MapPost(\"{route}\"", StringComparison.Ordinal);
        Assert.True(start > 0, $"أَداة عَمياء: لا نُقطَةَ «{route}» في المَصدَر.");

        var end = source.IndexOf(").DisableAntiforgery()", start, StringComparison.Ordinal);
        Assert.True(end > start, $"أَداة عَمياء: لَم يُغلَق جِسمُ «{route}».");
        return source[start..end];
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }
}
