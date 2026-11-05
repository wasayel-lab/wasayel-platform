using System.Net;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الرابِطُ يَحمِلُ خُطَّتَه، وإعادَةُ رَبطِ الخُطَّةِ تُسقِطُ روابِطَ الخُطَّةِ القَديمَة — التَبديل (‏2026-11-05، الموجَة ١٢ G4 · E-6؛ تَوصيفُه `98c312f`) ═══
//
//   (١) `SaveApproveLink` يُسَجِّلُ `PayPalApprovePlanId`، ونُقطَةُ الرابِطِ تُمَرِّرُ ما حَلَّته؛
//   (٢) `KeepsPayLink` يَحمِلُ الرابِطَ ما دامَ ما سَجَّلَه هُوَ ما يُحَلُّ اليَوم — والحِفظُ يَسأَلُه؛
//   (٣) `/plan/paypal-plan` يُسقِطُ روابِطَ الخُطَّةِ الَّتي حَلَّ مَحَلَّها لِكُلِّ مَتجَرٍ عَلى الباقَة، وسَطرُ تَدقيقٍ لِكُلٍّ مِنها.
// والاستِعلامُ نَفسُه عَلى Postgres في `LivePayPalStaleLinkProofTests`.

public class PayPalPlanRebindSwitchTests(ITestOutputHelper output)
{
    private const string Slug = "store-a";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    private static TenantPlan Holder(string id, string planId, string? link, string? madeFrom) => new()
    {
        Id = id, PlanId = planId, StartsAt = At.AddDays(-5), ExpiresAt = At.AddDays(25),
        PayPalApproveUrl = link, PayPalApprovePlanId = madeFrom,
    };

    // ─── (٢) KeepsPayLink — المُوجَبُ والسالِب ─────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَب</b>: الباقَةُ نَفسُها والخُطَّةُ الَّتي سَجَّلَها الرابِطُ هِيَ ما يُحَلّ ⇐ يُحمَل؛ ورابِطٌ قَبلَ الحَقلِ ولا خُطَّةَ تُحَلّ ⇐ يُحمَلُ كَما كان.</summary>
    [Theory]
    [InlineData("P-25", "P-25")]
    [InlineData(null, null)]
    public void The_same_plan_and_the_same_paypal_plan_keep_the_link(string? madeFrom, string? resolved)
        => Assert.True(TenantPlanAdminService.KeepsPayLink(Holder(Slug, "manual", "https://x", madeFrom), "manual", resolved));

    /// <summary><b>السالِب</b>: خُطَّةٌ أُعيدَ إنشاؤُها، أَو رابِطٌ قَبلَ الحَقلِ والخُطَّةُ تُحَلُّ اليَوم، أَو لا خُطَّةَ تُحَلُّ لِرابِطٍ سَجَّلَ واحِدَة ⇐ يُسقَط.</summary>
    [Theory]
    [InlineData("P-10", "P-25")]
    [InlineData(null, "P-25")]
    [InlineData("P-25", null)]
    [InlineData("p-25", "P-25")]
    public void Another_paypal_plan_drops_the_link(string? madeFrom, string? resolved)
        => Assert.False(TenantPlanAdminService.KeepsPayLink(Holder(Slug, "manual", "https://x", madeFrom), "manual", resolved));

