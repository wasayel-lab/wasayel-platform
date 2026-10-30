using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ رابِطُ دَفعِ PayPal يُحمَلُ ما دامَت الباقَةُ نَفسَها (مُراجَعَةُ تَيّارِ F4 الجَولَة ١) ═══
//
// **العِلَّة** (‏2026-10-30): `4bd0991` جَعَلَ التَعيينَ يَحمِلُ رابِطَ المُوافَقَةِ مِن المَحفوظِ دائِماً، والرابِطُ يُنشَأُ مِن خُطَّةِ PayPal
// لِلباقَةِ لَحظَتَها — فَتَغييرُ الباقَةِ كانَ يُبقي في الاستوديو زِرَّ دَفعٍ لِخُطَّةٍ غَيرِ باقَةِ المَتجَر (تَوصيفُه `e908295`). الدالَّةُ
// `TenantPlanAdminService.KeepsPayLink` هي القَرار، وقارِئُها الوَحيدُ `SetAsync`، وصَفّا السُلوكِ في `TenantPlanSetCharacterizationTests`
// (٣) الباقَةُ نَفسُها تَحمِلُ الرابِط، و(٥) باقَةٌ غَيرُها تُسقِطُه. وهُنا طَرَفا الدالَّةِ نَفسِها: المُوجَبُ والسالِب.

public class TenantPlanPayLinkTests
{
    private static TenantPlan Stored(string planId) => new()
    {
        Id = "plan-demo",
        PlanId = planId,
        PayPalApproveUrl = "https://www.paypal.com/webapps/billing/subscriptions?ba_token=BA-42",
    };

    /// <summary><b>المُوجَب</b>: الباقَةُ المَحفوظَةُ هي الَّتي تُعَيِّنُها الاستِمارَة ⇒ يُحمَلُ الرابِط.</summary>
    [Fact]
    public void The_same_plan_keeps_its_pay_link()
        => Assert.True(TenantPlanAdminService.KeepsPayLink(Stored("manual"), "manual"));

    /// <summary><b>السالِب</b>: باقَةٌ غَيرُها — ولَو بِحالَةِ حَرف، فَالكاتالوجُ ووَثيقَةُ الرِباطِ يَقرَآنِ المُعَرِّفَ <c>Ordinal</c> —
    /// أَو مَحفوظٌ بِلا مُعَرِّفِ باقَة ⇒ لا يُحمَلُ الرابِط.</summary>
    [Theory]
    [InlineData("plan-no-longer-listed", "manual")]
    [InlineData("manual", "plan-no-longer-listed")]
    [InlineData("Manual", "manual")]
    [InlineData("", "manual")]
    public void Another_plan_drops_the_pay_link(string storedPlanId, string planId)
        => Assert.False(TenantPlanAdminService.KeepsPayLink(Stored(storedPlanId), planId));

    /// <summary><b>السالِب</b>: لا باقَةَ مَحفوظَة ⇒ لا رابِطَ يُحمَل.</summary>
    [Fact]
    public void No_stored_plan_carries_no_pay_link()
        => Assert.False(TenantPlanAdminService.KeepsPayLink(null, "manual"));
}
