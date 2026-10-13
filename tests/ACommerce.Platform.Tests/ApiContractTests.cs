using System.Text.Json;
using ACommerce.Platform.I18n;
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
    /// وإلّا فَهُوَ سَطحٌ بِصِفر مُستَهلِك.
    /// <para><b>قُلِبَ بِاسمِه — صارَت اثنَي عَشَر</b> (‏ADR-036، ‏2026-10-12): كانَ
    /// <c>Exactly_eleven_error_codes_and_they_are_these</c>. والثانِيَ عَشَرَ <c>deal_changed_meanwhile</c> (‏409) مُنتِجُه
    /// حَيٌّ — <c>DealApi.FromAdvance</c>/<c>FromCancel</c> مِن رَمزِ الخِدمَة — واختِبارُ حالَتِه في الصَفِّ التالي وصَفّا
    /// التَصنيفِ أَدناه.</para>
    /// <para><b>قُلِبَ بِاسمِه — صارَت ثَلاثَةَ عَشَر</b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12؛ ‏ADR-033 §٧‑ح): كانَ
    /// <c>Exactly_twelve_error_codes_and_they_are_these</c>. والثالِثَ عَشَرَ <c>deal_awaits_carrier</c> (‏409) مُنتِجُه حَيٌّ —
    /// <c>DealApi.FromAdvance</c> مِن رَمزِ الخِدمَةِ نَفسِه، حينَ تَرُدُّ <c>AdvanceAsync</c> صَفقَةً طَلَبُها عِندَ OTO — واختِبارُ حالَتِه في
    /// الصَفِّ التالي وصَفّا التَصنيفِ أَدناه.</para>
    /// <para><b>قُلِبَ بِاسمِه — صارَت أَربَعَةَ عَشَر</b> (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12): كانَ
    /// <c>Exactly_thirteen_error_codes_and_they_are_these</c>. والرابِعَ عَشَرَ <c>deal_awaits_payment</c> (‏409) مُنتِجُه حَيٌّ —
    /// <c>DealApi.FromAdvance</c> مِن رَمزِ الخِدمَةِ نَفسِه، حينَ تَرُدُّ <c>AdvanceAsync</c> صَفقَةً عَلَيها فاتورَةُ بَوّابَةٍ لَم يَثبُت دَفعُها —
    /// واختِبارُ حالَتِه في الصَفِّ التالي وصَفّا التَصنيفِ أَدناه.</para>
    /// <para><b>قُلِبَ بِاسمِه — صارَت خَمسَةَ عَشَر</b> (‏2026-10-13، مُراجَعَةُ بَوّابَةِ المَوجَة 3b، G5 M4؛ ‏ADR-032 §٨‑ز مُلحَقاً): كانَ
    /// <c>Exactly_fourteen_error_codes_and_they_are_these</c>. والخامِسَ عَشَرَ <c>deal_invoice_shared</c> (‏409) مُنتِجُه حَيٌّ —
    /// <c>DealApi.FromCancel</c> مِن رَمزِ الخِدمَةِ نَفسِه، حينَ تَرُدُّ <c>CancelAsync</c> إلغاءَ صَفقَةٍ واحِدَةٍ مِن فاتورَةِ بَوّابَةٍ مَفتوحَةٍ
    /// مُشتَرَكَة؛ وكانَ ذلكَ الرَمزُ يَخرُجُ <c>validation_failed</c> (‏422). واختِبارُ حالَتِه في الصَفِّ التالي.</para></summary>
    [Fact]
    public void Exactly_fifteen_error_codes_and_they_are_these()
        => Assert.Equal(
            new[]
            {
                "auth_missing", "auth_invalid",
                "scope_missing", "entitlement_denied", "actor_not_allowed",
                "not_found",
                "deal_not_active", "deal_final_stage", "deal_changed_meanwhile", "deal_awaits_carrier", "deal_awaits_payment",
                "deal_invoice_shared",
                "idempotency_in_progress",
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
    [InlineData("deal_changed_meanwhile", 409)]
    [InlineData("deal_awaits_carrier", 409)]
    [InlineData("deal_awaits_payment", 409)]
    [InlineData("deal_invoice_shared", 409)]
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
        => Assert.Equal(409, DealApi.FromCancel(DealCancelResult.Refused(
            new DealCancelViolation(DealCancelAuthorization.DealNotActive, "…"),
            DealAt(DealStage.Booked, DealStatus.Cancelled))).Status);

    /// <summary><b>تَعارُضُ النُسخَةِ ‏409 بِرَمزِه</b> (‏ADR-036، ‏2026-10-12) — في التَقَدُّمِ يُقرَأُ رَمزُ الخِدمَةِ قَبلَ
    /// تَصنيفِ الحالَة، وإلّا قيلَ «فاعِلٌ غَيرُ مُخَوَّل» (‏403) عَن مُخَوَّلٍ سَبَقَه غَيرُه؛ وفي الإلغاءِ بِرَمزِ المُخالَفَةِ نَفسِه
    /// لا بِسُقوطِه إلى ‏422.</summary>
    [Fact]
    public void A_write_that_lost_the_version_race_answers_409_deal_changed_meanwhile()
    {
        var advance = DealApi.FromAdvance(new DealAdvanceResult(false, DealAt(DealStage.Booked), "…", DealsService.ChangedMeanwhile));
        Assert.Equal(409, advance.Status);
        Assert.Contains("\"deal_changed_meanwhile\"", ApiJson.Serialize(advance.Payload), StringComparison.Ordinal);
        Assert.Equal("actor_not_allowed", DealApi.AdvanceRejectionCode(DealAt(DealStage.Booked)));   // ما كانَ سَيُقال

        var cancel = DealApi.FromCancel(DealCancelResult.Refused(
            new DealCancelViolation(DealsService.ChangedMeanwhile, "…"), DealAt(DealStage.Booked)));
        Assert.Equal(409, cancel.Status);
        Assert.Contains("\"deal_changed_meanwhile\"", ApiJson.Serialize(cancel.Payload), StringComparison.Ordinal);
    }

    /// <summary><b>طَلَبُ الصَفقَةِ عِندَ الناقِلِ ‏409 بِرَمزِه</b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12): رَمزُ الخِدمَةِ يُقرَأُ قَبلَ تَصنيفِ
    /// الحالَة — وإلّا قيلَ «فاعِلٌ غَيرُ مُخَوَّل» (‏403) عَن الطَرَفِ الَّذي يُخرِجُها و<c>required_actor</c> هُوَ. <b>والسالِب</b>: رَفضُ فاعِلٍ
    /// بِلا رَمزٍ عَلى الصَفقَةِ نَفسِها يَبقى ‏403 <c>actor_not_allowed</c>، وتَعارُضُ النُسخَةِ يَبقى رَمزَه.</summary>
    [Fact]
    public void A_deal_awaiting_the_carrier_answers_409_deal_awaits_carrier_and_other_refusals_keep_their_codes()
    {
        var deal = DealAt(DealStage.Confirmed);
        var awaits = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "…", DealShipping.DealAwaitsCarrier));
        Assert.Equal(409, awaits.Status);
        Assert.Contains("\"deal_awaits_carrier\"", ApiJson.Serialize(awaits.Payload), StringComparison.Ordinal);
        Assert.DoesNotContain("actor_not_allowed", ApiJson.Serialize(awaits.Payload), StringComparison.Ordinal);

        var actor = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "يَتَطَلَّب فاعِلاً مِن نَوع: counterparty"));
        Assert.Equal(403, actor.Status);
        Assert.Contains("\"actor_not_allowed\"", ApiJson.Serialize(actor.Payload), StringComparison.Ordinal);

        var raced = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "…", DealsService.ChangedMeanwhile));
        Assert.Contains("\"deal_changed_meanwhile\"", ApiJson.Serialize(raced.Payload), StringComparison.Ordinal);
    }

    /// <summary><b>فاتورَةُ الصَفقَةِ عِندَ بَوّابَةِ المَتجَرِ لَم تُدفَع ‏409 بِرَمزِها</b> (‏ADR-032 الخُطوَة 10، تَيّارُ S9، ‏2026-10-12) — نَسَقُ صَفِّ
    /// الناقِلِ أَعلاه حَرفاً: رَمزُ الخِدمَةِ يُقرَأُ قَبلَ تَصنيفِ الحالَة، وإلّا قيلَ «فاعِلٌ غَيرُ مُخَوَّل» (‏403) عَن المُشتَري الَّذي يُدخِلُها
    /// «دَفع» و<c>required_actor</c> هُوَ (مَقيسٌ قَبلَ سَطرِ <c>FromAdvance</c>: ‏403 حَيثُ يُنتَظَرُ ‏409). <b>والسالِب</b>:
    /// رَفضُ فاعِلٍ بِلا رَمزٍ عَلى الصَفقَةِ نَفسِها يَبقى ‏403، ورَمزُ الناقِلِ يَبقى رَمزَه.</summary>
    [Fact]
    public void A_deal_awaiting_its_gateway_payment_answers_409_deal_awaits_payment_and_other_refusals_keep_their_codes()
    {
        var deal = DealAt(DealStage.Confirmed);
        var awaits = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "…", GatewayPayments.DealAwaitsPayment));
        Assert.Equal(409, awaits.Status);
        Assert.Contains("\"deal_awaits_payment\"", ApiJson.Serialize(awaits.Payload), StringComparison.Ordinal);
        Assert.DoesNotContain("actor_not_allowed", ApiJson.Serialize(awaits.Payload), StringComparison.Ordinal);

        var actor = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "يَتَطَلَّب فاعِلاً مِن نَوع: initiator"));
        Assert.Equal(403, actor.Status);
        Assert.Contains("\"actor_not_allowed\"", ApiJson.Serialize(actor.Payload), StringComparison.Ordinal);

        var carrier = DealApi.FromAdvance(new DealAdvanceResult(false, deal, "…", DealShipping.DealAwaitsCarrier));
        Assert.Contains("\"deal_awaits_carrier\"", ApiJson.Serialize(carrier.Payload), StringComparison.Ordinal);
    }

    /// <summary><b>ورِسالَتُه تُسَمّي البابَ الَّذي يُثبِتُ الدَفعَ بِحَرفِ زِرِّه في القامُوس</b> (<c>pay.return.verify</c>) — نَسَقُ رِسالَةِ الناقِل:
    /// لا وَعدَ بِنَقلٍ لا يُؤَدّيه كود، ولا «يَدخُلُها» بِلا شَرطِه.</summary>
    [Fact]
    public void The_deal_awaits_payment_message_names_the_verify_button_and_says_this_door_accepts_the_deal_after_it()
    {
        var message = ApiErrorCatalog.Require(ApiErrorCatalog.DealAwaitsPayment).MessageAr;
        var verify = LocaleCatalog.Text(LocaleCatalog.Arabic, "pay.return.verify");
        Assert.Contains($"«{verify}»", message, StringComparison.Ordinal);
        Assert.Contains("لا تَدخُلُ «دَفع»", message, StringComparison.Ordinal);
        Assert.Contains("وبَعدَه يَقبَلُها هذا الباب", message, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_the_deal_awaits_carrier_message_says_the_carrier_moves_the_deal_and_names_neither_the_resume_nor_the_label</c>
    /// (‏<c>5c456ba</c>): «تَدخُلُ «شَحن» حينَ يُثبِتُ الناقِلُ شُحنَتَها». <b>والآن</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، البَندُ A0 (ix)، ‏2026-10-12؛
    /// ‏ADR-033 §٧‑ك): الرِسالَةُ تُسَمّي OTO، والشَرطَ كامِلاً (الشُحنَةُ <b>والبوليصَة</b>)، والبابَينِ اللَّذَينِ يُدخِلانِها «شَحن» — «أَكمِل الشَّحن»
    /// بِحَرفِ زِرِّه في القامُوس (أَوَّلِ مَقطَعَي <c>deals.detail.ship_resume</c>) وإشعارَ OTO (§٧‑ط) — وأَنّ هذا البابَ لَيسَ مِنهُما.</summary>
    [Fact]
    public void The_deal_awaits_carrier_message_names_OTO_the_label_and_both_doors_that_move_the_deal()
    {
        var message = ApiErrorCatalog.Require(ApiErrorCatalog.DealAwaitsCarrier).MessageAr;
        Assert.Equal("طَلَبُ شَحنِ هذِه الصَفقَةِ عِندَ OTO — لا تَدخُلُ «شَحن» بِهذا الباب؛ تَدخُلُها حينَ يُثبِتُ جَوابُ OTO شُحنَتَها وبوليصَتَها: بِـ«أَكمِل الشَّحن» عِندَ البائِع، أَو بِإشعارٍ مِن OTO.", message);

        var resume = LocaleCatalog.Text(LocaleCatalog.Arabic, "deals.detail.ship_resume");
        var button = resume[..resume.IndexOf(" — ", StringComparison.Ordinal)];
        Assert.Contains($"«{button}»", message, StringComparison.Ordinal);
        Assert.Contains("بِإشعارٍ مِن OTO", message, StringComparison.Ordinal);
        Assert.Contains("شُحنَتَها وبوليصَتَها", message, StringComparison.Ordinal);
        Assert.Contains("لا تَدخُلُ «شَحن» بِهذا الباب", message, StringComparison.Ordinal);
        Assert.DoesNotContain("الناقِل", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancel_that_cancelled_answers_200()
        => Assert.Equal(200,
            DealApi.FromCancel(DealCancelResult.Cancelled(
                DealAt(DealStage.Booked, DealStatus.Cancelled))).Status);

    [Fact]
    public void A_cancel_on_a_missing_deal_answers_404()
        => Assert.Equal(404, DealApi.FromCancel(DealCancelResult.Refused(
            new DealCancelViolation(DealCancelAuthorization.DealNotFound, "…"))).Status);

    /// <summary><b>وغَيرُ الطَرَف يُطوى إلى ‏404 لا ‏403</b> — لا
    /// نُفشي وُجودَ مَورِدٍ لا يَملِكُه السائِل. وهذا الفَرعُ لا
    /// يُبلَغ مِن النُقطَةِ لِأَنَّها تَفحَص <c>IsParty</c> قَبلَه —
    /// وبَقاؤُه هُوَ ما يَجعَلُ إسقاطَ أَحَدِ الحارِسَينِ غَيرَ
    /// فاتِحٍ لِلباب.</summary>
    [Fact]
    public void A_cancel_by_someone_who_is_not_a_party_answers_404_not_403()
        => Assert.Equal(404, DealApi.FromCancel(DealCancelResult.Refused(
            new DealCancelViolation(DealCancelAuthorization.ActorNotParty, "…"),
            DealAt(DealStage.Booked))).Status);

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
