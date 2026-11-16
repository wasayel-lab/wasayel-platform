using System.Net;
using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الإمساكُ لِلاشتِراكِ المَحفوظِ وَحدَه — التَبديل (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ٢ · G4R2-1؛ تَوصيفُه `d356a3f` بِبَصمَتِه) ═══
//
// **ما صارَ**: `PayPalBillingPolicy.RenewsSubscription` — يُقرَأُ قَبلَ كِتابَةِ المُعَرِّف — هُوَ وَحدَه ما يَمحو `RenewalCancelledAt` و`PayPalSuspendedAt`
// في `Extend`: رِسالَةُ اشتِراكٍ غَيرِ المَحفوظ، أَو المَحفوظُ ما دامَ قائِماً. فَطَلَبُ الدَفعِ مَرَّةً ومُعامَلَةُ Paddle (مُعَرِّفُهُما `null`) ودَفعَةُ المُلغى
// المُتَأَخِّرَةُ تُمَدِّدُ ولا تُعيدُ الإمساك. و`StopRenewal` بِلا مُعَرِّفِ اشتِراكٍ (إلغاءُ Paddle) لا يَمَسُّ اشتِراكَ PayPal القائِم.
// **فَالإمساكُ لا يَبدَأُ إلّا بِرِسالَةِ PayPal لِاشتِراكٍ غَيرِ المَحفوظ، ولا يَفُكُّه إلّا إيقافُ المَحفوظِ نَفسِه** — ولِكُلِّ مَصدَرٍ مُوجَبٌ وسالِب (القاعِدَة ٤).

