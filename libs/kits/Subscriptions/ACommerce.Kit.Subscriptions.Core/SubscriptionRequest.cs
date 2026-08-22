using ACommerce.Platform.Flows;

namespace ACommerce.Kit.Subscriptions;

/// <summary>حالات طَلَب الاشتِراك — <b>مُحالَة إلى نَفس التَعريف
/// الواحِد</b> الَّذي تُحيل إلَيه عُدَدُ الأَدوار والمَظهَر وتَعريفات
/// الباقات. لا مَعجَم حالات خامِس، ولا دَورَةَ اعتِمادٍ ثانِيَة.</summary>
public static class SubscriptionRequestStatuses
{
    public const string Pending  = ApprovalFlow.Pending;
    public const string Approved = ApprovalFlow.Approved;
    public const string Rejected = ApprovalFlow.Rejected;

    public static IReadOnlyList<string> All => ApprovalFlow.All;

    public static bool Contains(string status) => ApprovalFlow.Contains(status);
}

/// <summary>
/// <para><b>طَلَبُ اشتِراكٍ في باقَةٍ بِسِعر</b> — وَثيقَةُ Marten
/// بِإيجارٍ مُقتَرِن، تَحمِل <b>نَفسَ الأَعضاء الثَمانِيَة</b> الَّتي
/// يَحمِلُها كُلُّ ما يَمُرّ بِدَورَة الاعتِماد في هذا المُستَودَع:
/// هُوِيَّة، وسلاج، ونَصّ، وحالَة مِن <see cref="ApprovalFlow"/>،
/// وأَثَرُ مَن كَتَبَ ومَن قَرَّرَ ومَتى.</para>
///
/// <para><b>ولِماذا لا تُنَفِّذ <c>ITenantDefinitionDocument</c></b>:
/// تِلكَ الواجِهَة عَقدُ <b>وَثيقَةِ تَعريفٍ يُؤَلِّفُها مُستَأجِر</b> —
/// لَها مُحَمِّلٌ ومُصادِقٌ يَقرَآنِ <c>DefinitionJson</c>، ولَها
/// <c>TenantDefinitionService</c> بِكاشٍ ولَقطَةٍ تُبنى مِن
/// المُعتَمَد. والطَلَبُ لَيسَ تَعريفاً: لا نَصَّ يُصادَق، ولا لَقطَةَ
/// تُبنى مِنه، وأَثَرُ اعتِمادِه <b>حَدَثٌ في مَجرى</b> لا سَطرٌ في
/// كاش. فَتَنفيذُ الواجِهَة كانَ سَيَجُرّ حَقلاً مَيِّتاً
/// (<c>DefinitionJson</c>) وطَبَقَةَ خِدمَةٍ لا مُستَهلِكَ لَها
/// (القاعِدَة ١). المُشتَرَكُ المَقصود — <b>مَعجَمُ الحالات ودَورَتُها</b>
/// — مُشتَرَكٌ فِعلاً وبِالإحالَة لا بِالنَسخ.</para>
///
/// <para><b>ولِماذا لَقطَةُ الباقَة داخِلَ الطَلَب</b>: الاعتِمادُ يَقَع
/// بَعدَ أَيّامٍ مِن الطَلَب — والباقَةُ قَد يُغَيَّر سِعرُها أَو
/// حِصَّتُها بَينَهُما، وقَد تُحذَف. فَالمَمنوحُ عِندَ الاعتِماد هُوَ
/// <b>ما طَلَبَه المُستَخدِم ودَفَعَ مُقابِلَه</b>، لا ما صارَت إلَيه
/// الوَثيقَةُ الحَيَّة. وهذا فَرقُ مالٍ لا فَرقُ عَرض.</para>
/// </summary>
public sealed class SubscriptionRequest
{
    /// <summary>
    /// <para><b>هُوِيَّةُ الوَثيقَة، وهي رَقمُ الطَلَب المَرجِعيّ
    /// نَفسُه.</b> يُعرَض لِلمُستَخدِم ويُكتَب في إشعار الحَوالَة،
    /// فَبِه يُطابِق المُشرِفُ الحَوالَةَ بِالطَلَب.</para>
    ///
    /// <para><b>ولا حَقلَ ثانٍ لِلمَرجِع</b>: مُعَرِّفانِ لِشَيءٍ واحِد
    /// يَنجَرِفان، وخاصِّيَّةٌ مُشتَقَّةٌ تُسَلسَل مَرَّةً ثانِيَة في
    /// الوَثيقَة المُخَزَّنَة. الفَرادَةُ داخِلَ المُستَأجِر مَضمونَةٌ
    /// بِالإيجار المُقتَرِن.</para>
    /// </summary>
    public string Id { get; set; } = "";

