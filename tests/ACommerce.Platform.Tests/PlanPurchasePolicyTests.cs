using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ الباقَةُ بِسِعرٍ لا تُباعُ مِن مَتجَرٍ بِلا مُزَوِّدِ دَفع ═══════
//
// **ما نَسَخَته هذِه المَوجَة**: يَومَ ‏2026-08-22 بُنِيَت دَورَةُ «طَلَبِ
// اشتِراكٍ مُعَلَّقٍ ← اعتِماد» بِتَعليماتِ حَوالَةٍ إلى **حِساب التاجِر**
// (‏ADR-002). وقَرارُ المالِك يَومَ ‏2026-08-23 حَرفيّاً: «لا تَسمَح
// لِلتاجِر بِاستِلام حَوالات» و«إمّا بَيعٌ بِلا رُسوم أَو تَكامُلُ
// بَوّابَةِ دَفعٍ خاصَّةٍ بِه لاحِقاً». فَالدَورَةُ حُذِفَت كامِلَةً
// (لا مُعَطَّلَةً — القاعِدَة ١)، وحَلَّ مَحَلَّها هذا القَرار.
//
// **وهذِه الاختِبارات هي الحَدُّ المَقيس** (القاعِدَة ٢): «الباقَةُ
// المَدفوعَةُ مَخفِيَّة» جُملَةٌ في وَثيقَة حَتّى تُكتَب هُنا.

public class PlanPurchasePolicyTests
{
    private static Plan Priced(decimal price, string id = "pro") => new()
    {
        Id = id, Name = "باقَة", Price = price,
        ListingsQuota = 10, DaysPeriod = 30, IsActive = true
    };

    // ─── الشِراء ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(99.5)]
    [InlineData(10000)]
    public void PaidPlan_IsRefused_WhenTheStoreHasNoPaymentProvider(decimal price)
    {
        Assert.False(PlanPurchasePolicy.IsPurchasable(Priced(price), false));
        Assert.Equal(PlanPurchasePolicy.PaidUnavailable,
            PlanPurchasePolicy.Refuse(Priced(price), false));
    }

    /// <summary>والمَجّانِيَّةُ تَبقى ذاتِيَّةً — لا شَيءَ يُمنَح مَجّاناً
    /// هُنا إلّا ما هُوَ مَجّانيٌّ بِتَعريفِه.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FreePlan_StaysSelfServe_EvenWithoutAProvider(decimal price)
    {
        Assert.True(PlanPurchasePolicy.IsPurchasable(Priced(price), false));
        Assert.Null(PlanPurchasePolicy.Refuse(Priced(price), false));
    }

    /// <summary>ويَومَ يُدمَج مُزَوِّدُ دَفعٍ لِلمَتجَر تُفتَح المَدفوعَة —
    /// الشَرطُ واحِدٌ لا شَرطان.</summary>
    [Fact]
    public void PaidPlan_OpensOnce_TheStoreHasAProvider()
    {
        Assert.True(PlanPurchasePolicy.IsPurchasable(Priced(50m), true));
        Assert.Null(PlanPurchasePolicy.Refuse(Priced(50m), true));
    }

    [Fact]
    public void AMissingPlan_IsRefusedByItsOwnCode()
        => Assert.Equal(PlanPurchasePolicy.PlanNotFound,
            PlanPurchasePolicy.Refuse(null, paymentProviderConfigured: true));

    // ─── العَرض ──────────────────────────────────────────────────────

    [Fact]
    public void PaidPlans_AreHiddenFromTheStorefront()
    {
        var all = new[] { Priced(0m, "free"), Priced(50m, "pro") };
        var visible = PlanPurchasePolicy.Visible(all, false);
        Assert.Single(visible);
        Assert.Equal("free", visible[0].Id);
    }

    /// <summary><b>التَكافُؤُ بِالمَرجِع</b>: قائِمَةٌ لا يُحذَف مِنها
    /// شَيءٌ تُرجَع هي نَفسُها — فَمَتجَرٌ كُلُّ باقاتِه مَجّانِيَّةٌ لا
    /// يَمُرّ بِفَرزٍ ولا نَسخ، ولا تَتَغَيَّر صَفحَتُه بايتاً.</summary>
    [Fact]
    public void AllFreePlans_AreReturnedByTheSameReference()
    {
        IReadOnlyList<Plan> all = new[] { Priced(0m, "a"), Priced(0m, "b") };
        Assert.Same(all, PlanPurchasePolicy.Visible(all, false));
    }

    [Fact]
    public void WithAProvider_NothingIsFiltered_AndTheReferenceIsKept()
    {
        IReadOnlyList<Plan> all = new[] { Priced(0m, "free"), Priced(50m, "pro") };
        Assert.Same(all, PlanPurchasePolicy.Visible(all, true));
    }

    // ─── المَعجَم ────────────────────────────────────────────────────

    /// <summary>الرُموزُ تُقرَأ في الواجِهَة وفي التَحويل — فَتُثَبَّت.
    /// انزِياحُ رَمزٍ يَعني رِسالَةً لا تُعرَض ومُستَخدِماً لا يَعرِف
    /// لِماذا رُفِض.</summary>
    [Fact]
    public void TheViolationCodes_ArePinned()
    {
        Assert.Equal("plan_not_found", PlanPurchasePolicy.PlanNotFound);
        Assert.Equal("plan_paid_unavailable", PlanPurchasePolicy.PaidUnavailable);
        Assert.Equal(2, PlanPurchasePolicy.Codes.Count);
    }
}
