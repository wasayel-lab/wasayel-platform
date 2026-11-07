using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ يَمُرُّ بِمُضيفاتِ المَتجَرِ كُلِّها — القَرارُ النَقِيُّ والتَذكِرَة (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-2؛ ADR-050 §١٤) ═══
//
// **المَقيسُ قَبل** (حَيّاً عِندَ f8e02c1): عُضوٌ دَخَلَ عَلى مُضيفِ المَتجَرِ وعَلى مُضيفِ البائِع، والخُروجُ عَلى أَحَدِهِما يَترُكُ الآخَرَ داخِلاً ثَلاثينَ يَوماً
// (الكَعكاتُ مَقصورَةٌ عَلى مُضيفِها). **والآن**: بابُ الخُروجِ يُحَوِّلُ إلى مَحَطَّةِ مُرورٍ عَلى المُضيفِ التالي، وكُلُّ مَحَطَّةٍ تَمحو جَرَّتَها وتُحَوِّلُ إلى ما
// بَعدَها، وتَنتَهي في جَذرِ مُضيفِ البِدايَة. **والوِجهَةُ مِن أَدوارِ المَتجَرِ ومُضيفِ البِدايَة لا مِن الطَلَب** — والحارِسُ هُنا صُفوفُ القَرار، والتَذكِرَةُ بِخُروقِها،
// ومَن يُمحى.

