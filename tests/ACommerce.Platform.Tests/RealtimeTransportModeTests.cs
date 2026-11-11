using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما يَخدِمُه كُلُّ وَضع: بِنِطاقٍ مُهَيَّإٍ WebSocket وَحدَه، وبِلاه نَواقِلُ الإطارِ الثَلاثَة (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·3 —
//     SEC-C2z-R2-3 وC2z-PATHS-R2-05 وC2z-PATHS-R3-02) ═══
//
// **المَقيسُ قَبل** (خادِمٌ حَيٌّ عِندَ ad0a770): `/realtime/negotiate` و`/_blazor/negotiate` بِلا نِطاقٍ ← `[WebSockets]` — والاستِطلاعُ الطَويلُ وSSE ← 404
// في وَضعِ المَسارِ أَيضاً، فَجَزيرَتا الوَكيلِ التَفاعُلِيَّتانِ لا تَبدَآنِ خَلفَ ما يَحجُبُ WebSocket. والهُجومُ الَّذي يُغلِقُه التَضييقُ يَحتاجُ مُضيفاً شَقيقاً مِن المَوقِعِ
// نَفسِه (كَعكَةُ الجَلسَةِ `SameSite=Lax` — مَقيسٌ في وَضعِ المَسار)، ولا شَقيقَ بِلا نِطاق. **والآن** — يُقرَأُ مِن `SubdomainTenancyOptions` في الحاوِيَة:
// بِنِطاقٍ `[WebSockets]` والاستِطلاعُ 404؛ وبِلاه `[WebSockets, ServerSentEvents, LongPolling]` والاستِطلاعُ مِن الصَفحَةِ يَبلُغُ المَركَزَ بِكَعكَتِه كَما قَبلَ e8edcd6.
// وما لا يَتَغَيَّرُ يُثَبِّتُه `RealtimeModeCharacterizationTests` بِبايتاتِه.

file sealed class ModeJoins { private int _n; public int Count => Volatile.Read(ref _n); public void Add() => Interlocked.Increment(ref _n); }

file sealed class ModeVictimHub(ModeJoins joins) : Hub
{
    public override Task OnConnectedAsync() { joins.Add(); return Task.CompletedTask; }
}

file sealed class ModeTransportProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public int Port { get; }
    public ModeJoins Joins { get; }
    private ModeTransportProbe(WebApplication app, int port, ModeJoins joins) { _app = app; Port = port; Joins = joins; }

    public static async Task<ModeTransportProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var joins = new ModeJoins();
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
            app.MapPlatformHub<ModeVictimHub>("/realtime");
            app.MapRazorComponents<ACommerce.Templates.Customer.Marketplace.Components.App>().AddPlatformInteractiveServer();
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new ModeTransportProbe(app, port, joins);
        }
    }

    public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string host, string pathAndQuery, string? origin = null, string? body = null)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{Port}{pathAndQuery}");
        request.Headers.Host = host;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Cookie", ".acommerce.auth.ashare=victim");
        if (method == HttpMethod.Post) request.Content = new StringContent(body ?? "", Encoding.UTF8);
        try
        {
            using var response = await client.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        catch (TaskCanceledException) { return (0, "timeout"); }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RealtimeTransportModeTests(ITestOutputHelper output)
{
    private const string Handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";

    private static string[] TransportsOf(string negotiate)
        => JsonDocument.Parse(negotiate).RootElement.GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString()!).ToArray();

    private static string TokenOf(string negotiate) => JsonDocument.Parse(negotiate).RootElement.GetProperty("connectionToken").GetString()!;

    [Theory]
    [InlineData("example.test", "ashare.example.test", "/realtime", new[] { "WebSockets" })]
    [InlineData("example.test", "ashare.example.test", "/_blazor", new[] { "WebSockets" })]
    [InlineData("example.test", "example.test", "/realtime", new[] { "WebSockets" })]
    [InlineData(null, "localhost", "/realtime", new[] { "WebSockets", "ServerSentEvents", "LongPolling" })]
    [InlineData(null, "localhost", "/_blazor", new[] { "WebSockets", "ServerSentEvents", "LongPolling" })]
    [InlineData("", "localhost", "/realtime", new[] { "WebSockets", "ServerSentEvents", "LongPolling" })]
    public async Task Each_mode_offers_its_transports(string? baseDomain, string host, string hub, string[] want)
    {
        await using var probe = await ModeTransportProbe.StartAsync(baseDomain);
        var (status, body) = await probe.SendAsync(HttpMethod.Post, host, $"{hub}/negotiate?negotiateVersion=1");
        var got = TransportsOf(body);
        output.WriteLine($"{(string.IsNullOrEmpty(baseDomain) ? "مَسار" : baseDomain)} · {host}{hub} ⇒ {status} [{string.Join(", ", got)}]");
        Assert.Equal(200, status);
        Assert.Equal(want, got);
    }

    /// <summary>بِلا نِطاقٍ يَعودُ الاستِطلاعُ الطَويلُ مِن الصَفحَةِ إلى المَركَزِ بِكَعكَتِه (الطَريقُ الَّذي تَسقُطُ إلَيه الجَزيرَتانِ خَلفَ ما يَحجُبُ WebSocket)؛
    /// وبِنِطاقٍ 404 قَبلَ المَركَز.</summary>
    [Theory]
    [InlineData(null, "localhost", "http://localhost", 200, 1)]
    [InlineData("example.test", "ashare.example.test", "http://ashare.example.test", 404, 0)]
    public async Task A_long_poll_from_the_page_reaches_the_hub_only_without_a_domain(string? baseDomain, string host, string origin, int firstStatus, int joins)
    {
        await using var probe = await ModeTransportProbe.StartAsync(baseDomain);
        var (_, negotiate) = await probe.SendAsync(HttpMethod.Post, host, "/realtime/negotiate?negotiateVersion=1", origin);
        var id = TokenOf(negotiate);
        var first = await probe.SendAsync(HttpMethod.Get, host, $"/realtime?slug=ashare&id={id}", origin);
        var send = await probe.SendAsync(HttpMethod.Post, host, $"/realtime?id={id}", origin, Handshake);
        var poll = await probe.SendAsync(HttpMethod.Get, host, $"/realtime?id={id}", origin);
        for (var i = 0; i < 40 && probe.Joins.Count < joins; i++) await Task.Delay(50);
        output.WriteLine($"{baseDomain ?? "مَسار"} · الاستِطلاعُ الأَوَّلُ {first.Status} · المُصافَحَة {send.Status} · التالي {poll.Status} «{poll.Body.TrimEnd('\u001e')}» · OnConnectedAsync {probe.Joins.Count}");
        Assert.Equal(firstStatus, first.Status);
        Assert.Equal(joins, probe.Joins.Count);
    }

    [Fact]
    public void The_mode_is_read_from_the_registered_options()
    {
        Assert.False(LiveConnections.HostsConfigured(null));
        Assert.False(LiveConnections.HostsConfigured(new ServiceCollection().BuildServiceProvider()));
        Assert.False(LiveConnections.HostsConfigured(new ServiceCollection().AddSingleton(new SubdomainTenancyOptions { BaseDomain = null }).BuildServiceProvider()));
        Assert.False(LiveConnections.HostsConfigured(new ServiceCollection().AddSingleton(new SubdomainTenancyOptions { BaseDomain = "  " }).BuildServiceProvider()));
        Assert.True(LiveConnections.HostsConfigured(new ServiceCollection().AddSingleton(new SubdomainTenancyOptions { BaseDomain = "example.test" }).BuildServiceProvider()));
    }
}
