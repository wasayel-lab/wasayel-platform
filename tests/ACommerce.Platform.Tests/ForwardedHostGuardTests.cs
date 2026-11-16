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

// ═══ X-Forwarded-Host لا يُقرَأُ ← 400 قَبلَ أَن يَفُكَّه الإطار (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-2 وC2z-PATHS-R1-03؛ ADR-050 §١٣) ═══
//
// **المَقيسُ قَبل** (`61dab36`، وأُعيدَ عَلى خادِمي): خَلفَ العامِلِ (`ForwardedHeaders__AllowedHosts=R,*.R`) و`Host: space.hf.example` — `X-Forwarded-Host: xn--zz.R`
// و`xn--mgbh0fb-vendor.R` ← **500** (`ArgumentException` مِن `ForwardedHeadersMiddleware.ApplyForwarders`). **والآن** بِالتَركيبِ الحَقيقيِّ بِاسمِه وسِياسَةِ المَنَصَّة:
// كُلُّ قيمَةٍ لا تُقرَأُ في رَأسٍ مُصَدَّقٍ ← 400، وما يُقرَأُ كَما كان (التَوصيفُ `ForwardedFrontCharacterizationTests` بِبايتاتِه)، وبِلا قائِمَةٍ لا يُسأَلُ الرَأس.

file sealed class GuardedFront : IAsyncDisposable
{
    public const string Internal = "space-owner.hf.example";
    private readonly WebApplication _app;
    private readonly int _port;
    private GuardedFront(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<GuardedFront> StartAsync(string allowed)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var policy = new ForwardedHeadersPolicy { AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(allowed) };
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = "example.test" });
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            app.UsePlatformRequestFront();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Scheme}://{ctx.Request.Host}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new GuardedFront(app, port);
        }
    }

    public async Task<int> GetAsync(string? forwardedHost, string path = "/")
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}{path}");
        request.Headers.Host = Internal;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        if (forwardedHost is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);
        using var response = await client.SendAsync(request);
        return (int)response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ForwardedHostGuardTests(ITestOutputHelper output)
{
    private const string Worker = "example.test,*.example.test";

    [Theory]
    [InlineData("xn--zz.example.test", "/")]
    [InlineData("xn--zz.example.test", "/ashare")]
    [InlineData("xn--mgbh0fb-vendor.example.test", "/")]
    [InlineData("ashare.example.test, xn--zz.example.test", "/")]
    [InlineData("xn--zz.example.test, ashare.example.test", "/")]
    [InlineData("example.test:99999", "/")]
    public async Task An_unreadable_forwarded_host_behind_the_worker_is_400_not_500(string forwardedHost, string path)
    {
        await using var front = await GuardedFront.StartAsync(Worker);
        var got = await front.GetAsync(forwardedHost, path);
        output.WriteLine($"X-Forwarded-Host {forwardedHost} · {path} ⇒ {got}");
        Assert.Equal(400, got);
    }

    /// <summary>وبِلا قائِمَةٍ لا يُقرَأُ الرَأسُ ولا يُسأَل — الطَلَبُ يُخدَمُ بِاسمِه الداخِلِيّ كَما كان.</summary>
    [Fact]
    public async Task Without_a_named_list_the_header_is_neither_read_nor_judged()
    {
        await using var front = await GuardedFront.StartAsync("");
        Assert.Equal(200, await front.GetAsync("xn--zz.example.test"));
    }

    [Theory]
    // رَأسُ Host الخامُ يُسأَلُ دائِماً
    [InlineData(Worker, "xn--zz.example.test", null, false)]
    [InlineData("", "xn--zz.example.test", null, false)]
    [InlineData(Worker, GuardedFront.Internal, null, true)]
    // والمُمَرَّرُ حينَ يُصَدَّقُ وَحدَه — كُلُّ قيمَة
    [InlineData(Worker, GuardedFront.Internal, "ashare.example.test", true)]
    [InlineData(Worker, GuardedFront.Internal, "ashare.example.test:5050", true)]
    [InlineData(Worker, GuardedFront.Internal, "xn--mgbh0fb.example.test", true)]
    [InlineData(Worker, GuardedFront.Internal, "a.example.test, b.example.test", true)]
    [InlineData(Worker, GuardedFront.Internal, "xn--zz.example.test", false)]
    [InlineData(Worker, GuardedFront.Internal, "a.example.test,xn--zz.example.test", false)]
    [InlineData(Worker, GuardedFront.Internal, "example.test:65536", false)]
    [InlineData("", GuardedFront.Internal, "xn--zz.example.test", true)]
    public void The_pure_decision_reads_the_raw_host_and_every_trusted_forwarded_value(string allowed, string host, string? forwardedHost, bool readable)
    {
        var policy = new ForwardedHeadersPolicy { AllowedHosts = ForwardedHeadersPolicy.ParseAllowedHosts(allowed) };
        Assert.Equal(readable, HostHeaderGuard.ForwardedReadable(policy, host, forwardedHost));
    }
}
