using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُقرَأَ مُخَطَّطُ الطَلَبِ مِن {http, https} وَحدَهُما في عَناوينِ المُضيفاتِ المُطلَقَة (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4
//     الجَولَةُ الثانِيَة · SEC-C2z-R2-1) ═══
//
// **ما سَيَتَغَيَّر**: `StoreUrl.ViewOf` يَنسَخُ `Request.Scheme` بِحَرفِه — و`X-Forwarded-Proto` يُصَدَّقُ مِن أَيِّ نِدٍّ (`KnownProxies` فارِغ) — فَتَحويلاتُ جَولَةِ
// الخُروجِ (`LogoutHops`) ورابِطُ «المَتجَرُ عَلى مُضيفِه» (`AdminHostSignIn.StoreHome`) والرابِطُ العابِرُ (`StoreUrl.App`) و«افتَح المَتجَر» (`StoreUrl.Open`) وأَصلُ
// المَنَصَّة (`StoreUrl.PlatformOrigin`) تَكتُبُ `javascript://…` لِطَلَبٍ يَحمِلُه (مَقيسٌ حَيّاً عِندَ المُراجِع: `302 Location: javascript://{slug}-customer.localhost…`).
// **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣): المُخَطَّطُ `http` و`https` بِحَرفِهِما، والفارِغُ `https`، في كُلِّ عُنوانٍ مُطلَقٍ مِن الخَمسَة؛
// ووَضعُ المَسار (بِلا نِطاقٍ مُهَيَّإ) بِحَرفِه.

public class HostSchemeCharacterizationTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CustomerVendor = ["vendor", "customer"];

    /// <summary>طَلَبٌ كَما يَترُكُه الحاقِنُ والوَسيط: عَلى الجَذرِ (<paramref name="onStore"/> <c>false</c>)، أَو عَلى مُضيفِ المَتجَر، أَو عَلى مُضيفِ دَورِه.</summary>
    private static DefaultHttpContext Request(string scheme, bool onStore, string? hostRole = null, string port = "", bool apexServes = false,
        string? cookieToken = null, string? ticket = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        var name = !onStore ? Apex : hostRole is null ? $"ashare.{Apex}" : $"ashare-{RoleHostNames.ByCatalogSlug[hostRole]}.{Apex}";
        ctx.Request.Host = new HostString(name + port);
        ctx.Items[StoreHostItems.Apex] = Apex;
        if (onStore) ctx.Items[StoreHostItems.Slug] = "ashare";
        if (hostRole is not null) ctx.Items[StoreHostItems.Role] = hostRole;
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        if (apexServes) ctx.Items[StoreHostItems.ApexServesStore] = true;
        if (cookieToken is not null) ctx.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(ctx.Request, "ashare")}={cookieToken}";
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        return ctx;
    }

    [Theory]
    [InlineData("http", "http")]
    [InlineData("https", "https")]
    [InlineData("", "https")]
    public void The_host_view_keeps_http_and_https_and_reads_none_as_https(string scheme, string want)
    {
        var view = StoreUrl.ViewOf(Request(scheme, onStore: true));
        output.WriteLine($"«{scheme}» ⇒ {view.Scheme}");
        Assert.Equal(want, view.Scheme);
    }

    [Theory]
    [InlineData("http", "http")]
    [InlineData("https", "https")]
    [InlineData("", "https")]
    public void The_logout_tour_hops_carry_http_and_https_as_they_are(string scheme, string want)
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);

        var begun = LogoutHops.Begin(Request(scheme, onStore: true), "ashare", token, Now);
        output.WriteLine($"«{scheme}» البِدايَة ⇒ {begun}");
        Assert.StartsWith($"{want}://ashare-customer.example.test/ashare/auth/logout/continue?t=", begun);

        var fromStore = AuthHandlers.MakeLogoutHop(user, "ashare", LogoutHops.StoreHost, Now);
        var (clear, last) = LogoutHops.Continue(Request(scheme, onStore: true, "vendor", cookieToken: token, ticket: fromStore), "ashare", Now);
        Assert.True(clear);
        Assert.Equal($"{want}://ashare.example.test/", last);

        var onApex = LogoutHops.Begin(Request(scheme, onStore: false, apexServes: true), "ashare", token, Now);
        Assert.StartsWith($"{want}://ashare.example.test/ashare/auth/logout/continue?t=", onApex);

        var fromApex = AuthHandlers.MakeLogoutHop(user, "ashare", LogoutHops.Apex, Now);
        var (_, toApex) = LogoutHops.Continue(Request(scheme, onStore: true, "vendor", apexServes: true, cookieToken: token, ticket: fromApex), "ashare", Now);
        output.WriteLine($"«{scheme}» آخِرُ جَولَةٍ بَدَأَت عَلى الجَذر ⇒ {toApex}");
        Assert.Equal($"{want}://example.test/ashare", toApex);
    }

    [Theory]
    [InlineData("http", "http")]
    [InlineData("https", "https")]
    [InlineData("", "https")]
    public void The_other_absolute_builders_carry_http_and_https_as_they_are(string scheme, string want)
    {
        var onStore = StoreUrl.ViewOf(Request(scheme, onStore: true));
        var onVendor = StoreUrl.ViewOf(Request(scheme, onStore: true, "vendor"));
        var onAdmin = StoreUrl.ViewOf(Request(scheme, onStore: true, "tenant_admin", ":5050"));
        var onApex = StoreUrl.ViewOf(Request(scheme, onStore: false));

        var rows = new (string Why, string Got, string Want)[]
        {
            ("App: مُضيفُ دَورٍ آخَر", StoreUrl.App(onStore, "ashare", "vendor", "login"), $"{want}://ashare-vendor.example.test/login"),
            ("App: دَورٌ مُؤَلَّفٌ مِن مُضيفِ دَور", StoreUrl.App(onVendor, "ashare", "mandoob", "me"), $"{want}://ashare.example.test/r/mandoob/me"),
            ("Open: مِن الجَذر", StoreUrl.Open(onApex, "ashare"), $"{want}://ashare.example.test/"),
            ("StoreHome: مِن مُضيفِ الإدارَة", AdminHostSignIn.StoreHome(onAdmin, "ashare"), $"{want}://ashare.example.test:5050/"),
            ("PlatformOrigin: مِن مُضيفِ المَتجَر", StoreUrl.PlatformOrigin(Request(scheme, onStore: true).Request), $"{want}://example.test"),
        };
        foreach (var (why, got, _) in rows) output.WriteLine($"«{scheme}» {why} ⇒ {got}");
        Assert.All(rows, r => Assert.Equal(r.Want, r.Got));
        Assert.Equal(5, rows.Length);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public void Path_mode_builds_what_it_built(string scheme)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString("localhost:5050");
        var view = StoreUrl.ViewOf(ctx);
        Assert.Equal("/ashare", StoreUrl.Open(view, "ashare"));
        Assert.Equal("/ashare/r/vendor/login", StoreUrl.App(view, "ashare", "vendor", "login"));
        Assert.Equal($"{scheme}://localhost:5050", StoreUrl.PlatformOrigin(ctx.Request));
        Assert.Null(LogoutHops.Next(view, "ashare", CustomerVendor, null));
    }
}
