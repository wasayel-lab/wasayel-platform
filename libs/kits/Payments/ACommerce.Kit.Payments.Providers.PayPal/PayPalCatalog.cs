using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Subscriptions;

namespace ACommerce.Kit.Payments.Providers.PayPal;

// ═══ كاتالوجُ PayPal — مُنتَجٌ ثُمَّ خُطَّة، مِن شاشَةِ المُشرِف ═══════
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ**: خُطُواتُ `docs/DEPLOY.md`
// ‏§٢·ج تَفتَرِض صَفحَةَ المُنتَجات/الخُطَط في لَوحَةِ PayPal، **وقَد
// تَعَذَّرَ على المالِكِ فَتحُها**. والواجِهَةُ REST تُنشِئُهُما بِلا
// لَوحَةٍ إطلاقاً — فَاللَوحَةُ تَصير طَريقاً أَوَّلَ لا شَرطاً.
//
// **وكُلُّ ما هُنا دَوالُّ نَقِيَّة**: لا HTTP ولا وَقتَ ولا عَشوائيَّة.
// نَفسُ نَمَطِ `PayPalBillingPolicy` و`AuthChannelSelection` — الحَدُّ
// الَّذي لا يُقاس آلِيّاً يَنهار (القاعِدَة ٢).

/// <summary>
/// <para><b>عُملاتُ المُعامَلَة في PayPal — مَعجَمٌ مُغلَقٌ مَقيسٌ لا
/// مَظنون.</b> خَمسٌ وعِشرونَ عُملَة، و<b>الريالُ السُعوديُّ لَيسَ
/// مِنها إطلاقاً</b>: أَربَعَةُ مَصادِرَ رَسمِيَّةٍ مُتَطابِقَة —
/// مَرجِعُ رُموزِ العُملاتِ في REST، وصَفحَةُ Supported Currencies،
/// وجَدوَلُ «الرَسمِ الثابِتِ لِلمُعامَلاتِ التِجارِيَّةِ حَسَبَ
/// العُملَةِ المُستَلَمَة» على صَفحَةِ الرُسومِ السُعودِيَّةِ نَفسِها،
/// وصَفُّ السُعودِيَّةِ في جَدوَلِ الدُوَلِ الَّذي يَقول
/// «‏Send, receive, and withdraw» بِلا لاحِقَةِ «‏in local currency»
/// المَوجودَةِ لِدُوَلٍ أُخرى.</para>
///
/// <para><b>والريالُ يَظهَر في مَوضِعٍ واحِدٍ فَقَط</b>: رَسماً ثابِتاً
/// لِلسَحبِ بِالبِطاقَة — أَي عُملَةَ وِجهَةِ سَحبٍ لا عُملَةَ تَسعيرٍ
/// ولا رَصيد. فَما يُعرَض بِالريالِ في الواجِهَةِ **عَرضٌ لا تَسعير**،
/// ومَصدَرُ سِعرِ الصَرفِ قَرارٌ مُنفَصِلٌ لَم يُتَّخَذ بَعد.</para>
///
/// <para><b>والافتِراضُ USD ويُقالُ لِماذا</b>: هُوَ المَنصوصُ عَلَيه
/// في الـJS SDK («‏Defaults to USD»)، ولا يَحمِل أَيَّ احتِمالِ رَفضٍ
/// على مُستَوى دَولَةِ البائِع. و<b>عُملَةٌ واحِدَةٌ لِكُلّ خُطَّة</b>
/// نَصّاً: «‏Only one currency_code is allowed per subscription plan»
/// — فَتَعَدُّدُ العُملاتِ يَعني تَعَدُّدَ خُطَط.</para>
/// </summary>
public static class PayPalCurrencies
{
    public const string Default = "USD";

    /// <summary>الخَمسُ والعِشرون كَما في مَرجِعِ REST. <b>ولا SAR
    /// فيها</b> — والغِيابُ هُنا هُوَ الحَقيقَة، لا نُقصانٌ يُكمَّل.</summary>
    public static readonly IReadOnlyList<string> Supported = new[]
    {
        "AUD", "BRL", "CAD", "CNY", "CZK", "DKK", "EUR", "HKD", "HUF",
        "ILS", "JPY", "MYR", "MXN", "TWD", "NZD", "NOK", "PHP", "PLN",
        "GBP", "RUB", "SGD", "SEK", "CHF", "THB", "USD"
    };

