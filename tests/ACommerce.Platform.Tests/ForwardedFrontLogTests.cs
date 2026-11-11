using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَدُّ الواجِهَةِ الأَمامِيَّةِ بِـ400 يَترُكُ سَطراً واحِداً في السِجِلّ — بِلا القيمَةِ الخام (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·7 —
//     SEC-C2z-R2-6) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِع، وعِندي عِندَ ad0a770 بِهذا المِلَفِّ نَفسِه): `UsePlatformForwardedHeaders` مُرَكَّبٌ قَبلَ `UseSerilogRequestLogging`،
// فَكُلُّ 400 لِـ`Host` مُختَلَقٍ أَو `X-Forwarded-Host` لا يُقرَأ بِصِفرِ سَطر — وقَبلَ 6271f4f كانَ الحارِسُ داخِلَ `UsePlatformRequestFront` بَعدَ
// سِجِلِّ الطَلَباتِ فَيُرى. **والآن** سَطرٌ واحِدٌ بِمُستَوى Warning في فِئَةِ `ACommerce.Platform.Hosting.HostHeaderGuard` (كَسَطرِ `[origin]`): رَمزُه، والفِعلُ
// والمَسار، والرَأس، وطولُ القيمَةِ وعَدَدُ قيَمِها وصِنفُها — **ولا القيمَةُ نَفسُها**. وما يُقرَأُ لا يَترُكُ شَيئاً، وبِلا قائِمَةٍ لا يُسأَلُ `X-Forwarded-Host`.

file sealed class CapturedLog : ILoggerProvider
{
    public readonly ConcurrentQueue<(string Category, LogLevel Level, string Text)> Lines = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
    public void Dispose() { }

    private sealed class Sink(CapturedLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Lines.Enqueue((category, logLevel, formatter(state, exception)));
    }
}

file sealed class LoggedFront : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    public CapturedLog Log { get; }
    private LoggedFront(WebApplication app, int port, CapturedLog log) { _app = app; _port = port; Log = log; }

    public static async Task<LoggedFront> StartAsync(string? allowed)
    {
        for (var attempt = 1; ; attempt++)
        {
            var log = new CapturedLog();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(log);
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            // كَما يَفعَلُ سِجِلُّ المَنَصَّة (`HostingExtensions` — ‏Override("Microsoft.AspNetCore.Hosting", Warning)): سَطرُ «Request starting» يَقرَأُ
            // `Request.Host` فَيَرمي عَلى المُختَلَقِ ويَقطَعُ الاتِّصال — وهُوَ مُطفَأٌ في التَطبيق.
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
            var policy = new ForwardedHeadersPolicy { AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(allowed) };
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = "example.test" });
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            app.Run(ctx => ctx.Response.WriteAsync("ok"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            while (log.Lines.TryDequeue(out _)) { }   // سُطورُ الإقلاعِ لا تُعَدّ
            return new LoggedFront(app, port, log);
        }
    }

    /// <summary>طَلَبٌ خامٌ عَلى TCP — <c>HttpClient</c> لا يُرسِلُ <c>Host</c> مُختَلَقاً كَما هُوَ (كَما <c>GuardProbe</c> في <c>RedirectHardeningTests</c>).</summary>
    public async Task<int> GetAsync(string host, string? forwardedHost, string path = "/x")
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: {host}\r\n" + (forwardedHost is null ? "" : $"X-Forwarded-Host: {forwardedHost}\r\n") + "Connection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(head));
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
        var status = int.Parse((await reader.ReadLineAsync())!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        await reader.ReadToEndAsync();
        await Task.Delay(50);   // السَطرُ يُكتَبُ قَبلَ الرَدّ؛ مُهلَةٌ لِلمُزَوِّدِ لا لِلقَرار
        return status;
    }

    public IReadOnlyList<(string Category, LogLevel Level, string Text)> HostLines()
        => Log.Lines.Where(l => l.Text.StartsWith("[host]", StringComparison.Ordinal)).ToList();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ForwardedFrontLogTests(ITestOutputHelper output)
{
    private const string Worker = "example.test,*.example.test";

    [Theory]
    [InlineData(Worker, "xn--zz.example.test", null, HostHeaderGuard.HostUnreadable, "Host", "idn")]
    [InlineData(null, "xn--zz.example.test", null, HostHeaderGuard.HostUnreadable, "Host", "idn")]
    [InlineData(Worker, "space.hf.example", "xn--zz.example.test", HostHeaderGuard.ForwardedHostUnreadable, "X-Forwarded-Host", "idn")]
    [InlineData(Worker, "space.hf.example", "ashare.example.test, xn--mgbh0fb-vendor.example.test", HostHeaderGuard.ForwardedHostUnreadable, "X-Forwarded-Host", "idn")]
    [InlineData(Worker, "space.hf.example", "example.test:99999", HostHeaderGuard.ForwardedHostUnreadable, "X-Forwarded-Host", "port")]
    public async Task Each_400_of_the_front_leaves_exactly_one_warning_without_the_raw_value(string? allowed, string host, string? forwardedHost,
        string code, string header, string shape)
    {
        await using var front = await LoggedFront.StartAsync(allowed);
        Assert.Equal(400, await front.GetAsync(host, forwardedHost));
        var lines = front.HostLines();
        foreach (var l in lines) output.WriteLine($"{l.Category} {l.Level}: {l.Text}");
        var line = Assert.Single(lines);
        Assert.Equal("ACommerce.Platform.Hosting.HostHeaderGuard", line.Category);
        Assert.Equal(LogLevel.Warning, line.Level);
        var raw = forwardedHost ?? host;
        Assert.Equal($"[host] {code} GET /x {header} length={raw.Length} values={raw.Split(',').Length} shape={shape}", line.Text);
        Assert.DoesNotContain("xn--", line.Text, StringComparison.Ordinal);       // القيمَةُ الخامُ لا تُكتَب
        Assert.DoesNotContain("example.test", line.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Worker, "space.hf.example", "ashare.example.test")]
    [InlineData(Worker, "example.test", null)]
    [InlineData(null, "space.hf.example", "xn--zz.example.test")]   // بِلا قائِمَةٍ لا يُقرَأُ الرَأسُ فَلا يُسأَلُ ولا يُسَجَّل
    public async Task What_the_front_reads_leaves_no_line(string? allowed, string host, string? forwardedHost)
    {
        await using var front = await LoggedFront.StartAsync(allowed);
        Assert.Equal(200, await front.GetAsync(host, forwardedHost));
        Assert.Empty(front.HostLines());
    }

    [Theory]
    [InlineData("ashare.example.test", "other")]
    [InlineData("xn--zz.example.test", "idn")]
    [InlineData("example.test:70000", "port")]
    [InlineData("example.test:", "port")]
    [InlineData("مثال.example.test", "non_ascii")]
    [InlineData("a\u0001b", "control")]
    [InlineData(null, "other")]
    public void The_shape_is_a_closed_lexicon(string? raw, string want)
        => Assert.Equal(want, HostHeaderGuard.ShapeOf(raw));
}
