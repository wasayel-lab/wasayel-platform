using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.Paddle;

// ═══ المُعامَلَةُ تُنشَأُ مِن الخادِم، والدَفعُ بِلا JavaScript عِندَنا ═
//
// **الشَكلُ المَقيسُ مِن وَثائِق Paddle**: تُنشَأُ المُعامَلَةُ بِـ
// `POST /transactions` فَتُرجِعُ رابِطَ دَفعٍ = **رابِطُ الدَفعِ
// الافتِراضيّ** + `?_ptxn=<txn_id>`. والرابِطُ الافتِراضيُّ
// **صَفحَةٌ نَستَضيفُها نَحن** وفيها `paddle.js` تَفتَح المُعامَلَةَ
// مِن ذلك المُعامِل.
//
// **فَلا React ولا npm ولا مُكَوِّنَ Blazor تَفاعُليّ**: صَفحَةٌ
// ساكِنَةٌ واحِدَةٌ في `wwwroot`. والبَديلُ (أَن تَستَضيفَ Paddle
// الصَفحَةَ) **يَحتاج مُوافَقَةً إضافِيَّةً لِلوَضعِ المُباشِر**،
// فَلا يُبنى عَلَيه أَساسٌ ويُذكَرُ خِياراً في `docs/DEPLOY.md`.

/// <summary>
/// <para><b>العُملَةُ حَقلٌ لا مَعجَمٌ مُغلَق — ويُقالُ لِماذا.</b>
/// قائِمَةُ عُملاتِ Paddle لَم تُقَس في هذِه الجَولَة، و<b>قائِمَةٌ
/// مُخترَعَةٌ تَرفُض عُملَةً مَقبولَة</b> أَسوَأُ مِن غِيابِ قائِمَة
/// (القاعِدَة ١٦). فَالمُصادِقُ يَفحَص <b>الشَكلَ</b> — ثَلاثَةُ
/// حُروفٍ لاتينِيَّة — وتَرُدُّ Paddle نَفسُها ما لا تَقبَل،
/// <b>ونَصُّها يُعرَض كَما هُوَ</b>.</para>
///
/// <para><b>والمَبلَغُ يُرسَل بِأَصغَرِ وَحدَةٍ في العُملَة</b> نَصّاً
/// (‏<c>"4900"</c> لِـ‏49.00 دولاراً). فَالتَحويلُ يَحتاج أُسَّ
/// العُملَة، و<see cref="ZeroDecimal"/> هي القائِمَةُ الوَحيدَةُ
/// المَكتوبَة.</para>
/// </summary>
public static class PaddleCurrencies
{
    /// <summary>الافتِراضُ — لا لِأَنَّه الأَفضَل بَل لِأَنَّه
    /// <b>الوَحيدُ الَّذي لا يَحمِل احتِمالَ رَفضٍ على مُستَوى دَولَةِ
    /// البائِع</b>، ونَفسُ افتِراضِ مَسارِ PayPal فَلا يَختَلِف
    /// حَقلانِ على شاشَةٍ واحِدَة.</summary>
    public const string Default = "USD";

    /// <summary>
    /// <para><b>عُملاتٌ بِلا كُسورٍ — أُسُّها صِفر.</b> مِن ISO 4217
    /// (وَحدَةٌ صُغرى بِلا أَجزاء).</para>
    ///
    /// <para><b>ودَينٌ مُعلَنٌ يُقالُ بِاسمِه</b>: هذِه القائِمَةُ
    /// <b>لَم تُقارَن بِجَدوَلِ Paddle</b> في هذِه الجَولَة. وأَثَرُ
    /// خَطَإٍ فيها <b>مَرئيٌّ قَبلَ المال لا بَعدَه</b>: المَبلَغُ
    /// المُرسَلُ بِأَصغَرِ وَحدَةٍ <b>يُعرَض في شاشَةِ المُشرِفِ
    /// نَصّاً</b> بِجِوارِ ما كَتَب، فَخَطَأٌ بِمِئَةِ ضِعفٍ يُرى قَبلَ
    /// إرسالِ الرابِط. ولا يُمَدَّدُ شَيءٌ بِمَبلَغٍ لا يُطابِق
    /// المَحفوظ على أَيّ حال.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ZeroDecimal =
        new[] { "JPY", "KRW", "VND", "CLP", "ISK" };