    /// <summary>
    /// <para><b>عُملاتٌ لا تَقبَل كُسوراً عَشرِيَّة</b> — و«‏9.99» فيها
    /// يَرتَدُّ مِن PayPal. تُصاغُ قيمَتُها صَحيحَةً بِلا فاصِلَة.</para>
    ///
    /// <para><b>ولِماذا يُقاسُ هذا وهُوَ لا يَقَع مَعَ USD</b>: لِأَنّ
    /// قائِمَةَ العُملاتِ مَعروضَةٌ في الشاشَة، فَالمالِكُ يَستَطيع
    /// اختِيارَ الينِّ اليَوم — و«لا يَقَع» عَن حَقلٍ مَعروضٍ دَعوى لا
    /// قِياس.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> WithoutDecimals = new[] { "JPY", "HUF", "TWD" };

    public static bool Contains(string? code)
        => code is not null && Supported.Contains(code.Trim().ToUpperInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// <para><b>أَنماطُ قِراءَةِ مَبلَغٍ نَصّاً — <c>NumberStyles.Number</c>
    /// مَمنوعَةٌ هُنا، ويُقالُ لِماذا.</b> تِلكَ تَحمِل
    /// <c>AllowThousands</c>، فَـ<c>"49,99"</c> — وهي كِتابَةُ نِصفِ
    /// أوروبّا لِـ‏49.99 — تُقرَأُ <b>‏4999</b>: مِئَةُ ضِعفٍ بِفاصِلَةٍ
    /// واحِدَة. والحَقلُ في الشاشَةِ <c>type="number"</c> لكِنّ النُقطَةَ
    /// تَقبَلُ أَيَّ نَصٍّ يَصِلُها (‏<c>curl</c>، لُصوقٌ مِن جَدوَل،
    /// مُتَصَفِّحٌ لا يُطَبِّق النَمَط).</para>
    ///
    /// <para><b>وتَحمِلُ أَيضاً <c>AllowLeadingSign</c> وقَد سَقَطَت
    /// مَعَها</b>: مَبلَغٌ سالِبٌ لَيسَ مَبلَغاً، ونَمَطُ PayPal نَفسُه
    /// يَقبَلُ الإشارَةَ فَتُرَدُّ ‏422 بَعدَ نَشر. وسُقوطُ القِراءَةِ
    /// إلى صِفرٍ يَرتَدُّ بِخَرقٍ يُسَمّيه المُصادِق.</para>
    ///
    /// <para><b>ومَوضِعٌ واحِدٌ يَقرَؤُه ثَلاثَة</b>: قِراءَةُ نَموذَجِ
    /// الخُطَّة، وقِراءَةُ نَموذَجِ الطَلَب، ومُقارَنَةُ المَبلَغِ
    /// الواصِلِ بِالمَحفوظ. وكانَ السَطرُ مَنسوخاً في الثَلاثَة —
    /// وثَلاثَةُ مَواضِعَ لِقاعِدَةٍ واحِدَةٍ تَنجَرِف
    /// (القاعِدَة ٢).</para>
    /// </summary>
    public const NumberStyles MoneyStyles =
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    /// <summary>صِياغَةُ المَبلَغ كَما تَشتَرِطُها PayPal: <b>سِلسِلَةٌ
    /// نَصِّيَّةٌ</b> بِنَمَط <c>^((-?[0-9]+)|(-?([0-9]+)?[.][0-9]+))$</c>
    /// — أَي <c>"9.99"</c> لا <c>9.99</c>، وبِفاصِلَةٍ إنجِليزِيَّةٍ
    /// دائِماً مَهما كانَت ثَقافَةُ الخادِم.</summary>
    public static string Money(decimal amount, string currency)
        => amount.ToString(
            WithoutDecimals.Contains(currency?.Trim().ToUpperInvariant() ?? "", StringComparer.Ordinal)
                ? "0" : "0.00",
            CultureInfo.InvariantCulture);
}

/// <summary>دَورِيَّةُ الفَوتَرَة — <b>اثنَتانِ لا أَكثَر</b> مِمّا
/// تَقبَلُه PayPal (‏<c>DAY|WEEK|MONTH|YEAR</c>)، لِأَنّ الباقَةَ
/// تُباعُ شَهرِيّاً أَو سَنَوِيّاً ولا شَيءَ سِواهُما يُطلَب. وقيمَةٌ
/// ثالِثَةٌ <b>لَيسَت افتِراضاً بَل رَفضاً</b>.</summary>
public static class PayPalPlanIntervals
{
    public const string Month = "MONTH";
    public const string Year  = "YEAR";

