using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ يَمُرُّ بِمُضيفاتِ المَتجَرِ — بِالبابَينِ المُسَجَّلَينِ عَلى Postgres (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-2؛ ADR-050 §١٤) ═══
//
// بابُ `POST /{slug}/auth/logout` ومَحَطَّةُ `GET /{slug}/auth/logout/continue` بِجِسمَيهِما المُسَجَّلَينِ (`LiveTemplateDoorHost`)، والطَلَبُ كَما يَترُكُه الحاقِنُ
// والوَسيط (`StoreHostItems` ومَعَها مُضيفاتُ المَتجَر): عُضوٌ يَخرُجُ عَلى مُضيفِ المَتجَرِ فَيَمُرُّ بِمُضيفِ العَميلِ ثُمَّ البائِعِ ويَعودُ إلى جَذرِ مُضيفِ المَتجَر، وكُلُّ
// مَحَطَّةٍ تَمحو جَرَّتَها؛ ومَحَطَّةٌ تَحمِلُ جَلسَةَ حِسابٍ آخَرَ لا تُمحى؛ وتَذكِرَةٌ مُختَلَقَةٌ لا تَمحو شَيئاً وتُحَوِّلُ إلى جَذرِ المُضيف.

[Collection(LiveProofCollection.Name)]
public class LiveHostLogoutChainProofTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly string[] CustomerVendor = ["customer", "vendor"];

    private static async Task<LiveHostLogoutCharacterizationProofTests.Answer> StopAsync(LiveTemplateDoorHost host, string slug, string role, Guid? sessionUser, string? ticket)
    {
        var door = host.GetDoor("/{slug}/auth/logout/continue");
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString($"{slug}-{role}.{Apex}");
        ctx.Request.Path = $"/{slug}/auth/logout/continue";
        ctx.Request.RouteValues["slug"] = slug;
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        ctx.Items[StoreHostItems.Apex] = Apex;
        ctx.Items[StoreHostItems.Slug] = slug;
        ctx.Items[StoreHostItems.Role] = role;
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        if (sessionUser is { } u)
        {
            var token = AuthHandlers.MakeToken(u, slug, 1);
            var generic = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug);
            var roleCookie = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug, "vendor");
            ctx.Request.Headers.Cookie = $"{generic}={token}; {generic}.name=x; {roleCookie}={token}; {roleCookie}.name=x";
        }
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(door);
        await door.RequestDelegate!(ctx);
        var cleared = ctx.Response.Headers.SetCookie
            .Where(c => c is not null && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
            .Select(c => c!.Split('=', 2)[0].Replace(slug, "{slug}")).Distinct().Order(StringComparer.Ordinal).ToList();
        return new(ctx.Response.StatusCode, ctx.Response.Headers.Location.ToString().Replace(slug, "{slug}"), cleared);
    }

    [Fact]
    public async Task Logout_on_the_store_host_passes_every_host_of_the_store_and_ends_where_it_began_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-hlc-" + Guid.NewGuid().ToString("N")[..8];
        var user = Guid.NewGuid();
        await using var host = LiveTemplateDoorHost.Build();

        var start = await LiveHostLogoutCharacterizationProofTests.LogoutAsync(host, slug, "store", signedIn: true,
            new Dictionary<string, object> { [StoreHostItems.HostedRoles] = CustomerVendor }, user);
        output.WriteLine($"الخُروجُ عَلى مُضيفِ المَتجَر ⇒ {start.Status} → {start.Location} · يَمحو {start.Cleared.Count}");
        Assert.Equal(302, start.Status);
        Assert.StartsWith("http://{slug}-customer.example.test/{slug}/auth/logout/continue?t=", start.Location);
        Assert.Equal(LiveHostLogoutCharacterizationProofTests.AllFour.Order(StringComparer.Ordinal), start.Cleared);
        var ticket = start.Location.Split("?t=")[1];

        var customer = await StopAsync(host, slug, "customer", user, ticket);
        output.WriteLine($"مَحَطَّةُ العَميل ⇒ {customer.Status} → {customer.Location} · يَمحو {customer.Cleared.Count}");
        Assert.Equal(302, customer.Status);
        Assert.Equal($"http://{{slug}}-vendor.example.test/{{slug}}/auth/logout/continue?t={ticket}", customer.Location);
        Assert.Equal(LiveHostLogoutCharacterizationProofTests.AllFour.Order(StringComparer.Ordinal), customer.Cleared);

        var vendor = await StopAsync(host, slug, "vendor", user, ticket);
        output.WriteLine($"مَحَطَّةُ البائِع ⇒ {vendor.Status} → {vendor.Location} · يَمحو {vendor.Cleared.Count}");
        Assert.Equal(302, vendor.Status);
        Assert.Equal("http://{slug}.example.test/", vendor.Location);
        Assert.Equal(LiveHostLogoutCharacterizationProofTests.AllFour.Order(StringComparer.Ordinal), vendor.Cleared);

        var stranger = await StopAsync(host, slug, "vendor", Guid.NewGuid(), ticket);
        output.WriteLine($"مَحَطَّةٌ بِجَلسَةِ حِسابٍ آخَر ⇒ {stranger.Status} → {stranger.Location} · يَمحو {stranger.Cleared.Count}");
        Assert.Equal("http://{slug}.example.test/", stranger.Location);
        Assert.Empty(stranger.Cleared);

        var forged = await StopAsync(host, slug, "vendor", user, ticket[..^3] + "xyz");
        output.WriteLine($"تَذكِرَةٌ مُختَلَقَة ⇒ {forged.Status} → {forged.Location} · يَمحو {forged.Cleared.Count}");
        Assert.Equal(302, forged.Status);
        Assert.Equal("/", forged.Location);
        Assert.Empty(forged.Cleared);
    }

    /// <summary>وفي وَضعِ المَسارِ بابُ المُرورِ لا يَمحو شَيئاً ولو حَمَلَ تَذكِرَةً صالِحَة — لا مُضيفَ لِلمَتجَرِ فَلا طَريق.</summary>
    [Fact]
    public async Task On_the_path_the_stop_clears_nothing_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-hlp-" + Guid.NewGuid().ToString("N")[..8];
        var user = Guid.NewGuid();
        await using var host = LiveTemplateDoorHost.Build();
        var door = host.GetDoor("/{slug}/auth/logout/continue");
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = $"/{slug}/auth/logout/continue";
        ctx.Request.RouteValues["slug"] = slug;
        ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={AuthHandlers.MakeLogoutHop(user, slug, "-", DateTimeOffset.UtcNow)}");
        ctx.Request.Headers.Cookie = $"{ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug)}={AuthHandlers.MakeToken(user, slug, 1)}";
        ctx.Response.Body = new MemoryStream();
        ctx.SetEndpoint(door);
        await door.RequestDelegate!(ctx);
        output.WriteLine($"وَضعُ المَسار ⇒ {ctx.Response.StatusCode} → {ctx.Response.Headers.Location} · Set-Cookie {ctx.Response.Headers.SetCookie.Count}");
        Assert.Equal(302, ctx.Response.StatusCode);
        Assert.Equal($"/{slug}", ctx.Response.Headers.Location.ToString());
        Assert.Equal(0, ctx.Response.Headers.SetCookie.Count);
    }
}
