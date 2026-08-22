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
    public const string IdempotencyInProgress  = "idempotency_in_progress";
    public const string IdempotencyKeyRequired = "idempotency_key_required";
    public const string ValidationFailed       = "validation_failed";

    /// <summary>الأَحَدَ عَشَرَ رَمزاً — مُرَتَّبَةً بِحالَتِها ثُمَّ
    /// بِرَمزِها.</summary>
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
