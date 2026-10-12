using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
            async (string slug, Guid id, HttpRequest req, Services.Deals.DealShipping shipping) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var shipper = new Services.Deals.DealShipper(userId, AuthSession.ResolveUserName(req, slug) ?? "");
            var res = await shipping.ShipAsync(slug, id, shipper, req.Form["city"].ToString());
            return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, $"deals/{id}" + (res.Ok ? "" : $"?err={res.Code}")));
        }).DisableAntiforgery()
          .RequireStoreWritable();

        return app;
    }
}
