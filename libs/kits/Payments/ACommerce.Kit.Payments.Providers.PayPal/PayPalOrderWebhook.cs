using System.Globalization;
using System.Text.Json;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.PayPal;

// ═══ رِسالَةُ طَلَبِ الدَفع — تُتَحَقَّق ثُمَّ تُقرَأ، لا العَكس ═══════
//
// **البابُ نَفسُه لا بابٌ ثانٍ**: `PayPalBillingPolicy.Gate` هي الحارِسُ
// هُنا كَما هي هُناك — رابِطٌ واحِدٌ ومُعَرِّفُ Webhook واحِد،
// والمُتَحَقِّقُ يَخدِمُ النَمَطَينِ بِلا تَفريع (‏`webhook_event`
// مُعَرَّفٌ في المُخَطَّطِ بِـ`resource: {type:object,
// additionalProperties:true}` — أَيُّ كائِنٍ كان).
//
// **وما يَتَبَدَّلُ هُوَ المَعجَمُ ومَسارُ الحُقول**، ولِذلك مِلَفٌّ
// ثانٍ ومَعجَمٌ ثانٍ: `PayPalEventTypes.All` يَبقى **أَربَعَةً**
// بِحَرفِه (تَقرَؤُه `docs/DEPLOY.md` §٢·ج و`PayPalRouteTests`)، ولا
// يُخلَط بِأَحداثِ الطَلَبات.

/// <summary>
/// <para><b>أَحداثُ مَسارِ الطَلَبات — مَعجَمٌ مُغلَق.</b> وما سِواها
/// يُتَجاهَل صَراحَةً، لا يُبتلَع صامِتاً.</para>
///
/// <para><b>و<c>CHECKOUT.ORDER.COMPLETED</c> غائِبٌ عَمداً</b>: وَصفُه
/// الرَسميّ «‏For use by marketplaces and platforms only» — لا يُشترَك
/// فيه ولَن يَصِل.</para>
/// </summary>
public static class PayPalOrderEventTypes
{
    /// <summary><b>مُوافَقَةٌ لا مال.</b> نَصُّ PayPal: «‏Listen for this
    /// webhook and <b>then capture the payment</b>». وهي إشارَةُ «التَقِط
    /// الآن» لا إثباتَ وُصول.</summary>
    public const string OrderApproved = "CHECKOUT.ORDER.APPROVED";

    /// <summary><b>★ الحَدَثُ الوَحيدُ الَّذي يُمَدِّد.</b></summary>
    public const string CaptureCompleted = "PAYMENT.CAPTURE.COMPLETED";

    /// <summary><b>مَمنوعٌ التَمديدُ بِنَصٍّ صَريح</b>: «‏Do not fulfill
    /// the order until payment completion is successful».</summary>
    public const string CapturePending = "PAYMENT.CAPTURE.PENDING";

    /// <summary>رُفِضَ الالتِقاط — يُعلَّمُ الطَلَبُ ويُبلَغُ المُشرِف،
    /// <b>ولا مَساسَ بِالباقَة</b> (لَم يَقَع تَمديدٌ أَصلاً).</summary>
    public const string CaptureDenied = "PAYMENT.CAPTURE.DENIED";

    /// <summary>اُستُرِدَّ المال.</summary>
    public const string CaptureRefunded = "PAYMENT.CAPTURE.REFUNDED";

    /// <summary>عُكِسَت الدَفعَة — نِزاعٌ أَو احتِيال.</summary>
    public const string CaptureReversed = "PAYMENT.CAPTURE.REVERSED";

    /// <summary><b>انقَضَت نافِذَةُ المُوافَقَةِ فَأَعادَت PayPal المالَ
    /// لِلمُشتَري.</b> وهذا هُوَ <b>اتِّجاهُ الفَشَلِ الصَحيح</b> حينَ لا
    /// تَصِلُ أَحداثٌ إطلاقاً: لا مالَ ضائِع، وشِراءٌ فاشِلٌ نَعرِف
    /// بِه.</summary>
    public const string ApprovalReversed = "CHECKOUT.PAYMENT-APPROVAL.REVERSED";

    /// <summary>السَبعَةُ الَّتي يُسَجِّلُها المالِكُ في لَوحَةِ PayPal
    /// لِمَسارِ الطَلَبات — <c>docs/DEPLOY.md</c> §٢·د يَقرَأُ
    /// مِنها.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        OrderApproved, CaptureCompleted, CapturePending,
        CaptureDenied, CaptureRefunded, CaptureReversed, ApprovalReversed
    };

    public static bool Handles(string? t)
        => t is not null && All.Contains(t, StringComparer.Ordinal);

    /// <summary>الحَدَثُ الَّذي يُوجِبُ نِداءَ <c>/capture</c>.</summary>
    public static bool TriggersCapture(string? t)
        => string.Equals(t, OrderApproved, StringComparison.Ordinal);

    /// <summary>ما يَسحَب المالَ بَعدَ وُصولِه.</summary>
    public static bool Withdraws(string? t)
        => t is CaptureRefunded or CaptureReversed;
}

