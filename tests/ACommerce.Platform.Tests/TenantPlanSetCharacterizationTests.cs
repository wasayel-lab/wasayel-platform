using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَعيينُ باقَةِ المَتجَرِ فَوقَ باقَةٍ مَحفوظَة — ما يَبقى وما يَضيع (تَدقيقُ الموجَة ١١ F-SDC-1) ═══
//
// **العِلَّة** (‏2026-10-30): فِعلُ المالِكِ في د51 كانَ «أَعِد حِفظَ باقَةِ كُلٍّ مِن `/admin/tenants/{slug}/plan`»، و`TenantPlanAdminService.Set`
// يَبني وَثيقَةً جَديدَةً ويُخَزِّنُها بِلا قِراءَةِ المَحفوظ: فَيَضيعُ مُعَرِّفُ اشتِراكِ PayPal ورابِطُ الدَفعِ الَّذي يَراه صاحِبُ المَتجَرِ
// في الاستوديو وعَلامَةُ «لَن يُجَدَّد تِلقائِيّاً». **وما يُقلَبُ** (القاعِدَة ٣): الصَفُّ الثالِثُ وَحدَه، بِاسمِه، في كوميتِ التَبديل.
//
// **وما يُثَبَّتُ لِيَبقى أَخضَرَ بِلا حَرفٍ في جِسمِه** — صَفّانِ لِما لا يَمَسُّه التَبديل:
//   (١) التَعيينُ يَكتُبُ قِيَمَ الاستِمارَة ويَجعَلُ الباقَةَ سارِيَةً ولَو كانَت مَوقوفَة — مَقصودٌ بِنَصِّ `Stop`: «مَن أَوقَفَ بِالخَطَأ
//       يُعيدُها بِتَعيينٍ واحِد»؛
//   (٢) وتَعيينٌ فيه خَرقٌ لا يُخَزِّنُ شَيئاً ولا يَمَسُّ المَحفوظ.
// والمِعيارُ نِداءُ الخِدمَةِ كَما تُناديه النُقطَة (`Save` أَدناه) — يَتَغَيَّرُ مَعَ النُقطَةِ وَحدَه، وأَجسامُ الصُفوفِ لا تُمَسّ.

public class TenantPlanSetCharacterizationTests
{
    private const string Slug = "plan-demo";
    private static readonly DateTime At = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Starts = new(2026, 11, 1, 20, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 12, 1, 20, 59, 59, DateTimeKind.Utc);

    /// <summary>باقَةٌ مَحفوظَةٌ بِكُلِّ ما يَكتُبُه غَيرُ الاستِمارَة: إيقافُ المُشرِف، ورَبطُ PayPal، ورِسالَةُ إلغاءِ التَجديد.</summary>
    private static TenantPlan StoredPlan() => new()
    {
        Id = Slug,
        PlanId = "manual",
        Status = PlatformPlanStatuses.Stopped,
        StartsAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
        GraceDays = 14,
        Price = 99m,
        SetBy = "studio · admin",
        SetAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        PayPalSubscriptionId = "I-SUB-42",
        PayPalApproveUrl = "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-42",
        RenewalCancelledAt = new DateTime(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc),
    };

    /// <summary>نِداءُ الخِدمَةِ كَما تُناديه <c>/admin/tenants/{slug}/plan/save</c>.</summary>
    private static Task<IReadOnlyList<TenantPlanViolation>> Save(
        DocWorld w, string planId, DateTime starts, DateTime expires, int grace, decimal price)
        => Task.FromResult(TenantPlanAdminService.Set(w.Session, Slug, planId, starts, expires, grace, price, "studio · admin-2", At));

    /// <summary>(١) قِيَمُ الاستِمارَةِ تُكتَب، والمَوقوفَةُ تَعودُ سارِيَة — كَما صُمِّمَ التَعيين.</summary>
    [Fact]
    public async Task Setting_a_plan_writes_the_form_values_and_makes_a_stopped_plan_active()
    {
        var w = new DocWorld().Put(StoredPlan());

        var violations = await Save(w, "manual", Starts, Expires, 7, 120m);

        Assert.Empty(violations);
        var plan = Assert.IsType<TenantPlan>(Assert.Single(w.Stored));
        Assert.Equal(Slug, plan.Id);
        Assert.Equal("manual", plan.PlanId);
        Assert.Equal(PlatformPlanStatuses.Active, plan.Status);
        Assert.Equal(Starts, plan.StartsAt);
        Assert.Equal(Expires, plan.ExpiresAt);
        Assert.Equal(7, plan.GraceDays);
        Assert.Equal(120m, plan.Price);
        Assert.Equal("studio · admin-2", plan.SetBy);
        Assert.Equal(At, plan.SetAt);
        Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(plan, At));
    }

    /// <summary>(٢) خَرقٌ واحِدٌ ⇒ لا وَثيقَةَ تُخَزَّن، والمَحفوظُ كَما هو.</summary>
    [Fact]
    public async Task A_setting_with_a_violation_stores_nothing_and_leaves_the_stored_plan()
    {
        var stored = StoredPlan();
        var w = new DocWorld().Put(stored);

        var violations = await Save(w, "manual", Expires, Starts, 7, 120m);

        Assert.Equal(TenantPlanPolicy.PeriodInvalid, Assert.Single(violations).Code);
        Assert.Empty(w.Stored);
        Assert.Same(stored, w.Read<TenantPlan>(Slug));
        Assert.Equal(PlatformPlanStatuses.Stopped, stored.Status);
        Assert.Equal("I-SUB-42", stored.PayPalSubscriptionId);
    }

    /// <summary>(٣) <b>اليَوم</b>: إعادَةُ الحِفظِ تُسقِطُ رَبطَ PayPal ورابِطَ الدَفعِ وعَلامَةَ إلغاءِ التَجديد — فَيَختَفي رابِطُ الدَفعِ
    /// مِن استوديو صاحِبِ المَتجَر وسَطرُ «لَن يُجَدَّد تِلقائِيّاً». يُقلَبُ بِاسمِه في كوميتِ التَبديل.</summary>
    [Fact]
    public async Task Re_saving_a_plan_today_drops_its_paypal_subscription_its_pay_link_and_its_renewal_cancelled_mark()
    {
        var w = new DocWorld().Put(StoredPlan());

        Assert.Empty(await Save(w, "manual", Starts, Expires, 14, 99m));

        var plan = Assert.IsType<TenantPlan>(Assert.Single(w.Stored));
        Assert.Null(plan.PayPalSubscriptionId);
        Assert.Null(plan.PayPalApproveUrl);
        Assert.Null(plan.RenewalCancelledAt);
    }
}
