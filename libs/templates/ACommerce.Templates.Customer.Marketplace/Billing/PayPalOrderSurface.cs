using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>مُهايِئُ HTTP لِمَسارِ الطَلَبات</b> — يُحَوِّل النَموذَجَ
/// إلى مُسَوَّدَة، والمُضيفَ إلى أَصلٍ يُبنى مِنه رابِطا العَودَةِ
/// والإلغاء، والقَرارَ النَقِيَّ إلى رَدٍّ وسَطرِ لوغ.</para>
///
/// <para><b>ونَفسُ حُجَّةِ <see cref="PayPalSurface"/> حَرفاً</b>:
/// <c>Services/Subscriptions</c> مَفروضٌ عَلَيه <b>صِفرُ مَعرِفَةٍ
/// بِـHTTP</b>، فَما يَعرِف <c>HttpRequest</c> و<c>IResult</c> يَسكُن
/// هُنا مَعَ النُقطَة.</para>
/// </summary>
public static class PayPalOrderSurface
{
    // ─── رُموزُ الرَدّ — مَعجَمٌ مُغلَقٌ لا سَلاسِلُ مَنثورَة ───────
    public const string OrderRefused   = "paypal_order_failed";
    public const string OrderNotFound  = "paypal_order_not_found";
    public const string CaptureRefused = "paypal_capture_failed";

    /// <summary>اسمُ حَقلِ المَرجِعِ في نَموذَجِ الالتِقاطِ اليَدَويّ —
    /// <b>مَوضِعٌ واحِدٌ</b> تَقرَؤُه الشاشَةُ والنُقطَةُ
    /// والاختِبار.</summary>
    public const string ReferenceField = "reference";

    /// <summary>
    /// <para><b>أَصلُ العُنوانِ مِن الطَلَبِ نَفسِه</b> —
    /// <c>{المُخَطَّط}://{المُضيف}</c>. ولا يُقرَأُ مِن إعدادٍ: مُضيفٌ
    /// مَكتوبٌ بِاليَد في مُتَغَيِّرٍ يَنجَرِف عَن المُضيفِ الَّذي
    /// يَفتَحُه المُشرِفُ فِعلاً، <b>ورابِطُ عَودَةٍ إلى مُضيفٍ خاطِئٍ
    /// يَترُك الدافِعَ على صَفحَةِ عَطَبٍ بَعدَ أَن يَدفَع</b>.</para>
    /// </summary>
    public static string OriginFrom(HttpRequest req)
        => $"{req.Scheme}://{req.Host.Value}";

    /// <summary>حُقولُ نَموذَجِ رابِطِ الدَفع — <b>والمَتجَرُ والباقَةُ
    /// مِن الخادِمِ لا مِن النَموذَج</b>.</summary>
    public static PayPalOrderDraft DraftFrom(HttpRequest req, string slug, TenantPlan? plan)
        => PayPalOrderPolicy.ReadDraft(
            slug, plan?.PlanId,
            req.Form["amount"], req.Form["currency"], req.Form["days"], req.Form["description"]);

    /// <summary>وَثيقَةُ الدَفعِ المُعَلَّق مِن المُسَوَّدَةِ ومِمّا
    /// رَدَّتهُ PayPal — <b>دالَّةٌ نَقِيَّة، والوَقتُ يُمَرَّرُ ولا
    /// يُقرَأُ مِن الساعَة</b>.</summary>
    public static PayPalOrderRecord RecordFor(
        PayPalOrderDraft draft, string reference, PayPalOrderResult result,
        string by, DateTime at)
        => new()
        {
            Id          = reference,
            TenantSlug  = draft.NormalizedSlug,
            PlanId      = draft.PlanId,
            Amount      = draft.Amount,
            Currency    = draft.NormalizedCurrency,
            Days        = draft.Days,
            Description = draft.TrimmedDescription,
            OrderId     = result.OrderId,
            ApproveUrl  = result.ApproveUrl,
            Status      = PayPalOrderStatuses.Created,
            CreatedBy   = by,
            CreatedAt   = at,
            At          = at,
        };

