using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Api;

/// <summary>جِسمُ الخَطَأ المُوَحَّد — <c>{ "error": { "code",
/// "message_ar", "details" } }</c> (‏§٤٫٤). الأَسماءُ ست
/// عَشرِيَّةُ الشَكل كَما تُكتَب في العَقد، فَلا تُحَوِّلُها
/// سِياسَةُ تَسمِيَة.</summary>
public sealed record ApiErrorBody(ApiErrorPayload error);

public sealed record ApiErrorPayload(string code, string message_ar, object? details);

/// <summary>رَمزُ خَطَأٍ واحِد: رَمزُه، وحالَتُه، ورِسالَتُه.</summary>
public sealed record ApiErrorCode(string Code, int Status, string MessageAr);

/// <summary>
/// <para><b>مَعجَمُ رُموزِ الخَطَأ المُغلَق</b> (‏§٤٫٤، القاعِدَة ٤).
/// كُلُّ رَمزٍ يَحمِل <b>حالَتَه</b> مَعَه — فَلا يُكتَب رَقمُ
/// الحالَةِ في مَوضِعِ الاستِعمال، ولا يَختَلِف رَمزانِ في مَوضِعَين
/// عَلى نَفس الحالَة. وهذا بِعَينِه ما مَنَعَه غِيابُ المَعجَم في
/// الواجِهَة القائِمَة: نَفسُ العَمَلِيَّة تُجيب ‏403 مِن
/// <c>/admin</c> و‏302 مِن <c>/studio</c> (‏§٣٫٢).</para>
///
/// <para><b>ولا رَمزَ بِلا مُنتِج</b> (القاعِدَة ١): كُلُّ رَمزٍ
/// أَدناه يُنتِجُه سَطرٌ حَيٌّ في <c>ApiV1Endpoints</c> أَو في
/// أَحَدِ مُرَشِّحاته، ولِكُلٍّ اختِبارٌ مُوجِبٌ وسالِب في حَقيبَة
/// المُطابَقَة.</para>
///
/// <para><b>ورُموزُ الرَفضِ لا تُفشي حالَةَ المِفتاح</b>: مِفتاحٌ
/// مَجهولٌ ومُزَوَّرٌ ومُبطَلٌ ومُنتَهٍ كُلُّها
/// <see cref="AuthInvalid"/> واحِد. التَفريقُ يُفيد المُهاجِمَ
/// وَحدَه — والسَبَبُ الحَقيقيّ يُقرَأ مِن
/// <c>ApiKeyRejection</c> في اللوغ لا مِن الجِسم.</para>
/// </summary>
public static class ApiErrorCatalog
{
    public const string AuthMissing            = "auth_missing";
    public const string AuthInvalid            = "auth_invalid";
    public const string ScopeMissing           = "scope_missing";
    public const string EntitlementDenied      = "entitlement_denied";
    public const string ActorNotAllowed        = "actor_not_allowed";
    public const string NotFound               = "not_found";
    public const string DealNotActive          = "deal_not_active";
    public const string DealFinalStage         = "deal_final_stage";
    /// <summary>كَتَبَ غَيرُ هذا الطَلَبِ الصَفقَةَ بَعدَ أَن قَرَأَها (‏ADR-036، ‏2026-10-12) — مُنتِجُه
    /// <c>DealApi.FromAdvance</c>/<c>FromCancel</c> مِن رَمزِ الخِدمَةِ نَفسِه. وجَوابُه نِهائِيٌّ لِمِفتاحِ
    /// مَرَّةٍ-واحِدَةٍ كَكُلِّ جَواب: الإعادَةُ طَلَبٌ جَديدٌ بِمِفتاحٍ جَديد.</summary>
    public const string DealChangedMeanwhile   = "deal_changed_meanwhile";
    /// <summary>طَلَبُ الصَفقَةِ عِندَ الناقِلِ (OTO) وتاليها «شَحن» — تَدخُلُها حينَ يُثبِتُ الناقِلُ شُحنَتَها عَبرَ نُقطَةِ الشَحن، لا بِهذا
    /// الباب (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ح). مُنتِجُه <c>DealApi.FromAdvance</c> مِن رَمزِ الخِدمَةِ نَفسِه
    /// (<c>DealShipping.DealAwaitsCarrier</c>)؛ ‏409 تَعارُضُ حالَةٍ كَأَخَواتِه — وتَصنيفُ الحالَةِ كانَ سَيَقولُ «فاعِلٌ غَيرُ مُخَوَّل» (‏403)
    /// عَن الفاعِلِ المُخَوَّلِ نَفسِه.
    /// <para><b>ورِسالَتُه تُسَمّي البابَينِ والشَرط</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 §٧‑ك): كانَت «تَدخُلُ «شَحن» حينَ
    /// يُثبِتُ الناقِلُ شُحنَتَها» — وَعدٌ بِنَقلٍ لا يُؤَدّيه كودٌ يَومَ كُتِبَت، ولا تُسَمّي البوليصَة. وما يُدخِلُها اليَومَ جَوابٌ مِن OTO يُثبِتُ
    /// الشُحنَةَ والبوليصَةَ عِندَ «أَكمِل الشَّحن» (<c>ShipAsync</c>) أَو عِندَ إشعارٍ مِنها (<c>DeliverAsync</c>، §٧‑ط) — والرِسالَةُ تَقولُهُما.</para></summary>
    public const string DealAwaitsCarrier      = "deal_awaits_carrier";
    /// <summary>عَلى الصَفقَةِ فاتورَةُ بَوّابَةِ المَتجَرِ ولَم يَثبُت دَفعُها، وتاليها «دَفع» (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12) —
    /// مُنتِجُه <c>DealApi.FromAdvance</c> مِن رَمزِ الخِدمَةِ نَفسِه (<c>GatewayPayments.DealAwaitsPayment</c>)؛ ‏409 تَعارُضُ حالَةٍ نَسَقَ
    /// <see cref="DealAwaitsCarrier"/> حَرفاً — وتَصنيفُ الحالَةِ كانَ سَيَقولُ «فاعِلٌ غَيرُ مُخَوَّل» (‏403) عَن المُشتَري نَفسِه، و<c>required_actor</c>
    /// فيه هُوَ (مَقيسٌ قَبلَ سَطرِه: ‏403). ورِسالَتُه تُسَمّي الزِرَّ الَّذي يُثبِتُ الدَفعَ بِحَرفِه في القامُوس.</summary>
    public const string DealAwaitsPayment      = "deal_awaits_payment";
    /// <summary>صَفقَةُ بِطاقَةٍ بِلا مَرجِعِ دَفعٍ وتاليها «دَفع» (‏2026-10-13، G5 (xv-g)) — مُنتِجُه <c>DealApi.FromAdvance</c> مِن رَمزِ الخِدمَةِ نَفسِه
    /// (<c>DealsService.PaymentMissing</c>)؛ ‏409 نَسَقَ <see cref="DealAwaitsPayment"/> — وتَصنيفُ الحالَةِ كانَ سَيَقولُ «فاعِلٌ غَيرُ مُخَوَّل» عَن المُشتَري نَفسِه.</summary>
    public const string DealPaymentMissing     = "deal_payment_missing";
    /// <summary>عَلى فاتورَةِ بَوّابَةِ المَتجَرِ المَفتوحَةِ لِهذِه الصَفقَةِ صَفَقاتٌ قائِمَةٌ أُخرى، فَلا تُلغى وَحدَها (‏2026-10-13، مُراجَعَةُ بَوّابَةِ
    /// المَوجَة 3b، G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً) — مُنتِجُه <c>DealApi.FromCancel</c> مِن رَمزِ الخِدمَةِ نَفسِه (<c>GatewayInvoiceShare.Shared</c>)؛
    /// ‏409 تَعارُضُ حالَةٍ نَسَقَ أَخَواتِه. وكانَ الرَمزُ المَجهولُ يَخرُجُ <see cref="ValidationFailed"/> (‏422) عَن طَلَبٍ صالِح.</summary>
    public const string DealInvoiceShared      = "deal_invoice_shared";
    public const string IdempotencyInProgress  = "idempotency_in_progress";
    public const string IdempotencyKeyRequired = "idempotency_key_required";
    public const string ValidationFailed       = "validation_failed";

