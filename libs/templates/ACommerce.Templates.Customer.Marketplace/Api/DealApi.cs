using ACommerce.Templates.Customer.Marketplace.Services.Deals;

namespace ACommerce.Templates.Customer.Marketplace.Api;

/// <summary>صَفقَةٌ كَما يَراها عَميلُ الـAPI. <b>لا
/// <c>Timeline</c> ولا <c>Refs</c> هُنا</b>: الأَوَّلُ سِجِلُّ
/// تَدقيقٍ داخِليّ، والثاني يَحمِل مُعَرِّفاتِ دَفعٍ خارِجِيَّة —
/// وكَشفُ حَقلٍ أَسهَلُ مِن سَحبِه بَعدَ أَن يُبنى عَلَيه عَميل.</summary>
public sealed record DealDto(
    Guid id,
    string pattern,
    string stage,
    string stage_label_ar,
    string status,
    Guid initiator_id,
    Guid? counterparty_id,
    Guid? listing_id,
    string listing_title,
    decimal amount_sar,
    string? next_stage,
    string? actor_for_current_stage,
    DateTime created_at,
    DateTime updated_at);

/// <summary>
/// <para><b>تَرجَمَةُ قَرارِ <c>DealsService</c> إلى عَقدِ الـAPI — ولا
/// قَرارَ يُعادُ اتِّخاذُه هُنا.</b> الخِدمَةُ تَقول «لا» وتَذكُر
/// سَبَبَها بِالعَرَبِيَّة؛ وعَقدُ الـAPI يَحتاج <b>رَمزاً</b>
/// و<b>حالَةً</b>. فَهذا الصَنفُ يُسَمّي ما قَرَّرَته، ولا
/// يُقَرِّر.</para>
///
/// <para><b>ولِماذا لا تُقرَأُ رِسالَةُ السَبَبِ نَصّاً</b>: نَصٌّ
/// عَرَبِيٌّ في <c>DealsService</c> يُصَحَّح إملاؤُه يَوماً فَيَنكَسِر
/// تَصنيفُ الأَخطاء صامِتاً. والتَصنيفُ هُنا يُعيد سُؤالَ
/// <b>البَيانات</b> الَّتي بَنَت القَرار — الحالَةُ والمَرحَلَةُ
/// التالِيَة — وهي دَوالُّ نَقِيَّة في <c>DealsPolicy</c>، مَوضِعٌ
/// واحِدٌ لا نُسخَةٌ ثانِيَة.</para>
/// </summary>
public static class DealApi
{
    public static DealDto ToDto(Deal d)
    {
        var next = DealsPolicy.Next(d.Pattern, d.Stage);
        return new DealDto(
            d.Id, d.Pattern, d.Stage.ToString(), DealsPolicy.LabelAr(d.Stage), d.Status.ToString(),
            d.InitiatorId, d.CounterpartyId, d.ListingId, d.ListingTitle, d.AmountSar,
            next?.ToString(), DealsPolicy.Actor(d.Stage),
            d.CreatedAt, d.UpdatedAt);
    }

    /// <summary>
    /// <para><b>هَل هذا الفاعِلُ طَرَفٌ في الصَفقَة؟</b> — دالَّةٌ
    /// نَقِيَّة، وهي <b>سَطحُ الـAPI أَشَدُّ مِن سَطحِ الشاشَة
    /// عَمداً</b>: نُقطَةُ الإلغاء في الواجِهَة
    /// (<c>/{slug}/deals/{id}/cancel</c>) كانَت تَكتَفي بِجَلسَةٍ
    /// صالِحَة ولا تَسأَلُ عَن العُضوِيَّة. فَمِفتاحٌ آلِيٌّ بِلا هذا
    /// الشَرط كانَ سَيُلغي صَفقَةَ غَيرِه.</para>
    ///
    /// <para><b>‏2026-08-22 — والشَرطُ نَزَلَ إلى الخِدمَة</b>: صارَت
    /// <c>DealsService.CancelAsync</c> تَطلُب
    /// <c>DealCanceller</c> في تَوقيعِها، و
    /// <c>DealCancelAuthorization.Validate</c> تَرُدُّ
    /// <c>actor_not_party</c>. فَهذِه الدالَّةُ <b>لَم تُحذَف</b> ولا
    /// صارَت تَكراراً: هي تَحكُم <b>ما يَراهُ</b> السائِل
    /// (‏<c>GET …/deals/{id}</c> يَرُدّ ‏404 لِغَيرِ الطَرَف — لا
    /// نُفشي وُجودَ مَورِد)، والخِدمَةُ تَحكُم <b>ما يَفعَلُه</b>.
    /// حارِسانِ لِسُؤالَين، لا سَطرانِ لِسُؤالٍ واحِد.</para>
    /// </summary>
    public static bool IsParty(Deal d, Guid actorId) =>
        d.InitiatorId == actorId || d.CounterpartyId == actorId;

