using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ACommerce.Templates.Customer.Marketplace.Shipping;

/// <summary>
/// <para><b>نِقاطُ الشَحنِ عَبرَ OTO</b> (‏ADR-033 §٢‑هـ الخُطوَة 3) — مِلَفٌّ مُنفَصِلٌ عَلى نَسَقِ
/// <c>Billing/PaddleEndpoints.cs</c>: نِطاقُ المُراجَعَةِ «هذا المِلَفّ»، وسَقفُ نَزيفِه يُقاسُ وَحدَه
/// (<c>EndpointBodyBleedTests</c>).</para>
///
/// <para><b>والمَساراتُ مَكتوبَةٌ حَرفاً لا بِثابِت</b> — حُجَّةُ <c>PaddleEndpoints</c> بِعَينِها: فَواحِصُ
/// النِقاطِ نَصِّيَّةٌ تَقرَأُ <c>MapPost("…"</c>، ونُقطَةٌ تُسَجَّلُ بِثابِتٍ لا يَراها عَدّادُ النَزيفِ ولا
/// فاحِصُ الحُرّاس.</para>
/// </summary>
public static class OtoShippingEndpoints
{
    /// <summary>مَسارُ الوارِدِ لِمَتجَرٍ بِعَينِه — <b>مَوضِعٌ واحِدٌ</b> يَقرَؤُه الاختِبارُ و<c>docs/DEPLOY.md</c> (عُنوانُ
    /// الإشعارِ الَّذي يُسَجَّلُ عِندَ OTO). والنُقطَةُ تَكتُبُه حَرفاً، و<c>OtoOrderStatusTests</c> يَحمَرُّ إن افتَرَقا.</summary>
    public const string OrderStatusPath = "/{slug}/shipping/oto/order-status";

    /// <summary>عَلامَةٌ لِفِئَةِ اللوغ وَحدَها — الصِنفُ الحاوي ساكِن (نَسَقُ <c>PaddleEndpoints.Log</c>).</summary>
    public sealed class Log { }

