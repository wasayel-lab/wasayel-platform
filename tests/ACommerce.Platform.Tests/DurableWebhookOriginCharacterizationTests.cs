using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ أَصلِ الخَطّافاتِ المُستَديمَةِ لِما يَنجو قَبلَ أَن يَصيرَ أَصلَ المَنَصَّة (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·3 — القاعِدَة ٣) ═══
//
// **ما يَتَغَيَّرُ عَمداً بَعدَ هذا المِلَفّ** (G14): عُنوانا Moyasar وOTO اللَّذانِ يُسَجَّلانِ عِندَ المُزَوِّدِ ويَبقَيانِ هُناك (‏`NoticeUrl` في `ConnectNoticesAsync`
// و`InboundUrl` في بابَي ربطِ OTO) يُبنَيانِ مِن `OriginFrom(req)` — أَصلِ الطَلَبِ أَيّاً كان — فَبائِعٌ رَبَطَ مِن مُضيفِ مَتجَرٍ يُسَجِّلُ ذلكَ المُضيف،
// ومُضيفٌ يَتَغَيَّرُ اسمُه يَقطَعُ الخَطّافَ صامِتاً (A2 §3.3). **وما يَنجو ويُثَبَّتُ هُنا**: `OriginFrom` نَفسُها (يَسأَلُها غَيرُهُما — عَودَةُ PayPal وMoyasar
// المُتَصَفِّحِيَّة)، وتَركيبُ العُنوانَينِ مِن أَصلٍ مُعطى، و**أَنّ مَسارَيهِما عَلى الجَذرِ لا يُحَوَّلانِ** (لا مَتجَرَ ولا مَساس) وعَلى مُضيفِ المَتجَرِ يُخدَمانِ
// بِمَسارِهِما الحامِلِ لِلمَتجَر. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ التَبديل.

public class DurableWebhookOriginCharacterizationTests(ITestOutputHelper output)
{
    private static HttpRequest Request(string scheme, string host)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        return ctx.Request;
    }

    [Theory]
    [InlineData("http", "localhost:5050", "http://localhost:5050")]
    [InlineData("https", "example.test", "https://example.test")]
    [InlineData("https", "ashare.example.test", "https://ashare.example.test")]
    [InlineData("https", "wasayel-app.hf.space", "https://wasayel-app.hf.space")]
    public void The_request_origin_is_scheme_and_host(string scheme, string host, string expected)
    {
        var got = PayPalOrderSurface.OriginFrom(Request(scheme, host));
        output.WriteLine($"{scheme}://{host} ⇒ {got}");
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("https://example.test", "ashare", "https://example.test/ashare/payments/notify", "https://example.test/ashare/shipping/oto/order-status")]
    [InlineData("https://example.test/", "theme-demo", "https://example.test/theme-demo/payments/notify", "https://example.test/theme-demo/shipping/oto/order-status")]
    [InlineData("http://localhost:5050", "rewalk-071301", "http://localhost:5050/rewalk-071301/payments/notify", "http://localhost:5050/rewalk-071301/shipping/oto/order-status")]
    public void The_durable_urls_are_the_origin_and_the_literal_path(string origin, string slug, string notice, string inbound)
    {
        var (n, i) = (GatewayPayments.NoticeUrl(origin, slug), VendorShippingSurface.InboundUrl(origin, slug));
        output.WriteLine($"{origin} · {slug} ⇒ {n} · {i}");
        Assert.Equal((notice, inbound), (n, i));
    }

    /// <summary>مَسارا الخَطّافَينِ عَلى الجَذرِ لا يُمَسّانِ (ولا تَحويلَ لِلمَسارِ عَلى الجَذرِ قَبلَ المَرحَلَةِ الخامِسَة)، وعَلى مُضيفِ المَتجَرِ يُخدَمانِ بِمَسارِهِما.</summary>
    [Theory]
    [InlineData("example.test", "POST", "/ashare/payments/notify", "Pass||")]
    [InlineData("example.test", "POST", "/ashare/shipping/oto/order-status", "Pass||")]
    [InlineData("example.test", "GET", "/ashare/payments/notify", "Pass||")]
    [InlineData("ashare.example.test", "POST", "/ashare/payments/notify", "Pass|ashare|")]
    [InlineData("ashare.example.test", "POST", "/ashare/shipping/oto/order-status", "Pass|ashare|")]
    public void The_durable_paths_are_never_redirected(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, "example.test", method, path, "", "https");
        var shown = $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}";
        output.WriteLine($"{method} {host}{path} ⇒ {shown}");
        Assert.Equal(expected, shown);
    }
}
