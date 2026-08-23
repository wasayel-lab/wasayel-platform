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
    /// <summary>
    /// <para>يُرجِع <b>رَمزَ خَرقٍ مِن <see cref="PlanPurchasePolicy"/></b>
    /// حينَ يُرفَض الشِراء، و<c>null</c> حينَ يَقَع. والمُنادي يَحفَظ
    /// عِندَ <c>null</c> وَحدَها.</para>
    /// </summary>
    /// <param name="session">جَلسَةُ العَمَلِيَّة — تُلحَق فيها الأَحداث.</param>
    /// <param name="paymentProviderConfigured">مِن وَثيقَة المُستَأجِر،
    /// يَقرَؤُها المُنادي — فَالخِدمَةُ لا تَفتَح جَلسَةً ولا تَملِكُها.</param>
    public static async Task<string?> SubscribeAsync(
        IDocumentSession session, string planId, Guid userId,
        bool paymentProviderConfigured, DateTime at, Guid subscriptionId,
        CancellationToken ct = default)
    {
        var plan = await session.LoadAsync<Plan>(planId, ct);
        var refusal = PlanPurchasePolicy.Refuse(plan, paymentProviderConfigured);
        if (refusal is not null) return refusal;

        var created = new SubscriptionCreated(
            subscriptionId, userId, planId, plan!.ListingsQuota, plan.DaysPeriod, at);
        session.Events.StartStream<Subscription>(created.Id, created);
        return null;
    }
}
