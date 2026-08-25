using System.Globalization;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.PayPal;

// ═══ طَلَبُ دَفعٍ واحِد — Orders v2، بِلا خُطَّةٍ مُسبَقَة ═════════════
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ (‏ADR-006)**: مَسارُ
// الاشتِراكات (‏Subscriptions v1) يَشتَرِط **خُطَّةً مُعَرَّفَةً سَلَفاً
// عِندَ PayPal** بِسِعرِها ودَورَتِها، ويَقوم تَحتَ الغِطاءِ عَلى
// اتِّفاقِيَّةِ فَوتَرَة (‏`ba_token=BA-…`) — أَي عائِلَةِ
// Reference Transactions الَّتي تَحتاج **استِحقاقاً** لَم يُثبَت لِحِسابٍ
// سُعوديّ. وهذا المَسارُ يُرسِل **المَبلَغَ والعُملَةَ والوَصفَ
// ومَرجِعَنا لَحظَةَ الطَلَب** فَيَفتَح صَفحَةَ دَفعٍ مُستَضافَة،
// و«‏Know before you code» في بَدءِ Standard Checkout **لا يَذكُر خُطوَةَ
// مُوافَقَةٍ ولا أَهلِيَّةٍ ولا اكتِتاب**.
//
// **وكُلُّ ما هُنا دَوالُّ نَقِيَّة**: لا HTTP ولا وَقتَ ولا عَشوائيَّة —
// نَفسُ نَمَطِ `PayPalCatalogPolicy` حَرفاً، ولِنَفسِ السَبَب: الحَدُّ
// الَّذي لا يُقاس آلِيّاً يَنهار (القاعِدَة ٢).

/// <summary>حالاتُ وَثيقَةِ الطَلَبِ عِندَنا — <b>مَعجَمٌ مُغلَق</b>،
/// ونَصٌّ لا تَعداد: الوَثيقَةُ تَعيشُ أَطوَلَ مِن أَسماءِ أَعضاءِ
/// تَعدادٍ في كود (نَفسُ حُجَّةِ <c>PayPalWebhookRecord.Action</c>).</summary>
public static class PayPalOrderStatuses
{
    /// <summary>أُنشِئَ الطَلَبُ وصارَ لَه رابِطٌ مُستَضاف — <b>ولا مالَ
    /// وَصَل</b>.</summary>
    public const string Created = "created";

    /// <summary>وافَقَ الدافِعُ على صَفحَةِ PayPal — <b>مُوافَقَةٌ لا
    /// مال</b>. وهي حالَةٌ <b>تَنتَهي صَلاحِيَّتُها</b>: طَلَبٌ لا
    /// يُلتَقَط خِلالَ النافِذَةِ تُلغيه PayPal وتُعيدُ المالَ
    /// لِلمُشتَري.</summary>
    public const string Approved = "approved";

    /// <summary>وَصَلَ المالُ فِعلاً — <c>PAYMENT.CAPTURE.COMPLETED</c>
    /// و<c>resource.status = COMPLETED</c>. <b>وهذِه وَحدَها تُمَدِّد
    /// باقَة.</b></summary>
    public const string Captured = "captured";

    /// <summary>الالتِقاطُ مُعَلَّق (‏eCheck، أَو حِسابُ مُستَلِمٍ غَيرُ
    /// مُؤَكَّد، أَو مُراجَعَة) — <b>مَمنوعٌ التَمديد بِنَصٍّ صَريحٍ مِن
    /// PayPal</b>: «‏Do not fulfill the order until payment completion is
    /// successful».</summary>
    public const string Pending = "pending";

    /// <summary>رُفِضَ الالتِقاط — بِطاقَةٌ مَرفوضَةٌ عادَةً. لا مَساسَ
    /// بِالباقَة (لَم يَقَع تَمديدٌ أَصلاً).</summary>
    public const string Denied = "denied";