    /// <summary>الحِفظُ يَسأَلُه: الرِباطُ اليَومَ P-25 ⇐ رابِطُ P-10 ورابِطُ ما قَبلَ الحَقلِ يُسقَطان، ورابِطُ P-25 يُحمَلُ بِخُطَّتِه.</summary>
    [Fact]
    public async Task Re_saving_after_a_rebind_drops_links_from_the_old_paypal_plan_and_keeps_the_current_one()
    {
        foreach (var (madeFrom, kept) in new (string?, bool)[] { ("P-10", false), (null, false), ("P-25", true) })
        {
            var stored = PayPalPlanRebindCharacterizationTests.StoreA();
            stored.PayPalApprovePlanId = madeFrom;
            var w = new DocWorld().Put(stored).Put(PayPalPlanRebindCharacterizationTests.Rebound());

            Assert.Empty(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", stored.StartsAt, stored.ExpiresAt, 14, 10m, "studio · admin", At));
            var plan = w.Read<TenantPlan>(Slug)!;
            output.WriteLine($"رابِطٌ مِن {madeFrom ?? "—"} والخُطَّةُ P-25 ⇐ الرابِطُ بَعدَه={plan.PayPalApproveUrl ?? "—"} · خُطَّتُه={plan.PayPalApprovePlanId ?? "—"}");

            Assert.Equal(kept ? PayPalPlanRebindCharacterizationTests.OldLink : null, plan.PayPalApproveUrl);
            Assert.Equal(kept ? "P-25" : null, plan.PayPalApprovePlanId);
            Assert.Equal("I-OLD", plan.PayPalSubscriptionId);
        }
    }

    // ─── (١) الرابِطُ يُسَجِّلُ خُطَّتَه ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Saving_a_link_records_the_paypal_plan_it_was_made_from()
    {
        var plan = new TenantPlan { Id = Slug, PlanId = "manual" };
        var result = new SubscriptionResult("I-NEW", false, default, null, "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-25");
        Assert.True(PayPalBillingService.SaveApproveLink(new DocWorld().Session, plan, result, "studio · admin", At, " P-25 "));
        Assert.Equal("P-25", plan.PayPalApprovePlanId);

        var none = new TenantPlan { Id = Slug, PlanId = "manual" };
        Assert.True(PayPalBillingService.SaveApproveLink(new DocWorld().Session, none, result, "studio · admin", At));
        Assert.Null(none.PayPalApprovePlanId);
    }

    /// <summary>نُقطَةُ الرابِطِ تُمَرِّرُ ما حَلَّته — الرِباطُ P-25 ⇐ الرابِطُ المَحفوظُ خُطَّتُه P-25.</summary>
    [Fact]
    public async Task The_link_door_records_the_resolved_paypal_plan()
    {
        var admin = Guid.NewGuid();
        var world = new DocWorld().Put(new TenantPlan { Id = Slug, PlanId = "manual", StartsAt = At, ExpiresAt = At.AddDays(30) })
            .Put(PayPalPlanRebindCharacterizationTests.Rebound()).Put(PayPalEndpointBehaviourTests.Admin(admin));
        var calls = new PayPalCalls().ThenToken().Then(HttpStatusCode.Created,
            """{"id":"I-NEW","status":"APPROVAL_PENDING","links":[{"rel":"approve","href":"https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-25"}]}""");
        await using var host = await PayPalHost.StartAsync(world, calls);
        PayPalEndpointBehaviourTests.SignIn(host.Client, admin);

        var response = await host.Client.PostAsync($"/admin/tenants/{Slug}/plan/paypal-link", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var plan = world.Read<TenantPlan>(Slug)!;
        output.WriteLine($"paypal-link: {(int)response.StatusCode} → {response.Headers.Location} · خُطَّةُ الرابِط={plan.PayPalApprovePlanId ?? "—"}");

        Assert.Contains("saved=1", response.Headers.Location!.ToString());
        Assert.Equal("P-25", plan.PayPalApprovePlanId);
        Assert.Null(plan.PayPalSubscriptionId);
    }

    // ─── (٣) الإسقاطُ عِندَ إعادَةِ الرَبط ──────────────────────────────────────────────────────────────────

    /// <summary><b>المُوجَبُ والسالِبُ في عالَمٍ واحِد</b>: A (رابِطُ P-10) وE (رابِطٌ قَبلَ الحَقل) يُسقَطان؛ B (رابِطُ P-25) وC (باقَةٌ أُخرى) وD (بِلا رابِط)
    /// لا تُمَسّ. ولا يُخَزَّنُ إلّا ما أُسقِط، والاشتِراكُ والتَواريخُ كَما هي.</summary>
    [Fact]
    public void Rebinding_drops_only_the_links_made_from_another_paypal_plan_of_that_platform_plan()
    {
        var holders = new[]
        {
            Holder("a", "manual", "https://pp/BA-10", "P-10"),
            Holder("b", "manual", "https://pp/BA-25", "P-25"),
            Holder("c", "other-plan", "https://pp/BA-77", "P-77"),
            Holder("d", "manual", null, null),
            Holder("e", "manual", "https://pp/BA-OLD", null),
        };
        holders[0].PayPalSubscriptionId = "I-A";
        var w = new DocWorld();
        foreach (var h in holders) w.Put(h);

        var dropped = PayPalBillingService.DropStaleLinks(w.Session, holders, "manual", "P-25");
        output.WriteLine($"أُسقِطَ: [{string.Join(", ", dropped)}] · مُخَزَّن={w.Wrote<TenantPlan>()}");

        Assert.Equal(new[] { "a", "e" }, dropped);
        Assert.Equal(2, w.Wrote<TenantPlan>());
        Assert.Null(holders[0].PayPalApproveUrl);
        Assert.Null(holders[0].PayPalApprovePlanId);
        Assert.Equal("I-A", holders[0].PayPalSubscriptionId);
        Assert.Null(holders[4].PayPalApproveUrl);
        Assert.Equal("https://pp/BA-25", holders[1].PayPalApproveUrl);
        Assert.Equal("https://pp/BA-77", holders[2].PayPalApproveUrl);

        Assert.True(PayPalBillingService.HoldsStaleLink(Holder("x", "manual", "https://pp", "P-10"), "manual", "P-25"));
        Assert.False(PayPalBillingService.HoldsStaleLink(Holder("x", "manual", "https://pp", "P-25"), "manual", " P-25 "));
        Assert.False(PayPalBillingService.HoldsStaleLink(Holder("x", "manual", "  ", "P-10"), "manual", "P-25"));
    }

    /// <summary>سَطرُ الخُطَّةِ كَما كانَ، وسَطرٌ لِكُلِّ مَتجَرٍ أُسقِطَ رابِطُه بِسلاجِه نِطاقاً.</summary>
    [Fact]
    public async Task The_catalog_audit_writes_one_row_per_dropped_link()
    {
        var w = new DocWorld();
        await PayPalSurface.AuditCatalogPlanAsync(new AuditWriter(w.Store), "store-b", Guid.NewGuid(), "studio · admin", "manual", "P-25", new[] { "a", "e" });
        var rows = w.Stored.OfType<AuditEntry>().ToList();
        output.WriteLine(string.Join(" · ", rows.Select(r => $"{r.Scope}:{r.Action}:{r.Note}")));

        Assert.Equal(3, rows.Count);
        Assert.Equal(PayPalBillingService.CatalogPlanAuditAction, rows[0].Action);
        Assert.Equal(new[] { "a", "e" }, rows.Skip(1).Select(r => r.Scope));
        Assert.All(rows.Skip(1), r => Assert.Equal(PayPalBillingService.LinkDroppedAuditAction, r.Action));
        Assert.All(rows, r => Assert.Equal("P-25", r.Note));
    }

    /// <summary>بابُ إنشاءِ الخُطَّة: الحارِسُ ثُمَّ الرَبطُ ثُمَّ الإسقاطُ في الجَلسَةِ نَفسِها ثُمَّ الإيداعُ ثُمَّ سُطورُ التَدقيق.</summary>
    [Fact]
    public void The_create_plan_door_drops_stale_links_in_the_same_session_before_it_commits()
    {
        var endpoint = Assert.Single(WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route == PayPalCatalogPlanTests.CreatePlanRoute).ToArray());
        int Pos(string token) => endpoint.Body.IndexOf(token, StringComparison.Ordinal);

        var order = new[] { "PlatformAdminGuard.EvaluateAsync", "BindCatalogPlan", "DropStaleLinksAsync(session, draft.PlanSlug, created.PlanId", "SaveChangesAsync", "AuditCatalogPlanAsync(" }
            .Select(t => (Token: t, Index: Pos(t))).ToList();
        output.WriteLine(string.Join(" ← ", order.Select(o => $"{o.Token}@{o.Index}")));
        Assert.All(order, o => Assert.True(o.Index >= 0, $"أَداةٌ عَمياء: «{o.Token}» غائِب."));
        Assert.Equal(order.Select(o => o.Index).OrderBy(i => i), order.Select(o => o.Index));
    }
}