/// <summary>
/// <para><b>حَدَثُ طَلَبٍ مَقروءٌ — وما لَم يوجَد يَبقى <c>null</c> ولا
/// يُخترَع.</b></para>
///
/// <para><b>ومَسارُ <c>custom_id</c> يَتَبَدَّلُ بَينَ نَوعَي
/// الحَدَث</b>، وهذا ما يَكسِرُ الشيفرَةَ إن نُسِخَ المَسار: أَحداثُ
/// <b>شَكلِ الطَلَب</b> تَحمِلُه في <c>resource.purchase_units[0]</c>،
/// وأَحداثُ <b>الالتِقاط</b> تَحمِلُه في <b>الجَذرِ مُباشَرَةً</b>.</para>
/// </summary>
/// <param name="EventId">‏<c>id</c> — مِفتاحُ مَرَّة-واحِدَة.</param>
/// <param name="EventType">‏<c>event_type</c>.</param>
/// <param name="Reference">مَرجِعُنا — <c>custom_id</c>. <b>وهُوَ ما
/// وَضَعناهُ نَحنُ عِندَ الإنشاء</b>، ولا يُقرَأُ مِن مَسارٍ ولا
/// رَأس.</param>
/// <param name="OrderId">مُعَرِّفُ الطَلَب: <c>resource.id</c> في
/// <c>ORDER.APPROVED</c>، و<c>resource.order_id</c> في
/// <c>PAYMENT-APPROVAL.REVERSED</c> (<b>اسمٌ مُختَلِف</b>)، و
/// <c>resource.supplementary_data.related_ids.order_id</c> في
/// الالتِقاط.</param>
/// <param name="CaptureId">مُعَرِّفُ الالتِقاط — <c>resource.id</c> في
/// أَحداثِ الالتِقاط.</param>
/// <param name="UpCaptureId">مُعَرِّفُ الالتِقاطِ مِن
/// <c>links[rel="up"]</c> — <b>وهُوَ المِفتاحُ الوَحيدُ الصالِحُ في
/// الاسترداد والعَكس</b>: مَورِدُهُما كائِنُ Refund لا Capture،
/// و<c>custom_id</c> فيهِما هُوَ ما أُرسِلَ في طَلَبِ الاسترداد —
/// وفي <c>REVERSED</c> تَكون PayPal هي البادِئَة فَلَم نُرسِلهُ
/// قَطّ.</param>
/// <param name="ResourceStatus">‏<c>resource.status</c> — <b>شَرطٌ ثانٍ
/// مُستَقِلٌّ عَن اسمِ الحَدَث</b>. اسمُ الحَدَثِ دَعوى، والحَقلُ
/// واقِعَة.</param>
/// <param name="StatusReason">‏<c>status_details.reason</c> — يُعرَض ولا
/// يُقَرِّر.</param>
/// <param name="Amount">‏<c>resource.amount.value</c> كَما وَصَل نَصّاً.</param>
/// <param name="Currency">‏<c>resource.amount.currency_code</c>.</param>
/// <param name="NetAmount">‏<c>seller_receivable_breakdown.net_amount</c>
/// — <b>صافي ما يَصِل الحِسابَ بَعدَ الرُسوم، ولا يُشتَقُّ مِن
/// <c>amount</c> بِأَيّ حِسابٍ مَحَلِّيّ</b>. ووُجودُه عَلامَةٌ إضافِيَّةٌ
/// على أَنّ المالَ تَحَرَّك.</param>
public sealed record PayPalOrderEvent(
    string  EventId,
    string  EventType,
    string? Reference,
    string? OrderId,
    string? CaptureId,
    string? UpCaptureId,
    string? ResourceStatus,
    string? StatusReason,
    string? Amount,
    string? Currency,
    string? NetAmount);

/// <summary>ما تَقَرَّرَ فِعلُه بِحَدَثِ طَلَب — <b>مَعجَمٌ
/// مُغلَق</b>.</summary>
public enum PayPalOrderAction
{
    /// <summary><b>هذِه الدَفعَةُ طُبِّقَت سَلَفاً — لا تَمديدَ ثانٍ.</b>
    /// وبِمِفتاحَين لا واحِد: نَفسُ <c>event_id</c> في سِجِلِّ
    /// مَرَّة-واحِدَة، <b>أَو</b> طَلَبٌ بَلَغَ <c>captured</c> فَوَصَلَه
    /// «مُكتَمِل» ثانٍ بِمُعَرِّفِ حَدَثٍ آخَر — <b>وطَلَبٌ واحِدٌ =
    /// التِقاطٌ واحِد</b>.</summary>
    Replay,

    /// <summary>نَوعٌ خارِجَ المَعجَم — لا كِتابَة.</summary>
    Ignored,

    /// <summary><b>مَرجِعٌ لا وَثيقَةَ دَفعٍ مُعَلَّقٍ لَه ⇒ صِفرُ
    /// كِتابَةٍ وسَطرُ خَطَإ.</b> ولا تُخترَعُ لَه وَثيقَة: المَبلَغُ
    /// والمُدَّةُ والمَتجَرُ قَرارُ مُشرِفٍ لا يَعرِفُه PayPal
    /// (القاعِدَة ١٦).</summary>
    UnknownReference,

    /// <summary>وافَقَ الدافِع — <b>يُنادى <c>/capture</c> ولا
    /// يُمَدَّدُ شَيء</b>.</summary>
    Capture,

    /// <summary><b>★ وَصَلَ المال.</b> يُمَدَّدُ <c>ExpiresAt</c>
    /// بِعَدَدِ الأَيّامِ المَحفوظِ في وَثيقَةِ الدَفع.</summary>
    Extend,

