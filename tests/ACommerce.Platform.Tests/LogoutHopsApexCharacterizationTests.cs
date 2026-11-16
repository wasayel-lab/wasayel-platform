using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تَصيرَ جَرَّةُ الجَذرِ مَحَطَّةً في الخُروج (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2b · SEC-C2b-R3-1) ═══
//
// **السَبَبُ القادِم**: مَعَ نِطاقٍ مُهَيَّأٍ يَبقى الجَذرُ `R` يَخدِمُ المَتجَرَ بِمَسارِه (`R/{slug}`، فَالمَرحَلَةُ الخامِسَةُ لَم تُبنَ) بِجَرَّةِ كَعكاتٍ مُستَقِلَّة — وجَلَساتُ ما
// قَبلَ النِطاقِ كُلُّها هُناك. والسِلسِلَةُ لا تَمُرُّ بِها: الخُروجُ عَلى مُضيفِ المَتجَرِ يَترُكُ `R/{slug}` داخِلاً، والخُروجُ عَلى الجَذرِ يَترُكُ المُضيفاتِ داخِلَة (مَقيسٌ
// حَيّاً عِندَ `818ef7b`). فَسَتَصيرُ جَرَّةُ الجَذرِ مَحَطَّةً — **بِعَلامَةٍ يَكتُبُها الوَسيطُ حينَ يُهَيَّأُ النِطاقُ ويَصلُحُ اسمُ المَتجَرِ مُضيفاً**.
//
// **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣): وَضعُ المَسارِ بِلا نِطاقٍ لا سِلسِلَةَ فيه؛ والجَذرُ بِلا تِلكَ العَلامَةِ كَما
// هُوَ اليَوم (لا سِلسِلَةَ يَبدَؤُها ولا مَحَطَّةَ يَقبَلُها)؛ والسِلسِلَةُ عَلى المُضيفاتِ بِلا العَلامَةِ بِحَرفِها. و`LogoutHopsTests` يَبقى بايتاتِه كَذلك.

public class LogoutHopsApexCharacterizationTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CustomerVendor = ["customer", "vendor"];

    private static DefaultHttpContext OnApex(string? cookieToken, bool withApexItem, string? ticket = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(withApexItem ? Apex : "localhost");
        ctx.Request.Path = "/ashare/auth/logout";
        if (withApexItem) ctx.Items[StoreHostItems.Apex] = Apex;
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        if (cookieToken is not null) ctx.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(ctx.Request, "ashare")}={cookieToken}";
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        return ctx;
    }

    [Fact]
    public void In_path_mode_there_is_no_chain_to_begin_or_to_continue()
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);
        var begun = LogoutHops.Begin(OnApex(token, withApexItem: false), "ashare", token, Now);
        var cont = LogoutHops.Continue(OnApex(token, withApexItem: false, AuthHandlers.MakeLogoutHop(user, "ashare", "-", Now)), "ashare", Now);
        output.WriteLine($"وَضعُ المَسار ⇒ البِدايَة {begun ?? "لا سِلسِلَة"} · المَحَطَّة {cont}");
        Assert.Null(begun);
        Assert.Equal((false, (string?)null), cont);
        Assert.Null(LogoutHops.Next(default, "ashare", CustomerVendor, null));
        Assert.Null(LogoutHops.Next(default, "ashare", CustomerVendor, "-"));
    }

    [Fact]
    public void On_the_apex_without_the_marker_the_logout_ends_where_it_did()
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);
        var view = new HostView(Apex, null, "https", "");
        var rows = new (string Why, object? Got)[]
        {
            ("Next عَلى الجَذرِ بِلا بِدايَة", LogoutHops.Next(view, "ashare", CustomerVendor, null)),
            ("Next عَلى الجَذرِ بِبِدايَةِ المُضيف", LogoutHops.Next(view, "ashare", CustomerVendor, "-")),
            ("Begin عَلى الجَذر", LogoutHops.Begin(OnApex(token, withApexItem: true), "ashare", token, Now)),
        };
        foreach (var (why, got) in rows) output.WriteLine($"{why} ⇒ {got?.ToString() ?? "لا سِلسِلَة"}");
        Assert.All(rows, r => Assert.Null(r.Got));
        Assert.Equal((false, (string?)null),
            LogoutHops.Continue(OnApex(token, withApexItem: true, AuthHandlers.MakeLogoutHop(user, "ashare", "-", Now)), "ashare", Now));
    }

    [Fact]
    public void The_tour_without_the_marker_is_the_store_host_then_its_role_hosts()
    {
        Assert.Equal(["-", "customer", "vendor"], LogoutHops.Tour(["vendor", "customer", "vendor", "mandoob"]));
        Assert.Equal(["-"], LogoutHops.Tour([]));
        Assert.Equal("-", LogoutHops.StoreHost);
    }
}
