using System.Globalization;
using System.Text.Json;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.Paddle;

// ═══ رِسالَةُ Paddle — تُتَحَقَّق ثُمَّ تُقرَأ، لا العَكس ═══════════════
//
// **التَسليمُ مَرَّةً على الأَقَلّ، وقَد يَصِلُ خارِجَ تَرتيبِه.**
// فَكُلُّ ما هُنا **مُحايِدٌ لِلتَكرار** بِطَبَقَتَين لا واحِدَة:
// سِجِلُّ `event_id` (وَثيقَةُ مَرَّة-واحِدَة تُدرَج بِـ`Insert`
// فَتَرتَدُّ مِن Postgres)، **وحالَةُ الوَثيقَةِ نَفسِها** — فَـ
// «وَصَلَ المال» لا تُكتَبُ مَرَّتَين ولَو تَبَدَّلَ مُعَرِّفُ
// الحَدَث.
//
// **وفَشَلُ التَحَقُّقِ لا يُرَدُّ بِـ2xx**: الـ2xx تُخبِرُ Paddle أَنّ
// التَسليمَ نَجَح فَتَتَوَقَّف عَن الإعادَة.

/// <summary>
/// <para><b>أَحداثُ Paddle الَّتي نَتَصَرَّفُ بِها — مَعجَمٌ
/// مُغلَق.</b> وما سِواها يُتَجاهَل <b>صَراحَةً بِسَطرِ لوغ</b>، لا
/// يُبتلَع صامِتاً.</para>
/// </summary>
public static class PaddleEventTypes
{
    /// <summary><b>★ الحَدَثُ الوَحيدُ الَّذي يُمَدِّد.</b> ومَعناهُ
    /// «اكتَمَلَت المُعامَلَةُ ووَصَلَ المال» — لا «أُنشِئَت» ولا
    /// «وافَقَ الدافِع».</summary>
    public const string TransactionCompleted = "transaction.completed";

    /// <summary>وُلِدَ اشتِراكٌ — <b>حالَةٌ لا مال</b>.</summary>
    public const string SubscriptionCreated = "subscription.created";

    /// <summary>تَغَيَّرَ اشتِراك — <b>حالَةٌ لا مال</b>.</summary>
    public const string SubscriptionUpdated = "subscription.updated";

    /// <summary>أُلغِيَ اشتِراك — <b>يُوقَفُ التَجديدُ ولا تُمَسُّ
    /// المُدَّةُ المَدفوعَة</b>: مَن دَفَعَ شَهراً يَأخُذُ شَهرَه
    /// كامِلاً.</summary>
    public const string SubscriptionCanceled = "subscription.canceled";

    /// <summary>تَسوِيَةٌ أُنشِئَت — واستِردادُ المالِ أَحَدُ
    /// أَفعالِها.</summary>
    public const string AdjustmentCreated = "adjustment.created";

    /// <summary>تَسوِيَةٌ تَغَيَّرَت — <b>وهُنا تَقَعُ المُوافَقَةُ
    /// عادَةً</b>: تَسوِيَةٌ تُنشَأُ «بِانتِظارِ المُوافَقَة» ثُمَّ
    /// تُعتَمَد.</summary>
    public const string AdjustmentUpdated = "adjustment.updated";

    public static readonly IReadOnlyList<string> All = new[]
    {
        TransactionCompleted,
        SubscriptionCreated, SubscriptionUpdated, SubscriptionCanceled,
        AdjustmentCreated, AdjustmentUpdated
    };

    public static bool Handles(string? t)
        => t is not null && All.Contains(t, StringComparer.Ordinal);

    public static bool IsSubscription(string? t)
        => t is SubscriptionCreated or SubscriptionUpdated or SubscriptionCanceled;

    public static bool IsAdjustment(string? t)
        => t is AdjustmentCreated or AdjustmentUpdated;
}