    public static readonly IReadOnlyList<string> All = new[] { Month, Year };

    public static bool Contains(string? unit)
        => unit is not null && All.Contains(unit.Trim().ToUpperInvariant(), StringComparer.Ordinal);
}

/// <summary>
/// <para><b>ما يَملَؤُه المُشرِفُ في النَموذَج</b> — أَربَعَةُ حُقولٍ لا
/// خامِس. <b>ولا قيمَةَ افتِراضِيَّةً لِاسمٍ ولا لِسِعر</b>: الرَقَمُ
/// والتَسمِيَةُ بَياناتُ مُنتَجٍ لا تُخترَع (القاعِدَة ١٦)، والعُملَةُ
/// وَحدَها تَحمِل افتِراضاً لِأَنَّه <b>مَقيسٌ لا مَظنون</b>.</para>
/// </summary>
/// <param name="PlanSlug">سلاجُ باقَةِ المَنَصَّةِ الَّتي تُربَط بِها
/// الخُطَّة — <b>مِن الكاتالوج لا مِن النَموذَج</b>، فَلا يَختار
/// المُتَصَفِّحُ ما يُربَط.</param>
/// <param name="Name">اسمُ المُنتَجِ والخُطَّةِ مَعاً — <b>واحِدٌ لا
/// اثنان</b>: اسمانِ مُنفَصِلانِ يَنجَرِفان، ولَيسَ في لَوحَةِ PayPal
/// شاشَةٌ تَقرَؤُهُما مَعاً.</param>
/// <param name="Amount">السِعرُ بِعُملَةِ الخُطَّة.</param>
/// <param name="Currency">مِن <see cref="PayPalCurrencies.Supported"/>
/// حَصراً.</param>
/// <param name="IntervalUnit">مِن <see cref="PayPalPlanIntervals.All"/>
/// حَصراً.</param>
public sealed record PayPalPlanDraft(
    string  PlanSlug,
    string  Name,
    decimal Amount,
    string  Currency,
    string  IntervalUnit)
{
    /// <summary>العُملَةُ مُطَبَّعَةً — PayPal تَقرَأُها كَبيرَةً.</summary>
    public string NormalizedCurrency => (Currency ?? "").Trim().ToUpperInvariant();

    public string NormalizedInterval => (IntervalUnit ?? "").Trim().ToUpperInvariant();

    public string TrimmedName => (Name ?? "").Trim();
}

/// <summary>خَرقٌ واحِدٌ في مُسَوَّدَةِ خُطَّة. نَفسُ شَكلِ
/// <c>PlanDefinitionViolation</c> حَرفاً — <c>Code</c> ثابِتٌ
/// لِلاختِبارِ ولِلقامُوس، و<c>MessageAr</c> لِلوغ.</summary>
public sealed record PayPalCatalogViolation(string Code, string MessageAr);

/// <summary>نَتيجَةُ الإنشاءِ — <b>المُعَرِّفانِ مَعاً</b>. والمُنتَجُ
/// يُخَزَّن ولا يُهمَل: خُطَّةٌ ثانِيَةٌ لِنَفسِ الباقَةِ غَداً
/// تُعَلَّقُ عَلَيه بَدَلَ أَن تُنشِئَ مُنتَجاً ثانِياً يَتيماً.</summary>
public sealed record PayPalCatalogPlan(string ProductId, string PlanId);

/// <summary>
/// <para><b>بَوّابَةُ إنشاءِ الخُطَّةِ ومَفاتيحُ مَرَّة-واحِدَة</b> —
/// دَوالُّ نَقِيَّة تُقاسُ بِجَدوَل.</para>
/// </summary>
public static class PayPalCatalogPolicy
{
    // ─── رُموزُ الخَرق — مَعجَمٌ مُغلَقٌ يَقرَؤُه المُصادِقُ والقامُوس ──
    public const string NameEmpty           = "paypal_plan_name_empty";
    public const string NameTooLong         = "paypal_plan_name_too_long";
    public const string AmountNotPositive   = "paypal_plan_amount_not_positive";
    public const string CurrencyUnsupported = "paypal_plan_currency_unsupported";
    public const string IntervalUnknown     = "paypal_plan_interval_unknown";
    public const string PlanSlugEmpty       = "paypal_plan_slug_empty";

