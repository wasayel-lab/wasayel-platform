using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>مُهايِئُ HTTP لِنُقاطِ Paddle</b> — يُحَوِّل النَموذَجَ إلى
/// مُسَوَّدَة، والقَرارَ النَقِيَّ إلى رَدٍّ وسَطرِ لوغ.</para>
///
/// <para><b>ونَفسُ حُجَّةِ <see cref="PayPalSurface"/> حَرفاً</b>:
/// <c>Services/Subscriptions</c> مَفروضٌ عَلَيه <b>صِفرُ مَعرِفَةٍ
/// بِـHTTP</b> (<c>TenantConfigServiceShapeTests</c>)، فَما يَعرِف
/// <c>HttpRequest</c> و<c>IResult</c> يَسكُن هُنا مَعَ
/// النُقطَة.</para>
/// </summary>
public static class PaddleSurface
{
    // ─── رُموزُ الرَدّ — مَعجَمٌ مُغلَقٌ لا سَلاسِلُ مَنثورَة ───────
    public const string Unavailable    = "paddle_unavailable";
    public const string Unreadable     = "paddle_event_unreadable";
    public const string Refused        = "paddle_tx_refused";
    public const string LinkMissing    = "paddle_tx_no_link";
    public const string AlreadySettled = "paddle_tx_settled";

    /// <summary>رَأسُ التَوقيعِ مِن الطَلَب — الغائِبُ <c>null</c>،
    /// و<c>PaddleWebhookGuard.Gate</c> هي الَّتي تَحكُم.</summary>
    public static string? SignatureFrom(HttpRequest req)
        => req.Headers.TryGetValue(PaddleSignature.Header, out var v)
            ? v.ToString()
            : null;

    /// <summary>
    /// <para>رَفضُ البَوّابَة — <b>‏400 لا ‏500، وبِلا تَحويلٍ ولا
    /// HTML</b>: المُنادي آلَةٌ لا مُتَصَفِّح.</para>
    ///
    /// <para><b>و‏400 لا ‏2xx عَمداً</b>: الـ2xx تُخبِرُ Paddle أَنّ
    /// التَسليمَ نَجَح فَتَتَوَقَّف عَن الإعادَة — <b>ورِسالَةٌ
    /// رُفِضَ تَوقيعُها يَجِبُ أَلّا تُعَدَّ مُسَلَّمَة</b>. و‏4xx
    /// لا ‏5xx لِأَنّ إعادَةَ رِسالَةٍ بِتَوقيعٍ فاسِدٍ آلافُ
    /// المُحاوَلاتِ على بابٍ مُغلَق.</para>
    /// </summary>
    public static IResult Rejected(ILogger log, PaddleWebhookGate gate)
    {
        var code = PaddleWebhookGuard.GateCode(gate);
        // يُقالُ في اللوغ بِاسمِه: «لا سِرَّ وِجهَة» غَيرُ «تَوقيعٌ
        // فاشِل» غَيرُ «زَمَنٌ خارِجَ التَسامُح»، وخَلطُها يُرسِل
        // المالِكَ يُفَتِّشُ عَن سِرٍّ خاطِئٍ ومُشكِلَتُه ساعَةُ خادِم.
        log.LogWarning("[Paddle] رِسالَةٌ مَرفوضَة: {Code}", code);
        return Results.BadRequest(new { error = code });
    }

    public static IResult UnreadableBody(ILogger log)
    {
        log.LogWarning("[Paddle] جِسمٌ مُوَثَّقٌ لكِنّ غَيرُ مَقروء — لا event_id/event_type.");
        return Results.BadRequest(new { error = Unreadable });
    }

