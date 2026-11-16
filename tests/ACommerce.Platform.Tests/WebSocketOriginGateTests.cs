using System.Net;
using System.Net.Sockets;
using System.Text;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُصافَحَةُ WebSocket مِن أَصلٍ آخَرَ تُرَدُّ 403 قَبلَ أَن يَبلُغَ المَركَز (‏2026-11-11، W13-4·C2z·2 — ADR-050 §١٥) ═══
//
// **المَقيسُ قَبل** (خَمسَةُ مُراجِعين، BaseDomain=localhost): `GET /realtime?slug={store}` بِـ`Upgrade: websocket` و`Origin` مُضيفٍ شَقيقٍ وكَعكَةِ الضَحِيَّة ← `101`
// ثُمَّ `{}` والاتِّصالُ قائِم، و`unread_changed` يَصِلُ ذلك المِقبَس؛ و`/_blazor` مِثلُه. **والآن**: الأُنبوبُ الحَقيقيُّ (`UsePlatformRequestFront`) عَلى مُضيفٍ
// مُصَغَّرٍ يَرُدُّ المُصافَحَةَ 403 مِن أَصلٍ غَيرِ أَصلِها — شَقيقاً كانَ أَو غَريباً أَو `null` — ويَقبَلُها مِن أَصلِها وبِلا أَصل (التَوصيفُ يُثَبِّتُ ذلك بِبايتاتِه).

file sealed class HandshakeProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private HandshakeProbe(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<HandshakeProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformRequestFront();
            app.UseWebSockets();
            app.Run(async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { await ctx.Response.WriteAsync("plain"); return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                await ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            });
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new HandshakeProbe(app, port);
        }
    }

    public async Task<int> HandshakeAsync(string host, string? origin, string path, string upgrade = "websocket")
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: {host}\r\n" + (origin is null ? "" : $"Origin: {origin}\r\n")
                 + $"Connection: Upgrade\r\nUpgrade: {upgrade}\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nContent-Length: 0\r\n\r\n";
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

public class WebSocketOriginGateTests(ITestOutputHelper output)
{
    [Theory]
    // مُضيفٌ شَقيقٌ تَحتَ النِطاقِ نَفسِه (مَوقِعٌ واحِد — كَعكَةُ Lax تَركَب)، ومُضيفُ الدَورِ مِن مُضيفِ المَتجَر، والجَذرُ مِن المَتجَر
    [InlineData("example.test", "ashare.example.test", "http://ejar.example.test", "/realtime?slug=ashare")]
    [InlineData("example.test", "ashare.example.test", "http://ejar.example.test", "/_blazor")]
    [InlineData("example.test", "ashare-vendor.example.test", "http://ashare.example.test", "/realtime?slug=ashare")]
    [InlineData("example.test", "example.test", "http://ashare.example.test", "/_blazor")]
    // أَصلٌ غَريب، و`null` (صَفحَةٌ مُعتِمَة)، ومُخَطَّطٌ آخَر
    [InlineData("example.test", "ashare.example.test", "https://evil.example", "/realtime?slug=ashare")]
    [InlineData("example.test", "ashare.example.test", "null", "/realtime?slug=ashare")]
    [InlineData("example.test", "ashare.example.test", "https://ashare.example.test", "/realtime?slug=ashare")]
    // ووَضعُ المَسارِ مِثلُه
    [InlineData(null, "localhost", "https://evil.example", "/realtime?slug=ashare")]
    [InlineData(null, "localhost", "https://evil.example", "/_blazor")]
    public async Task A_handshake_from_another_origin_is_refused_before_the_hub(string? baseDomain, string host, string origin, string path)
    {
        await using var probe = await HandshakeProbe.StartAsync(baseDomain);
        var got = await probe.HandshakeAsync(host, origin, path);
        output.WriteLine($"{baseDomain ?? "مَسار"} · {host}{path} · Origin {origin} ⇒ {got}");
        Assert.Equal(403, got);
    }

    [Theory]
    [InlineData("WebSocket")]
    [InlineData("websocket, h2c")]
    public async Task The_upgrade_token_is_read_case_blind_among_others(string upgrade)
    {
        await using var probe = await HandshakeProbe.StartAsync("example.test");
        Assert.Equal(403, await probe.HandshakeAsync("ashare.example.test", "http://ejar.example.test", "/realtime?slug=ashare", upgrade));
    }

    [Theory]
    [InlineData("GET", "websocket", true)]
    [InlineData("get", "WebSocket", true)]
    [InlineData("GET", "foo, websocket", true)]
    [InlineData("GET", "h2c", false)]
    [InlineData("GET", "", false)]
    [InlineData("GET", null, false)]
    [InlineData("POST", "websocket", false)]
    [InlineData("GET", "websockets", false)]
    public void The_handshake_is_named_by_method_and_upgrade_token(string method, string? upgrade, bool expected)
        => Assert.Equal(expected, CrossOriginWriteGate.IsWebSocketUpgrade(method, upgrade));

    [Fact]
    public void The_pure_gate_answers_the_handshake_with_the_write_codes_and_lets_the_rest_through()
    {
        var rows = new (string? Origin, string? Code)[]
        {
            ("http://ejar.example.test", CrossOriginWriteGate.ForeignOrigin),
            ("null", CrossOriginWriteGate.OpaqueOrigin),
            ("http://ashare.example.test", null),
            (null, null),
        };
        foreach (var (origin, code) in rows)
        {
            var got = CrossOriginWriteGate.Validate("GET", origin, "http", "ashare.example.test", "/realtime", isUpgrade: true);
            output.WriteLine($"مُصافَحَة · Origin {origin ?? "—"} ⇒ {got?.Code ?? "تَمُرّ"}");
            Assert.Equal(code, got?.Code);
        }
        // وGET بِلا تَرقِيَةٍ كَما كان: لا يُسأَل
        Assert.Null(CrossOriginWriteGate.Validate("GET", "http://ejar.example.test", "http", "ashare.example.test", "/realtime", isUpgrade: false));
    }
}
