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

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُسأَلَ X-Forwarded-Host قَبلَ أَن يَقرَأَه الإطار (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-2 وC2z-PATHS-R1-03) ═══
//
// **السَبَبُ القادِم**: خَلفَ العامِلِ الَّذي يوصي بِه DEPLOY (الخُطوَةُ ٦: `ForwardedHeaders__AllowedHosts=R,*.R`) يَصِلُ اسمُ كُلِّ زائِرٍ في `X-Forwarded-Host`،
// و`ForwardedHeadersMiddleware` يَفُكُّه بِـ`HostString.FromUriComponent` قَبلَ `HostHeaderGuard` — فَـ`xn--zz.R` ← **500** («Decoded string is not a valid IDN name»،
// مَقيسٌ عِندَ `61dab36`). سَيُسأَلُ الرَأسُ قَبلَ أَن يُقرَأ. **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ بِذلك** — بِالتَركيبِ الحَقيقيِّ بِاسمِه (`UsePlatformForwardedHeaders`
// بِسِياسَةِ المَنَصَّةِ ثُمَّ `UsePlatformRequestFront`) عَلى مُضيفٍ مُصَغَّر: المُضيفُ المُسَمّى يُقبَلُ مِن الرَأس، وغَيرُ المُسَمّى يُسقَط، وآخِرُ القيَمِ يَفوز، والمَنفَذُ فَوقَ
// 65535 ‏400 (يَراه الحارِسُ بَعدَ الكِتابَة)، ورَأسُ Host لا يُقرَأُ ‏400، وبِلا قائِمَةٍ لا يُقرَأُ الرَأسُ أَصلاً. ويَبقى بايتاتِه بَعدَ التَبديل (القاعِدَة ٣).

file sealed class ForwardedProbe : IAsyncDisposable
{
    public const string Internal = "space-owner.hf.example";
    private readonly WebApplication _app;
    private readonly int _port;
    private ForwardedProbe(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<ForwardedProbe> StartAsync(string allowed)
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
            return new ForwardedProbe(app, port);
        }
    }

    public async Task<(int Status, string Body)> GetAsync(string host, string? forwardedHost)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}/");
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        if (forwardedHost is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Host", forwardedHost);
        using var response = await client.SendAsync(request);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class ForwardedFrontCharacterizationTests(ITestOutputHelper output)
{
    private const string Worker = "example.test,*.example.test";

    [Theory]
    // خَلفَ العامِل: الاسمُ المُسَمّى مِن الرَأس، والجَذرُ بِاسمِه، وغَيرُ المُسَمّى يُسقَطُ إلى الاسمِ الداخِلِيّ، وآخِرُ القيَمِ يَفوز
    [InlineData(Worker, ForwardedProbe.Internal, "ashare.example.test", 200, "https://ashare.example.test")]
    [InlineData(Worker, ForwardedProbe.Internal, "ashare-vendor.example.test", 200, "https://ashare-vendor.example.test")]
    [InlineData(Worker, ForwardedProbe.Internal, "example.test", 200, "https://example.test")]
    [InlineData(Worker, ForwardedProbe.Internal, "evil.example", 200, "https://" + ForwardedProbe.Internal)]
    [InlineData(Worker, ForwardedProbe.Internal, "evil.example, ashare.example.test", 200, "https://ashare.example.test")]
    // ومَنفَذٌ فَوقَ 65535 في الرَأسِ يَراه الحارِسُ بَعدَ الكِتابَة، ورَأسُ Host لا يُقرَأُ بِلا رَأسٍ مُمَرَّر — 400 كِلاهُما
    [InlineData(Worker, ForwardedProbe.Internal, "ashare.example.test:70000", 400, "")]
    [InlineData(Worker, "xn--zz.example.test", null, 400, "")]
    // وبِلا قائِمَةٍ لا يُقرَأُ الرَأسُ أَصلاً — أَيّاً كانَت قيمَتُه
    [InlineData("", ForwardedProbe.Internal, "ashare.example.test", 200, "https://" + ForwardedProbe.Internal)]
    [InlineData("", ForwardedProbe.Internal, "xn--zz.example.test", 200, "https://" + ForwardedProbe.Internal)]
    [InlineData("", "ashare.example.test", null, 200, "https://ashare.example.test")]
    public async Task The_forwarded_front_reads_what_it_names_and_the_host_guard_answers_after_it(
        string allowed, string host, string? forwardedHost, int status, string body)
    {
        await using var probe = await ForwardedProbe.StartAsync(allowed);
        var got = await probe.GetAsync(host, forwardedHost);
        output.WriteLine($"AllowedHosts={(allowed.Length == 0 ? "—" : allowed)} · Host {host} · X-Forwarded-Host {forwardedHost ?? "—"} ⇒ {got.Status} «{got.Body}»");
        Assert.Equal(status, got.Status);
        Assert.Equal(body, got.Body);
    }

    /// <summary>والتَركيبُ المَقيسُ هُوَ تَركيبُ <c>Program.cs</c> بِاسمِه — سَطرٌ واحِدٌ بِسِياسَةِ الإقلاع، ولا <c>UseForwardedHeaders()</c> عارٍ خارِجَه.</summary>
    [Fact]
    public void Program_reads_forwarded_headers_through_the_named_front()
    {
        var program = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps/V1.App/Program.cs"));
        var code = string.Join('\n', program.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        Assert.Equal(1, Count(code, "app.UsePlatformForwardedHeaders(forwardedHeaders);"));
        Assert.Equal(0, Count(code, ".UseForwardedHeaders("));
    }

    private static int Count(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
