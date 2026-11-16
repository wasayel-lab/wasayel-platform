using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ جَرَّةُ الجَذرِ مَحَطَّةٌ في الخُروج (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2b · SEC-C2b-R3-1؛ ADR-050 §١٤) ═══
//
// **المَقيسُ قَبل** (عِندَ `818ef7b`، BaseDomain=localhost، جَرَّةٌ واحِدَةٌ تَعرِفُ النِطاقات): الخُروجُ عَلى مُضيفِ المَتجَرِ مَرَّ بِالأَدوارِ وانتَهى في جَذرِه — و
// `localhost/{store}/me` **بِجَلسَةٍ** بَعدَه؛ والخُروجُ عَلى `localhost/{store}` أَجابَ `302 /{store}` بِلا سِلسِلَةٍ والمُضيفُ **بِجَلسَة**. **والآن**: الوَسيطُ يَكتُبُ
// `ApexServesStore` (نِطاقٌ مُهَيَّأٌ واسمٌ يَصلُحُ مُضيفاً)، والجَولَةُ مُضيفُ المَتجَرِ ثُمَّ أَدوارُه ثُمَّ الجَذر؛ ومَن بَدَأَ عَلى الجَذرِ انتَهى في `R/{slug}`.
// وما لا يَتَغَيَّرُ يُثَبِّتُه `LogoutHopsApexCharacterizationTests` و`LogoutHopsTests` بِبايتاتِهِما.

