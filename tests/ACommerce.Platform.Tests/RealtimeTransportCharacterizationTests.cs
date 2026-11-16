using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَصيرَ WebSocket النَقلَ الوَحيدَ لِلمَركَزَين الحَيَّين (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-1) ═══
//
// **السَبَبُ القادِم**: فَحصُ `Origin` يَسأَلُ مُصافَحَةَ WebSocket وَحدَها (`CrossOriginWriteGate`، ‏0da1342)، و`/realtime` و`/_blazor` بِنَواقِلِهِما الافتِراضِيَّةِ الثَلاثَة —
// فَالاستِطلاعُ الطَويلُ (`GET /realtime?id=…` بِلا تَرقِيَة) يَمُرُّ بِكَعكَةِ الضَحِيَّةِ مِن صَفحَةِ مُضيفٍ شَقيق، ويَنضَمُّ إلى مَجموعَتِه (مَقيسٌ عِندَ المُراجِع). سَيَصيرُ
// WebSocket نَقلَهُما الوَحيد. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — بِالتَركيبِ الحَقيقيِّ بِاسمِه (`UsePlatformRequestFront` ثُمَّ `MapPlatformHub` و
// `AddPlatformInteractiveServer`) عَلى مُضيفٍ مُصَغَّر: WebSocket مِن أَصلِ الصَفحَةِ يُقبَلُ ويَبلُغُ المَركَزَ (المُصافَحَةُ `{}` ثُمَّ `OnConnectedAsync`)، و`negotiate` مِن
// أَصلِها 200 يَعرِضُ WebSockets، ومِن أَصلٍ آخَرَ 403، و`/_blazor` كَذلك. ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣).

/// <summary>سِجِلُّ المَركَزِ المُصَغَّر: كَم مَرَّةً بَلَغَ اتِّصالٌ <c>OnConnectedAsync</c>، وهَل حَمَلَ طَلَبُه كَعكَة.</summary>
file sealed class JoinLog
{
    private int _joins, _withCookie;
    public int Joins => Volatile.Read(ref _joins);
    public int WithCookie => Volatile.Read(ref _withCookie);
    public void Record(bool cookie) { Interlocked.Increment(ref _joins); if (cookie) Interlocked.Increment(ref _withCookie); }
}

file sealed class ProbeHub(JoinLog log) : Hub
{
    public override Task OnConnectedAsync()
    {
        log.Record(Context.GetHttpContext()?.Request.Headers.Cookie.Count > 0);
        return Task.CompletedTask;
    }
}

file sealed class LiveProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public int Port { get; }
    public JoinLog Log { get; }
    public string Origin => $"http://127.0.0.1:{Port}";
    private LiveProbe(WebApplication app, int port, JoinLog log) { _app = app; Port = port; Log = log; }

    public static async Task<LiveProbe> StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var log = new JoinLog();
            builder.Services.AddSingleton(log);
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = null });
            builder.Services.AddSignalR();
            builder.Services.AddRazorComponents().AddInteractiveServerComponents();
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformRequestFront();
            app.UseRouting();
            app.UseAntiforgery();
            app.MapPlatformHub<ProbeHub>("/realtime");
            app.MapRazorComponents<ACommerce.Templates.Customer.Marketplace.Components.App>().AddPlatformInteractiveServer();
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new LiveProbe(app, port, log);
        }
    }

    public async Task<(int Status, string Body)> NegotiateAsync(string hub, string? origin)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}{hub}/negotiate?negotiateVersion=1");
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Content = new StringContent("");
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>WebSocket مِن أَصلِ الصَفحَةِ بِكَعكَة — يُعيدُ جَوابَ مُصافَحَةِ SignalR (أَو <c>null</c> إن لَم يُقبَل الاتِّصال).</summary>
    public async Task<string?> ConnectAsync(string pathAndQuery, string? handshake)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", Origin);
        ws.Options.SetRequestHeader("Cookie", ".acommerce.auth.ashare=victim");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}{pathAndQuery}"), cts.Token); }
        catch (WebSocketException) { return null; }
        if (handshake is null) { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token); return "101"; }
        await ws.SendAsync(Encoding.UTF8.GetBytes(handshake), WebSocketMessageType.Text, true, cts.Token);
        var buffer = new byte[256];
        var got = await ws.ReceiveAsync(buffer, cts.Token);
        var text = Encoding.UTF8.GetString(buffer, 0, got.Count);
        for (var i = 0; i < 100 && Log.Joins == 0; i++) await Task.Delay(50, cts.Token);
        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
        return text;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RealtimeTransportCharacterizationTests(ITestOutputHelper output)
{
    private const string Handshake = "{\"protocol\":\"json\",\"version\":1}\u001e";

    [Fact]
    public async Task A_websocket_from_the_page_origin_reaches_the_hub_with_its_cookie()
    {
        await using var probe = await LiveProbe.StartAsync();
        var reply = await probe.ConnectAsync("/realtime?slug=ashare", Handshake);
        output.WriteLine($"/realtime · WebSocket مِن أَصلِه ⇒ «{reply?.TrimEnd('\u001e')}» · OnConnectedAsync {probe.Log.Joins} · بِكَعكَة {probe.Log.WithCookie}");
        Assert.Equal("{}\u001e", reply);
        Assert.Equal(1, probe.Log.Joins);
        Assert.Equal(1, probe.Log.WithCookie);
    }

    [Theory]
    [InlineData("/realtime")]
    [InlineData("/_blazor")]
    public async Task Negotiate_from_the_page_origin_offers_websockets_and_from_another_origin_is_refused(string hub)
    {
        await using var probe = await LiveProbe.StartAsync();
        var own = await probe.NegotiateAsync(hub, probe.Origin);
        var none = await probe.NegotiateAsync(hub, null);
        var foreign = await probe.NegotiateAsync(hub, "http://ejar.example.test");
        output.WriteLine($"{hub}/negotiate · مِن أَصلِه {own.Status} · بِلا أَصل {none.Status} · مِن أَصلٍ آخَر {foreign.Status}");
        Assert.Equal(200, own.Status);
        Assert.Contains("\"transport\":\"WebSockets\"", own.Body, StringComparison.Ordinal);
        Assert.Equal(200, none.Status);
        Assert.Equal(403, foreign.Status);
    }

    [Fact]
    public async Task The_blazor_hub_accepts_a_websocket_from_the_page_origin()
    {
        await using var probe = await LiveProbe.StartAsync();
        Assert.Equal("101", await probe.ConnectAsync("/_blazor", handshake: null));
    }

    /// <summary>والتَركيبُ المَقيسُ هُوَ تَركيبُ <c>Program.cs</c> بِاسمِه — سَطرٌ واحِدٌ لِكُلِّ مَركَز، ولا <c>MapHub</c> ولا <c>AddInteractiveServerRenderMode</c> خارِجَه.</summary>
    [Fact]
    public void Program_maps_both_live_hubs_through_the_named_composition()
    {
        var program = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps/V1.App/Program.cs"));
        var code = string.Join('\n', program.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Equal(1, Count(code, "app.MapPlatformHub<RealtimeHub>(\"/realtime\");"));
        Assert.Equal(1, Count(code, ".AddPlatformInteractiveServer();"));
        Assert.Equal(0, Count(code, ".MapHub<"));
        Assert.Equal(0, Count(code, ".AddInteractiveServerRenderMode("));
    }

    private static int Count(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
