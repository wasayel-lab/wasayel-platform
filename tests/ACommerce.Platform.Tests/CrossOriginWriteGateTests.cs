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

// ═══ G9 — طَلَبُ الكِتابَةِ مِن أَصلٍ آخَرَ يُرَدّ (‏2026-11-07، ADR-050 §٦ · W13-4·C2c·1) ═══
//
// ثَلاثُ طَبَقات: الدالَّةُ النَقِيَّةُ بِرَمزَيها (مُوجَبٌ وسالِبٌ لِكُلِّ رَمز — القاعِدَة ٤)؛ والتَركيبُ الحَقيقيُّ (`UsePlatformRequestFront`) عَلى مُضيفٍ
// مُصَغَّرٍ حَيٍّ بِصُفوفِ المُضيفاتِ الشَقيقَة؛ و**كُلُّ نُقطَةِ كِتابَةٍ في المَصدَر** (الماسِحُ الواحِدُ `WriteEndpointGuardTests`) تُرسَلُ بِأَصلٍ غَريبٍ فَتُرَدّ
// وبِأَصلِها فَتَبلُغُ الطَرَف — والعَدّادُ مَطبوع، والأَداةُ تُقاسُ: بِلا الفَحصِ يَبلُغُ الطَلَبُ الغَريبُ الطَرَفَ نَفسَه (القاعِدَة ١٠).
// والتَوصيفُ `CrossOriginWriteCharacterizationTests` يَبقى بِبَصمَتِه.

