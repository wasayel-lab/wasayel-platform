using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ WebSocket النَقلُ الوَحيدُ لِـ/realtime و/_blazor — فَلا يَبلُغُ المَركَزَ إلّا مُصافَحَةٌ يَسأَلُها فَحصُ Origin (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-1؛ ADR-050 §١٥) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِع، `61dab36`، BaseDomain=localhost): خادِمُ المُهاجِمِ يُفاوِضُ بِلا أَصلٍ ولا كَعكَة ← [WebSockets, ServerSentEvents, LongPolling]؛
// ومُتَصَفِّحُ الضَحِيَّةِ عَلى مُضيفٍ شَقيقٍ يُطلِقُ الاستِطلاعَ الأَوَّلَ `GET /realtime?id=…` بِـ`Origin` شَقيقٍ وكَعكَتِه ← 200؛ والمُهاجِمُ يُرسِلُ المُصافَحَة ← 200؛
// والاستِطلاعُ التالي ← `{}` والاتِّصالُ قائِم (انضَمَّ إلى مَجموعَةِ الضَحِيَّة). **والآن** بِالتَركيبِ الحَقيقيِّ بِاسمِه عَلى مُضيفٍ مُصَغَّر: `negotiate` يَعرِضُ WebSockets
// وَحدَه، والاستِطلاعُ وSSE ← 404 ولا يَبلُغُ المَركَزَ اتِّصال، ومُصافَحَةُ WebSocket مِن أَصلِها ← 101 ومِن شَقيقٍ ← 403. والتَوصيفُ (`RealtimeTransportCharacterizationTests`)
// بِبايتاتِه.

file sealed class Joins
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public void Add() => Interlocked.Increment(ref _count);
}

file sealed class VictimHub(Joins joins) : Hub
{
    public override Task OnConnectedAsync() { joins.Add(); return Task.CompletedTask; }
}