    /// <summary>أُسُّ العُملَة — صِفرٌ لِلمَعدودَةِ أَعلاه، واثنانِ
    /// لِما عَداها. <b>ولا يُخمَّن ثَلاثَة</b>: عُملاتُ الخَليجِ
    /// ذاتُ الفِلسِ الثُلاثيِّ (‏KWD/BHD/OMR) لَم يُقَس دُخولُها في
    /// Paddle أَصلاً، وقيمَةٌ مُخترَعَةٌ هُنا تَعني خَطَأً
    /// بِعَشرَةِ أَضعاف.</summary>
    public static int Exponent(string? currency)
        => ZeroDecimal.Contains((currency ?? "").Trim().ToUpperInvariant(), StringComparer.Ordinal)
            ? 0 : 2;

    /// <summary>أَشَكلُها شَكلُ رَمزِ عُملَة؟ ثَلاثَةُ حُروفٍ
    /// لاتينِيَّة — <b>فَحصُ شَكلٍ لا فَحصُ قَبول</b>.</summary>
    public static bool LooksLikeCode(string? code)
    {
        var c = (code ?? "").Trim();
        if (c.Length != 3) return false;
        foreach (var ch in c) if (ch is < 'A' or > 'Z' && ch is < 'a' or > 'z') return false;
        return true;
    }

    /// <summary>
    /// <para><b>أَنماطُ قِراءَةِ مَبلَغٍ نَصّاً — نَفسُ حُجَّةِ
    /// <c>PayPalCurrencies.MoneyStyles</c> حَرفاً</b>، ومَنقولَةٌ هُنا
    /// لِأَنّ الكِتَّينِ لا يَعتَمِد أَحَدُهُما على الآخَر:
    /// <c>NumberStyles.Number</c> تَحمِل <c>AllowThousands</c>،
    /// فَـ<c>"49,99"</c> — كِتابَةُ نِصفِ أوروبّا لِـ‏49.99 — تُقرَأُ
    /// <b>‏4999</b>. و<c>AllowLeadingSign</c> ساقِطَةٌ كَذلك: مَبلَغٌ
    /// سالِبٌ لَيسَ مَبلَغاً.</para>
    /// </summary>
    public const NumberStyles MoneyStyles =
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>
    /// <para><b>المَبلَغُ بِأَصغَرِ وَحدَة، نَصّاً صَحيحاً.</b>
    /// و<c>MidpointRounding.AwayFromZero</c> لا الافتِراضِيَّةُ
    /// المَصرِفِيَّة: <b>نِصفُ قِرشٍ يُقَرَّبُ لِصالِحِ البائِع
    /// دائِماً</b>، لا مَرَّةً صُعوداً ومَرَّةً نُزولاً بِحَسَبِ
    /// زَوجِيَّةِ الرَقَم — وذاكَ يَجعَل مَبلَغَينِ مُتَساوِيَينِ
    /// يُرسَلانِ مُختَلِفَين.</para>
    /// </summary>
    public static string Minor(decimal amount, string? currency)
        => decimal.Round(amount * Pow10(Exponent(currency)), 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    private static decimal Pow10(int exponent)
    {
        var v = 1m;
        for (var i = 0; i < exponent; i++) v *= 10m;
        return v;
    }
}

/// <summary>
/// <para><b>حالاتُ وَثيقَةِ المُعامَلَة — مَعجَمٌ مُغلَقٌ عِندَنا لا
/// عِندَ Paddle.</b> ‏Paddle لَها مَعجَمُها (‏<c>draft/ready/billed/
/// paid/completed/canceled/past_due</c>)، <b>وهذا مَعجَمُنا نَحن</b>:
/// أَربَعُ حالاتٍ تَكفي لِلقَرارِ الَّذي نَتَّخِذُه، وقِراءَةُ
/// مَعجَمِ طَرَفٍ ثالِثٍ حَرفاً تَجعَل كُلَّ إضافَةٍ عِندَه تَغييراً
/// عِندَنا.</para>
/// </summary>
public static class PaddleTransactionStatuses
{
    /// <summary>أُنشِئَت ولَم يُدفَع بَعد.</summary>
    public const string Created = "created";

