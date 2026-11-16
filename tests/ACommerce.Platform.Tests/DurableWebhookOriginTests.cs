using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G14 — الخَطّافاتُ المُستَديمَةُ بِأَصلِ المَنَصَّة (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·3) ═══
//
// **المَقيسُ قَبل** (عِندَ c8dd471، A2 §3.3): المَواضِعُ الثَلاثَةُ تُعطي `NoticeUrl`/`InboundUrl` أَصلَ الطَلَبِ (`OriginFrom(req)`)، فَرَبطٌ مِن `ashare.R` يُسَجِّلُ
// `https://ashare.R/ashare/payments/notify` عِندَ Moyasar. **والآن** `StoreUrl.PlatformOrigin`: `{scheme}://R{port}` مَتى هُيِّئَ النِطاق، و`OriginFrom` بِحَرفِه
// بِدونِه. وتَوصيفُ ما يَنجو `DurableWebhookOriginCharacterizationTests` بِبَصمَتِه.

public class DurableWebhookOriginTests(ITestOutputHelper output)
{
    private static HttpRequest Request(string scheme, string host, string? apex, string? hostSlug)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        if (apex is not null) ctx.Items[StoreHostItems.Apex] = apex;
        if (hostSlug is not null) ctx.Items[StoreHostItems.Slug] = hostSlug;
        return ctx.Request;
    }

    [Theory]
    [InlineData("https", "ashare.example.test", "example.test", "ashare", "https://example.test")]
    [InlineData("https", "example.test", "example.test", null, "https://example.test")]
    [InlineData("http", "ashare.localhost:5050", "localhost", "ashare", "http://localhost:5050")]
    [InlineData("https", "ashare-vendor.example.test", "example.test", null, "https://example.test")]
    // بِلا نِطاقٍ مُهَيَّإٍ — أَصلُ الطَلَبِ كَما كان
    [InlineData("http", "localhost:5050", null, null, "http://localhost:5050")]
    [InlineData("https", "wasayel-app.hf.space", null, null, "https://wasayel-app.hf.space")]
    public void The_platform_origin_is_the_apex_when_the_domain_is_set(string scheme, string host, string? apex, string? hostSlug, string expected)
    {
        var req = Request(scheme, host, apex, hostSlug);
        var origin = StoreUrl.PlatformOrigin(req);
        output.WriteLine($"{scheme}://{host} (R={apex ?? "—"}) ⇒ {origin} · {GatewayPayments.NoticeUrl(origin, "ashare")} · {VendorShippingSurface.InboundUrl(origin, "ashare")}");
        Assert.Equal(expected, origin);
    }

    /// <summary><b>الحارِس</b> (القاعِدَة ٢): كُلُّ مَوضِعٍ يُعطي <c>NoticeUrl</c>/<c>InboundUrl</c>/<c>ConnectNoticesAsync</c> أَصلاً في القالِبِ يُعطيه
    /// <c>StoreUrl.PlatformOrigin(req)</c> — لا <c>OriginFrom(req)</c>. يَطبَعُ عَدَدَ المَواضِع، ويَحمَرُّ إن كانَ صِفراً.</summary>
    [Fact]
    public void Durable_webhook_urls_use_the_platform_origin()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlatformV1.slnx"))) d = d.Parent;
        var root = Path.Combine(d!.FullName, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var call = new Regex(@"(?:VendorShippingSurface\.InboundUrl|GatewayPayments\.NoticeUrl|\.ConnectNoticesAsync)\((?<args>[^;]*)");
        var sites = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
            .Where(x => !x.l.TrimStart().StartsWith("//") && !x.l.TrimStart().StartsWith("///"))
            .Select(x => (Where: $"{Path.GetFileName(x.f)}:{x.i + 1}", M: call.Match(x.l))).Where(x => x.M.Success).ToList();
        var fromRequest = sites.Where(x => x.M.Groups["args"].Value.Contains("OriginFrom(", StringComparison.Ordinal)).ToList();
        var fromPlatform = sites.Where(x => x.M.Groups["args"].Value.Contains("StoreUrl.PlatformOrigin(req)", StringComparison.Ordinal)).ToList();
        output.WriteLine($"مَواضِعُ الخَطّافاتِ المُستَديمَة: {sites.Count} · بِأَصلِ المَنَصَّة: {fromPlatform.Count} · بِأَصلِ الطَلَب: {fromRequest.Count}");
        foreach (var x in sites) output.WriteLine($"  {x.Where}");
        Assert.Equal(3, fromPlatform.Count);
        Assert.Empty(fromRequest);
    }
}
