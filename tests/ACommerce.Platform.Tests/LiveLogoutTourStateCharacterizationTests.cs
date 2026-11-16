using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تَقرَأَ جَولَةُ الخُروجِ وَثيقَةَ المَتجَرِ طازَجَة (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · C2z-PATHS-R1-02) ═══
//
// **السَبَبُ القادِم**: مُنذُ 7337b74 يَقرَأُ مُضيفُ الدَورِ وَثيقَةَ المَتجَرِ طازَجَةً (404 لِدَورٍ أُزيل)، وجَولَةُ الخُروجِ (`StoreHostItems.HostedRoles`) عَلى مُضيفِ المَتجَرِ
// والجَذرِ تُبنى مِن ذاكِرَةِ الخَمسِ دَقائِق — فَدَورٌ أُزيلَ تَحتَها يَبقى مَحَطَّةً يُجيبُ مُضيفُها 404، فَتَنقَطِعُ السِلسِلَةُ وتَبقى جَرّاتُ ما بَعدَه داخِلَة (مَقيسٌ عِندَ
// المُراجِعِ وعِندي). سَتُقرَأُ الوَثيقَةُ طازَجَةً عَلى بابِ الخُروجِ ومَحَطَّتِه. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — الوَسيطُ الحَقيقيُّ بِذاكِرَةٍ دافِئَةٍ عَلى
// Postgres، والوَثيقَةُ تَتَغَيَّرُ تَحتَه بِلا إخلاء: صَفحَةٌ عَلى مُضيفِ المَتجَرِ وعَلى الجَذرِ تَقرَأُ الذاكِرَة (مُضيفاتُها كَما كانَت)، ومُضيفُ الدَورِ المُزالِ 404، ومُضيفُ
// دَورٍ باقٍ يَقرَأُ طازَجاً ويُجَدِّدُ الذاكِرَةَ لِمَن بَعدَه. ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣).

internal sealed class TourProbe(IDocumentStore store, string slug)
{
    private readonly TenantResolverMiddleware _mw = new(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }, store,
        new MemoryCache(new MemoryCacheOptions()), new SubdomainTenancyOptions { BaseDomain = "example.test" });

    public async Task SaveRolesAsync(params string[] roles)
    {
        await using var s = store.LightweightSession();
        s.Store(new Tenant { Id = slug, Name = "جَولَةُ الخُروج", Roles = roles.Select((r, i) => RoleCatalog.InstantiateRole(RoleCatalog.Find(r)!, i)).ToList() });
        await s.SaveChangesAsync();
    }

    /// <summary>طَلَبٌ كَما يَترُكُه الحاقِن (<paramref name="label"/> فارِغٌ = الجَذر) — يُعيدُ الحالَةَ ومُضيفاتِ المَتجَرِ الَّتي كَتَبَها الوَسيطُ لِلجَولَة.</summary>
    public async Task<(int Status, string Hosted)> HitAsync(string label, string method, string path, IServiceProvider services)
    {
        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.Request.Method = method;
        ctx.Request.Host = new HostString(label.Length == 0 ? "example.test" : $"{label}.example.test");
        ctx.Request.Path = path;
        await _mw.InvokeAsync(ctx);
        var hosted = ctx.Items[StoreHostItems.HostedRoles] is IReadOnlyList<string> h ? string.Join(",", h) : "—";
        return (ctx.Response.StatusCode, hosted);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveLogoutTourStateCharacterizationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Pages_read_the_warm_cache_and_role_hosts_read_fresh_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-tour-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var probe = new TourProbe(host.Services.GetRequiredService<IDocumentStore>(), slug);
        await probe.SaveRolesAsync("customer", "vendor", "tenant_admin");
        try
        {
            var warm = await probe.HitAsync(slug, "GET", $"/{slug}/explore", host.Services);
            await probe.SaveRolesAsync("vendor", "tenant_admin");   // بِلا إخلاءٍ لِلذاكِرَة — كَما يَكتُبُ أَيُّ كاتِبٍ لِلأَدوار
            var storePage = await probe.HitAsync(slug, "GET", $"/{slug}/explore", host.Services);
            var apexPage = await probe.HitAsync("", "GET", $"/{slug}/explore", host.Services);
            var removedHost = await probe.HitAsync($"{slug}-customer", "GET", $"/{slug}/r/customer", host.Services);
            var keptHost = await probe.HitAsync($"{slug}-vendor", "GET", $"/{slug}/r/vendor", host.Services);
            var storeAfter = await probe.HitAsync(slug, "GET", $"/{slug}/explore", host.Services);
            output.WriteLine($"دافِئ {warm} · بَعدَ الإزالَة: صَفحَةُ المَتجَر {storePage} · صَفحَةُ الجَذر {apexPage} · مُضيفُ customer {removedHost} · مُضيفُ vendor {keptHost} · صَفحَةُ المَتجَرِ بَعدَه {storeAfter}");

            Assert.Equal((200, "customer,tenant_admin,vendor"), warm);
            Assert.Equal((200, "customer,tenant_admin,vendor"), storePage);
            Assert.Equal((200, "customer,tenant_admin,vendor"), apexPage);
            Assert.Equal(404, removedHost.Status);
            Assert.Equal((200, "tenant_admin,vendor"), keptHost);
            Assert.Equal((200, "tenant_admin,vendor"), storeAfter);
        }
        finally
        {
            await using var s = host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
            s.Delete<Tenant>(slug);
            await s.SaveChangesAsync();
        }
    }
}