    public Guid UserId { get; set; }

    /// <summary>اسمُ صاحِب الطَلَب كَما تَعرِفُه الجَلسَة — لِلعَرض في
    /// شاشَة المُشرِف. و<see cref="UserId"/> هُوَ الحُجَّة.</summary>
    public string UserName { get; set; } = "";

    // ─── لَقطَةُ الباقَة وَقتَ الطَلَب (اُنظُر شَرحَ الصَنف) ───────────

    public string  PlanId        { get; set; } = "";
    public string  PlanName      { get; set; } = "";
    public decimal Price         { get; set; }
    public int     ListingsQuota { get; set; }
    public int     DaysPeriod    { get; set; }

    // ─── دَورَةُ الاعتِماد — نَفسُ الأَعضاء بِلا زِيادَة ولا نُقصان ───

    /// <summary>مِن <see cref="ApprovalFlow.All"/> حَصراً.</summary>
    public string Status { get; set; } = SubscriptionRequestStatuses.Pending;

    /// <summary>مَن كَتَبَ — لِلتَدقيق لا لِلقَرار.</summary>
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>مَن قَرَّرَ ومَتى — يُملَآنِ عِندَ الاعتِماد أَو الرَفض.</summary>
    public string?   DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>
    /// <para><b>مُعَرِّفُ الاشتِراك المَمنوح — وهُوَ مانِعُ التَكرار.</b>
    /// يُولَّد <b>عِندَ فَتح الطَلَب</b> لا عِندَ الاعتِماد، فَيَصير
    /// مَجرى الاشتِراك <b>مُشتَقّاً مِن الطَلَب</b> لا مِن لَحظَة
    /// النَقر. نَقرَتانِ على «اعتِمِد» تُنتِجانِ نَفسَ مُعَرِّف
    /// المَجرى، فَالثانِيَةُ تُرَدّ بِالحالَة لا بِمَجرىً ثانٍ.</para>
    ///
    /// <para><b>وحارِسانِ لا واحِد</b>: الحالَةُ تُفحَص أَوَّلاً
    /// (<c>pending</c> فَقَط تُقَرَّر)، والمُعَرِّفُ الثابِت هُوَ
    /// الشَبَكَةُ الثانِيَة لَو تَسابَقَ طَلَبانِ على نَفس
    /// الوَثيقَة — <c>StartStream</c> بِمُعَرِّفٍ قائِمٍ يَرمي، ولا
    /// يُنشِئ اشتِراكاً ثانِياً صامِتاً.</para>
    /// </summary>
    public Guid SubscriptionId { get; set; }
}

/// <summary>نَتيجَةُ تَوجيه نَقرَةِ «اشتَرِك».</summary>
public enum SubscribeRoute
{
    /// <summary>باقَةٌ بِلا سِعر — تُمنَح ذاتِيّاً كَما كانَت.</summary>
    GrantNow,

    /// <summary>باقَةٌ بِسِعر — يُفتَح طَلَبٌ مُعَلَّق ولا تُمنَح.</summary>
    OpenRequest
}

/// <summary>نَتيجَةُ قَرارِ مُشرِفٍ على طَلَب.</summary>
/// <param name="Ok">هَل وَقَعَ القَرارُ فِعلاً؟</param>
/// <param name="Code">رَمزُ الخَرق حينَ <c>!Ok</c> — ثابِتٌ
/// لِلاختِبارات ولِرِسالَة الواجِهَة.</param>
/// <param name="Grants">هَل يُنشَأ اشتِراكٌ بِهذا القَرار؟ اعتِمادٌ
/// واقِعٌ فَقَط يُعطي <c>true</c> — والرَفضُ لا يُنشِئ شَيئاً.</param>
public readonly record struct SubscriptionDecision(bool Ok, string Code, bool Grants)
{
    public static readonly SubscriptionDecision Approved = new(true,  "", true);
    public static readonly SubscriptionDecision Rejected = new(true,  "", false);

    public static SubscriptionDecision Refuse(string code) => new(false, code, false);
}

