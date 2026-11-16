using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اشتِراكُ PayPal أُلغِيَ ثُمَّ وَصَلَ مالٌ مِن غَيرِه — تَوصيفُ «اليَوم» (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ٢ · G4R2-1) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): `HoldsLiveSubscription` يَقرَأُ إلغاءَ PayPal لِلمُعَرِّفِ المَحفوظِ مِن
// `RenewalCancelledAt` (ومَعَه `PayPalSuspendedAt`)، و`PayPalBillingPolicy.Apply` يَمحو العَلامَتَينِ في **كُلِّ** `Extend` — ويَمُرُّ بِه طَلَبُ دَفعٍ مَرَّةً واحِدَة
// (`PayPalBillingService.ApplyOrder`، مُعَرِّفُ الاشتِراكِ `null`) ومُعامَلَةُ Paddle (`PaddleBillingService.ApplyTransaction`، كَذلِك) ودَفعَةٌ مُتَأَخِّرَةٌ لِلاشتِراكِ
// المُلغى نَفسِه (PayPal تُعيدُ ما فَشِلَ تَسليمُه، والرَسائِلُ لا تَصِلُ بِتَرتيبِها). فَيَعودُ «قائِماً» اشتِراكٌ أَلغَته PayPal: الحِفظُ يُرَدُّ بِـ
// `tenant_plan_paypal_subscription_live`، وبابُ الرابِطِ يَرُدُّ بِه، ورِسالَةُ الإلغاءِ لا تَعودُ (إعادَتُها مِن اللَوحَةِ بِمُعَرِّفِها نَفسِه ⇐ `Replay`).
// **وما يَبقى**: كُلُّ مَصدَرٍ يُمَدِّدُ التاريخَ بِما دُفِع، ولا يَمَسُّ المُعَرِّفَ ولا الحالَة؛ واشتِراكٌ جَديدٌ (مُعَرِّفٌ غَيرُ المَحفوظِ) بَعدَ الإلغاءِ يَتَسَلَّمُ
// المَرجِعَ ويُمحى «لَن يُجَدَّد» ويَصيرُ قائِماً؛ والقائِمُ يَبقى قائِماً بَعدَ أَيِّ تَمديد؛ وإعادَةُ الإلغاءِ إعادَة.

