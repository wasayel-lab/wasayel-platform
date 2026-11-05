using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَغييرُ تَرتيبِ PayPal لِمَتجَرٍ وعَلَيه اشتِراكٌ قائِم — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G4 · E-1) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): المُشرِفُ يُغَيِّرُ باقَةَ مَتجَرٍ أَو سِعرَها أَو تاريخَيها واشتِراكُ PayPal
// ‏S1 قائِمٌ (`PayPalSubscriptionId` مَحفوظٌ و`RenewalCancelledAt` فارِغ) — فَيُقبَلُ الحِفظ، ويَبقى S1 يَخصِمُ سِعرَه القَديمَ ويُمَدِّدُ الباقَةَ الجَديدَةَ
// بِطولِ مُدَّتِها (`Decide` يُحَمِّلُ الباقَةَ بِـ`custom_id` ولا يُقارِنُ `e.SubscriptionId`). ورابِطٌ تالٍ (S2) يَكتُبُ فَوقَ المَرجِعِ الوَحيدِ لِـS1
// (`SaveApproveLink`)، وكُلُّ رِسالَةٍ تُعيدُ كِتابَتَه — فَإن وافَقَ المَتجَرُ عَلى S2 خَصَمَ الاثنان، وإلغاءُ S1 مِن اللَوحَةِ يُعَلِّمُ «لَن يُجَدَّد» وS2 حَيّ.
// **وما يَبقى**: الحِفظُ لا يُسقِطُ مَرجِعَ الاشتِراكِ أَبَداً؛ وحِفظٌ لا يُغَيِّرُ التَرتيبَ (أَيّامُ السَماحِ وَحدَها) يُقبَل؛ واشتِراكٌ أُلغِيَ لا يَمنَعُ شَيئاً؛
// ورِسالَةٌ لِلاشتِراكِ المَحفوظِ نَفسِه تُمَدِّد؛ والرابِطُ يُحفَظُ ويُرسَمُ كَما كان.