/// <summary>
/// <para><b>قَرارُ «هَل تُمنَح الباقَةُ بِنَقرَة؟» — دالّاتٌ نَقِيَّة.</b>
/// بِلا Marten وبِلا HTTP وبِلا <c>DateTime.UtcNow</c>: الوَقتُ
/// والعَشوائيَّةُ يُمَرَّرانِ، فَتُنادى مِن اختِبارٍ بِلا قاعِدَةِ
/// بَيانات — وهذا شَرطُ أَن يُبرهَنَ الإغلاقُ أَصلاً في جَولَةٍ
/// القاعِدَةُ فيها غَير مُتاحَة.</para>
///
/// <para><b>والعِلَّةُ المَقيسَة الَّتي كَتَبَت هذا المِلَفّ</b>:
/// <c>POST /{slug}/plans/{planId}/subscribe</c> كانَ يُحَمِّل
/// <see cref="Plan"/>، <b>يَتَجاهَل <see cref="Plan.Price"/></b>،
/// ويَفتَح <see cref="SubscriptionCreated"/> لِأَيّ مُستَخدِمٍ
/// مُسَجَّلٍ بِنَقرَة. أَي أَنّ حِصَّةَ الإعلانات — وهي الشَيءُ
/// الَّذي يَحرُسُه الاستِحقاقُ على <c>listings/create</c> — كانَت
/// تُمنَح مَجّاناً مِن زِرٍّ مَعروض. والمالِكُ يَقبِض <b>حَوالاتٍ
/// بَنكِيَّةً يَدَوِيَّة</b>، فَهذا تَسريبُ إيرادٍ مُباشِر لا ثَغرَةٌ
/// نَظَرِيَّة.</para>
///
/// <para><b>وما لَم يُمَسّ</b>: الباقَةُ المَجّانِيَّة
/// (<c>Price == 0</c>) تَبقى ذاتِيَّةً بِنَفس الحَدَث ونَفس
/// المَجرى ونَفس التَحويل — لا شَيءَ يُمنَح مَجّاناً هُنا إلّا ما
/// هُوَ مَجّانيٌّ بِتَعريفِه.</para>
/// </summary>
public static class SubscriptionRequestPolicy
{
    // ─── رُموزُ الخَرق ────────────────────────────────────────────────

    /// <summary>لا طَلَبَ بِهذا المَرجِع في هذا المُستَأجِر.</summary>
    public const string NotFound = "request_not_found";

    /// <summary>الطَلَبُ قُرِّرَ مِن قَبل — والقَرارُ لا يُعاد.
    /// <b>هذا هُوَ رَمزُ النَقرَةِ الثانِيَة</b>.</summary>
    public const string AlreadyDecided = "request_already_decided";

    /// <summary>الحُكمُ لَيسَ مِن <see cref="ApprovalFlow"/> — أَي أَنّ
    /// النَموذَجَ أَرسَلَ ما لا تَعرِفُه الدَورَة.</summary>
    public const string BadVerdict = "request_bad_verdict";

    // ─── التَوجيه ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>المِحوَرُ الوَحيد: هَل لِلباقَةِ سِعر؟</b> وسِعرٌ سالِبٌ
    /// يُعامَل مَجّانِيّاً لا مَدفوعاً — فَالمُصادِقُ يَمنَعُه عِندَ
    /// الكِتابَة، ولَو تَسَرَّبَ فَالأَسلَمُ أَلّا يُطالَبَ
    /// المُستَخدِمُ بِحَوالَةٍ سالِبَة.</para>
    /// </summary>
    public static SubscribeRoute Route(Plan plan)
        => plan.Price > 0m ? SubscribeRoute.OpenRequest : SubscribeRoute.GrantNow;

    /// <summary>نَفسُ السُؤال عَن الطَلَبِ نَفسِه — لِلواجِهَة.</summary>
    public static bool RequiresApproval(Plan plan)
        => Route(plan) == SubscribeRoute.OpenRequest;

    // ─── فَتحُ الطَلَب ───────────────────────────────────────────────

    /// <summary>
    /// <para><b>رَقمُ الطَلَب المَرجِعيّ</b> — ثَمانِيَةُ مَحارِفَ مِن
    /// الـGuid، بِبادِئَة تَجعَلُه مَقروءاً في إشعارِ حَوالَة.
    /// <b>دالَّةٌ لا مُوَلِّد</b>: العَشوائيَّةُ تَقَع عِندَ النُقطَة،
    /// فَيَبقى هذا حَتمِيّاً ويُختَبَر بِمُدخَلٍ ثابِت.</para>
    /// </summary>
    public static string NewReference(Guid seed)
        => "SR-" + seed.ToString("N")[..8].ToUpperInvariant();