    /// <summary>اُستُرِدَّ المالُ أَو عُكِسَ — نِزاعٌ أَو احتِيالٌ أَو
    /// انقِضاءُ نافِذَةِ المُوافَقَة.</summary>
    public const string Reversed = "reversed";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Created, Approved, Captured, Pending, Denied, Reversed
    };

    /// <summary>أَلَم يُلتَقَط بَعد؟ <b>وهذا شَرطُ ظُهورِ زِرّ «التَقِط
    /// الآن» اليَدَوِيّ</b> — لِأَنّ انقِطاعَ الأَحداثِ يَجِب أَن يَكونَ
    /// لَه عِلاجٌ بِنَقرَة، لا بِأَمرِ كونسول (القاعِدَة ١٢).</summary>
    public static bool AwaitsCapture(string? status)
        => status is Created or Approved or Pending;

    /// <summary>
    /// <para><b>أَيَجوزُ الانتِقالُ مِن حالَةٍ إلى حالَة؟ — جَدوَلٌ
    /// صَريحٌ في مَوضِعٍ واحِد.</b> والكِتابَةُ <b>أُحادِيَّةُ
    /// الاتِّجاه</b>: ما بَلَغَ <c>captured</c> لا يَعودُ إلى ما دونَه،
    /// وما بَلَغَ <c>reversed</c> لا يَعودُ إطلاقاً.</para>
    ///
    /// <para><b>والكُلفَةُ الَّتي كَتَبَت هذا الجَدوَل — مَقيسَةٌ
    /// بِتَشغيلِ التَجميعَة</b>: كانَ <c>Apply</c> يَكتُب
    /// <c>order.Status = decision.OrderStatus</c> <b>بِلا فَحصٍ
    /// لِلحالَةِ السابِقَة</b>. فَحَدَثُ <c>PAYMENT.CAPTURE.PENDING</c>
    /// مُتَأَخِّراً على طَلَبٍ حالَتُه <c>captured</c> كانَ يَهبِطُ
    /// بِها إلى <c>pending</c>؛ ثُمَّ يَصِل
    /// <c>PAYMENT.CAPTURE.REFUNDED</c> فَيَجِدُ حارِسَ السَحبِ
    /// يَشتَرِط <c>captured</c> — <b>فَلا يُسحَبُ شَيء: المالُ يَعود
    /// والأَيّامُ تَبقى</b>. وPayPal تُعيد الإرسالَ ‏25 مَرَّةً خِلالَ
    /// ثَلاثَةِ أَيّام، فَالتَأَخُّرُ والتَكرارُ ليسا احتِمالاً
    /// بَعيداً بَل عادَةَ الخِدمَة.</para>
    ///
    /// <para><b>ولِماذا جَدوَلٌ لا شَرطانِ مَنثوران</b>: القاعِدَةُ
    /// تُقرَأُ في مَوضِعٍ واحِدٍ وتُختَبَرُ بِجَدوَلٍ كامِل — ‏36
    /// خانَةً — بَدَلَ أَن تُنسَخَ في كُلِّ مَوضِعِ كِتابَة
    /// (القاعِدَة ٢). ويَقرَؤُها اليَومَ ثَلاثَةُ كُتّاب:
    /// <c>PayPalOrderBillingPolicy.Apply</c>، ونِداءُ الالتِقاطِ
    /// المُوَحَّد، والقَرارُ نَفسُه قَبلَ أَن يُقَرِّرَ تَمديداً.</para>
    ///
    /// <para><b>ومُعامَلَةُ الحالَةِ المَجهولَةِ كَـ<c>created</c>
    /// مَقصودَة</b>: وَثيقَةٌ قَديمَةٌ بِحَقلٍ فارِغٍ لا تُقفَل دونَ
    /// كُلِّ انتِقال — <b>والتَكافُؤُ الصِفريُّ يَقتَضي أَلّا يُغلِقَ
    /// حارِسٌ جَديدٌ باباً بِسَبَبِ بَياناتٍ سابِقَةٍ عَلَيه</b>.</para>
    /// </summary>
    public static bool CanTransition(string? from, string? to)
    {
        var target = (to ?? "").Trim();
        if (!All.Contains(target, StringComparer.Ordinal)) return false;

        var current = (from ?? "").Trim();
        if (!All.Contains(current, StringComparer.Ordinal)) current = Created;

        // نَفسُ الحالَةِ إلى نَفسِها: تَعليمٌ مُكَرَّرٌ لا ضَرَرَ فيه —
        // يُنعِشُ الوَقتَ والسَبَبَ ولا يُبَدِّل شَيئاً يُقَرِّر.
        if (string.Equals(current, target, StringComparison.Ordinal)) return true;

        return current switch
        {
            Created  => true,
            Approved => target is Captured or Pending or Denied or Reversed,
            Pending  => target is Captured or Denied or Reversed,

            // **ورَفضٌ لا يُغلِقُ بابَ المال**: التِقاطٌ رُفِضَ ثُمَّ
            // نَجَحَ بِنِداءٍ ثانٍ واقِعَةٌ مُمكِنَة، و«وَصَلَ المال»
            // يَجِبُ أَن يَكتُبَ <c>captured</c> دائِماً وإلّا انكَسَرَ
            // حارِسُ السَحبِ مِن جِهَةٍ أُخرى.
            Denied   => target is Captured or Reversed,

            // **وَصَلَ المالُ ومُنِحَت الأَيّام**: لا يُنقَضُ ذلك إلّا
            // بِعَودَةِ المال.
            Captured => target is Reversed,

            // **عادَ المالُ**: حالَةٌ نِهائِيَّةٌ مُعلَنَةٌ لا مُشتَقَّة.
            Reversed => false,
            _        => false
        };
    }
}

