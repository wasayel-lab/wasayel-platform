using ACommerce.Kit.Subscriptions;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>رَفُّ الباقات: ما تَعرِضُه صَفحَةُ <c>/{slug}/plans</c> بِعَينِه</b> — باقاتُ المَتجَرِ المَرئِيَّةُ (<see cref="TenantPlanService.VisiblePlansAsync"/>)
/// مَصفِيَّةً بِـ<see cref="PlanPurchasePolicy.Visible"/> عَلى رابِطِ الدَفعِ الَّذي تُصَيِّرُه <c>PlanPay</c> (<c>PlansPayable</c>). <b>والصَفحَةُ وكُلُّ بابٍ إلَيها
/// يَسأَلونَ هذِه الدالَّةَ نَفسَها</b>، فَلا يُرسَمُ بابٌ يَفتَحُ عَلى «لا توجَد باقات بَعد».</para>
///
/// <para><b>لِماذا وُجِدَت (‏2026-10-25، تَدقيقُ الإطلاق W8-7 — <c>lens-dead-doors-5</c>)</b>: بِطاقَةُ «الباقات» في «حِسابي» كانَت تُرسَمُ في كُلِّ مَتجَر،
/// ومَتجَرُ الاستوديو يولَدُ بِلا <c>Plan</c> (‏<c>TenantFromAnalysisFactory.CreateAsync</c> يُخَزِّنُ <c>Tenant</c> وَحدَه) فَيَفتَحُها عَلى الحالَةِ الفارِغَةِ
/// ونَصٍّ لِلمُشَغِّل. مَقيسٌ حَيّاً: ‏<c>/owner-test/me</c> ← <c>/owner-test/plans</c> فارِغَة.</para>
///
/// <para><b>وشَرطُ الاستِخراجِ (القاعِدَة ١) ثَلاثَةُ مُستَهلِكين</b>: <c>Plans.razor</c> (الرَفُّ نَفسُه)، و<c>Me.razor</c> (البِطاقَةُ حينَ لا يَفرُغ)،
/// و<c>CreateListing.razor</c> (مَخرَجُ «عَرض الباقات» في رِسالَةِ نَفادِ الحِصَّة). والقِراءَتانِ وتَرتيبُهُما ما كانَ في <c>Plans.razor</c> حَرفاً،
/// ونُقطَةُ الاشتِراكِ تَقرَأُ <c>PlansPayable</c> بِنَفسِها كَما كانَت (‏<c>PaymentFlagReadersTests</c>).</para>
/// </summary>
public static class PlanShelf
{
    /// <summary>الباقاتُ الَّتي تَرسُمُها صَفحَةُ الباقات — وفَراغُها شَرطُ إخفاءِ كُلِّ بابٍ إلَيها.</summary>
    public static async Task<IReadOnlyList<Plan>> ShownAsync(
        TenantPlanService plans, TenantProviderService providers, string slug, CancellationToken ct = default)
        => PlanPurchasePolicy.Visible(await plans.VisiblePlansAsync(slug, ct), (await providers.ForAsync(slug, ct)).PlansPayable);
}
