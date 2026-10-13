using System.Net;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَمزٌ لَصَقَه البائِعُ وتَرُدُّه OTO عِندَ مُبادَلَتِه — يُختَمُ ويُقالُ «حُفِظ» ويُرسَمُ «إنشاء بوليصَة» لا يَنجَح — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (ii)): `SaveTokenAsync` يَختِمُ الرَمزَ ويَحفَظُه أَوَّلاً (`ConnectAsync` الخُطوَة ١)، ثُمَّ يُسَجِّلُ الوارِدَ
// بِه عِندَ OTO — والتَسجيلُ يُبادِلُ الرَمزَ أَوَّلاً، فَرَمزٌ تَرُدُّه OTO عِندَ «Refresh Token» يُكتَبُ رَفضُه `oto_token_refused` في `WebhookRefusal`
// والمَدخَلُ فَعّالٌ مَختوم. فَتَقولُ الصَفحَةُ «حُفِظَ رَمزُ حِسابِ الشَّحن»، و`CarrierReadyForAsync` صادِقَةٌ فَتَرسُمُ كُلُّ صَفقَةٍ لِلبائِعِ «إنشاء بوليصَة»
// لا تَنجَحُ أَبَداً (القاعِدَة ١٢).
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» أَدناه.

public class VendorTokenRefusedTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";
    private const string Refused = "RT-refused-0f0f";

    /// <summary>سَطحُ اللَصقِ عَلى عالَمِ الشَحن — بيئَةٌ مَفتوحَةٌ بِلا رَمزٍ سوقِيّ.</summary>
    internal static VendorShippingService PasteSurface(DealShipTests.World w)
    {
        var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken });
        return new VendorShippingService(w.Providers, w.Vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store),
            new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
    }

    internal static ListingVendor Vendor() => ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!;

    internal static TenantProviderBinding? Entry(DealShipTests.World w)
        => w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, DealShipTests.Seller));

    /// <summary>OTO تَرُدُّ الرَمزَ عِندَ «Refresh Token» — ‏401 كَما تَرُدُّ رَمزاً لا تَعرِفُه.</summary>
    internal static void RefuseTheToken(DealShipTests.World w)
        => w.Oto.Routes[OtoClient.RefreshTokenPath] = _ => OtoRouteHandler.Json(HttpStatusCode.Unauthorized, "{\"success\":false}");

    [Fact]
    public async Task Today_a_pasted_token_OTO_refuses_at_the_exchange_is_sealed_saved_and_the_deal_page_draws_the_ship_form()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        RefuseTheToken(w);

        var res = await PasteSurface(w).SaveTokenAsync(Slug, Vendor(), Refused, Inbound);
        var entry = Entry(w);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        var ready = await w.Shipping.CarrierReadyForAsync(Slug, deal);
        output.WriteLine($"الحِفظ: {res.Code ?? "ok"} · المَدخَل: {entry?.Status ?? "-"} · الرَفضُ المَكتوب: {entry?.WebhookRefusal ?? "-"} · " +
                         $"«إنشاء بوليصَة»: {ready} · نِداءاتُ الرَمز: {w.Oto.CallsTo(OtoClient.RefreshTokenPath)}");

        Assert.True(res.Ok);
        Assert.Equal(TenantProviderBinding.StatusActive, entry!.Status);
        Assert.Equal(OtoFailures.TokenRefused, entry.WebhookRefusal);
        Assert.Equal(Refused, entry.Values[VendorShippingSurface.RefreshField!]
            .Unseal(w.Vault, VendorShippingSurface.AadFor(Slug, VendorShippingSurface.RefreshField!, DealShipTests.Seller)));
        Assert.True(ready);
    }
}
