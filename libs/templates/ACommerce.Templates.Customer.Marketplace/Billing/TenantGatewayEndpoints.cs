using ACommerce.Kit.Auth.Server;
using ACommerce.Platform.I18n;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ACommerce.Templates.Customer.Marketplace.Gates;

namespace ACommerce.Templates.Customer.Marketplace.Billing;

/// <summary>
/// <para><b>نُقطَةُ التَحَقُّقِ مِن دَفعِ المُشتَري عِندَ بَوّابَةِ المَتجَر</b> (‏ADR-032 §٢‑هـ الخُطوَة 9) — مِلَفٌّ مُنفَصِلٌ عَلى نَسَقِ
/// <c>PaddleEndpoints</c> و<c>OtoShippingEndpoints</c>: نِطاقُ المُراجَعَةِ «هذا المِلَفّ»، وسَقفُ نَزيفِه يُقاسُ وَحدَه
/// (<c>EndpointBodyBleedTests</c>).</para>
///
/// <para><b>والعَودَةُ مِن البَوّابَةِ صَفحَةٌ لا نُقطَة</b> — <c>Components/Pages/GatewayPayReturn.razor</c> عَلى
/// <c>/{slug}/pay/return</c>: قِراءَةٌ خالِصَةٌ تَرسُمُ النَتيجَةَ وزِرّاً واحِداً يُرسِلُ إلى هُنا. ولِماذا لَيسَت <c>MapGet</c> في هذا
/// المِلَفِّ كَما كَتَبَ صَفُّ الخُطوَة: صَفَحاتُ المَتجَرِ مُكَوِّناتُ Razor تَحتَ قالَبِه وثيمِه، و<c>MapGet</c> يَرسُمُ صَفحَةً يَحتاجُ
/// أُنبوبَ تَصييرٍ لا وُجودَ لَه في المُستَودَع (القاعِدَة ٨) — وسابِقَتُه <c>PayPalReturn.razor</c>. و«الـGET لا يَكتُب» مَفروضٌ عَلَيها
/// بِـ<c>No_razor_page_writes_state</c> كَما يُفرَضُ عَلى <c>MapGet</c> بِـ<c>No_get_endpoint_writes_state</c> (‏ADR-032 §٨‑ب).</para>
///
/// <para><b>والمَسارُ مَكتوبٌ حَرفاً لا بِثابِت</b> — حُجَّةُ <c>PaddleEndpoints</c> بِعَينِها: فَواحِصُ النِقاطِ نَصِّيَّةٌ تَقرَأُ
/// <c>MapPost("…"</c>، ونُقطَةٌ تُسَجَّلُ بِثابِتٍ لا يَراها عَدّادُ النَزيفِ ولا فاحِصُ الحُرّاس.</para>
/// </summary>
public static class TenantGatewayEndpoints
{
    public static IEndpointRouteBuilder MapTenantGateway(this IEndpointRouteBuilder app)
    {
        // ─── المُشتَري يَطلُبُ إثباتَ دَفعِه ───────────────────────────────────
        //
        // **الحارِسُ قَبلَ أَيِّ حَقل** (القاعِدَة ٦): الجَلسَةُ أَوَّلاً، ومِنها سِجِلُّ السُلطَةِ `DealPayer` الَّذي يَطلُبُه تَوقيعُ
        // `GatewayPayments.VerifyAsync` — ثُمَّ يُقرَأُ حَقلُ الصَفقَةِ المَرجِع. والقَرارُ «أَهذِه صَفقَتُه وعَلَيها فاتورَة؟» في
        // `DealPayAuthorization` النَقِيَّةِ قَبلَ فَكِّ المِفتاحِ وقَبلَ البَوّابَة؛ والبُرهانُ في `GatewayPaymentProof.Gate` النَقِيَّة.
        // **وبِلا `RequireStoreWritable`** — مُثَبَّتٌ بِسَبَبِه في `PinnedUngatedStoreWrites`: دَفعٌ وَقَعَ عِندَ البَوّابَةِ خَبَرٌ عَن مالٍ
        // قُبِضَ لا مُحتَوىً يَكتُبُه المَتجَر. **ولا يُمَرَّرُ إلغاءُ الطَلَب** (`RequestAborted`) — نَسَقُ `/ship`: مُهلَةُ العَميلِ تَحُدُّ النِداء.
        //
        // **والدَفعُ الثابِتُ يُكتَبُ في الخِدمَةِ ويُشعَرُ بِه البائِعُ هُنا** (‏ADR-032 الخُطوَة 10، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق):
        // `VerifyAsync` تَكتُبُ بِـ`ConfirmGatewayPaymentAsync` وتُدخِلُ ما يَقِفُ قَبلَ «دَفع» بِالمُشتَري، وتُرجِعُ ما كُتِبَ الآنَ وَحدَه —
        // فَيُشعَرُ مالِكُ إعلانِ كُلٍّ عَلى الأُنبوبِ القائِمِ (`DealNotices`) بَعدَ الحِفظِ ومَرَّةً، نَسَقُ `/checkout/submit`.
        app.MapPost("/{slug}/payments/verify",
            async (string slug, HttpRequest req, Services.Deals.GatewayPayments gateway, Services.Deals.DealNotices notices, L l) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var payer = new Services.Deals.DealPayer(userId);
            // وعَشرُ نَقَراتٍ في السّاعَةِ لِكُلِّ مُشتَرٍ بِالحِصَّةِ القائِمَة — بَعدَ الجَلسَةِ وقَبلَ فَكِّ المِفتاحِ والنِداء (‏2026-10-29، قَرارُ المالِك د5 (٥)؛ تَوصيفُه `23fae63`).
            var dealRef = req.Form[Services.Deals.GatewayReturn.DealKey].ToString();
            var res = AuthHandlers.TryConsumeActionQuota(Services.Deals.GatewayReturn.VerifyQuotaKey(slug, userId))
                ? await gateway.VerifyAsync(slug, payer, dealRef, l)
                : Services.Deals.GatewayReturn.RateLimited(dealRef);
            await notices.NotifyPaidAsync(slug, res.Confirmed, l);
            // ومَن أَلغَته المَنصَّةُ بِانتِهاءِ المُهلَةِ يُخبَرُ مُشتَريه وكُلُّ بائِعٍ فيه (‏2026-10-29، قَرارُ المالِك د5 (٤)).
            // (‏2026-10-30، جَولَةُ الإصلاحِ الأولى لِلمَسار F3): وتُوسَمُ كُلٌّ بَعدَ إشعاراتِها — فَإشعارٌ يَرمي هُنا تُخبِرُ بِه رِسالَةُ المُهلَة.
            await notices.NotifyPaymentExpiredAsync(slug, res.Expired, l, told: id => gateway.MarkExpiryToldAsync(slug, id));
            return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, Services.Deals.GatewayReturn.AfterVerify(res)));
        }).RequireFormBody().DisableAntiforgery();

        // ─── إشعارُ الدَفعِ الآليُّ مِن بَوّابَةِ المَتجَر (‏2026-10-29، قَرارُ المالِك د5 (٢) «ج»؛ ‏ADR-032 §٩‑٨) ─────────────
        //
        // **لا جَلسَةَ لِمُنادِيه** (خادِمُ البَوّابَة)، فَالحارِسُ **دالَّةُ بَوّابَةٍ نَقِيَّةٌ بِاسمِها** قَبلَ أَوَّلِ كِتابَة — `GatewayNoticeGate.Admit`
        // بِسِرِّ المَتجَرِ الَّذي أَصدَرناه وسَجَّلناه عِندَها، مُسَجَّلَةٌ في `BodyGuards` عَلى سابِقَةِ `OtoOrderStatusGate.Admit`. **ولا يُقرَأُ الجِسمُ بِلا سِرّ**
        // (نَسَقُ وارِدِ OTO). **والإشعارُ إشارَةٌ لا بُرهان**: `ConfirmNoticeAsync` يَطلُبُ الإشعارَ المَقبولَ في تَوقيعِه ويَجلِبُ الفاتورَةَ بِمِفتاحِ المَتجَرِ
        // ويَعرِضُها عَلى `GatewayPaymentProof.Gate` نَفسِها قَبلَ أَيِّ كِتابَة، ثُمَّ يُخبَرُ البائِعُ كَما يُخبَرُ بَعدَ «تَحَقَّق». **وبِلا `RequireStoreWritable`** —
        // مُثَبَّتٌ بِسَبَبِ جارَتِه في `PinnedUngatedStoreWrites`: دَفعٌ وَقَعَ خَبَرٌ لا مُحتَوى. والمَسارُ حَرفُ `GatewayPayments.NoticePath`.
        app.MapPost("/{slug}/payments/notify",
            async (string slug, HttpRequest req, Services.Deals.GatewayPayments gateway, Services.Deals.DealNotices notices, L l) =>
        {
            var secret = await gateway.NoticeSecretAsync(slug, req.HttpContext.RequestAborted);
            var raw = secret is not null ? await new StreamReader(req.Body).ReadToEndAsync(req.HttpContext.RequestAborted) : null;
            var admitted = Services.Deals.GatewayNoticeGate.Admit(secret, raw);
            var res = admitted.Notice is { } notice ? await gateway.ConfirmNoticeAsync(slug, notice) : null;
            if (res is not null) await notices.NotifyPaidAsync(slug, res.Confirmed, l);
            // والرَدُّ مِن النَتيجَتَين: تَأكيدٌ تَعَذَّرَ لِسَبَبٍ عابِرٍ ‏503 فَتُعيدُه البَوّابَة (‏2026-10-30، تَدقيقُ الموجَة ١١ C-PAY-1).
            return Results.StatusCode(Services.Deals.GatewayNoticeGate.StatusFor(admitted.Violation?.Code, res?.Code));
        }).DisableAntiforgery();

        return app;
    }
}
