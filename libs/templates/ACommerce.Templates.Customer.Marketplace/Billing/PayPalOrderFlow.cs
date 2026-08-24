using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>تَركيبُ مَسارِ الطَلَبات — ولِماذا هُنا لا في جِسمِ
/// النُقطَة.</b> الخُطُواتُ <b>مُتَرابِطَةٌ بِمُخرَجِ الأولى مُدخَلاً
/// لِلثانِيَة</b> (وَثيقَةٌ تُحَمَّل ← قَرارٌ نَقيّ ← أَثَرٌ ← إيداعٌ ←
/// تَدقيق)، وتَركُ وَصلِها لِلنُقطَةِ يَجعَل التَرتيبَ سَطراً في جِسمٍ
/// يُنسى. نَفسُ حُجَّةِ <c>PayPalGateway.CreateCatalogPlanAsync</c>
/// حَرفاً.</para>
///
/// <para><b>ولا قَرارَ واحِدٌ هُنا</b>: كُلُّ الحُكمِ في
/// <see cref="PayPalOrderBillingPolicy"/> (دَوالُّ نَقِيَّةٌ يُمَرَّرُ
/// إلَيها الوَقت)، وكُلُّ الأَثَرِ في
/// <see cref="Services.Subscriptions.PayPalBillingService"/> (تَأخُذُ
/// الجَلسَةَ ولا تَملِكُها). فَما هُنا تَحميلٌ وتَرتيبٌ ورَدّ.</para>
/// </summary>
public static class PayPalOrderFlow
{
    /// <summary>
    /// <para><b>حَدَثُ طَلَبٍ مُوَثَّقٌ — يُقرَأُ، يُقَرَّرُ، يُطَبَّق.</b>
    /// والبَوّابَةُ مَرَّت قَبلَ أَن يَصِلَ هذا الجِسمُ إلى هُنا:
    /// التَوقيعُ يُتَحَقَّقُ مِنه <b>قَبلَ</b> أَن يُقرَأَ الجِسمُ
    /// كَبَيانات (‏<c>PayPalBillingPolicy.Gate</c>).</para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        PayPalOrderEvent e, IDocumentSession session, PayPalGateway paypal,
        Services.Audit.AuditWriter audit, ILogger log, HttpContext http)
    {
        var ct  = http.RequestAborted;
        var now = DateTime.UtcNow;

        var seen  = await session.LoadAsync<PayPalWebhookRecord>(e.EventId, ct);
        var order = await FindOrderAsync(session, e, ct);
        var plan  = order is null
            ? null
            : await session.LoadAsync<TenantPlan>(order.TenantSlug, ct);

        var decision = PayPalOrderBillingPolicy.Decide(e, order, plan, seen is not null, now);

        // ─── مُوافَقَةٌ لا مال: يُنادى الالتِقاط، ولا يُمَدَّدُ شَيء ───
        if (decision.Action == PayPalOrderAction.Capture && order is not null)
            return await CaptureAsync(order!, session, paypal, log, decision, ct);

        if (!Services.Subscriptions.PayPalBillingService.ApplyOrder(
                session, plan, order, e, decision, now))
            return PayPalOrderSurface.NoWrite(log, e, decision);

        await session.SaveChangesAsync(ct);
        await PayPalOrderSurface.AuditAsync(audit, e, order!, decision, http);
        return PayPalOrderSurface.Applied(log, e, decision, order!.TenantSlug);
    }

    /// <summary>
    /// <para><b>الالتِقاطُ يُنادى مِن الحَدَثِ وَحدَه — لا مِن صَفحَةِ
    /// العَودَة.</b> ومِفتاحُ مَرَّة-واحِدَةٍ عِندَنا <c>event_id</c> لا
    /// <c>capture_id</c>، فَلَو مَدَّدَت صَفحَةُ العَودَةِ أَيضاً لَكانَ
    /// مِفتاحُها مُختَلِفاً <b>لا يَرتَطِمُ بِالأَوَّل — فَيَقَع
    /// تَمديدانِ لِدَفعَةٍ واحِدَة</b>. والسِباقُ قائِمٌ لا مُحتَمَل:
    /// وَثيقَةُ ‏EPS الرَسمِيَّةُ تُظهِر حَدَثَ الالتِقاطِ في الخُطوَةِ
    /// ‏5 وعَودَةَ المُشتَري في الخُطوَةِ ‏6.</para>
    ///
    /// <para><b>ولا يُسَجَّلُ مِفتاحُ مَرَّة-واحِدَةٍ لِهذا الحَدَث
    /// عَمداً</b>: لَو سُجِّلَ لَصارَت إعادَةُ الإرسالِ «تَكراراً»
    /// فَتَتَخَطّى الالتِقاطَ — <b>وطَلَبٌ فَشِلَ التِقاطُه مَرَّةً
    /// يُلغى بَعدَ نافِذَتِه ويُعادُ المالُ</b>. والحاجِزُ ضِدَّ
    /// الالتِقاطِ المُكَرَّرِ هُوَ <c>PayPal-Request-Id</c> الثابِتُ
    /// عِندَ PayPal نَفسِها، لا سِجِلٌّ عِندَنا.</para>
    /// </summary>
    private static async Task<IResult> CaptureAsync(
        PayPalOrderRecord order, IDocumentSession session, PayPalGateway paypal,
        ILogger log, PayPalOrderDecision decision, CancellationToken ct)
    {
        var gate = PayPalOrderLocks.For(order.OrderId);
        await gate.WaitAsync(ct);
        try
        {
            var result = await paypal.CaptureOrderAsync(
                order.OrderId, PayPalOrderPolicy.CaptureRequestId(order.Id), ct);

            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                // **يُقالُ بِصَوت**: طَلَبٌ وافَقَ عَلَيه الدافِعُ ولَم
                // يُلتَقَط تُلغيه PayPal وتُعيدُ المالَ بَعدَ نافِذَتِه.
                // وعِلاجُه بِنَقرَة: زِرُّ «التَقِط الآن» في
                // ‏/admin/tenants/{slug}/plan.
                log.LogError("[PayPal] فَشِل الالتِقاط لِلطَلَب {Order}: {Reason}",
                    order.OrderId, result.FailureReason);
            }
            else if (result.CaptureId is { Length: > 0 })
            {
                order.CaptureId = result.CaptureId;
            }

            // الحالَةُ تَبقى «وافَقَ» حَتّى تَصِلَ رِسالَةُ
            // `PAYMENT.CAPTURE.COMPLETED` — **نَجاحُ النِداءِ لا
            // يُمَدِّد ولا يُعلِن وُصولَ المال**.
            order.Status = PayPalOrderStatuses.Approved;
            order.At     = DateTime.UtcNow;
            session.Store(order);
            await session.SaveChangesAsync(ct);

            log.LogInformation("[PayPal] Capture — الطَلَب {Order} ({Status}): {Reason}",
                order.OrderId, result.Status, decision.ReasonAr);

            return Results.Ok(new { action = nameof(PayPalOrderAction.Capture), applied = true });
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// <para><b>ثَلاثَةُ مَفاتيحَ نازِلَة، ولِكُلٍّ سَبَبُ وُجودِه.</b></para>
    /// <list type="number">
    ///   <item><b>مَرجِعُنا</b> (<c>custom_id</c>) — المِفتاحُ
    ///   الأَوَّليُّ لِلوَثيقَة، فَتَحميلٌ مُباشِرٌ بِلا استِعلام.</item>
    ///   <item><b>مُعَرِّفُ الطَلَب</b> — لِأَنَّه <b>لَم توجَد جُملَةٌ
    ///   رَسمِيَّةٌ تَنُصُّ على انتِقالِ <c>custom_id</c> إلى مَورِدِ
    ///   الالتِقاط</b>، وعَيِّنَتا <c>COMPLETED</c> الرَسمِيَّتانِ
    ///   تَخلُوانِ مِنه. فَـ<c>supplementary_data.related_ids.order_id</c>
    ///   هُوَ الجِسر.</item>
    ///   <item><b>مُعَرِّفُ الالتِقاطِ مِن <c>links[rel=up]</c></b> —
    ///   وهُوَ المِفتاحُ <b>الوَحيدُ</b> في الاسترداد والعَكس: مَورِدُهُما
    ///   كائِنُ Refund، وفي <c>REVERSED</c> تَكون PayPal هي البادِئَة
    ///   فَلَم نُرسِل <c>custom_id</c> قَطّ.</item>
    /// </list>
    /// </summary>
    public static async Task<PayPalOrderRecord?> FindOrderAsync(
        IQuerySession session, PayPalOrderEvent e, CancellationToken ct = default)
    {
        if (e.Reference is { Length: > 0 } reference)
        {
            var byRef = await session.LoadAsync<PayPalOrderRecord>(reference, ct);
            if (byRef is not null) return byRef;
        }

        if (e.OrderId is { Length: > 0 } orderId)
        {
            var byOrder = await session.Query<PayPalOrderRecord>()
                .Where(o => o.OrderId == orderId).FirstOrDefaultAsync(ct);
            if (byOrder is not null) return byOrder;
        }

        var capture = e.UpCaptureId ?? e.CaptureId;
        if (capture is { Length: > 0 })
            return await session.Query<PayPalOrderRecord>()
                .Where(o => o.CaptureId == capture).FirstOrDefaultAsync(ct);

        return null;
    }
}
