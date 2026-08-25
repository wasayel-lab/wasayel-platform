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
        int days = 30, string description = "اشتِراكُ شَهر",
        string cycle = "2026-09-03")
        => new(slug, "manual", amount, currency, days, description, cycle);

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

        // **و‏503 لا ‏200 — وهذا نَقضٌ لِما كُتِبَ هُنا أَوَّلاً،
        // يُقالُ بِاسمِه**: كانَ السَطرُ يَقول «‏200 لا خَطَأ… ورَدُّ
        // خَطَإٍ يَجعَل PayPal تُعيدُها ‏25 مَرَّةً في ثَلاثَةِ
        // أَيّام». والإعادَةُ هُنا **مَكسَبٌ لا كُلفَة**: هذا الفَرعُ
        // يَقول «المالُ وَصَلَ ويَنقُصُنا نَحنُ وَثيقَةً يُنشِئُها
        // المُشرِف»، فَإعادَةُ الإرسالِ هي النافِذَةُ الَّتي
        // تُطَبَّقُ فيها الرِسالَةُ بَعدَ الإصلاحِ **مِن تِلقاءِ
        // نَفسِها**. ورَدُّ ‏200 يُلغيها فَلا يَبقى إلّا لوغٌ يُقرَأُ
        // أَو لا يُقرَأ. التَفصيلُ في `docs/ADR-007`.
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
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
            PayPalOrderPolicy.Validate(new PayPalOrderDraft("ejar", "", 49m, "USD", 30, "", "2026-09-03"))
                .Select(v => v.Code));
    }

    /// <summary><b>والحُقولُ تُقرَأُ ولا تُخمَّن</b>: رَقَمٌ غَيرُ
    /// مَقروءٍ يَسقُط إلى صِفرٍ <b>فَيَرتَدُّ بِخَرقٍ يُسَمّيه</b>، ولا
    /// «شَهرٌ افتِراضيّ» يُخترَع. والعُملَةُ الغائِبَةُ وَحدَها تَرتَدُّ
    /// إلى الافتِراضِ المَقيس.</summary>
    [Fact]
    public void ReadDraft_FallsToZero_AndOnlyTheCurrencyHasADefault()
    {
        var d = PayPalOrderPolicy.ReadDraft("EJAR", "manual", "كَذا", null, "", null, null);

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

    // ═══ ١٢. الحالَةُ تُكتَبُ بِاتِّجاهٍ واحِد — وإلّا عادَ المالُ
    //     وبَقِيَت الأَيّام ═══════════════════════════════════════════
    //
    // **هذا القِسمُ كُلُّه مَكتوبٌ بِكُلفَةٍ مَقيسَةٍ بِتَشغيلِ
    // التَجميعَة**: كانَ `Apply` يَكتُب الحالَةَ بِلا فَحصِ ما
    // قَبلَها، فَحَدَثٌ مُتَأَخِّرٌ يَهبِط بِها مِن `captured` إلى
    // `pending`، ثُمَّ يَصِلُ الاستِردادُ فَيَجِدُ حارِسَ السَحبِ
    // يَشتَرِط `captured` — فَلا يُسحَبُ شَيء.

    /// <summary><b>السيناريو المَقيسُ حَرفاً</b>: طَلَبٌ وَصَلَ مالُه،
    /// ثُمَّ حَدَثُ «مُعَلَّق» مُتَأَخِّر، ثُمَّ استِردادٌ بِمَبلَغٍ
    /// مُطابِق. <b>يَجِبُ أَن يُسحَب</b> — وقَبلَ الإصلاحِ كانَ
    /// يُعَلَّمُ ولا يُسحَب: <b>المالُ يَعودُ والأَيّامُ تَبقى</b>.</summary>
    [Fact]
    public void ARefundStillWithdraws_AfterALateStatusEventOnACapturedOrder()
    {
        var plan  = Plan();
        var order = Order(days: 30);

        // ‏١) وَصَلَ المالُ فِعلاً — تُمَدَّدُ الباقَةُ وتَصيرُ الحالَةُ «قُبِض».
        var completed = Event(id: "WH-CAP-1");
        var extend = PayPalOrderBillingPolicy.Decide(completed, order, plan, false, Now);
        Assert.Equal(PayPalOrderAction.Extend, extend.Action);
        PayPalOrderBillingPolicy.Apply(order, completed, extend, Now);
        PayPalBillingPolicy.Apply(plan,
            new PayPalWebhookEvent(completed.EventId, completed.EventType, "ejar", null, null),
            new PayPalBillingDecision(PayPalBillingAction.Extend, extend.NewExpiresAt, extend.ReasonAr), Now);

        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);
        var granted = plan.ExpiresAt;

        // ‏٢) حَدَثٌ مُتَأَخِّرٌ يَقول «الالتِقاطُ مُعَلَّق» —
        //     **ولا يَهبِطُ بِالحالَة**.
        var late = Event(type: PayPalOrderEventTypes.CapturePending, id: "WH-PEND-2", status: "PENDING");
        var marked = PayPalOrderBillingPolicy.Decide(late, order, plan, false, Now);
        Assert.Equal(PayPalOrderAction.MarkOrder, marked.Action);
        PayPalOrderBillingPolicy.Apply(order, late, marked, Now);

        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);

        // ‏٣) اُستُرِدَّ المال ⇒ **يُسحَب** ما مُنِح، لا يُعَلَّمُ وَحسب.
        var refund = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.CaptureRefunded, id: "WH-REF-3",
                  reference: null, upCapture: "3C6"),
            order, plan, false, Now);

        Assert.Equal(PayPalOrderAction.Withdraw, refund.Action);
        Assert.True(refund.TouchesPlan);
        Assert.Equal(granted.AddDays(-30), refund.NewExpiresAt);
    }

    /// <summary><b>ومُوافَقَةٌ مُكَرَّرَةٌ لا تَنقُضُ وُصولَ المال</b>:
    /// ‏PayPal تُعيد إرسالَ الحَدَثِ نَفسِه ‏25 مَرَّةً، و
    /// <c>CHECKOUT.ORDER.APPROVED</c> بَعدَ الالتِقاطِ كانَ يُنزِل
    /// الحالَةَ إلى «وافَقَ» — فَيَنكَسِرُ حارِسُ السَحبِ نَفسُه.</summary>
    [Fact]
    public void ADuplicateApproval_NeverUnsaysThatTheMoneyArrived()
    {
        var order = Order(status: PayPalOrderStatuses.Captured);
        var approved = Event(type: PayPalOrderEventTypes.OrderApproved, id: "WH-APP-9", status: "APPROVED");

        PayPalOrderBillingPolicy.Apply(
            order, approved,
            new PayPalOrderDecision(PayPalOrderAction.MarkOrder, default,
                                    PayPalOrderStatuses.Approved, "—"), Now);

        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);
    }

    /// <summary><b>ومالٌ عادَ لا يُمنَحُ ثانِيَةً</b>: «مُكتَمِل» يَصِلُ
    /// بَعدَ الاستِردادِ واقِعَةٌ مُمكِنَة — كُلُّ رَدٍّ غَيرِ ‏2xx
    /// يَجعَل PayPal تُعيد الإرسالَ ثَلاثَةَ أَيّام.</summary>
    [Fact]
    public void MoneyThatCameBack_IsNeverGrantedASecondTime()
    {
        var plan = Plan();
        var before = plan.ExpiresAt;

        var d = PayPalOrderBillingPolicy.Decide(
            Event(), Order(status: PayPalOrderStatuses.Reversed), plan, false, Now);

        Assert.Equal(PayPalOrderAction.MarkOrder, d.Action);
        Assert.False(d.TouchesPlan);
        Assert.Equal(before, plan.ExpiresAt);
    }

    /// <summary><b>جَدوَلُ الانتِقالاتِ كامِلاً — والنُزولُ مَمنوع.</b>
    /// وكُلُّ خانَةٍ هُنا كانَت مَسموحَةً قَبلَ الإصلاح.</summary>
    [Theory]
    [InlineData(PayPalOrderStatuses.Captured, PayPalOrderStatuses.Pending)]
    [InlineData(PayPalOrderStatuses.Captured, PayPalOrderStatuses.Approved)]
    [InlineData(PayPalOrderStatuses.Captured, PayPalOrderStatuses.Denied)]
    [InlineData(PayPalOrderStatuses.Captured, PayPalOrderStatuses.Created)]
    [InlineData(PayPalOrderStatuses.Reversed, PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Reversed, PayPalOrderStatuses.Approved)]
    [InlineData(PayPalOrderStatuses.Reversed, PayPalOrderStatuses.Pending)]
    [InlineData(PayPalOrderStatuses.Pending,  PayPalOrderStatuses.Approved)]
    [InlineData(PayPalOrderStatuses.Denied,   PayPalOrderStatuses.Pending)]
    [InlineData(PayPalOrderStatuses.Approved, PayPalOrderStatuses.Created)]
    public void AStatusNeverGoesBackwards(string from, string to)
        => Assert.False(PayPalOrderStatuses.CanTransition(from, to));

    /// <summary>وما يَتَقَدَّمُ يَمُرّ — <b>وأَهَمُّها «وَصَلَ المال»</b>:
    /// لَو مُنِعَت <c>captured</c> مِن حالَةٍ ما لَانكَسَرَ حارِسُ
    /// السَحبِ مِن الجِهَةِ الأُخرى.</summary>
    [Theory]
    [InlineData(PayPalOrderStatuses.Created,  PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Approved, PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Pending,  PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Denied,   PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Captured, PayPalOrderStatuses.Reversed)]
    [InlineData(PayPalOrderStatuses.Created,  PayPalOrderStatuses.Approved)]
    public void MoneyArriving_AlwaysWritesItself(string from, string to)
        => Assert.True(PayPalOrderStatuses.CanTransition(from, to));

    /// <summary>وحالَةٌ مَجهولَةٌ (وَثيقَةٌ أَقدَمُ مِن هذا الجَدوَل)
    /// تُعامَلُ كَـ<c>created</c> — <b>فَلا يُغلِقُ حارِسٌ جَديدٌ باباً
    /// بِسَبَبِ بَياناتٍ سابِقَةٍ عَلَيه</b>.</summary>
    [Fact]
    public void AnUnknownStoredStatus_IsTreatedAsTheBeginning_NotAsALockedDoor()
    {
        Assert.True(PayPalOrderStatuses.CanTransition("", PayPalOrderStatuses.Captured));
        Assert.True(PayPalOrderStatuses.CanTransition(null, PayPalOrderStatuses.Captured));
        Assert.True(PayPalOrderStatuses.CanTransition("whatever", PayPalOrderStatuses.Captured));

        // وحالَةٌ خارِجَ المَعجَمِ لا تُكتَبُ أَبَداً.
        Assert.False(PayPalOrderStatuses.CanTransition(PayPalOrderStatuses.Created, "settled"));
        Assert.False(PayPalOrderStatuses.CanTransition(PayPalOrderStatuses.Created, ""));
    }

    // **و«قُبِض» كَلِمَةٌ يَملِكُها مَن يُمَدِّد وَحدَه** — وكانَ
    // هُنا `TheCaptureCall_NeverClaimsTheMoneyArrived` يَقرَأُ **نَصَّ
    // المَصدَر** بِعَيبَينِ مُتَعاكِسَين: يَشتَرِط وُجودَ رَمزٍ وغِيابَ
    // آخَر — فَـ`order.Status = "captured";` بِحَرفِيَّةٍ نَصِّيَّةٍ
    // تَمُرُّ خَضراء — **ويَقتَطِعُ إلى آخِرِ المِلَفِّ لا آخِرِ
    // الدالَّة**، فَذِكرُ الكَلِمَةِ في دالَّةٍ لاحِقَةٍ يُحمِرُّه بِلا
    // عَطَب.
    //
    // صارَ الشَرطُ على **الوَثيقَةِ المُخَزَّنَةِ بَعدَ تَشغيلِ
    // النُقطَة**:
    // `PayPalEndpointBehaviourTests.TheManualCaptureEndpoint_WritesApprovedNeverCaptured_AndLeavesATrail`.

    // ═══ ١٣. مَرجِعٌ لِكُلِّ دَورَة — وإلّا دُهِسَ سِجِلُّ الشَهرِ
    //     السابِق ═══════════════════════════════════════════════════════

    /// <summary><b>التَجديدُ الشَهرِيُّ العاديُّ لا يَدهَسُ وَثيقَةَ
    /// الشَهرِ الماضي.</b> نَفسُ المَبلَغِ ونَفسُ المُدَّةِ ونَفسُ
    /// الوَصف — <b>ومَرجِعانِ مُختَلِفان</b>، لِأَنّ الدَورَةَ
    /// تَحَرَّكَت. وقَبلَ الإصلاحِ كانا واحِداً، فَتُمحى
    /// <c>CaptureId</c> ولا يَجِدُ استِردادُ الشَهرِ السابِقِ ما
    /// يَربِطُه: <b>باقَةٌ قائِمَةٌ ومالٌ عاد</b>.</summary>
    [Fact]
    public void TheNextCyclesLink_GetsItsOwnReference_SoThePaidRecordSurvives()
    {
        var plan = Plan();
        var first = Draft(cycle: PayPalOrderPolicy.CycleOf(plan));

        // ‏وَصَلَ المالُ فَتَحَرَّكَ تاريخُ الانتِهاءِ ‏30 يَوماً.
        plan.ExpiresAt = plan.ExpiresAt.AddDays(30);
        var second = Draft(cycle: PayPalOrderPolicy.CycleOf(plan));

        Assert.NotEqual(PayPalOrderPolicy.Reference(first), PayPalOrderPolicy.Reference(second));
        Assert.NotEqual(PayPalOrderPolicy.OrderRequestId(first), PayPalOrderPolicy.OrderRequestId(second));
    }

    /// <summary>ونَقرَتانِ في <b>الدَورَةِ نَفسِها</b> تَبقَيانِ
    /// وَثيقَةً واحِدَةً — حَتمِيَّةُ المَرجِعِ لَم تُمَسّ.</summary>
    [Fact]
    public void TwoClicksInTheSameCycle_StillMakeOneDocument()
        => Assert.Equal(
            PayPalOrderPolicy.Reference(Draft(cycle: "2026-09-03")),
            PayPalOrderPolicy.Reference(Draft(cycle: "2026-09-03")));

    /// <summary><b>وشَبَكَةُ الأَمانِ تَحتَ ذلك</b>: وَثيقَةٌ تَجاوَزَت
    /// انتِظارَ الالتِقاطِ لا يُكتَبُ فَوقَها — <b>مَهما تَطابَقَت
    /// المُدخَلات</b>.</summary>
    [Theory]
    [InlineData(PayPalOrderStatuses.Captured)]
    [InlineData(PayPalOrderStatuses.Denied)]
    [InlineData(PayPalOrderStatuses.Reversed)]
    public void APaidOrdersRecord_IsNeverOverwritten(string status)
        => Assert.False(PayPalOrderPolicy.IsOverwritable(Order(status: status)));

    /// <summary>وما لَم يَقَع فيه شَيءٌ لا يُستَرجَع يُكتَبُ فَوقَه —
    /// فَلا يُرفَضُ على المالِكِ رابِطٌ لَم يُدفَع بَعد.</summary>
    [Fact]
    public void ALinkNobodyPaidYet_IsStillRewritable()
    {
        Assert.True(PayPalOrderPolicy.IsOverwritable(null));
        Assert.True(PayPalOrderPolicy.IsOverwritable(Order()));
        Assert.True(PayPalOrderPolicy.IsOverwritable(Order(status: PayPalOrderStatuses.Approved)));

        // ‏لكِنّ مُعَرِّفَ التِقاطٍ مَحفوظاً يَقفِلُها ولَو كانَت الحالَةُ تَنتَظِر:
        // **هُوَ المِفتاحُ الوَحيدُ الَّذي يَربِطُ الاستِردادَ**.
        var captured = Order(status: PayPalOrderStatuses.Approved);
        captured.CaptureId = "3C6";
        Assert.False(PayPalOrderPolicy.IsOverwritable(captured));
    }

    // ═══ ١٤. المَبلَغُ تَعريفٌ واحِدٌ لا اثنان ═════════════════════════

    /// <summary><b>‏5000.5 يَن</b>: تُرسَل <c>"5001"</c> لِأَنّ الينَّ
    /// بِلا كُسور، وتَعودُ <c>5001</c>. وقَبلَ الإصلاحِ كانَ المَخزونُ
    /// <c>5000.5</c> فَيَرتَدُّ التَأكيدُ بِـ<c>AmountMismatch</c> —
    /// <b>قُبِضَ المالُ ولَم تُمَدَّد الباقَة، صامِتاً</b>.</summary>
    [Fact]
    public void AFractionalYen_IsStoredAsItIsCharged_SoTheCaptureExtends()
    {
        var draft = Draft(amount: 5000.5m, currency: "JPY");
        Assert.Equal("5001", draft.MoneyValue);

        var order = PayPalOrderSurface.RecordFor(
            draft, PayPalOrderPolicy.Reference(draft),
            new PayPalOrderResult("5O1", "CREATED", "https://www.paypal.com/checkoutnow?token=5O1", null),
            "platform-admin", Now);

        Assert.Equal(5001m, order.Amount);

        var d = PayPalOrderBillingPolicy.Decide(
            Event(amount: "5001", currency: "JPY"), order, Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.Extend, d.Action);
    }

    /// <summary>و<b>‏49.005 دولاراً</b> كَذلك — الكَسرُ الثالِثُ
    /// يَختَفي عِندَ PayPal ويَبقى عِندَنا.</summary>
    [Fact]
    public void AThirdDecimalDollar_StillMatchesAtCapture()
    {
        var draft = Draft(amount: 49.005m);
        var order = PayPalOrderSurface.RecordFor(
            draft, PayPalOrderPolicy.Reference(draft),
            new PayPalOrderResult("5O1", "CREATED", "https://www.paypal.com/checkoutnow?token=5O1", null),
            "platform-admin", Now);

        Assert.Equal(draft.MoneyValue, PayPalCurrencies.Money(order.Amount, order.Currency));
        Assert.True(PayPalOrderBillingPolicy.MoneyMatches(
            Event(amount: draft.MoneyValue), order));
    }

    /// <summary><b>وفاصِلُ الآلافِ لا يُقرَأُ مِئَةَ ضِعف</b>:
    /// <c>"49,99"</c> — كِتابَةُ نِصفِ أوروبّا لِـ‏49.99 — كانَت
    /// تُقرَأُ <b>‏4999</b> بِـ<c>NumberStyles.Number</c>، في
    /// المَوضِعَين.</summary>
    [Fact]
    public void AThousandsSeparator_IsNeverReadAsAHundredfold()
    {
        var order = PayPalOrderPolicy.ReadDraft("ejar", "manual", "49,99", "USD", "30", "", "2026-09-03");
        Assert.NotEqual(4999m, order.Amount);
        Assert.Equal(0m, order.Amount);
        Assert.Contains(PayPalOrderPolicy.AmountNotPositive,
            PayPalOrderPolicy.Validate(order).Select(v => v.Code));

        var plan = PayPalCatalogPolicy.ReadDraft("manual", "باقَة", "49,99", "USD", "MONTH");
        Assert.NotEqual(4999m, plan.Amount);
        Assert.Contains(PayPalCatalogPolicy.AmountNotPositive,
            PayPalCatalogPolicy.Validate(plan).Select(v => v.Code));

        // وكَذلك المُقارَنَةُ بِما يَصِل: نَمَطُ PayPal لا يَحمِل فاصِلَ
        // آلافٍ أَصلاً، فَنَصٌّ يَحمِلُه **عَدَمُ تَطابُقٍ لا تَساهُل**.
        Assert.False(PayPalOrderBillingPolicy.MoneyMatches(
            Event(amount: "4,900"), Order(amount: 4900m)));
    }

    // ═══ ١٥. صَغائِرُ مَقيسَةٌ — ولِكُلٍّ أَثَرٌ يُسَمّى ═══════════════

    /// <summary><b>مُعَرِّفُ الاستِردادِ لا يُكتَبُ في حَقلِ
    /// الالتِقاط</b>: في <c>REFUNDED</c>/<c>REVERSED</c> يَكون
    /// <c>resource.id</c> مُعَرِّفَ <b>الاسترداد</b>، والمِفتاحُ
    /// الصَحيحُ في <c>links[rel=up]</c>. وكِتابَةُ الأَوَّلِ تَضَعُ في
    /// الحَقلِ مِفتاحاً لا يُطابِقُ شَيئاً — <b>وهُوَ المِفتاحُ
    /// الوَحيدُ الَّذي يَربِطُ دَورَةَ الحَياةِ كُلَّها</b>.</summary>
    [Fact]
    public void ARefundEvent_NeverWritesTheRefundIdIntoTheCaptureField()
    {
        var order = Order(status: PayPalOrderStatuses.Captured);
        order.CaptureId = null;

        // ‏`Event` تَضَعُ `CaptureId = "3C6"` (وهُوَ `resource.id`)،
        // و`upCapture` هُوَ ما جاءَ مِن `links[rel=up]`.
        var refund = Event(type: PayPalOrderEventTypes.CaptureRefunded,
                           id: "WH-REF-1", reference: null, upCapture: "REAL-CAPTURE-7");

        PayPalOrderBillingPolicy.Apply(
            order, refund,
            new PayPalOrderDecision(PayPalOrderAction.MarkOrder, default,
                                    PayPalOrderStatuses.Reversed, "—"), Now);

        Assert.Equal("REAL-CAPTURE-7", order.CaptureId);
    }

    /// <summary><b>وزِرُّ «التَقِط الآن» يَختَفي بَعدَ نَجاحِه</b> —
    /// والفَيصَلُ مُعَرِّفُ الالتِقاطِ لا الحالَةُ وَحدَها: النِداءُ
    /// الناجِحُ يُثَبِّتُه وتَبقى الحالَةُ «وافَقَ» حَتّى تَصِلَ
    /// رِسالَةُ <c>COMPLETED</c>. <b>ونَقرَةٌ ثانِيَةٌ كانَت تَرتَدُّ
    /// مِن PayPal بِـ<c>ORDER_ALREADY_CAPTURED</c> إنجِليزِيّاً
    /// خامّاً</b>.</summary>
    [Fact]
    public void ACapturedOrder_NoLongerOffersTheCaptureButton()
    {
        Assert.True(PayPalOrderPolicy.CanCaptureNow(Order()));
        Assert.True(PayPalOrderPolicy.CanCaptureNow(Order(status: PayPalOrderStatuses.Approved)));

        var afterCapture = Order(status: PayPalOrderStatuses.Approved);
        afterCapture.CaptureId = "3C6";
        Assert.False(PayPalOrderPolicy.CanCaptureNow(afterCapture));

        Assert.False(PayPalOrderPolicy.CanCaptureNow(Order(status: PayPalOrderStatuses.Captured)));
        Assert.False(PayPalOrderPolicy.CanCaptureNow(Order(status: PayPalOrderStatuses.Reversed)));
        Assert.False(PayPalOrderPolicy.CanCaptureNow(null));

        // ‏وطَلَبٌ بِلا مُعَرِّفٍ عِندَ PayPal لا يُلتَقَط أَصلاً.
        var noOrderId = Order();
        noOrderId.OrderId = "";
        Assert.False(PayPalOrderPolicy.CanCaptureNow(noOrderId));
    }

    /// <summary>
    /// <para><b>والشاشَةُ تَقرَأُ القاعِدَةَ نَفسَها لا نُسخَةً
    /// مِنها</b> — فَلا يُرسَم زِرٌّ تَرُدُّه النُقطَة.</para>
    ///
    /// <para><b>ونِصفُ هذا الشَرطِ صارَ سُلوكِيّاً، ونِصفُه دَينٌ
    /// مُعلَن</b>: طَرَفُ النُقطَةِ يُقاسُ بِتَشغيلِها في
    /// <c>PayPalEndpointBehaviourTests.TheManualCaptureEndpoint_RefusesAnOrderAlreadyCaptured_WithoutCallingPayPal</c>
    /// (رَدٌّ وعَدّادُ طَلَباتٍ خارِجَة). وطَرَفُ الشاشَةِ يَبقى
    /// <b>نَصِّيّاً</b> لِأَنّ تَصييرَ مُكَوِّنِ Razor يَحتاج مُهَيِّئَ
    /// تَصييرٍ لا وُجودَ لَه في المُستَودَع — <b>ويُقالُ صَراحَةً بَدَلَ
    /// أَن يُدَّعى أَنَّه مَقيس</b> (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void TheCaptureButtonOnTheScreen_ReadsTheSharedRule()
    {
        var screen = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, PlanScreenFile));
        Assert.Contains(nameof(PayPalOrderPolicy.CanCaptureNow), screen);
    }

    /// <summary><b>ونِداءُ الالتِقاطِ واحِدٌ لِلمَسارَين</b>
    /// (القاعِدَة ٨): كانَ مَنسوخاً — نُسخَةُ الحَدَثِ تَأخُذُ القُفلَ
    /// وتَكتُب، ونُسخَةُ الزِرِّ <b>بِلا قُفلٍ ولا حالَة</b>. أَي أَنّ
    /// طَريقَ العِلاجِ كانَ أَضعَفَ مِن الطَريقِ المُعتاد.</summary>
    [Fact]
    public void TheManualCapture_GoesThroughTheSamePipeAsTheEvent()
    {
        var body = EndpointBody("/admin/tenants/{slug}/plan/paypal-capture");

        Assert.Contains("PayPalOrderFlow.CaptureAsync", body);
        Assert.DoesNotContain("paypal.CaptureOrderAsync", body);
    }

    // **وسَطرُ التَدقيقِ لِنِداءِ التِقاطٍ بِيَدِ مُشرِف** — كانَ
    // `TheManualCapture_LeavesATrail` يَفحَص وُجودَ `audit.WriteAsync`
    // في النَصّ، وهو **حُضورٌ لا أَثَر**: سَطرٌ داخِلَ فَرعٍ لا يُنَفَّذ
    // يَمُرّ. صارَ الشَرطُ على `AuditEntry` **مُخَزَّنَةٍ فِعلاً**
    // بِنِطاقِها وفِعلِها، في
    // `PayPalEndpointBehaviourTests.TheManualCaptureEndpoint_WritesApprovedNeverCaptured_AndLeavesATrail`.
    //
    // **وحارِسُ الدَهسِ كَذلك**: كانَ
    // `TheOverwriteGuard_IsWiredIntoTheEndpoint_BeforeAnyCallToPayPal`
    // يَفحَص `Contains("PayPalOrderPolicy.IsOverwritable")` وتَرتيبَ
    // فِهرِسَين — **ونَزعُ `!` وَحدَه يَقلِبُ الحارِسَ وتَبقى خَضراء**.
    // قيسَ ذلك بِحَقنِ العَيبِ بِعَينِه: النَصِّيُّ **خَضراء**
    // والسُلوكِيُّ **حَمراء** بِرِسالَةٍ تُسَمّي ما وَقَع
    // (‏`Collection: ["/v1/oauth2/token"]` — أَي أَنّ PayPal نودِيَت).
    // فَصارَ الشَرطُ على الرَدِّ وعَدّادِ الطَلَباتِ الخارِجَةِ في
    // `PayPalEndpointBehaviourTests.ThePaymentLinkEndpoint_OverASettledOrder_AnswersWithoutCallingPayPal`،
    // ومَعَه طَرَفُه المُقابِلُ (‏`…_OverAFreshCycle_DoesCallPayPalAndStoresTheLink`)
    // فَلا يُخلَط «لَم تُنادَ» بِـ«لا تُنادى أَبَداً».

    /// <summary><b>وأَقفالُ الطَلَباتِ لا تَنمو</b>: كانَ قامُوساً
    /// <b>لا يُفَرَّغُ أَبَداً</b> — سيمافورٌ لِكُلِّ مَرجِعٍ يَبقى ما
    /// بَقِيَت العَمَلِيَّة، والمَراجِعُ حَتمِيَّةٌ لا مُعادَة.</summary>
    [Fact]
    public void TheOrderLocks_NeverGrowWithTheNumberOfOrders()
    {
        var seen = new HashSet<SemaphoreSlim>();
        for (var i = 0; i < 5_000; i++)
            seen.Add(PayPalOrderLocks.For($"wsl-ejar-{i:x8}"));

        Assert.True(seen.Count <= PayPalOrderLocks.Stripes,
            $"عَدَدُ الأَقفال {seen.Count} فَوقَ السَقفِ {PayPalOrderLocks.Stripes}.");

        // ونَفسُ المَرجِعِ يُعطي نَفسَ القُفل — وإلّا لَم يَتَسَلسَل شَيء.
        Assert.Same(PayPalOrderLocks.For("wsl-ejar-abc123"), PayPalOrderLocks.For("wsl-ejar-abc123"));
        Assert.Same(PayPalOrderLocks.For(" wsl-ejar-abc123 "), PayPalOrderLocks.For("wsl-ejar-abc123"));
    }

    /// <summary><b>مالٌ وَصَلَ ويَنقُصُنا نَحنُ وَثيقَة ⇒ رَمزٌ
    /// تُعيدُ PayPal الرِسالَةَ عَلَيه.</b> ورَدُّ ‏200 كانَ يُلغي
    /// إعادَةَ الإرسالِ الآليَّةَ (‏25 مُحاوَلَةً في ثَلاثَةِ أَيّام) —
    /// وهي بِالضَبطِ النافِذَةُ الَّتي يُصلِحُ فيها المُشرِفُ النَقصَ
    /// فَتُطَبَّقُ الرِسالَةُ مِن تِلقاءِ نَفسِها.</summary>
    [Theory]
    [InlineData(PayPalOrderAction.UnknownReference)]
    [InlineData(PayPalOrderAction.UnknownTenant)]
    public void MoneyWeCannotApplyYet_IsAnsweredWithACodeThatMakesPayPalTryAgain(PayPalOrderAction action)
    {
        Assert.True(PayPalOrderSurface.HealsOnRedelivery(action));

        var result = PayPalOrderSurface.NoWrite(
            new CapturingLogger(), Event(),
            new PayPalOrderDecision(action, default, "", "—"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary><b>وما لا تَشفيهِ الإعادَةُ يَبقى ‏200</b>: المَبلَغانِ
    /// لا يَتَحَرَّكُ أَحَدُهُما، وحالَةُ المَورِدِ لَقطَةٌ داخِلَ
    /// الجِسمِ المُعاد. فَـ‏25 إعادَةً ضَجيجٌ لا يَشتَري
    /// شَيئاً.</summary>
    [Theory]
    [InlineData(PayPalOrderAction.AmountMismatch)]
    [InlineData(PayPalOrderAction.StatusNotCompleted)]
    [InlineData(PayPalOrderAction.Replay)]
    [InlineData(PayPalOrderAction.Ignored)]
    public void AFailureRedeliveryCannotHeal_IsNotRepeatedForThreeDays(PayPalOrderAction action)
    {
        Assert.False(PayPalOrderSurface.HealsOnRedelivery(action));

        var result = PayPalOrderSurface.NoWrite(
            new CapturingLogger(), Event(),
            new PayPalOrderDecision(action, default, "", "—"));

        Assert.Equal(StatusCodes.Status200OK,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>
    /// <para><b>ورائِدُ الأَعمالِ يَبلُغُ رابِطَ التَجديدِ المُبَكِّرِ
    /// بِالنَقر</b> (القاعِدَة ١٢): مِرساةُ <c>max(الآن, ExpiresAt)</c>
    /// كُتِبَت لِلتَجديدِ المُبَكِّرِ بِعَينِه، والشَرطُ
    /// <c>Grace or Suspended</c> كانَ يَحجُبُ اللافِتَةَ عَن مَتجَرٍ
    /// سارٍ — <b>أَي عَن الحالَةِ الوَحيدَةِ الَّتي كُتِبَت لَها
    /// المِرساة</b>.</para>
    ///
    /// <para><b>وعَيبانِ أُصلِحا هُنا، كِلاهُما في اتِّجاهِ الحُمرَةِ
    /// الكاذِبَة</b>:</para>
    /// <list type="number">
    ///   <item>كانَ يُثَبِّتُ العِبارَةَ <c>"PendingOrder(slug) is not
    ///   null"</c> <b>حَرفاً</b>، فَإعادَةُ صِياغَةٍ صِفريَّةِ الأَثَرِ
    ///   (‏<c>is { }</c>، أَو تَبديلُ طَرَفَي <c>||</c>) تَكسِرُه بِلا
    ///   عَطَب. صارَ الشَرطُ على <b>الرَمز</b> لا على العِبارَة.</item>
    ///
    ///   <item>وكانَ يَقرَأُ <b>جِسمَ الدالَّةِ ولا يَقرَأُ مَن
    ///   يُنادِيها</b> — فَلا يَفحَص «يُبلَغُ بِالنَقر» وهُوَ ما سُمِّيَ
    ///   بِه: دالَّةٌ صَحيحَةٌ لا يَستَعمِلُها التَصييرُ لافِتَةٌ لا
    ///   تُرسَم. صارَ يَقرَأُ <b>حَلقَةَ التَصييرِ نَفسَها</b> وفَرعَ
    ///   الرابِطِ داخِلَها.</item>
    /// </list>
    ///
    /// <para><b>وما يَبقى دَيناً مُعلَناً</b>: هذا فَحصُ <b>وَصلٍ</b> لا
    /// فَحصُ <b>تَصيير</b>. الجَزمُ بِأَنّ الوَسمَ يَظهَر يَحتاج
    /// تَصييرَ مُكَوِّنِ Razor، ولا مُهَيِّئَ لَه في المُستَودَع
    /// اليَوم.</para>
    /// </summary>
    [Fact]
    public void TheEarlyRenewalLink_IsWiredFromTheBannerRuleToTheRenderedList()
    {
        var studio = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, StudioHomeFile));

        // ‏١) القاعِدَةُ تَشمَلُ الدَفعَ المُعَلَّق — بِالرَمزِ لا بِالعِبارَة.
        var at = studio.IndexOf("bool Renewable(string slug)", StringComparison.Ordinal);
        Assert.True(at > 0, "أَداة عَمياء: لا شَرطَ لافِتَةٍ في المَصدَر.");
        var rule = studio[at..studio.IndexOf(';', at)];
        Assert.Contains("PendingOrder", rule);

        // ‏٢) والتَصييرُ يَستَعمِلُها فِعلاً — وهذا ما لَم يَكُن يُفحَص.
        Assert.Matches(@"Where\(\s*\w+\s*=>\s*Renewable\(", studio);

        // ‏٣) والرابِطُ داخِلَ فَرعِ الدَفعِ المُعَلَّقِ لا خارِجَه.
        var branch = studio.IndexOf("PendingOrder(t.Slug)", StringComparison.Ordinal);
        Assert.True(branch > 0, "أَداة عَمياء: لا فَرعَ دَفعٍ مُعَلَّقٍ في التَصيير.");
        var next = studio.IndexOf("else if", branch, StringComparison.Ordinal);
        Assert.Contains("ApproveUrl", studio[branch..(next > branch ? next : studio.Length)]);
    }

    // ═══ ١٦. بابُ السَحبِ لا يُغلَق — والثابِتُ في مَوضِعٍ واحِد ══════
    //
    // **الثابِتُ المَحروسُ هُنا**: `order.Status == captured` لَحظَةَ
    // وُصولِ الاستِرداد. عَلَيه وَحدَه يَقوم حارِسُ السَحب، وكُلُّ فَرعٍ
    // يُخرِج الطَلَبَ مِن `captured` بِلا سَحبِ يَومٍ **يُغلِقُ بابَ
    // السَحبِ إلى الأَبَد**: المالُ يَعودُ والأَيّامُ تَبقى.

    /// <summary>
    /// <para><b>‏<c>CHECKOUT.PAYMENT-APPROVAL.REVERSED</c> على طَلَبٍ
    /// مَقبوضٍ لا يَكتُبُ حَرفاً — والاستِردادُ بَعدَه يَسحَب.</b></para>
    ///
    /// <para><b>والتَسَلسُلُ الَّذي كَتَبَ هذا الاختِبار — مَقيسٌ
    /// بِتَشغيلِ التَجميعَة</b>: الباقَةُ <c>09-03 → 10-03</c> بِوُصولِ
    /// المال، ثُمَّ يَصِلُ حَدَثُ نَقضِ المُوافَقَةِ فَيَكتُب
    /// <c>reversed</c> <b>بِلا مَساسٍ بِالباقَة</b> (فَرعٌ بِلا أَيِّ
    /// شَرطٍ على حالَةِ الطَلَب)، ثُمَّ يَصِلُ
    /// <c>PAYMENT.CAPTURE.REFUNDED</c> بِمَبلَغٍ مُطابِقٍ فَيَجِدُ
    /// حارِسَ السَحبِ يَشتَرِط <c>captured</c> — <b>فَلا يُسحَبُ شَيء
    /// وتَبقى <c>10-03</c></b>.</para>
    ///
    /// <para><b>وجَدوَلُ الانتِقالاتِ عاجِزٌ عَن إغلاقِه</b>:
    /// <c>captured → reversed</c> مَسموحٌ <b>عَمداً</b> لِأَنَّه انتِقالُ
    /// السَحبِ نَفسُه. فَالحُكمُ لَيسَ «إلى أَيِّ حالَة» بَل <b>«بِأَيِّ
    /// فِعل»</b>.</para>
    /// </summary>
    [Fact]
    public void ApprovalReversed_OnACapturedOrder_LeavesTheRefundAbleToWithdraw()
    {
        var plan  = Plan();
        var order = Order(days: 30);

        // ‏١) وَصَلَ المال: ‏30 يَوماً تُضاف، والطَلَبُ يَصيرُ «قُبِض».
        var arrival = PayPalOrderBillingPolicy.Decide(Event(), order, plan, false, Now);
        Assert.Equal(PayPalOrderAction.Extend, arrival.Action);
        plan.ExpiresAt = arrival.NewExpiresAt;
        PayPalOrderBillingPolicy.Apply(order, Event(), arrival, Now);
        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);
        var granted = plan.ExpiresAt;

        // ‏٢) نَقضُ المُوافَقَةِ بِمُعَرِّفِ حَدَثٍ آخَر ⇒ **صِفرُ كِتابَة**.
        var stray = Event(type: PayPalOrderEventTypes.ApprovalReversed, id: "WH-APR-1");
        var strayDecision = PayPalOrderBillingPolicy.Decide(stray, order, plan, false, Now);
        Assert.False(strayDecision.Writes);
        PayPalOrderBillingPolicy.Apply(order, stray, strayDecision, Now);
        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);

        // ‏٣) الاستِردادُ يَجِدُ البابَ مَفتوحاً ⇒ تُسحَبُ الأَيّامُ نَفسُها.
        var refund = Event(type: PayPalOrderEventTypes.CaptureRefunded,
                           id: "WH-REF-1", upCapture: "3C6");
        var withdraw = PayPalOrderBillingPolicy.Decide(refund, order, plan, false, Now);
        Assert.Equal(PayPalOrderAction.Withdraw, withdraw.Action);
        Assert.Equal(granted.AddDays(-30), withdraw.NewExpiresAt);
    }

    /// <summary>ونَقضُ المُوافَقَةِ على طَلَبٍ <b>لَم يُقبَض بَعد</b>
    /// يَبقى كَما كان — وهذِه هي حالَتُه الحَقيقِيَّةُ الوَحيدَة:
    /// «انقَضَت النافِذَةُ قَبلَ الالتِقاط».</summary>
    [Theory]
    [InlineData(PayPalOrderStatuses.Created)]
    [InlineData(PayPalOrderStatuses.Approved)]
    [InlineData(PayPalOrderStatuses.Pending)]
    public void ApprovalReversed_OnAnOrderStillAwaitingCapture_StillMarksIt(string status)
    {
        var d = PayPalOrderBillingPolicy.Decide(
            Event(type: PayPalOrderEventTypes.ApprovalReversed, id: "WH-APR-1"),
            Order(status: status), Plan(), false, Now);

        Assert.Equal(PayPalOrderAction.MarkOrder, d.Action);
        Assert.Equal(PayPalOrderStatuses.Reversed, d.OrderStatus);
        Assert.False(d.TouchesPlan);
    }

    /// <summary>
    /// <para><b>وغِيابُ وَثيقَةِ الباقَةِ لَحظَةَ الاستِرداد نَقصٌ
    /// عِندَنا لا حُكمٌ على الطَلَب</b>: كانَ <c>|| plan is null</c>
    /// مَطوِيّاً في حارِسِ السَحب، فَيَكتُب <c>reversed</c> على طَلَبٍ
    /// <b>مَقبوض</b> — و<b>يُقفِلُ نافِذَةَ الإعادَةِ الَّتي كانَت
    /// سَتَسحَب</b>: ‏200 لِPayPal، ووَثيقَةٌ لا تَعودُ
    /// <c>captured</c> أَبَداً.</para>
    ///
    /// <para>والصَوابُ <c>UnknownTenant</c> — <b>صِفرُ كِتابَةٍ
    /// و‏503</b> تَشفيها الإعادَة، كَما يَفعَل فَرعُ التَمديدِ
    /// حَرفاً.</para>
    /// </summary>
    [Fact]
    public void ARefundWithNoPlanDocument_WritesNothing_AndHealsOnRedelivery()
    {
        var order  = Order(status: PayPalOrderStatuses.Captured);
        var refund = Event(type: PayPalOrderEventTypes.CaptureRefunded,
                           id: "WH-REF-1", upCapture: "3C6");

        var d = PayPalOrderBillingPolicy.Decide(refund, order, null, false, Now);
        Assert.Equal(PayPalOrderAction.UnknownTenant, d.Action);
        Assert.False(d.Writes);
        Assert.True(PayPalOrderSurface.HealsOnRedelivery(d.Action));

        PayPalOrderBillingPolicy.Apply(order, refund, d, Now);
        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);

        // ويَضبُطُ المُشرِفُ الباقَةَ فَتُطَبَّقُ الإعادَةُ مِن تِلقاءِ نَفسِها.
        var again = PayPalOrderBillingPolicy.Decide(refund, order, Plan(), false, Now);
        Assert.Equal(PayPalOrderAction.Withdraw, again.Action);
    }

    /// <summary>
    /// <para><b>والثابِتُ مَفروضٌ في مَوضِعٍ واحِدٍ يَمُرُّ بِه
    /// الجَميع</b>: لا يَخرُجُ طَلَبٌ مِن <c>captured</c> إلّا
    /// بِفِعلِ <see cref="PayPalOrderAction.Withdraw"/> — ولَو أَخطَأَ
    /// فَرعٌ خامِسٌ يُكتَبُ غَداً، تَرُدُّه <c>Apply</c> بَدَلَ أَن
    /// تُنَفِّذَه.</para>
    ///
    /// <para><b>ولِماذا هُنا لا في جَدوَلِ الانتِقالات</b>: الجَدوَلُ
    /// يَعرِف الحالَتَينِ ولا يَعرِف الفِعل، و<c>captured → reversed</c>
    /// انتِقالٌ <b>مَشروعٌ</b> بِفِعلٍ واحِدٍ ومَمنوعٌ بِما
    /// عَداه.</para>
    /// </summary>
    [Fact]
    public void OnlyAWithdrawal_TakesAnOrderOutOfCaptured()
    {
        var marked = Order(status: PayPalOrderStatuses.Captured);
        PayPalOrderBillingPolicy.Apply(
            marked, Event(),
            new PayPalOrderDecision(PayPalOrderAction.MarkOrder, default,
                                    PayPalOrderStatuses.Reversed, "—"), Now);
        Assert.Equal(PayPalOrderStatuses.Captured, marked.Status);

        var withdrawn = Order(status: PayPalOrderStatuses.Captured);
        PayPalOrderBillingPolicy.Apply(
            withdrawn, Event(),
            new PayPalOrderDecision(PayPalOrderAction.Withdraw, default,
                                    PayPalOrderStatuses.Reversed, "—"), Now);
        Assert.Equal(PayPalOrderStatuses.Reversed, withdrawn.Status);

        // وما دونَ «قُبِض» يُعَلَّمُ كَما كان — الحارِسُ يَحرُسُ
        // الخُروجَ مِن «قُبِض» وَحدَه، لا كُلَّ تَعليمٍ بِـ`reversed`.
        var approved = Order(status: PayPalOrderStatuses.Approved);
        PayPalOrderBillingPolicy.Apply(
            approved, Event(),
            new PayPalOrderDecision(PayPalOrderAction.MarkOrder, default,
                                    PayPalOrderStatuses.Reversed, "—"), Now);
        Assert.Equal(PayPalOrderStatuses.Reversed, approved.Status);
    }

    // ═══ ١٧. طَلَبٌ واحِدٌ = التِقاطٌ واحِد ═══════════════════════════

    /// <summary>
    /// <para><b>رِسالَةُ «مُكتَمِل» ثانِيَةٌ بِمُعَرِّفِ حَدَثٍ آخَر لا
    /// تَشتَري الأَيّامَ مَرَّتَين.</b> ‏<c>event_id</c> مُختَلِفٌ
    /// فَسِجِلُّ مَرَّة-واحِدَةٍ لا يُوقِفُها، و
    /// <c>CanTransition(captured, captured)</c> تُرجِع <c>true</c>
    /// (فَرعُ «نَفسِ الحالَة» يَسبِقُ الجَدوَل) — فَكانَ الطَريقُ
    /// مَفتوحاً: <b>مَقيسٌ ‏09-03 → 10-03 → 11-02</b> بِنَفسِ مُعَرِّفِ
    /// الالتِقاطِ ونَفسِ المَبلَغ.</para>
    ///
    /// <para><b>ولا مُطلِقَ مَعروفاً لَه اليَوم</b> — وهذا بِعَينِه
    /// السَبَب: الثابِتُ كانَ مَفروضاً بِطَبَقَةٍ واحِدَةٍ هي <b>وَصفُ
    /// PayPal لِسُلوكِها</b>، وطَبَقَةٌ واحِدَةٌ عِندَ طَرَفٍ ثالِثٍ
    /// لَيسَت حِراسَة.</para>
    /// </summary>
    [Fact]
    public void ASecondCaptureCompleted_WithAFreshEventId_NeverBuysTheSameDaysTwice()
    {
        var plan  = Plan();
        var order = Order(days: 30);

        var first = PayPalOrderBillingPolicy.Decide(Event(), order, plan, false, Now);
        Assert.Equal(PayPalOrderAction.Extend, first.Action);
        plan.ExpiresAt = first.NewExpiresAt;
        PayPalOrderBillingPolicy.Apply(order, Event(), first, Now);
        var granted = plan.ExpiresAt;

        var second = PayPalOrderBillingPolicy.Decide(
            Event(id: "WH-CAP-2"), order, plan, false, Now);

        Assert.Equal(PayPalOrderAction.Replay, second.Action);
        Assert.False(second.Writes);
        Assert.False(second.TouchesPlan);
        Assert.Equal(granted, plan.ExpiresAt);
    }

    /// <summary>
    /// <para><b>وأَوَّلُ «مُكتَمِل» بَعدَ نِداءِ التِقاطٍ ناجِحٍ ما
    /// يَزالُ يُمَدِّد</b> — وهذا هُوَ القِياسُ الَّذي رَفَضَ الصِياغَةَ
    /// الأولى لِلحارِس.</para>
    ///
    /// <para><b>القِياس</b>: «ارفُض التَمديدَ حينَ يَكونُ
    /// <c>order.CaptureId</c> مَملوءاً ويُطابِق الواصِل» يَحجُب
    /// <b>التَمديدَ الأَوَّلَ نَفسَه</b>:
    /// <c>PayPalOrderFlow.CaptureAsync</c> يُثَبِّت <c>CaptureId</c>
    /// ويَترُك الحالَةَ <c>approved</c>، ثُمَّ تَصِلُ رِسالَةُ
    /// <c>COMPLETED</c> بِـ<b>نَفسِ</b> المُعَرِّف. فَالفَيصَلُ هُوَ
    /// <c>Status == captured</c> — وهي كَلِمَةٌ لا يَكتُبُها إلّا مَن
    /// مَدَّدَ فِعلاً.</para>
    /// </summary>
    [Fact]
    public void TheFirstCompletedAfterASuccessfulCaptureCall_StillExtends()
    {
        var order = Order(status: PayPalOrderStatuses.Approved);
        order.CaptureId = "3C6";   // ما ثَبَّتَه النِداءُ الناجِح — ونَفسُه في الحَدَث

        var d = PayPalOrderBillingPolicy.Decide(Event(), order, Plan(), false, Now);
        Assert.Equal(PayPalOrderAction.Extend, d.Action);
    }

    // ─── أَدَوات ─────────────────────────────────────────────────────

    private const string EndpointsFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PayPalEndpoints.cs";

    private const string PlanScreenFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/TenantPlanAdmin.razor";

    private const string StudioHomeFile =
        "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioHome.razor";

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
