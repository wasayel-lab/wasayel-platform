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

            // ─── مَسارُ الطَلَبات (‏ADR-006) — بابٌ واحِدٌ ومَعجَمان ──
            // `PayPalOrderBillingPolicy.Parse` تُعطي `null` لِما لَيسَ
            // حَدَثَ طَلَب، فَيَنزِل الجِسمُ إلى مَسارِ الاشتِراكاتِ
            // **بِلا تَغييرِ حَرفٍ فيه**. سَطرُ تَفريعٍ واحِدٌ لا
            // نُقطَةٌ ثانِيَة: الـwebhook واحِدٌ ومُعَرِّفُه واحِد.
            if (PayPalOrderBillingPolicy.Parse(raw) is { } order)
                return await PayPalOrderFlow.HandleAsync(order, session, paypal, audit, log, http);

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
            // مَصدَرانِ لِمُعَرِّفِ الخُطَّة (وَثيقَةُ الرِباطِ ثُمَّ
            // المِلَفّ)، وقاعِدَةُ التَرجيحِ **في مَوضِعٍ واحِدٍ** تَقرَؤُه
            // الشاشَةُ والنُقطَة — وإلّا عَرَضَت واحِداً وأَرسَلَت آخَر.
            var bound = string.IsNullOrWhiteSpace(plan?.PlanId)
                ? null
                : await session.LoadAsync<PlatformPlanPayPal>(plan!.PlanId, http.RequestAborted);
            var payPalPlanId = PlatformPlanPayPalBinding.Resolve(
                PlatformPlanCatalog.Find(plan?.PlanId), bound);
            if (plan is null || !paypal.IsConfigured || string.IsNullOrWhiteSpace(payPalPlanId))
                return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkUnavailable);

            var by = admin.User is { } u ? $"studio · {u.Id}" : "platform-admin";
            var result = await paypal.CreateSubscriptionAsync(
                payPalPlanId!, slug, PayPalSurface.LinkKey(slug, payPalPlanId!), http.RequestAborted);

            // ─── فَشَلُ PayPal يُسَمّى، ولا يُخفى خَلفَ «تَعَذَّرَ» ───
            //
            // **العِلَّة**: هذا **بِالذاتِ** هُوَ المَوضِعُ الَّذي
            // يُنتَظَر فيه `Merchant not enabled for reference
            // transaction` — الخُطَّةُ تُنشَأُ بِنَجاح ثُمَّ يَفشَل
            // تَفعيلُ أَوَّلِ اشتِراكٍ بِعَطَبِ استِحقاق. وكانَ يُبتَلَع
            // كُلُّه في `LinkRefused` = «راجِع سِجِلَّ الخادِم»، فَيَذهَب
            // المالِكُ يُفَتِّشُ لوغاً عَن عِلَّةٍ عِلاجُها **رِسالَةٌ
            // إلى دَعمِ PayPal**.
            //
            // **والفَصلُ عَن `SaveApproveLink` مَقصود**: «‏PayPal رَفَضَت»
            // غَيرُ «رَدَّت بِلا رابِطِ مُوافَقَة»، وخَلطُهُما يُعطي
            // رِسالَةً واحِدَةً لِعِلَّتَينِ عِلاجُهُما مُختَلِف.
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
                return PayPalSurface.LinkFailed(slug, PayPalFailure.ScreenCode(result.FailureReason));

            if (!Services.Subscriptions.PayPalBillingService.SaveApproveLink(
                    session, plan, result, by, DateTime.UtcNow))
                return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkRefused);

            await session.SaveChangesAsync(http.RequestAborted);
            await audit.WriteAsync(slug, admin.User?.Id, by,
                Services.Subscriptions.PayPalBillingService.LinkAuditAction,
                "Tenant", slug, note: result.SubscriptionId);
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        // ─── خُطَّةُ PayPal: تُنشَأُ مِن الشاشَة، فَاللَوحَةُ اختِيارِيَّة ──
        //
        // **العِلَّة**: خُطُواتُ `docs/DEPLOY.md` §٢·ج كانَت تَفتَرِض
        // صَفحَةَ المُنتَجات/الخُطَط في لَوحَةِ PayPal، **وقَد تَعَذَّرَ
        // على المالِكِ فَتحُها**. وهذِه النُقطَةُ تُنشِئُ المُنتَجَ
        // والخُطَّةَ بِالواجِهَةِ REST **وتَكتُبُ المُعَرِّفَ في وَثيقَةِ
        // الرِباطِ فَوراً** — فَلا يُطلَبُ مِن المالِكِ نَسخُ `P-…` إلى
        // مِلَفٍّ ودَفعُه ونَشرُه.
        //
        // **والحارِسُ أَوَّلُ سَطر، قَبلَ قِراءَةِ حَقلٍ واحِد**
        // (القاعِدَة ٦): التَخويلُ يَسبِق تَحَقُّقَ الحُقول، وإلّا صارَ
        // خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة.
        app.MapPost("/admin/tenants/{slug}/plan/paypal-plan", async (
            string slug, HttpRequest req, IDocumentSession session,
            Services.Incubator.StudioAuth auth, PayPalGateway paypal,
            Services.Audit.AuditWriter audit) =>
        {
            var admin = await Services.PlatformAdminGuard.EvaluateAsync(session.DocumentStore, auth);
            if (!admin.Allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!paypal.IsConfigured) return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkUnavailable);

            var ct = req.HttpContext.RequestAborted;
            var plan = await session.LoadAsync<TenantPlan>(slug, ct);
            var draft = PayPalCatalogPolicy.ReadDraft(
                plan?.PlanId, req.Form["name"], req.Form["price"],
                req.Form["currency"], req.Form["period"]);

            var violations = PayPalCatalogPolicy.Validate(draft);
            if (violations.Count > 0) return PayPalSurface.LinkFailed(slug, violations[0].Code);

            var by = admin.User is { } u ? $"studio · {u.Id}" : "platform-admin";
            PayPalCatalogPlan created;
            // ورِسالَةُ PayPal تُعرَض كَما هي — رَمزُها ونَصُّها. و«فَشِلَ
            // الإنشاء» وَحدَها تُرسِل المالِكَ يُخَمِّن.
            // ونَفسُ التَصنيفِ الَّذي يَقرَؤُه مَسارُ رابِطِ الدَفع —
            // خَطَأُ الاستِحقاقِ يُعطي رَمزَه، وما عَداهُ نَصَّ PayPal.
            try { created = await paypal.CreateCatalogPlanAsync(draft, ct); }
            catch (Exception ex) { return PayPalSurface.LinkFailed(slug, PayPalFailure.ScreenCode(ex.Message)); }

            if (!Services.Subscriptions.PayPalBillingService.BindCatalogPlan(
                    session, PayPalCatalogPolicy.BindingFor(draft, created, by, DateTime.UtcNow)))
                return PayPalSurface.LinkFailed(slug, PayPalSurface.LinkRefused);

            await session.SaveChangesAsync(ct);
            await audit.WriteAsync(slug, admin.User?.Id, by,
                Services.Subscriptions.PayPalBillingService.CatalogPlanAuditAction,
                "PlatformPlan", draft.PlanSlug, note: created.PlanId);
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        // ─── رابِطُ دَفعٍ مَرِن: المَبلَغُ والمُدَّةُ لَحظَةَ الطَلَب ──
        //
        // **العِلَّة (‏ADR-006)**: مَسارُ الخُطَّةِ أَعلاه يَشتَرِط
        // خُطَّةً مُعَرَّفَةً سَلَفاً عِندَ PayPal، ويَقوم تَحتَ
        // الغِطاءِ على اتِّفاقِيَّةِ فَوتَرَة — أَي عائِلَةِ
        // Reference Transactions الَّتي **تَحتاج استِحقاقاً لَم يُثبَت**.
        // وهذا المَسارُ يُرسِل المَبلَغَ والعُملَةَ والوَصفَ ومَرجِعَنا
        // في جِسمِ الطَلَبِ نَفسِه، فَيَفتَح صَفحَةَ دَفعٍ مُستَضافَةً
        // **بِصِفرِ استِحقاق**.
        //
        // **والحارِسُ أَوَّلُ سَطر، قَبلَ قِراءَةِ حَقلٍ واحِد**
        // (القاعِدَة ٦): التَخويلُ يَسبِق تَحَقُّقَ الحُقول، وإلّا صارَ
        // خَطَأُ التَحَقُّقِ قِناعاً لِلثَغرَة.
        app.MapPost("/admin/tenants/{slug}/plan/paypal-order", async (
            string slug, HttpRequest req, IDocumentSession session,
            Services.Incubator.StudioAuth auth, PayPalGateway paypal,
            Services.Audit.AuditWriter audit) =>
        {
            var admin = await Services.PlatformAdminGuard.EvaluateAsync(session.DocumentStore, auth);
            if (!admin.Allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!paypal.IsConfigured) return PayPalOrderSurface.Failed(slug, PayPalSurface.LinkUnavailable);

            var ct = req.HttpContext.RequestAborted;
            var plan = await session.LoadAsync<TenantPlan>(slug, ct);
            var draft = PayPalOrderSurface.DraftFrom(req, slug, plan);

            var violations = PayPalOrderPolicy.Validate(draft);
            if (violations.Count > 0) return PayPalOrderSurface.Failed(slug, violations[0].Code);

            var by = admin.User is { } u ? $"studio · {u.Id}" : "platform-admin";
            var reference = PayPalOrderPolicy.Reference(draft);
            var result = await paypal.CreateOrderAsync(
                draft, reference, PayPalOrderSurface.OriginFrom(req),
                PayPalOrderPolicy.OrderRequestId(draft), ct);

            // ورِسالَةُ PayPal تُعرَض كَما هي — رَمزُها ونَصُّها، بِنَفسِ
            // التَصنيفِ الَّذي يَقرَؤُه مَسارا الخُطَّةِ والاشتِراك.
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
                return PayPalOrderSurface.Failed(slug, PayPalFailure.ScreenCode(result.FailureReason));

            if (!Services.Subscriptions.PayPalBillingService.SaveOrder(
                    session, PayPalOrderSurface.RecordFor(draft, reference, result, by, DateTime.UtcNow)))
                return PayPalOrderSurface.Failed(slug, PayPalOrderSurface.OrderRefused);

            await session.SaveChangesAsync(ct);
            await audit.WriteAsync(slug, admin.User?.Id, by,
                Services.Subscriptions.PayPalBillingService.OrderAuditAction,
                "TenantPlan", slug, note: $"{reference} · {result.OrderId}");
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        // ─── «التَقِط الآن» — المَخرَجُ اليَدَويُّ الَّذي يُبلَغ
        //     بِالنَقر (القاعِدَة ١٢) ──────────────────────────────────
        //
        // **العِلَّة**: الالتِقاطُ يَقودُه الحَدَث. فَإن انقَطَعَت
        // الأَحداثُ بَقِيَ طَلَبٌ وافَقَ عَلَيه الدافِعُ بِلا التِقاط —
        // **وتُلغيه PayPal بَعدَ نافِذَتِه وتُعيدُ المالَ**. فَلِهذا
        // الانقِطاعِ عِلاجٌ بِنَقرَة، لا بِأَمرِ كونسول.
        app.MapPost("/admin/tenants/{slug}/plan/paypal-capture", async (
            string slug, HttpRequest req, IDocumentSession session,
            Services.Incubator.StudioAuth auth, PayPalGateway paypal) =>
        {
            var admin = await Services.PlatformAdminGuard.EvaluateAsync(session.DocumentStore, auth);
            if (!admin.Allowed) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (!paypal.IsConfigured) return PayPalOrderSurface.Failed(slug, PayPalSurface.LinkUnavailable);

            var ct = req.HttpContext.RequestAborted;
            var reference = req.Form[PayPalOrderSurface.ReferenceField].ToString().Trim();
            var order = await session.LoadAsync<PayPalOrderRecord>(reference, ct);
            if (order is null || order.TenantSlug != slug)
                return PayPalOrderSurface.Failed(slug, PayPalOrderSurface.OrderNotFound);

            var result = await paypal.CaptureOrderAsync(
                order.OrderId, PayPalOrderPolicy.CaptureRequestId(order.Id), ct);
            if (!string.IsNullOrWhiteSpace(result.FailureReason))
                return PayPalOrderSurface.Failed(slug, PayPalFailure.ScreenCode(result.FailureReason));

            // **ولا تُمَدَّدُ باقَةٌ مِن هُنا**: نَجاحُ الالتِقاطِ إثباتٌ
            // صَحيحٌ ولا يُستَعمَلُ لِلتَمديد — رِسالَةُ
            // `PAYMENT.CAPTURE.COMPLETED` هي وَحدَها الَّتي تُمَدِّد،
            // بِمِفتاحِ `event_id` الَّذي يَمنَع الازدِواج.
            order.CaptureId = string.IsNullOrWhiteSpace(result.CaptureId) ? order.CaptureId : result.CaptureId;
            order.At = DateTime.UtcNow;
            session.Store(order);
            await session.SaveChangesAsync(ct);
            return Results.Redirect($"/admin/tenants/{slug}/plan?saved=1");
        }).DisableAntiforgery();

        return app;
    }
}
