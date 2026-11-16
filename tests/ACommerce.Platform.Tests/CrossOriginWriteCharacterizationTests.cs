using System.Net;
using System.Text.RegularExpressions;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ أَوَّلِ الأُنبوبِ لِما يَنجو قَبلَ فَحصِ Origin لِطَلَباتِ الكِتابَة (‏2026-11-07، ADR-050 §٦ · W13-4·C2c·1 — القاعِدَة ٣) ═══
//
// **التَركيبُ الحَقيقيُّ عَلى مُضيفٍ مُصَغَّرٍ حَيّ**: `UsePlatformRequestFront` — الدالَّةُ الَّتي يَسأَلُها `UsePlatformHost` نَفسُه قَبلَ
// `UseStaticFiles` — وبَعدَها طَرَفٌ يَردُّ الفِعلَ والمَسارَ بَعدَ الحَقنِ وسلاجَ المُضيف. فَحينَ يُرَكَّبُ الفَحصُ في تِلكَ الدالَّةِ يَقيسُه هذا
// المِلَفُّ بِبَصمَتِه نَفسِها، ولا يُقاسُ تَركيبٌ مَنسوخ.
//
// **وما يُثَبَّتُ هُنا ما يَنجو مِن الفَحص**: الفِعلُ الآمِنُ (`GET`/`OPTIONS`) بِأَيِّ `Origin`؛ وطَلَبُ الكِتابَةِ بِلا `Origin` (خَطّافاتُ الخَوادِم)
// وبِـ`Origin` الطَلَبِ نَفسِه — عَلى الجَذرِ ومُضيفِ المَتجَرِ وبِمَنفَذٍ وبِحالَةِ أَحرُفٍ أُخرى ونِطاقٍ فارِغٍ و`localhost`؛ و**نِقاطُ المُزَوِّدينَ
// الأَربَعُ** (`/api/billing/paypal`، `/api/billing/paddle`، `/{slug}/payments/notify`، `/{slug}/shipping/oto/order-status`) بِـ`Origin` غَريبٍ —
// يُناديها خادِمُ المُزَوِّدِ ويَحرُسُها سِرُّه أَو بَصمَتُه لا كوكي، فَتُستَثنى بِأَسمائِها؛ و**صَفَحاتُ عَودَةِ الدَفعِ الأَربَعُ** (`/billing/paypal/return`،
// `/billing/paypal/cancel`، `/billing/paddle/return`، `/{slug}/pay/return`) — مَقيسَةٌ هُنا أَنَّها `@page` يَعودُ إلَيها المُتَصَفِّحُ بِـ`GET` ولا
// نُقطَةَ كِتابَةٍ بِمَسارِها، فَلا يَمَسُّها فَحصٌ لِلأَفعالِ غَيرِ الآمِنَة.
// **وما يَتَغَيَّرُ عَمداً لا يَرِد**: طَلَبُ الكِتابَةِ بِـ`Origin` غَيرِ أَصلِ الطَلَبِ إلى غَيرِ تِلكَ الأَربَع. والمِلَفُّ يَبقى بِبَصمَتِه بَعدَ الفَحص.

