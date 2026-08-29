using ACommerce.Kit.Payments.Providers.Paddle;
using ACommerce.Kit.Subscriptions;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>ثَلاثُ نُقاطٍ لِفَوتَرَةِ Paddle</b> — واحِدَةٌ تَستَقبِل،
/// وواحِدَةٌ تُنشِئ رابِطاً، وواحِدَةٌ تُخبِر صَفحَةَ الدَفعِ
/// الساكِنَةَ بِمَن تُهَيِّئ.</para>
///
/// <list type="number">
///   <item><c>POST /api/billing/paddle</c> — <b>بِلا مُستَأجِر</b>.
///   المَقطَعُ الأَوَّل <c>api</c> مَحجوزٌ في
///   <c>TenantResolverMiddleware.ReservedPaths</c>، فَالوَسيطُ لا
///   يَستَعلِم عَن مُستَأجِرٍ اسمُه «‏api» ولا يَضَعُ واحِداً.
///   والمُستَأجِرُ يَخرُج مِن <c>custom_data</c> داخِلَ الرِسالَةِ
///   المُوَثَّقَة — <b>لا مِن مَسارٍ ولا مِن رَأس</b>، ولَو قُرِئَ مِن
///   المَسارِ لَمَدَّدَ أَيُّ زائِرٍ باقَةَ أَيِّ مَتجَر.</item>
///
///   <item><c>POST /admin/tenants/{slug}/plan/paddle-link</c> —
///   بِحارِسِ مُشرِفِ المَنَصَّة، كَجاراتِها في مِلَفّ
///   PayPal.</item>
///
///   <item><c>GET /billing/paddle/config.json</c> — <b>رَمزُ العَميلِ
///   والبيئَة، وهُما عَلَنِيّانِ بِالتَصميم</b>: تَقرَؤُهُما
///   <c>paddle.js</c> في كُلِّ مُتَصَفِّحٍ يَفتَح صَفحَةَ الدَفع.
///   ولا سِرَّ يَمُرُّ مِن هُنا — لا مِفتاحُ API ولا سِرُّ
///   تَوقيع.</item>
/// </list>
///
/// <para><b>ومِلَفٌّ مُنفَصِلٌ عَن <c>PayPalEndpoints</c> عَمداً</b>:
/// نِطاقُ المُراجَعَةِ يُصبِح «هذا المِلَفّ»، وسَقفُ نَزيفِه يُقاس
/// وَحدَه — نَفسُ حُجَّةِ فَصلِ ذاكَ عَن مِلَفِّ النِقاط.</para>
/// </summary>
public static class PaddleEndpoints
{
    /// <summary>مَسارُ الرِسالَة — <b>مَوضِعٌ واحِد</b> يَقرَؤُه
    /// الاختِبارُ و<c>docs/DEPLOY.md</c>. وعُنوانٌ مَنسوخٌ بِيَدٍ في
    /// وَثيقَةٍ يَنجَرِف عَن الكود، ورِسالَةُ Paddle تَذهَب إلى ‏404
    /// بِصَمت.</summary>
    public const string WebhookPath = "/api/billing/paddle";

    /// <summary>نُقطَةُ إعدادِ صَفحَةِ الدَفعِ الساكِنَة.</summary>
    public const string ConfigPath = "/billing/paddle/config.json";

    /// <summary>عَلامَةٌ لِفِئَةِ اللوغ وَحدَها — الصِنفُ الحاوي ساكِنٌ
    /// فَلا يَصلُح وَسيطاً لِـ<c>ILogger&lt;T&gt;</c>.</summary>
    public sealed class Log { }