/// <summary>
/// <para><b>القيَمُ الَّتي تَجعَل حَرَكَةَ المالِ واقِعَةً لا
/// دَعوى.</b> اسمُ الحَدَثِ دَعوى، والحَقلُ واقِعَة — نَفسُ قاعِدَةِ
/// مَسارِ PayPal حَرفاً.</para>
/// </summary>
public static class PaddleFieldValues
{
    /// <summary><c>data.status</c> عِندَ اكتِمالِ المُعامَلَة.</summary>
    public const string TransactionCompleted = "completed";

    /// <summary><c>data.action</c> لِتَسوِيَةِ استِرداد.</summary>
    public const string AdjustmentRefund = "refund";

    /// <summary><c>data.action</c> لِرَدٍّ قَضائيّ — <b>مالٌ يَعودُ
    /// كَذلك</b>.</summary>
    public const string AdjustmentChargeback = "chargeback";

    /// <summary><c>data.status</c> لِتَسوِيَةٍ اعتُمِدَت — <b>وهي
    /// وَحدَها الَّتي تَعني وُصولَ الاستِرداد</b>. و«بِانتِظارِ
    /// المُوافَقَة» لا تَسحَبُ يَوماً.</summary>
    public const string AdjustmentApproved = "approved";

    /// <summary>أَيَعني هذا الفِعلُ عَودَةَ مال؟</summary>
    public static bool ReturnsMoney(string? action)
        => action is AdjustmentRefund or AdjustmentChargeback;
}

/// <summary>
/// <para><b>حَدَثُ Paddle مَقروءاً — وما لَم يوجَد يَبقى <c>null</c>
/// ولا يُخترَع.</b></para>
/// </summary>
/// <param name="EventId"><c>event_id</c> — <b>مِفتاحُ
/// مَرَّة-واحِدَة</b>.</param>
/// <param name="EventType"><c>event_type</c>.</param>
/// <param name="Reference">مَرجِعُنا — <c>data.custom_data.wasayel_ref</c>.
/// <b>وهُوَ ما وَضَعناهُ نَحنُ عِندَ الإنشاء</b>، ولا يُقرَأُ مِن
/// مَسارٍ ولا رَأس.</param>
/// <param name="TransactionId"><c>data.id</c> في أَحداثِ المُعامَلَة،
/// و<c>data.transaction_id</c> في أَحداثِ التَسوِيَة — <b>اسمانِ
/// مُختَلِفانِ لِشَيءٍ واحِد</b>، ولِذلك يُقرَآنِ مَعاً.</param>
/// <param name="SubscriptionId"><c>data.id</c> في أَحداثِ الاشتِراك،
/// و<c>data.subscription_id</c> في المُعامَلَة.</param>
/// <param name="Status"><c>data.status</c> — <b>شَرطٌ ثانٍ مُستَقِلٌّ
/// عَن اسمِ الحَدَث</b>.</param>
/// <param name="AmountMinor"><c>data.details.totals.grand_total</c> —
/// <b>ما يَدفَعُه الزَبونُ فِعلاً بَعدَ أَيِّ رَصيد</b>، بِأَصغَرِ
/// وَحدَةٍ نَصّاً.</param>
/// <param name="Currency"><c>data.currency_code</c>.</param>
/// <param name="AdjustmentAction"><c>data.action</c> في التَسوِيَة.</param>
public sealed record PaddleEvent(
    string  EventId,
    string  EventType,
    string? Reference,
    string? TransactionId,
    string? SubscriptionId,
    string? Status,
    string? AmountMinor,
    string? Currency,
    string? AdjustmentAction);

/// <summary>ما تَقَرَّرَ فِعلُه بِحَدَثِ Paddle — <b>مَعجَمٌ
/// مُغلَق</b>.</summary>
public enum PaddleAction
{
    /// <summary><b>هذا الحَدَثُ طُبِّقَ سَلَفاً — لا تَمديدَ ثانٍ.</b>
    /// وبِمِفتاحَينِ لا واحِد: نَفسُ <c>event_id</c> في سِجِلِّ
    /// مَرَّة-واحِدَة، <b>أَو</b> مُعامَلَةٌ بَلَغَت «وَصَلَ المال»
    /// فَوَصَلَها «مُكتَمِل» ثانٍ بِمُعَرِّفِ حَدَثٍ آخَر.</summary>
    Replay,

