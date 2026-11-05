using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اشتِراكُ PayPal القائِمُ يُمسِكُ التَرتيب — التَبديل (‏2026-11-05، الموجَة ١٢ G4 · E-1؛ تَوصيفُه `c7b49a7` بِبَصمَتِه) ═══
//
// **الأَبوابُ الأَربَعَة** وكُلُّها تَسأَلُ `TenantPlanPolicy.HoldsLiveSubscription` (مُعَرِّفٌ مَحفوظٌ لا تَكتُبُه إلّا رِسالَةٌ مُوَثَّقَة، ولا إلغاءَ بَعدَه):
//   (١) الحِفظُ يَرُدُّ تَغييرَ الباقَةِ والسِعرِ والتاريخَينِ بِـ`tenant_plan_paypal_subscription_live` — المُوجَبُ لِكُلِّ حَقلٍ والسالِبُ لِما سِواه؛
//   (٢) `Decide` يَرُدُّ رِسالَةَ اشتِراكٍ آخَرَ بِـ`ForeignSubscription` بِلا كِتابَة؛
//   (٣) `SaveApproveLink` لا يَكتُبُ مُعَرِّفَ الاشتِراك؛
//   (٤) نُقطَةُ الرابِطِ تَرُدُّ قَبلَ نِداءِ PayPal، والشاشَةُ تَقولُ الشَرطَ نَفسَه ولا تَرسُمُ الزِرّ.

