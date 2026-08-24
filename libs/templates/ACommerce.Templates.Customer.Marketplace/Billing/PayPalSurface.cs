using ACommerce.Kit.Payments.Providers.PayPal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>مُهايِئُ HTTP لِنُقطَتَي PayPal</b> — يُحَوِّل الرُؤوسَ إلى
/// نَوعٍ مَكتوب، والقَرارَ النَقِيَّ إلى رَدٍّ وسَطرِ لوغ.</para>
///
/// <para><b>ولِماذا مُهايِئٌ ومُجَلَّدُ الخِدماتِ بِلا واحِد</b>:
/// <c>Services/Subscriptions</c> مَفروضٌ عَلَيه <b>صِفرُ مَعرِفَةٍ
/// بِـHTTP</b> (<c>TenantConfigServiceShapeTests</c>). فَما يَعرِف
/// <c>HttpRequest</c> و<c>IResult</c> يَسكُن هُنا مَعَ النُقطَة، ولا
/// يَتَسَلَّل إلى الخِدمَة — وهذا هُوَ نَفسُ الحَدِّ الَّذي يَفصِل
/// <c>TenantConfigSurface</c> عَن خِدماتِه.</para>
/// </summary>
public static class PayPalSurface
{
    // ─── رُموزُ الرَدّ — مَعجَمٌ مُغلَقٌ لا سَلاسِلُ مَنثورَة ───────
    public const string UnreadableCode = "paypal_event_unreadable";
    public const string LinkUnavailable = "paypal_unavailable";
    public const string LinkRefused     = "paypal_link_failed";

    /// <summary>الرُؤوسُ الخَمسَة مِن الطَلَب — الغائِبُ سِلسِلَةٌ
    /// فارِغَة، و<see cref="PayPalWebhookHeaders.IsComplete"/> هي
    /// الَّتي تَحكُم. فَلا <c>null</c> يَتَسَلَّل إلى دالَّةِ
    /// القَرار.</summary>
    public static PayPalWebhookHeaders HeadersFrom(HttpRequest req) => new(
        req.Headers[PayPalWebhookHeaders.TransmissionIdHeader].ToString(),
        req.Headers[PayPalWebhookHeaders.TransmissionTimeHeader].ToString(),
        req.Headers[PayPalWebhookHeaders.CertUrlHeader].ToString(),
        req.Headers[PayPalWebhookHeaders.AuthAlgoHeader].ToString(),
        req.Headers[PayPalWebhookHeaders.TransmissionSigHeader].ToString());

    /// <summary>
    /// <para>رَفضُ البَوّابَة — <b>‏400 لا ‏500، وبِلا تَحويلٍ ولا
    /// HTML</b>: المُنادي آلَةٌ لا مُتَصَفِّح (نَفسُ حُجَّةِ
    /// الطَبَقَة ١٠).</para>
    ///
    /// <para><b>و‏400 لا ‏401 عَمداً</b>: ‏PayPal تُعيد الإرسالَ عِندَ
    /// ‏5xx وتَتَوَقَّف عِندَ ‏4xx. ورِسالَةٌ بِتَوقيعٍ فاسِدٍ لا
    /// تُريدُ إعادَتَها — إعادَتُها آلافُ المُحاوَلاتِ على بابٍ
    /// مُغلَق.</para>
    /// </summary>
    public static IResult Rejected(ILogger log, PayPalWebhookGate gate)
    {
        var code = PayPalBillingPolicy.GateCode(gate);
        // يُقالُ في اللوغ بِاسمِه: «لا مُعَرِّفَ Webhook» غَيرُ
        // «تَوقيعٌ فاشِل»، وخَلطُهُما يُرسِل المالِكَ يُفَتِّشُ عَن
        // سِرٍّ خاطِئٍ ومُشكِلَتُه سِرٌّ غائِب.
        log.LogWarning("[PayPal] رِسالَةٌ مَرفوضَة: {Code}", code);
        return Results.BadRequest(new { error = code });
    }

    public static IResult Unreadable(ILogger log)
    {
        log.LogWarning("[PayPal] جِسمٌ مُوَثَّقٌ لكِنّ غَيرُ مَقروء — لا id/event_type.");
        return Results.BadRequest(new { error = UnreadableCode });
    }