public class PayPalArrangementChangeCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Starts = new(2026, 10, 31, 20, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc);

    /// <summary>باقَةٌ عَلَيها اشتِراكُ PayPal قائِم: مُعَرِّفٌ مَحفوظٌ ولا رِسالَةَ إلغاءٍ بَعدَه.</summary>
    internal static TenantPlan LivePlan() => new()
    {
        Id = Slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Starts, ExpiresAt = Expires, GraceDays = 14, Price = 99m,
        SetBy = "paypal · BILLING.SUBSCRIPTION.ACTIVATED", SetAt = Starts,
        PayPalSubscriptionId = "I-SUB-1",
        PayPalApproveUrl = "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-1",
    };

    private static Task<IReadOnlyList<TenantPlanViolation>> Save(
        DocWorld w, string planId, DateTime starts, DateTime expires, int grace, decimal price)
        => TenantPlanAdminService.SetAsync(w.Session, Slug, planId, starts, expires, grace, price, "studio · admin", At);

    private string Print(string label, IReadOnlyList<TenantPlanViolation> v, DocWorld w)
    {
        var p = w.Read<TenantPlan>(Slug)!;
        var line = $"{label}: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>()} · السِعر={p.Price} · "
                 + $"الانتِهاء={p.ExpiresAt:yyyy-MM-dd} · الاشتِراك={p.PayPalSubscriptionId ?? "—"}";
        output.WriteLine(line);
        return line;
    }

    // ─── ١. حِفظُ الباقَة ────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>اشتِراكٌ قائِمٌ والسِعرُ يَتَغَيَّر</b>: يُطبَعُ القَبولُ والمُخَزَّن؛ ويَبقى مَرجِعُ الاشتِراكِ ولا تُكتَبُ عَلامَةُ إلغاء.</summary>
    [Fact]
    public async Task Changing_the_price_while_a_paypal_subscription_is_live_keeps_its_reference()
    {
        var w = new DocWorld().Put(LivePlan());
        var v = await Save(w, "manual", Starts, Expires, 14, 120m);
        Print("اشتِراكٌ قائِمٌ · السِعرُ 99 ← 120", v, w);

        Assert.Equal("I-SUB-1", w.Read<TenantPlan>(Slug)!.PayPalSubscriptionId);
        Assert.Null(w.Read<TenantPlan>(Slug)!.RenewalCancelledAt);
    }

    /// <summary><b>اشتِراكٌ قائِمٌ والتاريخانِ يَتَغَيَّران</b> (تَمديدٌ يَدَوِيّ): يُطبَعُ القَبول؛ ويَبقى المَرجِع.</summary>
    [Fact]
    public async Task Extending_by_hand_while_a_paypal_subscription_is_live_keeps_its_reference()
    {
        var w = new DocWorld().Put(LivePlan());
        var v = await Save(w, "manual", Starts, Expires.AddDays(30), 14, 99m);
        Print("اشتِراكٌ قائِمٌ · الانتِهاءُ +30 يَوماً", v, w);

        Assert.Equal("I-SUB-1", w.Read<TenantPlan>(Slug)!.PayPalSubscriptionId);
    }

    /// <summary><b>يَبقى</b>: حِفظٌ لا يُغَيِّرُ الباقَةَ ولا السِعرَ ولا التاريخَين — أَيّامُ السَماحِ وَحدَها — يُقبَلُ ويُكتَب.</summary>
    [Fact]
    public async Task A_save_that_changes_only_the_grace_days_is_accepted_while_a_subscription_is_live()
    {
        var w = new DocWorld().Put(LivePlan());
        var v = await Save(w, "manual", Starts, Expires, 7, 99m);
        Print("اشتِراكٌ قائِمٌ · السَماحُ 14 ← 7", v, w);

        Assert.Empty(v);
        Assert.Equal(1, w.Wrote<TenantPlan>());
        Assert.Equal(7, w.Read<TenantPlan>(Slug)!.GraceDays);
        Assert.Equal("I-SUB-1", w.Read<TenantPlan>(Slug)!.PayPalSubscriptionId);
    }

    /// <summary><b>يَبقى</b>: اشتِراكٌ وَصَلَت رِسالَةُ إلغائِه (`RenewalCancelledAt`) لا يَمنَعُ تَغييرَ السِعرِ ولا الباقَة.</summary>
    [Fact]
    public async Task A_cancelled_subscription_never_holds_the_save()
    {
        var plan = LivePlan();
        plan.RenewalCancelledAt = At.AddDays(-1);
        var w = new DocWorld().Put(plan);
        var v = await Save(w, "manual", Starts, Expires.AddDays(30), 14, 120m);
        Print("اشتِراكٌ أُلغِيَ · السِعرُ والانتِهاء", v, w);

        Assert.Empty(v);
        Assert.Equal(120m, w.Read<TenantPlan>(Slug)!.Price);
    }

    // ─── ٢. رِسالَةُ PayPal ──────────────────────────────────────────────────────────────────────────────────

    private static PayPalWebhookEvent Event(string type, string? sub, string id = "WH-1")
        => new(id, type, Slug, sub, null);

    private string Decided(string label, TenantPlan plan, PayPalWebhookEvent e)
    {
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, At);
        PayPalBillingPolicy.Apply(plan, e, d, At);
        var line = $"{label}: {d.Action} · يَكتُب={d.Writes} · الاشتِراكُ بَعدَه={plan.PayPalSubscriptionId ?? "—"} · "
                 + $"لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · الانتِهاء={plan.ExpiresAt:yyyy-MM-dd}";
        output.WriteLine(line);
        return line;
    }

    /// <summary><b>رِسالَةٌ مِن اشتِراكٍ آخَرَ والمَحفوظُ قائِم</b> — تَفعيلٌ ودَفعَةٌ وإلغاء: يُطبَعُ القَرارُ وأَثَرُه.</summary>
    [Fact]
    public void Events_from_another_subscription_while_one_is_live()
    {
        foreach (var type in new[] { PayPalEventTypes.SubscriptionActivated, PayPalEventTypes.PaymentSaleCompleted, PayPalEventTypes.SubscriptionCancelled })
        {
            var plan = LivePlan();
            Decided($"S1 قائِم · {type} لِـI-SUB-2", plan, Event(type, "I-SUB-2"));
            Assert.Equal(PlatformPlanStatuses.Active, plan.Status);    // ‏لا فَرعَ يَمَسُّ الحالَة
            Assert.Equal(99m, plan.Price);
        }
    }

    /// <summary><b>يَبقى</b>: رِسالَةُ الاشتِراكِ المَحفوظِ نَفسِه تُمَدِّد، وإلغاؤه يُعَلِّمُ «لَن يُجَدَّد».</summary>
    [Fact]
    public void Events_from_the_stored_subscription_extend_and_stop_as_before()
    {
        var plan = LivePlan();
        Decided("S1 قائِم · دَفعَةُ I-SUB-1", plan, Event(PayPalEventTypes.PaymentSaleCompleted, "I-SUB-1"));
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);

        var stop = LivePlan();
        Decided("S1 قائِم · إلغاءُ I-SUB-1", stop, Event(PayPalEventTypes.SubscriptionCancelled, "I-SUB-1", "WH-2"));
        Assert.Equal(At, stop.RenewalCancelledAt);
    }

    /// <summary><b>يَبقى</b>: والمَحفوظُ أُلغِيَ ⇒ اشتِراكٌ آخَرُ يُفَعَّلُ فَيُمَدِّدُ ويَصيرُ هُوَ المَرجِعَ ويُمحى «لَن يُجَدَّد».</summary>
    [Fact]
    public void After_a_cancel_another_subscription_takes_over()
    {
        var plan = LivePlan();
        plan.RenewalCancelledAt = At.AddDays(-1);
        Decided("S1 أُلغِيَ · تَفعيلُ I-SUB-2", plan, Event(PayPalEventTypes.SubscriptionActivated, "I-SUB-2"));

        Assert.Equal("I-SUB-2", plan.PayPalSubscriptionId);
        Assert.Null(plan.RenewalCancelledAt);
        Assert.Equal(Expires.AddDays(30), plan.ExpiresAt);
    }

    // ─── ٣. رابِطُ المُوافَقَة ───────────────────────────────────────────────────────────────────────────────

    /// <summary><b>رابِطٌ يُحفَظ</b> — عَلى باقَةٍ بِلا اشتِراكٍ وعَلى باقَةٍ عَلَيها S1 قائِم: يُطبَعُ المَرجِعُ بَعدَه؛ ويَبقى الرابِطُ مَحفوظاً.</summary>
    [Fact]
    public void Saving_an_approve_link_and_the_subscription_reference_after_it()
    {
        foreach (var (label, plan) in new[] { ("بِلا اشتِراك", new TenantPlan { Id = Slug, PlanId = "manual" }), ("S1 قائِم", LivePlan()) })
        {
            var w = new DocWorld().Put(plan);
            var result = new SubscriptionResult("I-SUB-3", false, default, null, "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3");
            var saved = PayPalBillingService.SaveApproveLink(w.Session, plan, result, "studio · admin", At);
            output.WriteLine($"رابِطٌ عَلى «{label}»: حُفِظ={saved} · الاشتِراكُ بَعدَه={w.Read<TenantPlan>(Slug)!.PayPalSubscriptionId ?? "—"}");

            Assert.True(saved);
            Assert.Equal(result.ApproveUrl, w.Read<TenantPlan>(Slug)!.PayPalApproveUrl);
        }
    }

    // ─── ٤. نُقطَةُ الرابِط — تُشَغَّلُ لا تُقرَأ ───────────────────────────────────────────────────────────────

    private const string SubscriptionReply =
        """
        {"id":"I-SUB-3","status":"APPROVAL_PENDING",
         "links":[{"rel":"approve","href":"https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3"}]}
        """;

    private static PlatformPlanPayPal Binding() => new()
    {
        Id = "manual", ProductId = "PROD-1", PlanId = "P-10", Name = "باقَة", Amount = 10m, Currency = "USD", IntervalUnit = "MONTH",
    };

    private async Task<(HttpResponseMessage Response, DocWorld World, List<string> Paths)> PostLinkAsync(TenantPlan plan)
    {
        var admin = Guid.NewGuid();
        var world = new DocWorld().Put(plan).Put(Binding()).Put(PayPalEndpointBehaviourTests.Admin(admin));
        var calls = new PayPalCalls().ThenToken().Then(HttpStatusCode.Created, SubscriptionReply);
        await using var host = await PayPalHost.StartAsync(world, calls);
        PayPalEndpointBehaviourTests.SignIn(host.Client, admin);
        var response = await host.Client.PostAsync($"/admin/tenants/{Slug}/plan/paypal-link", new FormUrlEncodedContent(new Dictionary<string, string>()));
        output.WriteLine($"paypal-link: {(int)response.StatusCode} → {response.Headers.Location} · PayPal [{string.Join(" ", calls.Paths)}] · "
                       + $"حِفظ={world.SaveCalls} · الاشتِراكُ بَعدَه={world.Read<TenantPlan>(Slug)!.PayPalSubscriptionId ?? "—"}");
        return (response, world, calls.Paths);
    }

    /// <summary><b>S1 قائِمٌ ويُطلَبُ رابِطٌ جَديد</b>: يُطبَعُ الرَدُّ ونِداءاتُ PayPal والمَرجِعُ بَعدَه.</summary>
    [Fact]
    public async Task Asking_for_a_new_link_while_a_subscription_is_live()
    {
        var (response, _, _) = await PostLinkAsync(LivePlan());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);    // ‏إلى شاشَةِ الباقَةِ لا خَطَأُ خادِم
    }

    /// <summary><b>يَبقى</b>: اشتِراكٌ أُلغِيَ ⇒ الرابِطُ يُنشَأُ عِندَ PayPal ويُحفَظُ ويُحَوَّلُ إلى الشاشَةِ بِـ<c>saved=1</c>.</summary>
    [Fact]
    public async Task A_new_link_after_a_cancel_reaches_paypal_and_is_saved()
    {
        var plan = LivePlan();
        plan.RenewalCancelledAt = At.AddDays(-1);
        var (response, world, paths) = await PostLinkAsync(plan);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("saved=1", response.Headers.Location!.ToString());
        Assert.Contains(PayPalPaymentProvider.SubscriptionsPath, paths);
        Assert.Equal("https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3", world.Read<TenantPlan>(Slug)!.PayPalApproveUrl);
    }
}
