using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACommerce.Templates.Customer.Marketplace.Shipping;

/// <summary>
/// <para><b>نُقطَةُ حِسابِ الشَحنِ لِلبائِع</b> (‏ADR-033 الخُطوَة 7، تَيّارُ S8، ‏2026-10-12) — مِلَفٌّ مُنفَصِلٌ عَن نِقاطِ الصَفقَةِ والوارِدِ
/// (<see cref="OtoShippingEndpoints"/>): سَطحُ البائِعِ نِطاقُ مُراجَعَةٍ آخَر، وسَقفُ نَزيفِه يُقاسُ وَحدَه (<c>EndpointBodyBleedTests</c>)،
/// ومُضيفُ اختِبارِ الوارِدِ يُسَجِّلُ خِدمَةَ الشَحنِ وَحدَها فَلا تَلزَمُه خِدماتُ هذا السَطح.</para>
/// <para><b>والمَسارُ مَكتوبٌ حَرفاً</b> — حُجَّةُ <c>PaddleEndpoints</c> و<c>OtoShippingEndpoints</c> بِعَينِها.</para>
/// </summary>
public static class VendorShippingEndpoints
{
    public static IEndpointRouteBuilder MapVendorShipping(this IEndpointRouteBuilder app)
    {
        // ─── البائِعُ يَلصِقُ رَمزَ حِسابِه عِندَ OTO ─────────────────────────
        //
        // **والحارِسُ في التَوقيع** (القاعِدَة ٦): جَلسَةُ المَتجَرِ تَقولُ «مَن أَنت»، وسِجِلُّ السُلطَةِ `ListingVendor` — لا يُبنى إلّا
        // بِـ`ListingVendorGate.Admit` بِعَدِّ إعلاناتِه غَيرِ المَحذوفَةِ في المَتجَر — يَطلُبُه تَوقيعُ `VendorShippingService.SaveTokenAsync`.
        // **والتَخويلُ قَبلَ الحَقل**: لا يُقرَأُ الرَمزُ قَبلَ أَن يُعرَفَ أَنّ النّاقِرَ بائِعٌ هُنا. و`RequireStoreWritable` في السِلسِلَة:
        // حِفظُ رَمزٍ كِتابَةٌ في مَتجَرٍ كَسائِرِ الكِتابات. والرَدُّ يَعودُ إلى «إعلاناتي» بِرَمزِه — نَسَقُ `/ship`.
        app.MapPost("/{slug}/me/shipping/oto",
            async (string slug, HttpRequest req, Services.Queries.AccountQueries accounts,
                   Services.Shipping.VendorShippingService vendors, Services.Audit.AuditWriter audit) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var owned = (await accounts.MyListingsAsync(slug, userId)).Count;
            var admitted = Services.Shipping.ListingVendorGate.Admit(userId, AuthSession.ResolveUserName(req, slug), owned);
            if (admitted.Vendor is not { } vendor)
                return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, $"me/listings?shipping_err={admitted.Violation!.Code}"));

            var res = await vendors.SaveTokenAsync(slug, vendor, req.Form["refresh_token"].ToString(),
                Services.Shipping.VendorShippingSurface.InboundUrl(Billing.PayPalOrderSurface.OriginFrom(req), slug));
            if (res.Ok)
                await audit.WriteAsync(slug, vendor.UserId, vendor.Name, "provider.vendor.bind", "provider",
                    res.After!.Id, note: res.After.ProviderSlug, ip: req.HttpContext.Connection.RemoteIpAddress?.ToString(),
                    before: Services.Providers.ProviderBindSurface.AuditLine(res.Before),
                    after: Services.Providers.ProviderBindSurface.AuditLine(res.After));
            return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug,
                "me/listings" + (res.Ok ? "?shipping=saved" : $"?shipping_err={res.Code}")));
        }).DisableAntiforgery()
          .RequireStoreWritable();

        // ─── البائِعُ يَفتَحُ حِسابَ شَحنٍ عِندَ OTO بِالاعتِمادِ السوقِيّ (‏ADR-033 الخُطوَة 8، ‏2026-10-12) ─────────
        //
        // **والحارِسُ جَلسَةُ المَتجَرِ وهُوِيَّةُ البائِعِ وَحدَهُما** — البَوّابَةُ نَفسُها الَّتي تَحرُسُ اللَصق، قَبلَ قِراءَةِ حَقلٍ واحِدٍ مِن الاستِمارَة.
        // **ولا `PlatformAdminGuard`** وإن صَرَفَت الخِدمَةُ اعتِماداً مِن نَوعِ `platform_key` (الرَمزَ السوقِيّ): بَياناتُ التَسجيلِ بَياناتُ
        // البائِعِ الشَخصِيَّةُ تُرسَلُ إلى طَرَفٍ ثالِثٍ وتُنشِئُ حِساباً بِاسمِه — فَالفِعلُ فِعلُه لا فِعلُ مُديرٍ يَنوبُ عَنه (نَقضُ ‏ADR-012 §٢‑ب
        // المُعلَنُ في تَرويسَةِ ‏ADR-033؛ مُثَبَّتٌ بِصَفٍّ مُسَمّى في `WriteEndpointGuardTests`). وسَطرُ الأَثَرِ قَبلَ أَوَّلِ نِداءٍ في الخِدمَة.
        // والرَدُّ إلى «إعلاناتي» بِرَمزِه — نَسَقُ اللَصق.
        app.MapPost("/{slug}/me/shipping/oto/register",
            async (string slug, HttpRequest req, Services.Queries.AccountQueries accounts,
                   Services.Shipping.VendorShippingService vendors) =>
        {
            var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
            if (parsed is null) return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, "login"));
            var (userId, _, _) = parsed.Value;
            var owned = (await accounts.MyListingsAsync(slug, userId)).Count;
            var admitted = Services.Shipping.ListingVendorGate.Admit(userId, AuthSession.ResolveUserName(req, slug), owned);
            if (admitted.Vendor is not { } vendor)
                return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug, $"me/listings?shipping_err={admitted.Violation!.Code}"));

            var res = await vendors.RegisterAsync(slug, vendor, Services.Shipping.VendorRegistrationForm.Read(req.Form),
                Services.Shipping.VendorShippingSurface.InboundUrl(Billing.PayPalOrderSurface.OriginFrom(req), slug),
                req.HttpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Redirect(MarketplaceTemplateExtensions.Link(req, slug,
                "me/listings" + (res.Ok ? "?shipping=registered" : $"?shipping_err={res.Code}")));
        }).DisableAntiforgery()
          .RequireStoreWritable();

        return app;
    }
}