public class PayPalArrangementChangeSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private static TenantPlan Live() => PayPalArrangementChangeCharacterizationTests.LivePlan();

    private static Task<IReadOnlyList<TenantPlanViolation>> Save(DocWorld w, TenantPlan next)
        => TenantPlanAdminService.SetAsync(w.Session, Slug, next.PlanId, next.StartsAt, next.ExpiresAt, next.GraceDays, next.Price, "studio · admin", At);

    // ─── (١) الحِفظ ────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> ArrangementChanges => new() { "plan", "price", "starts", "expires" };

    private static TenantPlan Changed(string field)
    {
        var p = Live();
        switch (field)
        {
            case "plan":    p.PlanId = "plan-no-longer-listed"; break;
            case "price":   p.Price = 120m; break;
            case "starts":  p.StartsAt = p.StartsAt.AddDays(-1); break;
            case "expires": p.ExpiresAt = p.ExpiresAt.AddDays(30); break;
        }
        return p;
    }

    /// <summary><b>المُوجَب</b>: اشتِراكٌ قائِمٌ وحَقلٌ مِن الأَربَعَةِ يَتَغَيَّر ⇐ رَمزُ الخَرقِ أَوَّلاً، ولا وَثيقَةَ تُخَزَّن، والمَحفوظُ كَما هو.
    /// ‏(الباقَةُ خارِجَ الكاتالوجِ تُعطي خَرقَها مَعَه — والإمساكُ قَبلَها.)</summary>
    [Theory]
    [MemberData(nameof(ArrangementChanges))]
    public async Task A_live_subscription_refuses_a_change_of_plan_price_or_dates(string field)
    {
        var stored = Live();
        var w = new DocWorld().Put(stored);
        var v = await Save(w, Changed(field));
        output.WriteLine($"{field}: [{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>()}");

        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, v[0].Code);
        Assert.Contains("I-SUB-1", v[0].MessageAr, StringComparison.Ordinal);
        Assert.Equal(0, w.Wrote<TenantPlan>());
        Assert.Same(stored, w.Read<TenantPlan>(Slug));
        Assert.Equal(99m, stored.Price);
    }

    /// <summary><b>السالِب</b>: أَيّامُ السَماحِ وَحدَها، أَو اشتِراكٌ أُلغِيَ، أَو لا اشتِراك، أَو لا باقَةَ مَحفوظَة ⇐ لا خَرق.</summary>
    [Fact]
    public void Only_a_live_subscription_and_an_arrangement_change_make_the_violation()
    {
        var grace = Live(); grace.GraceDays = 7;
        Assert.Empty(TenantPlanPolicy.ValidateChange(Live(), grace));

        var cancelled = Live(); cancelled.RenewalCancelledAt = At;
        Assert.Empty(TenantPlanPolicy.ValidateChange(cancelled, Changed("price")));

        var none = Live(); none.PayPalSubscriptionId = null;
        Assert.Empty(TenantPlanPolicy.ValidateChange(none, Changed("price")));

        var blank = Live(); blank.PayPalSubscriptionId = "  ";
        Assert.Empty(TenantPlanPolicy.ValidateChange(blank, Changed("price")));

        Assert.Empty(TenantPlanPolicy.ValidateChange(null, Changed("price")));

        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(Live()));
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(cancelled));
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(none));
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(null));
    }

    // ─── (٢) رِسالَةُ اشتِراكٍ آخَر ────────────────────────────────────────────────────────────────────────────

    private static PayPalWebhookEvent Event(string type, string? sub) => new("WH-9", type, Slug, sub, null);

    /// <summary><b>المُوجَب</b>: تَفعيلٌ ودَفعَةٌ وإلغاءٌ لِـI-SUB-2 والقائِمُ I-SUB-1 ⇐ <c>ForeignSubscription</c> بِلا كِتابَة، والسَطرُ يُسَمّي الاثنَين،
    /// والجَلسَةُ لا تُلمَس (تُمَرَّرُ <c>null</c>).</summary>
    [Theory]
    [InlineData(PayPalEventTypes.SubscriptionActivated)]
    [InlineData(PayPalEventTypes.PaymentSaleCompleted)]
    [InlineData(PayPalEventTypes.SubscriptionCancelled)]
    [InlineData(PayPalEventTypes.SubscriptionSuspended)]
    public void An_event_from_another_subscription_while_one_is_live_writes_nothing(string type)
    {
        var plan = Live();
        var e = Event(type, "I-SUB-2");
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, At);
        output.WriteLine($"{type}: {d.Action} · {d.ReasonAr}");

        Assert.Equal(PayPalBillingAction.ForeignSubscription, d.Action);
        Assert.False(d.Writes);
        Assert.Contains("I-SUB-2", d.ReasonAr, StringComparison.Ordinal);
        Assert.Contains("I-SUB-1", d.ReasonAr, StringComparison.Ordinal);
        Assert.False(PayPalBillingService.Apply(session: null!, plan, e, d, At));
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.Null(plan.RenewalCancelledAt);
    }

    /// <summary><b>السالِب</b>: الاشتِراكُ نَفسُه، أَو مَحفوظٌ أُلغِيَ، أَو رِسالَةٌ بِلا مُعَرِّف، أَو لا مُعَرِّفَ مَحفوظ ⇐ يُمَدِّدُ كَما كان؛ والإعادَةُ إعادَة.</summary>
    [Fact]
    public void Events_that_are_not_foreign_decide_as_before()
    {
        Assert.Equal(PayPalBillingAction.Extend,
            PayPalBillingPolicy.Decide(Event(PayPalEventTypes.PaymentSaleCompleted, "I-SUB-1"), Live(), false, At).Action);

        var cancelled = Live(); cancelled.RenewalCancelledAt = At.AddDays(-1);
        Assert.Equal(PayPalBillingAction.Extend,
            PayPalBillingPolicy.Decide(Event(PayPalEventTypes.SubscriptionActivated, "I-SUB-2"), cancelled, false, At).Action);

        Assert.Equal(PayPalBillingAction.Extend,
            PayPalBillingPolicy.Decide(Event(PayPalEventTypes.PaymentSaleCompleted, null), Live(), false, At).Action);

        var first = Live(); first.PayPalSubscriptionId = null;
        Assert.Equal(PayPalBillingAction.Extend,
            PayPalBillingPolicy.Decide(Event(PayPalEventTypes.SubscriptionActivated, "I-SUB-2"), first, false, At).Action);

        Assert.Equal(PayPalBillingAction.Replay,
            PayPalBillingPolicy.Decide(Event(PayPalEventTypes.SubscriptionActivated, "I-SUB-2"), Live(), true, At).Action);
    }

    // ─── (٣) الرابِطُ لا يَكتُبُ المَرجِع ────────────────────────────────────────────────────────────────────

    /// <summary>رابِطٌ يُحفَظ — والمُعَرِّفُ كَما كانَ: فارِغٌ لِباقَةٍ بِلا اشتِراك، و«I-SUB-1» لِلقائِم. والرابِطُ نَفسُه يُحفَظ.</summary>
    [Fact]
    public void Saving_an_approve_link_never_writes_the_subscription_reference()
    {
        var result = new SubscriptionResult("I-SUB-3", false, default, null, "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-3");

        var fresh = new TenantPlan { Id = Slug, PlanId = "manual" };
        Assert.True(PayPalBillingService.SaveApproveLink(new DocWorld().Session, fresh, result, "studio · admin", At));
        Assert.Null(fresh.PayPalSubscriptionId);
        Assert.Equal(result.ApproveUrl, fresh.PayPalApproveUrl);
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(fresh));

        var live = Live();
        Assert.True(PayPalBillingService.SaveApproveLink(new DocWorld().Session, live, result, "studio · admin", At));
        Assert.Equal("I-SUB-1", live.PayPalSubscriptionId);
    }

    // ─── (٤) نُقطَةُ الرابِطِ والشاشَة ───────────────────────────────────────────────────────────────────────

    /// <summary><b>اشتِراكٌ قائِمٌ ⇐ النُقطَةُ تُحَوِّلُ بِرَمزِ الخَرقِ قَبلَ أَيِّ نِداءٍ لِـPayPal</b>، ولا كِتابَةَ ولا إيداع.</summary>
    [Fact]
    public async Task The_link_endpoint_refuses_a_second_link_before_calling_paypal()
    {
        var admin = Guid.NewGuid();
        var world = new DocWorld().Put(Live())
            .Put(new PlatformPlanPayPal { Id = "manual", ProductId = "PROD-1", PlanId = "P-10" })
            .Put(PayPalEndpointBehaviourTests.Admin(admin));
        var calls = new PayPalCalls();
        await using var host = await PayPalHost.StartAsync(world, calls);
        PayPalEndpointBehaviourTests.SignIn(host.Client, admin);

        var response = await host.Client.PostAsync($"/admin/tenants/{Slug}/plan/paypal-link", new FormUrlEncodedContent(new Dictionary<string, string>()));
        output.WriteLine($"paypal-link: {(int)response.StatusCode} → {response.Headers.Location} · PayPal [{string.Join(" ", calls.Paths)}]");

        Assert.Empty(calls.Paths);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<TenantPlan>());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"err={TenantPlanPolicy.PayPalSubscriptionLive}", response.Headers.Location!.ToString());
    }

    /// <summary><b>ورِسالَةُ اشتِراكٍ آخَرَ عَبرَ الشَبَكَة</b> ⇐ ‏200 بِـ<c>ForeignSubscription</c> و<c>applied=false</c>، ولا إيداع، والمَحفوظُ كَما هو.</summary>
    [Fact]
    public async Task A_signed_event_from_another_subscription_is_answered_without_a_write()
    {
        var world = new DocWorld().Put(Live());
        var calls = new PayPalCalls().ThenToken().ThenVerify(true);
        await using var host = await PayPalHost.StartAsync(world, calls);

        var body = $$$"""
            {"id":"WH-FOREIGN-1","event_type":"{{{PayPalEventTypes.PaymentSaleCompleted}}}",
             "resource":{"billing_agreement_id":"I-SUB-2","custom":"{{{Slug}}}"}}
            """;
        var response = await host.Client.SendAsync(PayPalEndpointBehaviourTests.Signed(body));
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"webhook: {(int)response.StatusCode} {text}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(nameof(PayPalBillingAction.ForeignSubscription), text, StringComparison.Ordinal);
        Assert.Equal(0, world.SaveCalls);
        Assert.Equal(0, world.Wrote<PayPalWebhookRecord>());
        Assert.Equal("I-SUB-1", world.Read<TenantPlan>(Slug)!.PayPalSubscriptionId);
    }

    /// <summary><b>الشاشَةُ تَقولُ ما يُجيبُه البابُ</b> (القاعِدَة ١٢): السَطرُ ومُعَرِّفُ الاشتِراكِ فَوقَ نَموذَجِ التَعيينِ بِالشَرطِ نَفسِه، وزِرُّ الرابِطِ
    /// لا يُرسَمُ تَحتَه، ورَمزُ الخَرقِ يُترجَمُ مِن القامُوسِ لا يُعرَضُ خاماً — والمِفتاحانِ في <c>ar.json</c>.</summary>
    [Fact]
    public void The_admin_page_says_the_condition_the_door_answers()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var page = File.ReadAllText(Path.Combine(root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Admin", "TenantPlanAdmin.razor"));
        var ar = File.ReadAllText(Path.Combine(root, "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));

        var notice = page.IndexOf("L.Markup(\"admin.tenant_plan.paypal_subscription_live\")", StringComparison.Ordinal);
        var form = page.IndexOf("/plan/save", StringComparison.Ordinal);
        var guard = page.LastIndexOf("@if (TenantPlanPolicy.HoldsLiveSubscription(plan))", notice, StringComparison.Ordinal);
        Assert.True(notice > form && guard > form && notice - guard < 200, "سَطرُ الاشتِراكِ القائِمِ لَيسَ تَحتَ شَرطِه داخِلَ نَموذَجِ التَعيين.");
        Assert.Contains("@plan!.PayPalSubscriptionId", page[notice..(notice + 200)], StringComparison.Ordinal);

        var linkForm = page.IndexOf("/plan/paypal-link", StringComparison.Ordinal);
        var linkGuard = page.LastIndexOf("@if (!TenantPlanPolicy.HoldsLiveSubscription(plan))", linkForm, StringComparison.Ordinal);
        Assert.True(linkGuard > 0 && linkForm - linkGuard < 200, "زِرُّ الرابِطِ يُرسَمُ والاشتِراكُ قائِم.");

        Assert.Contains("TenantPlanPolicy.PayPalSubscriptionLive => L[\"admin.tenant_plan.err_paypal_subscription_live\"]", page, StringComparison.Ordinal);
        Assert.Contains("\"admin.tenant_plan.paypal_subscription_live\":", ar, StringComparison.Ordinal);
        Assert.Contains("\"admin.tenant_plan.err_paypal_subscription_live\":", ar, StringComparison.Ordinal);
    }
}
