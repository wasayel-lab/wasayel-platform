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
    public static string AuditActionFor(PayPalBillingAction action) => action switch
    {
        PayPalBillingAction.Extend   => ExtendAuditAction,
        PayPalBillingAction.Withdraw => WithdrawAuditAction,
        _                            => StoppedAuditAction
    };

    // ═══ مَسارُ الطَلَبات (‏ADR-006) — ونَفسُ الكاتِبِ لا كاتِبٌ ثانٍ ═══

    /// <summary>سَحبُ مُدَّةٍ بَعدَ استِردادٍ أَو عَكسِ دَفعَة —
    /// <b>قَرارٌ إداريٌّ بِأَثَرٍ نَقديّ</b>، فَلَه سَطرُ تَدقيقٍ
    /// بِاسمِه لا يُخلَط بِالتَمديد.</summary>
    public const string WithdrawAuditAction = "platform.tenant_plan_paypal_withdraw";

    /// <summary>إنشاءُ رابِطِ دَفعٍ مَرِن مِن الشاشَة.</summary>
    public const string OrderAuditAction = "platform.tenant_plan_paypal_order";

    /// <summary>التِقاطٌ نودِيَ — مِن الحَدَثِ أَو بِزِرٍّ يَدَوِيّ.</summary>
    public const string CaptureAuditAction = "platform.tenant_plan_paypal_capture";

    /// <summary>
    /// <para><b>يُخَزِّنُ وَثيقَةَ الدَفعِ المُعَلَّق، ويُرجِعُ هَل
    /// كُتِبَ شَيءٌ فِعلاً.</b> <c>false</c> تَعني <b>صِفرَ وَثيقَةٍ
    /// مُخَزَّنَة</b> — وهُوَ ما يَفحَصُه اختِبارُ «رابِطٌ لَم تُعِدهُ
    /// PayPal لا يُخَزَّن».</para>
    ///
    /// <para><b>و<c>Store</c> لا <c>Insert</c></b>: المِفتاحُ مَرجِعُنا
    /// الحَتميّ، والمَقصودُ أَنّ <b>لِمُدخَلاتٍ واحِدَةٍ وَثيقَةً
    /// واحِدَة</b> لا أَنّ الوَثيقَةَ تُكتَبُ مَرَّةً في العُمر.
    /// فَنَقرَتانِ تَكتُبانِ فَوقَ الوَثيقَةِ نَفسِها،
    /// و<c>PayPal-Request-Id</c> يَمنَع الطَلَبَ الثانِيَ عِندَ PayPal.</para>
    /// </summary>
    public static bool SaveOrder(IDocumentSession session, PayPalOrderRecord? order)
    {
        if (order is null
            || string.IsNullOrWhiteSpace(order.Id)
            || string.IsNullOrWhiteSpace(order.OrderId)
            || string.IsNullOrWhiteSpace(order.ApproveUrl)) return false;

        session.Store(order);
        return true;
    }

    /// <summary>
    /// <para><b>يُطَبِّقُ قَرارَ حَدَثِ طَلَب، ويُرجِعُ هَل كُتِبَ شَيءٌ
    /// فِعلاً.</b></para>
    ///
    /// <para><b>والباعِثُ واحِدٌ لا اثنان</b> (القاعِدَة ٨): تَحريكُ
    /// <c>ExpiresAt</c> وتَسجيلُ مِفتاحِ مَرَّة-واحِدَة يَقَعانِ في
    /// <see cref="Apply"/> نَفسِها الَّتي يُنادِيها مَسارُ الاشتِراكات —
    /// <b>نَفسُ الجَلسَةِ، ونَفسُ <c>PayPalWebhookRecord</c> المُدرَجِ
    /// بِـ<c>Insert</c>، ونَفسُ المُعامَلَة</b>. وما يُضيفُه هذا المَسار
    /// وَثيقَةُ الطَلَبِ وَحدَها.</para>
    ///
    /// <para><b>ولا سِجِلَّ مَرَّة-واحِدَةٍ لِما لا يُمَدِّد</b>: تَعليمُ
    /// طَلَبٍ «مُعَلَّق» أَو «مَرفوض» لا يُدرِج صَفّاً، فَإعادَةُ
    /// الإرسالِ تُعيدُ التَعليمَ نَفسَه — <b>وذاكَ عَمَلٌ لا ضَرَرَ في
    /// تَكرارِه</b>، بِخِلافِ تَمديدٍ يُشتَرى بِمالٍ واحِدٍ مَرَّتَين.</para>
    /// </summary>
    public static bool ApplyOrder(
        IDocumentSession session, TenantPlan? plan, PayPalOrderRecord? order,
        PayPalOrderEvent e, PayPalOrderDecision decision, DateTime at)
    {
        if (!decision.Writes || order is null) return false;

        var wrote = false;

        if (decision.TouchesPlan && plan is not null)
        {
            var billing = new PayPalBillingDecision(
                decision.Action == PayPalOrderAction.Extend
                    ? PayPalBillingAction.Extend
                    : PayPalBillingAction.Withdraw,
                decision.NewExpiresAt, decision.ReasonAr);

            // ‏`SubscriptionId` و`NextBillingTime` غائِبانِ عَمداً: هذا
            // طَلَبٌ لا اشتِراك، فَلا يُكتَب حَقلٌ لا مَعنى لَه هُنا.
            wrote = Apply(session, plan,
                new PayPalWebhookEvent(e.EventId, e.EventType, order.TenantSlug, null, null),
                billing, at);
        }

        if (decision.TouchesOrder)
        {
            PayPalOrderBillingPolicy.Apply(order, e, decision, at);
            session.Store(order);
            wrote = true;
        }

        return wrote;
    }

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