    /// <summary>
    /// <para><b>الطَلَبُ مَبنِيّاً</b> — لَقطَةُ الباقَة، وصاحِبُها،
    /// ومُعَرِّفُ الاشتِراك الَّذي سَيُمنَح <b>إن</b> اعتُمِد.</para>
    /// </summary>
    public static SubscriptionRequest Open(
        Plan plan, Guid userId, string userName, string createdBy,
        DateTime at, Guid referenceSeed, Guid subscriptionId) => new()
    {
        Id             = NewReference(referenceSeed),
        UserId         = userId,
        UserName       = userName,
        PlanId         = plan.Id,
        PlanName       = plan.Name,
        Price          = plan.Price,
        ListingsQuota  = plan.ListingsQuota,
        DaysPeriod     = plan.DaysPeriod,
        Status         = SubscriptionRequestStatuses.Pending,
        CreatedBy      = createdBy,
        CreatedAt      = at,
        SubscriptionId = subscriptionId,
    };

    // ─── القَرار ─────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>القَرارُ مَفحوصاً</b> — بِسُؤال <see cref="ApprovalFlow"/>
    /// لا بِشَرطٍ مَكتوبٍ بِاليَد: هَل يوجَد انتِقالٌ مِن
    /// <c>pending</c> إلى هذا الحُكم يَملِكُه
    /// <see cref="ApprovalFlow.DecisionActor"/>؟ فَلَو أُضيفَت حالَةٌ
    /// رابِعَةٌ يَوماً عَرَفَها هذا المَسارُ مِن يَومِها.</para>
    ///
    /// <para><b>والنَقرَةُ الثانِيَة تُرَدّ هُنا</b>: طَلَبٌ حالَتُه
    /// <c>approved</c> لا يُقَرَّر ثانِيَةً، فَلا اشتِراكَ ثانٍ ولا
    /// حِصَّةٌ مُضاعَفَة.</para>
    /// </summary>
    public static SubscriptionDecision Decide(SubscriptionRequest? request, string verdict)
    {
        if (request is null) return SubscriptionDecision.Refuse(NotFound);
        if (!ApprovalFlow.IsDecision(verdict)) return SubscriptionDecision.Refuse(BadVerdict);
        if (request.Status != SubscriptionRequestStatuses.Pending)
            return SubscriptionDecision.Refuse(AlreadyDecided);

        return verdict == SubscriptionRequestStatuses.Approved
            ? SubscriptionDecision.Approved
            : SubscriptionDecision.Rejected;
    }

    /// <summary>يَكتُبُ أَثَرَ القَرار في الوَثيقَة. <b>لا يُنادى إلّا
    /// بَعدَ <see cref="Decide"/> مُوجِبَة</b> — والفَصلُ مَقصود:
    /// دالَّةُ القَرار تُختَبَر بِلا تَحوير، ودالَّةُ الأَثَر تُختَبَر
    /// بِلا إعادَة اشتِقاق القَرار.</summary>
    public static SubscriptionRequest Stamp(
        SubscriptionRequest request, string verdict, string decidedBy, DateTime at)
    {
        request.Status    = verdict;
        request.DecidedBy = decidedBy;
        request.DecidedAt = at;
        return request;
    }

    /// <summary>
    /// <para><b>حَدَثُ الاشتِراك مِن الطَلَب</b> — <b>نَفسُ</b>
    /// <see cref="SubscriptionCreated"/> القائِم، بِنَفس الوُسَطاء
    /// وبِنَفس التَرتيب. لا حَدَثَ جَديد: المَسارُ المَدفوع يَلتَقي
    /// بِالمَجّانيّ عِندَ هذا السَطر بِالضَبط، فَما يَقرَؤُه
    /// <see cref="Subscription.Apply(SubscriptionCreated)"/> واحِدٌ في
    /// الحالَتَين.</para>
    ///
    /// <para><b>والحِصَّةُ مِن اللَقطَة لا مِن الوَثيقَة الحَيَّة</b> —
    /// اُنظُر شَرحَ <see cref="SubscriptionRequest"/>.</para>
    /// </summary>
    public static SubscriptionCreated ToCreatedEvent(SubscriptionRequest request, DateTime at)
        => new(request.SubscriptionId, request.UserId, request.PlanId,
               request.ListingsQuota, request.DaysPeriod, at);
}
