using System.Security.Claims;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Realtime.Server;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — رَمزُ الجَلسَةِ (‏HttpOnly) يُطبَعُ في كُلِّ صَفحَةِ مَتجَرٍ مُوَثَّقَة، ويُرسَلُ في رابِطِ realtime، ويُكتَبُ في السِجِلّ (تَدقيقُ الإطلاق lens-security-4، حاجِبَة) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `8470f35`):
//   ١. `App.razor` يَطبَعُ `window.acRealtimeToken = '@Auth.Token'` لِكُلِّ مُستَخدِمٍ مُوَثَّق — الرَمزُ نَفسُه الَّذي يَحمِلُه كوكي
//      `HttpOnly` (‏`AuthSession.BuildOpts`)، فَيَقرَؤُه أَيُّ سِكربتٍ في الصَفحَة، والكوكي `HttpOnly` لِيَمنَعَ ذلك بِعَينِه.
//   ٢. السِكربتُ المُضَمَّنُ في `App.razor` يَتَّصِلُ بِـ`/realtime?token=` + الرَمز، و`realtime-nav.js` بِـ`accessTokenFactory` (‏Bearer في
//      `negotiate` و`?access_token=` في مُصافَحَةِ WebSocket) — فَالرَمزُ في سِلسِلَةِ استِعلامٍ مَرَّتَين.
//   ٣. `RealtimeHub.OnConnectedAsync` يَقرَأُ `access_token` ثُمَّ `Authorization: Bearer` ثُمَّ `token` مِن الاستِعلام — **ولا يَقرَأُ
//      الكوكي**؛ فَاتِّصالٌ بِكوكي الجَلسَةِ وَحدَه يُقطَع.
//   ٤. `HostingExtensions.AddPlatformHost` يَضبِطُ Serilog بِـ`MinimumLevel.Information()` بِلا تَجاوُز، فَيُكتَبُ سَطرُ
//      `Request starting HTTP/1.1 POST …/realtime/negotiate?token=…` في سِجِلِّ الإنتاج — الرَمزُ صالِحٌ ‏30 يَوماً ولا يُبطَل (د28).
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): صُفوفُ `Today_…` الأَربَعَةُ تَنقَلِبُ بِأَسمائِها في كوميتِ العِلاج، وصَفُّ «المَجهولُ يُقطَعُ ولا
// يَنضَمُّ إلى مَجموعَة» يَبقى بِلا تَعديلِ حَرفٍ في جِسمِه. والهُبُّ يُشَغَّلُ بِسِياقِ اتِّصالٍ مُصطَنَعٍ يَحمِلُ `HttpContext` الطَلَب — نَفسُ
// ما تَقرَؤُه `GetHttpContext()` في الإنتاج — بِلا خادِمٍ ولا SignalR حَيّ.
public class RealtimeSessionTokenTests
{
    private const string Slug = "theme-demo";
    private const string TPL = "ACommerce.Templates.Customer.Marketplace";

