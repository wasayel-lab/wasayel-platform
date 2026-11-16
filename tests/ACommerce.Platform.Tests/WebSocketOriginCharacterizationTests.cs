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

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُسأَلَ Origin عِندَ مُصافَحَةِ WebSocket (‏2026-11-11، W13-4·C2z·2 — مُراجَعَةُ C2c · SEC-C2c-2 وSEC-C2c-R2-1 وSEC-C2c-R3-2 وC2c-P2-04 وC2c-P3-02) ═══
//
// **السَبَبُ القادِم**: مُصافَحَةُ WebSocket ‏`GET`، وفَحصُ `Origin` (`CrossOriginWriteGate`) يُمَرِّرُ كُلَّ `GET` — فَصَفحَةٌ عَلى مُضيفٍ شَقيقٍ (مَوقِعٌ واحِدٌ مَعَ الضَحِيَّة،
// فَكَعكَةُ `Lax` تَركَب) تَفتَحُ `/realtime` بِجَلسَةِ الضَحِيَّةِ وتَنضَمُّ إلى مَجموعَتِه (مَقيسٌ عِندَ المُراجِعين: 101 ثُمَّ `{}` والاتِّصالُ قائِم). سَيُسأَلُ `Origin`
// عِندَ المُصافَحَة. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — بِالأُنبوبِ الحَقيقيِّ (`UsePlatformRequestFront`) عَلى مُضيفٍ مُصَغَّرٍ وبِمِقبَسٍ خام (الرَأسانِ `Host`
// و`Origin` بِأَيدينا): مُصافَحَةٌ بِأَصلِها تُقبَل (101) في وَضعِ المَسارِ وعَلى مُضيفِ المَتجَر، وبِلا `Origin` تُقبَل (عَميلٌ لَيسَ مُتَصَفِّحاً)، و`GET` بِلا تَرقِيَةٍ مِن أَيِّ
// أَصلٍ يَبلُغُ، وطَلَبُ الكِتابَةِ مِن أَصلٍ آخَرَ يُرَدُّ كَما كان. ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣)، ومَعَه `CrossOriginWriteCharacterizationTests` و`CrossOriginWriteGateTests`.

file sealed class UpgradeProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public int Port { get; }
    private UpgradeProbe(WebApplication app, int port) { _app = app; Port = port; }

    public static async Task<UpgradeProbe> StartAsync(string? baseDomain)
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
                if (ctx.WebSockets.IsWebSocketRequest)
                {
                    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                    await ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    return;
                }
                await ctx.Response.WriteAsync($"{ctx.Request.Method} {ctx.Request.Path}");
            });
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new UpgradeProbe(app, port);
        }
    }

    /// <summary>طَلَبٌ خامٌ عَلى المِقبَس — يُعيدُ رَمزَ سَطرِ الحالَة. <paramref name="upgrade"/> يُرسِلُ رُؤوسَ مُصافَحَةِ WebSocket كَما يُرسِلُها المُتَصَفِّح.</summary>
    public async Task<int> SendAsync(string method, string host, string? origin, string path, bool upgrade)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port);
        var stream = tcp.GetStream();
        var sb = new StringBuilder();
        sb.Append($"{method} {path} HTTP/1.1\r\nHost: {host}\r\n");
        if (origin is not null) sb.Append($"Origin: {origin}\r\n");
        if (upgrade)
            sb.Append("Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
        if (method == "POST") sb.Append("Content-Type: application/x-www-form-urlencoded\r\nContent-Length: 3\r\n\r\nk=v");
        else sb.Append("Content-Length: 0\r\n\r\n");
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes);
        var buffer = new byte[64];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var read = await stream.ReadAtLeastAsync(buffer, 12, throwOnEndOfStream: false, cts.Token);
        var line = Encoding.ASCII.GetString(buffer, 0, read);
        return int.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class WebSocketOriginCharacterizationTests(ITestOutputHelper output)
{
    [Theory]
    // وَضعُ المَسار (بِلا نِطاق): مُصافَحَةٌ بِأَصلِها وبِلا أَصل
    [InlineData(null, "localhost", "http://localhost", "/realtime?slug=ashare", 101)]
    [InlineData(null, "localhost", null, "/realtime?slug=ashare", 101)]
    [InlineData(null, "localhost", "http://localhost", "/_blazor", 101)]
    // مُضيفُ المَتجَر: بِأَصلِه وبِلا أَصل
    [InlineData("example.test", "ashare.example.test", "http://ashare.example.test", "/realtime?slug=ashare", 101)]
    [InlineData("example.test", "ashare.example.test", null, "/realtime?slug=ashare", 101)]
    [InlineData("example.test", "example.test", "http://example.test", "/_blazor", 101)]
    public async Task A_handshake_from_its_own_origin_or_without_one_is_accepted(string? baseDomain, string host, string? origin, string path, int want)
    {
        await using var probe = await UpgradeProbe.StartAsync(baseDomain);
        var got = await probe.SendAsync("GET", host, origin, path, upgrade: true);
        output.WriteLine($"{baseDomain ?? "مَسار"} · {host}{path} · Origin {origin ?? "—"} ⇒ {got}");
        Assert.Equal(want, got);
    }

    [Theory]
    [InlineData(null, "localhost", "https://evil.example", "GET", 200)]
    [InlineData("example.test", "ashare.example.test", "http://ejar.example.test", "GET", 200)]
    [InlineData("example.test", "ashare.example.test", "http://ejar.example.test", "POST", 403)]
    [InlineData(null, "localhost", "https://evil.example", "POST", 403)]
    public async Task A_plain_get_from_any_origin_reaches_and_a_foreign_write_is_refused(string? baseDomain, string host, string origin, string method, int want)
    {
        await using var probe = await UpgradeProbe.StartAsync(baseDomain);
        var got = await probe.SendAsync(method, host, origin, "/ashare/explore", upgrade: false);
        output.WriteLine($"{method} {host} · Origin {origin} ⇒ {got}");
        Assert.Equal(want, got);
    }
}
