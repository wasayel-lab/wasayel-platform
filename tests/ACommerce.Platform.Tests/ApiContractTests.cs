using System.Text.Json;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>عَقدُ <c>/api/v1</c> — الخَطَأُ والجَوابُ ومَرَّة-واحِدَة.</b>
/// كُلُّ ما هُنا دَوالُّ نَقِيَّةٌ أَو تَنفيذُ <see cref="IResult"/>
/// على سِياقٍ مُصطَنَع: لا قاعِدَةَ بَيانات، ولا خادِم.</para>
/// </summary>
public class ApiContractTests
{
    // ─── مَعجَمُ رُموز الخَطَأ ─────────────────────────────────────────

    /// <summary><b>أَحَدَ عَشَرَ رَمزاً ولا ثانِيَ عَشَر.</b> والرَمزُ
    /// الَّذي يُضاف هُنا يَحتاج مُنتِجاً حَيّاً واختِبارَ حالَة —
    /// وإلّا فَهُوَ سَطحٌ بِصِفر مُستَهلِك.</summary>
    [Fact]
    public void Exactly_eleven_error_codes_and_they_are_these()
        => Assert.Equal(
            new[]
            {
                "auth_missing", "auth_invalid",
                "scope_missing", "entitlement_denied", "actor_not_allowed",
                "not_found",
                "deal_not_active", "deal_final_stage", "idempotency_in_progress",
                "idempotency_key_required", "validation_failed",
            },
            ApiErrorCatalog.Codes);

    /// <summary>كُلُّ رَمزٍ يَحمِل حالَتَه ورِسالَتَه — فَلا يُكتَب
    /// رَقمُ حالَةٍ في مَوضِع استِعمال، ولا يَختَلِف رَمزانِ على
    /// نَفس المَعنى.</summary>
    [Fact]
    public void Every_code_declares_a_status_and_an_arabic_message()
    {
        Assert.True(ApiErrorCatalog.All.Count >= 11,
            $"أَداة عَمياء: {ApiErrorCatalog.All.Count} رَمزاً فَقَط.");

        foreach (var c in ApiErrorCatalog.All)
        {
            Assert.InRange(c.Status, 400, 499);
            Assert.True(c.MessageAr.Length > 10, $"«{c.Code}» بِرِسالَةٍ أَقصَرَ مِن أَن تُفيد.");
            Assert.Equal(c, ApiErrorCatalog.Require(c.Code));
        }
    }

    /// <summary><b>الحالاتُ كَما نَصَّ العَقد</b> (‏§٤٫٤): ‏401 بِلا
    /// مِفتاح · ‏403 نِطاقٌ ناقِصٌ أَو استِحقاق · ‏404 خارِجَ
    /// المُستَأجِر · ‏409 تَعارُضُ حالَة · ‏422 تَحَقُّق.</summary>
    [Theory]
    [InlineData("auth_missing", 401)]
    [InlineData("auth_invalid", 401)]
    [InlineData("scope_missing", 403)]
    [InlineData("entitlement_denied", 403)]
    [InlineData("actor_not_allowed", 403)]
    [InlineData("not_found", 404)]
    [InlineData("deal_not_active", 409)]
    [InlineData("deal_final_stage", 409)]
    [InlineData("idempotency_in_progress", 409)]
    [InlineData("idempotency_key_required", 422)]
    [InlineData("validation_failed", 422)]
    public void Each_code_maps_to_the_status_the_contract_names(string code, int status)
        => Assert.Equal(status, ApiErrorCatalog.Require(code).Status);

