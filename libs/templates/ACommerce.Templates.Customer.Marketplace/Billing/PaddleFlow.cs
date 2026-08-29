using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Subscriptions;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>تَركيبُ مَسارِ Paddle — ولِماذا هُنا لا في جِسمِ
/// النُقطَة.</b> الخُطُواتُ <b>مُتَرابِطَةٌ بِمُخرَجِ الأولى مُدخَلاً
/// لِلثانِيَة</b> (وَثيقَةٌ تُحَمَّل ← قَرارٌ نَقيّ ← أَثَرٌ ← إيداعٌ ←
/// تَدقيق)، وتَركُ وَصلِها لِلنُقطَةِ يَجعَل التَرتيبَ سَطراً في
/// جِسمٍ يُنسى. نَفسُ حُجَّةِ <see cref="PayPalOrderFlow"/>
/// حَرفاً.</para>
///
/// <para><b>ولا قَرارَ واحِدٌ هُنا</b>: كُلُّ الحُكمِ في
/// <see cref="PaddleBillingPolicy"/> (دَوالُّ نَقِيَّةٌ يُمَرَّرُ
/// إلَيها الوَقت)، وكُلُّ الأَثَرِ في
/// <see cref="Services.Subscriptions.PaddleBillingService"/> (تَأخُذُ
/// الجَلسَةَ ولا تَملِكُها). فَما هُنا تَحميلٌ وتَرتيبٌ ورَدّ.</para>
/// </summary>
public static class PaddleFlow
{
    /// <summary>
    /// <para><b>حَدَثٌ مُوَثَّقٌ — يُقرَأُ، يُقَرَّرُ، يُطَبَّق.</b>
    /// والبَوّابَةُ مَرَّت قَبلَ أَن يَصِلَ هذا الجِسمُ إلى
    /// هُنا.</para>
    ///
    /// <para><b>ولا نِداءَ خارِجاً في هذا المَسارِ إطلاقاً</b> —
    /// وهذا فَرقٌ عَمَليٌّ عَن مَسارِ PayPal: هُناك حَدَثُ
    /// «وافَقَ الدافِع» يُوجِبُ نِداءَ <c>/capture</c>، وهُنا
    /// <b>لا التِقاطَ أَصلاً</b>. فَالرِسالَةُ تُقرَأُ وتُطَبَّقُ
    /// وتُرَدّ، ولا شَبَكَةَ بَينَهُما.</para>
    /// </summary>
    public static async Task<IResult> HandleAsync(
        PaddleEvent e, IDocumentSession session,
        Services.Audit.AuditWriter audit, ILogger log, HttpContext http)
    {
        var ct  = http.RequestAborted;
        var now = DateTime.UtcNow;

        var seen   = await session.LoadAsync<PayPalWebhookRecord>(e.EventId, ct);
        var record = await FindTransactionAsync(session, e, ct);
        var plan   = record is null
            ? null
            : await session.LoadAsync<TenantPlan>(record.TenantSlug, ct);

        var decision = PaddleBillingPolicy.Decide(e, record, plan, seen is not null, now);

        if (!Services.Subscriptions.PaddleBillingService.ApplyTransaction(
                session, plan, record, e, decision, now))
            return PaddleSurface.NoWrite(log, e, decision);

        await session.SaveChangesAsync(ct);
        await PaddleSurface.AuditAsync(audit, e, record!, decision, http);
        return PaddleSurface.Applied(log, e, decision, record!.TenantSlug);
    }

    /// <summary>
    /// <para><b>مِفتاحانِ، ولِكُلٍّ سَبَبُ وُجودِه — والثاني
    /// <c>else</c> لا <c>fallback</c>.</b></para>
    /// <list type="number">
    ///   <item><b>مَرجِعُنا</b> (<c>data.custom_data</c>) — المِفتاحُ
    ///   الأَوَّليُّ لِلوَثيقَة، فَتَحميلٌ مُباشِرٌ بِلا
    ///   استِعلام.</item>
    ///   <item><b>مُعَرِّفُ المُعامَلَة</b> — <b>حينَ لا مَرجِعَ
    ///   إطلاقاً</b>، وذاكَ حالُ أَحداثِ التَسوِيَة: الاستِردادُ
    ///   كائِنٌ آخَرُ بِحُقولِه <b>لا يَحمِل <c>custom_data</c>
    ///   المُعامَلَة</b>، وجِسرُه الوَحيدُ
    ///   <c>data.transaction_id</c>. <b>وبِلا هذا السَطرِ لا يُسحَبُ
    ///   استِردادٌ أَبَداً</b> — المالُ يَعودُ والأَيّامُ تَبقى.</item>
    /// </list>
    ///
    /// <para><b>ولِماذا لا يُجَرَّبُ الثاني بَعدَ فَشَلِ الأَوَّل</b>:
    /// مَرجِعٌ **مَوجودٌ ولا وَثيقَةَ لَه** لَن تَجِدَ لَه المُعَرِّفُ
    /// وَثيقَةً أُخرى — نَحنُ مَن يَكتُبُ الاثنَينِ في وَثيقَةٍ
    /// واحِدَة. فَالاستِعلامُ حينَئِذٍ **رِحلَةُ قاعِدَةِ بَياناتٍ
    /// مَعروفَةُ النَتيجَة** على كُلِّ رِسالَةٍ بِمَرجِعٍ مَجهول —
    /// وتِلكَ بِعَينِها الحالَةُ الَّتي تُعيدُها Paddle مِراراً.</para>
    /// </summary>
    public static async Task<PaddleTransactionRecord?> FindTransactionAsync(
        IQuerySession session, PaddleEvent e, CancellationToken ct = default)
    {
        if (e.Reference is { Length: > 0 } reference)
            return await session.LoadAsync<PaddleTransactionRecord>(reference, ct);

        if (e.TransactionId is { Length: > 0 } txn)
            return await session.Query<PaddleTransactionRecord>()
                .Where(r => r.TransactionId == txn).FirstOrDefaultAsync(ct);

        return null;
    }
}