    /// <summary>الأَحَدَ عَشَرَ رَمزاً — مُرَتَّبَةً بِحالَتِها ثُمَّ
    /// بِرَمزِها. <b>صارَت اثنَي عَشَر ‏2026-10-12</b> (‏ADR-036): <see cref="DealChangedMeanwhile"/>
    /// بَعدَ أُختَيهِ في ‏409. <b>ثُمَّ ثَلاثَةَ عَشَر</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S7 الثانِيَة؛ ‏ADR-033 §٧‑ح):
    /// <see cref="DealAwaitsCarrier"/> بَعدَ أَخَواتِه الثَلاثِ في ‏409. <b>ثُمَّ أَربَعَةَ عَشَر</b> (‏2026-10-12، ‏ADR-032 الخُطوَة 10، تَيّارُ S9):
    /// <see cref="DealAwaitsPayment"/> بَعدَ أَخَواتِه الأَربَعِ في ‏409. <b>ثُمَّ خَمسَةَ عَشَر</b> (‏2026-10-13، G5 M4): <see cref="DealInvoiceShared"/>
    /// بَعدَ أَخَواتِه الخَمسِ في ‏409. <b>ثُمَّ سِتَّةَ عَشَر</b> (‏2026-10-13، G5 (xv-g)): <see cref="DealPaymentMissing"/> في ‏409 بَعدَ
    /// <see cref="DealAwaitsPayment"/> أُختِه في المَعنى.</summary>
    public static readonly IReadOnlyList<ApiErrorCode> All = new[]
    {
        new ApiErrorCode(AuthMissing, StatusCodes.Status401Unauthorized,
            "لا مِفتاح — أَرسِل رَأس Authorization: Bearer wsl_…"),
        new ApiErrorCode(AuthInvalid, StatusCodes.Status401Unauthorized,
            "المِفتاح غَير صالِح."),
        new ApiErrorCode(ScopeMissing, StatusCodes.Status403Forbidden,
            "المِفتاح لا يَحمِل النِطاق اللازِم لِهذِه النُقطَة."),
        new ApiErrorCode(EntitlementDenied, StatusCodes.Status403Forbidden,
            "باقَةُ هذا المُستَأجِر لا تَشمَل الوُصول عَبر الـAPI."),
        new ApiErrorCode(ActorNotAllowed, StatusCodes.Status403Forbidden,
            "فاعِلُ هذا المِفتاح غَير مُخَوَّلٍ بِهذا الإجراء."),
        new ApiErrorCode(NotFound, StatusCodes.Status404NotFound,
            "لا مَورِدَ بِهذا المُعَرِّف."),
        new ApiErrorCode(DealNotActive, StatusCodes.Status409Conflict,
            "الصَفقَة لَيسَت في حالَةٍ تَقبَل هذا الإجراء."),
        new ApiErrorCode(DealFinalStage, StatusCodes.Status409Conflict,
            "الصَفقَة في آخِر مَرحَلَة — لا تالِيَ لَها."),
        new ApiErrorCode(DealChangedMeanwhile, StatusCodes.Status409Conflict,
            "تَغَيَّرَت الصَفقَةُ في الأَثناء — أَعِد المُحاوَلَة بِطَلَبٍ جَديد."),
        new ApiErrorCode(DealAwaitsCarrier, StatusCodes.Status409Conflict,
            "طَلَبُ شَحنِ هذِه الصَفقَةِ عِندَ OTO — لا تَدخُلُ «شَحن» بِهذا الباب؛ تَدخُلُها حينَ يُثبِتُ جَوابُ OTO شُحنَتَها وبوليصَتَها: بِـ«أَكمِل الشَّحن» عِندَ البائِع، أَو بِإشعارٍ مِن OTO."),
        new ApiErrorCode(DealAwaitsPayment, StatusCodes.Status409Conflict,
            "عَلى هذِه الصَفقَةِ فاتورَةُ بَوّابَةِ المَتجَرِ ولَم يَثبُت دَفعُها — لا تَدخُلُ «دَفع» حَتّى يَتَحَقَّقَ الخادِمُ مِن دَفعِ فاتورَتِها لَدى البَوّابَة: بِـ«تَحَقَّق مِن الدَّفع» عَلى صَفحَةِ العَودَةِ بَعدَ الدَّفع؛ وبَعدَه يَقبَلُها هذا الباب."),
        new ApiErrorCode(DealPaymentMissing, StatusCodes.Status409Conflict,
            "هذِه صَفقَةٌ بِالبِطاقَةِ ولا مَرجِعَ دَفعٍ عَلَيها (تَعَذَّرَ حَجزُ مَبلَغِها عِندَ الطَّلَب) — لا تَدخُلُ «دَفع» بِلا مال؛ تُلغى أَو يُعادُ الشِّراء."),
        new ApiErrorCode(DealInvoiceShared, StatusCodes.Status409Conflict,
            "عَلى فاتورَةِ بَوّابَةِ المَتجَرِ المَفتوحَةِ لِهذِه الصَفقَةِ صَفَقاتٌ قائِمَةٌ أُخرى لَم تُدفَع — لا تُلغى وَحدَها: يُلغي المُشتَري طَلَبَه كُلَّه مِن صَفحَتِه، أَو تُلغى بَعدَ الدَفعِ ويُستَرجَعُ مَبلَغُها مِن لَوحَةِ بَوّابَةِ المَتجَر."),
        new ApiErrorCode(IdempotencyInProgress, StatusCodes.Status409Conflict,
            "طَلَبٌ بِنَفس مِفتاح مَرَّة-واحِدَة ما زالَ قَيدَ التَنفيذ."),
        new ApiErrorCode(IdempotencyKeyRequired, StatusCodes.Status422UnprocessableEntity,
            "رَأس Idempotency-Key مَطلوب عَلى كُلّ كِتابَة."),
        new ApiErrorCode(ValidationFailed, StatusCodes.Status422UnprocessableEntity,
            "الطَلَب غَير مُكتَمِل أَو غَير صالِح."),
    };

