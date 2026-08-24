using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَرارُ فَوتَرَةِ PayPal — بِلا قاعِدَةِ بَياناتٍ وبِلا شَبَكَة ═════
//
// **ولِماذا كُلُّه دَوالُّ نَقِيَّة**: «صِفرُ كِتابَةٍ عِندَ تَوقيعٍ
// فاشِل» و«تَكرارُ الحَدَثِ لا يُمَدِّد» جُملَتانِ لا تُبرهَنانِ
// بِفَحصِ قاعِدَةِ بَياناتٍ بَعدَ الحَدَث — تُبرهَنانِ بِأَنّ القَرارَ
// نَفسَه **لا يُنتِج وَثيقَةً**. ولِذلك `Writes` خَصيصَةٌ عَلى
// القَرارِ لا نَتيجَةٌ تُلاحَظ.
//
// **والدَينُ المُعلَن**: المُعامَلَةُ الحَقيقِيَّةُ (Marten) والنِداءُ
// الحَقيقيُّ (PayPal) لا يُختَبَرانِ هُنا — لا حِسابَ PayPal في هذِه
// الجَولَة. يُسَدَّدُ يَومَ يَضَعُ المالِكُ أَسرارَه، والخُطُواتُ في
// `docs/DEPLOY.md` §٢·ج.

public class PayPalBillingPolicyTests
{
    private static readonly DateTime Now = new(2026, 08, 24, 12, 00, 00, DateTimeKind.Utc);

    private static readonly PayPalWebhookHeaders Full = new(
        "tx-1", "2026-08-24T10:00:00Z", "https://api.paypal.com/cert.pem", "SHA256withRSA", "sig==");

    private static PayPalOptions Ready(string webhookId = "WH-1") => new()
    {
        ClientId = "c", ClientSecret = "s",
        Environment = PayPalEnvironment.Live, WebhookId = webhookId
    };