file sealed class GateFrontProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    private GateFrontProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <param name="front">صادِقاً: التَركيبُ الحَقيقيّ (<c>UsePlatformRequestFront</c>)؛ كاذِباً: الحاقِنُ وَحدَه — مِعيارُ أَنّ المِجَسَّ لَيسَ أَعمى.</param>
    public static async Task<GateFrontProbe> StartAsync(string? baseDomain, bool front = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            var port = FreePort();
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            if (front) app.UsePlatformRequestFront(); else app.UseSubdomainTenantPath();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Method} {ctx.Request.Path}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            };
            return new GateFrontProbe(app, client);
        }
    }

    public async Task<(int Status, string Body)> SendAsync(string method, string host, string? origin, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
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

public class CrossOriginWriteGateTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    // ─── الدالَّةُ النَقِيَّة: رَمزانِ ثابِتانِ بِمُوجَبٍ وسالِب ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("null")]
    [InlineData("evil")]
    [InlineData("https://ashare.example.test/")]
    [InlineData("https://ashare.example.test/path")]
    [InlineData("https://ashare.example.test?x=1")]
    [InlineData("ftp://ashare.example.test")]
    [InlineData("https://user@ashare.example.test")]
    [InlineData("https://ashare.example.test, https://ashare.example.test")]
    [InlineData("")]
    public void An_unreadable_origin_on_a_write_is_opaque(string origin)
    {
        var refusal = CrossOriginWriteGate.Validate("POST", origin, "https", "ashare.example.test", "/ashare/listings/create");
        Assert.Equal(CrossOriginWriteGate.OpaqueOrigin, refusal?.Code);
        Assert.False(CrossOriginWriteGate.IsAllowed("POST", origin, "https", "ashare.example.test", "/ashare/listings/create"));
    }

    [Theory]
    [InlineData("https://evil.example.test", "https", "ashare.example.test")]
    [InlineData("https://ejar.example.test", "https", "ashare.example.test")]       // مَتجَرٌ شَقيق — same-site
    [InlineData("https://example.test", "https", "ashare.example.test")]            // الجَذرُ إلى مُضيفِ مَتجَر
    [InlineData("https://ashare.example.test", "https", "example.test")]            // مُضيفُ مَتجَرٍ إلى الجَذر
    [InlineData("https://ashare.example.test", "https", "ashare-vendor.example.test")] // المَتجَرُ إلى مُضيفِ دَورِه
    [InlineData("http://ashare.example.test", "https", "ashare.example.test")]       // المُخَطَّط
    [InlineData("https://ashare.example.test:8443", "https", "ashare.example.test")] // المَنفَذ
    [InlineData("https://ashare.example.test", "https", "ashare.example.test:8443")]
    [InlineData("https://ashare.example.test.evil.test", "https", "ashare.example.test")]
    public void A_readable_origin_that_is_not_the_request_own_is_foreign(string origin, string scheme, string host)
    {
        var refusal = CrossOriginWriteGate.Validate("POST", origin, scheme, host, "/ashare/listings/create");
        Assert.Equal(CrossOriginWriteGate.ForeignOrigin, refusal?.Code);
    }

    [Theory]
    [InlineData("https://ashare.example.test", "https", "ashare.example.test")]
    [InlineData("https://ASHARE.example.test", "https", "ashare.EXAMPLE.test")]
    [InlineData("https://ashare.example.test:443", "https", "ashare.example.test")]
    [InlineData("https://ashare.example.test", "https", "ashare.example.test:443")]
    [InlineData("http://ashare.localhost:5050", "http", "ashare.localhost:5050")]
    [InlineData("http://localhost:5050", "HTTP", "localhost:5050")]
    [InlineData("https://example.test", "https", "example.test")]
    public void The_request_own_origin_passes_both_codes(string origin, string scheme, string host)
    {
        Assert.Null(CrossOriginWriteGate.Validate("POST", origin, scheme, host, "/ashare/listings/create"));
        Assert.True(CrossOriginWriteGate.IsAllowed("DELETE", origin, scheme, host, "/ashare/x"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("get")]
    public void A_safe_method_is_not_asked_for_its_origin(string method)
    {
        Assert.Null(CrossOriginWriteGate.Validate(method, "https://evil.example.test", "https", "ashare.example.test", "/ashare/listings/create"));
        Assert.Null(CrossOriginWriteGate.Validate(method, "null", "https", "ashare.example.test", "/ashare/listings/create"));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("PROPFIND")]
    public void A_write_without_an_origin_header_goes_on_to_the_existing_guards(string method)
        => Assert.Null(CrossOriginWriteGate.Validate(method, null, "https", "ashare.example.test", "/ashare/listings/create"));

    [Theory]
    [InlineData("/api/billing/paypal", true)]
    [InlineData("/API/Billing/PayPal", true)]
    [InlineData("/api/billing/paddle", true)]
    [InlineData("/ashare/payments/notify", true)]
    [InlineData("/theme-demo/shipping/oto/order-status", true)]
    [InlineData("/api/billing/paypal/x", false)]
    [InlineData("/api/billing", false)]
    [InlineData("/ashare/payments/verify", false)]
    [InlineData("/payments/notify", false)]
    [InlineData("/a/b/payments/notify", false)]
    [InlineData("/ashare/shipping/oto", false)]
    [InlineData("/ashare/me/shipping/oto", false)]
    [InlineData("/admin/tenants/ashare/plan/paypal-capture", false)]
    [InlineData("", false)]
    public void Only_the_four_provider_callbacks_are_exempted_by_name(string path, bool exempt)
    {
        Assert.Equal(exempt, CrossOriginWriteGate.IsProviderCallback(path));
        var refusal = CrossOriginWriteGate.Validate("POST", "https://api.provider.test", "https", "example.test", path);
        Assert.Equal(exempt ? null : CrossOriginWriteGate.ForeignOrigin, refusal?.Code);
    }

    /// <summary>القائِمَةُ أَربَعٌ بِأَسمائِها، وكُلُّ اسمٍ نُقطَةُ كِتابَةٍ واحِدَةٌ في المَصدَر — لا استِثناءَ بِلا مُستَهلِك (القاعِدَة ١) ولا يُضافُ اسمٌ بِلا قَرارٍ مَرئِيّ.</summary>
    [Fact]
    public void Every_exempt_name_is_one_write_endpoint_and_the_list_is_four()
    {
        var writes = WriteEndpoints();
        output.WriteLine($"نِقاطُ الكِتابَة: {writes.Count} · المُستَثنى بِاسمِه: {string.Join(" · ", CrossOriginWriteGate.ProviderCallbacks)}");
        Assert.Equal(
            ["/api/billing/paypal", "/api/billing/paddle", "/{slug}/payments/notify", "/{slug}/shipping/oto/order-status"],
            CrossOriginWriteGate.ProviderCallbacks);
        foreach (var name in CrossOriginWriteGate.ProviderCallbacks)
            Assert.Single(writes, w => w.Route == name);
        var matched = writes.Where(w => CrossOriginWriteGate.IsProviderCallback(Concrete(w.Route))).Select(w => w.Route).ToList();
        output.WriteLine($"ما يُطابِقُه الاستِثناءُ مِن المَصدَر: {string.Join(" · ", matched)}");
        Assert.Equal(CrossOriginWriteGate.ProviderCallbacks.OrderBy(x => x), matched.OrderBy(x => x));
    }

    // ─── التَركيبُ الحَقيقيّ عَلى مُضيفٍ مُصَغَّرٍ حَيّ ───────────────────────────────────────────────────────────────────────

    /// <summary>(المُضيف، Origin، المَسار) ← الحالَةُ: 403 بِلا جِسمٍ لِلغَريب، والطَرَفُ لِأَصلِ الطَلَبِ نَفسِه — والمُضيفاتُ الشَقيقَةُ صُلبُ الحالَة.</summary>
    [Theory]
    [InlineData("ashare.example.test", "http://ejar.example.test", "/listings/create", 403)]
    [InlineData("ashare.example.test", "http://example.test", "/listings/create", 403)]
    [InlineData("ashare.example.test", "http://ashare-vendor.example.test", "/listings/create", 403)]
    [InlineData("ashare-vendor.example.test", "http://ashare.example.test", "/listings/create", 403)]
    [InlineData("ashare-vendor.example.test", "http://ashare-vendor.example.test", "/listings/create", 200)]
    [InlineData("example.test", "http://ashare.example.test", "/studio/apps/create", 403)]
    [InlineData("example.test", "http://ashare.example.test", "/admin/tenants/create", 403)]
    [InlineData("example.test", "null", "/ashare/auth/logout", 403)]
    [InlineData("example.test", "https://example.test", "/ashare/auth/logout", 403)]
    [InlineData("example.test", "http://example.test", "/ashare/auth/logout", 200)]
    [InlineData("example.test", "http://evil.test", "/_blazor/negotiate?negotiateVersion=1", 403)]
    [InlineData("example.test", "http://example.test", "/_blazor/negotiate?negotiateVersion=1", 200)]
    [InlineData("ashare.example.test", "http://evil.test", "/realtime/negotiate?negotiateVersion=1", 403)]
    [InlineData("ashare.example.test", "http://ashare.example.test", "/realtime/negotiate?negotiateVersion=1", 200)]
    public async Task The_request_front_refuses_a_foreign_write_and_serves_its_own(string host, string origin, string path, int expected)
    {
        await using var probe = await GateFrontProbe.StartAsync(Base);
        var (status, body) = await probe.SendAsync("POST", host, origin, path);
        output.WriteLine($"POST {host}{path} Origin={origin} ⇒ {status} «{body}»");
        Assert.Equal(expected, status);
        if (expected == 403) Assert.Equal("", body);
    }

    /// <summary>والمِجَسُّ لَيسَ أَعمى: الطَلَبُ الغَريبُ نَفسُه بِالحاقِنِ وَحدَه (ما كانَ قَبلَ الفَحص) يَبلُغُ الطَرَف — فَالرَدُّ مِن الفَحصِ لا مِن التَوجيه.</summary>
    [Fact]
    public async Task Without_the_gate_the_same_foreign_write_reaches_the_endpoint()
    {
        await using var bare = await GateFrontProbe.StartAsync(Base, front: false);
        await using var gated = await GateFrontProbe.StartAsync(Base);
        var before = await bare.SendAsync("POST", "ashare.example.test", "http://ejar.example.test", "/listings/create");
        var after = await gated.SendAsync("POST", "ashare.example.test", "http://ejar.example.test", "/listings/create");
        output.WriteLine($"الحاقِنُ وَحدَه ⇒ {before.Status} «{before.Body}» · التَركيبُ ⇒ {after.Status}");
        Assert.Equal((200, "POST /ashare/listings/create"), before);
        Assert.Equal(403, after.Status);
    }

    /// <summary>
    /// <para><b>G9 — كُلُّ نُقطَةِ كِتابَةٍ في المَصدَر</b> (‏MapPost/Put/Delete/Patch و<c>[WolverinePost]</c> بِالماسِحِ الواحِد) تُرسَلُ عَلى الجَذرِ
    /// بِأَصلٍ غَريبٍ فَتُرَدُّ 403 إلّا المُستَثنى بِاسمِه، وبِأَصلِها فَتَبلُغُ الطَرَف؛ وكُلُّ نُقطَةٍ تَحتَ <c>/{slug}</c> و<c>/api/{slug}</c> كَذلِكَ عَلى
    /// مُضيفِ المَتجَرِ بِأَصلِ مَتجَرٍ شَقيق. والعَدّادُ مَطبوعٌ ويَفشَلُ عَلى صِفر.</para>
    /// </summary>
    [Fact]
    public async Task Every_write_endpoint_refuses_a_foreign_origin_and_reaches_its_body_from_its_own()
    {
        var writes = WriteEndpoints();
        Assert.True(writes.Count >= 120, $"أَداةٌ عَمياء: {writes.Count} نُقطَةَ كِتابَة.");
        await using var probe = await GateFrontProbe.StartAsync(Base);
        var wrong = new List<string>();
        int checkedApex = 0, refusedApex = 0, exemptApex = 0, checkedHost = 0, refusedHost = 0, exemptHost = 0;

        foreach (var (verb, route, file) in writes)
        {
            var path = Concrete(route);
            var exempt = CrossOriginWriteGate.IsProviderCallback(path);
            var expected = $"{verb} {path}";

            checkedApex++;
            var foreign = await probe.SendAsync(verb, "example.test", "https://evil.example.test", path);
            var own = await probe.SendAsync(verb, "example.test", "http://example.test", path);
            if (own != (200, expected)) wrong.Add($"الجَذر · بِأَصلِه {verb} {route} ⇒ {own.Status} «{own.Body}» ({file})");
            if (exempt && foreign == (200, expected)) exemptApex++;
            else if (!exempt && foreign == (403, "")) refusedApex++;
            else wrong.Add($"الجَذر · غَريب {verb} {route} ⇒ {foreign.Status} ({file})");

            if (!route.StartsWith("/{slug}/", StringComparison.Ordinal) && !route.StartsWith("/api/{slug}/", StringComparison.Ordinal)) continue;
            checkedHost++;
            var sibling = await probe.SendAsync(verb, "ashare.example.test", "http://ejar.example.test", path);
            var mine = await probe.SendAsync(verb, "ashare.example.test", "http://ashare.example.test", path);
            if (mine != (200, expected)) wrong.Add($"المُضيف · بِأَصلِه {verb} {route} ⇒ {mine.Status} «{mine.Body}» ({file})");
            if (exempt && sibling == (200, expected)) exemptHost++;
            else if (!exempt && sibling == (403, "")) refusedHost++;
            else wrong.Add($"المُضيف · شَقيق {verb} {route} ⇒ {sibling.Status} ({file})");
        }

        output.WriteLine($"الجَذر — فُحِصَ: {checkedApex} · رُدَّ: {refusedApex} · مُستَثنىً بِاسمِه: {exemptApex}");
        output.WriteLine($"مُضيفُ المَتجَر — فُحِصَ: {checkedHost} · رُدَّ: {refusedHost} · مُستَثنىً بِاسمِه: {exemptHost}");
        foreach (var w in wrong) output.WriteLine("✗ " + w);
        Assert.Empty(wrong);
        Assert.Equal(CrossOriginWriteGate.ProviderCallbacks.Count, exemptApex);
        Assert.Equal(checkedApex - exemptApex, refusedApex);
        Assert.Equal(2, exemptHost);
        Assert.Equal(checkedHost - exemptHost, refusedHost);
        Assert.True(checkedHost >= 50, $"أَداةٌ عَمياء: {checkedHost} نُقطَةً تَحتَ المَتجَر.");
    }

    /// <summary>التَركيب: <c>UsePlatformRequestFront</c> يُرَكِّبُ الحاقِنَ ثُمَّ الفَحص — بَعدَه فَيَرى المَسارَ بِشَكلِ القالِب.</summary>
    [Fact]
    public void The_gate_is_mounted_right_after_the_host_rewrite_in_the_request_front()
    {
        var code = WriteEndpointGuardTests.StripComments(
            StudioConsentCharacterizationTests.Source("libs", "core", "ACommerce.Platform.Hosting", "HostingExtensions.cs"));
        var start = code.IndexOf("public static IApplicationBuilder UsePlatformRequestFront(", StringComparison.Ordinal);
        Assert.True(start > 0);
        var front = code[start..code.IndexOf("return app;", start, StringComparison.Ordinal)];
        var rewrite = front.IndexOf("app.UseSubdomainTenantPath();", StringComparison.Ordinal);
        var gate = front.IndexOf("app.UseCrossOriginWriteGate();", StringComparison.Ordinal);
        output.WriteLine($"rewrite@{rewrite} gate@{gate}");
        Assert.True(rewrite > 0 && gate > rewrite);
    }

    // ─── الأَدَوات ──────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static List<(string Verb, string Route, string File)> WriteEndpoints() =>
        WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Select(e => (Verb: Regex.Match(e.Body, @"^\.Map(?<v>Post|Put|Delete|Patch)\s*\(").Groups["v"].Value.ToUpperInvariant(), e.Route, e.File))
            .Where(e => e.Verb.Length > 0)
            .Concat(WriteEndpointGuardTests.WolverineWriteEndpoints().Select(e => (Verb: "POST", e.Route, e.File)))
            .ToList();

    /// <summary>مَسارٌ مَلموسٌ مِن القالِب: <c>{slug}</c> ← <c>ashare</c>، و<c>{…:guid}</c> ← مُعَرِّف، وما سِواه ← <c>x</c>.</summary>
    private static string Concrete(string route) =>
        Regex.Replace(route, @"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<c>:[^}]*)?\}", m =>
            m.Groups["name"].Value == "slug" ? "ashare"
            : m.Groups["c"].Value.Contains("guid", StringComparison.OrdinalIgnoreCase) ? "0f8fad5b-d9cb-469f-a165-70867728950e"
            : "x");
}
