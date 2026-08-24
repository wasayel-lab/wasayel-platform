using ACommerce.Kit.Payments;
using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

/// <summary>
/// <para><b>أَثَرُ رِسالَةِ PayPal على الوَثائِق</b> — تَأخُذ الجَلسَةَ
/// ولا تَفتَحُها ولا تُودِع، كَجارَتِها
/// <see cref="TenantPlanAdminService"/> حَرفاً. والقَرارُ نَفسُه في
/// <see cref="PayPalBillingPolicy"/> نَقِيّاً يُختَبَر بِلا قاعِدَةِ
/// بَيانات.</para>
///
/// <para><b>ومُعامَلَةٌ واحِدَةٌ لا اثنَتان</b>: وَثيقَةُ الباقَةِ
/// المُمَدَّدَة ووَثيقَةُ مَرَّة-واحِدَة تُخَزَّنانِ في <b>نَفسِ
/// الجَلسَة</b>، فَإمّا تُكتَبانِ مَعاً أَو لا يُكتَبُ شَيء. وهذا
/// هُوَ الشَكلُ الَّذي وَصَفَته وَثيقَةُ سَطحِ الـAPI وتَعَذَّرَ
/// عَلَيها هُناك — لِأَنّ <c>DealsService</c> تَفتَح جَلسَتَها
/// بِنَفسِها. وهُنا لا مانِع، <b>فَلا نافِذَةَ <c>in_progress</c>
/// أَصلاً</b>: لا يوجَد وَقتٌ يَكون فيه الحَدَثُ مُسَجَّلاً وأَثَرُه
/// لَم يَقَع.</para>
/// </summary>
public static class PayPalBillingService
{
    /// <summary>أَسماءُ أَفعال التَدقيق — تَسكُن مَعَ المَنطِق فَلا
    /// يَختَرِعُها سَطحٌ ولا تَنجَرِف. نَفسُ عادَةِ
    /// <see cref="TenantPlanAdminService"/>.</summary>
    public const string ExtendAuditAction  = "platform.tenant_plan_paypal_extend";
    public const string StoppedAuditAction = "platform.tenant_plan_paypal_renewal_stopped";
    public const string LinkAuditAction    = "platform.tenant_plan_paypal_link";

    /// <summary>إنشاءُ خُطَّةِ PayPal مِن الشاشَة — <b>قَرارٌ إداريٌّ
    /// بِأَثَرٍ نَقديّ</b> (يُنشِئ سِعراً يُخصَم شَهرِيّاً)، فَلَه
    /// سَطرُ تَدقيقٍ كَجاراتِه.</summary>
    public const string CatalogPlanAuditAction = "platform.paypal_catalog_plan_create";

    /// <summary>نِطاقُ التَدقيقِ لِرِسالَةٍ بِلا مُستَأجِرٍ مَعروف —
    /// نَفسُ <c>AuditWriter.PlatformScope</c> بِقيمَتِه، ويُقرَأُ مِن
    /// هُناك لا يُنسَخ.</summary>
    public const string UnknownTenantScope = Audit.AuditWriter.PlatformScope;

    /// <summary>
    /// <para><b>يُطَبِّقُ القَرارَ، ويُرجِعُ هَل كُتِبَ شَيءٌ فِعلاً.</b>
    /// <c>false</c> تَعني <b>صِفرَ وَثيقَةٍ مُخَزَّنَة</b> — وهذا
    /// بِعَينِه ما يَفحَصُه اختِبارُ «تَوقيعٌ فاشِلٌ ⇒ صِفرُ
    /// كِتابَة» و«‏custom_id مَجهولٌ ⇒ لا كِتابَة».</para>
    ///
    /// <para><b>ووَثيقَةُ مَرَّة-واحِدَة تُدرَج <c>Insert</c> لا
    /// <c>Store</c></b>: الأولى تَرتَدُّ مِن Postgres عِندَ تَكرارِ
    /// المِفتاح، والثانِيَةُ تَكتُب فَوقَه بِصَمت. والفَرقُ بَينَهُما
    /// هُوَ الفَرقُ بَينَ مَنعِ تَكرارٍ حَقيقيٍّ ومَظنون — نَفسُ
    /// حُجَّةِ <c>ApiIdempotencyService</c>.</para>
    ///
    /// <para><b>ولا يُسَجَّلُ حَدَثٌ لَم يُفعَل بِه شَيء</b>: نَوعٌ
    /// خارِجَ المَعجَم أَو مُستَأجِرٌ مَجهولٌ لا يَترُكُ صَفّاً. فَلَو
    /// ضُبِطَ الـ<c>custom_id</c> لاحِقاً وأُعيدَ إرسالُ الرِسالَةِ
    /// مِن لَوحَةِ PayPal، عَمِلَت — ولَم يَحجُبها سِجِلٌّ كُتِبَ يَومَ
    /// لَم نَكُن نَعرِف صاحِبَها.</para>
    /// </summary>
    public static bool Apply(
        IDocumentSession session, TenantPlan? plan,
        PayPalWebhookEvent e, PayPalBillingDecision decision, DateTime at)
    {
        if (!decision.Writes || plan is null) return false;

        PayPalBillingPolicy.Apply(plan, e, decision, at);
        session.Store(plan);

        session.Insert(PayPalBillingPolicy.RecordFor(e, decision, plan.ExpiresAt, at));
        return true;
    }