public class LogoutHopsApexTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] CustomerVendor = ["customer", "vendor"];
    private static HostView OnStore(string? hostRole) => new(Apex, "ashare", "https", "") { HostRole = hostRole };
    private static readonly HostView OnApex = new(Apex, null, "https", "");

    [Theory]
    // البِدايَةُ عَلى الجَذر: المُضيفُ ثُمَّ الأَدوارُ ثُمَّ العَودَةُ إلى R/{slug}
    [InlineData("@", null, "https://ashare.example.test/ashare/auth/logout/continue", false)]
    [InlineData("-", "@", "https://ashare-customer.example.test/ashare/auth/logout/continue", false)]
    [InlineData("vendor", "@", "https://example.test/ashare", true)]
    // البِدايَةُ عَلى المُضيف: الأَدوارُ ثُمَّ الجَذرُ ثُمَّ العَودَةُ إلى جَذرِ المُضيف
    [InlineData("-", null, "https://ashare-customer.example.test/ashare/auth/logout/continue", false)]
    [InlineData("vendor", "-", "https://example.test/ashare/auth/logout/continue", false)]
    [InlineData("@", "-", "https://ashare.example.test/", true)]
    // البِدايَةُ عَلى مُضيفِ الدَور
    [InlineData("@", "vendor", "https://ashare-vendor.example.test/", true)]
    public void The_apex_jar_is_a_stop_when_the_apex_serves_the_store(string here, string? origin, string url, bool final)
    {
        var view = here switch { "@" => OnApex, "-" => OnStore(null), _ => OnStore(here) };
        var got = LogoutHops.Next(view, "ashare", CustomerVendor, origin, apexServes: true);
        output.WriteLine($"هُنا {here} · البِدايَة {origin ?? "هُنا"} ⇒ {got}");
        Assert.Equal((url, final), got);
    }

    [Fact]
    public void A_store_with_no_role_host_still_has_two_jars_when_the_apex_serves_it()
    {
        Assert.Equal(("https://ashare.example.test/ashare/auth/logout/continue", false), LogoutHops.Next(OnApex, "ashare", [], null, apexServes: true));
        Assert.Equal(("https://example.test/ashare", true), LogoutHops.Next(OnStore(null), "ashare", [], "@", apexServes: true));
        Assert.Equal(("https://example.test/ashare/auth/logout/continue", false), LogoutHops.Next(OnStore(null), "ashare", [], null, apexServes: true));
        Assert.Equal(("https://ashare.example.test/", true), LogoutHops.Next(OnApex, "ashare", [], "-", apexServes: true));
        // وبِلا العَلامَةِ جَرَّةٌ واحِدَةٌ لا سِلسِلَة — كَما يُثَبِّتُه التَوصيف
        Assert.Null(LogoutHops.Next(OnStore(null), "ashare", [], null, apexServes: false));
    }

    [Fact]
    public void The_port_of_the_request_rides_every_stop()
    {
        var view = new HostView("localhost", null, "http", ":5050");
        Assert.Equal(("http://ashare.localhost:5050/ashare/auth/logout/continue", false), LogoutHops.Next(view, "ashare", CustomerVendor, null, apexServes: true));
        Assert.Equal(("http://localhost:5050/ashare", true),
            LogoutHops.Next(new HostView("localhost", "ashare", "http", ":5050") { HostRole = "vendor" }, "ashare", CustomerVendor, "@", apexServes: true));
    }

    [Fact]
    public void Every_tour_with_the_apex_visits_each_jar_once_and_ends_where_it_began()
    {
        var all = ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug.Keys.ToArray();
        var keys = LogoutHops.Tour(all, apexServes: true);
        Assert.Equal(LogoutHops.Apex, keys[^1]);
        var hops = 0;
        foreach (var start in keys)
        {
            HostView ViewFor(string k) => k == LogoutHops.Apex ? OnApex : OnStore(k == LogoutHops.StoreHost ? null : k);
            var stop = LogoutHops.Next(ViewFor(start), "ashare", all, null, apexServes: true);
            var seen = new List<string>();
            while (stop is { Final: false } s)
            {
                hops++;
                var host = new Uri(s.Url).Host;
                var here = host == Apex ? LogoutHops.Apex
                         : host == "ashare." + Apex ? LogoutHops.StoreHost
                         : ACommerce.Kit.Roles.RoleHostNames.CatalogSlugOf(host[..^(Apex.Length + 1)]["ashare-".Length..])!;
                seen.Add(here);
                stop = LogoutHops.Next(ViewFor(here), "ashare", all, start, apexServes: true);
            }
            Assert.NotNull(stop);
            var expected = start == LogoutHops.Apex ? $"https://{Apex}/ashare"
                         : $"https://{(start == LogoutHops.StoreHost ? "ashare" : "ashare-" + ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug[start])}.{Apex}/";
            Assert.Equal(expected, stop.Value.Url);
            Assert.Equal(keys.Where(k => k != start).Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        }
        output.WriteLine($"جِرار: {keys.Count} · مَحَطّاتٌ مَعدودَة: {hops}");
        Assert.Equal(keys.Count * (keys.Count - 1), hops);
        Assert.True(keys.Count >= 12, $"فَحصٌ أَعمى: {keys.Count}");
    }

    [Theory]
    [InlineData("example.test", "ashare", true)]
    [InlineData(null, "ashare", false)]
    [InlineData("", "ashare", false)]
    [InlineData("example.test", "best-vendor", false)]   // لا يَصلُحُ مُضيفاً (لاحِقَةُ دَور) — جَرَّةُ الجَذرِ وَحدَها
    [InlineData("example.test", "ab--cd", false)]
    public void The_apex_serves_a_store_only_with_a_domain_and_a_host_shaped_name(string? apex, string slug, bool serves)
        => Assert.Equal(serves, TenantResolverMiddleware.ApexServes(apex, slug));

    private static DefaultHttpContext Request(bool onApex, string? cookieToken, string? ticket = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(onApex ? Apex : $"ashare.{Apex}");
        ctx.Items[StoreHostItems.Apex] = Apex;
        if (!onApex) ctx.Items[StoreHostItems.Slug] = "ashare";
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        ctx.Items[StoreHostItems.ApexServesStore] = true;
        if (cookieToken is not null) ctx.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(ctx.Request, "ashare")}={cookieToken}";
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        return ctx;
    }

    [Fact]
    public void Logout_on_the_apex_begins_the_chain_with_a_ticket_that_names_the_apex()
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);
        var begun = LogoutHops.Begin(Request(onApex: true, token), "ashare", token, Now);
        output.WriteLine($"البِدايَةُ عَلى الجَذر ⇒ {begun}");
        Assert.StartsWith("https://ashare.example.test/ashare/auth/logout/continue?t=", begun);
        Assert.Equal((user, LogoutHops.Apex), AuthHandlers.ReadLogoutHop(begun!.Split("?t=")[1], "ashare", Now));
        Assert.Null(LogoutHops.Begin(Request(onApex: true, null), "ashare", null, Now));
    }

    [Fact]
    public void The_apex_stop_clears_the_account_that_left_and_hands_back_to_the_starting_host()
    {
        var user = Guid.NewGuid();
        var t = AuthHandlers.MakeLogoutHop(user, "ashare", LogoutHops.StoreHost, Now);
        var (clear, to) = LogoutHops.Continue(Request(onApex: true, AuthHandlers.MakeToken(user, "ashare", 1), t), "ashare", Now);
        Assert.True(clear);
        Assert.Equal("https://ashare.example.test/", to);
        var (clearOther, toOther) = LogoutHops.Continue(Request(onApex: true, AuthHandlers.MakeToken(Guid.NewGuid(), "ashare", 1), t), "ashare", Now);
        Assert.False(clearOther);
        Assert.Equal("https://ashare.example.test/", toOther);
    }
}
