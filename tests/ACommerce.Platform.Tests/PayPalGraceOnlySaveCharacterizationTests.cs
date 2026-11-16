using ACommerce.Kit.Culture;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَيّامُ السَماحِ وَحدَها عَلى انتِهاءٍ كَتَبَه PayPal — تَوصيفُ «اليَوم» عَبرَ الاستِمارَةِ كَما تُرسَم (‏2026-11-05، الموجَة ١٢ G4R1-1) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): المُشرِفُ يَضبُطُ الباقَةَ مِن الاستِمارَة (`ReadSetting` ثُمَّ `SetAsync`)،
// فَتَصِلُ `BILLING.SUBSCRIPTION.ACTIVATED` لِـI-SUB-1 بِـ`next_billing_time` ساعَتُه 10:00Z (`Parse` ثُمَّ `Decide` ثُمَّ `Apply`) — وهي الرِسالَةُ الوَحيدَةُ
// الَّتي تَجعَلُ الاشتِراكَ «قائِماً». ثُمَّ تَرسُمُ صَفحَةُ المُشرِفِ التاريخَينِ `yyyy-MM-dd` (`TenantPlanAdmin.razor`) ويُعيدُهُما `ReadSetting` آخِرَ ذلكَ اليَومِ
// بِتَوقيتِ المَنَصَّة، فَلا يُساوي المُرسَلُ المَحفوظَ أَبَداً: **حِفظُ أَيّامِ السَماحِ وَحدَها يُرَدُّ** بِـ`tenant_plan_paypal_subscription_live`
// (`ValidateChange` يُقارِنُ اللَحظَتَين) — والصَفحَةُ وADR-039 يَقولانِ «وأَيّامُ السَماحِ تُحفَظ». والتَجديدُ يُبقي الساعَة، فَالرَدُّ يَدومُ ما دامَ الاشتِراك.
// **وما يَبقى**: التَفعيلُ يُمَدِّدُ إلى مَوعِدِ PayPal بِساعَتِه ويَكتُبُ المَرجِع؛ والاستِمارَةُ تَحمِلُ يَوماً لا ساعَة؛ وحِفظُ أَيّامِ السَماحِ لا يُحَرِّكُ
// لَحظَةَ PayPal ولا يَمَسُّ المَرجِع؛ ويَومٌ غَيرُ المَحفوظِ يُرَدُّ بِالرَمزِ نَفسِه ولا يُخَزَّنُ شَيء.

public class PayPalGraceOnlySaveCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "plan-demo";
    private static readonly DateTime SetAt       = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ActivatedAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SaveAt      = new(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>مَوعِدُ PayPal كَما تَكتُبُه رِسالَةُ التَفعيل — بِساعَتِه.</summary>
    internal static readonly DateTime PayPalNext = new(2026, 12, 5, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>رِسالَةُ التَفعيلِ كَما تُرسِلُها PayPal — تُقرَأُ بِـ<see cref="PayPalBillingPolicy.Parse"/> لا تُبنى سِجِلّاً.</summary>
    internal static string ActivatedBody(string subscriptionId = "I-SUB-1") => $$$"""
        {"id":"WH-ACT-{{{subscriptionId}}}","event_type":"{{{PayPalEventTypes.SubscriptionActivated}}}",
         "resource":{"id":"{{{subscriptionId}}}","custom_id":"{{{Slug}}}","status":"ACTIVE",
                     "billing_info":{"next_billing_time":"2026-12-05T10:00:00Z"}}
        }
        """;

    /// <summary>المُشرِفُ يَضبُطُ الباقَةَ مِن الاستِمارَة (‏2026-10-31 ← 2026-11-30، سَماحٌ 14، سِعرٌ 99)، ثُمَّ يُفَعِّلُ PayPal الاشتِراك.</summary>
    internal static async Task<DocWorld> ActivatedWorldAsync()
    {
        var w = new DocWorld();
        var f = TenantPlanPolicy.ReadSetting("manual", "2026-10-31", "2026-11-30", "14", "99", SetAt);
        Assert.Empty(await TenantPlanAdminService.SetAsync(w.Session, Slug, f.PlanId, f.StartsAt, f.ExpiresAt, f.GraceDays, f.Price, "studio · admin", SetAt));

        var plan = w.Read<TenantPlan>(Slug)!;
        var e = PayPalBillingPolicy.Parse(ActivatedBody())!;
        var d = PayPalBillingPolicy.Decide(e, plan, alreadySeen: false, ActivatedAt);
        Assert.Equal(PayPalBillingAction.Extend, d.Action);
        Assert.True(PayPalBillingService.Apply(w.Session, plan, e, d, ActivatedAt));
        return w;
    }

    /// <summary>حُقولُ الاستِمارَةِ كَما تَرسُمُها <c>TenantPlanAdmin.razor</c> لِلوَثيقَةِ المَحفوظَة — بِالتَعبيرِ نَفسِه (<c>ToString("yyyy-MM-dd")</c>)،
    /// وأَيّامُ السَماحِ ما كَتَبَه المُشرِف، ثُمَّ <see cref="TenantPlanPolicy.ReadSetting"/> ثُمَّ <see cref="TenantPlanAdminService.SetAsync"/> كَما تُناديهِما
    /// <c>/admin/tenants/{slug}/plan/save</c>.</summary>
    internal static async Task<(IReadOnlyList<TenantPlanViolation> Violations, DateTime PostedExpires)> SaveAsRenderedAsync(
        DocWorld w, string grace, string? expiresDay = null)
    {
        var p = w.Read<TenantPlan>(Slug)!;
        var f = TenantPlanPolicy.ReadSetting(p.PlanId, p.StartsAt.ToString("yyyy-MM-dd"), expiresDay ?? p.ExpiresAt.ToString("yyyy-MM-dd"),
            grace, p.Price.ToString(System.Globalization.CultureInfo.InvariantCulture), SaveAt);
        var v = await TenantPlanAdminService.SetAsync(w.Session, Slug, f.PlanId, f.StartsAt, f.ExpiresAt, f.GraceDays, f.Price, "studio · admin", SaveAt);
        return (v, f.ExpiresAt);
    }

    /// <summary><b>يَبقى</b>: التَفعيلُ يُمَدِّدُ إلى مَوعِدِ PayPal بِساعَتِه ويَكتُبُ المَرجِعَ فَيَصيرُ قائِماً، والاستِمارَةُ تَرسُمُ يَومَه وتُعيدُه آخِرَ اليَوم.</summary>
    [Fact]
    public async Task Activation_writes_paypals_clock_time_and_the_form_carries_only_its_day()
    {
        var w = await ActivatedWorldAsync();
        var plan = w.Read<TenantPlan>(Slug)!;
        var day = plan.ExpiresAt.ToString("yyyy-MM-dd");
        var posted = TenantPlanPolicy.ReadSetting(plan.PlanId, plan.StartsAt.ToString("yyyy-MM-dd"), day, "7", "99", SaveAt).ExpiresAt;
        output.WriteLine($"بَعدَ ACTIVATED: الانتِهاء={plan.ExpiresAt:O} · قائِم={TenantPlanPolicy.HoldsLiveSubscription(plan)} · "
                       + $"الاستِمارَة: {day} ← {posted:O}");

        Assert.Equal(PayPalNext, plan.ExpiresAt);
        Assert.Equal("I-SUB-1", plan.PayPalSubscriptionId);
        Assert.True(TenantPlanPolicy.HoldsLiveSubscription(plan));
        Assert.Equal("2026-12-05", day);
        Assert.Equal(CultureTime.EndOfDayUtc(new DateOnly(2026, 12, 5)), posted);
        Assert.NotEqual(plan.ExpiresAt, posted);
    }

    /// <summary><b>اشتِراكٌ قائِمٌ وأَيّامُ السَماحِ وَحدَها مِن الاستِمارَةِ كَما تُرسَم</b>: يُطبَعُ الرَدُّ والمُخَزَّنُ والسَماح؛ ويَبقى أَنّ لَحظَةَ PayPal
    /// لا تَتَحَرَّكُ والمَرجِعُ كَما هو.</summary>
    [Fact]
    public async Task Saving_only_the_grace_days_through_the_rendered_form_over_paypals_expiry()
    {
        var w = await ActivatedWorldAsync();
        var before = w.Wrote<TenantPlan>();
        var (v, posted) = await SaveAsRenderedAsync(w, grace: "7");
        var after = w.Read<TenantPlan>(Slug)!;
        output.WriteLine($"السَماحُ 14 ← 7 عَبرَ الاستِمارَة: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before} · "
                       + $"السَماح={after.GraceDays} · المُرسَل={posted:O} · الانتِهاءُ بَعدَه={after.ExpiresAt:O}");

        Assert.Equal(PayPalNext, after.ExpiresAt);
        Assert.Equal("I-SUB-1", after.PayPalSubscriptionId);
        Assert.Null(after.RenewalCancelledAt);
    }

    /// <summary><b>يَبقى</b>: يَومُ انتِهاءٍ غَيرُ المَحفوظِ مِن الاستِمارَةِ والاشتِراكُ قائِم ⇐ <c>tenant_plan_paypal_subscription_live</c> ولا وَثيقَةَ تُخَزَّن.</summary>
    [Fact]
    public async Task Another_expiry_day_through_the_rendered_form_is_refused_while_the_subscription_is_live()
    {
        var w = await ActivatedWorldAsync();
        var before = w.Wrote<TenantPlan>();
        var (v, _) = await SaveAsRenderedAsync(w, grace: "14", expiresDay: "2027-01-05");
        output.WriteLine($"يَومٌ آخَرُ 2026-12-05 ← 2027-01-05: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · مُخَزَّن={w.Wrote<TenantPlan>() - before}");

        Assert.Equal(TenantPlanPolicy.PayPalSubscriptionLive, v[0].Code);
        Assert.Equal(0, w.Wrote<TenantPlan>() - before);
        Assert.Equal(PayPalNext, w.Read<TenantPlan>(Slug)!.ExpiresAt);
        Assert.Equal(14, w.Read<TenantPlan>(Slug)!.GraceDays);
    }

    /// <summary><b>يَبقى</b>: الصَفحَةُ تَرسُمُ حَقلَي التاريخِ بِالتَعبيرِ الَّذي يَستَعمِلُه هذا المِلَفّ — يَوماً بِلا ساعَة — فَإن تَغَيَّرَ الرَسمُ احمَرَّ هُنا.</summary>
    [Fact]
    public void The_admin_page_renders_the_two_dates_as_days()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "Admin", "TenantPlanAdmin.razor"));

        Assert.Contains("value=\"@((plan?.StartsAt ?? DateTime.UtcNow).ToString(\"yyyy-MM-dd\"))\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"@(plan?.ExpiresAt.ToString(\"yyyy-MM-dd\") ?? \"\")\"", page, StringComparison.Ordinal);
    }
}
