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
// **قُلِبَ** (‏2026-10-13، كوميتُ «fix(shipping)» بَعدَ ‏1ffbcfd): الرَمزُ يُبادَلُ قَبلَ أَن يُختَم (`OtoClient.AccessTokenAsync` المُخَزَّنَة)، ورَمزٌ تَرُدُّه
// OTO يُجابُ `vendor_token_refused` بِنَصِّه لِلبائِعِ ولا يُحفَظُ شَيء؛ وما سِوى الرَدِّ — انقِطاعٌ — يُحفَظُ كَما كان.

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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13) — كانَ <c>Today_a_pasted_token_OTO_refuses_at_the_exchange_is_sealed_saved_and_the_deal_page_draws_the_ship_form</c>
    /// (‏<c>1ffbcfd</c>): «الحِفظ: ok · المَدخَل: active · الرَفضُ المَكتوب: oto_token_refused · «إنشاء بوليصَة»: True». والآن يُرَدُّ بِرَمزِه ولا يُكتَبُ
    /// مَدخَلٌ ولا يُسَجَّلُ وارِد — وجاهِزِيَّةُ الناقِلِ لِصَفقَتِه ما كانَت قَبلَ اللَصقِ (في مَتجَرِ العَرضِ احتِياطُ التَهيئَة).</summary>
    [Fact]
    public async Task A_pasted_token_OTO_refuses_at_the_exchange_is_refused_with_its_code_and_nothing_is_stored()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        RefuseTheToken(w);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        var readyBefore = await w.Shipping.CarrierReadyForAsync(Slug, deal);

        var res = await PasteSurface(w).SaveTokenAsync(Slug, Vendor(), Refused, Inbound);
        var ready = await w.Shipping.CarrierReadyForAsync(Slug, deal);
        output.WriteLine($"الحِفظ: {res.Code ?? "ok"} · المَدخَل: {Entry(w)?.Status ?? "-"} · كِتابَةُ مَداخِل: {w.Docs.Wrote<TenantProviderBinding>()} · " +
                         $"«إنشاء بوليصَة» قَبلُ/بَعدُ: {readyBefore}/{ready} · نِداءاتُ الرَمز: {w.Oto.CallsTo(OtoClient.RefreshTokenPath)} · " +
                         $"webhook: {w.Oto.CallsTo(OtoClient.WebhookPath)}");

        Assert.False(res.Ok);
        Assert.Equal(VendorShippingService.TokenRefused, res.Code);
        Assert.Null(res.After);
        Assert.Null(Entry(w));
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.RefreshTokenPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.WebhookPath));
        Assert.Equal(readyBefore, ready);
    }

    /// <summary><b>ومَدخَلٌ قائِمٌ لا يَمَسُّه لَصقٌ مَرفوض</b>: رَمزُه المَختومُ وسِرُّ وارِدِه ومُعَرِّفُه كَما كانَت — فَلا يُستَبدَلُ ناقِلٌ يَعمَلُ بِرَمزٍ لا يَعمَل.</summary>
    [Fact]
    public async Task A_refused_paste_leaves_an_existing_entry_exactly_as_it_was()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: true);
        var kept = Entry(w)!;
        var keptToken = kept.Values[VendorShippingSurface.RefreshField!].Cipher;
        RefuseTheToken(w);

        var res = await PasteSurface(w).SaveTokenAsync(Slug, Vendor(), Refused, Inbound);

        Assert.Equal(VendorShippingService.TokenRefused, res.Code);
        Assert.Same(kept, Entry(w));
        Assert.Equal(keptToken, Entry(w)!.Values[VendorShippingSurface.RefreshField!].Cipher);
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        Assert.True(await w.Shipping.CarrierReadyForAsync(Slug, (await w.Deals.LoadAsync(Slug, w.Id))!));
    }

    /// <summary><b>والسالِب</b>: ما سِوى رَدِّ الرَمزِ لا يَقولُ شَيئاً عَنه — ‏5xx عِندَ المُبادَلَةِ يُحفَظُ مَعَه الرَمزُ كَما كان، ويُكتَبُ فَشَلُ تَسجيلِ الوارِد؛
    /// ورَمزٌ مَقبولٌ يُبادَلُ مَرَّةً واحِدَة (المُبادَلَةُ مُخَزَّنَة) فَلا نِداءَ زائِد.</summary>
    [Fact]
    public async Task Only_a_refusal_refuses_an_unreachable_exchange_still_saves_and_an_accepted_token_is_exchanged_once()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Oto.Routes[OtoClient.RefreshTokenPath] = _ => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "<html/>");
        var down = await PasteSurface(w).SaveTokenAsync(Slug, Vendor(), Refused, Inbound);
        Assert.True(down.Ok, down.Code);
        Assert.Equal(OtoFailures.Unreachable, Entry(w)!.WebhookRefusal);

        var fresh = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var ok = await PasteSurface(fresh).SaveTokenAsync(Slug, Vendor(), "RT-accepted-1a1a", Inbound);
        Assert.True(ok.Ok, ok.Code);
        Assert.Equal(1, fresh.Oto.CallsTo(OtoClient.RefreshTokenPath));
        Assert.Equal(1, fresh.Oto.CallsTo(OtoClient.WebhookPath));
        Assert.Null(Entry(fresh)!.WebhookRefusal);
    }

    /// <summary>ولِلرَمزِ نَصٌّ لِلبائِعِ في القامُوس — مِفتاحُ صَفحَةِ «إعلاناتي» (<c>shipping.vendor.err_{code}</c>) لا نَصُّ «اعتِمادِ المَتجَر».</summary>
    [Fact]
    public void The_refusal_has_its_own_vendor_text_in_the_dictionary()
    {
        var key = "shipping.vendor.err_" + VendorShippingService.TokenRefused;
        var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key);
        output.WriteLine(text);
        Assert.NotEqual(key, text);
        Assert.Contains("لَصَقتَه", text, StringComparison.Ordinal);
        Assert.Contains(VendorShippingService.TokenRefused, VendorShippingService.Codes);
        Assert.NotEqual(text, ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "shipping.vendor.err_" + OtoFailures.TokenRefused));
    }
}
