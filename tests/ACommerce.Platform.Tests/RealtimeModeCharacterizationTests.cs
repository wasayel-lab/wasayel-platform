using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
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

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَعودَ لِوَضعِ المَسارِ نَواقِلُه — WebSocket وَحدَه حينَ يُهَيَّأُ النِطاق، والمُصافَحَةُ يَحكُمُها أَصلُها في الوَضعَين
//     (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·3 — SEC-C2z-R2-3 وC2z-PATHS-R2-05 وC2z-PATHS-R3-02) ═══
//
// `e8edcd6` جَعَلَ `/realtime` و`/_blazor` WebSocket وَحدَه في كُلِّ نَشر، والهُجومُ الَّذي يُغلِقُه (استِطلاعٌ طَويلٌ يَحمِلُ كَعكَةَ `Lax` مِن مُضيفٍ شَقيق) يَحتاجُ
// مُضيفاً شَقيقاً مِن المَوقِعِ نَفسِه — لا يوجَدُ إلّا حينَ يُهَيَّأُ `MultiTenancy:BaseDomain`. وهذا المِلَفُّ يُثَبِّتُ بِالتَركيبِ الحَقيقيِّ بِاسمِه (`MapPlatformHub`
// و`AddPlatformInteractiveServer`) ما لا يَتَغَيَّرُ حينَ يَعودُ لِوَضعِ المَسارِ استِطلاعُه: (١) بِنِطاقٍ مُهَيَّإٍ يَعرِضُ المَركَزانِ WebSockets وَحدَه، والاستِطلاعُ الطَويلُ
// وSSE ← 404 قَبلَ المَركَز؛ (٢) والمُصافَحَةُ في الوَضعَين: مِن أَصلِها 101، ومِن أَصلٍ آخَرَ 403؛ و`negotiate` مِن أَصلٍ آخَرَ 403 في الوَضعَين. ويَبقى بِبَصمَتِه.

file sealed class ModeHub : Hub;

file sealed class ModeProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public int Port { get; }
    private ModeProbe(WebApplication app, int port) { _app = app; Port = port; }

    public static async Task<ModeProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
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
            app.MapPlatformHub<ModeHub>("/realtime");
            app.MapRazorComponents<ACommerce.Templates.Customer.Marketplace.Components.App>().AddPlatformInteractiveServer();
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new ModeProbe(app, port);
        }
    }

    public async Task<(int Status, string Body)> SendAsync(HttpMethod method, string host, string pathAndQuery, string? origin = null, string? accept = null)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{Port}{pathAndQuery}");
        request.Headers.Host = host;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
        if (method == HttpMethod.Post) request.Content = new StringContent("");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return ((int)response.StatusCode, (int)response.StatusCode == 200 && accept is not null ? "" : await response.Content.ReadAsStringAsync());
        }
        catch (TaskCanceledException) { return (0, "timeout"); }
    }

    public async Task<int> HandshakeAsync(string host, string origin, string path)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", origin);
        ws.Options.SetRequestHeader("Host", host);
        ws.Options.CollectHttpResponseDetails = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}{path}"), cts.Token); }
        catch (WebSocketException) { return (int)ws.HttpStatusCode; }
        var code = (int)ws.HttpStatusCode;
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
        return code;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RealtimeModeCharacterizationTests(ITestOutputHelper output)
{
    private const string Store = "ashare.example.test";
    private const string Sibling = "http://ejar.example.test";

    private static string[] TransportsOf(string negotiate)
        => JsonDocument.Parse(negotiate).RootElement.GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString()!).ToArray();

    private static string TokenOf(string negotiate) => JsonDocument.Parse(negotiate).RootElement.GetProperty("connectionToken").GetString()!;

    [Theory]
    [InlineData("/realtime")]
    [InlineData("/_blazor")]
    public async Task With_a_domain_each_hub_offers_websockets_only_and_answers_polling_and_sse_with_404(string hub)
    {
        await using var probe = await ModeProbe.StartAsync("example.test");
        var (status, body) = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}/negotiate?negotiateVersion=1");
        var transports = TransportsOf(body);
        var poll = await probe.SendAsync(HttpMethod.Get, Store, $"{hub}?id={TokenOf(body)}");
        var (_, again) = await probe.SendAsync(HttpMethod.Post, Store, $"{hub}/negotiate?negotiateVersion=1");
        var sse = await probe.SendAsync(HttpMethod.Get, Store, $"{hub}?id={TokenOf(again)}", accept: "text/event-stream");
        output.WriteLine($"{hub} بِنِطاق · negotiate {status} [{string.Join(", ", transports)}] · الاستِطلاع {poll.Status} · SSE {sse.Status}");
        Assert.Equal(200, status);
        Assert.Equal(["WebSockets"], transports);
        Assert.Equal(404, poll.Status);
        Assert.Equal(404, sse.Status);
    }

    [Theory]
    [InlineData("example.test", Store, "http://" + Store, "/realtime?slug=ashare", 101)]
    [InlineData("example.test", Store, "http://" + Store, "/_blazor", 101)]
    [InlineData("example.test", Store, Sibling, "/realtime?slug=ashare", 403)]
    [InlineData("example.test", Store, Sibling, "/_blazor", 403)]
    [InlineData(null, "localhost", "http://localhost", "/realtime?slug=ashare", 101)]
    [InlineData(null, "localhost", "http://localhost", "/_blazor", 101)]
    [InlineData(null, "localhost", "https://evil.example", "/realtime?slug=ashare", 403)]
    [InlineData(null, "localhost", "https://evil.example", "/_blazor", 403)]
    public async Task In_both_modes_the_handshake_is_judged_by_its_origin(string? baseDomain, string host, string origin, string path, int expected)
    {
        await using var probe = await ModeProbe.StartAsync(baseDomain);
        Assert.Equal(expected, await probe.HandshakeAsync(host, origin, path));
    }

    [Theory]
    [InlineData("example.test", Store, Sibling, "/realtime")]
    [InlineData("example.test", Store, Sibling, "/_blazor")]
    [InlineData(null, "localhost", "https://evil.example", "/realtime")]
    [InlineData(null, "localhost", "https://evil.example", "/_blazor")]
    public async Task In_both_modes_negotiate_from_another_origin_is_refused(string? baseDomain, string host, string origin, string hub)
    {
        await using var probe = await ModeProbe.StartAsync(baseDomain);
        Assert.Equal(403, (await probe.SendAsync(HttpMethod.Post, host, $"{hub}/negotiate?negotiateVersion=1", origin)).Status);
    }
}