/// <summary>
/// <para><b>ما يَملَؤُه المُشرِفُ في نَموذَجِ رابِطِ الدَفع</b> — أَربَعَةُ
/// حُقولٍ ومَتجَرُها. <b>ولا قيمَةَ افتِراضِيَّةً لِمَبلَغٍ ولا
/// لِمُدَّة</b>: رَقَمُ الفاتورَةِ وطولُ ما اشتُرِيَ بَياناتُ صَفقَةٍ لا
/// تُخترَع (القاعِدَة ١٦). والعُملَةُ وَحدَها تَحمِل افتِراضاً لِأَنَّه
/// <b>مَقيسٌ لا مَظنون</b> (‏<see cref="PayPalCurrencies.Default"/>).</para>
/// </summary>
/// <param name="TenantSlug">سلاجُ المَتجَرِ الَّذي تُمَدَّدُ باقَتُه —
/// <b>مِن المَسارِ لا مِن النَموذَج</b>.</param>
/// <param name="PlanId">سلاجُ الباقَةِ كَما هُوَ في وَثيقَةِ المَتجَر —
/// <b>لَقطَةٌ لِلتَدقيقِ لا قَرار</b>.</param>
/// <param name="Amount">المَبلَغُ المُرسَلُ لَحظَةَ الطَلَب — وهُوَ ما
/// يُخصَم، بِتَعريفٍ واحِدٍ لا اثنَين.</param>
/// <param name="Currency">مِن <see cref="PayPalCurrencies.Supported"/>
/// حَصراً — <b>ولا SAR فيها</b>.</param>
/// <param name="Days">كَم يَوماً تُمَدَّدُ الباقَةُ عِندَ وُصولِ المال.
/// <b>يُخَزَّنُ في وَثيقَةِ الدَفعِ المُعَلَّق</b>، فَلا يُشتَقُّ يَومَ
/// الحَدَثِ ولا يُخترَع.</param>
/// <param name="Description">ما يَراهُ الدافِعُ على صَفحَةِ PayPal —
/// اختِياريّ.</param>
/// <param name="Cycle"><b>مُمَيِّزُ الدَورَة — لا يُرسَل إلى PayPal
/// إطلاقاً، ويَدخُل بَصمَةَ المَرجِعِ وَحدَها.</b> وهُوَ تاريخُ
/// انتِهاءِ الباقَةِ القائِمُ يَومَ يُنشَأُ الرابِط
/// (<see cref="PayPalOrderPolicy.CycleOf"/>).
///
/// <para><b>والكُلفَةُ الَّتي أَدخَلَتهُ</b>: المَرجِعُ كانَ بَصمَةَ
/// مُدخَلاتِ المُسَوَّدَةِ وَحدَها، فَالتَجديدُ الشَهرِيُّ العاديُّ —
/// نَفسُ المَبلَغِ ونَفسُ المُدَّةِ ونَفسُ الوَصف — يُعطي
/// <b>المَرجِعَ نَفسَه</b>، ووَثيقَتُه تُكتَبُ بِـ<c>Store</c>
/// فَتَدهَسُ سِجِلَّ الشَهرِ السابِق: يَضيعُ <c>CaptureId</c>
/// وتَعودُ الحالَةُ <c>created</c>، فَلا يَجِدُ استِردادُ الشَهرِ
/// السابِقِ ما يَربِطُه — <b>باقَةٌ قائِمَةٌ ومالٌ عاد</b>. وبِمُمَيِّزِ
/// الدَورَةِ يَصيرُ التَجديدُ مَرجِعاً جَديداً <b>طَبيعِيّاً</b>،
/// وتَبقى نَقرَتانِ في الدَورَةِ نَفسِها وَثيقَةً واحِدَة.</para></param>
public sealed record PayPalOrderDraft(
    string  TenantSlug,
    string  PlanId,
    decimal Amount,
    string  Currency,
    int     Days,
    string  Description,
    string  Cycle)
{
    public string NormalizedSlug => (TenantSlug ?? "").Trim().ToLowerInvariant();

    /// <summary>العُملَةُ مُطَبَّعَةً — PayPal تَقرَأُها كَبيرَة.</summary>
    public string NormalizedCurrency => (Currency ?? "").Trim().ToUpperInvariant();

    /// <summary><b>الوَصفُ مَقصوصٌ عِندَ ‏127</b>: المُخَطَّطُ يَقول
    /// ‏1..3000 لكِنّ النَصَّ يُقَصّ فِعلاً عِندَ ‏127 <b>والقَصُّ
    /// يَنعَكِس في الاستِجابَة</b> — فَقَصُّه عِندَنا يَجعَل ما نُرسِلُه
    /// هُوَ ما يُخَزَّن.</summary>
    public string TrimmedDescription
    {
        get
        {
            var d = (Description ?? "").Trim();
            return d.Length <= PayPalOrderPolicy.MaxDescriptionLength
                ? d
                : d[..PayPalOrderPolicy.MaxDescriptionLength];
        }
    }

    /// <summary>المَبلَغُ كَما تَشتَرِطُه PayPal: <b>سِلسِلَةٌ
    /// نَصِّيَّة</b> بِفاصِلَةٍ إنجِليزِيَّةٍ دائِماً. ودالَّةُ الصِياغَةِ
    /// هي نَفسُها الَّتي يَستَعمِلُها مَسارُ الخُطَط — فَلا صِياغَتانِ
    /// تَنجَرِفان.</summary>
    public string MoneyValue => PayPalCurrencies.Money(Amount, NormalizedCurrency);

    /// <summary>
    /// <para><b>المَبلَغُ الَّذي يُخصَمُ فِعلاً — وهُوَ ما يُخَزَّن.</b>
    /// قِراءَةُ <see cref="MoneyValue"/> عَدَداً، فَـ<b>المُرسَلُ
    /// والمَحفوظُ تَعريفٌ واحِدٌ لا اثنان</b>.</para>
    ///
    /// <para><b>والكُلفَةُ الَّتي كَتَبَتها</b>: الوَثيقَةُ كانَت
    /// تُخَزِّن <c>Amount</c> خاماً بَينَما تَتَلَقّى PayPal
    /// <see cref="MoneyValue"/> <b>مَصوغاً ومُدَوَّراً</b> (صيغَةُ
    /// <c>"0"</c> لِعُملاتٍ بِلا كُسور: JPY/HUF/TWD)، و
    /// <c>MoneyMatches</c> تُقارِنُ الواصِلَ بِالخام. فَـ
    /// <c>5000.5 JPY</c> يُرسَل <c>"5001"</c> ويَعودُ <c>5001</c>
    /// فَلا يُطابِق <c>5000.5</c> ⇒ <c>AmountMismatch</c> ⇒
    /// <b>قُبِضَ المالُ ولَم تُمَدَّد الباقَة، صامِتاً</b>. وكَذلك
    /// <c>49.005 USD</c>.</para>
    ///
    /// <para><b>ولِماذا التَطبيعُ لا الرَفض</b>: الرَفضُ يَجعَل
    /// المُشرِفَ يُصَحِّحُ رَقَماً <b>صَحيحاً في عُملَتِه</b>
    /// (‏<c>0.5 JPY</c> لا وُجودَ لَها أَصلاً)، والتَطبيعُ يَجعَل
    /// المَخزونَ <b>بِعَينِه</b> ما يُقارَنُ بِه. والشاشَةُ تَعرِض
    /// المَخزون، فَالمُشرِفُ يَرى ما يُخصَم لا ما كَتَب. وحارِسُ
    /// الإدخالِ في النَموذَجِ (<c>step</c> بِحَسَبِ العُملَة) يَمنَعُ
    /// الفارِقَ قَبلَ وُقوعِه.</para>
    /// </summary>
    public decimal NormalizedAmount
        => decimal.TryParse(MoneyValue, PayPalCurrencies.MoneyStyles,
                            CultureInfo.InvariantCulture, out var v) ? v : Amount;
}

/// <summary>خَرقٌ واحِدٌ في مُسَوَّدَةِ طَلَب. نَفسُ شَكلِ
/// <see cref="PayPalCatalogViolation"/> حَرفاً — <c>Code</c> ثابِتٌ
/// لِلاختِبارِ ولِلقامُوس، و<c>MessageAr</c> لِلوغ.</summary>
public sealed record PayPalOrderViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>نَتيجَةُ إنشاءِ الطَلَب.</b> <c>ApproveUrl</c> هُوَ
/// <b>رابِطُ الصَفحَةِ المُستَضافَة</b> — وهُوَ المَقصودُ كُلُّه.</para>
///
/// <para><b>ولا رَميَ عِندَ الفَشَل بَل سَبَبٌ مُسَمّى</b>: هذِه تُنادى
/// مِن نُقطَةِ نَموذَجٍ تُحَوِّل، فَتُعيد نَصَّ PayPal لِيُصَنَّف
/// بِـ<see cref="PayPalFailure.ScreenCode"/> — نَفسُ عادَةِ
/// <see cref="SubscriptionResult"/> حَرفاً.</para>
/// </summary>
public sealed record PayPalOrderResult(
    string  OrderId,
    string  Status,
    string? ApproveUrl,
    string? FailureReason);