    public static IEndpointRouteBuilder MapPaddleBilling(this IEndpointRouteBuilder app)
    {
        // ─── الرِسالَة: تُتَحَقَّق ثُمَّ تُقرَأ ─────────────────────
        //
        // **التَرتيبُ هُوَ الأَمن**: الجِسمُ نَصٌّ يَكتُبُه أَيُّ مَن
        // يَعرِف العُنوان، فَلا يُقرَأُ حَتّى تَقولَ البَصمَةُ إنَّها
        // مِن Paddle. **والجِسمُ يُقرَأُ خامّاً** قَبلَ أَيِّ
        // `JsonDocument.Parse` — والتَحليلُ قَبلَ التَحَقُّقِ يُفشِلُه
        // (اختِبارٌ بِطَرَفَيه في `PaddleBillingPolicyTests`).
        //
        // **والمَسارُ مَكتوبٌ حَرفاً هُنا لا بِالثابِت، ويُقالُ لِماذا**:
        // فَواحِصُ النِقاطِ كُلُّها **نَصِّيَّة** — تَقرَأ `MapPost("…"`
        // مِن المَصدَر. ونُقطَةٌ تُسَجَّل بِثابِتٍ **لا يَراها عَدّادُ
        // النَزيفِ ولا فاحِصُ الحُرّاس** فَتَمُرُّ خَضراءَ لِأَنَّها
        // غَيرُ مَفحوصَة (القاعِدَة ١٠). والثابِتُ يَبقى لِلوَثيقَةِ
        // والاختِبار، و`PaddleRouteTests` يُحمِرُّ إن افتَرَقا.
        app.MapPost("/api/billing/paddle", async (
            HttpContext http, IDocumentSession session, PaddleGateway paddle,
            Services.Audit.AuditWriter audit, ILogger<Log> log) =>
        {
            var raw = await new StreamReader(http.Request.Body).ReadToEndAsync(http.RequestAborted);

            var gate = PaddleWebhookGuard.Gate(
                paddle.Options, PaddleSurface.SignatureFrom(http.Request), raw, DateTimeOffset.UtcNow);
            if (gate != PaddleWebhookGate.Accepted)
                return PaddleSurface.Rejected(log, gate);

            var e = PaddleBillingPolicy.Parse(raw);
            if (e is null) return PaddleSurface.UnreadableBody(log);

            return await PaddleFlow.HandleAsync(e, session, audit, log, http);
        }).DisableAntiforgery();

        // ─── رابِطُ الدَفع: يُنشَأُ بِيَدِ المُشرِفِ ويُخَزَّن ───────
        //
        // **والحارِسُ أَوَّلُ سَطر، قَبلَ قِراءَةِ حَقلٍ واحِد**
        // (القاعِدَة ٦): التَخويلُ يَسبِق تَحَقُّقَ الحُقول، وإلّا صارَ
        // خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة.
        app.MapPost("/admin/tenants/{slug}/plan/paddle-link", async (
            string slug, HttpRequest req, IDocumentSession session,
            Services.Incubator.StudioAuth auth, PaddleGateway paddle,
            Services.Audit.AuditWriter audit) =>
        {
            var admin = await Services.PlatformAdminGuard.EvaluateAsync(session.DocumentStore, auth);
            if (!admin.Allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            // **و«مُهَيَّأ» هُنا تَعني `CanSell` لا `IsConfigured`**:
            // مِفتاحٌ بِلا رَمزِ عَميلٍ ولا صَفحَةِ دَفعٍ يُنشِئ
            // مُعامَلَةً **بِرابِطٍ لا يُفتَح** — ومَدخَلٌ يَضُرّ
            // أَسوَأُ مِن غِيابِ مَدخَل (القاعِدَة ١٢).
            if (!paddle.CanSell) return PaddleSurface.Failed(slug, PaddleSurface.Unavailable);

            var ct = req.HttpContext.RequestAborted;
            var plan = await session.LoadAsync<TenantPlan>(slug, ct);
            var draft = PaddleSurface.DraftFrom(req, slug, plan);

            var violations = PaddleTransactionPolicy.Validate(draft);
            if (violations.Count > 0) return PaddleSurface.Failed(slug, violations[0].Code);

            var by = admin.User is { } u ? $"studio · {u.Id}" : "platform-admin";
            var reference = PaddleTransactionPolicy.Reference(draft);

            // ─── شَبَكَةُ الأَمان: لا يُدهَسُ سِجِلُّ دَفعٍ مَضى ─────
            //
            // مِفتاحُ الوَثيقَةِ هُوَ المَرجِع، والكِتابَةُ `Store`.
            // فَوَثيقَةٌ بَلَغَت «وَصَلَ المال» تَحمِل مُعَرِّفَ
            // المُعامَلَةِ الَّذي يَربِط أَيَّ استِردادٍ لاحِق، ودَهسُها
            // يُغلِقُ بابَ السَحبِ إلى الأَبَد. **وقَبلَ النِداءِ لا
            // بَعدَه**، فَلا تُفتَحُ مُعامَلَةٌ عِندَ Paddle ثُمَّ
            // يُرفَضُ حِفظُها.
            if (!PaddleTransactionPolicy.IsOverwritable(
                    await session.LoadAsync<PaddleTransactionRecord>(reference, ct)))
                return PaddleSurface.Failed(slug, PaddleSurface.AlreadySettled);

            var result = await paddle.CreateTransactionAsync(draft, reference, ct);

            // ورِسالَةُ Paddle تُعرَض كَما هي — رَمزُها ونَصُّها.
            // و«فَشِلَ الإنشاء» وَحدَها تُرسِل المُشرِفَ يُخَمِّن.
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
                return PaddleSurface.Failed(slug, result.FailureReason!);

            var checkoutUrl = PaddleTransactionPolicy.CheckoutUrl(
                paddle.Options.DefaultPaymentLink, result.CheckoutUrl, result.TransactionId, reference);
            if (string.IsNullOrWhiteSpace(checkoutUrl))
                return PaddleSurface.Failed(slug, PaddleSurface.LinkMissing);

            if (!Services.Subscriptions.PaddleBillingService.SaveTransaction(
                    session,
                    PaddleSurface.RecordFor(draft, reference, result, checkoutUrl, by, DateTime.UtcNow)))
                return PaddleSurface.Failed(slug, PaddleSurface.Refused);

            await session.SaveChangesAsync(ct);
            await audit.WriteAsync(slug, admin.User?.Id, by,
                Services.Subscriptions.PaddleBillingService.TransactionAuditAction,
                "TenantPlan", slug, note: $"{reference} · {result.TransactionId}");
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        // ─── إعدادُ صَفحَةِ الدَفعِ الساكِنَة ────────────────────────
        //
        // **العِلَّة**: صَفحَةُ `wwwroot` ساكِنَةٌ فَلا تَقرَأُ تَهيئَة،
        // و`paddle.js` تَحتاج **رَمزَ العَميلِ والبيئَة**. وكِتابَتُهُما
        // في الصَفحَةِ بِاليَد تَجعَل تَبديلَ الحِسابِ تَحريرَ مِلَفٍّ
        // ودَفعاً ونَشراً، **ونُسخَةَ الاختِبارِ تُنادي مُضيفَ
        // الإنتاج**.
        //
        // **ولا سِرَّ يَمُرُّ مِن هُنا**: رَمزُ العَميلِ عَلَنيٌّ
        // بِالتَصميم (يُرسَل إلى كُلِّ مُتَصَفِّح)، ومِفتاحُ الـAPI
        // وسِرُّ التَوقيعِ **لا يُقرَآنِ في هذا الجِسمِ إطلاقاً** —
        // مُثَبَّتٌ بِاختِبارٍ سالِب.
        //
        // **وقِراءَةٌ خالِصَة**: صِفرُ جَلسَةٍ وصِفرُ كِتابَة، فَلا
        // تَحمَرُّ الطَبَقَةُ ٨ ولا حارِسُ «نُقطَةُ GET تُبَدِّل
        // حالَةً مُخَزَّنَة».
        app.MapGet("/billing/paddle/config.json", (PaddleGateway paddle) =>
            Results.Json(new
            {
                enabled     = paddle.CanSell,
                token       = paddle.CanSell ? paddle.ClientToken : "",
                environment = paddle.Environment,
                returnUrl   = PaddleTransactionPolicy.ReturnPath,
            }));

        return app;
    }
}