    /// <summary>
    /// <para><b>قَرارٌ بِلا كِتابَة — ويُقالُ سَبَبُه</b>. و‏200 لا
    /// ‏4xx: الرِسالَةُ وَصَلَت صَحيحَةً وفُهِمَت، وقَرارُنا أَلّا
    /// نَفعَل. ورَدُّ خَطَإٍ هُنا يَجعَل PayPal تُعيدُها إلى الأَبَد
    /// على حَدَثٍ لا يَعنينا.</para>
    ///
    /// <para><b>والسَطرُ هُوَ المُنتَج</b> في حالَةِ
    /// <c>UnknownTenant</c>: مالٌ وَصَلَ وَسايِل ولا يُعرَف
    /// لِمَن — <b>يُقالُ بِصَوتٍ ولا يُبتلَع</b>، ويُسَوّى بِضَبطِ
    /// باقَةِ المَتجَرِ مِن <c>/admin</c> ثُمَّ إعادَةِ إرسالِ
    /// الرِسالَةِ مِن لَوحَةِ PayPal.</para>
    /// </summary>
    public static IResult NoWrite(ILogger log, PayPalWebhookEvent e, PayPalBillingDecision d)
    {
        if (d.Action == PayPalBillingAction.UnknownTenant)
            log.LogError("[PayPal] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);
        else
            log.LogInformation("[PayPal] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);

        return Results.Ok(new { action = d.Action.ToString(), applied = false });
    }

    public static IResult Applied(ILogger log, PayPalWebhookEvent e, PayPalBillingDecision d)
    {
        log.LogInformation("[PayPal] {Action} — {Slug} حَتّى {Expires:yyyy-MM-dd}: {Reason}",
            d.Action, e.TenantSlug, d.NewExpiresAt, d.ReasonAr);
        return Results.Ok(new { action = d.Action.ToString(), applied = true });
    }

    /// <summary>سَطرُ تَدقيقٍ لِكُلّ تَمديدٍ وكُلّ إيقافِ تَجديد —
    /// «لا قَرارَ إداريٌّ بِلا أَثَر»، وتَحريكُ تاريخِ انتِهاءِ مَتجَرٍ
    /// قَرارٌ إداريٌّ ولَو كانَ فاعِلُه آلَة.</summary>
    public static Task AuditAsync(
        Services.Audit.AuditWriter audit, PayPalWebhookEvent e,
        PayPalBillingDecision d, HttpContext http)
        => audit.WriteAsync(
            e.TenantSlug ?? Services.Subscriptions.PayPalBillingService.UnknownTenantScope,
            actorId: null, actorName: $"paypal · {e.EventType}",
            Services.Subscriptions.PayPalBillingService.AuditActionFor(d.Action),
            "TenantPlan", e.TenantSlug ?? "",
            note: d.ReasonAr,
            ip: http.Connection.RemoteIpAddress?.ToString(),
            after: $"expiresAt={d.NewExpiresAt:yyyy-MM-dd}; event={e.EventId}");

    /// <summary>مِفتاحُ مَرَّة-واحِدَة عِندَ PayPal
    /// (<c>PayPal-Request-Id</c>). يَحمِل السلاجَ والدَقيقَةَ:
    /// نَقرَتانِ مُتَتالِيَتانِ بِالخَطَإ لا تُنشِئانِ اشتِراكَين،
    /// ومُحاوَلَةٌ بَعدَ دَقيقَةٍ تُنشِئ واحِداً جَديداً حينَ يُرادُ
    /// ذلك فِعلاً.</summary>
    public static string LinkKey(string slug, DateTime now)
        => $"plan-link:{slug}:{now:yyyyMMddHHmm}";

    /// <summary>فَشَلُ إنشاءِ الرابِط — <b>تَحويلٌ لا JSON</b>: هذِه
    /// نُقطَةُ نَموذَجٍ في <c>/admin</c> يُنادِيها مُتَصَفِّحٌ، لا
    /// آلَة. (والطَبَقَةُ ١٠ تَمنَع التَحويلَ تَحتَ <c>/api/v1</c>
    /// وَحدَها، ولِهذا السَبَبِ بِعَينِه.)</summary>
    ///
    /// <para><b>والرَمزُ يُهرَّب</b>: مِنه ما هُوَ رِسالَةُ PayPal
    /// نَفسِها بِرَمزِها ونَصِّها (وفيها مَسافاتٌ وأَقواسٌ وعَرَبِيَّة)،
    /// ورِسالَةٌ غَيرُ مُهَرَّبَةٍ في مَسارٍ تُنتِج عُنواناً مَكسوراً
    /// فَتَضيع العِلَّةُ الَّتي كانَت تُقال. وتَهريبُ رَمزٍ ASCII
    /// (‏<c>paypal_unavailable</c>) يُعطي نَفسَه حَرفاً — فَلا يَتَغَيَّر
    /// سُلوكُ ما كانَ.</para>
    public static IResult LinkFailed(string slug, string code)
        => Results.Redirect($"/admin/tenants/{slug}/plan?err={Uri.EscapeDataString(code)}");
}