    /// <summary>
    /// <para><b>قَرارٌ بِلا كِتابَة — ويُقالُ سَبَبُه.</b> و‏200 لا ‏4xx:
    /// الرِسالَةُ وَصَلَت صَحيحَةً وفُهِمَت، وقَرارُنا أَلّا نَفعَل.
    /// ورَدُّ خَطَإٍ هُنا يَجعَل PayPal تُعيدُها «‏up to 25 times over
    /// the course of 3 days».</para>
    ///
    /// <para><b>وثَلاثَةُ فُروعٍ تُصَعَّدُ إلى <c>Error</c>، والباقي
    /// خَبَر</b>: مَرجِعٌ مَجهول (مالٌ وَصَلَ ولا يُعرَف لِمَن)، ومَبلَغٌ
    /// لا يُطابِق (دَفعٌ ناقِصٌ أَو مُعامَلَةٌ لَيسَت لَنا)، وحالَةٌ
    /// تُناقِض اسمَ الحَدَث. وسِجِلٌّ يَصرُخ عِندَ كُلّ شَيءٍ لا
    /// يُقرَأ.</para>
    /// </summary>
    public static IResult NoWrite(ILogger log, PayPalOrderEvent e, PayPalOrderDecision d)
    {
        if (d.Action is PayPalOrderAction.UnknownReference
                     or PayPalOrderAction.AmountMismatch
                     or PayPalOrderAction.StatusNotCompleted
                     or PayPalOrderAction.UnknownTenant)
            log.LogError("[PayPal] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);
        else
            log.LogInformation("[PayPal] {Action} — الحَدَث {EventId} ({Type}): {Reason}",
                d.Action, e.EventId, e.EventType, d.ReasonAr);

        return Results.Ok(new { action = d.Action.ToString(), applied = false });
    }

    public static IResult Applied(ILogger log, PayPalOrderEvent e, PayPalOrderDecision d, string slug)
    {
        if (d.TouchesPlan)
            log.LogInformation("[PayPal] {Action} — {Slug} حَتّى {Expires:yyyy-MM-dd}: {Reason}",
                d.Action, slug, d.NewExpiresAt, d.ReasonAr);
        else
            log.LogInformation("[PayPal] {Action} — {Slug} ({Status}): {Reason}",
                d.Action, slug, d.OrderStatus, d.ReasonAr);

        return Results.Ok(new { action = d.Action.ToString(), applied = true });
    }

    /// <summary>سَطرُ تَدقيقٍ لِكُلّ حَرَكَةِ مال — «لا قَرارَ إداريٌّ
    /// بِلا أَثَر»، وتَحريكُ تاريخِ انتِهاءِ مَتجَرٍ قَرارٌ إداريٌّ ولَو
    /// كانَ فاعِلُه آلَة. نَفسُ شَكلِ
    /// <see cref="PayPalSurface.AuditAsync"/> حَرفاً.</summary>
    public static Task AuditAsync(
        Services.Audit.AuditWriter audit, PayPalOrderEvent e,
        PayPalOrderRecord order, PayPalOrderDecision d, HttpContext http)
        => audit.WriteAsync(
            string.IsNullOrWhiteSpace(order.TenantSlug)
                ? Services.Subscriptions.PayPalBillingService.UnknownTenantScope
                : order.TenantSlug,
            actorId: null, actorName: $"paypal · {e.EventType}",
            ActionFor(d.Action),
            "TenantPlan", order.TenantSlug,
            note: d.ReasonAr,
            ip: http.Connection.RemoteIpAddress?.ToString(),
            after: $"expiresAt={d.NewExpiresAt:yyyy-MM-dd}; order={order.Id}; event={e.EventId}");

    /// <summary>فِعلُ التَدقيقِ المُقابِل — <b>يُقرَأُ مِن الخِدمَةِ ولا
    /// يُنسَخ</b>، فَلا يَنجَرِف اسمُ فِعلٍ بَينَ مَوضِعَين.</summary>
    public static string ActionFor(PayPalOrderAction action) => action switch
    {
        PayPalOrderAction.Extend
            => Services.Subscriptions.PayPalBillingService.ExtendAuditAction,
        PayPalOrderAction.Withdraw
            => Services.Subscriptions.PayPalBillingService.WithdrawAuditAction,
        _   => Services.Subscriptions.PayPalBillingService.CaptureAuditAction
    };

    /// <summary>فَشَلُ نُقطَةِ نَموذَجٍ في <c>/admin</c> — <b>تَحويلٌ لا
    /// JSON</b>، ونَفسُ صيغَةِ <see cref="PayPalSurface.LinkFailed"/>
    /// حَرفاً فَيَقرَأُ الرَمزَ نَفسُ <c>FormatError</c> في
    /// الشاشَة.</summary>
    public static IResult Failed(string slug, string code)
        => PayPalSurface.LinkFailed(slug, code);
}

/// <summary>
/// <para><b>قُفلٌ تَسَلسُلِيٌّ لِكُلّ طَلَبٍ على حِدَة — ولِماذا هُوَ
/// لازِمٌ لا احتِياطيّ.</b> ‏PayPal تُعيد إرسالَ الحَدَثِ نَفسِه «‏up to
/// 25 times over the course of 3 days» على أَيّ رَدٍّ غَيرِ ‏2xx،
/// ونِداءانِ <b>مُتَزامِنانِ</b> لِـ<c>/capture</c> بِنَفسِ
/// <c>PayPal-Request-Id</c> — بِنَصِّ PayPal — «‏processes the first …
/// <b>might fail the second</b>».</para>
///
/// <para><b>ومَداهُ العَمَلِيَّةُ الواحِدَةُ ويُقالُ بِصَراحَة</b>: هذا
/// قُفلٌ داخِلَ النُسخَة، لا قُفلٌ مُوَزَّع. وهُوَ يَكفي لِنُسخَةٍ
/// واحِدَةٍ في الـSpace، <b>ولا يَكفي لِنُسخَتَين</b> — والحاجِزُ الثاني
/// قائِمٌ تَحتَه على أَيّ حال: <c>PayPal-Request-Id</c> عِندَ PayPal
/// نَفسِها، ومِفتاحُ <c>event_id</c> في وَثيقَةِ مَرَّة-واحِدَة الَّتي
/// تُدرَج بِـ<c>Insert</c> فَتَرتَدُّ مِن Postgres.</para>
/// </summary>
public static class PayPalOrderLocks
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>
        Gates = new(StringComparer.Ordinal);

    public static SemaphoreSlim For(string? orderId)
        => Gates.GetOrAdd((orderId ?? "").Trim(), _ => new SemaphoreSlim(1, 1));
}