    public static readonly IReadOnlyList<string> Codes = All.Select(c => c.Code).ToArray();

    private static readonly Dictionary<string, ApiErrorCode> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.Ordinal);

    public static bool Contains(string code) => ByCode.ContainsKey(code);

    public static ApiErrorCode? Find(string code) =>
        ByCode.TryGetValue(code, out var c) ? c : null;

    /// <summary>يَرمي عِندَ الخَرق — لِمَواضِع التَركيب، فَرَمزٌ
    /// مَجهولٌ يُفشِل الإقلاعَ لا طَلَباً واحِداً. نَفس
    /// <c>CapabilityCatalog.Require</c>.</summary>
    public static ApiErrorCode Require(string code) =>
        Find(code) ?? throw new ArgumentException(
            $"رَمزُ الخَطَأ «{code}» خارِج مَعجَم ApiErrorCatalog. " +
            $"المَعجَم: {string.Join("، ", Codes)}.", nameof(code));
}

/// <summary>
/// <para><b>مَنفَذُ الخَطَأ الوَحيد تَحتَ <c>/api/v1</c></b>. رَقمُ
/// الحالَةِ يَأتي مِن المَعجَم لا مِن مَوضِع النِداء، والجِسمُ JSON
/// دائِماً — <b>ولا <c>Results.Forbid()</c> ولا
/// <c>Results.Redirect</c></b>: الأَوَّلُ يَرمي ‏500 لِغياب
/// <c>IAuthenticationService</c> (عَطَبٌ مُثَبَّت في
/// <c>ForbidResultTests</c>)، والثاني يُعطي العَميلَ الآليَّ
/// صَفحَةَ دُخولٍ بَدَلَ رَفض.</para>
/// </summary>
public static class ApiError
{
    public static IResult Of(string code, object? details = null) =>
        ApiOutcome.Error(code, details).ToResult();
}
