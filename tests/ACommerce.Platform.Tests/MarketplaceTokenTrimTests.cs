using ACommerce.Kit.Delivery;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ `Shipping__Oto__MarketplaceToken` بِسَطرٍ جَديدٍ في آخِرِه — يُعَدُّ مَضبوطاً ويُرسَلُ بِسَطرِه — تَوصيفُ «اليَوم» ═══════════════════
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 (xiii)): رَمزُ تَحديثِ التَهيئَةِ يُقَصُّ حَيثُ يُقرَأ (`DealShipping.VendorTokenFor`: `.Trim()`)، والرَمزُ السوقِيُّ
// لا يُقَصّ: `MarketplaceTokenConfigured` تَسأَلُ `IsNullOrWhiteSpace` فَقِيمَةٌ بِسَطرٍ جَديدٍ (يُلصَقُ في أَداةِ أَسرارٍ كَثيراً) «مَضبوطَة»، و`RegisterAsync`
// تُرسِلُها كَما هي إلى «Refresh Token» — فَتَرُدُّها OTO (`oto_token_refused`) عِندَ كُلِّ تَسجيل. يُقاسُ هُنا ما يُرسَلُ في جِسمِ المُبادَلَة.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: صَفُّ «Today_» وَحدَه؛ وصَفُّ القيمَةِ البَيضاءِ يَبقى بِحَرفِه.

public class MarketplaceTokenTrimTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    internal static VendorShippingService Surface(DealShipTests.World w, string marketplaceToken)
    {
        var options = Options.Create(new OtoOptions
        {
            Environment = OtoEnvironment.Staging, RefreshToken = DealShipTests.ConfigToken, MarketplaceToken = marketplaceToken,
        });
        return new VendorShippingService(w.Providers, w.Vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store),
            new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
    }

    /// <summary>جِسمُ أَوَّلِ مُبادَلَةٍ لِلرَمزِ أُرسِلَت — رَمزُ التَسجيلِ هُوَ أَوَّلُ ما يُبادَلُ في <c>RegisterAsync</c>.</summary>
    internal static string FirstExchangeBody(DealShipTests.World w)
        => w.Oto.Calls.First(c => c.Path == OtoClient.RefreshTokenPath).Body ?? "";

    [Fact]
    public async Task Today_a_marketplace_token_with_a_trailing_newline_counts_as_configured_and_is_sent_with_it()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w, "MKT-token\n");

        var input = surface.Input;
        var res = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(Guid.NewGuid()), VendorShippingTests.Form(), Inbound);
        var body = FirstExchangeBody(w);
        output.WriteLine($"المَدخَل: {input} · التَسجيل: {res.Code ?? "ok"} · جِسمُ المُبادَلَة: {body.Replace("\n", "⏎", StringComparison.Ordinal)}");

        Assert.Equal(VendorShippingInput.Register, input);
        Assert.Contains("\"MKT-token\\n\"", body, StringComparison.Ordinal);
    }

    /// <summary>وقيمَةٌ بَيضاءُ كُلُّها لَيسَت رَمزاً — المَدخَلُ لَصق. يَبقى بِحَرفِه بَعدَ الإصلاح.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\n")]
    [InlineData(" \t\r\n")]
    public async Task A_blank_marketplace_token_is_not_configured_and_the_paste_stays(string blank)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.Equal(VendorShippingInput.Paste, Surface(w, blank).Input);
        Assert.False(VendorShippingSurface.MarketplaceTokenConfigured(new OtoOptions { MarketplaceToken = blank }));
    }
}
