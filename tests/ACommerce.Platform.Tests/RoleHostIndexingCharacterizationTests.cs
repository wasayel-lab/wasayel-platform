using ACommerce.Kit.Tenants;
using ACommerce.Kit.Tenants.Server;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَفتَحَ robots.txt مُضيفِ الدَورِ لِلزاحِف (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·5 — C2z-PATHS-R1-05) ═══
//
// `7337b74` جَعَلَ robots مُضيفِ الدَورِ `Disallow: /` — والزاحِفُ المَمنوعُ لا يَرى `X-Robots-Tag: noindex` فَيُفَهرِسُ الرابِطَ عُنواناً بِلا صَفحَة. وما لا يَتَغَيَّرُ
// بِفَتحِه — ويَبقى هذا المِلَفُّ بِبَصمَتِه: (١) كُلُّ جَوابٍ عَلى مُضيفِ الدَورِ بِـ`X-Robots-Tag: noindex`، ولا جَوابَ عَلى مُضيفِ المَتجَرِ أَو الجَذرِ بِه؛
// (٢) خَريطَةُ مُضيفِ الدَورِ فارِغَة، وrobots مُضيفِ الدَورِ بِلا `Sitemap:`؛ (٣) robots مُضيفِ المَتجَرِ والجَذرِ بِحَرفِه (`BuildRobotsTxt`).

public class RoleHostIndexingCharacterizationTests
{
    [Theory]
    [InlineData("ashare-vendor.example.test", "/", "noindex")]
    [InlineData("ashare-vendor.example.test", "/robots.txt", "noindex")]
    [InlineData("ashare-customer.example.test", "/explore", "noindex")]
    [InlineData("ashare.example.test", "/", "")]
    [InlineData("ashare.example.test", "/robots.txt", "")]
    [InlineData("example.test", "/", "")]
    public async Task Noindex_rides_every_role_host_answer_and_no_other(string host, string path, string robots)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        var mw = new SubdomainTenantPathMiddleware(_ => Task.CompletedTask, new SubdomainTenancyOptions { BaseDomain = "example.test" },
            new ServiceCollection().BuildServiceProvider());
        await mw.InvokeAsync(ctx);
        Assert.Equal(robots, ctx.Response.Headers["X-Robots-Tag"].ToString());
    }

    private static async Task<string> RobotsAsync(string? role)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(role is null ? "ashare.example.test" : $"ashare-{role}.example.test");
        if (role is not null) ctx.Items[StoreHostItems.Role] = role;
        ctx.Response.Body = new MemoryStream();
        await SeoHandlers.Robots(ctx);
        ctx.Response.Body.Position = 0;
        return await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    }

    [Fact]
    public async Task The_role_host_robots_names_no_sitemap_and_its_sitemap_is_empty()
    {
        var robots = await RobotsAsync("vendor");
        Assert.StartsWith("User-agent: *\n", robots, StringComparison.Ordinal);
        Assert.DoesNotContain("Sitemap:", robots, StringComparison.Ordinal);
        var empty = SeoDocuments.BuildSitemapXml([]);
        Assert.DoesNotContain("<loc>", empty, StringComparison.Ordinal);
        Assert.Contains("urlset", empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_store_host_robots_is_what_it_was()
        => Assert.Equal(SeoDocuments.BuildRobotsTxt("https://ashare.example.test"), await RobotsAsync(null));
}
