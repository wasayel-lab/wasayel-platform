using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

/// <summary>
/// <para><b>أَثَرُ نَقرَة «اشتَرِك»</b> — تَأخُذ جَلسَةَ العَمَلِيَّة ولا
/// تَفتَحُها ولا تُودِع، كَما تَفعَل <c>ListingEditService</c> و
/// <c>BrandingSaveService</c>. فَتَبقى المُعامَلَةُ لِلنُقطَة، ويَبقى
/// القَرارُ نَفسُه في <see cref="PlanPurchasePolicy"/> نَقِيّاً يُختَبَر
/// بِلا قاعِدَةِ بَيانات.</para>
///
/// <para><b>وهذا خَلَفُ <c>SubscriptionRequestService</c> المَحذوف</b>
/// (‏2026-08-22 ← 2026-08-23). ذاكَ كانَ يَفتَح <b>طَلَبَ اشتِراكٍ
/// مُعَلَّقاً</b> لِلباقَة بِسِعر، ويَعرِض تَعليماتِ حَوالَةٍ إلى
/// <b>حِساب التاجِر</b>، ويَنتَظِر اعتِمادَ مُشرِفِ المَتجَر. وقَرارُ
/// المالِك نَسَخَ ذلك مِن أَصلِه: «لا تَسمَح لِلتاجِر بِاستِلام
/// حَوالات». فَالمَدفوعَةُ تُرَدّ، ولا تُترَك آلِيَّةٌ مُعَطَّلَةٌ
/// تَنتَظِر مُستَهلِكاً (القاعِدَة ١).</para>
///
/// <para><b>وباعِثُ <c>SubscriptionCreated</c> يَبقى واحِداً</b> —
/// انتَقَلَ إلى هذا المِلَفّ بِنَفس الوُسَطاء وبِنَفس التَرتيب.</para>
/// </summary>
public static class PlanSubscribeService
{
    /// <summary>ثَلاثَةُ مَخارِجَ لا اثنان: مَنحٌ، أَو رَفضٌ بِرَمزٍ مِن
    /// المَعجَمِ المُغلَق، أَو <b>تَحويلٌ إلى صَفحَةِ الدَفعِ عِندَ
    /// مُزَوِّدِ التاجِر</b>. والمُنادي يَحفَظ عِندَ
    /// <see cref="Granted"/> وَحدَها.</summary>
    public readonly record struct Outcome(
        PlanPurchasePolicy.PlanPurchaseRoute Route, string? Refusal)
    {
        public bool Granted => Route == PlanPurchasePolicy.PlanPurchaseRoute.Grant;
        public bool PayAtProvider => Route == PlanPurchasePolicy.PlanPurchaseRoute.PayAtProvider;
    }

    /// <summary>
    /// <para><b>الباقَةُ بِسِعرٍ لا تُمنَح مِن هُنا أَبَداً</b> — ولا
    /// حَتّى حينَ يَكونُ لِلمَتجَرِ مُزَوِّدُ دَفع. رابِطُ الدَفعِ
    /// المُستَضاف <b>لا يُعطي بُرهانَ دَفع</b> (لا سِرَّ وارِدٍ
    /// يُتَحَقَّقُ بِه، ولا جَلبَ حالَةٍ مِن الخادِم)، فَمَنحُ
    /// الحِصَّةِ عِندَ التَحويلِ إلَيه كانَ سَيُعيدُ ثَغرَةَ ‏ADR-002
    /// حَرفاً: حِصَّةٌ تُفتَح بِنَقرَةٍ بِلا قَبض.</para>
    ///
    /// <para><b>والتَكافُؤُ الصِفريّ</b>: عِندَ
    /// <paramref name="paymentProviderConfigured"/> = <c>false</c> —
    /// حالُ كُلّ مَتجَرٍ قَبلَ هذِه المَوجَة — الجَوابُ مُطابِقٌ
    /// لِلسابِقِ حَرفاً: مَنحٌ لِلمَجّانِيَّةِ ورَفضٌ بِنَفسِ الرَمزِ
    /// لِلمَدفوعَة.</para>
    /// </summary>
    /// <param name="session">جَلسَةُ العَمَلِيَّة — تُلحَق فيها الأَحداث.</param>
    /// <param name="paymentProviderConfigured">مِن وَثيقَة المُستَأجِر،
    /// يَقرَؤُها المُنادي — فَالخِدمَةُ لا تَفتَح جَلسَةً ولا تَملِكُها.</param>
    public static async Task<Outcome> SubscribeAsync(
        IDocumentSession session, string planId, Guid userId,
        bool paymentProviderConfigured, DateTime at, Guid subscriptionId,
        CancellationToken ct = default)
    {
        var plan = await session.LoadAsync<Plan>(planId, ct);
        var (route, refusal) = PlanPurchasePolicy.Decide(plan, paymentProviderConfigured);

        if (route != PlanPurchasePolicy.PlanPurchaseRoute.Grant)
            return new Outcome(route, refusal);

        var created = new SubscriptionCreated(
            subscriptionId, userId, planId, plan!.ListingsQuota, plan.DaysPeriod, at);
        session.Events.StartStream<Subscription>(created.Id, created);
        return new Outcome(route, null);
    }
}
