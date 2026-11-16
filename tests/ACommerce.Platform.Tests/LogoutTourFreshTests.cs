using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ جَولَةُ الخُروجِ تَقرَأُ وَثيقَةَ المَتجَرِ طازَجَةً كَبابِ مُضيفِ الدَور (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · C2z-PATHS-R1-02؛ ADR-050 §١٤) ═══
//
// **المَقيسُ قَبل** (عِندي، BaseDomain=localhost): الذاكِرَةُ دافِئَةٌ بِثَلاثَةِ أَدوار، و`customer` يُزالُ بِلا إخلاء، ثُمَّ POST الخُروجِ عَلى مُضيفِ المَتجَر ← 302 إلى
// `{store}-customer…/auth/logout/continue` ← 404، وأَربَعُ كَعكاتٍ باقِيَةٌ عَلى مُضيفِ البائِع. **والآن** بابُ الخُروجِ ومَحَطَّتُه (`IsLogoutTour`) يَقرَآنِ طازَجاً
// مَتى هُيِّئَ النِطاق: مُضيفاتُ الجَولَةِ عَلى مُضيفِ المَتجَرِ وعَلى الجَذرِ بِلا الدَورِ المُزال؛ والصَفَحاتُ تَقرَأُ الذاكِرَةَ كَما كانَت (التَوصيفُ بِبايتاتِه).

public class LogoutTourFreshTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("/ejar/auth/logout", true)]
    [InlineData("/ejar/auth/logout/continue", true)]
    [InlineData("/EJAR/Auth/Logout", true)]
    [InlineData("/ejar/auth/logout/", true)]
    [InlineData("/ejar/auth/login", false)]
    [InlineData("/ejar/auth/logout/continue/x", false)]
    [InlineData("/ashare/auth/logout", false)]
    [InlineData("/auth/logout", false)]
    [InlineData("/ejar/r/vendor/auth/logout", false)]
    [InlineData("/ejar/explore", false)]
    [InlineData(null, false)]
    public void The_logout_door_and_its_hop_are_named_by_their_segments(string? path, bool tour)
        => Assert.Equal(tour, TenantResolverMiddleware.IsLogoutTour(path, "ejar"));

    /// <summary>والمَسارانِ هُما مَسارا القالِب — بابُ الخُروجِ ونُقطَةُ المُرورِ بِذَيلِها في <see cref="LogoutHops.ContinueTail"/> — فَلا يَنجَرِفُ الاسمُ عَن التَوجيه.</summary>
    [Fact]
    public void The_named_paths_are_the_templates_routes()
    {
        var src = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs"));
        Assert.Contains("app.MapPost(\"/{slug}/auth/logout\",", src, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/{slug}/auth/logout/continue\",", src, StringComparison.Ordinal);
        Assert.True(TenantResolverMiddleware.IsLogoutTour($"/ejar/{LogoutHops.ContinueTail}", "ejar"));
        Assert.True(TenantResolverMiddleware.IsLogoutTour("/ejar/auth/logout", "ejar"));
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveLogoutTourFreshProofTests(ITestOutputHelper output)
{
    [Fact]
    public async Task The_logout_tour_drops_a_role_removed_under_the_warm_cache_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-tourf-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var probe = new TourProbe(host.Services.GetRequiredService<IDocumentStore>(), slug);
        await probe.SaveRolesAsync("customer", "vendor", "tenant_admin");
        try
        {
            var warm = await probe.HitAsync(slug, "GET", $"/{slug}/explore", host.Services);
            await probe.SaveRolesAsync("vendor", "tenant_admin");   // بِلا إخلاءٍ لِلذاكِرَة
            var onStore = await probe.HitAsync(slug, "POST", $"/{slug}/auth/logout", host.Services);
            await probe.SaveRolesAsync("tenant_admin");
            var onApex = await probe.HitAsync("", "POST", $"/{slug}/auth/logout", host.Services);
            await probe.SaveRolesAsync("vendor", "tenant_admin");
            var hop = await probe.HitAsync(slug, "GET", $"/{slug}/{LogoutHops.ContinueTail}", host.Services);
            output.WriteLine($"دافِئ {warm} · خُروجٌ عَلى المَتجَر {onStore} · خُروجٌ عَلى الجَذر {onApex} · مَحَطَّةٌ عَلى المَتجَر {hop}");

            Assert.Equal((200, "customer,tenant_admin,vendor"), warm);
            Assert.Equal((200, "tenant_admin,vendor"), onStore);
            Assert.Equal((200, "tenant_admin"), onApex);
            Assert.Equal((200, "tenant_admin,vendor"), hop);
        }
        finally
        {
            await using var s = host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
            s.Delete<Tenant>(slug);
            await s.SaveChangesAsync();
        }
    }
}