    /// <summary>نَوعٌ خارِجَ المَعجَم، أَو فِعلُ تَسوِيَةٍ لا يُعيدُ
    /// مالاً — لا كِتابَة.</summary>
    Ignored,

    /// <summary><b>مَرجِعٌ لا وَثيقَةَ مُعامَلَةٍ لَه ⇒ صِفرُ كِتابَةٍ
    /// وسَطرُ خَطَإ.</b> ولا تُخترَعُ لَه وَثيقَة: المَبلَغُ
    /// والمُدَّةُ والمَتجَرُ قَرارُ مُشرِفٍ لا يَعرِفُه
    /// Paddle.</summary>
    UnknownReference,

    /// <summary>لِلمَرجِعِ وَثيقَةُ مُعامَلَةٍ لكِنّ لا وَثيقَةَ باقَةٍ
    /// لِمَتجَرِه — لا كِتابَة.</summary>
    UnknownTenant,

    /// <summary><b>★ وَصَلَ المال.</b> يُمَدَّدُ <c>ExpiresAt</c>
    /// بِعَدَدِ الأَيّامِ المَحفوظِ في الوَثيقَة.</summary>
    Extend,

    /// <summary>عادَ المالُ — يُسحَبُ ما مُنِح.</summary>
    Withdraw,

    /// <summary>يُوقَفُ التَجديدُ التِلقائيّ — <b>ولا تُمَسُّ
    /// المُدَّةُ المَدفوعَة</b>.</summary>
    StopRenewal,

    /// <summary>تُعَلَّمُ وَثيقَةُ المُعامَلَةِ ولا تُمَسُّ
    /// الباقَة.</summary>
    MarkTransaction,

    /// <summary><b>اسمُ الحَدَثِ «اكتَمَلَت» و<c>data.status</c> يَقول
    /// غَيرَ ذلك</b> — لا تَمديد.</summary>
    StatusNotCompleted,

    /// <summary><b>المَبلَغُ أَو العُملَةُ لا يُطابِقانِ
    /// المَحفوظ</b> — لا تَمديد. ودَفعٌ بِمَبلَغٍ أَقَلَّ لا يَشتَري
    /// مُدَّةً كامِلَة.</summary>
    AmountMismatch
}

/// <summary>القَرارُ كامِلاً. <c>NewExpiresAt</c> ذاتُ مَعنىً عِندَ
/// <see cref="PaddleAction.Extend"/> و<see cref="PaddleAction.Withdraw"/>
/// وَحدَهُما.</summary>
public sealed record PaddleDecision(
    PaddleAction Action,
    DateTime NewExpiresAt,
    string TransactionStatus,
    string ReasonAr)
{
    /// <summary>أَتُحَرَّكُ باقَةٌ أَصلاً؟ <b>هذا هُوَ تَعريفُ «صِفرُ
    /// تَمديد»</b> الَّذي يَفحَصُه الاختِبار.</summary>
    public bool TouchesPlan => Action is PaddleAction.Extend
                                      or PaddleAction.Withdraw
                                      or PaddleAction.StopRenewal;

    /// <summary>أَتُعَلَّمُ وَثيقَةُ المُعامَلَة؟</summary>
    public bool TouchesTransaction => Action is PaddleAction.Extend
                                            or PaddleAction.Withdraw
                                            or PaddleAction.MarkTransaction;

    /// <summary>أَتُكتَبُ وَثيقَةٌ أَصلاً؟</summary>
    public bool Writes => TouchesPlan || TouchesTransaction;
}