public class PayPalCancelledHoldCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private const string PayPalPlan = "P-PLAN-1";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc);

    private static TenantPlan Live() => PayPalArrangementChangeCharacterizationTests.LivePlan();

    private static PayPalWebhookEvent Subscription(string type, string id, string sub = "I-SUB-1")
        => PayPalSuspendedHoldCharacterizationTests.SubscriptionEvent(type, id, sub);

    private static PayPalBillingDecision Paypal(DocWorld w, PayPalWebhookEvent e)
    {
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, At);
        PayPalBillingService.Apply(w.Session, plan, e, d, At);
        return d;
    }

    /// <summary>إلغاءُ PayPal لِلمَحفوظ — ويَبقى أَنّه يَفُكُّ الإمساكَ ويَفتَحُ بابَ الرابِط.</summary>
    private static void Cancel(DocWorld w)
    {
        Assert.Equal(PayPalBillingAction.StopRenewal, Paypal(w, Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1")).Action);
        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(plan));
        Assert.Null(PayPalSurface.LinkRefusal(plan, configured: true, PayPalPlan));
    }

    /// <summary>طَلَبُ دَفعٍ مَرَّةً واحِدَة (ADR-006) — يُمَدِّدُ بِأَيّامِه.</summary>
    private static PayPalOrderDecision Order(DocWorld w)
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
        PayPalBillingService.ApplyOrder(w.Session, plan, order, e, d, At);
        Assert.Equal(PayPalOrderStatuses.Captured, order.Status);
        return d;
    }

    /// <summary>مُعامَلَةُ Paddle اكتَمَلَت — تُمَدِّدُ بِأَيّامِها.</summary>
    private static PaddleDecision Paddle(DocWorld w)
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
        PaddleBillingService.ApplyTransaction(w.Session, plan, record, e, d, At);
        Assert.Equal(PaddleTransactionStatuses.Completed, record.Status);
        return d;
    }

    private async Task<string> Print(string label, DocWorld w)
    {
        var plan = w.Read<TenantPlan>(Slug)!;
        var refusal = PayPalSurface.LinkRefusal(plan, configured: true, PayPalPlan);
        var before = w.Wrote<TenantPlan>();
        var v = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt, 14, 120m, "studio · admin", At);
        var line = $"{label}: لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · مُعَلَّق={(plan.PayPalSuspendedAt is null ? "لا" : "نَعَم")} · "
                 + $"قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)} · رَفضُ الرابِط={refusal ?? "null"} · "
                 + $"السِعرُ 99 ← 120: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before}";
        output.WriteLine(line);
        return line;
    }

    // ─── ١. بَعدَ الإلغاءِ: ثَلاثَةُ مَصادِرَ لَيسَت الاشتِراكَ القائِم ─────────────────────────────────────────────

    /// <summary><b>طَلَبُ دَفعٍ بَعدَ الإلغاء</b>: يُطبَعُ الإمساكُ والحِفظُ والرابِط؛ ويَبقى أَنّه يُمَدِّدُ ثَلاثينَ يَوماً ولا يَمَسُّ المُعَرِّف.</summary>
    [Fact]
    public async Task A_one_off_order_after_the_cancel()
    {
        var w = new DocWorld().Put(Live());
        Cancel(w);
        var d = Order(w);
        await Print("أُلغِيَ I-SUB-1 ثُمَّ طَلَبُ دَفعٍ (Extend)", w);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(PayPalOrderAction.Extend, d.Action);
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);
    }

    /// <summary><b>مُعامَلَةُ Paddle بَعدَ الإلغاء</b>: يُطبَعُ الإمساك؛ ويَبقى أَنّها تُمَدِّدُ ثَلاثينَ يَوماً ولا تَمَسُّ المُعَرِّف.</summary>
    [Fact]
    public async Task A_paddle_completion_after_the_cancel()
    {
        var w = new DocWorld().Put(Live());
        Cancel(w);
        var d = Paddle(w);
        await Print("أُلغِيَ I-SUB-1 ثُمَّ transaction.completed (Extend)", w);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(PaddleAction.Extend, d.Action);
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);
    }

    /// <summary><b>دَفعَةٌ مُتَأَخِّرَةٌ لِلمُلغى نَفسِه</b>: يُطبَعُ الإمساك؛ ويَبقى أَنّها تُمَدِّدُ بِطولِ المُدَّةِ وسِجِلُّها <c>Extend</c>.</summary>
    [Fact]
    public async Task A_late_sale_of_the_cancelled_subscription()
    {
        var w = new DocWorld().Put(Live());
        Cancel(w);
        var d = Paypal(w, PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-LATE"));
        await Print("أُلغِيَ I-SUB-1 ثُمَّ PAYMENT.SALE.COMPLETED مُتَأَخِّرَةٌ لِـI-SUB-1 (Extend)", w);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.Equal(1, w.Stored.OfType<PayPalWebhookRecord>().Count(r => r.Id == "WH-SALE-LATE" && r.Action == nameof(PayPalBillingAction.Extend)));
    }

    /// <summary><b>إلغاءٌ يَكتُبُ أَوَّلَ مُعَرِّفٍ ثُمَّ دَفعَتُه المُتَأَخِّرَة</b> (باقَةٌ بِلا مَرجِعٍ قَبلَه): يُطبَعُ الإمساك؛ ويَبقى أَنّ الإلغاءَ يَكتُبُ المُعَرِّفَ
    /// والدَفعَةَ تُمَدِّد.</summary>
    [Fact]
    public async Task A_late_sale_after_a_cancel_that_wrote_the_first_reference()
    {
        var first = Live(); first.PayPalSubscriptionId = null;
        var w = new DocWorld().Put(first);
        Assert.Equal(PayPalBillingAction.StopRenewal, Paypal(w, Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1")).Action);
        Assert.Equal("I-SUB-1", w.Read<TenantPlan>(Slug)!.PayPalSubscriptionId);

        var d = Paypal(w, PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-LATE"));
        await Print("بِلا مَرجِع · CANCELLED لِـI-SUB-1 ثُمَّ دَفعَتُه المُتَأَخِّرَة (Extend)", w);
        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(Expires.AddDays(30), w.Read<TenantPlan>(Slug)!.ExpiresAt);
    }

    // ─── ٢. ما يَبقى ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>يَبقى</b>: اشتِراكٌ جَديدٌ بَعدَ الإلغاءِ — تَفعيلُه أَو دَفعَتُه — يَتَسَلَّمُ المَرجِعَ ويَمحو «لَن يُجَدَّد» ويَصيرُ قائِماً يُمسِك.</summary>
    [Theory]
    [InlineData(PayPalEventTypes.SubscriptionActivated)]
    [InlineData(PayPalEventTypes.PaymentSaleCompleted)]
    public async Task A_new_subscription_after_the_cancel_takes_the_reference_and_holds(string type)
    {
        var w = new DocWorld().Put(Live());
        Cancel(w);
        var e = type == PayPalEventTypes.PaymentSaleCompleted
            ? PayPalSuspendedHoldCharacterizationTests.Sale("WH-NEW-1", "I-SUB-2")
            : Subscription(type, "WH-NEW-1", "I-SUB-2");
        Assert.Equal(PayPalBillingAction.Extend, Paypal(w, e).Action);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal("I-SUB-2", plan.PayPalSubscriptionId);
        Assert.Null(plan.RenewalCancelledAt);
        Assert.Null(plan.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, PayPalSurface.LinkRefusal(plan, configured: true, PayPalPlan));
        Assert.NotEmpty(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt, 14, 120m, "studio · admin", At));
    }

    /// <summary><b>يَبقى</b>: إلغاءٌ يُعادُ مِن لَوحَةِ PayPal بِمُعَرِّفِه نَفسِه ⇐ <c>Replay</c> بِلا كِتابَة.</summary>
    [Fact]
    public void A_cancel_resent_with_the_same_event_id_is_a_replay()
    {
        var d = PayPalBillingPolicy.Decide(Subscription(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1"), Live(), alreadySeen: true, At);
        Assert.Equal(PayPalBillingAction.Replay, d.Action);
        Assert.False(d.Writes);
    }

    /// <summary><b>يَبقى</b>: عَلى اشتِراكٍ قائِمٍ لَم يُلغَ يَبقى الإمساكُ بَعدَ طَلَبِ دَفعٍ أَو مُعامَلَةِ Paddle أَو دَفعَتِه هُوَ، ولا عَلامَةَ تُكتَب.</summary>
    [Theory]
    [InlineData("order")]
    [InlineData("paddle")]
    [InlineData("sale")]
    public void A_live_subscription_still_holds_after_any_extension(string source)
    {
        var w = new DocWorld().Put(Live());
        if (source == "order") Order(w);
        else if (source == "paddle") Paddle(w);
        else Paypal(w, PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-1"));

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Null(plan.RenewalCancelledAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }

    // ─── ٣. ما يُطبَعُ بِجِوارِه — حالانِ يَمَسُّهُما التَبديل ─────────────────────────────────────────────────

    /// <summary><b>مُعَلَّقٌ ثُمَّ طَلَبُ دَفع</b>: يُطبَعُ ما بَقِيَ مِن العَلامَتَين؛ ويَبقى الإمساكُ (المُعَلَّقُ قائِم).</summary>
    [Fact]
    public async Task A_one_off_order_on_a_suspended_subscription()
    {
        var w = new DocWorld().Put(Live());
        Paypal(w, Subscription(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));
        Order(w);
        await Print("عُلِّقَ I-SUB-1 ثُمَّ طَلَبُ دَفعٍ (Extend)", w);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(w.Read<TenantPlan>(Slug)!));
    }

    /// <summary><b>إلغاءُ Paddle عَلى اشتِراكِ PayPal قائِمٍ ثُمَّ دَفعَةُ المَحفوظ</b>: يُطبَعُ الإمساكُ بَعدَ كُلِّ خُطوَة؛ ويَبقى أَنّ الدَفعَةَ تُمَدِّد.</summary>
    [Fact]
    public async Task A_paddle_cancel_on_a_live_paypal_subscription_then_its_sale()
    {
        var w = new DocWorld().Put(Live());
        var plan = w.Read<TenantPlan>(Slug)!;
        PayPalBillingPolicy.Apply(plan, new PayPalWebhookEvent("PD-1", PaddleEventTypes.SubscriptionCanceled, Slug, null, null, PaddleBillingService.ProviderName),
            new PayPalBillingDecision(PayPalBillingAction.StopRenewal, plan.ExpiresAt, "paddle"), At);
        output.WriteLine($"إلغاءُ Paddle وI-SUB-1 قائِم: قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)}");

        Assert.Equal(PayPalBillingAction.Extend, Paypal(w, PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-1")).Action);
        await Print("ثُمَّ PAYMENT.SALE.COMPLETED لِـI-SUB-1 (Extend)", w);
        Assert.Equal(Expires.AddDays(30), w.Read<TenantPlan>(Slug)!.ExpiresAt);
    }
}