    private static string Source(params string[] parts) =>
        WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(
            StudioConsentCharacterizationTests.Source(parts)));

    internal static string Shell() => Source("libs", "templates", TPL, "Components", "App.razor");
    internal static string NavScript() => Source("libs", "templates", TPL, "wwwroot", "realtime-nav.js");
    internal static string Hub() => Source("libs", "kits", "Realtime", "ACommerce.Kit.Realtime.Server", "RealtimeHub.cs");
    internal static string Hosting() => Source("libs", "core", "ACommerce.Platform.Hosting", "HostingExtensions.cs");

    // ─── سِياقُ اتِّصالٍ مُصطَنَع — ما يَراه الهُبُّ مِن الطَلَبِ وَحدَه ─────────────────────────────────

    private sealed class HttpFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }

    internal sealed class ProbeContext : HubCallerContext
    {
        private readonly FeatureCollection _features = new();
        public ProbeContext(HttpContext http) => _features.Set<IHttpContextFeature>(new HttpFeature { HttpContext = http });
        public bool Aborted { get; private set; }
        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    internal sealed class ProbeGroups : IGroupManager
    {
        public List<string> Joined { get; } = new();
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Joined.Add(groupName);
            return Task.CompletedTask;
        }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    internal static HttpContext Request(string query = "", string? cookie = null, string? bearer = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/realtime";
        if (query.Length > 0) http.Request.QueryString = new QueryString(query);
        if (cookie is not null) http.Request.Headers.Cookie = cookie;
        if (bearer is not null) http.Request.Headers.Authorization = $"Bearer {bearer}";
        return http;
    }

    internal static RealtimeHub NewHub() => new();

    internal static async Task<(bool Aborted, List<string> Joined)> ConnectAsync(HttpContext http)
    {
        var hub = NewHub();
        var context = new ProbeContext(http);
        var groups = new ProbeGroups();
        hub.Context = context;
        hub.Groups = groups;
        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);
        return (context.Aborted, groups.Joined);
    }

    // ─── ١. المَجهولُ يُقطَعُ ولا يَنضَمّ — لا يَتَغَيَّرُ بِالعِلاج ─────────────────────────────────────

    [Fact]
    public async Task An_anonymous_connection_is_aborted_and_joins_no_group()
    {
        var forged = Convert.ToBase64String("00000000-0000-0000-0000-000000000001|theme-demo|9999999999|forged"u8.ToArray());
        foreach (var http in new[]
                 {
                     Request(),
                     Request($"?slug={Slug}"),
                     Request($"?slug={Slug}", cookie: $"{AuthSession.CookieName(Slug)}={forged}"),
                     Request($"?slug={Slug}&token={forged}"),
                 })
        {
            var (aborted, joined) = await ConnectAsync(http);
            Assert.True(aborted, $"اتِّصالٌ بِلا جَلسَةٍ صالِحَةٍ لَم يُقطَع: {http.Request.QueryString}");
            Assert.Empty(joined);
        }
    }

    // ─── ٢. «اليَوم» — الهُبُّ يُصَدِّقُ الرَمزَ في الاستِعلامِ وفي Bearer ─────────────────────────────

    [Fact]
    public async Task Today_the_hub_joins_a_connection_that_carries_the_token_in_the_query_or_a_bearer_header()
    {
        var user = Guid.NewGuid();
        var token = AuthHandlers.MakeToken(user, Slug);
        foreach (var http in new[]
                 {
                     Request($"?token={Uri.EscapeDataString(token)}"),
                     Request($"?access_token={Uri.EscapeDataString(token)}"),
                     Request(bearer: token),
                 })
        {
            var (aborted, joined) = await ConnectAsync(http);
            Assert.False(aborted);
            Assert.Equal(new[] { RealtimeHub.GroupName(Slug, user) }, joined);
        }
    }

    // ─── ٣. «اليَوم» — والكوكي وَحدَه لا يَكفي ─────────────────────────────────────────────────────

    [Fact]
    public async Task Today_the_hub_aborts_a_connection_that_carries_the_session_only_in_its_cookie()
    {
        var token = AuthHandlers.MakeToken(Guid.NewGuid(), Slug);
        var (aborted, joined) = await ConnectAsync(
            Request($"?slug={Slug}", cookie: $"{AuthSession.CookieName(Slug)}={token}"));
        Assert.True(aborted);
        Assert.Empty(joined);
    }

    // ─── ٤. «اليَوم» — الصَفحَةُ تَطبَعُ الرَمزَ، والرابِطانِ يَحمِلانِه ───────────────────────────────

    [Fact]
    public void Today_the_store_shell_prints_the_session_token_and_both_clients_send_it_in_a_url()
    {
        var shell = Shell();
        Assert.Contains("window.acRealtimeToken = '@Auth.Token';", shell, StringComparison.Ordinal);
        Assert.Contains(".withUrl('/realtime?token=' + encodeURIComponent(window.acRealtimeToken))", shell, StringComparison.Ordinal);

        var nav = NavScript();
        Assert.Contains("const token = window.acRealtimeToken;", nav, StringComparison.Ordinal);
        Assert.Contains("accessTokenFactory: () => token", nav, StringComparison.Ordinal);

        var hub = Hub();
        Assert.Matches(@"Query\[""access_token""\]", hub);
        Assert.Matches(@"Query\[""token""\]", hub);
        Assert.DoesNotMatch(@"Cookies", hub);
    }

    // ─── ٥. «اليَوم» — السِجِلُّ يَكتُبُ كُلَّ طَلَبٍ بِسِلسِلَتِه عِندَ Information ─────────────────────

    [Fact]
    public void Today_the_host_log_writes_framework_request_lines_at_information_with_no_override()
    {
        var hosting = Hosting();
        Assert.Single(Regex.Matches(hosting, @"\.MinimumLevel\.Information\(\)"));
        Assert.DoesNotMatch(@"MinimumLevel\.Override\(", hosting);
        Assert.Contains("app.UseSerilogRequestLogging();", hosting, StringComparison.Ordinal);
    }
}
