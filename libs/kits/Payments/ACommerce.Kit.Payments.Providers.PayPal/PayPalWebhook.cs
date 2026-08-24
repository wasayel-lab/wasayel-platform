using System.Text.Json;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.PayPal;

// ═══ رِسالَةُ PayPal — تُتَحَقَّقُ ثُمَّ تُقرَأ، لا العَكس ═════════════
//
// **التَرتيبُ هُوَ الأَمن**: جِسمُ الرِسالَةِ نَصٌّ يَكتُبُه **أَيُّ
// أَحَد** يَعرِف العُنوان. فَقِراءَتُه كَبَيانات — أَيّاً كانَ
// المَقروء، ولَو `event_type` وَحدَه لِلوغ — قَبلَ التَحَقُّق مِن
// التَوقيعِ تَجعَل المَجهولَ يُقَرِّرُ فَرعَ الكود. ولِذلك
// `PayPalWebhookGate` **دالَّةٌ نَقِيَّةٌ تُقاس بِجَدوَل**، ونَتيجَتُها
// شَرطٌ لِلقِراءَة لا نَصيحَةٌ بِجِوارِها.
//
// **وهذا هُوَ نَمَط `AuthChannelSelection` بِحَرفِه**: مُعجَمٌ مُغلَقٌ
// مِن الحالات، والغِيابُ إغلاقٌ لا افتِراض.

/// <summary>
/// رُؤوسُ التَوقيعِ الخَمسَة كَما تُرسِلُها PayPal.
/// <b>وأَسماؤُها ثَوابِتُ هُنا لا سَلاسِلُ في مَوضِعِ القِراءَة</b>:
/// رَأسٌ مَنسوخٌ بِخَطَإ حَرفٍ يُعطي تَوقيعاً فاشِلاً بِلا تَوضيح.
/// </summary>
public sealed record PayPalWebhookHeaders(
    string TransmissionId,
    string TransmissionTime,
    string CertUrl,
    string AuthAlgo,
    string TransmissionSig)
{
    public const string TransmissionIdHeader   = "paypal-transmission-id";
    public const string TransmissionTimeHeader = "paypal-transmission-time";
    public const string CertUrlHeader          = "paypal-cert-url";
    public const string AuthAlgoHeader         = "paypal-auth-algo";
    public const string TransmissionSigHeader  = "paypal-transmission-sig";

    /// <summary>كُلُّ الرُؤوسِ المَطلوبَة — لِيَقرَأَها المُنتِجُ
    /// والمُختَبِرُ مِن مَوضِعٍ واحِد.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        TransmissionIdHeader, TransmissionTimeHeader,
        CertUrlHeader, AuthAlgoHeader, TransmissionSigHeader
    };

    /// <summary>رَأسٌ ناقِصٌ واحِدٌ يَكفي لِيَستَحيلَ التَحَقُّق —
    /// فَالنُقصانُ يُقال، ولا يُرسَل طَلَبُ تَحَقُّقٍ ناقِصٌ لِيَرُدَّ
    /// PayPal رَفضاً غامِضاً.</summary>
    public bool IsComplete
        => !string.IsNullOrWhiteSpace(TransmissionId)
           && !string.IsNullOrWhiteSpace(TransmissionTime)
           && !string.IsNullOrWhiteSpace(CertUrl)
           && !string.IsNullOrWhiteSpace(AuthAlgo)
           && !string.IsNullOrWhiteSpace(TransmissionSig);

    public static readonly PayPalWebhookHeaders Empty = new("", "", "", "", "");
}

/// <summary>أَنواعُ الأَحداثِ الَّتي تَعنينا — <b>مَعجَمٌ مُغلَق</b>.
/// وما سِواها يُتَجاهَل صَراحَةً بِـ<see cref="PayPalBillingAction.Ignored"/>،
/// لا يُبتلَع صامِتاً.</summary>
public static class PayPalEventTypes
{
    /// <summary>الاشتِراكُ صارَ فَعّالاً — أَوَّلُ دَفعَةٍ نَجَحَت.</summary>
    public const string SubscriptionActivated = "BILLING.SUBSCRIPTION.ACTIVATED";