    /// <summary>
    /// <para><b>أَيَشفي تَكرارُ الرِسالَةِ هذا الفَرع؟</b> — وهُوَ
    /// السُؤالُ الَّذي يُحَدِّد رَمزَ الرَدّ، لا «أَهُوَ خَطَأٌ أَم
    /// لا». نَفسُ قِسمَةِ <see cref="PayPalOrderSurface.HealsOnRedelivery"/>
    /// حَرفاً.</para>
    ///
    /// <para><b>فَرعانِ يَشفِيهِما التَكرار</b>: مَرجِعٌ مَجهولٌ
    /// ومَتجَرٌ بِلا وَثيقَةِ باقَة. كِلاهُما يَقول «<b>المالُ وَصَلَ
    /// ويَنقُصُنا نَحنُ وَثيقَة</b>»، ويُصلِحُها المُشرِفُ بِنَقرَةٍ
    /// فَتُطَبَّق رِسالَةٌ لاحِقَةٌ مِن تِلقاءِ نَفسِها. ورَدُّ ‏200
    /// هُنا يُلغي تِلكَ الشَبَكَةَ كُلَّها.</para>
    ///
    /// <para><b>وثالِثٌ أُلحِقَ بِهِما بِقِياسٍ لا بِذَوق:
    /// <c>AmountMismatch</c>.</b> والدافِعُ <b>لا يَملِكُ أَن
    /// يُغَيِّرَ المَبلَغ</b> — الكَمِّيَّةُ مَحبوسَةٌ ‏1..1 والسِعرُ
    /// مُثَبَّتٌ في المُعامَلَة — فَعَدَمُ التَطابُقِ <b>لَيسَ دافِعاً
    /// دَفَعَ أَقَلّ، بَل تَعريفَينِ لِقيمَةٍ واحِدَةٍ عِندَنا</b>
    /// (ضَريبَةٌ، خَصمٌ، رَصيد). وذاكَ <b>يُشفى</b>: تَهيئَةٌ
    /// تُصَحَّح، أَو إعادَةٌ يَدَوِيَّةٌ مِن لَوحَةِ Paddle.</para>
    ///
    /// <para><b>ورَدُّ ‏200 عَلَيه كانَ يَجعَلُ الخَسارَةَ
    /// نِهائِيَّة</b>: تَتَوَقَّفُ إعادَةُ Paddle، فَيَبقى المالُ
    /// مَقبوضاً والباقَةُ بِلا يَومٍ واحِدٍ <b>ولا نافِذَةَ تَشفيه</b>.
    /// و‏503 تُبقي النافِذَةَ مَفتوحَة.</para>
    /// </summary>
    public static bool HealsOnRedelivery(PaddleAction action)
        => action is PaddleAction.UnknownReference
                  or PaddleAction.UnknownTenant
                  or PaddleAction.AmountMismatch;

