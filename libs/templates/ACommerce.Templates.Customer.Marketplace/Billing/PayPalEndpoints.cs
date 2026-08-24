using ACommerce.Kit.Payments.Providers.PayPal;
using ACommerce.Kit.Subscriptions;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>نُقطَتانِ لِفَوتَرَةِ PayPal</b> — واحِدَةٌ تَستَقبِل، وواحِدَةٌ
/// تُنشِئ رابِطاً.</para>
///
/// <list type="number">
///   <item><c>POST /api/billing/paypal</c> — <b>بِلا مُستَأجِر</b>.
///   المَقطَعُ الأَوَّل <c>api</c> مَحجوزٌ في
///   <c>TenantResolverMiddleware.ReservedPaths</c> مُنذُ مَوجَةِ
///   الـAPI، فَالوَسيطُ لا يَستَعلِم عَن مُستَأجِرٍ اسمُه «‏api»
///   ولا يَضَعُ واحِداً. والمُستَأجِرُ يَخرُج مِن <c>custom_id</c>
///   داخِلَ الرِسالَةِ المُوَثَّقَة — <b>لا مِن مَسارٍ ولا مِن
///   رَأس</b>، ولَو قُرِئَ مِن المَسارِ لَمَدَّدَ أَيُّ زائِرٍ باقَةَ
///   أَيِّ مَتجَر.</item>
///
///   <item><c>POST /admin/tenants/{slug}/plan/paypal-link</c> —
///   بِحارِسِ مُشرِفِ المَنَصَّة، كَجاراتِها الثَلاثِ في مِلَفّ
///   النِقاط.</item>
/// </list>
///
/// <para><b>ولا <c>IDocumentStore</c> في التَوقيعَين</b>: تُحقَن
/// <c>IDocumentSession</c>. وهذا صَحيحٌ هُنا بِالذات لِأَنّ
/// <c>TenantPlan</c> و<c>PayPalWebhookRecord</c> مُسَجَّلَتانِ
/// <c>SingleTenanted()</c> صَراحَةً — فَجَلسَةٌ بِلا سلاجٍ هي
/// <b>الجَلسَةُ الصَحيحَة</b> لَهُما، لا تَنازُلاً. (السَبَبُ الَّذي
/// يَمنَع ذلك في نِقاطِ المَتجَرِ قائِمٌ هُناك: وَثائِقُها
/// <c>AllDocumentsAreMultiTenanted</c>، فَجَلسَةٌ بِلا سلاجٍ تَكتُب
/// في <c>*DEFAULT*</c> صامِتَة.)</para>
///
/// <para><b>ومِلَفٌّ مُنفَصِلٌ عَن مِلَفّ النِقاط عَمداً</b>: نِطاقُ
/// المُراجَعَةِ يُصبِح «هذا المِلَفّ»، وسَقفُ نَزيفِه يُقاس وَحدَه —
/// نَفسُ حُجَّةِ <c>ApiV1Endpoints.cs</c> يَومَ فُصِل.</para>
/// </summary>
public static class PayPalEndpoints
{
    /// <summary>مَسارُ الرِسالَة — <b>مَوضِعٌ واحِد</b> يَقرَؤُه
    /// التَسجيلُ والاختِبارُ و<c>docs/DEPLOY.md</c> §٢·ج. وعُنوانٌ
    /// مَنسوخٌ بِيَدٍ في وَثيقَةٍ يَنجَرِف عَن الكود، ورِسالَةُ PayPal
    /// تَذهَب إلى ‏404 بِصَمت.</summary>
    public const string WebhookPath = "/api/billing/paypal";

    /// <summary>عَلامَةٌ لِفِئَةِ اللوغ وَحدَها — الصِنفُ الحاوي ساكِنٌ
    /// فَلا يَصلُح وَسيطاً لِـ<c>ILogger&lt;T&gt;</c>.</summary>
    public sealed class Log { }