    /// <summary>دَفعَةٌ دَورِيَّةٌ نَجَحَت — التَجديدُ الشَهريّ.</summary>
    public const string PaymentSaleCompleted = "PAYMENT.SALE.COMPLETED";

    /// <summary>أَلغى الدافِعُ اشتِراكَه.</summary>
    public const string SubscriptionCancelled = "BILLING.SUBSCRIPTION.CANCELLED";

    /// <summary>عُلِّقَ الاشتِراك (فَشَلُ دَفعٍ مُتَكَرِّرٌ عادَةً).</summary>
    public const string SubscriptionSuspended = "BILLING.SUBSCRIPTION.SUSPENDED";

    /// <summary>الأَربَعَةُ الَّتي يُسَجِّلُها المالِكُ في لَوحَةِ
    /// PayPal — <c>docs/DEPLOY.md</c> §٢·ج يَقرَأُ مِنها.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        SubscriptionActivated, PaymentSaleCompleted,
        SubscriptionCancelled, SubscriptionSuspended
    };

    public static bool Extends(string? t)
        => t is SubscriptionActivated or PaymentSaleCompleted;

    public static bool StopsRenewal(string? t)
        => t is SubscriptionCancelled or SubscriptionSuspended;
}

/// <summary>حَدَثٌ مَقروءٌ — <b>وما لَم يوجَد يَبقى <c>null</c> ولا
/// يُخترَع</b>.</summary>
/// <param name="EventId">‏<c>id</c> — مِفتاحُ مَرَّة-واحِدَة.</param>
/// <param name="EventType">‏<c>event_type</c>.</param>
/// <param name="TenantSlug">‏<c>resource.custom_id</c> لِلاشتِراك، أَو
/// <c>resource.custom</c> لِلدَفعَة — وهُوَ ما وَضَعناهُ نَحنُ عِندَ
/// الإنشاء. <b>ولا يُقرَأُ المُستَأجِرُ مِن مَسارٍ ولا رَأسٍ</b>: مَن
/// يَملِك العُنوانَ يَملِك المَسار، ومَن يَملِك الاشتِراكَ وَحدَه
/// يَملِك هذا الحَقل.</param>
/// <param name="SubscriptionId">مُعَرِّفُ الاشتِراكِ في PayPal.</param>
/// <param name="NextBillingTime">‏<c>resource.billing_info.next_billing_time</c>
/// — <b>مَوعِدُ الاستِحقاقِ القادِم كَما تَقولُه PayPal نَفسُها</b>. وهُوَ
/// أَصدَقُ مَصدَرٍ لِتاريخِ الانتِهاءِ الجَديد، ولَيسَ رَقماً
/// نَحسُبُه.</param>
public sealed record PayPalWebhookEvent(
    string EventId,
    string EventType,
    string? TenantSlug,
    string? SubscriptionId,
    DateTime? NextBillingTime);

/// <summary>حالَةُ بابِ الرِسالَة — <b>مَعجَمٌ مُغلَق</b>، وثَلاثٌ مِن
/// أَربَعٍ رَفض.</summary>
public enum PayPalWebhookGate
{
    /// <summary>لا <c>WebhookId</c> (أَو لا اعتِماد) — فَلا سَبيلَ
    /// إلى تَحَقُّق. <b>فَشَلٌ مُغلَق</b>.</summary>
    NotConfigured,

    /// <summary>رَأسُ تَوقيعٍ ناقِص.</summary>
    HeadersMissing,

    /// <summary>‏PayPal رَدَّت بِغَير <c>SUCCESS</c>.</summary>
    SignatureInvalid,

    /// <summary>تَوقيعٌ صَحيح — <b>والآنَ فَقَط</b> يُقرَأُ الجِسمُ
    /// كَبَيانات.</summary>
    Accepted
}

/// <summary>ما تَقَرَّرَ فِعلُه بِالحَدَث — <b>مَعجَمٌ مُغلَق</b>.</summary>
public enum PayPalBillingAction
{
    /// <summary>تَمديدُ <c>ExpiresAt</c>.</summary>
    Extend,