/// <summary>نَتيجَةُ الالتِقاط — والمَقروءُ مِنها <b>مُعَرِّفُ
/// الالتِقاطِ وحالَتُه</b>. <c>NetAmount</c> صافي ما يَصِل الحِسابَ بَعدَ
/// الرُسوم، <b>ولا يُشتَقُّ مِن المَبلَغِ بِأَيّ حِسابٍ مَحَلِّيّ</b> —
/// يُسَجَّلُ وَقتَ الالتِقاطِ أَو يَضيع.</summary>
public sealed record PayPalCaptureResult(
    string  CaptureId,
    string  Status,
    string? NetAmount,
    string? FailureReason);

/// <summary>
/// <para><b>بَوّابَةُ طَلَبِ الدَفعِ ومَفاتيحُ مَرَّة-واحِدَة ومَرجِعُنا</b>
/// — دَوالُّ نَقِيَّة تُقاسُ بِجَدوَل.</para>
/// </summary>
public static class PayPalOrderPolicy
{
    // ─── رُموزُ الخَرق — مَعجَمٌ مُغلَقٌ يَقرَؤُه المُصادِقُ والقامُوس ──
    public const string TenantEmpty         = "paypal_order_tenant_empty";
    public const string PlanMissing         = "paypal_order_plan_missing";
    public const string AmountNotPositive   = "paypal_order_amount_not_positive";
    public const string CurrencyUnsupported = "paypal_order_currency_unsupported";
    public const string DaysOutOfRange      = "paypal_order_days_out_of_range";
    public const string DescriptionTooLong  = "paypal_order_description_too_long";

    // ─── قيَمُ الجِسمِ الثابِتَة — مَعاجِمُ PayPal المُغلَقَة ──────────
    //
    // تُكتَب هُنا لا في جِسمِ النِداء، فَيَقرَؤُها الاختِبارُ مِن
    // مَوضِعِها بَدَلَ أَن يَنسَخَها — وسِلسِلَةٌ مَنسوخَةٌ بِخَطَإ حَرفٍ
    // تُعطي ‏422 غامِضَةً بَعدَ نَشر.

    /// <summary>‏<c>CAPTURE|AUTHORIZE</c>. و<c>AUTHORIZE</c> يُلتَقَط عَبرَ
    /// Payments v2 لا Orders — نُقطَةُ فَشَلٍ ثانِيَةٌ بِلا
    /// مُقابِل.</summary>
    public const string Intent = "CAPTURE";

    /// <summary>يَفتَح صَفحَةَ الدُخولِ مُباشَرَةً بَدَلَ نَموذَجِ
    /// الزائِر.</summary>
    public const string LandingPage = "LOGIN";

    /// <summary>مُنتَجٌ رَقميّ — <b>ولا عُنوانَ شَحنٍ يُطلَب</b>.
    /// (⚠ صَفحَةُ حالاتِ الاستِعمالِ الرَسمِيَّةُ تَكتُب
    /// <c>SET_FROM_PROVIDER</c> وهي قيمَةٌ <b>غَيرُ مَوجودَةٍ في
    /// المُخَطَّطِ إطلاقاً</b> — مِقياسٌ لِمِقدارِ الثِقَةِ الواجِبَةِ في
    /// النَثر.)</summary>
    public const string ShippingPreference = "NO_SHIPPING";

    /// <summary>الافتِراضُ <c>CONTINUE</c> يَعرِض «‏Continue to Review
    /// Order» ويُلزِمُ بِصَفحَةِ مُراجَعَةٍ عِندَنا. ومَبلَغُنا مَعلومٌ
    /// مُسبَقاً.</summary>
    public const string UserAction = "PAY_NOW";

    /// <summary>يَمنَع eCheck الَّتي تَبقى <c>PENDING</c> أَيّاماً —
    /// <b>وباقَةٌ تُفَعَّل لَحظَةَ الدَفعِ لا تُفَعَّل بِمالٍ لَم
    /// يَصِل</b>.</summary>
    public const string PaymentMethodPreference = "IMMEDIATE_PAYMENT_REQUIRED";

    /// <summary>ما يُعرَض على كَشفِ حِسابِ الدافِع. <b>يُعرَض مِنه ‏22
    /// مِحرَفاً فَقَط</b> وبادِئَةُ «‏PAYPAL » تَلتَهِم ثَمانِيَةً —
    /// فَلاتينيٌّ قَصيرٌ لا عَرَبيٌّ يُقَصّ.</summary>
    public const string SoftDescriptor = "WASAYEL";

    /// <summary>اسمُ المَنَصَّةِ كَما يَراهُ الدافِعُ على صَفحَةِ
    /// PayPal. <b>قيمَةٌ تُرسَل إلى طَرَفٍ ثالِثٍ لا نَصُّ شاشَةٍ
    /// عِندَنا</b> — فَلا تَسكُن في القامُوس (القاعِدَة ١١)، ولاتينِيَّةٌ
    /// لِأَنّ تَغطِيَةَ خَطِّ PayPal لِلعَرَبِيَّةِ <b>لَم تُقرَأ مِن
    /// صَفحَةٍ حَقيقِيَّة</b>.</summary>
    public const string BrandName = "Wasayel";

    /// <summary>الرَمزُ الَّذي يُدرِجُه الجَدوَلُ الرَسميُّ
    /// لِلسُعودِيَّةِ بِأَولَوِيَّةِ لُغَةٍ ‏1. <b>ولَم تُفتَح صَفحَةُ
    /// دَفعٍ حَقيقِيَّةٌ لِلنَظَر</b> — دَينٌ مُعلَنٌ في
    /// <c>docs/DEPLOY.md</c> §٢·د.</summary>
    public const string Locale = "ar-SA";

    /// <summary>سَقفُ الوَصفِ الفِعليّ — <b>‏127 لا ‏3000</b>. المُخَطَّطُ
    /// يَقول الأَكبَر والخِدمَةُ تَقُصُّ عِندَ الأَصغَر.</summary>
    public const int MaxDescriptionLength = 127;

