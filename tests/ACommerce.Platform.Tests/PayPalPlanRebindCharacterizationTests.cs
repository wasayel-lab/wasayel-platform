using System.Text.RegularExpressions;
using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ رَبطِ خُطَّةِ PayPal المُشتَرَكَةِ وروابِطُ المَتاجِرِ المَحفوظَة — تَوصيفُ «اليَوم» (‏2026-11-05، الموجَة ١٢ G4 · E-6) ═══
//
// **السِلسِلَةُ المَقيسَة** (القاعِدَة ٣ — يُثَبَّتُ ما يَبقى، ويُطبَعُ ما يَتَغَيَّر): رِباطُ PayPal مِفتاحُه سلاجُ باقَةِ المَنَصَّة (`manual`)، فَخُطَّةٌ
// تُنشَأُ مِن صَفحَةِ مَتجَرِ B تُعيدُ تَسعيرَ رابِطِ A التالي. ورابِطُ A المَحفوظُ ($10 مِن P-10) لا يَحمِلُ الخُطَّةَ الَّتي صُنِعَ مِنها، فَيَبقى بَعدَ
// أَن صارَت الخُطَّةُ P-25، وإعادَةُ حِفظِ الباقَةِ تَحمِلُه (`KeepsPayLink` يُقارِنُ `PlanId` وَحدَه)، والاستوديو يَعرِضُه «الدفع عبر PayPal».
// **وما يَبقى**: إعادَةُ الحِفظِ تُقبَلُ وتَحمِلُ مُعَرِّفَ الاشتِراكِ وعَلامَةَ الإلغاء؛ وبابُ إنشاءِ الخُطَّةِ يَحرُسُ أَوَّلاً ويَربِطُ قَبلَ الإيداع.

public class PayPalPlanRebindCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "store-a";
    private static readonly DateTime At = new(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Starts = new(2026, 10, 31, 20, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime Expires = new(2026, 11, 30, 20, 59, 59, DateTimeKind.Utc);

    internal const string OldLink = "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-10";

    /// <summary>مَتجَرُ A وعَلَيه رابِطٌ صُنِعَ مِن الخُطَّةِ القَديمَة P-10 ولَم يُوافَق عَلَيه، واشتِراكٌ سابِقٌ أُلغِيَ.</summary>
    internal static TenantPlan StoreA() => new()
    {
        Id = Slug, PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Starts, ExpiresAt = Expires, GraceDays = 14, Price = 10m,
        PayPalSubscriptionId = "I-OLD", RenewalCancelledAt = At.AddDays(-3),
        PayPalApproveUrl = OldLink,
    };

    /// <summary>الرِباطُ بَعدَ أَن أَعادَ المُشرِفُ إنشاءَ الخُطَّةِ بِـ$25 — مِن صَفحَةِ أَيِّ مَتجَر.</summary>
    internal static PlatformPlanPayPal Rebound() => new()
    {
        Id = "manual", ProductId = "PROD-1", PlanId = "P-25", Name = "باقَة", Amount = 25m, Currency = "USD", IntervalUnit = "MONTH",
    };

    /// <summary><b>إعادَةُ حِفظِ الباقَةِ نَفسِها والخُطَّةُ أُعيدَ إنشاؤُها</b>: يُطبَعُ الرابِطُ بَعدَها؛ ويَبقى القَبولُ والمُعَرِّفُ والعَلامَة.</summary>
    [Fact]
    public async Task Re_saving_the_same_plan_after_its_paypal_plan_was_re_created()
    {
        var w = new DocWorld().Put(StoreA()).Put(Rebound());
        var v = await TenantPlanAdminService.SetAsync(w.Session, Slug, "manual", Starts, Expires, 14, 10m, "studio · admin", At);
        var plan = w.Read<TenantPlan>(Slug)!;
        output.WriteLine($"إعادَةُ حِفظِ A والخُطَّةُ P-25: خُروق=[{string.Join(",", v.Select(x => x.Code))}] · الرابِطُ بَعدَه={plan.PayPalApproveUrl ?? "—"}");

        Assert.Empty(v);
        Assert.Equal(1, w.Wrote<TenantPlan>());
        Assert.Equal("I-OLD", plan.PayPalSubscriptionId);
        Assert.Equal(At.AddDays(-3), plan.RenewalCancelledAt);
    }

    /// <summary><b>بابُ إنشاءِ الخُطَّة</b>: تُطبَعُ نِداءاتُ خِدمَةِ الفَوتَرَةِ في جِسمِه (أَيُّها يَمَسُّ روابِطَ المَتاجِرِ الأُخرى)؛ ويَبقى الحارِسُ أَوَّلاً
    /// والرَبطُ قَبلَ الإيداع.</summary>
    [Fact]
    public void The_create_plan_door_and_what_it_writes()
    {
        var endpoint = Assert.Single(WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route == PayPalCatalogPlanTests.CreatePlanRoute).ToArray());
        var calls = Regex.Matches(endpoint.Body, @"PayPalBillingService\.(\w+)\(").Select(m => m.Groups[1].Value).ToList();
        output.WriteLine($"/plan/paypal-plan يُنادي: [{string.Join(", ", calls)}]");

        var guard = endpoint.Body.IndexOf("PlatformAdminGuard.EvaluateAsync", StringComparison.Ordinal);
        var bind = endpoint.Body.IndexOf("BindCatalogPlan", StringComparison.Ordinal);
        var save = endpoint.Body.IndexOf("SaveChangesAsync", StringComparison.Ordinal);
        Assert.True(guard >= 0 && bind > guard && save > bind, $"الحارِس {guard} · الرَبط {bind} · الإيداع {save}");
        Assert.Contains("BindCatalogPlan", calls);
    }
}