    /// <summary><b>وَصَلَ المال</b> — وهي الكَلِمَةُ الَّتي لا
    /// يَكتُبُها إلّا مَن مَدَّدَ فِعلاً.</summary>
    public const string Completed = "completed";

    /// <summary>أُلغِيَت قَبلَ الدَفع — لا مَساسَ بِالباقَة.</summary>
    public const string Canceled = "canceled";

    /// <summary>عادَ المالُ — استِردادٌ أَو ردٌّ قَضائيّ.</summary>
    public const string Refunded = "refunded";

    public static readonly IReadOnlyList<string> All =
        new[] { Created, Completed, Canceled, Refunded };

    /// <summary>أَتَنتَظِرُ هذِه الحالَةُ دَفعاً؟ — يَقرَؤُها الاستوديو
    /// فَلا يُعرَض رابِطٌ لِمُعامَلَةٍ حُسِمَت.</summary>
    public static bool AwaitsPayment(string? status)
        => string.Equals((status ?? "").Trim(), Created, StringComparison.Ordinal);

    /// <summary>
    /// <para><b>جَدوَلُ الانتِقالاتِ — اتِّجاهٌ واحِد.</b> ونَفسُ
    /// السَبَبِ الَّذي كَتَبَ جَدوَلَ PayPal حَرفاً: حَدَثٌ
    /// مُتَأَخِّرٌ أَو مُكَرَّرٌ يَهبِط بِالحالَةِ مِن «وَصَلَ المال»
    /// إلى «أُنشِئَ»، ثُمَّ يَصِلُ الاستِردادُ فَيَجِدُ حارِسَ السَحبِ
    /// يَشتَرِط «وَصَلَ المال» — <b>فَلا يُسحَبُ شَيء: المالُ يَعودُ
    /// والأَيّامُ تَبقى</b>.</para>
    /// </summary>
    public static bool CanTransition(string? from, string? to)
    {
        var f = (from ?? "").Trim();
        var t = (to ?? "").Trim();

        if (string.IsNullOrEmpty(t) || !All.Contains(t, StringComparer.Ordinal)) return false;
        if (string.Equals(f, t, StringComparison.Ordinal)) return true;

        return (f, t) switch
        {
            (Created,   Completed) => true,
            (Created,   Canceled)  => true,
            (Completed, Refunded)  => true,
            _                      => false
        };
    }
}

/// <summary>
/// <para><b>ما يَملَؤُه المُشرِفُ في النَموذَج.</b> ولا قيمَةَ
/// افتِراضِيَّةً لِمَبلَغٍ ولا لِمُدَّة (القاعِدَة ١٦)، والعُملَةُ
/// وَحدَها تَحمِل افتِراضاً.</para>
/// </summary>
/// <param name="TenantSlug">مِن المَسارِ لا مِن النَموذَج — ولَو
/// قُرِئَ مِن الطَلَبِ لَمَدَّدَ مُتَصَفِّحٌ باقَةَ مَتجَرٍ لَم
/// يَخترهُ المُشرِف.</param>
/// <param name="PlanId">مِن وَثيقَةِ باقَةِ المَتجَرِ في الخادِم —
/// <b>لَقطَةٌ لِلتَدقيقِ لا قَرار</b>.</param>
/// <param name="Days">كَم يَوماً تُمَدَّدُ الباقَةُ عِندَ وُصولِ
/// المال — <b>ورِسالَةُ Paddle لا تَقولُ مُدَّةً إطلاقاً</b>، فَهذا
/// هُوَ المَصدَرُ الوَحيد.</param>
/// <param name="Cycle">مُمَيِّزُ الدَورَة — تاريخُ انتِهاءِ الباقَةِ
/// لَحظَةَ الإنشاء. <b>ولَولاه لَكانَ تَجديدُ الشَهرِ التالي بِنَفسِ
/// المُدخَلاتِ يُعطي المَرجِعَ نَفسَه فَيَدهَسُ وَثيقَةَ الشَهرِ
/// السابِق</b> — نَفسُ عِلَّةِ <c>PayPalOrderDraft.Cycle</c>
/// حَرفاً.</param>
public sealed record PaddleTransactionDraft(
    string  TenantSlug,
    string  PlanId,
    decimal Amount,
    string  Currency,
    int     Days,
    string  Description,
    string  Cycle)
{
    public string NormalizedSlug => (TenantSlug ?? "").Trim().ToLowerInvariant();

    /// <summary>العُملَةُ مُطَبَّعَةً — تُرسَل كَبيرَةً دائِماً.</summary>
    public string NormalizedCurrency => (Currency ?? "").Trim().ToUpperInvariant();

    public string TrimmedDescription
    {
        get
        {
            var d = (Description ?? "").Trim();
            return d.Length <= PaddleTransactionPolicy.MaxDescriptionLength
                ? d
                : d[..PaddleTransactionPolicy.MaxDescriptionLength];
        }
    }

    /// <summary><b>المَبلَغُ كَما يُرسَل — بِأَصغَرِ وَحدَةٍ
    /// نَصّاً.</b> وهُوَ بِعَينِه ما يُخَزَّن وما يُقارَنُ بِالواصِل،
    /// <b>فَتَعريفٌ واحِدٌ لا ثَلاثَة</b>.</summary>
    public string MinorAmount => PaddleCurrencies.Minor(Amount, NormalizedCurrency);
}

/// <summary>خَرقٌ واحِدٌ في مُسَوَّدَةِ مُعامَلَة — <c>Code</c> ثابِتٌ
/// لِلاختِبارِ ولِلقامُوس، و<c>MessageAr</c> لِلوغ. نَفسُ شَكلِ
/// <c>PayPalOrderViolation</c> حَرفاً.</summary>
public sealed record PaddleTransactionViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>نَتيجَةُ إنشاءِ المُعامَلَة.</b> <c>CheckoutUrl</c> هُوَ
/// <b>رابِطُ الدَفع</b> — وهُوَ المَقصودُ كُلُّه.</para>
///
/// <para><b>ولا رَميَ عِندَ الفَشَلِ بَل سَبَبٌ مُسَمّى</b>: تُنادى
/// مِن نُقطَةِ نَموذَجٍ تُحَوِّل، فَتُعيد نَصَّ Paddle كَما هُوَ —
/// و«فَشِلَ الإنشاء» وَحدَها تُرسِل المُشرِفَ يُخَمِّن.</para>
/// </summary>
public sealed record PaddleTransactionResult(
    string  TransactionId,
    string  Status,
    string? CheckoutUrl,
    string? FailureReason);

/// <summary>
/// <para><b>بَوّابَةُ المُعامَلَةِ ومَرجِعُنا وجِسمُ النِداء — دَوالُّ
/// نَقِيَّة.</b> لا HTTP ولا وَقتَ ولا عَشوائيَّة، فَتُقاس بِجَدوَل.</para>
/// </summary>
public static class PaddleTransactionPolicy
{
    // ─── رُموزُ الخَرق — مَعجَمٌ مُغلَقٌ يَقرَؤُه القامُوسُ والاختِبار ─
    public const string TenantEmpty        = "paddle_tx_tenant_empty";
    public const string PlanMissing        = "paddle_tx_plan_missing";
    public const string AmountNotPositive  = "paddle_tx_amount_not_positive";
    public const string CurrencyMalformed  = "paddle_tx_currency_malformed";
    public const string DaysOutOfRange     = "paddle_tx_days_out_of_range";
    public const string DescriptionTooLong = "paddle_tx_description_too_long";