    /// <summary>اُستُرِدَّ المالُ أَو عُكِسَ — يُسحَبُ ما مُنِح.</summary>
    Withdraw,

    /// <summary>تُعَلَّمُ وَثيقَةُ الطَلَبِ ولا تُمَسُّ الباقَة —
    /// مُعَلَّقٌ، أَو مَرفوض، أَو انقَضَت مُوافَقَتُه.</summary>
    MarkOrder,

    /// <summary><b>الحَدَثُ اسمُه «مُكتَمِل» و<c>resource.status</c>
    /// يَقول غَيرَ ذلك</b> — لا تَمديد. اسمُ الحَدَثِ دَعوى، والحَقلُ
    /// واقِعَة.</summary>
    StatusNotCompleted,

    /// <summary><b>المَبلَغُ أَو العُملَةُ لا يُطابِقانِ المَحفوظ</b> —
    /// لا تَمديد. ودَفعٌ بِمَبلَغٍ أَقَلَّ لا يَشتَري مُدَّةً كامِلَة.</summary>
    AmountMismatch,

    /// <summary>لِلمَرجِعِ وَثيقَةُ دَفعٍ لكِنّ لا وَثيقَةَ باقَةٍ
    /// لِمَتجَرِه — لا كِتابَة.</summary>
    UnknownTenant
}

/// <summary>القَرارُ كامِلاً. <c>NewExpiresAt</c> ذاتُ مَعنىً عِندَ
/// <see cref="PayPalOrderAction.Extend"/> و<see cref="PayPalOrderAction.Withdraw"/>
/// وَحدَهُما، و<c>OrderStatus</c> عِندَ كُلِّ ما يَلمِسُ الوَثيقَة.</summary>
public sealed record PayPalOrderDecision(
    PayPalOrderAction Action,
    DateTime NewExpiresAt,
    string OrderStatus,
    string ReasonAr)
{
    /// <summary>أَتُحَرَّكُ باقَةٌ أَصلاً؟ <b>هذا هُوَ تَعريفُ «صِفرُ
    /// تَمديد»</b> الَّذي يَفحَصُه الاختِبار.</summary>
    public bool TouchesPlan => Action is PayPalOrderAction.Extend or PayPalOrderAction.Withdraw;

    /// <summary>أَتُعَلَّمُ وَثيقَةُ الطَلَب؟</summary>
    public bool TouchesOrder => Action is PayPalOrderAction.Extend
                                       or PayPalOrderAction.Withdraw
                                       or PayPalOrderAction.MarkOrder;

    /// <summary>أَتُكتَبُ وَثيقَةٌ أَصلاً؟</summary>
    public bool Writes => TouchesPlan || TouchesOrder;
}

/// <summary>
/// <para><b>كُلُّ قَرارِ مَسارِ الطَلَبات — دَوالُّ نَقِيَّة.</b> لا
/// Marten، ولا HTTP، ولا <c>DateTime.UtcNow</c>: الوَقتُ يُمَرَّر.</para>
/// </summary>
public static class PayPalOrderBillingPolicy
{
    /// <summary>القيمَةُ الَّتي تَجعَل «وَصَلَ المال» واقِعَةً لا
    /// دَعوى.</summary>
    public const string CaptureCompletedStatus = "COMPLETED";