    /// <summary><b>ولا ‏3xx إطلاقاً</b> — الكُتلَة «ب» مِن حَقيبَة
    /// المُطابَقَة: عَميلٌ آلِيٌّ يَتبَعُ تَحويلاً يَصِل صَفحَةَ
    /// دُخولٍ ويَظُنُّها جَواباً.</summary>
    [Fact]
    public void No_error_code_carries_a_redirect_status()
        => Assert.DoesNotContain(ApiErrorCatalog.All, c => c.Status is >= 300 and < 400);

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("Auth_Missing")]
    public void Require_throws_on_a_code_outside_the_vocabulary(string code)
    {
        Assert.False(ApiErrorCatalog.Contains(code));
        var ex = Assert.Throws<ArgumentException>(() => ApiErrorCatalog.Require(code));
        foreach (var c in ApiErrorCatalog.Codes) Assert.Contains(c, ex.Message);
    }

    // ─── شَكلُ الجِسم ─────────────────────────────────────────────────

    /// <summary><b>‏<c>{ "error": { "code", "message_ar", "details" } }</c></b>
    /// حَرفاً. والأَسماءُ تُقرَأُ مِن JSON فِعليٍّ لا مِن نَوع — فَلَو
    /// بَدَّلَت سِياسَةُ تَسمِيَةٍ يَوماً أَسماءَ الحُقول لَاحمَرَّ
    /// هذا قَبلَ أَن يَنكَسِرَ عَميل.</summary>
    [Fact]
    public async Task An_error_body_has_the_shape_the_contract_names()
    {
        var (status, body) = await ExecuteAsync(
            ApiError.Of(ApiErrorCatalog.ScopeMissing, new { required = "deals:write" }));

        Assert.Equal(403, status);

        using var doc = JsonDocument.Parse(body);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("scope_missing", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message_ar").GetString()));
        Assert.Equal("deals:write", error.GetProperty("details").GetProperty("required").GetString());
    }

    /// <summary>و<c>details</c> يَغيب حينَ لا شَيءَ يُقال — لا
    /// <c>null</c> يُكتَب.</summary>
    [Fact]
    public async Task Details_is_omitted_when_there_is_nothing_to_say()
    {
        var (_, body) = await ExecuteAsync(ApiError.Of(ApiErrorCatalog.NotFound));
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("error").TryGetProperty("details", out _));
    }

    /// <summary><b>العَرَبِيَّةُ تَخرُج حَرفاً لا هُروباً</b> —
    /// فَـ<c>curl</c> يُقرَأ بِعَينٍ بَشَرِيَّة. وهذا هُوَ سَبَبُ
    /// <c>UnsafeRelaxedJsonEscaping</c>، مَقيساً لا مَوصوفاً.</summary>
    [Fact]
    public async Task Arabic_messages_are_not_unicode_escaped()
    {
        var (_, body) = await ExecuteAsync(ApiError.Of(ApiErrorCatalog.AuthMissing));
        Assert.DoesNotContain("\\u06", body);
        Assert.Contains("مِفتاح", body);
    }

    /// <summary>وكُلُّ جَوابٍ <c>application/json</c> — لا HTML.</summary>
    [Fact]
    public async Task Every_response_is_json()
    {
        var ctx = Context();
        await ApiError.Of(ApiErrorCatalog.AuthInvalid).ExecuteAsync(ctx);
        Assert.StartsWith("application/json", ctx.Response.ContentType);
    }

    [Fact]
    public async Task A_successful_outcome_serialises_its_payload_at_200()
    {
        var (status, body) = await ExecuteAsync(ApiOutcome.Ok(new { ok = true }).ToResult());
        Assert.Equal(200, status);
        Assert.Contains("\"ok\":true", body);
    }

    // ─── مَرَّة-واحِدَة: الدَوالُّ النَقِيَّة ───────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_idempotency_key_is_rejected(string? raw)
        => Assert.Null(ApiIdempotencyService.NormalizeKey(raw));

    [Fact]
    public void An_over_long_idempotency_key_is_rejected()
        => Assert.Null(ApiIdempotencyService.NormalizeKey(
            new string('k', ApiIdempotencyService.MaxKeyLength + 1)));

    [Fact]
    public void A_normal_idempotency_key_is_trimmed_and_kept()
        => Assert.Equal("k1", ApiIdempotencyService.NormalizeKey("  k1 "));

    /// <summary><b>المُعَرِّفُ مُرَكَّبٌ مِن مُعَرِّفِ المِفتاح</b> —
    /// فَمُستَأجِرانِ يَختارانِ <c>k1</c> لا يَتَصادَمان.</summary>
    [Fact]
    public void The_record_id_carries_the_key_id()
    {
        Assert.Equal("abc|k1", ApiIdempotencyRecord.IdFor("abc", "k1"));
        Assert.NotEqual(
            ApiIdempotencyRecord.IdFor("abc", "k1"),
            ApiIdempotencyRecord.IdFor("xyz", "k1"));
    }

    [Fact]
    public void A_completed_record_replays()
    {
        var rec = new ApiIdempotencyRecord
        {
            Id = "a|k", Endpoint = "deals.advance",
            Status = ApiIdempotencyRecord.StatusCompleted,
            ResponseStatus = 200, ResponseJson = "{\"x\":1}",
        };

        var begin = ApiIdempotencyService.Classify(rec, rec.Id, "deals.advance");
        Assert.Equal(IdempotencyBeginKind.Replay, begin.Kind);
        Assert.Equal("{\"x\":1}", begin.Existing!.ResponseJson);
    }

    [Fact]
    public void An_unfinished_record_answers_in_progress()
        => Assert.Equal(IdempotencyBeginKind.InProgress,
            ApiIdempotencyService.Classify(
                new ApiIdempotencyRecord { Id = "a|k", Endpoint = "deals.advance" },
                "a|k", "deals.advance").Kind);

    /// <summary><b>ونَفسُ المِفتاحِ على نُقطَةٍ أُخرى خَطَأُ عَميلٍ لا
    /// إعادَةُ مُحاوَلَة</b> — وإعادَةُ جَوابِ نُقطَةٍ أُخرى أَسوَأُ
    /// مِن رَفضِه.</summary>
    [Fact]
    public void The_same_key_on_another_endpoint_is_a_mismatch()
        => Assert.Equal(IdempotencyBeginKind.EndpointMismatch,
            ApiIdempotencyService.Classify(
                new ApiIdempotencyRecord
                {
                    Id = "a|k", Endpoint = "deals.advance",
                    Status = ApiIdempotencyRecord.StatusCompleted,
                },
                "a|k", "deals.cancel").Kind);

    // ─── تَصنيفُ قَرارِ الصَفقَة ───────────────────────────────────────

    private static Deal DealAt(DealStage stage, DealStatus status = DealStatus.Active,
                               string pattern = "marketplace")
        => new()
        {
            Id = Guid.NewGuid(), Pattern = pattern, Stage = stage, Status = status,
            InitiatorId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CounterpartyId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        };

    [Fact]
    public void A_cancelled_deal_that_will_not_advance_answers_409_not_active()
        => Assert.Equal("deal_not_active",
            DealApi.AdvanceRejectionCode(DealAt(DealStage.Booked, DealStatus.Cancelled)));

    /// <summary>‏<c>classifieds</c> يَنتَهي عِندَ <c>Confirmed</c> —
    /// فَلا تالِيَ لَه، والجَوابُ ‏409 لا ‏403.</summary>
    [Fact]
    public void A_deal_at_its_final_stage_answers_409_final_stage()
        => Assert.Equal("deal_final_stage",
            DealApi.AdvanceRejectionCode(DealAt(DealStage.Confirmed, pattern: "classifieds")));

    /// <summary>وما بَقِيَ رَفضُ فاعِل — ‏403، لا ‏409.</summary>
    [Fact]
    public void Anything_else_answers_403_actor_not_allowed()
        => Assert.Equal("actor_not_allowed",
            DealApi.AdvanceRejectionCode(DealAt(DealStage.Booked)));

    [Fact]
    public void An_advance_that_found_no_deal_answers_404()
        => Assert.Equal(404,
            DealApi.FromAdvance(new DealAdvanceResult(false, null, "deal not found")).Status);

    [Fact]
    public void A_successful_advance_answers_200_with_the_deal()
    {
        var deal = DealAt(DealStage.Confirmed);
        var outcome = DealApi.FromAdvance(new DealAdvanceResult(true, deal, null));

        Assert.Equal(200, outcome.Status);
        Assert.Equal(deal.Id, Assert.IsType<DealDto>(outcome.Payload).id);
    }

    [Fact]
    public void A_cancel_that_did_not_cancel_answers_409()
        => Assert.Equal(409, DealApi.FromCancel(DealAt(DealStage.Booked)).Status);

    [Fact]
    public void A_cancel_that_cancelled_answers_200()
        => Assert.Equal(200,
            DealApi.FromCancel(DealAt(DealStage.Booked, DealStatus.Cancelled)).Status);

    [Fact]
    public void A_cancel_on_a_missing_deal_answers_404()
        => Assert.Equal(404, DealApi.FromCancel(null).Status);

    // ─── العُضوِيَّة ───────────────────────────────────────────────────

    /// <summary><b>غَيرُ الطَرَفِ لَيسَ طَرَفاً</b> — وهذا هُوَ
    /// الشَرطُ الَّذي يَجعَل مِفتاحاً آلِيّاً عاجِزاً عَن إلغاءِ
    /// صَفقَةِ غَيرِه، بَينَما نُقطَةُ الواجِهَة لا تَسأَلُه.</summary>
    [Fact]
    public void Only_the_two_parties_are_parties()
    {
        var deal = DealAt(DealStage.Booked);
        Assert.True(DealApi.IsParty(deal, deal.InitiatorId));
        Assert.True(DealApi.IsParty(deal, deal.CounterpartyId!.Value));
        Assert.False(DealApi.IsParty(deal, Guid.NewGuid()));
        Assert.False(DealApi.IsParty(deal, Guid.Empty));
    }

    /// <summary>وصَفقَةٌ بِلا طَرَفٍ ثانٍ لا تَجعَل <c>Guid.Empty</c>
    /// طَرَفاً — وهو ما يَحمِلُه مِفتاحٌ بِلا فاعِل.</summary>
    [Fact]
    public void A_deal_without_a_counterparty_does_not_admit_the_empty_actor()
    {
        var deal = DealAt(DealStage.Offered);
        deal.CounterpartyId = null;
        Assert.False(DealApi.IsParty(deal, Guid.Empty));
    }

    // ─── سَبَبُ الإلغاء ────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_cancel_reason_is_rejected(string? raw)
        => Assert.Null(DealApi.NormalizeCancelReason(raw));

    [Fact]
    public void An_over_long_cancel_reason_is_rejected()
        => Assert.Null(DealApi.NormalizeCancelReason(
            new string('ر', DealApi.MaxCancelReasonLength + 1)));

    [Fact]
    public void A_normal_cancel_reason_is_trimmed_and_kept()
        => Assert.Equal("تَأَخَّرَ التَسليم", DealApi.NormalizeCancelReason(" تَأَخَّرَ التَسليم "));

    // ─── الأَدَوات ────────────────────────────────────────────────────

    private static DefaultHttpContext Context()
    {
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new DefaultHttpContext
        {
            RequestServices = sp,
            Response = { Body = new MemoryStream() },
        };
    }

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var ctx = Context();
        await result.ExecuteAsync(ctx);
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return (ctx.Response.StatusCode, await reader.ReadToEndAsync());
    }
}