    /// <summary>
    /// <para><b>سَقفُ المُدَّة — حارِسُ إدخالٍ لا سِياسَةُ
    /// تَسعير.</b> صِفرٌ زائِدٌ بِالخَطَإ يَشتَري عَشرَ سِنينَ بِثَمَنِ
    /// شَهر، ولا فاحِصَ بَعدَه.</para>
    ///
    /// <para><b>وهُوَ نَفسُ رَقَمِ <c>PayPalOrderPolicy.MaxDays</c>
    /// مَكتوباً هُنا ثانِيَةً — ويُقالُ لِماذا لَم يُستَخرَج</b>:
    /// الكِتّانِ أَخَوانِ لا يَعتَمِد أَحَدُهُما على الآخَر، واستِخراجُه
    /// إلى ثالِثٍ يُنشِئ مَشروعاً لِثابِتٍ واحِد. والانحِرافُ
    /// المُحتَمَل (أَن يَصيرَ سَقفُ مُزَوِّدٍ ‏730 والآخَرِ ‏365)
    /// <b>لا يُنتِج عَطَباً ماليّاً</b>: كِلاهُما حارِسُ إدخالٍ عَلى
    /// شاشَتِه، والمُدَّةُ المَحفوظَةُ هي المَقروءَةُ عِندَ
    /// التَمديد.</para>
    /// </summary>
    public const int MaxDays = 730;