    /// <summary>فِعلُ التَدقيقِ المُقابِلُ لِلقَرار — فَسَطرُ السِجِلّ
    /// يَقول ماذا وَقَع لا «‏paypal».</summary>
    public static string AuditActionFor(PayPalBillingAction action)
        => action == PayPalBillingAction.Extend ? ExtendAuditAction : StoppedAuditAction;

    /// <summary>
    /// <para><b>يَكتُبُ رِباطَ الباقَةِ بِخُطَّةِ PayPal، ويُرجِعُ هَل
    /// كُتِبَ شَيءٌ فِعلاً.</b> <c>false</c> تَعني <b>صِفرَ وَثيقَةٍ
    /// مُخَزَّنَة</b> — وهُوَ ما يَفحَصُه اختِبارُ «بِلا مُعَرِّفِ
    /// خُطَّةٍ لا كِتابَة».</para>
    ///
    /// <para><b>و<c>Store</c> لا <c>Insert</c> هُنا، بِخِلافِ سِجِلّ
    /// مَرَّة-واحِدَة</b>: مِفتاحُ الوَثيقَةِ سلاجُ الباقَة، والمَقصودُ
    /// أَنّ <b>الباقَةَ لَها رِباطٌ واحِدٌ</b> لا أَنّ الرِباطَ يُكتَب
    /// مَرَّةً في العُمر. فَمُشرِفٌ يُصَحِّحُ سِعراً يَكتُب فَوقَه،
    /// و<c>Insert</c> كانَت سَتَرُدُّ خَطَأَ قاعِدَةِ بَياناتٍ لا
    /// يَفهَمُه أَحَد. <b>والتَكرارُ يُمنَع عِندَ PayPal نَفسِها</b>
    /// بِمِفتاحٍ مُشتَقٍّ حَتمِيّاً مِن المُدخَلات
    /// (<c>PayPalCatalogPolicy.PlanRequestId</c>) — فَنَقرَتانِ على
    /// نَفسِ النَموذَجِ لا تُنشِئانِ خُطَّتَين.</para>
    /// </summary>
    public static bool BindCatalogPlan(IDocumentSession session, PlatformPlanPayPal? binding)
    {
        if (binding is null
            || string.IsNullOrWhiteSpace(binding.Id)
            || string.IsNullOrWhiteSpace(binding.PlanId)) return false;

        session.Store(binding);
        return true;
    }

    /// <summary>
    /// <para><b>يَحفَظُ رابِطَ المُوافَقَةِ على وَثيقَةِ الباقَة.</b>
    /// يَراهُ المُشرِفُ لِيَنسَخَه، ورائِدُ الأَعمالِ في لافِتَةِ
    /// الاستوديو — <b>طَرَفانِ لا واحِد، ولِذلك يُخَزَّن</b> بَدَلَ
    /// أَن يُعرَض مَرَّةً ويَضيعَ بِتَحديثِ صَفحَة.</para>
    ///
    /// <para><b>ولا تُمَسُّ التَواريخُ ولا الحالَة</b>: إنشاءُ رابِطٍ
    /// لَيسَ دَفعاً. التَمديدُ يَقَع حينَ تَصِل رِسالَةٌ مُوَثَّقَةٌ
    /// تَقول إنّ المالَ وَصَل — لا حينَ يُنشَأُ الرابِط.</para>
    /// </summary>
    public static bool SaveApproveLink(
        IDocumentSession session, TenantPlan? plan,
        SubscriptionResult result, string by, DateTime at)
    {
        if (plan is null || string.IsNullOrWhiteSpace(result.ApproveUrl)) return false;

        plan.PayPalSubscriptionId = result.SubscriptionId;
        plan.PayPalApproveUrl     = result.ApproveUrl;
        plan.SetBy                = by;
        plan.SetAt                = at;
        session.Store(plan);
        return true;
    }
}