public class LogoutHopsTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static HostView On(string? hostRole) => new(Apex, "ashare", "https", "") { HostRole = hostRole };
    private static readonly string[] CustomerVendor = ["vendor", "customer"];

    [Theory]
    // البِدايَة: مِن مُضيفِ المَتجَرِ إلى أَوَّلِ مُضيفِ دَور، ومِن مُضيفِ الدَورِ إلى مُضيفِ المَتجَر
    [InlineData(null, null, "https://ashare-customer.example.test/ashare/auth/logout/continue", false)]
    [InlineData("vendor", null, "https://ashare.example.test/ashare/auth/logout/continue", false)]
    [InlineData("customer", null, "https://ashare.example.test/ashare/auth/logout/continue", false)]
    // في الطَريق: مِن البِدايَةِ عَلى مُضيفِ المَتجَر — العَميلُ ثُمَّ البائِعُ ثُمَّ جَذرُ مُضيفِ المَتجَر
    [InlineData("customer", "-", "https://ashare-vendor.example.test/ashare/auth/logout/continue", false)]
    [InlineData("vendor", "-", "https://ashare.example.test/", true)]
    // في الطَريق: مِن البِدايَةِ عَلى مُضيفِ البائِع — المَتجَرُ ثُمَّ العَميلُ ثُمَّ جَذرُ مُضيفِ البائِع
    [InlineData(null, "vendor", "https://ashare-customer.example.test/ashare/auth/logout/continue", false)]
    [InlineData("customer", "vendor", "https://ashare-vendor.example.test/", true)]
    public void The_next_stop_comes_from_the_store_roles_and_the_starting_host(string? hostRole, string? origin, string url, bool final)
    {
        var got = LogoutHops.Next(On(hostRole), "ashare", CustomerVendor, origin);
        output.WriteLine($"عَلى {hostRole ?? "المَتجَر"} · البِدايَة {origin ?? "هُنا"} ⇒ {got}");
        Assert.Equal((url, final), got);
    }

    /// <summary>لا سِلسِلَة: وَضعُ المَسار، ومَتجَرٌ آخَر، ومَتجَرٌ بِلا مُضيفِ دَور، ومُضيفُ دَورٍ لا يَحمِلُه، وبِدايَةٌ لَيسَت مِن مُضيفاتِه، والبِدايَةُ نَفسُها مَحَطَّة.</summary>
    [Fact]
    public void There_is_no_chain_where_the_store_has_no_other_host_or_the_request_is_off_the_road()
    {
        var rows = new (string Why, (string, bool)? Got)[]
        {
            ("وَضعُ المَسار", LogoutHops.Next(default, "ashare", CustomerVendor, null)),
            ("الجَذرُ بِلا مَتجَرِ مُضيف", LogoutHops.Next(new HostView(Apex, null, "https", ""), "ashare", CustomerVendor, null)),
            ("مَتجَرٌ آخَر", LogoutHops.Next(On(null), "ejar", CustomerVendor, null)),
            ("مَتجَرٌ بِلا مُضيفِ دَور", LogoutHops.Next(On(null), "ashare", [], null)),
            ("دَورٌ مُؤَلَّفٌ وَحدَه", LogoutHops.Next(On(null), "ashare", ["mandoob"], null)),
            ("مُضيفُ دَورٍ لا يَحمِلُه", LogoutHops.Next(On("driver"), "ashare", CustomerVendor, null)),
            ("بِدايَةٌ لَيسَت مِن مُضيفاتِه", LogoutHops.Next(On("customer"), "ashare", CustomerVendor, "driver")),
            ("بِدايَةٌ تُسَمّي أَصلاً آخَر", LogoutHops.Next(On("customer"), "ashare", CustomerVendor, "evil.example")),
            ("البِدايَةُ نَفسُها مَحَطَّة", LogoutHops.Next(On("vendor"), "ashare", CustomerVendor, "vendor")),
        };
        foreach (var (why, got) in rows) output.WriteLine($"{why} ⇒ {got?.ToString() ?? "لا سِلسِلَة"}");
        Assert.All(rows, r => Assert.Null(r.Got));
        Assert.Equal(9, rows.Length);
    }

    /// <summary>كُلُّ وِجهَةٍ مُطلَقَةٍ اسمُ مُضيفٍ لِهذا المَتجَرِ تَحتَ النِطاق — لِكُلِّ مُضيفِ بِدايَةٍ ولِكُلِّ مَحَطَّةٍ في طَريقِه، وبِأَدوارِ الكَتالوجِ كُلِّها؛
    /// والطَريقُ يَمُرُّ بِكُلِّ مُضيفٍ مَرَّةً ويَنتَهي في البِدايَة. يَطبَعُ ما عَدَّه.</summary>
    [Fact]
    public void Every_tour_visits_each_host_once_and_ends_where_it_began()
    {
        var all = ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug.Keys.ToArray();
        var keys = LogoutHops.Tour(all);
        var hops = 0;
        foreach (var start in keys)
        {
            var at = start == LogoutHops.StoreHost ? null : start;
            var stop = LogoutHops.Next(On(at), "ashare", all, null);
            var seen = new List<string>();
            while (stop is { Final: false } s)
            {
                hops++;
                var host = new Uri(s.Url).Host;
                Assert.EndsWith("." + Apex, host);
                var label = host[..^(Apex.Length + 1)];
                var here = label == "ashare" ? null : ACommerce.Kit.Roles.RoleHostNames.CatalogSlugOf(label["ashare-".Length..]);
                seen.Add(here ?? LogoutHops.StoreHost);
                stop = LogoutHops.Next(On(here), "ashare", all, start);
            }
            Assert.NotNull(stop);
            Assert.Equal($"https://{(at is null ? "ashare" : "ashare-" + ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug[at])}.{Apex}/", stop.Value.Url);
            Assert.Equal(keys.Where(k => k != start).Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        }
        output.WriteLine($"مُضيفاتٌ: {keys.Count} · مَحَطّاتٌ مَعدودَة: {hops}");
        Assert.Equal(keys.Count * (keys.Count - 1), hops);
        Assert.True(keys.Count >= 10, $"فَحصٌ أَعمى: {keys.Count}");
    }

    // ─── التَذكِرَة ────────────────────────────────────────────────────

    [Fact]
    public void A_ticket_reads_back_its_account_and_starting_host_for_its_store_until_it_expires()
    {
        var user = Guid.NewGuid();
        var t = AuthHandlers.MakeLogoutHop(user, "ashare", "vendor", Now);
        output.WriteLine($"التَذكِرَة: {t.Length} مِحرَفاً");
        Assert.Matches("^[A-Za-z0-9_-]+$", t);
        Assert.Equal((user, "vendor"), AuthHandlers.ReadLogoutHop(t, "ashare", Now));
        Assert.Equal((user, "vendor"), AuthHandlers.ReadLogoutHop(t, "ashare", Now + AuthHandlers.LogoutHopLife));
        Assert.Null(AuthHandlers.ReadLogoutHop(t, "ashare", Now + AuthHandlers.LogoutHopLife + TimeSpan.FromSeconds(1)));
        Assert.Null(AuthHandlers.ReadLogoutHop(t, "ejar", Now));
    }

    [Fact]
    public void A_forged_bent_or_foreign_ticket_reads_as_nothing()
    {
        var user = Guid.NewGuid();
        var good = AuthHandlers.MakeLogoutHop(user, "ashare", "-", Now);
        string B64(string raw) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = (Now + TimeSpan.FromMinutes(1)).ToUnixTimeSeconds();
        var rows = new (string Why, string? Ticket)[]
        {
            ("فارِغَة", ""),
            ("غائِبَة", null),
            ("بِلا تَوقيع", B64($"logout-hop|{user}|ashare|-|{exp}")),
            ("تَوقيعٌ مُختَلَق", B64($"logout-hop|{user}|ashare|-|{exp}|AAAA")),
            ("حَرفٌ مُبَدَّل", good[..^2] + (good[^2] == 'A' ? "B" : "A") + good[^1]),
            ("مَسافَةٌ في الوَسَط", good[..10] + " " + good[10..]),
            ("سَطرٌ جَديدٌ في الوَسَط", good[..10] + "%0d%0a" + good[10..]),
            ("رَمزُ جَلسَةٍ لا تَذكِرَة", AuthHandlers.MakeToken(user, "ashare", 1)),
            ("طَويلَةٌ جِدّاً", new string('A', 600)),
        };
        foreach (var (why, ticket) in rows)
        {
            var got = AuthHandlers.ReadLogoutHop(ticket, "ashare", Now);
            output.WriteLine($"{why} ⇒ {got?.ToString() ?? "لا شَيء"}");
            Assert.Null(got);
        }
        // والتَذكِرَةُ لا تُقرَأُ رَمزَ جَلسَة
        Assert.Null(AuthHandlers.ParseToken(good));
        Assert.NotNull(AuthHandlers.ReadLogoutHop(good, "ashare", Now));
    }

    // ─── البِدايَةُ ومَحَطَّةُ المُرورِ بِالطَلَبِ كَما يَترُكُه الحاقِنُ والوَسيط ─────

    private static DefaultHttpContext Request(string? hostRole, string? cookieToken, IReadOnlyList<string>? hosted, string? ticket = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(hostRole is null ? $"ashare.{Apex}" : $"ashare-{hostRole}.{Apex}");
        ctx.Items[StoreHostItems.Apex] = Apex;
        ctx.Items[StoreHostItems.Slug] = "ashare";
        if (hostRole is not null) ctx.Items[StoreHostItems.Role] = hostRole;
        if (hosted is not null) ctx.Items[StoreHostItems.HostedRoles] = hosted;
        if (cookieToken is not null) ctx.Request.Headers.Cookie = $"{AuthSession.CookieName("ashare")}={cookieToken}";
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        return ctx;
    }

    [Fact]
    public void Logout_begins_the_chain_only_for_a_session_of_this_store_on_a_store_with_another_host()
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, "ashare", 1);
        var begun = LogoutHops.Begin(Request(null, token, CustomerVendor), "ashare", token, Now);
        output.WriteLine($"البِدايَة ⇒ {begun}");
        Assert.NotNull(begun);
        Assert.StartsWith("https://ashare-customer.example.test/ashare/auth/logout/continue?t=", begun);
        Assert.Equal((user, "-"), AuthHandlers.ReadLogoutHop(begun!.Split("?t=")[1], "ashare", Now));

        Assert.Null(LogoutHops.Begin(Request(null, null, CustomerVendor), "ashare", null, Now));
        Assert.Null(LogoutHops.Begin(Request(null, token, null), "ashare", token, Now));
        var other = AuthHandlers.MakeToken(user, "ejar", 1);
        Assert.Null(LogoutHops.Begin(Request(null, other, CustomerVendor), "ashare", other, Now));
    }

    [Theory]
    // (الجَلسَةُ عَلى هذا المُضيف، التَذكِرَة) ⇒ أَيُمحى، والوِجهَة
    [InlineData("same", "good", true, "https://ashare-vendor.example.test/ashare/auth/logout/continue?t=")]
    [InlineData("none", "good", true, "https://ashare-vendor.example.test/ashare/auth/logout/continue?t=")]
    [InlineData("other", "good", false, "https://ashare-vendor.example.test/ashare/auth/logout/continue?t=")]
    [InlineData("same", "forged", false, null)]
    [InlineData("same", "absent", false, null)]
    [InlineData("same", "foreign", false, null)]
    public void A_stop_clears_only_the_account_that_left_and_only_with_its_ticket(string session, string ticket, bool clears, string? next)
    {
        var user = Guid.NewGuid();
        var good = AuthHandlers.MakeLogoutHop(user, "ashare", "-", Now);
        var t = ticket switch
        {
            "good" => good,
            "forged" => good[..^3] + "xyz",
            "foreign" => AuthHandlers.MakeLogoutHop(user, "ejar", "-", Now),
            _ => null,
        };
        var cookie = session switch
        {
            "same" => AuthHandlers.MakeToken(user, "ashare", 1),
            "other" => AuthHandlers.MakeToken(Guid.NewGuid(), "ashare", 1),
            _ => null,
        };
        var (clear, to) = LogoutHops.Continue(Request("customer", cookie, CustomerVendor, t), "ashare", Now);
        output.WriteLine($"جَلسَة {session} · تَذكِرَة {ticket} ⇒ يَمحو {clear} → {to ?? "جَذرُ هذا المُضيف"}");
        Assert.Equal(clears, clear);
        if (next is null) Assert.Null(to);
        else Assert.Equal(next + good, to);
    }

    [Fact]
    public void The_last_stop_lands_on_the_starting_host_root_without_the_ticket()
    {
        var user = Guid.NewGuid();
        var t = AuthHandlers.MakeLogoutHop(user, "ashare", "-", Now);
        var (clear, to) = LogoutHops.Continue(Request("vendor", AuthHandlers.MakeToken(user, "ashare", 1), CustomerVendor, t), "ashare", Now);
        Assert.True(clear);
        Assert.Equal("https://ashare.example.test/", to);
    }

    // ─── المُضيفاتُ مِن أَدوارِ المَتجَرِ بِشَرطِ الـ404 نَفسِه ─────────────

    [Fact]
    public void The_hosted_roles_are_the_catalog_roles_the_store_holds_by_their_own_name()
    {
        var tenant = new Tenant
        {
            Id = "ashare",
            Roles =
            [
                new ACommerce.Kit.Roles.Role { Slug = "vendor", CatalogSlug = "vendor" },
                new ACommerce.Kit.Roles.Role { Slug = "customer", CatalogSlug = "customer" },
                new ACommerce.Kit.Roles.Role { Slug = "mandoob", CatalogSlug = "driver" },
                new ACommerce.Kit.Roles.Role { Slug = "tenant_admin", CatalogSlug = "tenant_admin" },
            ],
        };
        var hosted = TenantResolverMiddleware.HostedRoles(tenant);
        output.WriteLine($"المُضيفات: {string.Join(" · ", hosted)}");
        Assert.Equal(["customer", "tenant_admin", "vendor"], hosted);
        Assert.False(TenantResolverMiddleware.HoldsRoleHost(tenant, "driver"));
        Assert.Empty(TenantResolverMiddleware.HostedRoles(new Tenant { Id = "empty" }));
    }
}