    /// <summary>سَقفُ اسمِ الخُطَّةِ عِندَ PayPal: ‏1..127. <b>ووَصفُ
    /// المُنتَجِ 256 ووَصفُ الخُطَّةِ 127</b> — فَرقٌ يُنسى فَيَرتَدُّ
    /// النِداء، ولِذلك لا يُرسَل وَصفٌ إطلاقاً مِن هُنا: نَصٌّ يُخترَع
    /// لِمِلءِ حَقلٍ اختِيارِيّ بَياناتُ مُنتَجٍ لا تُخترَع
    /// (القاعِدَة ١٦).</summary>
    public const int MaxNameLength = 127;

    // ─── قيَمُ الجِسمِ الثابِتَة — مَعاجِمُ PayPal المُغلَقَة ──────────
    //
    // تُكتَب هُنا لا في جِسمِ النِداء، فَيَقرَؤُها الاختِبارُ مِن
    // مَوضِعِها بَدَلَ أَن يَنسَخَها — وسِلسِلَةٌ مَنسوخَةٌ بِخَطَإ
    // حَرفٍ تُعطي ‏422 غامِضَةً بَعدَ نَشر.

    /// <summary>‏<c>PHYSICAL|DIGITAL|SERVICE</c> — ثَلاثٌ لا رابِعَ لَها.
    /// واشتِراكُ مَنَصَّةٍ <b>خِدمَة</b>.</summary>
    public const string ProductType = "SERVICE";

    /// <summary>مِن مَعجَمٍ مُغلَقٍ بِـ‏446 قيمَة. و<c>SOFTWARE</c>
    /// أَقرَبُها لِمَنَصَّةِ مَتاجِرَ تُباعُ بِاشتِراك.</summary>
    public const string ProductCategory = "SOFTWARE";

    /// <summary>‏<c>CREATED|ACTIVE</c> مُدخَلاتٍ، والافتِراضُ
    /// <c>ACTIVE</c>. ويُكتَب صَراحَةً كَي لا يَلزَمَ نِداءُ تَفعيلٍ
    /// ثالِث.</summary>
    public const string PlanStatusActive = "ACTIVE";

    public const string TenureRegular = "REGULAR";

    /// <summary><b>صِفرٌ = لا نِهائِيَّة</b> — وهُوَ مَوضِعُ ضَبطِ
    /// التَجديدِ الدائِم. و<c>auto_renewal</c> على الاشتِراكِ
    /// <b>مُهمَلَة</b> ولا تَفعَل هذا.</summary>
    public const int InfiniteCycles = 0;

    /// <summary>الافتِراضُ <c>CANCEL</c> — يُلغي الاشتِراكَ عِندَ
    /// تَعَثُّرِ رَسمِ التَهيئَة. يُكتَب <c>CONTINUE</c> صَراحَةً.</summary>
    public const string SetupFeeFailureAction = "CONTINUE";

    /// <summary>الافتِراضُ <b>صِفر</b> — أَي إلغاءٌ عِندَ **أَوَّلِ**
    /// تَعَثُّرِ بِطاقَة. ثَلاثٌ كَما في مِثالِ PayPal الرَسميّ.</summary>
    public const int PaymentFailureThreshold = 3;