    /// <summary>إيقافُ التَجديدِ التِلقائيّ — <b>ولا يُطفَأُ مَتجَرٌ
    /// سارٍ</b>: الحالَةُ تَبقى مُشتَقَّةً مِن الوَقت، فَمَن دَفَعَ
    /// شَهراً يَأخُذُ شَهرَه كامِلاً.</summary>
    StopRenewal,

    /// <summary>حَدَثٌ خارِجَ المَعجَم — لا كِتابَة.</summary>
    Ignored,

    /// <summary>‏<c>custom_id</c> لا يُقابِلُه مُستَأجِرٌ لَه وَثيقَةُ
    /// باقَة — <b>لا كِتابَةَ وسَطرُ لوغ</b>. ولا تُخترَعُ لَه
    /// وَثيقَةٌ: الباقَةُ والمُهلَةُ والسِعرُ قَرارُ مُشرِفٍ لا
    /// يَعرِفُه PayPal (القاعِدَة ١٦).</summary>
    UnknownTenant,

    /// <summary>لَه وَثيقَةٌ لكِنّ لا مُدَّةَ تُشتَقّ مِنها ولا
    /// <c>next_billing_time</c> — <b>لا كِتابَة</b>، ولا يُخترَع
    /// «شَهرٌ» ولا «سَنَة».</summary>
    UnknownPeriod,

    /// <summary>نَفسُ <c>event_id</c> عولِجَ سَلَفاً — لا تَمديدَ
    /// ثانٍ.</summary>
    Replay,

    /// <summary>
    /// <para><b>يُسحَبُ ما مُنِح</b> — استِردادٌ أَو عَكسُ دَفعَة
    /// (‏ADR-006). يُحَرِّك <c>ExpiresAt</c> إلى الخَلفِ بِمِقدارِ ما
    /// أَضافَتهُ الدَفعَةُ نَفسُها، <b>ولا يَمَسّ <c>Status</c></b>.</para>
    ///
    /// <para><b>وهُوَ عُضوٌ في هذا المَعجَمِ لا في مَعجَمٍ ثانٍ عَمداً</b>:
    /// الكاتِبُ واحِد (‏<c>PayPalBillingService.Apply</c>)، ومَسارُ
    /// الطَلَباتِ يُمَرِّرُ قَرارَه إلَيه بَدَلَ أَن يَفتَحَ باعِثَ
    /// تَمديدٍ ثانِياً — «لا أُنبوبَ رابِع» (القاعِدَة ٨).</para>
    /// </summary>
    Withdraw
}

/// <summary>القَرارُ كامِلاً. <c>NewExpiresAt</c> ذاتُ مَعنىً عِندَ
/// <see cref="PayPalBillingAction.Extend"/> وَحدَها.</summary>
public sealed record PayPalBillingDecision(
    PayPalBillingAction Action,
    DateTime NewExpiresAt,
    string ReasonAr)
{
    /// <summary>أَتُكتَبُ وَثيقَةٌ أَصلاً؟ <b>هذا هُوَ تَعريفُ «صِفرُ
    /// كِتابَة»</b> الَّذي يَفحَصُه الاختِبار — لا فَحصُ قاعِدَةِ
    /// بَياناتٍ بَعدَ الحَدَث.</summary>
    public bool Writes => Action is PayPalBillingAction.Extend
                                 or PayPalBillingAction.StopRenewal
                                 or PayPalBillingAction.Withdraw;
}

/// <summary>
/// <para><b>كُلُّ قَرارِ الفَوتَرَةِ عَبرَ PayPal — دَوالُّ نَقِيَّة.</b>
/// لا Marten، ولا HTTP، ولا <c>DateTime.UtcNow</c>: الوَقتُ يُمَرَّر.
/// نَفسُ شَكلِ <see cref="TenantPlanPolicy"/> حَرفاً، ولِنَفس السَبَب —
/// أَنّ الإغلاقَ لا يُبرهَن إلّا إذا أَمكَنَ اختِبارُه بِلا قاعِدَةِ
/// بَيانات.</para>
/// </summary>
public static class PayPalBillingPolicy
{
    /// <summary>ما تُجيبُ بِه PayPal عِندَ تَوقيعٍ صَحيح.</summary>
    public const string VerificationSuccess = "SUCCESS";

