using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;

/// <summary>ما وَقَعَ عِندَ نَقرَة «اشتَرِك» — أَربَعُ نَتائِجَ لا
/// اثنَتان، لِأَنّ الواجِهَةَ تُفَرِّق بَينَها في العَرض.</summary>
public enum SubscribeOutcome
{
    /// <summary>لا باقَةَ بِهذا المُعَرِّف — نَفسُ سُقوط اليَوم حَرفاً
    /// (رَدٌّ إلى صَفحَة الباقات بِلا أَثَر).</summary>
    PlanMissing,

    /// <summary>باقَةٌ مَجّانِيَّة — مُنِحَ الاشتِراكُ ذاتِيّاً كَما
    /// كانَ.</summary>
    Granted,

    /// <summary>باقَةٌ بِسِعر — فُتِحَ طَلَبٌ مُعَلَّق.</summary>
    RequestOpened,

    /// <summary>لِلمُستَخدِم طَلَبٌ مُعَلَّقٌ لِهذِه الباقَة أَصلاً —
    /// يُعاد إلَيه مَرجِعُه ولا يُفتَح ثانٍ. <b>وهذا هُوَ رَدُّ النَقرَة
    /// المُكَرَّرَة على زِرّ الاشتِراك</b>.</summary>
    RequestAlreadyOpen
}

/// <summary>نَتيجَةُ النَقرَة مَع الطَلَب إن وُجِد.</summary>
public readonly record struct SubscribeResult(
    SubscribeOutcome Outcome, SubscriptionRequest? Request)
{
    public static readonly SubscribeResult PlanMissing = new(SubscribeOutcome.PlanMissing, null);
    public static readonly SubscribeResult Granted     = new(SubscribeOutcome.Granted, null);
}

/// <summary>
/// <para><b>أَثَرُ طَلَبِ الاشتِراك وقَرارِه</b> — تَأخُذ الجَلسَةَ ولا
/// تَفتَحُها ولا تُودِع، كَما تَفعَل <c>ListingEditService</c> و
/// <c>BrandingSaveService</c>. فَتَبقى المُعامَلَةُ لِلنُقطَة، ويَبقى
/// القَرارُ نَفسُه في <see cref="SubscriptionRequestPolicy"/> نَقِيّاً
/// يُختَبَر بِلا قاعِدَةِ بَيانات.</para>
///
/// <para><b>والعِلَّةُ المَقيسَة</b>: كانَ جِسمُ نُقطَة
/// <c>POST /{slug}/plans/{planId}/subscribe</c> يُحَمِّل الباقَةَ
/// <b>ويَتَجاهَل سِعرَها</b> ثُمَّ يَفتَح مَجرى اشتِراك — فَحِصَّةُ
/// الإعلانات تُمنَح بِنَقرَة لِأَيّ مُستَخدِمٍ مُسَجَّل. والمالِكُ
/// يَقبِض حَوالاتٍ بَنكِيَّةً يَدَوِيَّةً ويَمنَح الباقاتِ بِيَدِه،
/// فَالزِرُّ كانَ يَبيع ما لا يُقبَض ثَمَنُه.</para>
///
/// <para><b>ولا دَورَةَ اعتِمادٍ جَديدَة</b>: نَفسُ
/// <c>pending/approved/rejected</c> ونَفسُ
/// <c>CreatedBy/DecidedBy/DecidedAt</c> الَّتي تَمُرّ بِها تَعريفاتُ
/// الأَدوار والمَظهَر — بِالإحالَة إلى <c>ApprovalFlow</c> لا
/// بِنَسخِه.</para>
/// </summary>
public static class SubscriptionRequestService
{
    /// <summary>اسمُ فِعل التَدقيق — يَسكُن مَع المَنطِق فَلا
    /// يَختَرِعُه سَطحٌ ولا يَنجَرِف.</summary>
    public const string DecideAuditAction = "tenant.subscription_request_decide";

