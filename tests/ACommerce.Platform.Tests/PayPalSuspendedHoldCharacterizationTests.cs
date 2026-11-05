using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اشتِراكٌ عَلَّقَته PayPal ثُمَّ أُعيدَ تَفعيلُه — تَوصيفُ «اليَوم» (‏2026-11-05، مُراجَعَةُ G4 الجَولَة ١ · G4R1-2) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): خُطَطُ المَنَصَّةِ تُعَلِّقُ الاشتِراكَ بَعدَ ثَلاثِ دَفَعاتٍ فاشِلَة
// (`PayPalCatalogPolicy.PaymentFailureThreshold`)، ويُعيدُ التاجِرُ تَفعيلَه مِن لَوحَةِ PayPal فَتَخصِمُ `auto_bill_outstanding=true` المُتَأَخِّرَ بِسِعرِ
// الاشتِراكِ نَفسِه. و`BILLING.SUBSCRIPTION.SUSPENDED` يَمُرُّ اليَومَ بِـ`StopsRenewal` كَالإلغاءِ فَيَكتُبُ `RenewalCancelledAt` — وبِه يَنفَكُّ
// `HoldsLiveSubscription`: فَيُقبَلُ تَغييرُ السِعرِ والتاريخَين، ثُمَّ تُمَدِّدُ دَفعَةُ إعادَةِ التَفعيلِ (لِلاشتِراكِ نَفسِه) التَرتيبَ الجَديدَ بِطولِ مُدَّتِه
// والمَخصومُ السِعرُ القَديم — E-1 نَفسُه مِن طَريقِ التَعليقِ لا رابِطٍ ثانٍ.
// **وما يَبقى**: التَعليقُ قَرارُ `StopRenewal` يَكتُبُ «لَن يُجَدَّد» ولا يَمَسُّ المَرجِعَ ولا الحالَةَ ولا السِعر؛ ودَفعَةُ الاشتِراكِ نَفسِه تُمَدِّدُ وتَمحو
// العَلامَةَ فَيَعودُ قائِماً؛ والإلغاءُ — بَعدَ تَعليقٍ أَو بِدونِه — يَفُكُّ الإمساكَ ويُقبَلُ بَعدَه تَغييرُ التَرتيب.

public class PayPalSuspendedHoldCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>رِسالَةُ اشتِراكٍ كَما تُرسِلُها PayPal — تُقرَأُ بِـ<see cref="PayPalBillingPolicy.Parse"/>.</summary>
    internal static PayPalWebhookEvent SubscriptionEvent(string type, string id, string subscriptionId = "I-SUB-1")
        => PayPalBillingPolicy.Parse($$$"""
            {"id":"{{{id}}}","event_type":"{{{type}}}",
             "resource":{"id":"{{{subscriptionId}}}","custom_id":"{{{Slug}}}"}}
            """)!;

    /// <summary>دَفعَةُ الاشتِراكِ الدَورِيَّة — المُعَرِّفُ في <c>billing_agreement_id</c> والمَتجَرُ في <c>custom</c>.</summary>
    internal static PayPalWebhookEvent Sale(string id, string subscriptionId = "I-SUB-1")
        => PayPalBillingPolicy.Parse($$$"""
            {"id":"{{{id}}}","event_type":"{{{PayPalEventTypes.PaymentSaleCompleted}}}",
             "resource":{"billing_agreement_id":"{{{subscriptionId}}}","custom":"{{{Slug}}}","state":"completed"}}
            """)!;

    private string Applied(string label, DocWorld w, PayPalWebhookEvent e)
    {
        var plan = w.Read<TenantPlan>(Slug)!;
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, At);
        PayPalBillingService.Apply(w.Session, plan, e, d, At);
        var line = $"{label}: {d.Action} · يَكتُب={d.Writes} · لَن يُجَدَّد={(plan.RenewalCancelledAt is null ? "لا" : "نَعَم")} · "
                 + $"قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)} · الانتِهاء={plan.ExpiresAt:yyyy-MM-dd} · السِعر={plan.Price}";
        output.WriteLine(line);
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);    // ‏لا فَرعَ يَمَسُّ الحالَة
        return line;
    }

    /// <summary><b>التَعليق</b>: يُطبَعُ القَرارُ والإمساكُ بَعدَه؛ ويَبقى أَنّه <c>StopRenewal</c> يَكتُبُ «لَن يُجَدَّد» ولا يَمَسُّ المَرجِعَ ولا السِعرَ ولا الانتِهاء.</summary>
    [Fact]
    public void A_suspension_of_the_stored_subscription()
    {
        var w = new DocWorld().Put(PayPalArrangementChangeCharacterizationTests.LivePlan());
        var e = SubscriptionEvent(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1");
        Applied("SUSPENDED لِـI-SUB-1", w, e);

        var plan = w.Read<TenantPlan>(Slug)!;
        Assert.Equal(PayPalBillingAction.StopRenewal, PayPalBillingPolicy.Decide(e, PayPalArrangementChangeCharacterizationTests.LivePlan(), false, At).Action);
        Assert.Equal(At, plan.RenewalCancelledAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.Equal(99m, plan.Price);
        Assert.Equal(new DateTime(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc), plan.ExpiresAt);
    }

    /// <summary><b>تَعليقٌ ثُمَّ تَغييرُ التَرتيبِ ثُمَّ دَفعَةُ إعادَةِ التَفعيل</b>: يُطبَعُ قَبولُ الحِفظِ ومُدَّةُ التَمديدِ وسِعرُه؛ ويَبقى أَنّ الدَفعَةَ لِلاشتِراكِ
    /// نَفسِه تُمَدِّدُ وتَمحو «لَن يُجَدَّد» فَيَعودُ قائِماً بِمَرجِعِه.</summary>
    [Fact]
    public async Task Suspend_then_change_the_arrangement_then_the_reactivated_sale()
    {
        var w = new DocWorld().Put(PayPalArrangementChangeCharacterizationTests.LivePlan());
        Applied("SUSPENDED لِـI-SUB-1", w, SubscriptionEvent(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));

        var stored = w.Read<TenantPlan>(Slug)!;
        var before = w.Wrote<TenantPlan>();
        var v = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", stored.StartsAt, stored.ExpiresAt.AddDays(60), 14, 120m, "studio · admin", At);
        output.WriteLine($"بَعدَ التَعليق · السِعرُ 99 ← 120 والانتِهاءُ +60: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before} · "
                       + $"السِعر={w.Read<TenantPlan>(Slug)!.Price} · الانتِهاء={w.Read<TenantPlan>(Slug)!.ExpiresAt:yyyy-MM-dd}");

        var sale = Sale("WH-SALE-1");
        Applied("أُعيدَ تَفعيلُه · PAYMENT.SALE.COMPLETED لِـI-SUB-1", w, sale);

        var after = w.Read<TenantPlan>(Slug)!;
        Assert.Equal("I-SUB-1", after.PayPalSubscriptionId);
        Assert.Null(after.RenewalCancelledAt);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(after));
        Assert.Equal(1, w.Stored.OfType<PayPalWebhookRecord>().Count(r => r.Id == "WH-SALE-1" && r.Action == nameof(PayPalBillingAction.Extend)));
    }

    /// <summary><b>يَبقى</b>: الإلغاءُ — بَعدَ تَعليقٍ أَو بِدونِه — يَفُكُّ الإمساكَ، ويُقبَلُ بَعدَه تَغييرُ السِعرِ والانتِهاء.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancel_releases_the_hold_with_or_without_a_suspension_before_it(bool suspendedFirst)
    {
        var w = new DocWorld().Put(PayPalArrangementChangeCharacterizationTests.LivePlan());
        if (suspendedFirst) Applied("SUSPENDED لِـI-SUB-1", w, SubscriptionEvent(PayPalEventTypes.SubscriptionSuspended, "WH-SUS-1"));
        Applied("CANCELLED لِـI-SUB-1", w, SubscriptionEvent(PayPalEventTypes.SubscriptionCancelled, "WH-CAN-1"));

        var stored = w.Read<TenantPlan>(Slug)!;
        Assert.False(TenantPlanPolicy.HoldsLiveSubscription(stored));
        Assert.Empty(await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", stored.StartsAt, stored.ExpiresAt.AddDays(60), 14, 120m, "studio · admin", At));
        Assert.Equal(120m, w.Read<TenantPlan>(Slug)!.Price);
    }
}