    // ─── القِراءَة ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>قِراءَةُ حَدَثِ طَلَبٍ — تُعطي <c>null</c> ولا تَرمي.</b>
    /// و<c>null</c> تَعني <b>«لَيسَ حَدَثَ طَلَب»</b> — إمّا جِسمٌ
    /// مُشَوَّه، وإمّا نَوعٌ خارِجَ مَعجَمِ الطَلَبات فَيُترَكُ
    /// لِمَسارِ الاشتِراكات. وهذا هُوَ سَطرُ التَفريعِ الوَحيدُ في
    /// النُقطَة.</para>
    /// </summary>
    public static PayPalOrderEvent? Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(rawJson); }
        catch { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var id   = Str(root, "id");
            var type = Str(root, "event_type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type)) return null;
            if (!PayPalOrderEventTypes.Handles(type)) return null;

            if (!root.TryGetProperty("resource", out var res) || res.ValueKind != JsonValueKind.Object)
                return new(id!, type!, null, null, null, null, null, null, null, null, null);

            var isCapture = !PayPalOrderEventTypes.TriggersCapture(type)
                            && !string.Equals(type, PayPalOrderEventTypes.ApprovalReversed,
                                              StringComparison.Ordinal);

            // ‏«شَكلُ الطَلَب» يَحمِلُ المَرجِعَ داخِلَ مَصفوفَة،
            // و«شَكلُ الالتِقاط» يَحمِلُه في الجَذر. ويُقرَآنِ مَعاً
            // لِأَنّ قِراءَةَ أَحَدِهِما وَحدَه تَجعَل نِصفَ الأَحداثِ
            // «مَرجِعاً مَجهولاً».
            var reference = Trim(Str(res, "custom_id")) ?? FirstUnitString(res, "custom_id");

            var orderId = isCapture
                ? RelatedOrderId(res)
                : Trim(Str(res, "id")) ?? Trim(Str(res, "order_id"));

            var captureId = isCapture ? Trim(Str(res, "id")) : null;

            string? amount = null, currency = null;
            if (res.TryGetProperty("amount", out var amt) && amt.ValueKind == JsonValueKind.Object)
            {
                amount   = Trim(Str(amt, "value"));
                currency = Trim(Str(amt, "currency_code"));
            }

            string? net = null;
            if (res.TryGetProperty("seller_receivable_breakdown", out var srb)
                && srb.ValueKind == JsonValueKind.Object
                && srb.TryGetProperty("net_amount", out var na)
                && na.ValueKind == JsonValueKind.Object)
                net = Trim(Str(na, "value"));

            string? reason = null;
            if (res.TryGetProperty("status_details", out var sd) && sd.ValueKind == JsonValueKind.Object)
                reason = Trim(Str(sd, "reason"));

            return new(
                id!, type!, reference, orderId, captureId,
                UpCaptureId(res), Trim(Str(res, "status")), reason,
                amount, currency, net);
        }
    }

    /// <summary>‏<c>links[rel="up"]</c> يُشير إلى مَورِدِ الالتِقاط،
    /// وآخِرُ مَقطَعٍ فيه هُوَ مُعَرِّفُه.</summary>
    private static string? UpCaptureId(JsonElement res)
    {
        if (!res.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var l in links.EnumerateArray())
        {
            if (l.ValueKind != JsonValueKind.Object) continue;
            if (!string.Equals(Str(l, "rel"), "up", StringComparison.OrdinalIgnoreCase)) continue;

            var href = Trim(Str(l, "href"));
            if (href is null) continue;

            var cut = href.LastIndexOf('/');
            var tail = cut >= 0 && cut < href.Length - 1 ? href[(cut + 1)..] : href;
            return string.IsNullOrWhiteSpace(tail) ? null : tail;
        }
        return null;
    }

    private static string? RelatedOrderId(JsonElement res)
        => res.TryGetProperty("supplementary_data", out var sup)
           && sup.ValueKind == JsonValueKind.Object
           && sup.TryGetProperty("related_ids", out var rel)
           && rel.ValueKind == JsonValueKind.Object
            ? Trim(Str(rel, "order_id"))
            : null;

    private static string? FirstUnitString(JsonElement res, string name)
    {
        if (!res.TryGetProperty("purchase_units", out var units)
            || units.ValueKind != JsonValueKind.Array) return null;

        foreach (var u in units.EnumerateArray())
        {
            if (u.ValueKind != JsonValueKind.Object) continue;
            var v = Trim(Str(u, name));
            if (v is not null) return v;
        }
        return null;
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ─── القَرار — خَمسَةُ شُروطٍ مُجتَمِعَةٍ قَبلَ أَيّ كِتابَة ───────

    /// <summary>
    /// <para><b>ماذا يُفعَل بِحَدَثِ طَلَبٍ مُوَثَّق.</b></para>
    ///
    /// <para><b>ويُمَدِّدُ الباقَةَ حَدَثٌ واحِدٌ لا غَير</b>:
    /// <c>PAYMENT.CAPTURE.COMPLETED</c>. وبِسِتَّةِ شُروطٍ مُجتَمِعَة،
    /// كُلُّها <b>قَبلَ</b> أَيِّ كِتابَة:</para>
    /// <list type="number">
    ///   <item>البَوّابَةُ قَبِلَت (‏<c>PayPalBillingPolicy.Gate</c>) —
    ///   تُفحَص في النُقطَةِ قَبلَ أَن يَصِلَ الجِسمُ إلى هُنا
    ///   أَصلاً.</item>
    ///   <item>نَوعُ الحَدَثِ هُوَ ذاك بِعَينِه.</item>
    ///   <item><c>resource.status == "COMPLETED"</c> — <b>شَرطٌ ثانٍ
    ///   مُستَقِلٌّ عَن اسمِ الحَدَث</b>.</item>
    ///   <item><b>الطَلَبُ لَم يَبلُغ <c>captured</c> بَعد</b> — طَلَبٌ
    ///   واحِدٌ = التِقاطٌ واحِدٌ = تَمديدٌ واحِد، ولَو تَبَدَّلَ
    ///   <c>event_id</c>.</item>
    ///   <item>العُملَةُ والمَبلَغُ يُطابِقانِ المَحفوظَ في وَثيقَةِ
    ///   الدَفع — <b>يُتَحَقَّقانِ ولا يُفتَرَضان</b>.</item>
    ///   <item>المَرجِعُ يُقابِل وَثيقَةَ دَفعٍ ووَثيقَةَ باقَةٍ
    ///   <b>قائِمَتَين</b>، و<c>event_id</c> غَيرُ مُسَجَّلٍ سَلَفاً.</item>
    /// </list>
    ///
    /// <para><b>ولا شَيءَ آخَرَ يُمَدِّد. البَتَّة.</b> ولا تُمَسُّ
    /// <c>Status</c> في أَيّ فَرع: إيقافُ المُشرِفِ اليَدَوِيُّ يَبقى
    /// فَوقَ كُلِّ دَفعَة.</para>
    ///
    /// <para><b>وثابِتٌ ثانٍ يَعبُرُ الفُروعَ كُلَّها</b>: لا يَخرُجُ
    /// طَلَبٌ مِن <c>captured</c> إلّا بِفِعلِ
    /// <see cref="PayPalOrderAction.Withdraw"/>. تَحرُسُه الفُروعُ
    /// عِندَ القَرار، وتَرُدُّ خَرقَه <see cref="MayWriteStatus"/>
    /// عِندَ الكِتابَة — <b>مَوضِعٌ واحِدٌ يَمُرُّ بِه الجَميع</b>.</para>
    /// </summary>
    public static PayPalOrderDecision Decide(
        PayPalOrderEvent e, PayPalOrderRecord? order, TenantPlan? plan,
        bool alreadySeen, DateTime now)
    {
        if (alreadySeen)
            return new(PayPalOrderAction.Replay, default, "",
                $"الحَدَث «{e.EventId}» عولِجَ سابِقاً — لا تَمديدَ ثانٍ.");

        if (!PayPalOrderEventTypes.Handles(e.EventType))
            return new(PayPalOrderAction.Ignored, default, "",
                $"نَوعُ الحَدَث «{e.EventType}» خارِجَ مَعجَمِ الطَلَبات — لا فِعل.");

        if (order is null)
            return new(PayPalOrderAction.UnknownReference, default, "",
                $"المَرجِع «{e.Reference ?? "—"}» بِلا وَثيقَةِ دَفعٍ مُعَلَّق — " +
                "لا كِتابَة. يُنشِئُها المُشرِفُ مِن /admin ثُمَّ تُعادُ الرِسالَة.");

        // ─── مُوافَقَةٌ لا مال — تُنادى /capture ولا يُمَدَّدُ شَيء ───
        if (PayPalOrderEventTypes.TriggersCapture(e.EventType))
            return new(PayPalOrderAction.Capture, default, PayPalOrderStatuses.Approved,
                $"وافَقَ الدافِعُ على الطَلَب «{order.OrderId}» — يُنادى الالتِقاط، ولا تَمديد.");

        // ─── انقَضَت نافِذَةُ المُوافَقَةِ فَأُعيدَ المالُ لِلمُشتَري ──
        //
        // **ولا يَنقُضُ حَدَثُ مُوافَقَةٍ دَفعَةً وَصَلَت**: مَعنى هذا
        // الحَدَثِ «انقَضَت النافِذَةُ **قَبلَ** الالتِقاط»، فَوُصولُه
        // على طَلَبٍ بَلَغَ `captured` يُناقِضُ نَفسَه — ولا يُصَدَّق.
        //
        // **والكُلفَةُ الَّتي كَتَبَت الشَرط — تَسَلسُلٌ مَقيس**: كانَ
        // الفَرعُ بِلا أَيِّ شَرطٍ على حالَةِ الطَلَب، فَيَكتُب
        // `reversed` على طَلَبٍ مَقبوضٍ **بِلا مَساسٍ بِالباقَة**؛ ثُمَّ
        // يَصِلُ `PAYMENT.CAPTURE.REFUNDED` بِمَبلَغٍ مُطابِقٍ فَيَجِدُ
        // حارِسَ السَحبِ يَشتَرِط `captured` — **فَلا يُسحَبُ شَيء:
        // المالُ يَعودُ والأَيّامُ تَبقى**. الباقَةُ `09-03 → 10-03`
        // ثُمَّ تَبقى `10-03`.
        //
        // **وجَدوَلُ الانتِقالاتِ عاجِزٌ عَن إغلاقِه**:
        // `captured → reversed` مَسموحٌ **عَمداً** لِأَنَّه انتِقالُ
        // السَحبِ نَفسُه. فَالشَرطُ هُنا على **الفِعل** لا على
        // الحالَتَين، وشَبَكَتُه الأَخيرَةُ في `MayWriteStatus`.
        if (string.Equals(e.EventType, PayPalOrderEventTypes.ApprovalReversed, StringComparison.Ordinal))
            return PayPalOrderStatuses.AwaitsCapture(order.Status)
                ? new(PayPalOrderAction.MarkOrder, default, PayPalOrderStatuses.Reversed,
                    "انقَضَت نافِذَةُ المُوافَقَةِ وأُعيدَ المالُ لِلمُشتَري — لا مَساسَ بِالباقَة.")
                : new(PayPalOrderAction.Ignored, default, "",
                    $"‏{PayPalOrderEventTypes.ApprovalReversed} على طَلَبٍ حالَتُه " +
                    $"«{order.Status}» — لا نافِذَةَ مُوافَقَةٍ تَنقَضي بَعدَ أَن حُسِمَ " +
                    "الطَلَب. لا كِتابَة، ويَبقى بابُ السَحبِ مَفتوحاً لِلاسترداد.");

        // ─── ★ الحَدَثُ الَّذي يُمَدِّد ─────────────────────────────────
        if (string.Equals(e.EventType, PayPalOrderEventTypes.CaptureCompleted, StringComparison.Ordinal))
        {
            if (!string.Equals(e.ResourceStatus, CaptureCompletedStatus, StringComparison.Ordinal))
                return new(PayPalOrderAction.StatusNotCompleted, default, "",
                    $"اسمُ الحَدَثِ «مُكتَمِل» و resource.status «{e.ResourceStatus ?? "—"}» — " +
                    "الحَقلُ واقِعَةٌ والاسمُ دَعوى. لا تَمديد.");

            // **ولا يُمنَحُ ما رُدَّ**: طَلَبٌ بَلَغَ حالَةً نِهائِيَّةً
            // (‏`reversed`) عادَ مالُه وسُحِبَت أَيّامُه. و«مُكتَمِل»
            // يَصِلُ بَعدَها واقِعَةٌ مُمكِنَةٌ لا نادِرَة: كُلُّ رَدٍّ
            // غَيرِ ‏2xx يَجعَل PayPal تُعيد الإرسالَ **حَتّى ثَلاثَةِ
            // أَيّام**، فَيَصِلُ التَأكيدُ بَعدَ الاستِرداد. والجَدوَلُ
            // نَفسُه يَحكُم هُنا وفي `Apply` — قاعِدَةٌ واحِدَةٌ في
            // مَوضِعٍ واحِد.
            if (!PayPalOrderStatuses.CanTransition(order.Status, PayPalOrderStatuses.Captured))
                return new(PayPalOrderAction.MarkOrder, default, order.Status,
                    $"وَصَلَ «مُكتَمِل» على طَلَبٍ حالَتُه «{order.Status}» — " +
                    "المالُ عادَ سَلَفاً، فَلا يُمنَحُ مَرَّتَين. يُعَلَّمُ ولا يُمَدَّد.");

            // **وطَلَبٌ واحِدٌ = التِقاطٌ واحِدٌ = تَمديدٌ واحِد.**
            // الجَدوَلُ أَعلاهُ لا يَحرُس هذا: فَرعُ «نَفسِ الحالَة»
            // يَسبِقُه، فَـ`CanTransition(captured, captured)` تُرجِع
            // `true`. ورِسالَةٌ ثانِيَةٌ **بِمُعَرِّفِ حَدَثٍ آخَر**
            // تَتَخَطّى سِجِلَّ مَرَّة-واحِدَة، فَتَمُرُّ وتُمَدِّد
            // ثانِيَةً — **مَقيس: ‏09-03 → 10-03 → 11-02** بِنَفسِ
            // مُعَرِّفِ الالتِقاطِ ونَفسِ المَبلَغ.
            //
            // **ولا مُطلِقَ مَعروفاً لَه اليَوم** — وهذا بِعَينِه
            // السَبَب: الثابِتُ كانَ مَفروضاً بِطَبَقَةٍ واحِدَةٍ هي
            // **وَصفُ PayPal لِسُلوكِها**، وطَبَقَةٌ واحِدَةٌ عِندَ
            // طَرَفٍ ثالِثٍ لَيسَت حِراسَة.
            //
            // **والفَيصَلُ الحالَةُ لا مُعَرِّفُ الالتِقاط — بِقِياس**:
            // «‏`CaptureId` مَملوءٌ ويُطابِقُ الواصِل» يَحجُبُ
            // **التَمديدَ الأَوَّلَ نَفسَه**، لِأَنّ
            // `PayPalOrderFlow.CaptureAsync` يُثَبِّتُ المُعَرِّفَ
            // ويَترُكُ الحالَةَ `approved`، ثُمَّ تَصِلُ رِسالَةُ
            // `COMPLETED` بِـ**نَفسِ** المُعَرِّف. و«قُبِض» كَلِمَةٌ لا
            // يَكتُبُها إلّا مَن مَدَّدَ فِعلاً — فَهي وَحدَها تَقول
            // «طُبِّقَ سَلَفاً».
            if (string.Equals(order.Status, PayPalOrderStatuses.Captured, StringComparison.Ordinal))
                return new(PayPalOrderAction.Replay, default, "",
                    $"الطَلَب «{order.Id}» بَلَغَ «قُبِض» سَلَفاً بِالالتِقاط " +
                    $"«{order.CaptureId ?? "—"}»، والحَدَث «{e.EventId}» يَحمِل " +
                    $"«{e.UpCaptureId ?? e.CaptureId ?? "—"}» — لا تَمديدَ ثانٍ لِدَفعَةٍ واحِدَة.");

            if (!MoneyMatches(e, order))
                return new(PayPalOrderAction.AmountMismatch, default, "",
                    $"المَبلَغُ الواصِل «{e.Amount ?? "—"} {e.Currency ?? "—"}» " +
                    $"لا يُطابِق المَحفوظ «{PayPalCurrencies.Money(order.Amount, order.Currency)} " +
                    $"{order.Currency}» — لا تَمديد.");

            if (plan is null)
                return new(PayPalOrderAction.UnknownTenant, default, "",
                    $"المَتجَر «{order.TenantSlug}» بِلا وَثيقَةِ باقَة — لا كِتابَة. " +
                    "يَضبُطُها المُشرِفُ مَرَّةً مِن /admin ثُمَّ تُعادُ الرِسالَة.");

            // **والمِرساةُ `max(الآن, ExpiresAt)`**: مَن جَدَّدَ مُبَكِّراً
            // لا يُصادَر ما تَبَقّى لَه، ومَن عادَ بَعدَ انقِطاعٍ لا
            // يُشتَرى لَه ماضٍ مَضى. نَفسُ مِرساةِ مَسارِ الاشتِراكات.
            var anchor = now > plan.ExpiresAt ? now : plan.ExpiresAt;
            return new(PayPalOrderAction.Extend,
                anchor.AddDays(order.Days), PayPalOrderStatuses.Captured,
                $"وَصَلَ المال ({PayPalCurrencies.Money(order.Amount, order.Currency)} {order.Currency}) — " +
                $"‏{order.Days} يَوماً تُضاف إلى {anchor:yyyy-MM-dd}.");
        }

        if (string.Equals(e.EventType, PayPalOrderEventTypes.CapturePending, StringComparison.Ordinal))
            return new(PayPalOrderAction.MarkOrder, default, PayPalOrderStatuses.Pending,
                $"الالتِقاطُ مُعَلَّق ({e.StatusReason ?? "—"}) — مَمنوعٌ التَمديدُ حَتّى يَكتَمِل.");

        if (string.Equals(e.EventType, PayPalOrderEventTypes.CaptureDenied, StringComparison.Ordinal))
            return new(PayPalOrderAction.MarkOrder, default, PayPalOrderStatuses.Denied,
                $"رُفِضَ الالتِقاط ({e.StatusReason ?? "—"}) — لا مَساسَ بِالباقَة، ولَم يَقَع تَمديدٌ أَصلاً.");

        // ─── الاسترداد والعَكس — يُسحَبُ ما مُنِح ──────────────────────
        if (PayPalOrderEventTypes.Withdraws(e.EventType))
        {
            // **ولا يُسحَبُ ما لَم يُمنَح**: طَلَبٌ لَم يَبلُغ
            // `captured` لَم يُحَرِّك تاريخاً، فَسَحبُه يُصادِر مُدَّةً
            // اشتُرِيَت بِطَلَبٍ آخَر.
            if (!string.Equals(order.Status, PayPalOrderStatuses.Captured, StringComparison.Ordinal))
                return new(PayPalOrderAction.MarkOrder, default, PayPalOrderStatuses.Reversed,
                    $"‏{e.EventType} على طَلَبٍ حالَتُه «{order.Status}» — يُعَلَّمُ ولا يُسحَبُ ما لَم يُمنَح.");

            // **وغِيابُ وَثيقَةِ الباقَةِ نَقصٌ عِندَنا لا حُكمٌ على
            // الطَلَب** — فَيُفصَل عَن حارِسِ السَحبِ ويُسَمّى بِاسمِه،
            // كَما يَفعَل فَرعُ التَمديدِ حَرفاً: **صِفرُ كِتابَةٍ
            // و‏503 تَشفيها الإعادَة**.
            //
            // **والكُلفَةُ الَّتي فَصَلَته**: كانَ مَطوِيّاً في الحارِسِ
            // (‏`|| plan is null`)، فَاستِردادٌ يَصِلُ ووَثيقَةُ
            // الباقَةِ غائِبَةٌ كانَ يَكتُب `reversed` على طَلَبٍ
            // **مَقبوض** ويَرُدُّ ‏200. فَتُقفَلُ نافِذَةُ الإعادَةِ
            // الَّتي كانَت سَتَسحَب، **ولا تَعودُ الوَثيقَةُ
            // `captured` أَبَداً**: عَطَبٌ ثالِثٌ بِنَفسِ الشَكل.
            if (plan is null)
                return new(PayPalOrderAction.UnknownTenant, default, "",
                    $"المَتجَر «{order.TenantSlug}» بِلا وَثيقَةِ باقَة — لا كِتابَة. " +
                    "يَضبُطُها المُشرِفُ مَرَّةً مِن /admin ثُمَّ تُعادُ الرِسالَة فَيُسحَب.");

            // **والسَحبُ كامِلٌ ولَو كانَ الاستِردادُ جُزئِيّاً، ويُقالُ
            // لِماذا**: المُدَّةُ لا تُباعُ بِالتَجزِئَة — نِصفُ مالٍ
            // مُستَرَدٍّ لا يَشتَري نِصفَ شَهر، والمِقدارُ المَسحوبُ هُوَ
            // **بِعَينِه** ما أَضافَتهُ الدَفعَةُ نَفسُها.
            return new(PayPalOrderAction.Withdraw,
                plan.ExpiresAt.AddDays(-order.Days), PayPalOrderStatuses.Reversed,
                $"‏{e.EventType} — تُسحَبُ {order.Days} يَوماً مَنَحَتها هذِه الدَفعَة.");
        }

        return new(PayPalOrderAction.Ignored, default, "",
            $"نَوعُ الحَدَث «{e.EventType}» بِلا فِعلٍ مُعَرَّف.");
    }

    /// <summary><b>يُتَحَقَّق ولا يُفتَرَض</b>: العُملَةُ حَرفاً
    /// والمَبلَغُ بِقيمَتِه العَدَدِيَّة. ونَصٌّ غَيرُ مَقروءٍ
    /// <b>عَدَمُ تَطابُقٍ لا تَساهُل</b>.</summary>
    public static bool MoneyMatches(PayPalOrderEvent e, PayPalOrderRecord order)
        => e.Currency is { Length: > 0 } cur
           && string.Equals(cur, order.Currency, StringComparison.OrdinalIgnoreCase)
           && decimal.TryParse(e.Amount, PayPalCurrencies.MoneyStyles, CultureInfo.InvariantCulture, out var v)
           && v == order.Amount;

    // ─── الثابِتُ الأَخير — مَوضِعٌ واحِدٌ يَمُرُّ بِه كُلُّ كاتِب ─────

    /// <summary>
    /// <para><b>أَتُكتَبُ هذِه الحالَةُ بِهذا الفِعل؟ — جَدوَلُ
    /// الانتِقالاتِ زائِداً شَرطٌ واحِدٌ لا يَعرِفُه الجَدوَل.</b></para>
    ///
    /// <para><b>الثابِت</b>: <c>order.Status == captured</c> لَحظَةَ
    /// وُصولِ الاستِرداد. عَلَيه وَحدَه يَقوم حارِسُ السَحب، فَكُلُّ
    /// فَرعٍ يُخرِج الطَلَبَ مِن <c>captured</c> <b>بِلا سَحبِ يَوم</b>
    /// يُغلِقُ بابَ السَحبِ إلى الأَبَد: <b>المالُ يَعودُ والأَيّامُ
    /// تَبقى</b>.</para>
    ///
    /// <para><b>ولِماذا لا يُغلِقُه جَدوَلُ الانتِقالات</b>: الجَدوَلُ
    /// يَعرِف الحالَتَينِ ولا يَعرِف الفِعل، و<c>captured → reversed</c>
    /// <b>مَسموحٌ عَمداً</b> لِأَنَّه انتِقالُ السَحبِ نَفسُه. فَالحُكمُ
    /// لَيسَ «إلى أَيِّ حالَة» بَل <b>«بِأَيِّ فِعل»</b>.</para>
    ///
    /// <para><b>وثَلاثَةُ كُتّابٍ خَرَقوهُ فِعلاً</b>، ولِذلك مَوضِعٌ
    /// واحِدٌ لا ثَلاثَةُ شُروطٍ مَنثورَة: فَرعُ
    /// <c>PAYMENT.CAPTURE.PENDING</c> المُتَأَخِّر (أَغلَقَهُ الجَدوَل)،
    /// وفَرعُ <c>CHECKOUT.PAYMENT-APPROVAL.REVERSED</c> بِلا شَرطٍ على
    /// الحالَة، و<c>plan is null</c> مَطوِيّاً في حارِسِ السَحب. وهذِه
    /// الدالَّةُ تَرُدُّ **الرابِعَ الَّذي لَم يُكتَب بَعد**.</para>
    /// </summary>
    public static bool MayWriteStatus(string? from, string? to, PayPalOrderAction action)
    {
        if (!PayPalOrderStatuses.CanTransition(from, to)) return false;

        var leavesCaptured =
            string.Equals((from ?? "").Trim(), PayPalOrderStatuses.Captured, StringComparison.Ordinal)
            && !string.Equals((to ?? "").Trim(), PayPalOrderStatuses.Captured, StringComparison.Ordinal);

        return !leavesCaptured || action == PayPalOrderAction.Withdraw;
    }

    // ─── الأَثَرُ على وَثيقَةِ الطَلَب — دالَّةٌ نَقِيَّةٌ أَيضاً ──────

    /// <summary>
    /// <para><b>تُطَبَّقُ نَتيجَةُ القَرارِ على وَثيقَةِ الطَلَبِ ولا
    /// شَيءَ آخَر.</b> لا جَلسَةَ هُنا ولا إيداع — الخِدمَةُ في القالِبِ
    /// هي الَّتي تُخَزِّن، كَجارَتِها في مَسارِ الاشتِراكات
    /// حَرفاً.</para>
    ///
    /// <para><b>و<c>CaptureId</c> يُملَأُ ولا يُكتَبُ فَوقَه</b>: أَوَّلُ
    /// مَن يَعرِفُه يُثَبِّتُه، وحَدَثُ الاستِردادِ اللاحِقُ يَصِل
    /// بِمُعَرِّفِ استِردادٍ لا التِقاط.</para>
    /// </summary>
    public static void Apply(
        PayPalOrderRecord order, PayPalOrderEvent e, PayPalOrderDecision decision, DateTime at)
    {
        if (!decision.TouchesOrder) return;

        // **والحالَةُ تُكتَبُ بِاتِّجاهٍ واحِد** — جَدوَلُ الانتِقالاتِ
        // في `PayPalOrderStatuses.CanTransition` هُوَ الحَكَم. وكانَ
        // هذا السَطرُ يَكتُب بِلا فَحص، فَحَدَثٌ مُتَأَخِّرٌ أَو
        // مُكَرَّرٌ يَهبِط بِالحالَةِ مِن `captured` إلى `pending` —
        // ثُمَّ يَصِلُ الاستِردادُ فَيَجِدُ حارِسَ السَحبِ يَشتَرِط
        // `captured`، **فَلا يُسحَبُ شَيء: المالُ يَعودُ والأَيّامُ
        // تَبقى**.
        if (MayWriteStatus(order.Status, decision.OrderStatus, decision.Action))
            order.Status = decision.OrderStatus;

        // **ومُعَرِّفُ الالتِقاطِ مِن `links[rel=up]` أَوَّلاً** — في
        // الاستِردادِ والعَكسِ يَكون `resource.id` مُعَرِّفَ
        // **الاستِرداد** لا الالتِقاط، فَكِتابَتُه في `CaptureId`
        // تَضَعُ في الحَقلِ مِفتاحاً لا يُطابِقُ شَيئاً — وهُوَ
        // المِفتاحُ الوَحيدُ الَّذي يَربِط دَورَةَ الحَياةِ كُلَّها.
        if (string.IsNullOrWhiteSpace(order.CaptureId)
            && (e.UpCaptureId ?? e.CaptureId) is { Length: > 0 } cap)
            order.CaptureId = cap;
        if (string.IsNullOrWhiteSpace(order.OrderId) && e.OrderId is { Length: > 0 } oid)
            order.OrderId = oid;
        order.NetAmount = e.NetAmount ?? order.NetAmount;
        order.StatusReason = e.StatusReason;
        order.At = at;
    }
}