    /// <summary>
    /// <para><b>سَقفُ المُدَّةِ المَشتَراةِ بِطَلَبٍ واحِد: سَنَتان.</b>
    /// وهُوَ <b>حارِسُ إدخالٍ لا بَيانُ مُنتَج</b> — نَفسُ طَبَقَةِ
    /// <see cref="TenantPlanPolicy.MaxGraceDays"/> حَرفاً: ما فَوقَه
    /// خَطَأُ لَوحَةِ مَفاتيحَ لا سِياسَةُ تَسعير.</para>
    ///
    /// <para><b>ولِماذا سَقفٌ أَصلاً</b>: الرَقَمُ يُضافُ إلى
    /// <c>ExpiresAt</c> بِمالٍ حَقيقيّ. وصِفرٌ زائِدٌ بِالخَطَإ يَشتَري
    /// عَشرَ سِنينَ بِثَمَنِ شَهر، <b>ولا فاحِصَ بَعدَه</b>: كُلُّ شَيءٍ
    /// يَعمَل كَما كُتِب.</para>
    /// </summary>
    public const int MaxDays = 730;

    /// <summary>
    /// <para><b>وُسومُ رابِطِ التَحويل — اثنانِ لا واحِد، ويُقالُ
    /// لِماذا.</b> طَلَبٌ <b>فيه</b>
    /// <c>payment_source.paypal.experience_context</c> (وهُوَ شَكلُنا)
    /// يَرُدّ <c>PAYER_ACTION_REQUIRED</c> ورابِطاً مَوسوماً
    /// <c>payer-action</c>، و<c>approve</c> <b>غائِبٌ أَصلاً</b>؛ وطَلَبٌ
    /// بِلا <c>payment_source</c> يَرُدّ <c>approve</c>. والعُنوانُ
    /// <b>واحِدٌ في الحالَتَين</b>.</para>
    ///
    /// <para><b>ومَعَ ذلك ما يَزالُ وَصفُ نُقطَةِ الالتِقاطِ في
    /// المُواصَفَةِ يَقول «the rel:approve URL»</b> — أَي أَنّ الوَثائِقَ
    /// نَفسَها غَيرُ مُتَّسِقَة. فَيُؤخَذُ <b>أَوَّلُ</b> رابِطٍ رَمزُه
    /// أَحَدُهُما، ولا يُبنى الكودُ على اسمٍ واحِد.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ApproveRels = new[] { "payer-action", "approve" };

    /// <summary>مَسارُ صَفحَةِ العَودَةِ — <b>مَوضِعٌ واحِدٌ</b> تَقرَؤُه
    /// النُقطَةُ والصَفحَةُ والاختِبار.</summary>
    public const string ReturnPath = "/billing/paypal/return";

    /// <summary>مَسارُ صَفحَةِ الإلغاء.</summary>
    public const string CancelPath = "/billing/paypal/cancel";

    /// <summary>اسمُ مُتَغَيِّرِ الاستِعلامِ الَّذي نَضَعُه بِأَنفُسِنا في
    /// رابِطَي العَودَةِ والإلغاء. <b>ولا نَعتَمِد على <c>token</c> الَّذي
    /// تُلحِقُه PayPal</b>: اسمُ مُتَغَيِّرٍ عِندَ طَرَفٍ ثالِثٍ لَيسَ
    /// عَقداً نَبني عَلَيه صَفحَةً.</summary>
    public const string ReferenceQueryKey = "ref";