    /// <summary><b>قَرارٌ بِلا كِتابَة — ويُقالُ سَبَبُه.</b> وخَمسَةُ
    /// فُروعٍ تُصَعَّدُ إلى <c>Error</c>: مَرجِعٌ مَجهول (مالٌ وَصَلَ
    /// ولا يُعرَف لِمَن)، ومَبلَغٌ لا يُطابِق، وحالَةٌ تُناقِض اسمَ
    /// الحَدَث، ومَتجَرٌ بِلا باقَة، <b>ودَفعَةٌ ثانِيَةٌ على نَفسِ
    /// المَرجِع</b> (مالٌ قُبِضَ مَرَّتَينِ ومُدِّدَ مَرَّة).
    /// <b>وسِجِلٌّ يَصرُخ عِندَ كُلّ شَيءٍ لا يُقرَأ.</b></summary>
    public static IResult NoWrite(ILogger log, PaddleEvent e, PaddleDecision d)
    {
        if (d.Action is PaddleAction.UnknownReference
                     or PaddleAction.UnknownTenant
                     or PaddleAction.AmountMismatch
                     or PaddleAction.StatusNotCompleted
                     or PaddleAction.DuplicatePayment)
            log.LogError("[Paddle] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);
        else
            log.LogInformation("[Paddle] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);

        var body = new { action = d.Action.ToString(), applied = false };
        return HealsOnRedelivery(d.Action)
            ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(body);
    }

    public static IResult Applied(ILogger log, PaddleEvent e, PaddleDecision d, string slug)
    {
        if (d.TouchesPlan)
            log.LogInformation("[Paddle] {Action} — {Slug} حَتّى {Expires:yyyy-MM-dd}: {Reason}",
                d.Action, slug, d.NewExpiresAt, d.ReasonAr);
        else
            log.LogInformation("[Paddle] {Action} — {Slug} ({Status}): {Reason}",
                d.Action, slug, d.TransactionStatus, d.ReasonAr);

        return Results.Ok(new { action = d.Action.ToString(), applied = true });
    }

    /// <summary>سَطرُ تَدقيقٍ لِكُلّ حَرَكَةِ مال — «لا قَرارَ إداريٌّ
    /// بِلا أَثَر»، وتَحريكُ تاريخِ انتِهاءِ مَتجَرٍ قَرارٌ إداريٌّ
    /// ولَو كانَ فاعِلُه آلَة. نَفسُ شَكلِ
    /// <see cref="PayPalOrderSurface.AuditAsync"/> حَرفاً.</summary>
    public static Task AuditAsync(
        Services.Audit.AuditWriter audit, PaddleEvent e,
        PaddleTransactionRecord record, PaddleDecision d, HttpContext http)
        => audit.WriteAsync(
            string.IsNullOrWhiteSpace(record.TenantSlug)
                ? Services.Subscriptions.PayPalBillingService.UnknownTenantScope
                : record.TenantSlug,
            actorId: null, actorName: $"paddle · {e.EventType}",
            Services.Subscriptions.PaddleBillingService.AuditActionFor(d.Action),
            "TenantPlan", record.TenantSlug,
            note: d.ReasonAr,
            ip: http.Connection.RemoteIpAddress?.ToString(),
            after: $"expiresAt={d.NewExpiresAt:yyyy-MM-dd}; tx={record.Id}; event={e.EventId}");

    /// <summary>حُقولُ نَموذَجِ رابِطِ الدَفع — <b>والمَتجَرُ والباقَةُ
    /// ومُمَيِّزُ الدَورَةِ مِن الخادِمِ لا مِن النَموذَج</b>. ولَو
    /// قُرِئَ أَحَدُها مِن الطَلَبِ لَاختارَ مُتَصَفِّحٌ أَيَّ مِنها
    /// شاء.</summary>
    public static PaddleTransactionDraft DraftFrom(HttpRequest req, string slug, TenantPlan? plan)
        => PaddleTransactionPolicy.ReadDraft(
            slug, plan?.PlanId,
            req.Form["amount"], req.Form["currency"], req.Form["days"], req.Form["description"],
            PaddleTransactionPolicy.CycleOf(plan));

    /// <summary>وَثيقَةُ المُعامَلَةِ المُعَلَّقَةِ مِن المُسَوَّدَةِ
    /// ومِمّا رَدَّتهُ Paddle — <b>دالَّةٌ نَقِيَّة، والوَقتُ يُمَرَّرُ
    /// ولا يُقرَأُ مِن الساعَة</b>.</summary>
    public static PaddleTransactionRecord RecordFor(
        PaddleTransactionDraft draft, string reference, PaddleTransactionResult result,
        string? checkoutUrl, string by, DateTime at)
        => new()
        {
            Id            = reference,
            TenantSlug    = draft.NormalizedSlug,
            PlanId        = draft.PlanId,
            Amount        = draft.Amount,
            AmountMinor   = draft.MinorAmount,
            Currency      = draft.NormalizedCurrency,
            Days          = draft.Days,
            Description   = draft.TrimmedDescription,
            TransactionId = result.TransactionId,
            CheckoutUrl   = checkoutUrl,
            Status        = PaddleTransactionStatuses.Created,
            ProviderStatus = result.Status,
            CreatedBy     = by,
            CreatedAt     = at,
            At            = at,
        };

    /// <summary>فَشَلُ نُقطَةِ نَموذَجٍ في <c>/admin</c> — <b>تَحويلٌ
    /// لا JSON</b>، ونَفسُ صيغَةِ
    /// <see cref="PayPalSurface.LinkFailed"/> حَرفاً فَيَقرَأُ الرَمزَ
    /// نَفسُ <c>FormatError</c> في الشاشَة.</summary>
    public static IResult Failed(string slug, string code)
        => PayPalSurface.LinkFailed(slug, code);
}