    /// <summary>
    /// <para><b>حُقولُ النَموذَجِ مَقروءَةً — دالَّةٌ نَقِيَّةٌ لا
    /// تَعرِف HTTP.</b> تَأخُذ سَلاسِلَ وتُعطي مُسَوَّدَةً بِأَنواعِها،
    /// كَـ<c>TenantPlanPolicy.ReadSetting</c> حَرفاً — فَلا تَحليلَ
    /// أَرقامٍ في جِسمِ نُقطَة.</para>
    ///
    /// <para><b>والسُقوطُ عِندَ كُلّ حَقلٍ مَقصود</b>: سِعرٌ غَيرُ
    /// مَقروءٍ = صِفر <b>فَيَرتَدُّ بِخَرقٍ يُسَمّيه</b> ولا يُخمَّن،
    /// وعُملَةٌ غائِبَةٌ = <see cref="PayPalCurrencies.Default"/> وهُوَ
    /// <b>الافتِراضُ الوَحيدُ المَأذون</b> لِأَنَّه مَقيس، ودَورِيَّةٌ
    /// غائِبَةٌ = خَرق لا شَهرٌ مُفتَرَض.</para>
    ///
    /// <para><b>و<c>planSlug</c> لا يُقرَأُ مِن النَموذَج</b>: يَأتي مِن
    /// وَثيقَةِ باقَةِ المَتجَرِ في الخادِم. ولَو قُرِئَ مِن الطَلَبِ
    /// لَرَبَطَ مُتَصَفِّحٌ خُطَّةً بِباقَةٍ لَم يَخترها المُشرِف.</para>
    /// </summary>
    public static PayPalPlanDraft ReadDraft(
        string? planSlug, string? name, string? amount, string? currency, string? interval)
        => new(
            (planSlug ?? "").Trim(),
            (name ?? "").Trim(),
            decimal.TryParse(amount, PayPalCurrencies.MoneyStyles, CultureInfo.InvariantCulture, out var v) ? v : 0m,
            string.IsNullOrWhiteSpace(currency) ? PayPalCurrencies.Default : currency.Trim().ToUpperInvariant(),
            (interval ?? "").Trim().ToUpperInvariant());

    /// <summary>القائِمَةُ فارِغَةٌ تَعني مُسَوَّدَةً صالِحَة.</summary>
    public static IReadOnlyList<PayPalCatalogViolation> Validate(PayPalPlanDraft? d)
    {
        var v = new List<PayPalCatalogViolation>();
        if (d is null)
        {
            v.Add(new(PlanSlugEmpty, "لا مُسَوَّدَةَ خُطَّةٍ أَصلاً."));
            return v;
        }

        if (string.IsNullOrWhiteSpace(d.PlanSlug))
            v.Add(new(PlanSlugEmpty, "لا باقَةَ مَضبوطَةٌ لِهذا المَتجَر، فَلا شَيءَ تُربَط بِه الخُطَّة."));

        if (string.IsNullOrWhiteSpace(d.Name))
            v.Add(new(NameEmpty, "اسمُ الخُطَّةِ فارِغ — وهُوَ ما يَراهُ الدافِعُ في صَفحَةِ PayPal."));
        else if (d.TrimmedName.Length > MaxNameLength)
            v.Add(new(NameTooLong,
                $"اسمُ الخُطَّةِ {d.TrimmedName.Length} مِحرَفاً، والسَقفُ عِندَ PayPal {MaxNameLength}."));

        // الصِفرُ لَيسَ «مَجّانِيَّة» بَل حَقلٌ لَم يُملَأ: خُطَّةُ
        // اشتِراكٍ بِصِفرٍ تُنشَأ ولا تَقبِض شَيئاً أَبَداً.
        if (d.Amount <= 0m)
            v.Add(new(AmountNotPositive,
                $"سِعرُ الخُطَّة {d.Amount} — خُطَّةٌ لا تَقبِض شَيئاً."));

        if (!PayPalCurrencies.Contains(d.Currency))
            v.Add(new(CurrencyUnsupported,
                $"العُملَة «{d.Currency}» خارِجَ عُملاتِ المُعامَلَةِ في PayPal " +
                $"(‏{PayPalCurrencies.Supported.Count} عُملَة، ولا SAR فيها)."));

        if (!PayPalPlanIntervals.Contains(d.IntervalUnit))
            v.Add(new(IntervalUnknown,
                $"الدَورِيَّة «{d.IntervalUnit}» خارِجَ " +
                $"{string.Join("/", PayPalPlanIntervals.All)}."));

        return v;
    }

    public static bool IsValid(PayPalPlanDraft? d) => Validate(d).Count == 0;