    /// <summary>
    /// <para><b>حُقولُ النَموذَجِ مَقروءَةً — دالَّةٌ نَقِيَّةٌ لا تَعرِف
    /// HTTP</b>، كَـ<see cref="PayPalCatalogPolicy.ReadDraft"/> حَرفاً.</para>
    ///
    /// <para><b>والسُقوطُ عِندَ كُلّ حَقلٍ مَقصود</b>: مَبلَغٌ غَيرُ
    /// مَقروءٍ = صِفر <b>فَيَرتَدُّ بِخَرقٍ يُسَمّيه</b>، ومُدَّةٌ غَيرُ
    /// مَقروءَةٍ = صِفر فَتَرتَدُّ كَذلك — <b>ولا «شَهرٌ افتِراضيّ»</b>،
    /// فَذاكَ رَقَمٌ مُخترَعٌ بِثَمَنٍ نَقديّ. والعُملَةُ الغائِبَةُ
    /// وَحدَها تَرتَدُّ إلى <see cref="PayPalCurrencies.Default"/> لِأَنَّه
    /// <b>الافتِراضُ الوَحيدُ المَقيس</b>.</para>
    ///
    /// <para><b>و<c>tenantSlug</c>/<c>planId</c> لا يُقرَآنِ مِن
    /// النَموذَج</b>: الأَوَّلُ مِن المَسارِ والثاني مِن وَثيقَةِ باقَةِ
    /// المَتجَرِ في الخادِم. ولَو قُرِئا مِن الطَلَبِ لَمَدَّدَ
    /// مُتَصَفِّحٌ باقَةَ مَتجَرٍ لَم يَخترهُ المُشرِف.</para>
    /// </summary>
    public static PayPalOrderDraft ReadDraft(
        string? tenantSlug, string? planId,
        string? amount, string? currency, string? days, string? description, string? cycle)
        => new(
            (tenantSlug ?? "").Trim().ToLowerInvariant(),
            (planId ?? "").Trim(),
            decimal.TryParse(amount, PayPalCurrencies.MoneyStyles, CultureInfo.InvariantCulture, out var a) ? a : 0m,
            string.IsNullOrWhiteSpace(currency) ? PayPalCurrencies.Default : currency.Trim().ToUpperInvariant(),
            int.TryParse(days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0,
            (description ?? "").Trim(),
            (cycle ?? "").Trim());

    /// <summary>
    /// <para><b>مُمَيِّزُ الدَورَةِ الَّتي يُشتَرى لَها هذا الطَلَب</b> —
    /// تاريخُ انتِهاءِ الباقَةِ القائِمُ لَحظَةَ إنشاءِ الرابِط.</para>
    ///
    /// <para><b>ولِماذا هذا بِعَينِه لا عَدّادُ طَلَبات</b>: هُوَ
    /// <b>مُشتَقٌّ مِن حالَةٍ قائِمَةٍ ولا يُكتَب</b> — دالَّةٌ نَقِيَّةٌ
    /// بِلا قِراءَةٍ ثانِيَةٍ ولا سِباق. ويَتَحَرَّكُ <b>بِالضَبط</b>
    /// حينَ تُشتَرى دَورَةٌ جَديدَة: التَمديدُ يُحَرِّك
    /// <c>ExpiresAt</c>، فَطَلَبُ الشَهرِ التالي يَحمِلُ مُمَيِّزاً
    /// آخَرَ بِلا تَدَخُّل. ونَقرَتانِ في الدَورَةِ نَفسِها تَبقَيانِ
    /// مَرجِعاً واحِداً — وهُوَ المَقصودُ مِن حَتمِيَّةِ
    /// المَرجِع.</para>
    ///
    /// <para><b>وطَلَبٌ لَم يُدفَع لا يُنشِئ مَرجِعاً ثانِياً</b>: ما
    /// دامَ <c>ExpiresAt</c> لَم يَتَحَرَّك، الرابِطُ المُعادُ إنشاؤُه
    /// يَكتُب فَوقَ وَثيقَتِه نَفسِها — ولا شَيءَ فيها يَضيع.</para>
    /// </summary>
    public static string CycleOf(TenantPlan? plan)
        => plan is null ? "" : plan.ExpiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>القائِمَةُ فارِغَةٌ تَعني مُسَوَّدَةً صالِحَة.</summary>
    public static IReadOnlyList<PayPalOrderViolation> Validate(PayPalOrderDraft? d)
    {
        var v = new List<PayPalOrderViolation>();
        if (d is null)
        {
            v.Add(new(TenantEmpty, "لا مُسَوَّدَةَ طَلَبٍ أَصلاً."));
            return v;
        }

        if (string.IsNullOrWhiteSpace(d.NormalizedSlug))
            v.Add(new(TenantEmpty, "لا مَتجَرَ لِهذا الطَلَب."));

        // **وَثيقَةُ الباقَةِ شَرطٌ لا تَحسين**: رابِطُ دَفعٍ بِلا باقَةٍ
        // مَضبوطَةٍ يُنتِج مالاً يَصِل ولا يُعرَف ماذا يُمَدَّد بِه —
        // والباقَةُ ومُهلَتُها قَرارُ مُشرِفٍ لا يَعرِفُه PayPal
        // (القاعِدَة ١٦).
        if (string.IsNullOrWhiteSpace(d.PlanId))
            v.Add(new(PlanMissing,
                "لا باقَةَ مَضبوطَةٌ لِهذا المَتجَر — اضبِطها أَوَّلاً، فَالتَمديدُ يُحَرِّك تاريخَها."));

        if (d.Amount <= 0m)
            v.Add(new(AmountNotPositive, $"مَبلَغُ الطَلَب {d.Amount} — طَلَبٌ لا يَقبِض شَيئاً."));

        if (!PayPalCurrencies.Contains(d.Currency))
            v.Add(new(CurrencyUnsupported,
                $"العُملَة «{d.NormalizedCurrency}» خارِجَ عُملاتِ المُعامَلَةِ في PayPal " +
                $"(‏{PayPalCurrencies.Supported.Count} عُملَة، ولا SAR فيها). " +
                $"استَعمِل {PayPalCurrencies.Default}."));

        if (d.Days <= 0 || d.Days > MaxDays)
            v.Add(new(DaysOutOfRange,
                $"مُدَّةُ التَمديد {d.Days} يَوماً خارِجَ المَدى 1..{MaxDays}."));

        // الوَصفُ يُقَصّ ولا يُرَدّ — إلّا أَن يَكونَ الفارِقُ فاحِشاً،
        // فَيُقالُ لِلمُشرِفِ إنّ ما كَتَبَه لَن يُعرَض كَما كَتَبَه.
        if ((d.Description ?? "").Trim().Length > MaxDescriptionLength)
            v.Add(new(DescriptionTooLong,
                $"الوَصف {(d.Description ?? "").Trim().Length} مِحرَفاً، والسَقفُ المَعروضُ عِندَ PayPal {MaxDescriptionLength}."));

        return v;
    }

    public static bool IsValid(PayPalOrderDraft? d) => Validate(d).Count == 0;

    // ═══ مَرجِعُنا ومَفاتيحُ مَرَّة-واحِدَة ═══════════════════════════
    //
    // **‏PayPal-Request-Id يَحفَظُه الخادِمُ سِتَّ ساعاتٍ افتِراضاً**
    // (تُمَدَّد إلى ‏72 بِطَلَبٍ مِن مُديرِ الحِساب)، وإعادَةُ النِداءِ
    // بِنَفسِ المِفتاحِ تُرجِع **‏200 بِالجِسمِ الأَصليّ** بَدَلَ ‏201.
    // فَالمِفتاحُ يُشتَقُّ **حَتمِيّاً مِن مُدخَلاتِ الطَلَب** — لا مِن
    // زَمَنٍ ولا عَشوائيَّة:
    //
    //   · نَقرَتانِ على نَفسِ النَموذَج ⇒ نَفسُ المِفتاح ⇒ **طَلَبُ دَفعٍ
    //     واحِد** عِندَ PayPal، ووَثيقَةٌ واحِدَةٌ عِندَنا.
    //   · تَغييرُ المَبلَغِ أَو العُملَةِ أَو المُدَّةِ أَو الوَصف ⇒
    //     مِفتاحٌ آخَر ⇒ طَلَبٌ جَديدٌ حينَ يُرادُ فِعلاً.
    //
    // **والعَطَبُ الَّذي أَصلَحَه هذا**: `PayPalSurface.LinkKey` كانَ
    // `$"plan-link:{slug}:{now:yyyyMMddHHmm}"` — **مِفتاحٌ يَحمِلُ
    // الوَقت**. نَقرَتانِ في دَقيقَتَينِ مُختَلِفَتَينِ كانَتا تُنشِئانِ
    // اشتِراكَين. ومِفتاحُ مَرَّة-واحِدَةٍ مُشتَقٌّ مِن الساعَةِ **لَيسَ
    // مِفتاحَ مَرَّة-واحِدَة**.

    private const string ReferencePrefix = "wsl";
    private const string OrderKeyPrefix   = "wsl-o-";
    private const string CaptureKeyPrefix = "wsl-c-";

    /// <summary>
    /// <para><b>مَرجِعُنا — وهُوَ مُعَرِّفُ وَثيقَةِ الدَفعِ المُعَلَّق
    /// و<c>custom_id</c> مَعاً.</b> يُشتَقُّ حَتمِيّاً مِن كُلِّ ما
    /// يُرسَل، فَنَقرَتانِ تُعطيانِ مَرجِعاً واحِداً ووَثيقَةً واحِدَة.</para>
    ///
    /// <para><b>ويَحمِلُ السلاجَ ظاهِراً لا مَخفِيّاً</b>: مَن يَفتَح
    /// تَقريرَ التَسوِيَةِ عِندَ PayPal يَقرَأُ المَتجَرَ بِعَينِه بَدَلَ
    /// بَصمَةٍ صَمّاء. و<c>custom_id</c> سَقفُه ‏255 <b>ولا يَراهُ
    /// المُشتَري</b>.</para>
    /// </summary>
    public static string Reference(PayPalOrderDraft d)
        => $"{ReferencePrefix}-{d.NormalizedSlug}-{Body(d)}";

    /// <summary>رَأسُ مَرَّة-واحِدَة لِإنشاءِ الطَلَب — ‏30 مِحرَفاً،
    /// تَحتَ حَدِّ المُخَطَّطِ (‏108) وتَحتَ حَدِّ دَليلِ
    /// الـidempotency العامّ (‏38) مَعاً.</summary>
    public static string OrderRequestId(PayPalOrderDraft d)
        => OrderKeyPrefix + Body(d);

    /// <summary>
    /// <para><b>رَأسُ مَرَّة-واحِدَة لِلالتِقاط — وهُنا يَتَحَوَّلُ مِن
    /// تَرَفٍ إلى ضَرورَةٍ مالِيَّة.</b> تَوجيهُ PayPal الصَريح: عِندَ
    /// ‏5xx أَو انقِطاعِ شَبَكَةٍ مِن <c>/capture</c> «‏repeat the same
    /// /capture call at least once, <b>with the same PayPal-Request-Id
    /// header as before</b>». وبِدونِه قَد تُلتَقَطُ الدَفعَةُ
    /// <b>مَرَّتَين</b>.</para>
    ///
    /// <para><b>ولِذلك يُشتَقُّ مِن المَرجِعِ وَحدَه</b> — ثابِتٌ عَبرَ
    /// كُلِّ إعادَةِ مُحاوَلَةٍ لِنَفسِ الطَلَب، مَهما تَبَدَّلَ الحَدَثُ
    /// الَّذي أَيقَظَها.</para>
    /// </summary>
    public static string CaptureRequestId(string reference)
        => CaptureKeyPrefix + PayPalCatalogPolicy.Fingerprint((reference ?? "").Trim());

    // **ومُمَيِّزُ الدَورَةِ داخِلَ البَصمَة** (‏`Cycle`): بِدونِه
    // كانَ التَجديدُ الشَهرِيُّ العاديُّ — نَفسُ المَبلَغِ ونَفسِ
    // المُدَّةِ ونَفسِ الوَصف — يُعطي المَرجِعَ نَفسَه فَيَدهَسُ
    // وَثيقَةَ الشَهرِ السابِقِ ويَمحو `CaptureId`. والحُجَّةُ
    // المَكتوبَةُ يَومَها («‏PayPal-Request-Id يَمنَعُ الطَلَبَ
    // الثاني») **باطِلَةٌ بِنَصِّ المِلَفِّ نَفسِه**: نافِذَتُه سِتُّ
    // ساعاتٍ افتِراضاً، والدَورَةُ شَهر.
    private static string Body(PayPalOrderDraft d)
        => PayPalCatalogPolicy.Fingerprint(
            d.NormalizedSlug, (d.PlanId ?? "").Trim(),
            d.MoneyValue, d.NormalizedCurrency,
            d.Days.ToString(CultureInfo.InvariantCulture),
            d.TrimmedDescription, (d.Cycle ?? "").Trim());

    // ═══ حُرّاسُ الوَثيقَةِ القائِمَة — دَوالُّ نَقِيَّةٌ تَقرَؤُها
    //     الشاشَةُ والنُقطَة مَعاً ═════════════════════════════════════

    /// <summary>
    /// <para><b>أَيُكتَبُ فَوقَ وَثيقَةِ طَلَبٍ قائِمَة؟ — شَبَكَةُ
    /// الأَمانِ تَحتَ مُمَيِّزِ الدَورَة.</b> ‏<c>true</c> حينَ لا
    /// وَثيقَةَ أَصلاً، أَو حينَ <b>لَم يَقَع فيها شَيءٌ لا
    /// يُستَرجَع</b>: لا مُعَرِّفَ التِقاطٍ، وحالَتُها تَنتَظِرُ
    /// الالتِقاطَ بَعد.</para>
    ///
    /// <para><b>ولِماذا حارِسٌ ومُمَيِّزٌ مَعاً وأَحَدُهُما يَكفي
    /// نَظَرِيّاً</b>: المُمَيِّزُ يَمنَع الاصطِدامَ في <b>الحالَةِ
    /// المَعروفَة</b> (تَجديدٌ بَعدَ تَمديد)، والحارِسُ يَمنَعُه في
    /// <b>ما لَم يُتَوَقَّع</b> — تاريخُ انتِهاءٍ أُعيدَ بِاليَدِ إلى
    /// قيمَتِه السابِقَة، أَو وَثيقَةٌ أُنشِئَت قَبلَ هذا التَغيير.
    /// والفارِقُ بَينَ الاثنَينِ هُوَ الفارِقُ بَينَ «لا يَقَع» و«لا
    /// يُمكِنُ أَن يَقَع».</para>
    /// </summary>
    public static bool IsOverwritable(PayPalOrderRecord? existing)
        => existing is null
           || (string.IsNullOrWhiteSpace(existing.CaptureId)
               && PayPalOrderStatuses.AwaitsCapture(existing.Status));

    /// <summary>
    /// <para><b>أَيُنادى الالتِقاطُ لِهذا الطَلَبِ الآن؟</b> — وهُوَ
    /// <b>شَرطُ رَسمِ زِرِّ «التَقِط الآن» وشَرطُ قَبولِ نُقطَتِه
    /// مَعاً</b>، مِن مَوضِعٍ واحِدٍ لا مِن قاعِدَتَينِ تَنجَرِفان.</para>
    ///
    /// <para><b>ومُعَرِّفُ الالتِقاطِ هُوَ الفَيصَل، لا الحالَةُ
    /// وَحدَها</b>: نِداءٌ ناجِحٌ يُثَبِّتُ <c>CaptureId</c> بَينَما
    /// تَبقى الحالَةُ <c>approved</c> حَتّى تَصِلَ رِسالَةُ
    /// <c>COMPLETED</c> (فَلا يَكتُبُ <c>captured</c> إلّا مَن
    /// يُمَدِّد). فَلَو كانَ الشَرطُ الحالَةَ وَحدَها لَبَقِيَ الزِرُّ
    /// مَرسوماً بَعدَ نَجاحِه — <b>والنَقرَةُ الثانِيَةُ تَرتَدُّ مِن
    /// PayPal بِـ<c>ORDER_ALREADY_CAPTURED</c> إنجِليزِيّاً خامّاً</b>.
    /// (القاعِدَة ١٢: مَدخَلٌ يَضُرُّ أَسوَأُ مِن غِيابِ مَدخَل.)</para>
    /// </summary>
    public static bool CanCaptureNow(PayPalOrderRecord? order)
        => order is not null
           && !string.IsNullOrWhiteSpace(order.OrderId)
           && string.IsNullOrWhiteSpace(order.CaptureId)
           && PayPalOrderStatuses.AwaitsCapture(order.Status);

    // ═══ جِسمُ النِداءِ ورابِطاه ═══════════════════════════════════════

    /// <summary>رابِطُ العَودَةِ لِهذا المَرجِع — <b>ولَيسَ اختِيارِيّاً
    /// واقِعاً</b>: وَصفُ <c>links</c> في المُواصَفَةِ يَقول إنّ إغفالَه
    /// يُري المُشتَريَ بَعدَ مُوافَقَتِه «‏We're sorry, Things don't
    /// appear to be working at the moment» — أَي الدَفعُ يَنجَح
    /// والمُشتَرِكُ يُترَكُ على شاشَةِ عَطَب.</summary>
    public static string ReturnUrl(string origin, string reference)
        => $"{Trim(origin)}{ReturnPath}?{ReferenceQueryKey}={Uri.EscapeDataString(reference)}";

    public static string CancelUrl(string origin, string reference)
        => $"{Trim(origin)}{CancelPath}?{ReferenceQueryKey}={Uri.EscapeDataString(reference)}";

    private static string Trim(string? origin) => (origin ?? "").TrimEnd('/');

    /// <summary>
    /// <para><b>جِسمُ إنشاءِ الطَلَبِ كامِلاً — دالَّةٌ نَقِيَّةٌ
    /// يَقرَؤُها الاختِبارُ بَدَلَ أَن يَنسَخَها.</b></para>
    ///
    /// <para><b>وثَلاثَةُ حُقولٍ غائِبَةٌ عَمداً، ولِكُلٍّ سَبَبُه</b>:</para>
    /// <list type="bullet">
    ///   <item><c>breakdown</c> — إن وُضِعَ وَجَبَ أَن يَتَوازَن
    ///   (<c>item_total + tax + … = value</c>) وإلّا ‏422. مَصدَرُ
    ///   أَعطابٍ بِلا مُقابِلٍ لِسَطرٍ واحِد.</item>
    ///   <item><c>application_context</c> و<c>payer</c> —
    ///   <b>مَهجورَتانِ بِالكامِل</b> مُنذُ ‏2.9، والبَديلُ
    ///   <c>payment_source.paypal.experience_context</c>.</item>
    ///   <item><c>invoice_id</c> — <b>مَرجِعُنا حَتميٌّ لا زَمَنيّ</b>،
    ///   فَطَلَبٌ ثانٍ بِنَفسِ الحُقولِ بَعدَ انقِضاءِ نافِذَةِ
    ///   الـidempotency كانَ يَرتَدُّ بِـ<c>DUPLICATE_INVOICE_ID</c>.
    ///   والحَقلُ اختِياريّ، والمَرجِعُ يَصِلُنا في <c>custom_id</c>
    ///   و<c>reference_id</c> مَعاً.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<string, object> CreateBody(
        PayPalOrderDraft draft, string reference, string origin)
    {
        var unit = new Dictionary<string, object>
        {
            ["reference_id"] = reference,
            ["amount"] = new Dictionary<string, object>
            {
                // سِلسِلَةٌ نَصِّيَّةٌ لا رَقَم — نَمَطُ PayPal يَشتَرِط
                // ذلك حَرفاً: ^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$
                ["currency_code"] = draft.NormalizedCurrency,
                ["value"]         = draft.MoneyValue,
            },
            ["custom_id"]       = reference,
            ["soft_descriptor"] = SoftDescriptor,
        };

        // وَصفٌ فارِغٌ لا يُرسَل: حَقلٌ اختِياريٌّ يُملَأُ بِنَصٍّ
        // مُخترَعٍ بَياناتُ صَفقَةٍ لا تُخترَع (القاعِدَة ١٦).
        if (draft.TrimmedDescription.Length > 0)
            unit["description"] = draft.TrimmedDescription;

        return new Dictionary<string, object>
        {
            ["intent"] = Intent,
            ["purchase_units"] = new object[] { unit },
            ["payment_source"] = new Dictionary<string, object>
            {
                ["paypal"] = new Dictionary<string, object>
                {
                    ["experience_context"] = new Dictionary<string, object>
                    {
                        ["brand_name"]                = BrandName,
                        ["locale"]                    = Locale,
                        ["landing_page"]              = LandingPage,
                        ["shipping_preference"]       = ShippingPreference,
                        ["user_action"]               = UserAction,
                        ["payment_method_preference"] = PaymentMethodPreference,
                        ["return_url"]                = ReturnUrl(origin, reference),
                        ["cancel_url"]                = CancelUrl(origin, reference),
                    },
                },
            },
        };
    }
}