    /// <summary>
    /// <para><b>سَقفُ الوَصف — سَقفُنا نَحنُ لا سَقفُ Paddle.</b>
    /// حَدُّ Paddle لِوَصفِ السِعرِ لَم يُقَس في هذِه الجَولَة،
    /// و<b>رَقَمٌ مُخترَعٌ مَنسوبٌ إلَيها</b> أَسوَأُ مِن رَقَمٍ
    /// مَنسوبٍ إلَينا: الأَوَّلُ يُصَدَّقُ ولا يُراجَع.</para>
    /// </summary>
    public const int MaxDescriptionLength = 200;

    /// <summary><b>مِفتاحُ مَرجِعِنا داخِلَ <c>custom_data</c></b> —
    /// مَوضِعٌ واحِدٌ يَكتُبُه مُنشِئُ الجِسمِ ويَقرَؤُه قارِئُ
    /// الحَدَث. واسمانِ يَنجَرِفانِ يَجعَلانِ كُلَّ رِسالَةٍ «مَرجِعاً
    /// مَجهولاً».</summary>
    public const string ReferenceKey = "wasayel_ref";

    /// <summary><b>وَضعُ التَحصيل</b> — تِلقائيّ: يَدفَع الزائِرُ
    /// بِالبِطاقَةِ على الصَفحَةِ فَوراً، لا فاتورَةٌ تُرسَل
    /// وتُنتَظَر.</summary>
    public const string CollectionMode = "automatic";

    /// <summary>صِنفُ الضَريبَةِ لِلمُنتَجِ المُرتَجَل — <c>standard</c>.
    /// <b>وهُوَ اختِيارُنا الواعي لا افتِراضٌ مَخفِيّ</b>: باقَةُ
    /// مَنَصَّةٍ لَيسَت كِتاباً ولا خِدمَةً مُعفاة، وتاجِرُ التَسجيلِ
    /// هُوَ مَن يَحسِبُ الضَريبَةَ عَلَيها.</summary>
    public const string TaxCategory = "standard";

    /// <summary>مُعامِلُ المُعامَلَةِ في رابِطِ الدَفع — <b>اسمٌ
    /// تَفرِضُه Paddle</b>، ويُقرَأُ في صَفحَتِنا الساكِنَة.</summary>
    public const string TransactionQueryKey = "_ptxn";

    /// <summary>مُعامِلُ مَرجِعِنا في رابِطِ العَودَة — <b>اسمُنا
    /// نَحن</b>، ونَفسُ اسمِ مَسارِ PayPal فَلا تَتَعَلَّم صَفحَتانِ
    /// اسمَينِ لِشَيءٍ واحِد.</summary>
    public const string ReferenceQueryKey = "ref";

    /// <summary>مَسارُ صَفحَةِ العَودَةِ عِندَنا — قِراءَةٌ خالِصَة.</summary>
    public const string ReturnPath = "/billing/paddle/return";