    // ─── البابُ: يُتَحَقَّقُ قَبلَ أَن يُقرَأ ─────────────────────────

    /// <summary>
    /// <para>قَرارُ البابِ — <b>وتَرتيبُ فُروعِه مَقصود</b>: التَهيئَةُ
    /// أَوَّلاً، فَالرُؤوس، فَالتَوقيع. أَي أَنّ «لا مُعَرِّفَ Webhook»
    /// يُقال بِاسمِه ولا يُخلَط بِـ«تَوقيعٌ فاشِل» — وإلّا بَحَثَ
    /// المالِكُ عَن سِرٍّ خاطِئٍ ومُشكِلَتُه سِرٌّ غائِب.</para>
    /// </summary>
    public static PayPalWebhookGate Gate(
        PayPalOptions? options, PayPalWebhookHeaders headers, bool? signatureVerified)
    {
        if (!PayPalEnvironment.CanVerifyWebhooks(options)) return PayPalWebhookGate.NotConfigured;
        if (!headers.IsComplete) return PayPalWebhookGate.HeadersMissing;
        return signatureVerified == true
            ? PayPalWebhookGate.Accepted
            : PayPalWebhookGate.SignatureInvalid;
    }

    /// <summary>رَمزُ الرَفضِ الَّذي تَرُدُّه النُقطَة — <b>مَعجَمٌ
    /// مُغلَق</b>، ورَمزٌ لِكُلّ حالَةٍ فَلا يُخلَط سَبَبان.</summary>
    public static string GateCode(PayPalWebhookGate gate) => gate switch
    {
        PayPalWebhookGate.NotConfigured   => "paypal_not_configured",
        PayPalWebhookGate.HeadersMissing  => "paypal_signature_headers_missing",
        PayPalWebhookGate.SignatureInvalid => "paypal_signature_invalid",
        PayPalWebhookGate.Accepted        => "accepted",
        _ => "paypal_rejected"
    };

    // ─── القِراءَة ────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>قِراءَةُ الحَدَث — تُعطي <c>null</c> ولا تَرمي.</b>
    /// جِسمٌ مُشَوَّهٌ أَو بِلا <c>id</c>/<c>event_type</c> لَيسَ
    /// حَدَثاً، وخُروجُ ‏500 مِن مُفَكِّكِ JSON يَجعَل PayPal تُعيد
    /// الإرسالَ إلى الأَبَد.</para>
    ///
    /// <para><b>و<c>custom_id</c> مَوضِعانِ لا واحِد</b>: أَحداثُ
    /// الاشتِراك تَحمِلُه <c>resource.custom_id</c>، ودَفعَةُ
    /// <c>PAYMENT.SALE.COMPLETED</c> تَحمِلُه <c>resource.custom</c> —
    /// اسمانِ لِشَيءٍ واحِدٍ في واجِهَتَينِ مِن جيلَين. وقِراءَةُ
    /// أَحَدِهِما وَحدَه تَجعَل التَجديدَ الشَهريَّ «مُستَأجِراً
    /// مَجهولاً» كُلَّ شَهر.</para>
    /// </summary>
    public static PayPalWebhookEvent? Parse(string? rawJson)
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

            string? slug = null, subscriptionId = null;
            DateTime? next = null;

            if (root.TryGetProperty("resource", out var res) && res.ValueKind == JsonValueKind.Object)
            {
                slug = Str(res, "custom_id") ?? Str(res, "custom");

                // مُعَرِّفُ الاشتِراك: `id` في أَحداثِ الاشتِراك،
                // و`billing_agreement_id` في الدَفعَة الدَورِيَّة.
                subscriptionId = PayPalEventTypes.PaymentSaleCompleted.Equals(type, StringComparison.Ordinal)
                    ? Str(res, "billing_agreement_id")
                    : Str(res, "id");

                if (res.TryGetProperty("billing_info", out var bi)
                    && bi.ValueKind == JsonValueKind.Object)
                    next = Utc(Str(bi, "next_billing_time"));
            }