    /// <summary>
    /// <para><b>تَصنيفُ رَفضِ <c>AdvanceAsync</c></b> — الصَفقَةُ
    /// المُرفَقَةُ بِالنَتيجَة هي المَقروءَة، فَالتَصنيفُ يَقرَأُ
    /// نَفسَ الحالَةِ الَّتي قَرَّرَت.</para>
    /// </summary>
    public static string AdvanceRejectionCode(Deal deal)
    {
        if (deal.Status != DealStatus.Active) return ApiErrorCatalog.DealNotActive;
        if (DealsPolicy.Next(deal.Pattern, deal.Stage) is null) return ApiErrorCatalog.DealFinalStage;
        return ApiErrorCatalog.ActorNotAllowed;
    }

    /// <summary>‏<c>advance</c> كامِلاً: مِن نَتيجَةِ الخِدمَة إلى
    /// جَوابِ الـAPI.</summary>
    public static ApiOutcome FromAdvance(DealAdvanceResult result)
    {
        if (result.Deal is null) return ApiOutcome.Error(ApiErrorCatalog.NotFound);
        if (result.Ok) return ApiOutcome.Ok(ToDto(result.Deal));

        var code = AdvanceRejectionCode(result.Deal);
        return ApiOutcome.Error(code, new
        {
            stage = result.Deal.Stage.ToString(),
            status = result.Deal.Status.ToString(),
            required_actor = DealsPolicy.Actor(result.Deal.Stage),
        });
    }

    /// <summary>
    /// <para>‏<c>cancel</c> كامِلاً. <b>ورَفضُ الخِدمَةِ صارَ
    /// مَقروءاً</b>: كانَت <c>CancelAsync</c> تُعيد <c>Deal?</c>
    /// فَيُستَنتَجُ الرَفضُ مِن الحالَةِ بَعدَ النِداء؛ صارَت تُعيد
    /// <c>DealCancelResult</c> بِرَمزٍ مِن مَعجَمٍ مُغلَق.</para>
    ///
    /// <para><b>و<c>actor_not_party</c> يُطوى إلى ‏404 لا ‏403</b> —
    /// نَفسُ قاعِدَةِ الكُتلَة (أ): لا نُفشي وُجودَ مَورِدٍ لا
    /// يَملِكُه السائِل. وعَمَلِيّاً لا يُبلَغ هذا الفَرعُ مِن
    /// الـAPI أَصلاً لِأَنّ النُقطَةَ تَفحَص <c>IsParty</c> قَبلَه —
    /// وبَقاؤُه هُنا هُوَ ما يَجعَلُ الطَبَقَتَينِ مُستَقِلَّتَين:
    /// إسقاطُ إحداهُما لا يَفتَح البابَ.</para>
    /// </summary>
    public static ApiOutcome FromCancel(DealCancelResult result)
    {
        if (result.Ok && result.Deal is not null) return ApiOutcome.Ok(ToDto(result.Deal));

        return result.Violation!.Code switch
        {
            DealCancelAuthorization.DealNotFound  => ApiOutcome.Error(ApiErrorCatalog.NotFound),
            DealCancelAuthorization.ActorNotParty => ApiOutcome.Error(ApiErrorCatalog.NotFound),
            DealCancelAuthorization.DealNotActive => ApiOutcome.Error(
                ApiErrorCatalog.DealNotActive,
                new { status = result.Deal?.Status.ToString() }),
            _ => ApiOutcome.Error(ApiErrorCatalog.ValidationFailed),
        };
    }

    /// <summary>سَبَبُ الإلغاء مِن جِسمِ الطَلَب — <c>null</c> يَعني
    /// خَرقَ تَحَقُّق. والحَدُّ يَمنَع نَصّاً بِلا سَقفٍ في
    /// <c>Timeline</c>.</summary>
    public const int MaxCancelReasonLength = 300;

    public static string? NormalizeCancelReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var r = raw.Trim();
        return r.Length > MaxCancelReasonLength ? null : r;
    }
}

/// <summary>جِسمُ طَلَبِ الإلغاء — JSON حَصراً، لا
/// <c>req.Form[</c>.</summary>
public sealed record CancelDealRequest(string? reason);

/// <summary>جِسمُ طَلَبِ التَقَدُّم — المُلاحَظَة اختِيارِيَّة.</summary>
public sealed record AdvanceDealRequest(string? note);

/// <summary>قائِمَةٌ مُغَلَّفَة — <c>{ "deals": [...], "count": n }</c>.
/// والتَغليفُ لِيَبقى مُمكِناً إضافَةُ تَصفُّحٍ لاحِقاً بِلا كَسرِ
/// عَقد.</summary>
public sealed record DealListResponse(IReadOnlyList<DealDto> deals, int count);