/// <summary>
/// <para><b>كُلُّ قَرارِ فَوتَرَةِ Paddle — دَوالُّ نَقِيَّة.</b> لا
/// Marten، ولا HTTP، ولا <c>DateTime.UtcNow</c>: الوَقتُ يُمَرَّر.</para>
/// </summary>
public static class PaddleBillingPolicy
{
    // ─── القِراءَة ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>قِراءَةُ رِسالَةِ Paddle — تُعطي <c>null</c> ولا
    /// تَرمي.</b> و<c>null</c> تَعني «جِسمٌ مُشَوَّهٌ أَو بِلا
    /// <c>event_id</c>/<c>event_type</c>».</para>
    ///
    /// <para><b>ونَوعٌ خارِجَ المَعجَمِ يُقرَأُ ولا يُرَدُّ
    /// <c>null</c></b>، بِخِلافِ مَسارِ PayPal: هُناك نُقطَةٌ واحِدَةٌ
    /// تَخدِم مَعجَمَين فَـ<c>null</c> تَعني «لِلمَعجَمِ الآخَر»،
    /// وهُنا <b>لا مَعجَمَ ثانِيَ</b> — فَالتَجاهُلُ قَرارٌ يُقالُ في
    /// اللوغ بِاسمِ النَوع.</para>
    /// </summary>
    public static PaddleEvent? Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawJson); }
        catch { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var id   = Trim(Str(root, "event_id"));
            var type = Trim(Str(root, "event_type"));
            if (id is null || type is null) return null;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return new(id, type, null, null, null, null, null, null, null);

            var reference = CustomReference(data);
            var isSub     = PaddleEventTypes.IsSubscription(type);
            var isAdj     = PaddleEventTypes.IsAdjustment(type);

            // **مُعَرِّفُ المُعامَلَةِ يَسكُن في حَقلَينِ بِحَسَبِ نَوعِ
            // الحَدَث** — `data.id` في المُعامَلَة، و`data.transaction_id`
            // في التَسوِيَةِ والاشتِراك. وقِراءَةُ أَحَدِهِما وَحدَه
            // تَجعَل نِصفَ الأَحداثِ «مَرجِعاً مَجهولاً».
            var txn = isSub || isAdj
                ? Trim(Str(data, "transaction_id"))
                : Trim(Str(data, "id"));

            var sub = isSub
                ? Trim(Str(data, "id"))
                : Trim(Str(data, "subscription_id"));

            string? amount = null;
            if (data.TryGetProperty("details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("totals", out var totals)
                && totals.ValueKind == JsonValueKind.Object)
                amount = Trim(Str(totals, "grand_total"));

            return new(
                id, type, reference, txn, sub,
                Trim(Str(data, "status")),
                amount,
                Trim(Str(data, "currency_code")),
                Trim(Str(data, "action")));
        }
    }

    /// <summary>مَرجِعُنا مِن <c>custom_data</c> — <b>ومَفتاحُه
    /// واحِدٌ</b> يَكتُبُه مُنشِئُ الجِسمِ ويَقرَؤُه هذا
    /// السَطر.</summary>
    private static string? CustomReference(JsonElement data)
        => data.TryGetProperty("custom_data", out var custom)
           && custom.ValueKind == JsonValueKind.Object
            ? Trim(Str(custom, PaddleTransactionPolicy.ReferenceKey))
            : null;

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ─── القَرار ──────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>ماذا يُفعَل بِحَدَثٍ مُوَثَّق.</b></para>
    ///
    /// <para><b>ويُمَدِّدُ الباقَةَ حَدَثٌ واحِدٌ لا غَير</b>:
    /// <c>transaction.completed</c>. وبِسِتَّةِ شُروطٍ مُجتَمِعَة،
    /// كُلُّها <b>قَبلَ</b> أَيِّ كِتابَة:</para>
    /// <list type="number">
    ///   <item>البَوّابَةُ قَبِلَت (<c>PaddleWebhookGuard.Gate</c>) —
    ///   تُفحَص في النُقطَةِ قَبلَ أَن يَصِلَ الجِسمُ إلى هُنا.</item>
    ///   <item>نَوعُ الحَدَثِ هُوَ ذاك بِعَينِه.</item>
    ///   <item><c>data.status == "completed"</c> — <b>شَرطٌ ثانٍ
    ///   مُستَقِلٌّ عَن اسمِ الحَدَث</b>.</item>
    ///   <item><b>المُعامَلَةُ لَم تَبلُغ «وَصَلَ المال» بَعد</b> —
    ///   مُعامَلَةٌ واحِدَةٌ = تَمديدٌ واحِد، ولَو تَبَدَّلَ
    ///   <c>event_id</c>.</item>
    ///   <item>العُملَةُ والمَبلَغُ يُطابِقانِ المَحفوظ —
    ///   <b>يُتَحَقَّقانِ ولا يُفتَرَضان</b>.</item>
    ///   <item>المَرجِعُ يُقابِل وَثيقَةَ مُعامَلَةٍ ووَثيقَةَ باقَةٍ
    ///   <b>قائِمَتَين</b>، و<c>event_id</c> غَيرُ مُسَجَّلٍ
    ///   سَلَفاً.</item>
    /// </list>
    ///
    /// <para><b>ولا شَيءَ آخَرَ يُمَدِّد. البَتَّة.</b> ولا تُمَسُّ
    /// <c>TenantPlan.Status</c> في أَيّ فَرع: إيقافُ المُشرِفِ
    /// اليَدَوِيُّ يَبقى فَوقَ كُلِّ دَفعَة.</para>
    /// </summary>
    public static PaddleDecision Decide(
        PaddleEvent e, PaddleTransactionRecord? record, TenantPlan? plan,
        bool alreadySeen, DateTime now)
    {
        if (alreadySeen)
            return new(PaddleAction.Replay, default, "",
                $"الحَدَث «{e.EventId}» عولِجَ سابِقاً — لا تَمديدَ ثانٍ.");

        if (!PaddleEventTypes.Handles(e.EventType))
            return new(PaddleAction.Ignored, default, "",
                $"نَوعُ الحَدَث «{e.EventType}» خارِجَ مَعجَمِ Paddle عِندَنا — لا فِعل.");

        if (record is null)
            return new(PaddleAction.UnknownReference, default, "",
                $"المَرجِع «{e.Reference ?? "—"}» بِلا وَثيقَةِ مُعامَلَة — لا كِتابَة. " +
                "يُنشِئُها المُشرِفُ مِن /admin ثُمَّ تُعادُ الرِسالَة.");

        // ─── الاشتِراك: حالَةٌ لا مال ────────────────────────────────
        if (PaddleEventTypes.IsSubscription(e.EventType))
        {
            if (!string.Equals(e.EventType, PaddleEventTypes.SubscriptionCanceled, StringComparison.Ordinal))
                return new(PaddleAction.MarkTransaction, default, record.Status,
                    $"‏{e.EventType} — يُسَجَّلُ الاشتِراك «{e.SubscriptionId ?? "—"}» ولا تُمَسُّ الباقَة.");

            if (plan is null)
                return new(PaddleAction.UnknownTenant, default, "",
                    $"المَتجَر «{record.TenantSlug}» بِلا وَثيقَةِ باقَة — لا كِتابَة.");

            return new(PaddleAction.StopRenewal, plan.ExpiresAt, record.Status,
                $"‏{e.EventType} — يُوقَف التَجديد، وتَبقى المُدَّةُ المَدفوعَةُ " +
                $"إلى {plan.ExpiresAt:yyyy-MM-dd} كَما هي.");
        }

        // ─── ★ الحَدَثُ الَّذي يُمَدِّد ───────────────────────────────
        if (string.Equals(e.EventType, PaddleEventTypes.TransactionCompleted, StringComparison.Ordinal))
        {
            if (!string.Equals(e.Status, PaddleFieldValues.TransactionCompleted, StringComparison.Ordinal))
                return new(PaddleAction.StatusNotCompleted, default, "",
                    $"اسمُ الحَدَثِ «اكتَمَلَت» و data.status «{e.Status ?? "—"}» — " +
                    "الحَقلُ واقِعَةٌ والاسمُ دَعوى. لا تَمديد.");

            // **ولا يُمنَحُ ما رُدَّ**: مُعامَلَةٌ بَلَغَت حالَةً
            // نِهائِيَّةً عادَ مالُها وسُحِبَت أَيّامُها، و«اكتَمَلَت»
            // تَصِلُ بَعدَها واقِعَةٌ مُمكِنَةٌ — كُلُّ رَدٍّ غَيرِ ‏2xx
            // يَجعَل Paddle تُعيد الإرسال، فَيَصِلُ التَأكيدُ بَعدَ
            // الاستِرداد.
            if (!PaddleTransactionStatuses.CanTransition(
                    record.Status, PaddleTransactionStatuses.Completed))
                return new(PaddleAction.MarkTransaction, default, record.Status,
                    $"وَصَلَ «اكتَمَلَت» على مُعامَلَةٍ حالَتُها «{record.Status}» — " +
                    "المالُ حُسِمَ سَلَفاً، فَلا يُمنَحُ مَرَّتَين. تُعَلَّمُ ولا تُمَدَّد.");

            // **ومُعامَلَةٌ واحِدَةٌ = تَمديدٌ واحِد.** الجَدوَلُ
            // أَعلاهُ لا يَحرُس هذا: فَرعُ «نَفسِ الحالَة» يَسبِقُه،
            // فَـ`CanTransition(completed, completed)` تُرجِع `true`.
            // ورِسالَةٌ ثانِيَةٌ **بِمُعَرِّفِ حَدَثٍ آخَر** تَتَخَطّى
            // سِجِلَّ مَرَّة-واحِدَة، فَتَمُرُّ وتُمَدِّدُ ثانِيَة.
            if (string.Equals(record.Status, PaddleTransactionStatuses.Completed, StringComparison.Ordinal))
                return new(PaddleAction.Replay, default, "",
                    $"المُعامَلَة «{record.Id}» بَلَغَت «وَصَلَ المال» سَلَفاً " +
                    $"بِـ«{record.TransactionId}» — لا تَمديدَ ثانٍ لِدَفعَةٍ واحِدَة.");

            if (!MoneyMatches(e, record))
                return new(PaddleAction.AmountMismatch, default, "",
                    $"المَبلَغُ الواصِل «{e.AmountMinor ?? "—"} {e.Currency ?? "—"}» " +
                    $"لا يُطابِق المَحفوظ «{record.AmountMinor} {record.Currency}» — لا تَمديد.");

            if (plan is null)
                return new(PaddleAction.UnknownTenant, default, "",
                    $"المَتجَر «{record.TenantSlug}» بِلا وَثيقَةِ باقَة — لا كِتابَة. " +
                    "يَضبُطُها المُشرِفُ مَرَّةً مِن /admin ثُمَّ تُعادُ الرِسالَة.");

            // **والمِرساةُ `max(الآن, ExpiresAt)`**: مَن جَدَّدَ
            // مُبَكِّراً لا يُصادَر ما تَبَقّى لَه، ومَن عادَ بَعدَ
            // انقِطاعٍ لا يُشتَرى لَه ماضٍ مَضى.
            var anchor = now > plan.ExpiresAt ? now : plan.ExpiresAt;
            return new(PaddleAction.Extend,
                anchor.AddDays(record.Days), PaddleTransactionStatuses.Completed,
                $"وَصَلَ المال ({record.AmountMinor} {record.Currency} بِأَصغَرِ وَحدَة) — " +
                $"‏{record.Days} يَوماً تُضاف إلى {anchor:yyyy-MM-dd}.");
        }

        // ─── التَسوِيَة: يُسحَبُ ما مُنِح ─────────────────────────────
        if (PaddleEventTypes.IsAdjustment(e.EventType))
        {
            if (!PaddleFieldValues.ReturnsMoney(e.AdjustmentAction))
                return new(PaddleAction.Ignored, default, "",
                    $"تَسوِيَةٌ فِعلُها «{e.AdjustmentAction ?? "—"}» — لا يُعيدُ مالاً، لا فِعل.");

            if (!string.Equals(e.Status, PaddleFieldValues.AdjustmentApproved, StringComparison.Ordinal))
                return new(PaddleAction.Ignored, default, "",
                    $"تَسوِيَةُ «{e.AdjustmentAction}» حالَتُها «{e.Status ?? "—"}» ولَيسَت " +
                    $"«{PaddleFieldValues.AdjustmentApproved}» — لا يُسحَبُ قَبلَ الاعتِماد.");

            // **ولا يُسحَبُ ما لَم يُمنَح**: مُعامَلَةٌ لَم تَبلُغ
            // «وَصَلَ المال» لَم تُحَرِّك تاريخاً، فَسَحبُها يُصادِر
            // مُدَّةً اشتُرِيَت بِمُعامَلَةٍ أُخرى.
            if (!string.Equals(record.Status, PaddleTransactionStatuses.Completed, StringComparison.Ordinal))
                return new(PaddleAction.MarkTransaction, default, PaddleTransactionStatuses.Refunded,
                    $"‏{e.EventType} على مُعامَلَةٍ حالَتُها «{record.Status}» — " +
                    "تُعَلَّمُ ولا يُسحَبُ ما لَم يُمنَح.");

            // **وغِيابُ وَثيقَةِ الباقَةِ نَقصٌ عِندَنا لا حُكمٌ على
            // المُعامَلَة** — فَيُفصَل عَن حارِسِ السَحبِ ويُسَمّى
            // بِاسمِه: صِفرُ كِتابَةٍ ورَدٌّ تَشفيهِ الإعادَة. ولَو
            // طُوِيَ في الحارِسِ لَكُتِبَت «مُستَرَدّ» على مُعامَلَةٍ
            // **مَقبوضَة** فَتُقفَل نافِذَةُ الإعادَةِ الَّتي كانَت
            // سَتَسحَب.
            if (plan is null)
                return new(PaddleAction.UnknownTenant, default, "",
                    $"المَتجَر «{record.TenantSlug}» بِلا وَثيقَةِ باقَة — لا كِتابَة. " +
                    "يَضبُطُها المُشرِفُ مَرَّةً ثُمَّ تُعادُ الرِسالَةُ فَيُسحَب.");

            // **والسَحبُ كامِلٌ ولَو كانَ الاستِردادُ جُزئِيّاً، ويُقالُ
            // لِماذا**: المُدَّةُ لا تُباعُ بِالتَجزِئَة — نِصفُ مالٍ
            // مُستَرَدٍّ لا يَشتَري نِصفَ شَهر، والمِقدارُ المَسحوبُ
            // هُوَ **بِعَينِه** ما أَضافَتهُ الدَفعَةُ نَفسُها.
            return new(PaddleAction.Withdraw,
                plan.ExpiresAt.AddDays(-record.Days), PaddleTransactionStatuses.Refunded,
                $"‏{e.EventType} ({e.AdjustmentAction}) — تُسحَبُ {record.Days} يَوماً " +
                "مَنَحَتها هذِه الدَفعَة.");
        }

        return new(PaddleAction.Ignored, default, "",
            $"نَوعُ الحَدَث «{e.EventType}» بِلا فِعلٍ مُعَرَّف.");
    }

    /// <summary><b>يُتَحَقَّق ولا يُفتَرَض</b>: العُملَةُ حَرفاً
    /// والمَبلَغُ بِأَصغَرِ وَحدَةٍ عَدَداً صَحيحاً. <b>والمُقارَنَةُ
    /// بِما أُرسِلَ لا بِما كُتِبَ في الشاشَة</b> — تَعريفٌ واحِدٌ لا
    /// اثنان. ونَصٌّ غَيرُ مَقروءٍ <b>عَدَمُ تَطابُقٍ لا
    /// تَساهُل</b>.</summary>
    public static bool MoneyMatches(PaddleEvent e, PaddleTransactionRecord record)
        => e.Currency is { Length: > 0 } cur
           && string.Equals(cur, record.Currency, StringComparison.OrdinalIgnoreCase)
           && long.TryParse(e.AmountMinor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var got)
           && long.TryParse(record.AmountMinor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var want)
           && got == want;

    // ─── الثابِتُ الأَخير — مَوضِعٌ واحِدٌ يَمُرُّ بِه كُلُّ كاتِب ─────

    /// <summary>
    /// <para><b>أَتُكتَبُ هذِه الحالَةُ بِهذا الفِعل؟ — جَدوَلُ
    /// الانتِقالاتِ زائِداً شَرطٌ واحِدٌ لا يَعرِفُه الجَدوَل.</b></para>
    ///
    /// <para><b>الثابِت</b>: <c>record.Status == completed</c> لَحظَةَ
    /// وُصولِ التَسوِيَة. عَلَيه وَحدَه يَقوم حارِسُ السَحب، فَكُلُّ
    /// فَرعٍ يُخرِج المُعامَلَةَ مِن <c>completed</c> <b>بِلا سَحبِ
    /// يَوم</b> يُغلِقُ بابَ السَحبِ إلى الأَبَد: <b>المالُ يَعودُ
    /// والأَيّامُ تَبقى</b>.</para>
    ///
    /// <para><b>ولِماذا لا يُغلِقُه جَدوَلُ الانتِقالات</b>: الجَدوَلُ
    /// يَعرِف الحالَتَينِ ولا يَعرِف الفِعل، و<c>completed → refunded</c>
    /// <b>مَسموحٌ عَمداً</b> لِأَنَّه انتِقالُ السَحبِ نَفسُه. فَالحُكمُ
    /// لَيسَ «إلى أَيِّ حالَة» بَل <b>«بِأَيِّ فِعل»</b>. نَفسُ
    /// حُجَّةِ ‏ADR-007 حَرفاً، ومَنقولَةٌ عَمداً لِأَنّ الكِتَّينِ لا
    /// يَعتَمِد أَحَدُهُما على الآخَر.</para>
    /// </summary>
    public static bool MayWriteStatus(string? from, string? to, PaddleAction action)
    {
        if (!PaddleTransactionStatuses.CanTransition(from, to)) return false;

        var leavesCompleted =
            string.Equals((from ?? "").Trim(), PaddleTransactionStatuses.Completed, StringComparison.Ordinal)
            && !string.Equals((to ?? "").Trim(), PaddleTransactionStatuses.Completed, StringComparison.Ordinal);

        return !leavesCompleted || action == PaddleAction.Withdraw;
    }

    // ─── الأَثَرُ على وَثيقَةِ المُعامَلَة — دالَّةٌ نَقِيَّةٌ أَيضاً ──

    /// <summary>
    /// <para><b>تُطَبَّقُ نَتيجَةُ القَرارِ على وَثيقَةِ المُعامَلَةِ
    /// ولا شَيءَ آخَر.</b> لا جَلسَةَ هُنا ولا إيداع — الخِدمَةُ في
    /// القالِبِ هي الَّتي تُخَزِّن.</para>
    ///
    /// <para><b>والمُعَرِّفاتُ تُملَأُ ولا يُكتَبُ فَوقَها</b>: أَوَّلُ
    /// مَن يَعرِفُها يُثَبِّتُها.</para>
    /// </summary>
    public static void Apply(
        PaddleTransactionRecord record, PaddleEvent e, PaddleDecision decision, DateTime at)
    {
        if (!decision.TouchesTransaction) return;

        if (MayWriteStatus(record.Status, decision.TransactionStatus, decision.Action))
            record.Status = decision.TransactionStatus;

        if (string.IsNullOrWhiteSpace(record.TransactionId) && e.TransactionId is { Length: > 0 } txn)
            record.TransactionId = txn;
        if (string.IsNullOrWhiteSpace(record.SubscriptionId) && e.SubscriptionId is { Length: > 0 } sub)
            record.SubscriptionId = sub;

        record.ProviderStatus = e.Status;
        record.At = at;
    }
}