    /// <summary>باقَةُ شَهرٍ سارِيَة: بَدَأَت قَبلَ ‏20 يَوماً وتَنتَهي
    /// بَعدَ ‏10. فَمُدَّتُها ‏30 يَوماً — <b>رَقَمٌ مِن بَياناتِ
    /// المَتجَرِ لا مِن كود</b>.</summary>
    private static TenantPlan MonthlyPlan(string slug = "ejar") => new()
    {
        Id = slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-20), ExpiresAt = Now.AddDays(10),
        GraceDays = 14, Price = 0m,
    };

    private static PayPalWebhookEvent Event(
        string type = PayPalEventTypes.SubscriptionActivated,
        string id = "WH-EVT-1", string? slug = "ejar",
        string? sub = "I-SUB1", DateTime? next = null)
        => new(id, type, slug, sub, next);

    // ═══ ١. البَوّابَة — يُتَحَقَّقُ قَبلَ أَن يُقرَأ ═══════════════════

    [Fact]
    public void Gate_Accepts_OnlyWhenConfigured_HeadersComplete_AndSignatureVerified()
        => Assert.Equal(PayPalWebhookGate.Accepted,
            PayPalBillingPolicy.Gate(Ready(), Full, signatureVerified: true));

    /// <summary><b>غِيابُ <c>WebhookId</c> يُقالُ بِاسمِه</b> ولا
    /// يُخلَط بِـ«تَوقيعٌ فاشِل» — وإلّا بَحَثَ المالِكُ عَن سِرٍّ
    /// خاطِئٍ ومُشكِلَتُه سِرٌّ غائِب. وهذا هُوَ **المَطلوب: غِيابُ
    /// WebhookId ⇒ النُقطَةُ تَرفُض**.</summary>
    [Fact]
    public void Gate_Rejects_WhenTheWebhookIdIsMissing_WithItsOwnCode()
    {
        var gate = PayPalBillingPolicy.Gate(Ready(webhookId: ""), Full, signatureVerified: true);

        Assert.Equal(PayPalWebhookGate.NotConfigured, gate);
        Assert.Equal("paypal_not_configured", PayPalBillingPolicy.GateCode(gate));
        Assert.NotEqual(PayPalBillingPolicy.GateCode(PayPalWebhookGate.SignatureInvalid),
                        PayPalBillingPolicy.GateCode(gate));
    }

    [Fact]
    public void Gate_Rejects_WhenPayPalIsNotConfiguredAtAll()
        => Assert.Equal(PayPalWebhookGate.NotConfigured,
            PayPalBillingPolicy.Gate(new PayPalOptions(), Full, signatureVerified: true));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Gate_Rejects_WhenASignatureHeaderIsBlank(string blank)
        => Assert.Equal(PayPalWebhookGate.HeadersMissing,
            PayPalBillingPolicy.Gate(Ready(), Full with { CertUrl = blank }, signatureVerified: true));

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Gate_Rejects_WhenTheSignatureIsNotVerified(bool? verified)
        => Assert.Equal(PayPalWebhookGate.SignatureInvalid,
            PayPalBillingPolicy.Gate(Ready(), Full, verified));

    /// <summary><b>‏400 لا ‏500، وبِلا HTML ولا تَحويل</b>: المُنادي
    /// آلَةٌ لا مُتَصَفِّح. و‏4xx يُوقِف إعادَةَ إرسالِ PayPal —
    /// ورِسالَةٌ بِتَوقيعٍ فاسِدٍ لا تُرادُ إعادَتُها.</summary>
    [Theory]
    [InlineData(PayPalWebhookGate.NotConfigured)]
    [InlineData(PayPalWebhookGate.HeadersMissing)]
    [InlineData(PayPalWebhookGate.SignatureInvalid)]
    public void RejectedGate_Answers400(PayPalWebhookGate gate)
    {
        var result = PayPalSurface.Rejected(NullLog, gate);
        Assert.Equal(StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    // ═══ ٢. القِراءَة — بَعدَ التَحَقُّق وَحدَه ═══════════════════════

    [Fact]
    public void Parse_ReadsTheEventId_TypeSlug_SubscriptionAndNextBilling()
    {
        var e = PayPalBillingPolicy.Parse(
            """
            {"id":"WH-9","event_type":"BILLING.SUBSCRIPTION.ACTIVATED",
             "resource":{"id":"I-SUB7","custom_id":"ejar",
                         "billing_info":{"next_billing_time":"2026-09-24T00:00:00Z"}}}
            """);

        Assert.NotNull(e);
        Assert.Equal("WH-9", e!.EventId);
        Assert.Equal(PayPalEventTypes.SubscriptionActivated, e.EventType);
        Assert.Equal("ejar", e.TenantSlug);
        Assert.Equal("I-SUB7", e.SubscriptionId);
        Assert.Equal(new DateTime(2026, 09, 24, 0, 0, 0, DateTimeKind.Utc), e.NextBillingTime);
    }

    /// <summary><b>‏<c>custom_id</c> مَوضِعانِ لا واحِد</b>: الدَفعَةُ
    /// الدَورِيَّةُ تَحمِلُه <c>resource.custom</c>. وقِراءَةُ أَحَدِهِما
    /// وَحدَه تَجعَل التَجديدَ الشَهريَّ «مُستَأجِراً مَجهولاً» كُلَّ
    /// شَهر.</summary>
    [Fact]
    public void Parse_ReadsTheSaleFlavour_custom_AndTheBillingAgreementId()
    {
        var e = PayPalBillingPolicy.Parse(
            """
            {"id":"WH-10","event_type":"PAYMENT.SALE.COMPLETED",
             "resource":{"id":"SALE-1","custom":"ejar","billing_agreement_id":"I-SUB7"}}
            """);

        Assert.Equal("ejar", e!.TenantSlug);
        Assert.Equal("I-SUB7", e.SubscriptionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"event_type\":\"BILLING.SUBSCRIPTION.ACTIVATED\"}")]   // بِلا id
    [InlineData("{\"id\":\"WH-1\"}")]                                     // بِلا نَوع
    public void Parse_ReturnsNull_AndNeverThrows(string body)
        => Assert.Null(PayPalBillingPolicy.Parse(body));

    // ═══ ٣. القَرار — تاريخٌ يُؤخَذ ولا يُخترَع ═══════════════════════

    /// <summary>‏<c>next_billing_time</c> مِن PayPal هُوَ المَصدَرُ
    /// الأَوَّل — مَوعِدُ الاستِحقاقِ الحَقيقيُّ لِلدافِع.</summary>
    [Fact]
    public void Extend_PrefersTheNextBillingTimeFromPayPal()
    {
        var next = Now.AddDays(40);
        var d = PayPalBillingPolicy.Decide(Event(next: next), MonthlyPlan(), false, Now);

        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(next, d.NewExpiresAt);
        Assert.True(d.Writes);
    }

    /// <summary>وبِلا <c>next_billing_time</c> يُضاف <b>طولُ المُدَّةِ
    /// الَّتي ضَبَطَها المُشرِف</b> — ‏30 يَوماً هُنا، مَقروءَةً مِن
    /// الوَثيقَةِ لا مَكتوبَةً في كود.</summary>
    [Fact]
    public void Extend_FallsBackToTheAdminSetPeriodLength()
    {
        var plan = MonthlyPlan();
        var d = PayPalBillingPolicy.Decide(Event(), plan, false, Now);

        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(plan.ExpiresAt.AddDays(30), d.NewExpiresAt);
    }

    /// <summary><b>مَن جَدَّدَ مُبَكِّراً لا يُصادَر ما تَبَقّى لَه</b>:
    /// المِرساةُ <c>ExpiresAt</c> لا <c>now</c>.</summary>
    [Fact]
    public void Extend_AnchorsOnTheRemainingPeriod_NotOnToday()
    {
        var plan = MonthlyPlan();
        var d = PayPalBillingPolicy.Decide(Event(), plan, false, Now);
        Assert.True(d.NewExpiresAt > Now.AddDays(30));
    }

    /// <summary><b>ومَن عادَ بَعدَ انقِطاعٍ لا يُشتَرى لَه ماضٍ مَضى</b>:
    /// المِرساةُ <c>now</c> حينَ تَجاوَزَتها.</summary>
    [Fact]
    public void Extend_AnchorsOnToday_WhenThePlanAlreadyLapsed()
    {
        var plan = MonthlyPlan();
        plan.StartsAt  = Now.AddDays(-90);
        plan.ExpiresAt = Now.AddDays(-60);      // مُدَّةٌ طولُها ‏30 يَوماً، مُنقَضِيَة

        var d = PayPalBillingPolicy.Decide(Event(), plan, false, Now);

        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(Now.AddDays(30), d.NewExpiresAt);
    }

    /// <summary>ولا مُدَّةَ ولا مَوعِدَ ⇒ <b>لا كِتابَة</b>. و«شَهرٌ
    /// افتِراضيّ» هُنا اختِراعُ بَياناتِ مُنتَجٍ بِثَمَنٍ نَقديّ
    /// (القاعِدَة ١٦).</summary>
    [Fact]
    public void UnknownPeriod_WritesNothing_AndInventsNoMonth()
    {
        var plan = MonthlyPlan();
        plan.StartsAt = plan.ExpiresAt;   // صِفرُ مُدَّة

        var d = PayPalBillingPolicy.Decide(Event(), plan, false, Now);

        Assert.Equal(PayPalBillingAction.UnknownPeriod, d.Action);
        Assert.False(d.Writes);
    }

    // ═══ ٤. مَرَّة-واحِدَة: تَكرارُ الحَدَثِ لا يُمَدِّد ثانِيَةً ══════

    [Fact]
    public void ASeenEvent_IsAReplay_AndWritesNothing()
    {
        var d = PayPalBillingPolicy.Decide(Event(), MonthlyPlan(), alreadySeen: true, Now);

        Assert.Equal(PayPalBillingAction.Replay, d.Action);
        Assert.False(d.Writes);
        Assert.Contains("WH-EVT-1", d.ReasonAr);
    }

    /// <summary><b>ونَفسُ الحَدَثِ مَرَّتَينِ يُعطي تَمديداً واحِداً</b>
    /// — الأولى تُمَدِّد وتُسَجِّل، والثانِيَةُ تَقرَأُ السِجِلَّ
    /// فَتَقِف. والتاريخُ بَعدَهُما هُوَ التاريخُ بَعدَ الأولى
    /// حَرفاً.</summary>
    [Fact]
    public void TheSameEventTwice_ExtendsExactlyOnce()
    {
        var plan = MonthlyPlan();
        var e = Event();

        var first = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, Now);
        PayPalBillingPolicy.Apply(plan, e, first, Now);
        var afterFirst = plan.ExpiresAt;

        var second = PayPalBillingPolicy.Decide(e, plan, alreadySeen: true, Now);
        PayPalBillingPolicy.Apply(plan, e, second, Now);

        Assert.Equal(PayPalBillingAction.Replay, second.Action);
        Assert.Equal(afterFirst, plan.ExpiresAt);
    }

    /// <summary>ومِفتاحُ السِجِلِّ هُوَ <c>event_id</c> — لا الوَقتُ ولا
    /// السلاج. وهذا هُوَ المَوضِعُ الَّذي يَنكَسِر صامِتاً: مِفتاحٌ
    /// آخَرُ يَجعَل كُلَّ إعادَةِ إرسالٍ تُمَدِّدُ شَهراً.</summary>
    [Fact]
    public void TheIdempotencyRecord_IsKeyedByTheEventId()
    {
        var e = Event(id: "WH-UNIQUE-42");
        var d = PayPalBillingPolicy.Decide(e, MonthlyPlan(), false, Now);
        var rec = PayPalBillingPolicy.RecordFor(e, d, Now.AddDays(40), Now);

        Assert.Equal("WH-UNIQUE-42", rec.Id);
        Assert.Equal("ejar", rec.TenantSlug);
        Assert.Equal(nameof(PayPalBillingAction.Extend), rec.Action);
        Assert.Equal(Now.AddDays(40), rec.AppliedExpiresAt);
    }

    // ═══ ٥. custom_id مَجهول ⇒ لا كِتابَة، وسَطرُ لوغ ═════════════════

    [Fact]
    public void AnUnknownCustomId_WritesNothing()
    {
        var d = PayPalBillingPolicy.Decide(
            Event(slug: "no-such-store"), plan: null, alreadySeen: false, Now);

        Assert.Equal(PayPalBillingAction.UnknownTenant, d.Action);
        Assert.False(d.Writes);
        Assert.Contains("no-such-store", d.ReasonAr);
    }

    [Fact]
    public void AMissingCustomId_WritesNothing()
    {
        var d = PayPalBillingPolicy.Decide(
            Event(slug: null), MonthlyPlan(), alreadySeen: false, Now);

        Assert.Equal(PayPalBillingAction.UnknownTenant, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>والسَطرُ هُوَ المُنتَج</b>: مالٌ وَصَلَ وَسايِل ولا
    /// يُعرَف لِمَن — يُقالُ عِندَ <c>Error</c> لا <c>Information</c>،
    /// فَيُرى في سِجِلٍّ يُقرَأ بِالمُستَوى.</summary>
    [Fact]
    public void AnUnknownCustomId_IsLoggedAsAnError_NamingTheEvent()
    {
        var log = new CapturingLogger();
        var e = Event(slug: "no-such-store");
        var d = PayPalBillingPolicy.Decide(e, null, false, Now);

        var result = PayPalSurface.NoWrite(log, e, d);

        var line = Assert.Single(log.Lines.Where(l => l.Level == LogLevel.Error));
        Assert.Contains("UnknownTenant", line.Text);
        Assert.Contains("WH-EVT-1", line.Text);
        // ‏200 لا خَطَأ: الرِسالَةُ فُهِمَت، وقَرارُنا أَلّا نَفعَل —
        // ورَدُّ خَطَإٍ يَجعَل PayPal تُعيدُها إلى الأَبَد.
        Assert.Equal(StatusCodes.Status200OK,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    /// <summary>وحَدَثٌ عادِيٌّ بِلا فِعلٍ لا يُصَعِّدُ إلى
    /// <c>Error</c> — سِجِلٌّ يَصرُخ عِندَ كُلّ شَيءٍ لا يُقرَأ.</summary>
    [Fact]
    public void AnIgnoredEventType_IsInformationalNotAnError()
    {
        var log = new CapturingLogger();
        var e = Event(type: "BILLING.SUBSCRIPTION.CREATED");
        var d = PayPalBillingPolicy.Decide(e, MonthlyPlan(), false, Now);

        Assert.Equal(PayPalBillingAction.Ignored, d.Action);
        PayPalSurface.NoWrite(log, e, d);
        Assert.Empty(log.Lines.Where(l => l.Level == LogLevel.Error));
    }

    // ═══ ٦. الإلغاء لا يُطفِئ مَتجَراً سارِياً ════════════════════════

    /// <summary>
    /// <b>مَن دَفَعَ مُدَّتَه يَأخُذُها كامِلَةً.</b> الإلغاءُ يُوقِف
    /// التَجديدَ ولا يَمَسّ <c>ExpiresAt</c> ولا <c>Status</c> —
    /// والحالَةُ تَبقى <c>Active</c> إلى يَومِها. وإطفاءُ مَتجَرٍ
    /// دَفَعَ ثَمَنَ شَهرِه لِأَنَّه أَلغى التَجديدَ في يَومِه الثاني
    /// مُصادَرَةٌ لا سِياسَة.
    /// </summary>
    [Theory]
    [InlineData(PayPalEventTypes.SubscriptionCancelled)]
    [InlineData(PayPalEventTypes.SubscriptionSuspended)]
    public void Cancellation_StopsRenewal_ButNeverDarkensALivePaidStore(string type)
    {
        var plan = MonthlyPlan();
        var expiresBefore = plan.ExpiresAt;
        var e = Event(type: type);

        var d = PayPalBillingPolicy.Decide(e, plan, false, Now);
        Assert.Equal(PayPalBillingAction.StopRenewal, d.Action);

        PayPalBillingPolicy.Apply(plan, e, d, Now);

        Assert.Equal(expiresBefore, plan.ExpiresAt);
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);
        Assert.Equal(Now, plan.RenewalCancelledAt);

        // والحالَةُ المُشتَقَّةُ — وهي ما يَراهُ الحارِسُ فِعلاً — سارِيَة.
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(plan, Now));
        Assert.True(TenantPlanPolicy.AllowsWrite(TenantPlanPolicy.Derive(plan, Now)));

        // وتَنتَهي في يَومِها هي، لا قَبلَه.
        Assert.Equal(TenantPlanState.Grace,
            TenantPlanPolicy.Derive(plan, expiresBefore.AddDays(1)));
    }

    /// <summary>ومَن أَلغى ثُمَّ عادَ فَدَفَع، عادَ تَجديدُه — وإلّا
    /// قالَت الشاشَةُ «التَجديدُ مُوقَف» لِمَن يَدفَع.</summary>
    [Fact]
    public void APaymentAfterACancellation_ClearsTheCancellationMark()
    {
        var plan = MonthlyPlan();
        var cancel = Event(type: PayPalEventTypes.SubscriptionCancelled, id: "WH-C");
        PayPalBillingPolicy.Apply(plan, cancel,
            PayPalBillingPolicy.Decide(cancel, plan, false, Now), Now);
        Assert.NotNull(plan.RenewalCancelledAt);

        var paid = Event(type: PayPalEventTypes.PaymentSaleCompleted, id: "WH-P");
        PayPalBillingPolicy.Apply(plan, paid,
            PayPalBillingPolicy.Decide(paid, plan, false, Now), Now);

        Assert.Null(plan.RenewalCancelledAt);
    }

    /// <summary><b>وإيقافُ المُشرِفِ اليَدَوِيُّ فَوقَ كُلِّ دَفعَة</b>:
    /// مَن أُوقِفَ لِسَبَبٍ لا يُعيدُه دَفعُ مالٍ وَحدَه.</summary>
    [Fact]
    public void APayment_NeverResurrectsAManuallyStoppedPlan()
    {
        var plan = MonthlyPlan();
        plan.Status = PlatformPlanStatuses.Stopped;
        var e = Event();

        PayPalBillingPolicy.Apply(plan, e, PayPalBillingPolicy.Decide(e, plan, false, Now), Now);

        Assert.Equal(PlatformPlanStatuses.Stopped, plan.Status);
        Assert.Equal(TenantPlanState.Suspended, TenantPlanPolicy.Derive(plan, Now));
    }

    /// <summary>والتَمديدُ يُبقي الباقَةَ صالِحَةً بِمَعجَمِ
    /// <see cref="TenantPlanPolicy.Validate"/> — فَلا يُنتِج PayPal
    /// وَثيقَةً تَرفُضُها شاشَةُ المُشرِف.</summary>
    [Fact]
    public void AnExtendedPlan_StaysValidByTheExistingValidator()
    {
        var plan = MonthlyPlan();
        var e = Event();
        PayPalBillingPolicy.Apply(plan, e, PayPalBillingPolicy.Decide(e, plan, false, Now), Now);

        Assert.True(TenantPlanPolicy.IsValid(plan));
        Assert.Equal("I-SUB1", plan.PayPalSubscriptionId);
    }

    // ═══ ٧. «صِفرُ كِتابَة» — بُرهانٌ بِنيَوِيّ لا مُلاحَظَة ═════════

    /// <summary>
    /// <para><b>قَرارٌ لا يَكتُب لا يَلمِس الجَلسَةَ إطلاقاً</b> —
    /// والبُرهان: تُمَرَّرُ الجَلسَةُ <c>null</c>. فَلَو لَمَسَتها
    /// الخِدمَةُ لَانفَجَرَت.</para>
    ///
    /// <para>وهذا أَقوى مِن «فَحَصنا القاعِدَةَ فَلَم نَجِد صَفّاً»:
    /// ذاكَ يَفحَص نَتيجَةً، وهذا يَفحَص أَنّ الطَريقَ نَفسَه
    /// مَقطوع.</para>
    /// </summary>
    [Theory]
    [InlineData(PayPalBillingAction.Replay)]
    [InlineData(PayPalBillingAction.Ignored)]
    [InlineData(PayPalBillingAction.UnknownTenant)]
    [InlineData(PayPalBillingAction.UnknownPeriod)]
    public void ANonWritingDecision_NeverTouchesTheSession(PayPalBillingAction action)
    {
        var decision = new PayPalBillingDecision(action, default, "—");
        Assert.False(decision.Writes);

        var applied = PayPalBillingService.Apply(
            session: null!, MonthlyPlan(), Event(), decision, Now);

        Assert.False(applied);
    }

    /// <summary>وكَذلك قَرارٌ يَكتُب بِلا وَثيقَةِ باقَة — لا شَيءَ
    /// يُخترَع لِيُخَزَّن.</summary>
    [Fact]
    public void AWritingDecision_WithoutAPlan_StillTouchesNothing()
    {
        var decision = new PayPalBillingDecision(PayPalBillingAction.Extend, Now.AddDays(30), "—");
        Assert.True(decision.Writes);
        Assert.False(PayPalBillingService.Apply(null!, plan: null, Event(), decision, Now));
    }

    /// <summary>ورابِطٌ لَم تُعِدهُ PayPal لا يُخَزَّن — ولا يُصنَع
    /// رابِطٌ فارِغٌ يَنقُرُه رائِدُ الأَعمالِ فَلا يَصِل شَيئاً.</summary>
    [Fact]
    public void AnEmptyApproveUrl_IsNeverSaved()
    {
        var refused = new SubscriptionResult("I-1", false, default, "nope");
        Assert.False(PayPalBillingService.SaveApproveLink(null!, MonthlyPlan(), refused, "by", Now));
    }

    // ═══ ٨. المَعجَمُ مُغلَق ═════════════════════════════════════════

    [Fact]
    public void TheEventVocabulary_IsTheFourTheOwnerSubscribesTo()
    {
        Assert.Equal(4, PayPalEventTypes.All.Count);
        Assert.All(PayPalEventTypes.All, t =>
            Assert.True(PayPalEventTypes.Extends(t) || PayPalEventTypes.StopsRenewal(t)));

        Assert.False(PayPalEventTypes.Extends("BILLING.SUBSCRIPTION.CREATED"));
        Assert.False(PayPalEventTypes.StopsRenewal("BILLING.SUBSCRIPTION.CREATED"));
    }

    [Fact]
    public void EveryGate_HasItsOwnCode()
    {
        var codes = Enum.GetValues<PayPalWebhookGate>()
            .Select(PayPalBillingPolicy.GateCode).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
    }

    // ─── أَدَوات ─────────────────────────────────────────────────────

    private static readonly ILogger NullLog =
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }
}
