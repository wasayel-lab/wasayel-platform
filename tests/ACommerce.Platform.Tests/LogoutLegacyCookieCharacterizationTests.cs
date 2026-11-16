using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَمحُوَ الخُروجُ عَلى HTTPS الاسمَ القَديمَ أَيضاً (‏2026-11-11، W13-4·C2z·6 — مُراجَعَةُ C2c · C2c-P2-02 وSEC-C2c-R3-3) ═══
//
// **ما سَيَتَغَيَّر**: الخُروجُ عَلى HTTPS يَمحو أَسماءَ `__Host-` وَحدَها، فَكَعكَةُ ما قَبلَ النَشرِ بِالاسمِ القَديمِ (رَمزٌ صالِحٌ ثَلاثينَ يَوماً) تَبقى — ويَقرَؤُها البِناءُ
// السابِقُ إن تُراجِعَ إلَيه، فَمَن خَرَجَ عَلى جِهازٍ مُشتَرَكٍ يَعودُ داخِلاً. سَيُمحى الاسمُ القَديمُ مَعَه عَلى HTTPS. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى
// بايتاتِه (القاعِدَة ٣): عَلى HTTP قائِمَةُ `Set-Cookie` بِحُروفِها، وعَلى HTTPS مَحوُ أَسماءِ `__Host-` بِشُروطِها باقٍ.

public class LogoutLegacyCookieCharacterizationTests(ITestOutputHelper output)
{
    private static List<string> Deleted(bool https, Action<HttpResponse> clear)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = https ? "https" : "http";
        ctx.Request.Host = new HostString("ashare.example.test");
        clear(ctx.Response);
        return ctx.Response.Headers.SetCookie.Select(c => c!).ToList();
    }

    [Fact]
    public void On_http_the_store_logout_deletes_exactly_what_it_deleted()
    {
        var got = Deleted(false, r => AuthSession.ClearAllCookiesForTenant(r, "ashare", ["vendor"]));
        output.WriteLine(string.Join("\n", got));
        Assert.Equal(4, got.Count);
        Assert.Equal([".acommerce.auth.ashare", ".acommerce.auth.ashare.name", ".acommerce.auth.ashare.vendor", ".acommerce.auth.ashare.vendor.name"],
            got.Select(c => c.Split('=', 2)[0]));
        Assert.All(got, c => Assert.Contains("expires=Thu, 01 Jan 1970", c, StringComparison.OrdinalIgnoreCase));
        Assert.All(got, c => Assert.DoesNotContain("secure", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void On_http_the_studio_logout_deletes_exactly_what_it_deleted()
    {
        var got = Deleted(false, StudioAuth.DeleteCookie);
        Assert.Equal([".acommerce.studio", ".acommerce.studio.name"], got.Select(c => c.Split('=', 2)[0]));
    }

    [Fact]
    public void On_https_the_host_prefixed_names_are_still_deleted_with_their_conditions()
    {
        var store = Deleted(true, r => AuthSession.ClearAllCookiesForTenant(r, "ashare", ["vendor"]));
        var studio = Deleted(true, StudioAuth.DeleteCookie);
        output.WriteLine(string.Join("\n", store.Concat(studio)));
        foreach (var name in new[] { "__Host-.acommerce.auth.ashare", "__Host-.acommerce.auth.ashare.name", "__Host-.acommerce.auth.ashare.vendor", "__Host-.acommerce.auth.ashare.vendor.name" })
        {
            var c = Assert.Single(store, x => x.StartsWith(name + "=", StringComparison.Ordinal));
            Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", c, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Single(studio, x => x.StartsWith("__Host-.acommerce.studio=", StringComparison.Ordinal));
        Assert.Single(studio, x => x.StartsWith("__Host-.acommerce.studio.name=", StringComparison.Ordinal));
    }
}
