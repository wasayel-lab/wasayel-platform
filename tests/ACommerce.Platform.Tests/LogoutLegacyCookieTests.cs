using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ عَلى HTTPS يَمحو الاسمَ القَديمَ مَعَ __Host- — فَالتَراجُعُ لا يُعيدُ جَلسَةً خَرَجَت (‏2026-11-11، W13-4·C2z·6 — G8؛ ADR-050 §١٥) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِعين): ‏`POST /auth/logout` عَلى HTTPS بِالاسمَين ← `Set-Cookie` يَمحو `__Host-.acommerce.auth.{store}` و`.name` وَحدَهُما، والاسمُ
// القَديمُ «200 in» عَلى طَلَبٍ لا يَرى HTTPS (البِناءُ السابِق)؛ و`POST /studio/logout` مِثلُه. **والآن** الأَسماءُ القَديمَةُ تُمحى مَعَها عَلى HTTPS؛ وعَلى HTTP القائِمَةُ
// بِحُروفِها (`LogoutLegacyCookieCharacterizationTests` بِبايتاتِه).

public class LogoutLegacyCookieTests(ITestOutputHelper output)
{
    private static List<string> Deleted(Action<HttpResponse> clear)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString("ashare.example.test");
        clear(ctx.Response);
        return ctx.Response.Headers.SetCookie.Select(c => c!).ToList();
    }

    [Fact]
    public void On_https_the_store_logout_deletes_the_legacy_names_too()
    {
        var got = Deleted(r => AuthSession.ClearAllCookiesForTenant(r, "ashare", ["vendor"]));
        output.WriteLine(string.Join("\n", got));
        Assert.Equal(8, got.Count);
        foreach (var name in new[] { ".acommerce.auth.ashare", ".acommerce.auth.ashare.name", ".acommerce.auth.ashare.vendor", ".acommerce.auth.ashare.vendor.name" })
        {
            var c = Assert.Single(got, x => x.StartsWith(name + "=", StringComparison.Ordinal));
            Assert.Contains("expires=Thu, 01 Jan 1970", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", c, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void On_https_the_studio_logout_deletes_the_legacy_names_too()
    {
        var got = Deleted(StudioAuth.DeleteCookie);
        output.WriteLine(string.Join("\n", got));
        Assert.Equal(["__Host-.acommerce.studio", "__Host-.acommerce.studio.name", ".acommerce.studio", ".acommerce.studio.name"], got.Select(c => c.Split('=', 2)[0]));
    }

    [Fact]
    public void The_one_door_that_revokes_every_device_also_clears_both_names()
    {
        // ‏«اخرج من كل الأجهزة» يَمُرُّ بِـClearAllCookiesForTenant نَفسِها — فَيَمحو الاسمَينِ عَلى HTTPS
        var got = Deleted(r => AuthSession.ClearAllCookiesForTenant(r, "ashare", Array.Empty<string>()));
        Assert.Equal(["__Host-.acommerce.auth.ashare", "__Host-.acommerce.auth.ashare.name", ".acommerce.auth.ashare", ".acommerce.auth.ashare.name"],
            got.Select(c => c.Split('=', 2)[0]));
    }
}