    /// <summary>
    /// <para><b>قِراءَةُ النَموذَج — ولا يُسقَطُ حَقلٌ إلى قيمَةٍ
    /// مُخترَعَة.</b> نَصٌّ غَيرُ مَقروءٍ يُعطي صِفراً <b>فَيَرتَدُّ
    /// بِخَرقٍ مُسَمّى</b>، ولا يُخمَّنُ لَه رَقَم. والعُملَةُ
    /// الغائِبَةُ وَحدَها تَرتَدُّ إلى
    /// <see cref="PaddleCurrencies.Default"/>.</para>
    /// </summary>
    public static PaddleTransactionDraft ReadDraft(
        string? tenantSlug, string? planId,
        string? amount, string? currency, string? days, string? description, string? cycle)
        => new(
            (tenantSlug ?? "").Trim().ToLowerInvariant(),
            (planId ?? "").Trim(),
            decimal.TryParse(amount, PaddleCurrencies.MoneyStyles, CultureInfo.InvariantCulture, out var a) ? a : 0m,
            string.IsNullOrWhiteSpace(currency) ? PaddleCurrencies.Default : currency.Trim().ToUpperInvariant(),
            int.TryParse(days, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0,
            (description ?? "").Trim(),
            (cycle ?? "").Trim());

    /// <summary>مُمَيِّزُ الدَورَة — تاريخُ الانتِهاءِ القائِمُ لَحظَةَ
    /// الإنشاء. <b>مُشتَقٌّ مِن حالَةٍ قائِمَةٍ ولا يُكتَب</b>، ونَفسُ
    /// اشتِقاقِ <c>PayPalOrderPolicy.CycleOf</c>.</summary>
    public static string CycleOf(TenantPlan? plan)
        => plan is null ? "" : plan.ExpiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>القائِمَةُ فارِغَةٌ تَعني مُسَوَّدَةً صالِحَة.</summary>
    public static IReadOnlyList<PaddleTransactionViolation> Validate(PaddleTransactionDraft? d)
    {
        var v = new List<PaddleTransactionViolation>();
        if (d is null)
        {
            v.Add(new(TenantEmpty, "لا مُسَوَّدَةَ مُعامَلَةٍ أَصلاً."));
            return v;
        }

        if (string.IsNullOrWhiteSpace(d.NormalizedSlug))
            v.Add(new(TenantEmpty, "لا مَتجَرَ لِهذِه المُعامَلَة."));

        // **وَثيقَةُ الباقَةِ شَرطٌ لا تَحسين**: رابِطُ دَفعٍ بِلا
        // باقَةٍ مَضبوطَةٍ يُنتِج مالاً يَصِل ولا يُعرَف ماذا يُمَدَّدُ
        // بِه — والباقَةُ ومُهلَتُها قَرارُ مُشرِفٍ لا يَعرِفُه Paddle.
        if (string.IsNullOrWhiteSpace(d.PlanId))
            v.Add(new(PlanMissing,
                "لا باقَةَ مَضبوطَةٌ لِهذا المَتجَر — اضبِطها أَوَّلاً، فَالتَمديدُ يُحَرِّك تاريخَها."));

        if (d.Amount <= 0m)
            v.Add(new(AmountNotPositive, $"مَبلَغُ المُعامَلَة {d.Amount} — مُعامَلَةٌ لا تَقبِض شَيئاً."));

        if (!PaddleCurrencies.LooksLikeCode(d.Currency))
            v.Add(new(CurrencyMalformed,
                $"رَمزُ العُملَة «{d.NormalizedCurrency}» لَيسَ ثَلاثَةَ حُروفٍ لاتينِيَّة. " +
                $"والافتِراضُ {PaddleCurrencies.Default}."));

        if (d.Days <= 0 || d.Days > MaxDays)
            v.Add(new(DaysOutOfRange,
                $"مُدَّةُ التَمديد {d.Days} يَوماً خارِجَ المَدى 1..{MaxDays}."));

        if ((d.Description ?? "").Trim().Length > MaxDescriptionLength)
            v.Add(new(DescriptionTooLong,
                $"الوَصف {(d.Description ?? "").Trim().Length} مِحرَفاً، والسَقفُ {MaxDescriptionLength}."));

        return v;
    }

    public static bool IsValid(PaddleTransactionDraft? d) => Validate(d).Count == 0;

    // ═══ مَرجِعُنا ═══════════════════════════════════════════════════

    private const string ReferencePrefix = "wsl-pd";
    private const string FieldSeparator  = "|";

    /// <summary>
    /// <para><b>مَرجِعُنا — وهُوَ مُعَرِّفُ وَثيقَةِ المُعامَلَةِ
    /// المُعَلَّقَةِ وقيمَةُ <c>custom_data</c> مَعاً.</b> يُشتَقُّ
    /// حَتمِيّاً مِن كُلِّ ما يُرسَل، <b>فَنَقرَتانِ تُعطيانِ مَرجِعاً
    /// واحِداً ووَثيقَةً واحِدَة</b>.</para>
    ///
    /// <para><b>ويَحمِلُ السلاجَ ظاهِراً لا مَخفِيّاً</b>: مَن يَفتَح
    /// تَقريرَ Paddle يَقرَأُ المَتجَرَ بِعَينِه بَدَلَ بَصمَةٍ
    /// صَمّاء.</para>
    /// </summary>
    public static string Reference(PaddleTransactionDraft d)
        => $"{ReferencePrefix}-{d.NormalizedSlug}-{Body(d)}";

    private static string Body(PaddleTransactionDraft d)
        => Fingerprint(
            d.NormalizedSlug, d.PlanId, d.MinorAmount, d.NormalizedCurrency,
            d.Days.ToString(CultureInfo.InvariantCulture), d.TrimmedDescription, d.Cycle);

    /// <summary>بَصمَةٌ حَتمِيَّةٌ ثابِتَةٌ عَبرَ العَمَلِيّات —
    /// <c>SHA-256</c> لا <c>string.GetHashCode</c>، فَبَذرَةُ الأَخيرَةِ
    /// تَتَبَدَّل مَعَ كُلِّ عَمَلِيَّة (‏<c>StableHashTests</c>).</summary>
    public static string Fingerprint(params string[] parts)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(FieldSeparator, parts))), 0, 12)
            .ToLowerInvariant();

    /// <summary>
    /// <para><b>أَيُكتَبُ فَوقَ وَثيقَةٍ قائِمَةٍ بِهذا المَرجِع؟</b>
    /// — <c>true</c> حينَ لا وَثيقَةَ، أَو حينَ لا تَزالُ تَنتَظِرُ
    /// دَفعاً.</para>
    ///
    /// <para><b>والعِلَّة</b>: المِفتاحُ هُوَ المَرجِع، والكِتابَةُ
    /// <c>Store</c>. فَوَثيقَةٌ بَلَغَت «وَصَلَ المال» تَحمِل
    /// مُعَرِّفَ المُعامَلَةِ الَّذي يَربِط أَيَّ استِردادٍ لاحِق،
    /// ودَهسُها يُغلِقُ بابَ السَحبِ إلى الأَبَد.</para>
    /// </summary>
    public static bool IsOverwritable(PaddleTransactionRecord? existing)
        => existing is null
           || PaddleTransactionStatuses.AwaitsPayment(existing.Status);

    // ═══ رابِطُ الدَفع ════════════════════════════════════════════════

    /// <summary>
    /// <para><b>رابِطُ الدَفع = صَفحَتُنا + <c>?_ptxn=</c> + مَرجِعُنا.</b>
    /// و<c>null</c> حينَ لا مُعَرِّفَ مُعامَلَةٍ أَو لا صَفحَةَ
    /// مَضبوطَة — <b>ولا يُبنى رابِطٌ ناقِصٌ يُرسَل ثُمَّ لا يُفتَح</b>.</para>
    ///
    /// <para><b>وما تُعيدُه Paddle يَفوز</b> حينَ تُعيدُه: هي أَعلَمُ
    /// بِرابِطِ الدَفعِ الافتِراضيِّ المُسَجَّلِ في لَوحَتِها، وقيمَتُنا
    /// نُسخَةٌ قَد تَنجَرِف. <b>ومَرجِعُنا يُلحَقُ في الحالَتَين</b>
    /// لِتَقولَ صَفحَةُ العَودَةِ شَيئاً.</para>
    /// </summary>
    public static string? CheckoutUrl(
        string? defaultPaymentLink, string? paddleCheckoutUrl, string? transactionId, string? reference)
    {
        var withTxn = !string.IsNullOrWhiteSpace(paddleCheckoutUrl)
            ? paddleCheckoutUrl!.Trim()
            : !string.IsNullOrWhiteSpace(defaultPaymentLink) && !string.IsNullOrWhiteSpace(transactionId)
                ? Append(defaultPaymentLink!.Trim(), TransactionQueryKey, transactionId!.Trim())
                : null;

        if (withTxn is null) return null;
        return string.IsNullOrWhiteSpace(reference)
            ? withTxn
            : Append(withTxn, ReferenceQueryKey, reference!.Trim());
    }

    /// <summary>إلحاقُ مُعامِلٍ بِعُنوانٍ قَد يَحمِل مُعامِلاتٍ سَلَفاً
    /// — <b>الفاصِلُ يُحسَبُ ولا يُفتَرَض</b>: عُنوانٌ فيه
    /// <c>?</c> ثُمَّ <c>?</c> أُخرى يُصبِح جُزءاً مِن قيمَةٍ لا
    /// مُعامِلاً.</summary>
    private static string Append(string url, string key, string value)
        => url + (url.Contains('?') ? '&' : '?') + key + "=" + Uri.EscapeDataString(value);

    // ═══ جِسمُ النِداء ════════════════════════════════════════════════

    /// <summary>
    /// <para><b>جِسمُ <c>POST /transactions</c> — دالَّةٌ نَقِيَّةٌ
    /// تُقاسُ بِلا شَبَكَة.</b></para>
    ///
    /// <para><b>وسِعرٌ مُرتَجَلٌ لا سِعرُ كاتالوج</b>: الباقَةُ
    /// تُباعُ بِمَبلَغٍ يُقَرِّرُه المُشرِفُ لَحظَةَ الطَلَب، فَلا
    /// مُنتَجَ مُعَرَّفٌ سَلَفاً ولا سِعرٌ مَحفوظٌ عِندَ Paddle —
    /// <b>وذاكَ هُوَ المَقصودُ كُلُّه</b> (لا خُطَّةَ مُسبَقَةً ولا
    /// صَفحَةَ لَوحَة). ولِذلك يُرسَل <c>product</c> مُضَمَّناً
    /// بِاسمٍ وصِنفِ ضَريبَة.</para>
    ///
    /// <para><b>و<c>quantity</c> واحِدٌ مَحبوسٌ بِحَدَّين</b>: باقَةُ
    /// مَنَصَّةٍ لا تُشتَرى «‏3 مَرّات»، وسَقفٌ مَفتوحٌ يَجعَل الدافِعَ
    /// يَرفَع العَدَدَ على الصَفحَةِ فَيَدفَع ثَلاثَةَ أَضعافٍ
    /// <b>ولا يُمَدَّدُ شَيء</b> — المَبلَغُ الواصِلُ لا يُطابِقُ
    /// المَحفوظ.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, object> CreateBody(
        PaddleTransactionDraft draft, string reference)
        => new Dictionary<string, object>
        {
            ["items"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["quantity"] = 1,
                    ["price"] = new Dictionary<string, object>
                    {
                        ["name"]        = Label(draft),
                        ["description"] = Label(draft),
                        ["tax_mode"]    = "account_setting",
                        ["unit_price"]  = new Dictionary<string, object>
                        {
                            ["amount"]        = draft.MinorAmount,
                            ["currency_code"] = draft.NormalizedCurrency,
                        },
                        ["quantity"] = new Dictionary<string, object>
                        {
                            ["minimum"] = 1,
                            ["maximum"] = 1,
                        },
                        ["product"] = new Dictionary<string, object>
                        {
                            ["name"]         = Label(draft),
                            ["tax_category"] = TaxCategory,
                        },
                    },
                }
            },
            ["collection_mode"] = CollectionMode,
            ["currency_code"]   = draft.NormalizedCurrency,
            ["custom_data"]     = new Dictionary<string, object> { [ReferenceKey] = reference },
        };

    /// <summary>ما يَراهُ الدافِعُ على صَفحَةِ Paddle — <b>وَصفُ
    /// المُشرِفِ إن كَتَبَه، وإلّا سَطرٌ يُشتَقُّ مِن السلاجِ
    /// والمُدَّة</b>. ولا يُترَكُ فارِغاً: <c>name</c> مَطلوبٌ
    /// لِلسِعرِ المُرتَجَل، وسَطرٌ فارِغٌ يُنتِج صَفحَةَ دَفعٍ بِلا
    /// اسمٍ لِما يُشتَرى.</summary>
    public static string Label(PaddleTransactionDraft draft)
        => draft.TrimmedDescription is { Length: > 0 } d
            ? d
            : $"{draft.NormalizedSlug} · {draft.Days}d";
}
