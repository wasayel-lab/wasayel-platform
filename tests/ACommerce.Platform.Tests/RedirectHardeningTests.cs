using System.Net;
using System.Net.Sockets;
using System.Text;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ التَحويلاتُ والرُؤوس: مُخَطَّطٌ مِن قائِمَة، وتَحويلٌ مُؤَقَّت، وذَيلٌ مَحَلِّيّ، و400 لِرَأسِ Host لا يُقرَأ (‏2026-11-11، W13-4·C2z·3) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِعين): `X-Forwarded-Proto: javascript` ← `301 Location: javascript://localhost:5110/studio`؛ `ashare.R/terms` ← 301 بِلا
// `Cache-Control`؛ `StoreUrl.For(…, "/\\evil.com")` عَلى المُضيفِ ← `/\evil.com` (والمُتَصَفِّحُ يَقرَؤُه `//evil.com`)؛ `Host: xn--zz.localhost` و`localhost:99999` ← 500.
// **والآن** ما في هذا المِلَفّ؛ وما لا يَتَغَيَّرُ يُثَبِّتُه `RedirectHardeningCharacterizationTests` بِبايتاتِه.

file sealed class GuardProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private GuardProbe(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<GuardProbe> StartAsync(string? baseDomain)
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
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Host.Value}|{ctx.Request.Path}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new GuardProbe(app, port);
        }
    }

    public async Task<(int Status, string Location)> GetAsync(string host, string path, string? forwardedProto = null)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: {host}\r\n" + (forwardedProto is null ? "" : $"X-Forwarded-Proto: {forwardedProto}\r\n") + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var status = int.Parse((await reader.ReadLineAsync())!.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        var location = "";
        for (var line = await reader.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync())
            if (line.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)) location = line["Location:".Length..].Trim();
        return (status, location);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class RedirectHardeningTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("javascript", "https://example.test/studio")]
    [InlineData("data", "https://example.test/studio")]
    [InlineData("ftp", "https://example.test/studio")]
    [InlineData("HTTP", "http://example.test/studio")]
    [InlineData("Https", "https://example.test/studio")]
    public void The_redirect_scheme_comes_only_from_http_or_https(string scheme, string location)
        => Assert.Equal(location, TenantResolverMiddleware.AnswerStoreHost("ashare.example.test", Base, "GET", "/studio", "", scheme).Location);

    [Theory]
    [InlineData("/studio?from=store", "http", "http://example.test/studio?from=store")]
    [InlineData("/terms", "http", "http://example.test/terms")]
    [InlineData("/admin", "http", "http://example.test/admin")]
    public async Task A_platform_area_on_a_store_host_is_a_temporary_redirect(string path, string proto, string location)
    {
        await using var probe = await GuardProbe.StartAsync(Base);
        var (status, got) = await probe.GetAsync("ashare.example.test", path, proto == "http" ? null : proto);
        output.WriteLine($"ashare.example.test{path} · XFP {proto} ⇒ {status} {got}");
        Assert.Equal((302, location), (status, got));
    }

    [Theory]
    [InlineData(null, "\\evil.com", "/ashare/\\evil.com")]
    [InlineData(null, "/\\evil.com", "/ashare/\\evil.com")]
    [InlineData(null, "\\\\evil.com", "/ashare/\\\\evil.com")]
    [InlineData(null, "\t/evil.com", "/ashare/\t/evil.com")]
    [InlineData(null, "me/\\x", "/ashare/me/\\x")]
    [InlineData("vendor", "\\evil.com", "/ashare/r/vendor/\\evil.com")]
    public void A_tail_that_would_leave_the_host_keeps_the_store_prefix(string? role, string tail, string expected)
    {
        var got = StoreUrl.For(new HostView(Base, "ashare", "https", ""), "ashare", role, tail);
        output.WriteLine($"«{tail}» ⇒ «{got}» · مَحَلِّيّ: {ACommerce.Templates.Customer.Marketplace.Services.LocalRedirect.IsLocal(got) || got.StartsWith("/ashare/", StringComparison.Ordinal)}");
        Assert.Equal(expected, got);
        Assert.StartsWith("/ashare/", got);
        // وعَلى مُضيفِ الدَورِ مِثلُه
        Assert.StartsWith("/ashare/", StoreUrl.For(new HostView(Base, "ashare", "https", "") { HostRole = "vendor" }, "ashare", role, tail));
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/explore?category=deals", true)]
    [InlineData("/r/vendor/me", true)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("/me/\\x", false)]
    [InlineData("/me\u0000x", false)]
    [InlineData("/me\nx", false)]
    public void A_local_tail_is_named_by_its_first_two_characters_and_its_controls(string tail, bool local)
        => Assert.Equal(local, StoreUrl.IsLocalTail(tail));

    [Theory]
    [InlineData("/\\evil.com")]
    [InlineData("//evil.com")]
    [InlineData("/me\\listings")]
    [InlineData("/me\u0001")]
    public void A_role_home_route_that_could_leave_the_host_is_malformed(string route)
    {
        var violations = RoleDefinitionValidator.Validate(new RoleDefinition
        {
            Slug = "driver", Icon = "🚗", HomeRoute = route,
            Label = new LocalizedText("سائِق"), Description = new LocalizedText("سائِق مَركَبَة."),
            Permissions = ["listing.browse", "offer.submit"], Fields = [], Composition = new RoleComposition(),
        });
        Assert.Contains(violations, v => v.Code == "home_route_malformed");
    }

    [Theory]
    [InlineData(null, "xn--zz.localhost", "/ashare")]
    [InlineData(null, "xn--zz", "/ashare")]
    [InlineData(null, "localhost:99999", "/ashare")]
    [InlineData(null, "localhost:", "/")]
    [InlineData(Base, "xn--zz.example.test", "/")]
    [InlineData(Base, "xn--mgbh0fb-vendor.example.test", "/")]
    [InlineData(Base, "ashare.example.test:70000", "/explore")]
    public async Task A_host_header_that_cannot_be_read_is_400_not_500(string? baseDomain, string host, string path)
    {
        await using var probe = await GuardProbe.StartAsync(baseDomain);
        var (status, _) = await probe.GetAsync(host, path);
        output.WriteLine($"{baseDomain ?? "مَسار"} · Host {host} ⇒ {status}");
        Assert.Equal(400, status);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("ashare.example.test:5050", true)]
    [InlineData("[::1]:5050", true)]
    [InlineData("xn--mgbh0fb.localhost", true)]
    [InlineData("", true)]
    [InlineData("xn--zz.localhost", false)]
    [InlineData("localhost:65536", false)]
    [InlineData("localhost:-1", false)]
    [InlineData("localhost:5x", false)]
    public void The_host_guard_reads_like_the_framework_and_bounds_the_port(string raw, bool readable)
        => Assert.Equal(readable, HostHeaderGuard.IsReadable(raw));
}