            return new PayPalWebhookEvent(id!, type!, Trim(slug), Trim(subscriptionId), next);
        }
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? Utc(string? s)
        => DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AssumeUniversal
               | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
           ? d : null;

    // ─── القَرار ──────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>ماذا يُفعَل بِحَدَثٍ مُوَثَّق.</b></para>
    ///
    /// <para><b>وتاريخُ الانتِهاءِ الجَديدُ يُؤخَذ ولا يُخترَع</b>،
    /// بِثَلاثِ دَرَجاتٍ نازِلَة:</para>
    /// <list type="number">
    ///   <item><c>next_billing_time</c> مِن PayPal إن كانَ بَعدَ
    ///   المِرساة — <b>مَوعِدُ الاستِحقاقِ الحَقيقيُّ لِلدافِع</b>.</item>
    ///   <item>وإلّا <b>طولُ المُدَّةِ الَّتي ضَبَطَها المُشرِف</b>
    ///   (<c>ExpiresAt − StartsAt</c>) يُضاف إلى المِرساة. رَقمٌ
    ///   مِن بَياناتِ المَتجَرِ نَفسِه لا مِن كود.</item>
    ///   <item>وإلّا <see cref="PayPalBillingAction.UnknownPeriod"/> —
    ///   <b>لا كِتابَة</b>. و«شَهرٌ افتِراضيّ» هُنا اختِراعُ بَياناتِ
    ///   مُنتَجٍ بِثَمَنٍ نَقديّ (القاعِدَة ١٦).</item>
    /// </list>
    ///
    /// <para><b>والمِرساةُ <c>max(now, ExpiresAt)</c> لا <c>now</c></b>:
    /// مَن جَدَّدَ قَبلَ انتِهاءِ مُدَّتِه <b>لا يُصادَر</b> ما تَبَقّى
    /// لَه. ولا <c>ExpiresAt</c> وَحدَها: مَن عادَ بَعدَ انقِطاعِ
    /// شَهرَينِ لا يُشتَرى لَه ماضٍ مَضى.</para>
    ///
    /// <para><b>ولا تُمَسُّ <c>Status</c> في أَيّ فَرع</b>: إيقافُ
    /// المُشرِفِ اليَدَوِيُّ يَبقى فَوقَ كُلِّ دَفعَة — مَن أُوقِفَ
    /// لِسَبَبٍ لا يُعيدُه دَفعُ مالٍ وَحدَه.</para>
    /// </summary>
    public static PayPalBillingDecision Decide(
        PayPalWebhookEvent e, TenantPlan? plan, bool alreadySeen, DateTime now)
    {
        if (alreadySeen)
            return new(PayPalBillingAction.Replay, default,
                $"الحَدَث «{e.EventId}» عولِجَ سابِقاً — لا تَمديدَ ثانٍ.");

        if (!PayPalEventTypes.Extends(e.EventType) && !PayPalEventTypes.StopsRenewal(e.EventType))
            return new(PayPalBillingAction.Ignored, default,
                $"نَوعُ الحَدَث «{e.EventType}» خارِجَ المَعجَم — لا فِعل.");

        if (string.IsNullOrWhiteSpace(e.TenantSlug) || plan is null)
            return new(PayPalBillingAction.UnknownTenant, default,
                $"‏custom_id «{e.TenantSlug ?? "—"}» بِلا وَثيقَةِ باقَة — " +
                "لا كِتابَة. يَضبُطُها المُشرِفُ مَرَّةً مِن /admin ثُمَّ يُمَدِّدُها PayPal.");

        if (PayPalEventTypes.StopsRenewal(e.EventType))
            return new(PayPalBillingAction.StopRenewal, plan.ExpiresAt,
                $"‏{e.EventType} — يُوقَف التَجديد، وتَبقى المُدَّةُ المَدفوعَةُ " +
                $"إلى {plan.ExpiresAt:yyyy-MM-dd} كَما هي.");

        var anchor = now > plan.ExpiresAt ? now : plan.ExpiresAt;

        if (e.NextBillingTime is { } next && next > anchor)
            return new(PayPalBillingAction.Extend, next,
                $"‏next_billing_time مِن PayPal: {next:yyyy-MM-dd}.");

        var period = plan.ExpiresAt - plan.StartsAt;
        if (period > TimeSpan.Zero)
            return new(PayPalBillingAction.Extend, anchor + period,
                $"طولُ المُدَّةِ المَضبوطَة ({period.Days} يَوماً) يُضاف إلى {anchor:yyyy-MM-dd}.");

        return new(PayPalBillingAction.UnknownPeriod, default,
            "لا next_billing_time ولا مُدَّةٌ تُشتَقّ مِن الوَثيقَة — لا كِتابَة، " +
            "ولا يُخترَع طولُ اشتِراك.");
    }

    // ─── الأَثَرُ على الوَثيقَة — دالَّةٌ نَقِيَّةٌ أَيضاً ───────────

    /// <summary>
    /// <para><b>تُطَبَّقُ نَتيجَةُ القَرارِ على الوَثيقَة، ولا شَيءَ
    /// آخَر.</b> لا جَلسَةَ هُنا ولا إيداع — الخِدمَةُ في القالِبِ هي
    /// الَّتي تُخَزِّن. فَالجُملَتانِ «ماذا يَتَغَيَّر» و«مَتى
    /// يُودَع» تُقاسانِ مُنفَصِلَتَين.</para>
    ///
    /// <para><b>والتَمديدُ يَمسَح <c>RenewalCancelledAt</c></b>: مَن
    /// أَلغى ثُمَّ عادَ فَدَفَع، عادَ تَجديدُه. وتَركُها يَجعَل
    /// الشاشَةَ تَقول «التَجديدُ مُوقَف» لِمَن يَدفَع.</para>
    /// </summary>
    public static void Apply(
        TenantPlan plan, PayPalWebhookEvent e, PayPalBillingDecision decision, DateTime at)
    {
        switch (decision.Action)
        {
            case PayPalBillingAction.Extend:
                plan.ExpiresAt = decision.NewExpiresAt;
                plan.RenewalCancelledAt = null;
                break;

            case PayPalBillingAction.StopRenewal:
                plan.RenewalCancelledAt = at;
                break;

            // **سَحبٌ لا إطفاء**: يُحَرَّكُ التاريخُ إلى الخَلفِ وَحدَه،
            // و<c>Status</c> لا تُمَسّ — فَقَرارُ المُشرِفِ يَبقى فَوقَ
            // كُلِّ حَرَكَةِ مال، صُعوداً كانَت أَو نُزولاً.
            case PayPalBillingAction.Withdraw:
                plan.ExpiresAt = decision.NewExpiresAt;
                break;

            default:
                return;    // لا وَثيقَةَ تُلمَس
        }

        if (e.SubscriptionId is { Length: > 0 } sub)
            plan.PayPalSubscriptionId = sub;

        plan.SetBy = $"paypal · {e.EventType}";
        plan.SetAt = at;
    }

    /// <summary>
    /// <para><b>وَثيقَةُ مَرَّة-واحِدَة لِحَدَثٍ طُبِّق</b> — دالَّةٌ
    /// نَقِيَّةٌ لِيُقاسَ <b>مِفتاحُها</b> بِلا قاعِدَةِ بَيانات. وهُوَ
    /// المَوضِعُ الَّذي يَنكَسِر صامِتاً: مِفتاحٌ يَحمِل الوَقتَ أَو
    /// السلاجَ بَدَلَ <c>event_id</c> يَجعَل كُلَّ إعادَةِ إرسالٍ
    /// تُمَدِّدُ شَهراً آخَر.</para>
    /// </summary>
    public static PayPalWebhookRecord RecordFor(
        PayPalWebhookEvent e, PayPalBillingDecision decision, DateTime appliedExpiresAt, DateTime at)
        => new()
        {
            Id               = e.EventId,
            EventType        = e.EventType,
            TenantSlug       = e.TenantSlug ?? "",
            Action           = decision.Action.ToString(),
            AppliedExpiresAt = appliedExpiresAt,
            At               = at,
        };
}
