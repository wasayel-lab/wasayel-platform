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

    /// <summary><b>وَثيقَةُ طَلَبٍ قائِمَةٌ تَجاوَزَت انتِظارَ
    /// الالتِقاط</b> — لا يُكتَبُ فَوقَها. ورَفضٌ مُسَمّىً بِالعَرَبِيَّةِ
    /// خَيرٌ مِن دَهسٍ صامِتٍ يَمحو <c>CaptureId</c> فَلا يَجِدُ
    /// الاستِردادُ ما يَربِطُه.</summary>
    public const string OrderSettled = "paypal_order_settled";

    /// <summary><b>طَلَبٌ لَم يَعُد يَقبَلُ التِقاطاً</b> — التُقِطَ
    /// سَلَفاً أَو بَلَغَ حالَةً نِهائِيَّة. ويُقالُ مِن عِندِنا
    /// بِالعَرَبِيَّة، <b>لا يُترَكُ لِـPayPal تَرُدُّه
    /// <c>ORDER_ALREADY_CAPTURED</c> إنجِليزِيّاً خامّاً</b>.</summary>
    public const string CaptureNotAllowed = "paypal_capture_not_allowed";

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
    /// ومُمَيِّزُ الدَورَةِ مِن الخادِمِ لا مِن النَموذَج</b>. ولَو
    /// قُرِئَ أَحَدُها مِن الطَلَبِ لَاختارَ مُتَصَفِّحٌ أَيَّ مِنها
    /// شاءَ — ومُمَيِّزُ الدَورَةِ يُحَدِّد مِفتاحَ الوَثيقَة.</summary>
    public static PayPalOrderDraft DraftFrom(HttpRequest req, string slug, TenantPlan? plan)
        => PayPalOrderPolicy.ReadDraft(
            slug, plan?.PlanId,
            req.Form["amount"], req.Form["currency"], req.Form["days"], req.Form["description"],
            PayPalOrderPolicy.CycleOf(plan));

    /// <summary>وَثيقَةُ الدَفعِ المُعَلَّق مِن المُسَوَّدَةِ ومِمّا
    /// رَدَّتهُ PayPal — <b>دالَّةٌ نَقِيَّة، والوَقتُ يُمَرَّرُ ولا
    /// يُقرَأُ مِن الساعَة</b>.
    ///
    /// <para><b>والمَبلَغُ المَحفوظُ هُوَ المَصوغُ لا الخام</b>
    /// (<see cref="PayPalOrderDraft.NormalizedAmount"/>): ما يُخَزَّنُ
    /// هُوَ بِعَينِه ما يُرسَل، فَتُقارِنُ <c>MoneyMatches</c>
    /// تَعريفاً واحِداً لا اثنَين.</para></summary>
    public static PayPalOrderRecord RecordFor(
        PayPalOrderDraft draft, string reference, PayPalOrderResult result,
        string by, DateTime at)
        => new()
        {
            Id          = reference,
            TenantSlug  = draft.NormalizedSlug,
            PlanId      = draft.PlanId,
            Amount      = draft.NormalizedAmount,
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
    /// <para><b>أَيَشفي تَكرارُ الرِسالَةِ هذا الفَرع؟</b> — وهُوَ
    /// السُؤالُ الَّذي يُحَدِّد رَمزَ الرَدّ، لا «أَهُوَ خَطَأٌ أَم
    /// لا».</para>
    ///
    /// <para><b>فَرعانِ يَشفِيهِما التَكرار</b>: <c>UnknownReference</c>
    /// و<c>UnknownTenant</c>. كِلاهُما يَقول «<b>المالُ وَصَلَ ويَنقُصُنا
    /// نَحنُ وَثيقَة</b>» — وَثيقَةُ دَفعٍ يُنشِئُها المُشرِفُ، أَو
    /// وَثيقَةُ باقَةٍ يَضبُطُها مَرَّةً. وإعادَةُ PayPal «‏up to 25
    /// times over the course of 3 days» هي بِالضَبطِ النافِذَةُ الَّتي
    /// يَتَّسِعُ لَها ذلك: يُصلِحُ المُشرِفُ النَقصَ فَتَصِلُ رِسالَةٌ
    /// لاحِقَةٌ <b>فَتُطَبَّق مِن تِلقاءِ نَفسِها</b>. ورَدُّ ‏200
    /// هُنا <b>يُلغي تِلكَ الشَبَكَةَ كُلَّها</b>: يُقالُ لِPayPal
    /// «فُهِمَت وطُبِّقَت» وهي لَم تُطَبَّق، فَلا يَبقى إلّا لوغٌ
    /// يُقرَأُ أَو لا يُقرَأ.</para>
    ///
    /// <para><b>وفَرعانِ لا يَشفيهِما</b>: <c>AmountMismatch</c> —
    /// المَبلَغانِ لا يَتَحَرَّكُ أَحَدُهُما (لا شاشَةَ تُعَدِّلُ مَبلَغَ
    /// طَلَبٍ قائِم)، فَالإعادَةُ تُعيدُ المُقارَنَةَ نَفسَها ‏25
    /// مَرَّة؛ و<c>StatusNotCompleted</c> — الحالَةُ لَقطَةٌ **داخِلَ
    /// الجِسمِ المُعاد**، فَكُلُّ إعادَةٍ تَحمِلُها كَما هي. وهُما
    /// يَبقَيانِ ‏200 بِسَطرِ خَطَإٍ صارِخ: <b>ضَجيجٌ لِثَلاثَةِ
    /// أَيّامٍ لا يَشتَري شَيئاً</b>.</para>
    ///
    /// <para><b>و‏503 لا ‏500</b>: «مَفهومَةٌ ولا تُطَبَّقُ بَعد» لا
    /// «انفَجَرنا». والفَرقُ يُقرَأُ في لَوحَةِ PayPal ويُقرَأُ
    /// عِندَنا.</para>
    /// </summary>
    public static bool HealsOnRedelivery(PayPalOrderAction action)
        => action is PayPalOrderAction.UnknownReference or PayPalOrderAction.UnknownTenant;

    /// <summary>
    /// <para><b>قَرارٌ بِلا كِتابَة — ويُقالُ سَبَبُه.</b> والرَمزُ
    /// يَنقَسِم بِـ<see cref="HealsOnRedelivery"/>: ما تَشفيهِ
    /// الإعادَةُ يُرَدُّ بِـ‏503 فَتُعيدُه PayPal، وما لا تَشفيهِ
    /// يُرَدُّ بِـ‏200 لِأَنّ الرِسالَةَ فُهِمَت وقَرارُنا أَلّا
    /// نَفعَل.</para>
    ///
    /// <para><b>وأَربَعَةُ فُروعٍ تُصَعَّدُ إلى <c>Error</c>، والباقي
    /// خَبَر</b>: مَرجِعٌ مَجهول (مالٌ وَصَلَ ولا يُعرَف لِمَن)، ومَبلَغٌ
    /// لا يُطابِق (دَفعٌ ناقِصٌ أَو مُعامَلَةٌ لَيسَت لَنا)، وحالَةٌ
    /// تُناقِض اسمَ الحَدَث، ومَتجَرٌ بِلا وَثيقَةِ باقَة. وسِجِلٌّ
    /// يَصرُخ عِندَ كُلّ شَيءٍ لا يُقرَأ.</para>
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

        var body = new { action = d.Action.ToString(), applied = false };
        return HealsOnRedelivery(d.Action)
            ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(body);
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
///
/// <para><b>وعَدَدُ الأَقفالِ ثابِتٌ لا يَنمو — والكُلفَةُ الَّتي
/// غَيَّرَتهُ</b>: كانَ قامُوساً مُتَزامِناً <c>مَرجِع ← سيمافور</c>
/// <b>لا يُفَرَّغُ أَبَداً</b>. فَكُلُّ طَلَبِ دَفعٍ في عُمرِ
/// العَمَلِيَّةِ يَترُك خَلفَه سيمافوراً حَيّاً، والمَراجِعُ حَتمِيَّةٌ
/// لا مُعادَة — أَي <b>نُمُوٌّ بِلا سَقفٍ في عَمَلِيَّةٍ طَويلَةِ
/// العُمر</b>. والبَديلُ لَيسَ حَذفاً بَعدَ الإطلاق (سِباقٌ: مُنتَظِرٌ
/// يَأخُذُ سيمافوراً حُذِفَ مِن القامُوسِ فَيَقَعُ نِداءانِ مَعاً)، بَل
/// <b>مَصفوفَةٌ ثابِتَةٌ يُوَزَّعُ عَلَيها المَرجِعُ بِبَصمَتِه</b>:
/// ذاكِرَةٌ مَحدودَةٌ مَعروفَةٌ سَلَفاً، وتَسَلسُلٌ مَحفوظٌ لِكُلِّ
/// مَرجِعٍ مَعَ نَفسِه. <b>والثَمَنُ يُقال</b>: مَرجِعانِ يَتَشارَكانِ
/// خانَةً يَتَسَلسَلانِ بِلا حاجَة — وذاكَ انتِظارُ نِداءٍ واحِدٍ
/// قَصير، لا عُطل.</para>
/// </summary>
public static class PayPalOrderLocks
{
    /// <summary>عَدَدُ الخانات — قُوَّةُ اثنَين، وأَكبَرُ بِكَثيرٍ مِن
    /// أَيِّ تَزامُنٍ مُتَوَقَّعٍ على نُقطَةِ رِسالَةٍ واحِدَة.</summary>
    public const int Stripes = 64;

    private static readonly SemaphoreSlim[] Gates = Create();

    private static SemaphoreSlim[] Create()
    {
        var gates = new SemaphoreSlim[Stripes];
        for (var i = 0; i < Stripes; i++) gates[i] = new SemaphoreSlim(1, 1);
        return gates;
    }

    /// <summary>
    /// <para><b>خانَةُ هذا المَرجِع.</b></para>
    ///
    /// <para><b>والتَوزيعُ بِبَصمَةِ المُستَودَعِ لا
    /// بِـ<c>string.GetHashCode</c></b>، و<c>StableHashTests</c>
    /// يُحمِرُّ على الثانِيَة: بَذرَتُها تَتَبَدَّلُ مَعَ كُلِّ
    /// عَمَلِيَّة. <b>والاستِثناءُ هُنا كانَ مُمكِناً نَظَرِيّاً</b> —
    /// قُفلٌ مَداهُ العَمَلِيَّةُ لا يَحتاج ثَباتاً عَبرَها — <b>ولَم
    /// يُؤخَذ</b>: قاعِدَةٌ لَها استِثناءٌ مَشروحٌ في تَعليقٍ تُعاد
    /// قِراءَتُها بَعدَ شَهرَينِ بِلا التَعليق، ونِداءُ بَصمَةٍ
    /// واحِدٌ لِكُلِّ التِقاطٍ ثَمَنٌ لا يُقاس (القاعِدَة ٢).</para>
    /// </summary>
    public static SemaphoreSlim For(string? reference)
    {
        var print = PayPalCatalogPolicy.Fingerprint((reference ?? "").Trim());
        return Gates[Convert.ToUInt32(print[..8], 16) % Stripes];
    }
}
