using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Api;

/// <summary>
/// <para><b>تَسَلسُلُ جَوابِ الـAPI — مَوضِعٌ واحِد.</b> والمُرَمِّزُ
/// <c>UnsafeRelaxedJsonEscaping</c> مَقصود: بِلا هُروبِ
/// <c>ع</c> تَخرُج العَرَبِيَّةُ حَرفاً كَما كُتِبَت، فَيَقرَأُها
/// الإنسانُ في <c>curl</c> والآلَةُ سَواء. والأَمانُ لا يُمَسّ:
/// المُخرَجُ <c>application/json</c> لا HTML، ولا يُحقَن في مُستَند.</para>
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string ContentType = "application/json; charset=utf-8";

    public static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, Options);
}

/// <summary>
/// <para><b>جَوابُ نُقطَةٍ كَقيمَة</b> — حالَةٌ وحُمولَة، قَبلَ أَن
/// يَصيرَ <see cref="IResult"/>. والسَبَبُ أَنّ جَوابَ الكِتابَة
/// <b>يُخَزَّن ثُمَّ يُعاد</b>: لا يُمكِن حِفظُ <c>IResult</c>،
/// ويُمكِن حِفظُ هذا.</para>
/// </summary>
public sealed record ApiOutcome(int Status, object Payload)
{
    public static ApiOutcome Ok(object payload) => new(StatusCodes.Status200OK, payload);

    /// <summary>خَطَأٌ بِرَمزٍ مِن المَعجَم المُغلَق — والحالَةُ
    /// تَأتي مِن المَعجَم لا مِن مَوضِع النِداء.</summary>
    public static ApiOutcome Error(string code, object? details = null)
    {
        var c = ApiErrorCatalog.Require(code);
        return new(c.Status, new ApiErrorBody(new ApiErrorPayload(c.Code, c.MessageAr, details)));
    }

    public IResult ToResult() =>
        Results.Text(ApiJson.Serialize(Payload), ApiJson.ContentType, statusCode: Status);
}

/// <summary>
/// <para><b>مَراسِمُ الكِتابَة تَحتَ <c>/api/v1</c></b> — رَأسُ
/// <c>Idempotency-Key</c>، ثُمَّ الحَجز، ثُمَّ العَمَلِيَّة، ثُمَّ
/// الإتمام. <b>مَوضِعٌ واحِدٌ يَقرَؤُه كُلُّ كاتِب</b>، فَلا تَنسى
/// نُقطَةٌ نِصفَ المَراسِم — وهذا بِعَينِه سَبَبُ وُقوع «الحِراسَة
/// سَطرٌ في الجِسم» (القاعِدَة ٦).</para>
/// </summary>
public static class ApiWrite
{
    public static async Task<IResult> OnceAsync(
        HttpContext http, ApiIdempotencyService idem, string endpoint,
        Func<ApiKeyPrincipal, Task<ApiOutcome>> operation)
    {
        var principal = http.ApiPrincipal()
            ?? throw new InvalidOperationException(
                "‏ApiWrite.OnceAsync نودِيَت على نُقطَةٍ بِلا ApiKeyFilter — " +
                "المَراسِمُ تَفتَرِض هُوِيَّةً مُعتَمَدَة.");

        var key = ApiIdempotencyService.NormalizeKey(
            http.Request.Headers[ApiIdempotencyService.HeaderName].ToString());
        if (key is null)
            return ApiOutcome.Error(ApiErrorCatalog.IdempotencyKeyRequired,
                new { header = ApiIdempotencyService.HeaderName }).ToResult();

        var begin = await idem.TryBeginAsync(
            principal.TenantSlug, principal.KeyId, key, endpoint, http.RequestAborted);

        switch (begin.Kind)
        {
            case IdempotencyBeginKind.Replay:
                // نَفسُ الجَوابِ حَرفاً — لا مُكافِئٌ لَه.
                return Results.Text(begin.Existing!.ResponseJson, ApiJson.ContentType,
                    statusCode: begin.Existing.ResponseStatus);

            case IdempotencyBeginKind.InProgress:
                return ApiOutcome.Error(ApiErrorCatalog.IdempotencyInProgress).ToResult();

            case IdempotencyBeginKind.EndpointMismatch:
                return ApiOutcome.Error(ApiErrorCatalog.ValidationFailed,
                    new { field = ApiIdempotencyService.HeaderName, used_for = begin.Existing!.Endpoint }).ToResult();
        }

        var outcome = await operation(principal);
        var json = ApiJson.Serialize(outcome.Payload);
        await idem.CompleteAsync(principal.TenantSlug, begin.Id, outcome.Status, json, http.RequestAborted);
        return Results.Text(json, ApiJson.ContentType, statusCode: outcome.Status);
    }
}