    /// <summary>
    /// <para><b>وَثيقَةُ الرِباطِ مِن المُسَوَّدَةِ ومِمّا رَدَّتهُ
    /// PayPal — دالَّةٌ نَقِيَّة.</b> مُعَرِّفُها <b>سلاجُ الباقَة</b>،
    /// فَنِداءٌ ثانٍ لِنَفسِ الباقَةِ يَكتُب <b>فَوقَ الوَثيقَةِ
    /// نَفسِها</b> ولا يُنشِئ ثانِيَة — «مَرَّةٌ واحِدَة» بِمِفتاحِ
    /// الوَثيقَةِ لا بِفَحصِ وُجودٍ في التَطبيق (نَفسُ حُجَّةِ
    /// <c>RecordFor</c> حَرفاً).</para>
    ///
    /// <para><b>والوَقتُ يُمَرَّرُ ولا يُقرَأُ مِن الساعَة</b> — وإلّا
    /// لَما كانَت الدالَّةُ نَقِيَّةً ولا قابِلَةً لِلقِياس.</para>
    /// </summary>
    public static PlatformPlanPayPal BindingFor(
        PayPalPlanDraft draft, PayPalCatalogPlan created, string by, DateTime at)
        => new()
        {
            Id           = (draft.PlanSlug ?? "").Trim(),
            ProductId    = created.ProductId,
            PlanId       = created.PlanId,
            Name         = draft.TrimmedName,
            Amount       = draft.Amount,
            Currency     = draft.NormalizedCurrency,
            IntervalUnit = draft.NormalizedInterval,
            CreatedBy    = by,
            CreatedAt    = at,
        };

    // ═══ مَفاتيحُ مَرَّة-واحِدَة ═══════════════════════════════════════
    //
    // **‏PayPal-Request-Id يَحفَظُه الخادِمُ ‏72 ساعَة**، وإعادَةُ
    // المُحاوَلَةِ بِلا مِفتاحٍ **تُنشِئ خُطَّةً ثانِيَة**. ولِذلك
    // يُشتَقُّ المِفتاحُ **حَتمِيّاً مِن مُدخَلاتِ الطَلَب** — لا مِن
    // زَمَنٍ ولا عَشوائيَّة:
    //
    //   · نَقرَتانِ على نَفسِ النَموذَج ⇒ نَفسُ المِفتاح ⇒ **خُطَّةٌ
    //     واحِدَة** عِندَ PayPal.
    //   · تَغييرُ السِعرِ أَو الاسمِ أَو الدَورِيَّة ⇒ مِفتاحٌ آخَر ⇒
    //     خُطَّةٌ جَديدَةٌ حينَ تُرادُ فِعلاً.
    //
    // **ولِماذا SHA-256 لا `string.GetHashCode`**: الأَخيرَةُ
    // **مُبَذَّرَةٌ لِكُلّ عَمَلِيَّة** — نَفسُ السِلسِلَةِ تُعطي رَقَماً
    // مُختَلِفاً بَعدَ كُلّ إقلاع. فَمِفتاحٌ مَبنيٌّ عَلَيها يَبدو
    // حَتمِيّاً داخِلَ العَمَلِيَّة ويَتَبَدَّل بَينَها — وهُوَ أَسوَأُ
    // مِن التَقَلُّبِ الصَريحِ لِأَنَّه يَنجو مِن كُلّ اختِبارٍ يَجري
    // في عَمَلِيَّةٍ واحِدَة (‏`StableHashTests`).

    private const string ProductKeyPrefix = "wasayel-product-";
    private const string PlanKeyPrefix    = "wasayel-plan-";

    /// <summary>مِفتاحُ إنشاءِ المُنتَج — مِن السلاجِ والاسمِ وَحدَهُما،
    /// وهُما ما يُرسَل في جِسمِ المُنتَج. <b>ولا يَدخُلُه السِعر</b>:
    /// تَغييرُ السِعرِ يُنشِئ خُطَّةً جَديدَةً على <b>نَفسِ
    /// المُنتَج</b>، لا مُنتَجاً ثانِياً بِنَفسِ الاسم.</summary>
    public static string ProductRequestId(PayPalPlanDraft d)
        => ProductKeyPrefix + Fingerprint(d.PlanSlug ?? "", d.TrimmedName);