    public static IEndpointRouteBuilder MapPayPalBilling(this IEndpointRouteBuilder app)
    {
        // ─── الرِسالَة: تُتَحَقَّق ثُمَّ تُقرَأ ─────────────────────
        // **التَرتيبُ هُوَ الأَمن**: الجِسمُ نَصٌّ يَكتُبُه أَيُّ مَن
        // يَعرِف العُنوان، فَلا يُقرَأُ حَتّى يَقولَ PayPal إنَّها
        // مِنها. والبَوّابَةُ دالَّةٌ نَقِيَّةٌ مُقاسَةٌ بِجَدوَل
        // (‏`PayPalBillingPolicy.Gate`)، لا شَرطٌ مَنثورٌ في الجِسم.
        //
        // **والمَسارُ مَكتوبٌ حَرفاً هُنا لا بِالثابِت، ويُقالُ لِماذا**:
        // فَواحِصُ النِقاطِ كُلُّها **نَصِّيَّة** — تَقرَأ
        // `MapPost("…"` مِن المَصدَر. ونُقطَةٌ تُسَجَّل بِثابِتٍ
        // **لا يَراها عَدّادُ النَزيفِ ولا فاحِصُ الحُرّاس** —
        // فَتَمُرُّ خَضراءَ لِأَنَّها غَيرُ مَفحوصَة، وذاكَ أَسوَأُ مِن
        // أَن تَحمَرّ (القاعِدَة ١٠). والثابِتُ يَبقى لِلوَثيقَةِ
        // والاختِبار، و`PayPalRouteTests` يُحمِرّ إن افتَرَقا.
        app.MapPost("/api/billing/paypal", async (
            HttpContext http, IDocumentSession session, PayPalGateway paypal,
            Services.Audit.AuditWriter audit, ILogger<Log> log) =>
        {
            var raw = await new StreamReader(http.Request.Body).ReadToEndAsync(http.RequestAborted);
            var headers = PayPalSurface.HeadersFrom(http.Request);

            var verified = paypal.CanVerifyWebhooks
                && await paypal.VerifyWebhookSignatureAsync(headers, raw, http.RequestAborted);
            var gate = PayPalBillingPolicy.Gate(paypal.Options, headers, verified);
            if (gate != PayPalWebhookGate.Accepted)
                return PayPalSurface.Rejected(log, gate);

            var e = PayPalBillingPolicy.Parse(raw);
            if (e is null) return PayPalSurface.Unreadable(log);

            var now = DateTime.UtcNow;
            var seen = await session.LoadAsync<PayPalWebhookRecord>(e.EventId, http.RequestAborted);
            var plan = e.TenantSlug is null
                ? null
                : await session.LoadAsync<TenantPlan>(e.TenantSlug, http.RequestAborted);
            var decision = PayPalBillingPolicy.Decide(e, plan, seen is not null, now);

            if (!Services.Subscriptions.PayPalBillingService.Apply(session, plan, e, decision, now))
                return PayPalSurface.NoWrite(log, e, decision);

            await session.SaveChangesAsync(http.RequestAborted);
            await PayPalSurface.AuditAsync(audit, e, decision, http);
            return PayPalSurface.Applied(log, e, decision);
        }).DisableAntiforgery();

        // ─── رابِطُ الدَفع: يُنشَأُ بِيَدِ المُشرِفِ ويُخَزَّن ────────
        // ولا دَورَةَ اعتِمادٍ هُنا كَجاراتِها: القابِضُ هُوَ المُقِرّ.
        app.MapPost("/admin/tenants/{slug}/plan/paypal-link", async (
            string slug, HttpContext http, IDocumentSession session,
            Services.Incubator.StudioAuth auth, PayPalGateway paypal,
            Services.Audit.AuditWriter audit) =>
        {
            // المَخزَنُ مِن الجَلسَةِ لا مِن التَوقيع — فَلا تُضاف
            // إدخالَةٌ إلى سِجِلِّ آخِذي المَخزَن (وهُوَ سِجِلٌّ
            // يَتَقَلَّص)، والجَلسَةُ الواحِدَةُ تَبقى هي مَن يُودِع.
            var admin = await Services.PlatformAdminGuard.EvaluateAsync(session.DocumentStore, auth);
            if (!admin.Allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var plan = await session.LoadAsync<TenantPlan>(slug, http.RequestAborted);
            var payPalPlanId = PlatformPlanCatalog.Find(plan?.PlanId)?.PayPalPlanId;
            if (plan is null || !paypal.IsConfigured || string.IsNullOrWhiteSpace(payPalPlanId))
                return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkUnavailable);

            var by = admin.User is { } u ? $"studio · {u.Id}" : "platform-admin";
            var result = await paypal.CreateSubscriptionAsync(
                payPalPlanId!, slug, PayPalSurface.LinkKey(slug, DateTime.UtcNow), http.RequestAborted);

            if (!Services.Subscriptions.PayPalBillingService.SaveApproveLink(
                    session, plan, result, by, DateTime.UtcNow))
                return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkRefused);

            await session.SaveChangesAsync(http.RequestAborted);
            await audit.WriteAsync(slug, admin.User?.Id, by,
                Services.Subscriptions.PayPalBillingService.LinkAuditAction,
                "Tenant", slug, note: result.SubscriptionId);
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        return app;
    }
}
