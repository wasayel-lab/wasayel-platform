using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الخُروجُ يَمُرُّ بِجَرَّةِ الجَذرِ عَلى بابَي الخُروجِ ومَحَطَّتِه الحَقيقِيَّين (‏2026-11-11، W13-4·C2z·1 — مُراجَعَةُ C2b · SEC-C2b-R3-1) ═══
//
// البابانِ مِن مَسارِ القالِبِ نَفسِه (`LiveTemplateDoorHost`) والعَناصِرُ كَما يَكتُبُها الوَسيط: عَلى الجَذرِ `Apex` ومُضيفاتُ المَتجَرِ والعَلامَة، وعَلى المُضيفِ مَعَها
// مَتجَرُه. الخُروجُ عَلى الجَذرِ يَبدَأُ السِلسِلَةَ إلى مُضيفِ المَتجَر؛ ومَحَطَّةُ الجَذرِ في سِلسِلَةٍ بَدَأَت عَلى المُضيفِ تَمحو جَرَّتَه وتُعيدُ إلى جَذرِ المُضيف.

[Collection(LiveProofCollection.Name)]
public class LiveApexLogoutChainProofTests(ITestOutputHelper output)
{
    private const string Apex = "example.test";
    private static readonly string[] CustomerVendor = ["customer", "vendor"];

    private static async Task<LiveHostLogoutCharacterizationProofTests.Answer> OnApexAsync(LiveTemplateDoorHost host, string method, string slug, Guid user, string? ticket)
    {
        var door = method == "POST" ? host.Door("/{slug}/auth/logout") : host.GetDoor("/{slug}/auth/logout/continue");
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        ctx.Request.Method = method;
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString(Apex);
        ctx.Request.Path = method == "POST" ? $"/{slug}/auth/logout" : $"/{slug}/auth/logout/continue";
        ctx.Request.RouteValues["slug"] = slug;
        if (ticket is not null) ctx.Request.QueryString = new QueryString($"?{LogoutHops.TicketKey}={ticket}");
        ctx.Items[StoreHostItems.Apex] = Apex;
        ctx.Items[StoreHostItems.HostedRoles] = CustomerVendor;
        ctx.Items[StoreHostItems.ApexServesStore] = true;
        var token = AuthHandlers.MakeToken(user, slug, 1);
        var generic = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug);
        var role = ACommerce.Templates.Customer.Marketplace.AuthSession.CookieName(slug, "vendor");
        ctx.Request.Headers.Cookie = $"{generic}={token}; {generic}.name=x; {role}={token}; {role}.name=x";
        if (method == "POST")
        {
            ctx.Request.ContentType = "application/x-www-form-urlencoded";
            ctx.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());
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
    public async Task Logout_on_the_apex_path_begins_the_chain_and_the_apex_stop_clears_its_jar_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-hla-" + Guid.NewGuid().ToString("N")[..8];
        var user = Guid.NewGuid();
        await using var host = LiveTemplateDoorHost.Build();

        var start = await OnApexAsync(host, "POST", slug, user, ticket: null);
        output.WriteLine($"الخُروجُ عَلى R/{{slug}} ⇒ {start.Status} → {start.Location} · يَمحو {start.Cleared.Count}");
        Assert.Equal(302, start.Status);
        Assert.StartsWith("http://{slug}.example.test/{slug}/auth/logout/continue?t=", start.Location);
        Assert.Equal(LiveHostLogoutCharacterizationProofTests.AllFour.Order(StringComparer.Ordinal), start.Cleared);
        Assert.Equal(LogoutHops.Apex, AuthHandlers.ReadLogoutHop(start.Location.Split("?t=")[1], slug, DateTimeOffset.UtcNow)?.Origin);

        var fromStore = AuthHandlers.MakeLogoutHop(user, slug, LogoutHops.StoreHost, DateTimeOffset.UtcNow);
        var stop = await OnApexAsync(host, "GET", slug, user, fromStore);
        output.WriteLine($"مَحَطَّةُ الجَذرِ في سِلسِلَةِ المُضيف ⇒ {stop.Status} → {stop.Location} · يَمحو {stop.Cleared.Count}");
        Assert.Equal(302, stop.Status);
        Assert.Equal("http://{slug}.example.test/", stop.Location);
        Assert.Equal(LiveHostLogoutCharacterizationProofTests.AllFour.Order(StringComparer.Ordinal), stop.Cleared);

        var stranger = await OnApexAsync(host, "GET", slug, Guid.NewGuid(), fromStore);
        output.WriteLine($"مَحَطَّةُ الجَذرِ بِجَلسَةِ حِسابٍ آخَر ⇒ {stranger.Status} → {stranger.Location} · يَمحو {stranger.Cleared.Count}");
        Assert.Equal("http://{slug}.example.test/", stranger.Location);
        Assert.Empty(stranger.Cleared);
    }
}