    /// <summary>مِفتاحُ إنشاءِ الخُطَّة — يَدخُلُه <b>كُلُّ</b> ما
    /// يُرسَل: المُنتَجُ والاسمُ والمَبلَغُ والعُملَةُ والدَورِيَّة.
    /// فَحَقلٌ يَتَغَيَّر ⇒ خُطَّةٌ أُخرى، وحُقولٌ لا تَتَغَيَّر ⇒ لا
    /// خُطَّةَ ثانِيَة.</summary>
    public static string PlanRequestId(string productId, PayPalPlanDraft d)
        => PlanKeyPrefix + Fingerprint(
            productId ?? "", d.PlanSlug ?? "", d.TrimmedName,
            PayPalCurrencies.Money(d.Amount, d.NormalizedCurrency),
            d.NormalizedCurrency, d.NormalizedInterval);

    /// <summary>فاصِلُ الحُقولِ في البَصمَة — مِحرَفٌ <b>لا يَقَع في
    /// أَيّ حَقل</b>، فَلا تُعطي «‏a» و«‏bc» نَفسَ بَصمَةِ «‏ab» و«‏c».
    /// <b>ومَكتوبٌ بِهُروبِه لا بِبايتِه</b>: مِحرَفُ
    /// تَحَكُّمٍ خامٌّ في مِلَفّ مَصدَرٍ يُبتلَع في أَوَّلِ أَداةٍ
    /// تُطَبِّعُ النَصّ، فَتَتَبَدَّلُ البَصمَةُ بِلا سَبَبٍ مَرئيّ.</summary>
    private const char FieldSeparator = '\u001F';

    /// <summary><para>بَصمَةٌ ثابِتَةٌ عَبرَ العَمَلِيّات — ‏24 مِحرَفاً
    /// سِتَّ عَشرِيّاً مِن ‏SHA-256.</para>
    ///
    /// <para><b>وعامَّةٌ لِأَنّ مَسارَ الطَلَبات يَشتَقُّ مَفاتيحَه
    /// بِها (‏<see cref="PayPalOrderPolicy"/>)</b> — <b>ودالَّةُ بَصمَةٍ
    /// مَنسوخَةٌ أَخطَرُ مِن سِلسِلَةٍ مَنسوخَة</b>: نُسخَتانِ
    /// تَختَلِفانِ في الفاصِلِ أَو في عَدَدِ البايتاتِ تُعطِيانِ
    /// مِفتاحَينِ يَبدُوانِ صَحيحَينِ ولا يَرتَطِمان. وهذا لَيسَ
    /// تَجريداً يَسبِق مُستَهلِكَه (القاعِدَة ١): الدالَّةُ قائِمَةٌ
    /// ومُستَعمَلَةٌ، والمَكشوفُ رُؤيَتُها لا صِنفٌ جَديد.</para></summary>
    public static string Fingerprint(params string[] parts)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(FieldSeparator, parts))), 0, 12)
            .ToLowerInvariant();
}

// ═══ خَطَأُ الاستِحقاقِ يُسَمّى ولا يُبتلَع ═══════════════════════════
//
// **العِلَّةُ المَقيسَة**: الاشتِراكاتُ عِندَ PayPal تَقوم على
// **Reference Transactions / Billing Agreements**، وPayPal تُصَنِّفُها
// *limited-release* «لِتُجّارٍ مُختارينَ وحالاتِ استِخدامٍ مُعتَمَدَة»،
// وتُوَثِّق خَطَأً بِاسمِه: `Merchant not enabled for reference
// transaction`. **والسيناريو المُتَوَقَّع أَنّ الخُطَّةَ تَنجَح ثُمَّ
// يَفشَل تَفعيلُ أَوَّلِ اشتِراك** — أَي أَنّ الخَطَأَ يَقَع على
// المَسارِ الَّذي كانَ يَرُدُّ «تَعَذَّرَ إنشاءُ رابِطِ الدَفع».
//
// **ولِماذا رَمزٌ مُغلَقٌ لا نَصُّ PayPal كَما هُوَ**: نَصُّها
// إنجِليزيٌّ ويَقول **ما وَقَعَ** ولا يَقول **ما يُفعَل**. والمالِكُ
// أَمامَ «‏Merchant not enabled for reference transaction» يَبحَث في
// اللَوحَةِ عَن إعدادٍ لا وُجودَ لَه — **والعِلاجُ الوَحيدُ مُراسَلَةُ
// دَعمِ PayPal بِطَلَبٍ مُحَدَّد**. فَهذا الخَطَأُ وَحدَه يُترجَم إلى
// رَمزٍ يَحمِلُه القامُوسُ بِنَصٍّ عَرَبيٍّ يَقول الطَلَبَ حَرفاً، وما
// عَداهُ يُعرَض كَما هُوَ بِرَمزِ PayPal ونَصِّه.