    /// <summary>
    /// <para><b>نَقرَةُ «اشتَرِك»</b> — الباقَةُ المَجّانِيَّة تُمنَح
    /// كَما كانَت حَرفاً (<c>StartStream&lt;Subscription&gt;</c> بِنَفس
    /// الحَدَث ونَفس الوُسَطاء)، والمَدفوعَةُ تَفتَح طَلَباً
    /// مُعَلَّقاً.</para>
    ///
    /// <para><b>وطَلَبٌ مُعَلَّقٌ واحِدٌ لِكُلّ (مُستَخدِم، باقَة)</b>:
    /// نَقرَتانِ لا تَفتَحانِ طَلَبَين — فَيُطابِقُ المُشرِفُ حَوالَةً
    /// واحِدَةً بِمَرجِعٍ واحِد، ولا يَعتَمِد اثنَينِ فَيَمنَح
    /// حِصَّتَين.</para>
    /// </summary>
    public static async Task<SubscribeResult> SubscribeAsync(
        IDocumentSession session, string planId, Guid userId, string userName,
        DateTime at, Guid referenceSeed, Guid subscriptionId,
        CancellationToken ct = default)
    {
        var plan = await session.LoadAsync<Plan>(planId, ct);
        if (plan is null) return SubscribeResult.PlanMissing;

        if (SubscriptionRequestPolicy.Route(plan) == SubscribeRoute.GrantNow)
        {
            var free = new SubscriptionCreated(
                subscriptionId, userId, planId, plan.ListingsQuota, plan.DaysPeriod, at);
            session.Events.StartStream<Subscription>(free.Id, free);
            return SubscribeResult.Granted;
        }

        var open = await OpenRequestAsync(session, userId, planId, ct);
        if (open is not null) return new(SubscribeOutcome.RequestAlreadyOpen, open);

        var request = SubscriptionRequestPolicy.Open(
            plan, userId, userName, $"{userName} · {userId}", at, referenceSeed, subscriptionId);
        session.Store(request);
        return new(SubscribeOutcome.RequestOpened, request);
    }

    /// <summary>الطَلَبُ المُعَلَّق لِهذا المُستَخدِم في هذِه الباقَة،
    /// إن وُجِد.</summary>
    public static async Task<SubscriptionRequest?> OpenRequestAsync(
        IDocumentSession session, Guid userId, string planId, CancellationToken ct = default)
        => (await session.Query<SubscriptionRequest>()
                .Where(r => r.UserId == userId
                         && r.PlanId == planId
                         && r.Status == SubscriptionRequestStatuses.Pending)
                .ToListAsync(ct))
           .OrderByDescending(r => r.CreatedAt)
           .FirstOrDefault();

    /// <summary>
    /// <para><b>قَرارُ المُشرِف</b> — والاعتِمادُ وَحدَه يَفتَح مَجرى
    /// الاشتِراك، بِـ<b>نَفس</b> <c>SubscriptionCreated</c> الَّذي
    /// يَفتَحُه المَسارُ المَجّانيّ.</para>
    ///
    /// <para><b>ومُعَرِّفُ المَجرى مِن الوَثيقَة لا مِن اللَحظَة</b>:
    /// نَقرَتانِ على «اعتِمِد» تُعطِيانِ نَفسَ المُعَرِّف. فَإن أَفلَتَت
    /// الثانِيَةُ مِن فَحصِ الحالَة (سِباقٌ على نَفس الوَثيقَة) رَدَّها
    /// Marten بِمَجرىً قائِم — ولَم تُنشِئ اشتِراكاً ثانِياً
    /// صامِتاً.</para>
    /// </summary>
    public static async Task<(SubscriptionDecision Decision, SubscriptionRequest? Request)> DecideAsync(
        IDocumentSession session, string reference, string verdict, string decidedBy,
        DateTime at, CancellationToken ct = default)
    {
        var request = await session.LoadAsync<SubscriptionRequest>(reference, ct);
        var decision = SubscriptionRequestPolicy.Decide(request, verdict);
        if (!decision.Ok) return (decision, request);

        SubscriptionRequestPolicy.Stamp(request!, verdict, decidedBy, at);
        session.Store(request!);

        if (decision.Grants)
            session.Events.StartStream<Subscription>(
                request!.SubscriptionId,
                SubscriptionRequestPolicy.ToCreatedEvent(request, at));

        return (decision, request);
    }
}
