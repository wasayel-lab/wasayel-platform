using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ المُعَلَّقُ قائِمٌ يُمسِك، والإلغاءُ وَحدَه يَفُكّ — التَبديل (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ١ · G4R1-2؛ تَوصيفُه `7dce2aa` بِبَصمَتِه) ═══
//
// **ما صارَ**: `TenantPlan.PayPalSuspendedAt` يَكتُبُه `BILLING.SUBSCRIPTION.SUSPENDED` مَعَ «لَن يُجَدَّد» ما دامَ الاشتِراكُ قائِماً، ويَمحوهُ الإلغاءُ
// (ومِنه إلغاءُ Paddle المارُّ بِالباعِثِ نَفسِه) والتَمديد؛ و`HoldsLiveSubscription` صادِقٌ ما دامَ المَحفوظُ مُعَلَّقاً؛ و`SetAsync` يَحمِلُ العَلامَة؛ والصَفحَةُ
// تَقولُ لِماذا يُمسِك. **ولِكُلِّ نَوعِ رِسالَةٍ مُوجَبٌ وسالِب** (القاعِدَة ٤).
//
// **‏2026-11-05 — مُراجَعَةُ G4 الجَولَة ٢ · G4R2-1**: «ومِنه إلغاءُ Paddle» أَعلاه مَقلوبٌ بِاسمِه في صَفِّه — إيقافٌ لا يُسَمّي اشتِراكاً لا يَمَسُّ القائِم،
// ويُمحى التَعليقُ بِتَمديدِ المَحفوظِ القائِمِ أَو اشتِراكٍ جَديدٍ لا بِأَيِّ تَمديد (`PayPalBillingPolicy.RenewsSubscription`).

public class PayPalSuspendedHoldSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc);

    private static TenantPlan Live() => PayPalArrangementChangeCharacterizationTests.LivePlan();

    private static PayPalWebhookEvent Suspended(string id = "WH-SUS-1", string sub = "I-SUB-1")
        => PayPalSuspendedHoldCharacterizationTests.SubscriptionEvent(PayPalEventTypes.SubscriptionSuspended, id, sub);

    private static PayPalWebhookEvent Cancelled(string id = "WH-CAN-1", string sub = "I-SUB-1")
        => PayPalSuspendedHoldCharacterizationTests.SubscriptionEvent(PayPalEventTypes.SubscriptionCancelled, id, sub);

    private static PayPalBillingDecision Apply(TenantPlan plan, PayPalWebhookEvent e, DateTime at)
    {
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, at);
        PayPalBillingPolicy.Apply(plan, e, d, at);
        return d;
    }

    // ─── (١) SUSPENDED ────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَب</b>: تَعليقُ الاشتِراكِ القائِم ⇐ «لَن يُجَدَّد» وعَلامَةُ التَعليقِ مَعاً، والإمساكُ باقٍ: تَغييرُ السِعرِ والانتِهاءِ يُرَدُّ بِالرَمز،
    /// وأَيّامُ السَماحِ تُحفَظُ والعَلامَةُ مَحمولَة.</summary>
    [Fact]
    public async Task A_suspension_of_the_live_subscription_keeps_the_hold()
    {
        var w = new DocWorld().Put(Live());
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = Apply(plan, Suspended(), At);

        Assert.Equal(PayPalBillingAction.StopRenewal, d.Action);
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.Equal(At, plan.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));

        var change = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt.AddDays(60), 14, 120m, "studio · admin", At);
        var grace = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt, 7, 99m, "studio · admin", At);
        var kept = w.Read<TenantPlan>(Slug)!;
        output.WriteLine($"بَعدَ التَعليق: قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)} · السِعرُ والانتِهاء: [{string.Join(",", change.Select(x => x.Code))}] · "
                       + $"السَماحُ وَحدَه: [{string.Join(",", grace.Select(x => x.Code))}] · عَلامَةُ التَعليقِ بَعدَه={kept.PayPalSuspendedAt:O}");

        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, change[0].Code);
        Assert.Empty(grace);
        Assert.Equal(7, kept.GraceDays);
        Assert.Equal(99m, kept.Price);
        Assert.Equal(At, kept.PayPalSuspendedAt);
        Assert.Equal(At, kept.RenewalCancelledAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(kept));
    }

    /// <summary><b>المُوجَبُ إلى آخِرِه</b>: تَعليقٌ ثُمَّ حِفظٌ مَردودٌ ثُمَّ دَفعَةُ إعادَةِ التَفعيل ⇐ يُمَدِّدُ التَرتيبَ المَحفوظَ بِطولِه (+30 لا +90) وبِسِعرِه،
    /// ويَمحو العَلامَتَين؛ ورِسالَةُ اشتِراكٍ آخَرَ والمُعَلَّقُ مَحفوظٌ ⇐ <c>ForeignSubscription</c>.</summary>
    [Fact]
    public async Task The_reactivated_sale_extends_the_arrangement_the_subscription_was_made_for()
    {
        var w = new DocWorld().Put(Live());
        var plan = w.Read<TenantPlan>(Slug)!;
        Apply(plan, Suspended(), At);
        Assert.NotEmpty(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", plan.StartsAt, plan.ExpiresAt.AddDays(60), 14, 120m, "studio · admin", At));

        var foreign = PayPalBillingPolicy.Decide(PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-2", "I-SUB-2"), plan, false, At);
        Assert.Equal(PayPalBillingAction.ForeignSubscription, foreign.Action);

        var d = Apply(plan, PayPalSuspendedHoldCharacterizationTests.Sale("WH-SALE-1"), At);
        output.WriteLine($"أُعيدَ تَفعيلُه · PAYMENT.SALE.COMPLETED لِـI-SUB-1: {d.Action} → {plan.ExpiresAt:yyyy-MM-dd} · السِعر={plan.Price} · "
                       + $"لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · مُعَلَّق={(plan.PayPalSuspendedAt is null ? "لا" : "نَعَم")}");

        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal(99m, plan.Price);
        Assert.Null(plan.RenewalCancelledAt);
        Assert.Null(plan.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
    }

    /// <summary><b>السالِبُ لِلتَعليق</b>: تَعليقٌ يَصِلُ بَعدَ الإلغاءِ (الرَسائِلُ لا تَصِلُ بِتَرتيبِها)، أَو لِباقَةٍ بِلا مَرجِعٍ مَحفوظ ⇐ لا عَلامَةَ تَعليقٍ ولا إمساك.</summary>
    [Fact]
    public void A_suspension_never_creates_a_hold_that_is_not_there()
    {
        var cancelledFirst = Live();
        Apply(cancelledFirst, Cancelled(), At);
        Apply(cancelledFirst, Suspended("WH-SUS-LATE"), At.AddMinutes(1));
        Assert.Null(cancelledFirst.PayPalSuspendedAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(cancelledFirst));

        var noReference = Live(); noReference.PayPalSubscriptionId = null;
        Apply(noReference, Suspended(), At);
        Assert.Null(noReference.PayPalSuspendedAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(noReference));
    }

    // ─── (٢) CANCELLED ────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَبُ لِلإلغاء</b>: الإلغاءُ — بَعدَ تَعليقٍ أَو بِدونِه — يَمحو عَلامَةَ التَعليقِ ويَفُكُّ الإمساك. و<b>إلغاءُ Paddle مَقلوبٌ بِاسمِه</b>
    /// (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ٢ · G4R2-1؛ كانَ «وإلغاءُ Paddle المارُّ بِالباعِثِ نَفسِه يَمحوها أَيضاً»): لا يُسَمّي اشتِراكاً، فَلا يَمَسُّ تَعليقَ
    /// اشتِراكِ PayPal القائِمِ ولا يَفُكُّ إمساكَه — PayPal ما زالَت تُجَدِّدُ بِسِعرِها.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cancel_clears_the_suspension_and_releases_the_hold(bool suspendedFirst)
    {
        var plan = Live();
        if (suspendedFirst) Apply(plan, Suspended(), At);
        var d = Apply(plan, Cancelled(), At.AddMinutes(1));

        Assert.Equal(PayPalBillingAction.StopRenewal, d.Action);
        Assert.Null(plan.PayPalSuspendedAt);
        Assert.Equal(At.AddMinutes(1), plan.RenewalCancelledAt);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(plan));

        var paddle = Live();
        Apply(paddle, Suspended(), At);
        PayPalBillingPolicy.Apply(paddle, new PayPalWebhookEvent("PD-1", "subscription.canceled", Slug, null, null, "paddle"),
            new PayPalBillingDecision(PayPalBillingAction.StopRenewal, paddle.ExpiresAt, "paddle"), At);
        Assert.Equal(At, paddle.PayPalSuspendedAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(paddle));
    }

    // ─── (٣) الحُكمُ النَقِيّ والصَفحَة ─────────────────────────────────────────────────────────────────────

    /// <summary><b><c>HoldsLiveSubscription</c> بِجَدوَلِه</b>: مَرجِعٌ ولا عَلامَة ⇐ يُمسِك؛ مَرجِعٌ وتَعليق ⇐ يُمسِك؛ مَرجِعٌ وإلغاءٌ بِلا تَعليق ⇐ لا؛ ولا مَرجِع ⇐ لا.</summary>
    [Fact]
    public void HoldsLiveSubscription_reads_the_suspension_mark()
    {
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(Live()));

        var suspended = Live(); suspended.RenewalCancelledAt = At; suspended.PayPalSuspendedAt = At;
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(suspended));

        var cancelled = Live(); cancelled.RenewalCancelledAt = At;
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(cancelled));

        var none = Live(); none.PayPalSubscriptionId = null; none.RenewalCancelledAt = At; none.PayPalSuspendedAt = At;
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(none));
    }

    /// <summary><b>الشاشَةُ تَقولُ لِماذا يُمسِك</b> (القاعِدَة ١٢): سَطرُ التَعليقِ داخِلَ شَرطِ الإمساكِ فَوقَ نَموذَجِ التَعيين، بِشَرطِه، ومِفتاحُه في <c>ar.json</c>.</summary>
    [Fact]
    public void The_admin_page_says_the_suspended_subscription_still_holds()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var page = File.ReadAllText(Path.Combine(root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Admin", "TenantPlanAdmin.razor"));
        var ar = File.ReadAllText(Path.Combine(root, "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));

        var hold = page.IndexOf("@if (TenantPlanPolicy.HoldsLiveSubscription(plan))", StringComparison.Ordinal);
        var line = page.IndexOf("L.Markup(\"admin.tenant_plan.paypal_subscription_suspended\")", StringComparison.Ordinal);
        var guard = page.LastIndexOf("@if (plan.PayPalSuspendedAt is { } suspended)", line, StringComparison.Ordinal);
        var field = page.IndexOf("admin.tenant_plan.field_plan", StringComparison.Ordinal);
        Assert.True(hold > 0 && guard > hold && line > guard && line - guard < 200 && line < field,
            "سَطرُ التَعليقِ لَيسَ تَحتَ شَرطِه داخِلَ شَرطِ الإمساكِ فَوقَ حُقولِ التَعيين.");
        Assert.Contains("\"admin.tenant_plan.paypal_subscription_suspended\":", ar, StringComparison.Ordinal);
    }
}