file sealed class OriginFrontProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    private OriginFrontProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<OriginFrontProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformRequestFront();
            app.Run(ctx => ctx.Response.WriteAsync(
                $"{ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}|{ctx.Items[SubdomainTenantPathMiddleware.HostSlugItem]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            return new OriginFrontProbe(app, client);
        }
    }

    /// <summary>طَلَبٌ بِفِعلِه ومُضيفِه و<c>Origin</c> (أَو بِلا <c>Origin</c> إن كانَ فارِغاً) — يُعيدُ الحالَةَ والجِسم.</summary>
    public async Task<(int Status, string Body)> SendAsync(string method, string host, string? origin, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (!string.IsNullOrEmpty(origin)) req.Headers.TryAddWithoutValidation("Origin", origin);
        if (method is "POST" or "PUT" or "PATCH") req.Content = new FormUrlEncodedContent([new("k", "v")]);
        using var res = await Client.SendAsync(req);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class CrossOriginWriteCharacterizationTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    /// <summary>(الفِعل، المُضيف، Origin، المَسار) ← (الفِعلُ والمَسارُ بَعدَ الحَقن | سلاجُ المُضيف) — والجَوابُ 200 مِن الطَرَفِ في كُلِّ صَفّ.</summary>
    [Theory]
    // الفِعلُ الآمِنُ بِأَيِّ Origin
    [InlineData("GET", "example.test", "https://evil.example.test", "/studio", "GET /studio|")]
    [InlineData("GET", "ashare.example.test", "https://ejar.example.test", "/listings/3f2c", "GET /ashare/listings/3f2c|ashare")]
    [InlineData("OPTIONS", "example.test", "https://evil.example.test", "/ashare/listings/create", "OPTIONS /ashare/listings/create|")]
    // الكِتابَةُ بِلا Origin — خَطّافاتُ الخَوادِمِ وأَدَواتُ سَطرِ الأَوامِر
    [InlineData("POST", "example.test", null, "/ashare/listings/create", "POST /ashare/listings/create|")]
    [InlineData("POST", "ashare.example.test", null, "/listings/create", "POST /ashare/listings/create|ashare")]
    [InlineData("PUT", "example.test", null, "/ashare/x", "PUT /ashare/x|")]
    [InlineData("DELETE", "example.test", null, "/ashare/x", "DELETE /ashare/x|")]
    [InlineData("PATCH", "example.test", null, "/ashare/x", "PATCH /ashare/x|")]
    // الكِتابَةُ بِـOrigin الطَلَبِ نَفسِه
    [InlineData("POST", "example.test", "http://example.test", "/ashare/listings/create", "POST /ashare/listings/create|")]
    [InlineData("POST", "example.test", "http://example.test", "/studio/apps/create", "POST /studio/apps/create|")]
    [InlineData("POST", "ashare.example.test", "http://ashare.example.test", "/listings/create", "POST /ashare/listings/create|ashare")]
    [InlineData("POST", "ashare.example.test", "http://ashare.example.test", "/ashare/auth/logout", "POST /ashare/auth/logout|ashare")]
    [InlineData("POST", "ASHARE.example.test", "http://ashare.example.test", "/listings/create", "POST /ashare/listings/create|ashare")]
    [InlineData("POST", "ashare.example.test:8443", "http://ashare.example.test:8443", "/cart/clear", "POST /ashare/cart/clear|ashare")]
    [InlineData("DELETE", "example.test", "http://example.test", "/ashare/x", "DELETE /ashare/x|")]
    // نِقاطُ المُزَوِّدينَ الأَربَعُ بِـOrigin غَريب — عَلى الجَذرِ (أَصلُ المَنَصَّةِ الَّذي تُسَجَّلُ بِه، G14) وعَلى مُضيفِ المَتجَرِ (ما سُجِّلَ قَبلَه)
    [InlineData("POST", "example.test", "https://www.paypal.com", "/api/billing/paypal", "POST /api/billing/paypal|")]
    [InlineData("POST", "example.test", "https://vendors.paddle.com", "/api/billing/paddle", "POST /api/billing/paddle|")]
    [InlineData("POST", "example.test", "https://api.moyasar.com", "/ashare/payments/notify", "POST /ashare/payments/notify|")]
    [InlineData("POST", "example.test", "https://api.tryoto.com", "/ashare/shipping/oto/order-status", "POST /ashare/shipping/oto/order-status|")]
    [InlineData("POST", "ashare.example.test", "https://api.moyasar.com", "/ashare/payments/notify", "POST /ashare/payments/notify|ashare")]
    [InlineData("POST", "ashare.example.test", "https://api.moyasar.com", "/payments/notify", "POST /ashare/payments/notify|ashare")]
    [InlineData("POST", "ashare.example.test", "https://api.tryoto.com", "/shipping/oto/order-status", "POST /ashare/shipping/oto/order-status|ashare")]
    // صَفَحاتُ عَودَةِ الدَفعِ: يَعودُ إلَيها المُتَصَفِّحُ بِـGET مِن أَصلِ البَوّابَة
    [InlineData("GET", "example.test", "https://www.paypal.com", "/billing/paypal/return?token=EC-1&PayerID=P1", "GET /billing/paypal/return?token=EC-1&PayerID=P1|")]
    [InlineData("GET", "example.test", "https://www.paypal.com", "/billing/paypal/cancel?token=EC-1", "GET /billing/paypal/cancel?token=EC-1|")]
    [InlineData("GET", "example.test", "https://checkout.paddle.com", "/billing/paddle/return?_ptxn=txn_1", "GET /billing/paddle/return?_ptxn=txn_1|")]
    [InlineData("GET", "example.test", "https://checkout.moyasar.com", "/ashare/pay/return?deal=0f8fad5bd9cb469fa16570867728950e", "GET /ashare/pay/return?deal=0f8fad5bd9cb469fa16570867728950e|")]
    [InlineData("GET", "ashare.example.test", "https://checkout.moyasar.com", "/pay/return?deal=0f8fad5bd9cb469fa16570867728950e", "GET /ashare/pay/return?deal=0f8fad5bd9cb469fa16570867728950e|ashare")]
    public async Task The_request_front_answers_as_today(string method, string host, string? origin, string path, string expected)
    {
        await using var probe = await OriginFrontProbe.StartAsync(Base);
        var (status, body) = await probe.SendAsync(method, host, origin, path);
        output.WriteLine($"{method} {host}{path} Origin={origin ?? "—"} ⇒ {status} {body}");
        Assert.Equal((200, expected), (status, body));
    }

    /// <summary>التَطويرُ المَحَلِّيُّ بِنِطاقِ <c>localhost</c>، وبِلا نِطاقٍ أَساسٍ مُهَيَّأٍ — الافتِراضيّ.</summary>
    [Theory]
    [InlineData("localhost", "ashare.localhost:5050", "http://ashare.localhost:5050", "/listings/create", "POST /ashare/listings/create|ashare")]
    [InlineData("localhost", "localhost:5050", "http://localhost:5050", "/studio/apps/create", "POST /studio/apps/create|")]
    [InlineData(null, "localhost:5050", "http://localhost:5050", "/ashare/listings/create", "POST /ashare/listings/create|")]
    [InlineData(null, "localhost:5050", null, "/ashare/listings/create", "POST /ashare/listings/create|")]
    [InlineData("", "example.test", "http://example.test", "/admin/tenants/create", "POST /admin/tenants/create|")]
    public async Task Same_origin_and_originless_writes_pass_under_every_base_domain(string? baseDomain, string host, string? origin, string path, string expected)
    {
        await using var probe = await OriginFrontProbe.StartAsync(baseDomain);
        var (status, body) = await probe.SendAsync("POST", host, origin, path);
        output.WriteLine($"[{baseDomain ?? "null"}] POST {host}{path} Origin={origin ?? "—"} ⇒ {status} {body}");
        Assert.Equal((200, expected), (status, body));
    }

    // ─── المَصدَر: ما يُرَكِّبُه المُضيفُ، ونِقاطُ المُزَوِّدين، وصَفَحاتُ العَودَة ─────────────────────────────────────────────

    /// <summary><c>UsePlatformHost</c> يَسأَلُ <c>UsePlatformRequestFront</c> قَبلَ <c>UseStaticFiles</c> و<c>UseRouting</c>، والدالَّةُ تُرَكِّبُ الحاقِن —
    /// فَما يَقيسُه المُضيفُ المُصَغَّرُ أَعلاه هُوَ ما يُرَكَّبُ في الإنتاج.</summary>
    [Fact]
    public void The_platform_host_mounts_the_request_front_before_static_files_and_routing()
    {
        var code = WriteEndpointGuardTests.StripComments(
            StudioConsentCharacterizationTests.Source("libs", "core", "ACommerce.Platform.Hosting", "HostingExtensions.cs"));
        var host = Body(code, "public static WebApplication UsePlatformHost(");
        var front = Body(code, "public static IApplicationBuilder UsePlatformRequestFront(");
        int At(string s, string needle) => s.IndexOf(needle, StringComparison.Ordinal);
        output.WriteLine($"UsePlatformHost: front@{At(host, "app.UsePlatformRequestFront();")} static@{At(host, "app.UseStaticFiles();")} routing@{At(host, "app.UseRouting();")}");
        output.WriteLine($"UsePlatformRequestFront: subdomain@{At(front, "app.UseSubdomainTenantPath();")}");
        Assert.True(At(host, "app.UsePlatformRequestFront();") > 0);
        Assert.True(At(host, "app.UsePlatformRequestFront();") < At(host, "app.UseStaticFiles();"));
        Assert.True(At(host, "app.UseStaticFiles();") < At(host, "app.UseRouting();"));
        Assert.True(At(front, "app.UseSubdomainTenantPath();") > 0);
    }

    /// <summary>نِقاطُ المُزَوِّدينَ الأَربَع: كُلٌّ <c>MapPost</c> واحِدٌ بِمَسارِه، ولا يَقرَأُ جِسمُه جَلسَةً — يُناديها خادِمٌ بِسِرٍّ أَو بَصمَة، لا مُتَصَفِّحٌ بِكوكي.</summary>
    [Fact]
    public void The_four_provider_callbacks_are_posts_that_read_no_session()
    {
        var callbacks = new[] { "/api/billing/paypal", "/api/billing/paddle", "/{slug}/payments/notify", "/{slug}/shipping/oto/order-status" };
        var posts = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => IsWrite(e.Body)).ToList();
        output.WriteLine($"نِقاطُ الكِتابَةِ المَقروءَة: {posts.Count}");
        Assert.True(posts.Count > 100, $"أَداةٌ عَمياء: {posts.Count} نُقطَةَ كِتابَة.");
        string[] sessionReads = ["AuthSession", "StudioAuth", "ResolveToken(", "ResolveCookieBase(", "Request.Cookies", "RequireAuth", "AuthFilter"];
        foreach (var route in callbacks)
        {
            var found = posts.Where(e => e.Route == route).ToList();
            output.WriteLine($"{route}: {found.Count} نُقطَة — {string.Join(", ", found.Select(f => f.File))}");
            Assert.Single(found);
            var reads = sessionReads.Where(s => found[0].Body.Contains(s, StringComparison.Ordinal)).ToList();
            Assert.True(reads.Count == 0, $"{route} يَقرَأُ جَلسَة: {string.Join(", ", reads)}");
        }
    }

    /// <summary>صَفَحاتُ عَودَةِ الدَفعِ الأَربَع: <c>@page</c> كُلٌّ في مِلَفِّه، ولا نُقطَةَ كِتابَةٍ (minimal API ولا Wolverine) بِمَسارِها — يَعودُ إلَيها المُتَصَفِّحُ
    /// بِـ<c>GET</c>، فَلا يَمَسُّها فَحصُ الأَفعالِ غَيرِ الآمِنَة.</summary>
    [Fact]
    public void The_four_payment_return_pages_are_get_pages_with_no_write_twin()
    {
        var pages = new (string Route, string File)[]
        {
            ("/billing/paypal/return", "PayPalReturn.razor"),
            ("/billing/paypal/cancel", "PayPalCancel.razor"),
            ("/billing/paddle/return", "PaddleReturn.razor"),
            ("/{slug}/pay/return", "GatewayPayReturn.razor"),
        };
        var writes = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => IsWrite(e.Body)).Select(e => e.Route).ToList();
        var wolverineWrites = EntitlementContractTests.SourceFiles()
            .SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripComments(f.Text),
                @"Wolverine(?:Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""").Select(m => m.Groups["route"].Value))
            .ToList();
        output.WriteLine($"نِقاطُ كِتابَة: minimal {writes.Count} · Wolverine {wolverineWrites.Count}");
        Assert.True(writes.Count > 100 && wolverineWrites.Count > 0, "أَداةٌ عَمياء.");
        foreach (var (route, file) in pages)
        {
            var razor = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", file);
            var directives = Regex.Matches(razor, @"^@page\s+""(?<r>[^""]+)""", RegexOptions.Multiline).Select(m => m.Groups["r"].Value).ToList();
            output.WriteLine($"{file}: {string.Join(" · ", directives)}");
            Assert.Contains(route, directives, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(route, writes, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(route, wolverineWrites, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsWrite(string body) => Regex.IsMatch(body, @"^\.Map(?:Post|Put|Delete|Patch)\s*\(");

    private static string Body(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"لَم يُعثَر عَلى «{signature}»");
        var open = code.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[start..(i + 1)];
        }
        return code[start..];
    }
}
