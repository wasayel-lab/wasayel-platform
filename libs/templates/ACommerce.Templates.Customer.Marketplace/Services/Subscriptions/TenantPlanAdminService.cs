using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

/// <summary>
/// <para><b>ضَبطُ باقَةِ مُستَأجِرٍ وإيقافُها، وتَحريرُ تَعليمات
/// التَحويل</b> — تَأخُذ الجَلسَةَ ولا تَفتَحُها ولا تُودِع، كَبَقِيَّة
/// خِدمات هذا المُجَلَّد. والقَرارُ نَفسُه في
/// <see cref="TenantPlanPolicy"/> نَقِيّاً يُختَبَر بِلا قاعِدَةِ
/// بَيانات.</para>
///
/// <para><b>ولا دَورَةَ اعتِماد هُنا</b>: مُشرِفُ المَنَصَّة <b>هُوَ</b>
/// القابِض، فَلا مَعنى لِأَن يَطلُبَ مِن نَفسِه ويَعتَمِدَ لِنَفسِه.
/// وهذا بِعَينِه ما جَعَلَ آلِيَّةَ الأَمس (‏طَلَبٌ ← اعتِماد) غَيرَ
/// قابِلَةٍ لِإعادَةِ الاستِعمال هُنا — فَحُذِفَت ولَم تُعَطَّل
/// (القاعِدَة ١).</para>
/// </summary>
public static class TenantPlanAdminService
{
    /// <summary>أَسماءُ أَفعال التَدقيق — تَسكُن مَعَ المَنطِق فَلا
    /// يَختَرِعُها سَطحٌ ولا تَنجَرِف.</summary>
    public const string SetAuditAction      = "platform.tenant_plan_set";
    public const string StopAuditAction     = "platform.tenant_plan_stop";
    public const string SettingsAuditAction = "platform.transfer_instructions_save";

    /// <summary>
    /// <para><b>تَعيينٌ أَو تَمديد — عَمَلِيَّةٌ واحِدَة.</b> التَمديدُ
    /// لَيسَ فِعلاً ثانِياً بَل نَفسُ النَموذَجِ بِتاريخِ انتِهاءٍ
    /// أَبعَد: فِعلانِ لِشَيءٍ واحِدٍ يَنجَرِفانِ في التَحَقُّق.</para>
    ///
    /// <para>يُرجِع قائِمَةَ الخُروق — فارِغَةٌ تَعني أَنّ الوَثيقَةَ
    /// خُزِّنَت في الجَلسَة. <b>ولا يُخَزَّن شَيءٌ عِندَ أَيّ خَرق.</b></para>
    /// </summary>
    public static IReadOnlyList<TenantPlanViolation> Set(
        IDocumentSession session, string tenantSlug, string planId,
        DateTime startsAt, DateTime expiresAt, int graceDays, decimal price,
        string setBy, DateTime at)
    {
        var plan = new TenantPlan
        {
            Id        = tenantSlug,
            PlanId    = planId,
            Status    = PlatformPlanStatuses.Active,
            StartsAt  = startsAt,
            ExpiresAt = expiresAt,
            GraceDays = graceDays,
            Price     = price,
            SetBy     = setBy,
            SetAt     = at,
        };

        var violations = TenantPlanPolicy.Validate(plan);
        if (violations.Count > 0) return violations;

        session.Store(plan);
        return Array.Empty<TenantPlanViolation>();
    }

    /// <summary>
    /// <para><b>إيقافٌ يَدَوِيّ</b> — يُخفي المَتجَرَ مِن لَحظَتِه ولا
    /// يَنتَظِر انتِهاءَ المُدَّة، <b>ولا يُغَيِّر التَواريخ</b>: مَن
    /// أَوقَفَ بِالخَطَأ يُعيدُها بِتَعيينٍ واحِد، وسِجِلُّ المُدَّة
    /// المَدفوعَة يَبقى كَما هو.</para>
    ///
    /// <para>يُرجِع <c>false</c> إن لَم تَكُن لِلمَتجَر باقَةٌ أَصلاً —
    /// ولا يُخترَع لَه واحِدَةٌ لِيُوقَف.</para>
    /// </summary>
    public static bool Stop(
        IDocumentSession session, TenantPlan? plan, string stoppedBy, DateTime at)
    {
        if (plan is null) return false;
        plan.Status = PlatformPlanStatuses.Stopped;
        plan.SetBy  = stoppedBy;
        plan.SetAt  = at;
        session.Store(plan);
        return true;
    }

    /// <summary>يُعيدُ باقَةً مُوقَفَةً إلى السَرَيان بِلا مَسّ
    /// التَواريخ — والاشتِقاقُ يَحكُم بَعدَها: مُنتَهِيَةٌ تَعود
    /// إلى السَماحِ أَو الإخفاء بِحَسَب تاريخِها لا بِحَسَب
    /// النَقرَة.</summary>
    public static bool Resume(
        IDocumentSession session, TenantPlan? plan, string resumedBy, DateTime at)
    {
        if (plan is null) return false;
        plan.Status = PlatformPlanStatuses.Active;
        plan.SetBy  = resumedBy;
        plan.SetAt  = at;
        session.Store(plan);
        return true;
    }

    /// <summary>تَعليماتُ التَحويلِ إلى وَسايِل — نَصٌّ واحِدٌ لِلمَنَصَّة
    /// كُلِّها.</summary>
    public static void SaveTransferInstructions(
        IDocumentSession session, PlatformSettings settings, string text,
        string savedBy, DateTime at)
    {
        settings.Id                   = PlatformSettings.SingletonId;
        settings.TransferInstructions = text.Trim();
        settings.UpdatedBy            = savedBy;
        settings.UpdatedAt            = at;
        session.Store(settings);
    }
}