/// <summary>
/// <para><b>تَصنيفُ فَشَلِ PayPal — دالَّةٌ نَقِيَّةٌ واحِدَةٌ
/// يَقرَؤُها المَساران.</b> إنشاءُ الخُطَّةِ وإنشاءُ رابِطِ الدَفعِ
/// كِلاهُما قَد يَرُدّ خَطَأَ الاستِحقاقِ نَفسَه، و<b>تَصنيفُهُ في
/// مَوضِعَينِ يَنجَرِف</b>: مَسارٌ يُسَمّيه ومَسارٌ يَبتَلِعُه
/// (القاعِدَة ٢).</para>
/// </summary>
public static class PayPalFailure
{
    /// <summary>رَمزُ الشاشَةِ لِخَطَإ الاستِحقاق — مِن مَعجَمٍ مُغلَقٍ
    /// يَقرَؤُه القامُوسُ والاختِبار، كَـ<c>PayPalSurface.LinkRefused</c>
    /// حَرفاً.</summary>
    public const string ReferenceTransactionsDisabled = "paypal_reference_transactions_disabled";

    /// <summary>
    /// <para><b>العَلامَةُ المَقيسَة</b> — والمَقروءُ مِن تَوثيقِ PayPal
    /// نَصُّ الرِسالَةِ حَرفاً: «‏Merchant not enabled for reference
    /// transaction». <b>ورَمزُ العَطَبِ (<c>issue</c>) لَم يُقرَأ مِن
    /// مَصدَرٍ رَسميّ</b> ولا يُخترَع (القاعِدَة ١٦).</para>
    ///
    /// <para><b>ولِذلك تُطابَقُ العِبارَةُ لا الرَمز</b>، بَعدَ تَطبيعٍ
    /// يَجعَل الشُرطَةَ السُفلِيَّةَ مَسافَةً — فَتُلتَقَط الرِسالَةُ
    /// المُوَثَّقَةُ <b>وأَيُّ رَمزٍ مِن عائِلَتِها</b>
    /// (<c>REFERENCE_TRANSACTIONS_NOT_ENABLED</c> يُطَبَّع إلى
    /// «‏REFERENCE TRANSACTIONS NOT ENABLED» فَيَحوي العِبارَة) بِقاعِدَةٍ
    /// واحِدَةٍ لا بِقائِمَةٍ مَظنونَة.</para>
    /// </summary>
    private const string Marker = "REFERENCE TRANSACTION";

    /// <summary>أَهُوَ خَطَأُ استِحقاقِ المُعامَلاتِ المَرجِعِيَّة؟
    /// و<c>null</c> أَو فارِغٌ ⇒ <c>false</c>: «لا رِسالَة» لَيسَ
    /// «هذِه الرِسالَة».</summary>
    public static bool IsReferenceTransactionsDisabled(string? payPalText)
        => !string.IsNullOrWhiteSpace(payPalText)
           && payPalText.Replace('_', ' ').ToUpperInvariant().Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// <para><b>ما يُوضَع في <c>?err=</c>.</b> خَطَأُ الاستِحقاقِ يُعطي
    /// رَمزَه المُغلَق، وما عَداهُ <b>يُعطي نَفسَه حَرفاً</b> — رَمزُ
    /// PayPal ونَصُّه كَما هُما، لِأَنّ «فَشِلَ الإنشاء» وَحدَها تُرسِل
    /// المُشرِفَ يُخَمِّن.</para>
    /// </summary>
    public static string ScreenCode(string? payPalText)
        => IsReferenceTransactionsDisabled(payPalText)
            ? ReferenceTransactionsDisabled
            : (payPalText ?? "").Trim();
}
