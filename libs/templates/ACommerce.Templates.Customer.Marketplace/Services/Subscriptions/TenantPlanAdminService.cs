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
    ///
    /// <para><b>وما لا تَكتُبُه الاستِمارَةُ يُحمَلُ مِن المَحفوظ</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ F-SDC-1؛ تَوصيفُه
    /// <c>TenantPlanSetCharacterizationTests</c> عِندَ <c>24d5fb1</c>): مُعَرِّفُ اشتِراكِ PayPal، ورابِطُ المُوافَقَةِ الَّذي يَراه صاحِبُ
    /// المَتجَرِ في الاستوديو، وعَلامَةُ «لَن يُجَدَّد تِلقائِيّاً» — يَكتُبُها غَيرُ هذا البابِ (المُشرِفُ مِن بِطاقَةِ PayPal، ورَسائِلُ PayPal)،
    /// وكانَ التَعيينُ يَبني وَثيقَةً جَديدَةً فَيُسقِطُها. <b>والحالَةُ <c>Active</c> عِندَ التَعيينِ كَما صُمِّمَت</b>: «مَن أَوقَفَ بِالخَطَأ
    /// يُعيدُها بِتَعيينٍ واحِد» (<see cref="Stop"/>). والتَحميلُ هُنا لا في النُقطَة: نُقطَةٌ تَنسى التَحميلَ تُعيدُ العَطَبَ صامِتَة.</para>
    /// </summary>
    public static async Task<IReadOnlyList<TenantPlanViolation>> SetAsync(
        IDocumentSession session, string tenantSlug, string planId,
        DateTime startsAt, DateTime expiresAt, int graceDays, decimal price,
        string setBy, DateTime at)
    {
        var stored = await session.LoadAsync<TenantPlan>(tenantSlug);
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
            PayPalSubscriptionId = stored?.PayPalSubscriptionId,
            PayPalApproveUrl     = stored?.PayPalApproveUrl,
            RenewalCancelledAt   = stored?.RenewalCancelledAt,
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

/// <summary>
/// <para><b>تَرميمُ باقاتٍ حُفِظَت قَبلَ د51 — يَومُها يَنتَهي آخِرَه بِتَوقيتِ المَنَصَّةِ لا مُنتَصَفَ لَيلِه UTC</b> (‏2026-10-30، تَدقيقُ الموجَة ١١
/// F-SDC-1؛ ‏ADR-039 مُلحَق د51). القَرارُ هُنا دالَّةً نَقِيَّة، والسكربتُ <c>scripts/repair-plan-day-end.cs</c> يَعرِضُ ثُمَّ يُنَفِّذُ بِعَدَدٍ يُؤَكَّد.</para>
///
/// <para><b>ولِماذا سكربتٌ لا «أَعِد الحِفظ»</b>: <c>e3c8b2d</c> غَيَّرَ قِراءَةَ التاريخِ لَحظَةَ الحِفظ، فَبَقِيَت الباقاتُ المَحفوظَةُ قَبلَه عَلى
/// مُنتَصَفِ لَيلِ UTC (الثالِثَةَ فَجراً بِتَوقيتِ الرِياض)، وكانَ فِعلُ المالِكِ إعادَةَ حِفظِ كُلٍّ مِنها — والحِفظُ يُعيدُ الحالَةَ سارِيَةً
/// (يُظهِرُ مَتجَراً أَوقَفَه المُشرِف) وكانَ يُسقِطُ رَبطَ PayPal (<c>24d5fb1</c>). فَالسكربتُ يَكتُبُ التاريخَينِ وَحدَهُما.</para>
///
/// <para><b>المُرَشَّحُ باقَةٌ انتِهاؤُها مُنتَصَفُ لَيلٍ UTC بِالضَبط</b> — وهو ما يَقرَؤُه <see cref="TenantPlanPolicy.Derive"/>، وما يَعُدُّه أَمرُ
/// «ج» د51 (<c>like '%T00:00:00Z'</c>). ويُكتَبُ البَدءُ مَعَه إن كانَ مُنتَصَفَ لَيلٍ أَيضاً، فَيَبقى طولُ المُدَّةِ أَيّاماً كامِلَةً كَما يَكتُبُه
/// الحِفظُ اليَوم (‏«أ» S6·٢). <b>وبَدءٌ وَحدَه عَلى مُنتَصَفِ اللَيلِ لا يُمَسّ</b>: <c>Derive</c> لا يَقرَؤُه، ونَقلُه وَحدَه يُقَصِّرُ المُدَّةَ
/// الَّتي يُضيفُها تَمديدُ PayPal. وما سِوى التاريخَينِ — الحالَةُ وPayPal وعَلامَةُ إلغاءِ التَجديدِ ومَن ضَبَطَ ومَتى — لا يُمَسّ.</para>
/// </summary>
public static class TenantPlanDayEndRepair
{
    /// <summary>سَطرُ التَدقيقِ لِكُلِّ باقَةٍ تُرَمَّم — <c>before</c>/<c>after</c> تاريخُ الانتِهاءِ قَبلُ وبَعدُ.</summary>
    public const string AuditAction = "platform.tenant_plan_day_end_repair";

    /// <summary>لَحظَةٌ عَلى مُنتَصَفِ لَيلٍ UTC بِالضَبط (ولا تُعَدُّ <see cref="DateTime.MinValue"/> تاريخاً).</summary>
    public static bool IsMidnightUtc(DateTime at) => at != DateTime.MinValue && at.TimeOfDay == TimeSpan.Zero;

    /// <summary>أَتُرَمَّمُ هذه الباقَة؟ انتِهاؤُها عَلى مُنتَصَفِ لَيلٍ UTC.</summary>
    public static bool IsCandidate(TenantPlan plan) => IsMidnightUtc(plan.ExpiresAt);

    /// <summary>آخِرُ اليَومِ (بِتاريخِ UTC المُخَزَّن) بِتَوقيتِ المَنَصَّة — الدالَّةُ الَّتي يَقرَأُ بِها الحِفظُ اليَومَ المَكتوب.</summary>
    public static DateTime DayEnd(DateTime midnightUtc)
        => ACommerce.Kit.Culture.CultureTime.EndOfDayUtc(DateOnly.FromDateTime(midnightUtc));

    /// <summary>يَكتُبُ التاريخَينِ وَحدَهُما ويُرجِعُ عَدَدَ ما تَغَيَّرَ مِنهُما (صِفرٌ لِغَيرِ المُرَشَّح، فَإعادَةُ التَشغيلِ لا تَجِدُ شَيئاً).</summary>
    public static int Apply(TenantPlan plan)
    {
        if (!IsCandidate(plan)) return 0;
        var changed = 1;
        if (IsMidnightUtc(plan.StartsAt))
        {
            plan.StartsAt = DayEnd(plan.StartsAt);
            changed++;
        }
        plan.ExpiresAt = DayEnd(plan.ExpiresAt);
        return changed;
    }
}