public class PayPalCancelledHoldSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private const string PayPalPlan = "P-PLAN-1";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc);

    private static TenantPlan Live() => PayPalArrangementChangeCharacterizationTests.LivePlan();

    private static PayPalWebhookEvent Subscription(string type, string id, string sub = "I-SUB-1")
        => PayPalSuspendedHoldCharacterizationTests.SubscriptionEvent(type, id, sub);

    private static PayPalWebhookEvent Sale(string id, string sub = "I-SUB-1")
        => PayPalSuspendedHoldCharacterizationTests.Sale(id, sub);

    private static PayPalBillingDecision Paypal(DocWorld w, PayPalWebhookEvent e)
    {
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, At);
        PayPalBillingService.Apply(w.Session, plan, e, d, At);
        return d;
    }

    private static DocWorld Cancelled()
    {
        var w = new DocWorld().Put(Live());
        Paypal(w, Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1"));
        return w;
    }

    private static void Order(DocWorld w)
    {
        var order = new PayPalOrderRecord
        {
            Id = "wsl-plan-demo-ord1", TenantSlug = Slug, PlanId = "manual", Amount = 49m, Currency = "USD", Days = 30,
            OrderId = "5O190127TN364715T", ApproveUrl = "https://www.paypal.com/checkoutnow?token=5O1",
            Status = PayPalOrderStatuses.Created, CreatedAt = At, At = At,
        };
        var e = new PayPalOrderEvent("WH-CAP-1", PayPalOrderEventTypes.CaptureCompleted, order.Id, order.OrderId, "3C6", null,
            PayPalOrderBillingPolicy.CaptureCompletedStatus, null, "49.00", "USD", "46.30");
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = PayPalOrderBillingPolicy.Decide(e, order, plan, alreadySeen: false, At);
        Assert.Equal(PayPalOrderAction.Extend, d.Action);
        Assert.True(PayPalBillingService.ApplyOrder(w.Session, plan, order, e, d, At));
    }

    private static void Paddle(DocWorld w)
    {
        var record = new PaddleTransactionRecord
        {
            Id = "wsl-pd-plan-demo-1", TenantSlug = Slug, PlanId = "manual", Amount = 49m, AmountMinor = "4900", Currency = "USD",
            Days = 30, TransactionId = "txn_01j", Status = PaddleTransactionStatuses.Created,
            CheckoutUrl = "https://wasayel.example/billing/paddle/checkout.html?_ptxn=txn_01j", CreatedAt = At, At = At,
        };
        var e = new PaddleEvent("evt_1", PaddleEventTypes.TransactionCompleted, record.Id, "txn_01j", null,
            PaddleFieldValues.TransactionCompleted, "4900", "USD", null);
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = PaddleBillingPolicy.Decide(e, record, plan, alreadySeen: false, At);
        Assert.Equal(PaddleAction.Extend, d.Action);
        Assert.True(PaddleBillingService.ApplyTransaction(w.Session, plan, record, e, d, At));
    }

    private static void PaddleCancel(TenantPlan plan)
        => PayPalBillingPolicy.Apply(plan,
            new PayPalWebhookEvent("PD-1", PaddleEventTypes.SubscriptionCanceled, Slug, null, null, PaddleBillingService.ProviderName),
            new PayPalBillingDecision(PayPalBillingAction.StopRenewal, plan.ExpiresAt, "paddle"), At);

    // ─── (١) الحُكمُ النَقِيّ ────────────────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَب</b>: اشتِراكٌ جَديدٌ بَعدَ الإلغاء، وأَوَّلُ مُعَرِّفٍ لِباقَةٍ بِلا مَرجِع، والمَحفوظُ القائِم، والمَحفوظُ المُعَلَّق ⇐ يُجَدِّد.</summary>
    [Fact]
    public void RenewsSubscription_for_a_new_subscription_or_the_live_stored_one()
    {
        var cancelled = Live(); cancelled.RenewalCancelledAt = At;
        Assert.True(PayPalBillingPolicy.RenewsSubscription(cancelled, Sale("WH-1", "I-SUB-2")));

        var first = Live(); first.PayPalSubscriptionId = null; first.RenewalCancelledAt = At;
        Assert.True(PayPalBillingPolicy.RenewsSubscription(first, Subscription(PayPalEventTypes.SubscriptionActivated, "WH-2")));

        Assert.True(PayPalBillingPolicy.RenewsSubscription(Live(), Sale("WH-3")));

        var suspended = Live(); suspended.RenewalCancelledAt = At; suspended.PayPalSuspendedAt = At;
        Assert.True(PayPalBillingPolicy.RenewsSubscription(suspended, Sale("WH-4")));
    }

    /// <summary><b>السالِب</b>: رِسالَةٌ بِلا مُعَرِّفِ اشتِراكٍ (طَلَبٌ، Paddle) عَلى قائِمٍ أَو مُلغى، ودَفعَةُ المَحفوظِ بَعدَ إلغائِه ⇐ لا يُجَدِّد.</summary>
    [Fact]
    public void RenewsSubscription_is_false_for_a_message_without_a_subscription_or_the_cancelled_stored_one()
    {
        var cancelled = Live(); cancelled.RenewalCancelledAt = At;
        var noId = new PayPalWebhookEvent("WH-5", PayPalOrderEventTypes.CaptureCompleted, Slug, null, null);

        Assert.False(PayPalBillingPolicy.RenewsSubscription(Live(), noId));
        Assert.False(PayPalBillingPolicy.RenewsSubscription(cancelled, noId));
        Assert.False(PayPalBillingPolicy.RenewsSubscription(cancelled, Sale("WH-6")));
        Assert.False(PayPalBillingPolicy.RenewsSubscription(cancelled, noId with { SubscriptionId = "" }));
    }

    // ─── (٢) بَعدَ الإلغاء: كُلُّ مَصدَرٍ يُمَدِّدُ ولا يُعيدُ الإمساك ─────────────────────────────────────────────

    /// <summary><b>السالِبُ لِكُلِّ مَصدَر</b>: طَلَبُ دَفعٍ، مُعامَلَةُ Paddle، دَفعَةُ المُلغى المُتَأَخِّرَة، وإلغاءٌ كَتَبَ أَوَّلَ مُعَرِّفٍ ثُمَّ دَفعَتُه ⇐ يُمَدِّدُ ثَلاثينَ يَوماً،
    /// و«لَن يُجَدَّد» باقٍ، ولا إمساك: الحِفظُ يُقبَلُ ويُكتَب، وبابُ الرابِطِ لا يَرُدّ.</summary>
    [Theory]
    [InlineData("order")]
    [InlineData("paddle")]
    [InlineData("late-sale")]
    [InlineData("first-reference")]
    public async Task After_the_cancel_no_source_rearms_the_hold(string source)
    {
        DocWorld w;
        if (source == "first-reference")
        {
            var first = Live(); first.PayPalSubscriptionId = null;
            w = new DocWorld().Put(first);
            Paypal(w, Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1"));
        }
        else w = Cancelled();

        if (source == "order") Order(w);
        else if (source == "paddle") Paddle(w);
        else Assert.Equal(PayPalBillingAction.Extend, Paypal(w, Sale("WH-SALE-LATE")).Action);

        var plan = w.Read<TenantPlan>(Slug)!;
        var refusal = PayPalSurface.LinkRefusal(plan, configured: true, PayPalPlan);
        var before = w.Wrote<TenantPlan>();
        var v = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt, 14, 120m, "studio · admin", At);
        output.WriteLine($"{source}: الانتِهاء={plan.ExpiresAt:yyyy-MM-dd} · لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · "
                       + $"قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)} · رَفضُ الرابِط={refusal ?? "null"} · خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before}");

        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(plan));
        Assert.Null(refusal);
        Assert.Empty(v);
        Assert.Equal(1, w.Wrote<TenantPlan>() - before);
        Assert.Equal(120m, w.Read<TenantPlan>(Slug)!.Price);
    }

    /// <summary><b>المُوجَبُ المُقابِل</b>: تَفعيلُ اشتِراكٍ جَديدٍ أَو دَفعَتُه بَعدَ الإلغاء ⇐ يَتَسَلَّمُ المَرجِعَ ويَمحو العَلامَتَينِ ويُمسِك؛ ثُمَّ إلغاؤه هُوَ يَفُكّ.</summary>
    [Theory]
    [InlineData(PayPalEventTypes.SubscriptionActivated)]
    [InlineData(PayPalEventTypes.PaymentSaleCompleted)]
    public async Task A_new_subscription_after_the_cancel_holds_until_its_own_cancel(string type)
    {
        var w = Cancelled();
        var e = type == PayPalEventTypes.PaymentSaleCompleted ? Sale("WH-NEW-1", "I-SUB-2") : Subscription(type, "WH-NEW-1", "I-SUB-2");
        Assert.Equal(PayPalBillingAction.Extend, Paypal(w, e).Action);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal("I-SUB-2", plan.PayPalSubscriptionId);
        Assert.Null(plan.RenewalCancelledAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, PayPalSurface.LinkRefusal(plan, configured: true, PayPalPlan));
        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive,
            Assert.Single(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt, 14, 120m, "studio · admin", At)).Code);

        Assert.Equal(PayPalBillingAction.StopRenewal, Paypal(w, Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-2", "I-SUB-2")).Action);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(w.Read<TenantPlan>(Slug)!));
    }

    // ─── (٣) القائِمُ والمُعَلَّق ──────────────────────────────────────────────────────────────────────────

    /// <summary><b>السالِب</b>: طَلَبُ دَفعٍ أَو مُعامَلَةُ Paddle عَلى مُعَلَّقٍ ⇐ يُمَدِّدُ ويَبقى سَطرُ التَعليقِ والإمساك (PayPal ما زالَت مُعَلِّقَة).</summary>
    [Theory]
    [InlineData("order")]
    [InlineData("paddle")]
    public void A_one_off_payment_keeps_the_suspension_line_and_the_hold(string source)
    {
        var w = new DocWorld().Put(Live());
        Paypal(w, Subscription(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));
        if (source == "order") Order(w); else Paddle(w);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal(At, plan.PayPalSuspendedAt);
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }

    /// <summary><b>المُوجَب</b>: دَفعَةُ المَحفوظِ القائِمِ أَو المُعَلَّق (إعادَةُ تَفعيلِه) ⇐ تُمَدِّدُ وتَمحو العَلامَتَينِ ويَبقى الإمساك.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_sale_of_the_live_stored_subscription_clears_both_marks(bool suspendedFirst)
    {
        var w = new DocWorld().Put(Live());
        if (suspendedFirst) Paypal(w, Subscription(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));
        Assert.Equal(PayPalBillingAction.Extend, Paypal(w, Sale("WH-SALE-1")).Action);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Null(plan.RenewalCancelledAt);
        Assert.Null(plan.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }

    // ─── (٤) إيقافٌ لا يُسَمّي اشتِراكاً ─────────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَب</b>: إلغاءُ Paddle عَلى اشتِراكِ PayPal قائِمٍ أَو مُعَلَّق ⇐ لا عَلامَةَ ولا فَكّ، ودَفعَةُ المَحفوظِ بَعدَه تَبقى تُمسِك.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_paddle_cancel_never_releases_a_live_paypal_subscription(bool suspendedFirst)
    {
        var w = new DocWorld().Put(Live());
        if (suspendedFirst) Paypal(w, Subscription(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));
        var plan = w.Read<TenantPlan>(Slug)!;
        var cancelledBefore = plan.RenewalCancelledAt;
        var suspendedBefore = plan.PayPalSuspendedAt;

        PaddleCancel(plan);
        Assert.Equal(cancelledBefore, plan.RenewalCancelledAt);
        Assert.Equal(suspendedBefore, plan.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));

        Assert.Equal(PayPalBillingAction.Extend, Paypal(w, Sale("WH-SALE-1")).Action);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(w.Read<TenantPlan>(Slug)!));
    }

    /// <summary><b>السالِب</b>: إلغاءُ Paddle عَلى مَتجَرٍ بِلا اشتِراكِ PayPal قائِمٍ (لا مَرجِع، أَو مُلغىً) ⇐ يَكتُبُ «لَن يُجَدَّد» كَما كان؛ وإلغاءُ PayPal
    /// لِلمَحفوظِ يَفُكّ.</summary>
    [Fact]
    public void Without_a_live_paypal_subscription_a_stop_writes_the_mark_as_before()
    {
        var paddleOnly = Live(); paddleOnly.PayPalSubscriptionId = null;
        PaddleCancel(paddleOnly);
        Assert.Equal(At, paddleOnly.RenewalCancelledAt);
        Assert.Null(paddleOnly.PayPalSuspendedAt);

        var w = Cancelled();
        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }

    // ─── (٥) عَبرَ الشَبَكَة ──────────────────────────────────────────────────────────────────────────────

    /// <summary><b>دَفعَةٌ مُتَأَخِّرَةٌ مُوَقَّعَةٌ لِلمُلغى</b> ⇐ ‏200 بِـ<c>Extend</c> والتاريخُ يَتَحَرَّك، و«لَن يُجَدَّد» باقٍ ولا إمساك.</summary>
    [Fact]
    public async Task A_signed_late_sale_of_the_cancelled_subscription_extends_without_rearming()
    {
        var stored = Live(); stored.RenewalCancelledAt = At;
        var world = new DocWorld().Put(stored);
        var calls = new PayPalCalls().ThenToken().ThenVerify(true);
        await using var host = await PayPalHost.StartAsync(world, calls);

        var body = $$$"""
            {"id":"WH-SALE-LATE","event_type":"{{{PayPalEventTypes.PaymentSaleCompleted}}}",
             "resource":{"billing_agreement_id":"I-SUB-1","custom":"{{{Slug}}}"}}
            """;
        var response = await host.Client.SendAsync(PayPalEndpointBehaviourTests.Signed(body));
        var text = await response.Content.ReadAsStringAsync();
        var plan = world.Read<TenantPlan>(Slug)!;
        output.WriteLine($"webhook: {(int)response.StatusCode} {text} · لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PayPalBillingAction.Extend), text, StringComparison.Ordinal);
        Assert.Equal(1, world.Wrote<PayPalWebhookRecord>());
        Assert.True(plan.ExpiresAt > Expires);
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }
}