    public static IEndpointRouteBuilder MapOtoShipping(this IEndpointRouteBuilder app)
    {
        // ─── البائِعُ يُنشِئُ البوليصَة ───────────────────────────────────
        //
        // **والحارِسُ في التَوقيع** (القاعِدَة ٦): سِجِلُّ السُلطَةِ `DealShipper` في تَوقيعِ
        // `DealShipping.ShipAsync` وقَرارُه في `DealShipAuthorization.Validate` النَقِيَّة — هُوِيَّةُ
        // مالِكِ الإعلانِ بِدالَّةِ `/accept` نَفسِها (‏`OwnerIdOf`)، لا أُنبوبٌ جَديد — و`RequireStoreWritable`
        // في السِلسِلَة. والجَلسَةُ تَقولُ «مَن أَنت» وَحدَها؛ والجِسمُ يُصَرِّحُ بِهُوِيَّةِ الشاحِنِ ومَدينَةِ
        // المُستَلِمِ ولا يُقَرِّر. والرَفضُ يَعودُ إلى الصَفحَةِ بِرَمزِه — نَسَقُ `/accept` و`/cancel`.
        // **ولا يُمَرَّرُ إلغاءُ الطَلَب** (`RequestAborted`): مُتَصَفِّحٌ يُغلَقُ بَعدَ أَن أُنشِئَ الطَلَبُ عِندَ OTO لا
        // يَقطَعُ كِتابَةَ مَرجِعِه — مُهلَةُ العَميلِ تَحُدُّ النِداء.
        app.MapPost("/{slug}/deals/{id:guid}/ship",
            async (string slug, Guid id, HttpRequest req, Services.Deals.DealShipping shipping, Services.Queries.AccountQueries actorNames) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var shipper = new Services.Deals.DealShipper(userId, await actorNames.ActorNameAsync(slug, userId) ?? "");
            var res = await shipping.ShipAsync(slug, id, shipper, req.Form["city"].ToString());
            return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, $"deals/{id}" + (res.Ok ? "" : $"?err={res.Code}")));
        }).DisableAntiforgery().RequireFormBody()
          .RequireStoreWritable();

        // ─── وارِدُ حالِ الطَلَبِ مِن OTO ─────────────────────────────────
        //
        // **لا جَلسَةَ لِمُنادِيه** (خادِمُ OTO)، فَالحارِسُ **دالَّةُ بَوّابَةٍ نَقِيَّةٌ بِاسمِها** قَبلَ أَوَّلِ كِتابَة —
        // `OtoOrderStatusGate.Admit`، مُسَجَّلَةٌ في `BodyGuards` عَلى سابِقَةِ `PaddleWebhookGuard.Gate` في الشَكل.
        // **أَمّا المَضمونُ فَالإشعارُ إشارَةٌ لا بُرهان** (‏ADR-033 §٢‑هـ القاعِدَة ٦): `ReceiveAsync` يَطلُبُ الإشعارَ
        // المَقبولَ في تَوقيعِه ويَسأَلُ OTO عَن الطَلَبِ بِرَمزِ البائِعِ قَبلَ أَيِّ كِتابَة. ومَتجَرٌ بِلا رَمزِ ناقِلٍ يُجابُ
        // ‏404 فارِغاً ولا يُكتَبُ فيه شَيء. **وبِلا `RequireStoreWritable`** — مُثَبَّتٌ بِسَبَبِه في `PinnedUngatedStoreWrites`:
        // تَسليمٌ وَقَعَ في العالَمِ خَبَرٌ لا مُحتَوىً يَكتُبُه المَتجَر.
        // **ولا يُقرَأُ الجِسمُ بِلا ناقِل** (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ تَوصيفُه `ecb278a`): كانَ `ReadToEndAsync` يَسبِقُ
        // البَوّابَةَ فَيُقرَأُ كُلُّه ثُمَّ يُجابُ ‏404 — والدَليلُ والبَوّابَةُ يَقولانِ «لا يُقرَأ». فَيُسأَلُ الناقِلُ أَوَّلاً ولا يُقرَأُ إلّا مَعَه،
        // والبَوّابَةُ هي هي: `Admit` تُجيبُ `carrier_unavailable` قَبلَ أَن تَنظُرَ في النَصّ.
        // **والناقِلُ صارَ «لِلمَتجَرِ ناقِلٌ أَصلاً»** (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7): رَمزُ التَهيئَةِ لِمَتجَرِ العَرض، أَو مَدخَلُ بائِعٍ
        // فَعّالٌ واحِدٌ عَلى الأَقَلّ — ويُتَحَقَّقُ مِن تَوقيعِ الإشعارِ بِسِرِّ بائِعِ صَفقَتِه في `ReceiveAsync` قَبلَ سُؤالِ OTO.
        // **وقاعِدَةٌ تَتَعَطَّلُ لَحظَةَ السُؤالِ ‏503 لا ‏404** (‏2026-10-29، قَرارُ المالِك د9 (٢) «ب»؛ تَوصيفُه `7acae84`): `StoreCarrierAsync` بِجَوابٍ ثالِث،
        // و`Admit` تُجيبُه `carrier_unreadable` بِلا قِراءَةِ جِسم — فَتُعيدُ OTO الإرسالَ ولا تَحسَبُ النُقطَةَ غائِبَة.
        app.MapPost("/{slug}/shipping/oto/order-status",
            async (string slug, HttpRequest req, Services.Deals.DealShipping shipping, ILogger<Log> log) =>
        {
            var carrier = await shipping.StoreCarrierAsync(slug, req.HttpContext.RequestAborted);
            var raw = carrier == Services.Deals.StoreCarrier.Ready ? await new StreamReader(req.Body).ReadToEndAsync(req.HttpContext.RequestAborted) : null;
            var admitted = Services.Deals.OtoOrderStatusGate.Admit(carrier, raw);
            var code = admitted.Notice is { } notice
                ? (await shipping.ReceiveAsync(slug, notice)).Code
                : admitted.Violation!.Code;
            log.LogInformation("[OTO] orderStatus {Slug} ← {Code}", slug, code);
            return Results.StatusCode(Services.Deals.OtoOrderStatusGate.StatusFor(code));
        }).DisableAntiforgery();

        return app;
    }
}