file sealed class TransportProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public int Port { get; }
    public Joins Joins { get; }
    private TransportProbe(WebApplication app, int port, Joins joins) { _app = app; Port = port; Joins = joins; }

    public static async Task<TransportProbe> StartAsync(string? baseDomain, Action<WebApplication>? map = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var joins = new Joins();
            builder.Services.AddSingleton(joins);
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            builder.Services.AddSignalR();
            builder.Services.AddRazorComponents().AddInteractiveServerComponents();
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformRequestFront();
            app.UseRouting();
            app.UseAntiforgery();
            if (map is null)
            {
                app.MapPlatformHub<VictimHub>("/realtime");
                app.MapRazorComponents<ACommerce.Templates.Customer.Marketplace.Components.App>().AddPlatformInteractiveServer();
            }
            else map(app);
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new TransportProbe(app, port, joins);
        }
    }

    public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string host, string pathAndQuery, string? origin = null,
                                                           string? cookie = null, string? accept = null, string? body = null, int timeoutSeconds = 20)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{Port}{pathAndQuery}");
        request.Headers.Host = host;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookie is not null) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        if (method == HttpMethod.Post) request.Content = new StringContent(body ?? "");
        try
        {
            using var response = await client.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (TaskCanceledException) { return (0, "مُعَلَّق"); }
    }

    public async Task<int> HandshakeAsync(string host, string? origin, string path)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: {host}\r\n" + (origin is null ? "" : $"Origin: {origin}\r\n")
                 + "Cookie: .acommerce.auth.ashare=victim\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\n"
                 + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nContent-Length: 0\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var buffer = new byte[64];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var read = await stream.ReadAtLeastAsync(buffer, 12, throwOnEndOfStream: false, cts.Token);
        return int.Parse(Encoding.ASCII.GetString(buffer, 0, read).Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RealtimeTransportTests(ITestOutputHelper output)
{
    private const string Store = "ashare.example.test";
    private const string Sibling = "http://ejar.example.test";
    private const string VictimCookie = ".acommerce.auth.ashare=victim";
    private const string Handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";

    private static string[] TransportsOf(string negotiate)
        => JsonDocument.Parse(negotiate).RootElement.GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString()!).ToArray();

    private static string TokenOf(string negotiate) => JsonDocument.Parse(negotiate).RootElement.GetProperty("connectionToken").GetString()!;

    [Theory]
    [InlineData("/realtime")]
    [InlineData("/_blazor")]
    public async Task Negotiate_offers_websockets_and_nothing_else(string hub)
    {
        await using var probe = await TransportProbe.StartAsync("example.test");
        var (status, body) = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}/negotiate?negotiateVersion=1");
        var transports = TransportsOf(body);
        output.WriteLine($"{hub}/negotiate ⇒ {status} · [{string.Join(", ", transports)}]");
        Assert.Equal(200, status);
        Assert.Equal(["WebSockets"], transports);
    }

    /// <summary>
    /// <para><b>صَفُّ G2 بِالنَقلِ الَّذي لَم يُسأَل</b>: المُهاجِمُ يُفاوِض، ومُتَصَفِّحُ الضَحِيَّةِ يُطلِقُ الاستِطلاعَ الأَوَّلَ بِكَعكَتِه (مِن صَفحَةِ مُضيفٍ شَقيقٍ، أَو
    /// <c>no-cors</c> بِلا <c>Origin</c> أَصلاً)، والمُهاجِمُ يُرسِلُ المُصافَحَةَ ويَستَطلِع — فَلا يَبلُغُ المَركَزَ اتِّصال (<c>OnConnectedAsync</c> صِفر).</para>
    /// </summary>
    [Theory]
    [InlineData("/realtime?slug=ashare&", Sibling)]
    [InlineData("/realtime?slug=ashare&", null)]
    [InlineData("/_blazor?", Sibling)]
    [InlineData("/_blazor?", null)]
    public async Task A_long_poll_carrying_the_victims_cookie_never_reaches_the_hub(string connect, string? origin)
    {
        await using var probe = await TransportProbe.StartAsync("example.test");
        var hub = connect[..connect.IndexOf('?')];
        var (_, negotiate) = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}/negotiate?negotiateVersion=1");
        var id = TokenOf(negotiate);
        var first = await probe.SendAsync(HttpMethod.Get, Store, $"{connect}id={id}", origin, VictimCookie, timeoutSeconds: 5);
        var send = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}?id={id}", body: Handshake, timeoutSeconds: 5);
        var poll = await probe.SendAsync(HttpMethod.Get, Store, $"{hub}?id={id}", timeoutSeconds: 5);
        await Task.Delay(200);
        output.WriteLine($"{hub} · Origin {origin ?? "—"} · الاستِطلاعُ الأَوَّلُ {first.Status} «{first.Body}» · المُصافَحَة {send.Status} · التالي {poll.Status} · OnConnectedAsync {probe.Joins.Count}");
        Assert.Equal(404, first.Status);
        Assert.Contains("LongPolling transport not supported", first.Body, StringComparison.Ordinal);
        Assert.Equal(404, poll.Status);
        Assert.Equal(0, probe.Joins.Count);
    }

    [Theory]
    [InlineData("/realtime")]
    [InlineData("/_blazor")]
    public async Task Server_sent_events_never_reach_the_hub(string hub)
    {
        await using var probe = await TransportProbe.StartAsync("example.test");
        var (_, negotiate) = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}/negotiate?negotiateVersion=1");
        var sse = await probe.SendAsync(HttpMethod.Get, Store, $"{hub}?id={TokenOf(negotiate)}", Sibling, VictimCookie, accept: "text/event-stream", timeoutSeconds: 5);
        output.WriteLine($"{hub} · SSE ⇒ {sse.Status} «{sse.Body}» · OnConnectedAsync {probe.Joins.Count}");
        Assert.Equal(404, sse.Status);
        Assert.Equal(0, probe.Joins.Count);
    }

    /// <summary>والطَريقُ الباقي: مُصافَحَةُ WebSocket — مِن أَصلِها تُقبَلُ (101) عَلى مُضيفِ المَتجَرِ وفي وَضعِ المَسار، ومِن مُضيفٍ شَقيقٍ تُرَدُّ (403) كَما ثَبَّتَه 0da1342.</summary>
    [Theory]
    [InlineData("example.test", Store, "http://" + Store, "/realtime?slug=ashare", 101)]
    [InlineData("example.test", Store, "http://" + Store, "/_blazor", 101)]
    [InlineData(null, "localhost", "http://localhost", "/realtime?slug=ashare", 101)]
    [InlineData("example.test", Store, Sibling, "/realtime?slug=ashare", 403)]
    [InlineData("example.test", Store, Sibling, "/_blazor", 403)]
    public async Task The_websocket_handshake_is_the_only_way_in_and_its_origin_decides(string? baseDomain, string host, string origin, string path, int expected)
    {
        await using var probe = await TransportProbe.StartAsync(baseDomain);
        var got = await probe.HandshakeAsync(host, origin, path);
        output.WriteLine($"{baseDomain ?? "مَسار"} · {host}{path} · Origin {origin} ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary><b>العُرفُ يَحكُمُ النُقطَتَينِ بِكائِنٍ واحِد</b>: مَركَزٌ بِـ<c>MapHub</c> بِلا خِيارٍ ومَعَه <see cref="LiveConnections.WebSocketsOnly(EndpointBuilder)"/> عُرفاً —
    /// شَكلُ <c>/_blazor</c> (الإطارُ يُرَكِّبُه بِـ<c>MapHub</c> بِلا خِيار) — يَعرِضُ WebSockets وَحدَه ويَرُدُّ الاستِطلاعَ 404.</summary>
    [Fact]
    public async Task The_endpoint_convention_narrows_a_hub_mapped_without_options()
    {
        await using var probe = await TransportProbe.StartAsync(null, app => app.MapHub<VictimHub>("/plain").Add(LiveConnections.WebSocketsOnly));
        var (_, negotiate) = await probe.SendAsync(HttpMethod.Post, "localhost", "/plain/negotiate?negotiateVersion=1");
        var poll = await probe.SendAsync(HttpMethod.Get, "localhost", $"/plain?id={TokenOf(negotiate)}", timeoutSeconds: 5);
        output.WriteLine($"/plain ⇒ [{string.Join(", ", TransportsOf(negotiate))}] · الاستِطلاع {poll.Status}");
        Assert.Equal(["WebSockets"], TransportsOf(negotiate));
        Assert.Equal(404, poll.Status);
    }

    [Fact]
    public void The_convention_touches_connection_options_only()
    {
        var hub = new RouteEndpointBuilder(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/x/negotiate"), 0);
        var options = new HttpConnectionDispatcherOptions();
        hub.Metadata.Add(options);
        var page = new RouteEndpointBuilder(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/x"), 0);
        LiveConnections.WebSocketsOnly(hub);
        LiveConnections.WebSocketsOnly(page);
        Assert.Equal(HttpTransportType.WebSockets, options.Transports);
        Assert.Empty(page.Metadata);
    }
}
