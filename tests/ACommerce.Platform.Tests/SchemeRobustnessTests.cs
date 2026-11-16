using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Tenants.Server;
using ACommerce.Platform.Hosting;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Billing;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ المُخَطَّطُ لا يَكسِرُ صَفحَةً ولا يَصيرُ عُنواناً: X-Forwarded-Proto لا يُقرَأُ ← 400، وكُلُّ عُنوانٍ مُطلَقٍ مِن {http, https}، والتَحويلُ المَحَلِّيُّ غَيرُ ASCII
//     مُرَمَّز (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·2 — SEC-C2z-R3-2 وSEC-C2z-R3-3 وما بَقِيَ مِن SEC-C2z-R1-4 وC2z-PATHS-R2-02) ═══
//
// **المَقيسُ قَبل** (خادِمٌ حَيٌّ عِندَ ad0a770): `X-Forwarded-Proto: 1http` أَو `9` أَو `+x` ← **500** عَلى كُلِّ صَفحَةٍ في الوَضعَين (`UriFormatException`
// في `NavigationManager`)؛ و`javascript` ← canonical وog:url وog:image وtwitter:image بِـ`javascript://` في الوَضعَين؛ و`POST /lang/ar` بِـ`return=/ashare/مثال`
// ← **500** (Kestrel يَرفُضُ «م» في `Location`). وما لا يَتَغَيَّرُ يُثَبِّتُه `SchemeRobustnessCharacterizationTests` بِبايتاتِه.

file sealed class ProtoLog : ILoggerProvider
{
    public readonly ConcurrentQueue<string> Lines = new();
    public ILogger CreateLogger(string categoryName) => new Sink(this);
    public void Dispose() { }
    private sealed class Sink(ProtoLog owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Lines.Enqueue(formatter(state, exception));
    }
}

file sealed class ProtoGuard : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    public ProtoLog Log { get; }
    private ProtoGuard(WebApplication app, int port, ProtoLog log) { _app = app; _port = port; Log = log; }

    public static async Task<ProtoGuard> StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var log = new ProtoLog();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(log);
            builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
            var policy = new ForwardedHeadersPolicy();
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions());
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            // الصَفحَةُ تَبني ما يَبنيه NavigationManager: عُنواناً مِن المُخَطَّط — فَمُخَطَّطٌ لا يُقرَأُ يَرمي هُنا كَما رَمى هُناك (500)
            app.Run(ctx => ctx.Response.WriteAsync(new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host}/").AbsoluteUri));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            while (log.Lines.TryDequeue(out _)) { }
            return new ProtoGuard(app, port, log);
        }
    }

    public async Task<int> GetAsync(string proto)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        using var response = await client.SendAsync(request);
        return (int)response.StatusCode;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

// ‏(‏2026-11-11، مُراجَعَةُ الإغلاقِ الأَخيرِ لِـW13-4 الجَولَةُ الثانِيَة · W13-4·C2f-R2·1 — R2-SEC-1 — يُلحَق) الصَفحَةُ الحَقيقِيَّةُ (`StudioNewStore` بِحَقنِها) خَلفَ
// الواجِهَةِ الأَمامِيَّةِ ونُقطَةِ Razor الحَقيقِيَّتَين — تَركيبُ `StudioNewRedirectCharacterizationTests` نَفسُه (ذاكَ مِلَفُّ تَوصيفٍ لا يُمَسّ، فَالمُضيفُ هُنا نُسخَتُه).
file sealed class StudioNewGuard : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _port;
    private StudioNewGuard(WebApplication app, int port) { _app = app; _port = port; }

    public static async Task<StudioNewGuard> StartAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            var policy = new ForwardedHeadersPolicy();
            builder.Services.Configure<ForwardedHeadersOptions>(policy.ApplyTo);
            builder.Services.AddSingleton(new SubdomainTenancyOptions());
            builder.Services.AddRazorComponents();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<Marten.IDocumentStore>(_ => Marten.DocumentStore.For(o => o.Connection("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")));
            builder.Services.AddScoped<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth>();
            builder.Services.AddScoped<ACommerce.Templates.Customer.Marketplace.Services.Queries.StudioQueries>();
            builder.Services.AddScoped<ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioTierService>();
            builder.Services.AddScoped<ACommerce.Platform.I18n.L>();
            int port;
            using (var l = new TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UsePlatformForwardedHeaders(policy);
            app.UseRouting();
            app.UseAntiforgery();
            app.MapRazorComponents<ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioNewStore>();
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new StudioNewGuard(app, port);
        }
    }

    /// <summary>الحالَةُ وكُلُّ رُؤوسِ الجَوابِ — لِيُسأَلَ كُلُّ رَأسٍ لا <c>Location</c> وَحدَه.</summary>
    public async Task<(int Status, IReadOnlyList<(string Name, string Value)> Headers)> GetAsync(string pathAndQuery, string? proto, bool enhanced = false)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}{pathAndQuery}");
        request.Headers.Host = "localhost:5050";
        if (proto is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        if (enhanced) request.Headers.TryAddWithoutValidation("Accept", "text/html; blazor-enhanced-nav=on");
        using var response = await client.SendAsync(request);
        var headers = response.Headers.Concat(response.Content.Headers).SelectMany(h => h.Value.Select(v => (h.Key, v))).ToList();
        return ((int)response.StatusCode, headers);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public class SchemeRobustnessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("1http")]
    [InlineData("9")]
    [InlineData("+x")]
    [InlineData("https, 1http")]
    [InlineData("1http, https")]
    [InlineData("java:script")]
    // ‏(‏2026-11-11، W13-4·C2f-R1·1 — SEC-C2f-R1-1 وC2f-PATHS-R1-01 — يُلحَق) ما يَمُرُّ بِـUri.CheckSchemeName ولا يَبني عُنواناً بِمُضيفٍ ومَنفَذ — كانَ 500 عَلى كُلِّ صَفحَة
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("https,a")]
    [InlineData("a,https")]
    [InlineData("file")]
    [InlineData("net.pipe")]
    [InlineData("vsmacros")]
    public async Task An_unreadable_forwarded_proto_is_400_with_one_line_not_500(string proto)
    {
        await using var guard = await ProtoGuard.StartAsync();
        Assert.Equal(400, await guard.GetAsync(proto));
        var lines = guard.Log.Lines.Where(l => l.StartsWith("[host]", StringComparison.Ordinal)).ToList();
        foreach (var l in lines) output.WriteLine(l);
        var line = Assert.Single(lines);
        Assert.StartsWith($"[host] {HostHeaderGuard.ForwardedProtoUnreadable} GET / X-Forwarded-Proto length={proto.Length} values={proto.Split(',').Length} shape=", line);
    }

    [Theory]
    [InlineData("https")]
    [InlineData("javascript")]
    [InlineData("")]
    public async Task A_readable_forwarded_proto_reaches_the_page(string proto)
    {
        await using var guard = await ProtoGuard.StartAsync();
        Assert.Equal(200, await guard.GetAsync(proto));
        Assert.DoesNotContain(guard.Log.Lines, l => l.StartsWith("[host]", StringComparison.Ordinal));
    }

    [Fact]
    public void The_front_decision_without_a_proto_is_what_it_was()
    {
        var policy = new ForwardedHeadersPolicy();
        Assert.Null(HostHeaderGuard.ForwardedRefusal(policy, "example.test", null));
        Assert.Null(HostHeaderGuard.ForwardedRefusal(policy, "example.test", null, "https"));
        Assert.Equal(HostHeaderGuard.ForwardedProtoUnreadable, HostHeaderGuard.ForwardedRefusal(policy, "example.test", null, "1http")!.Code);
        // Host يُسأَلُ أَوَّلاً كَما كان
        Assert.Equal(HostHeaderGuard.HostUnreadable, HostHeaderGuard.ForwardedRefusal(policy, "xn--zz.example.test", null, "1http")!.Code);
    }

    /// <summary>
    /// <para><b>ما تُمَرِّرُه الواجِهَةُ يَبني الصَفحَةَ عَلى كُلِّ مُضيفٍ مَقروء، وما تَرُدُّه يَكسِرُها عَلى واحِدٍ مِنها</b> (‏2026-11-11، W13-4·C2f-R1·1 — SEC-C2f-R1-1
    /// وC2f-PATHS-R1-01). الصَفحَةُ هُنا ما تَفعَلُه الصَفحَةُ الحَقيقِيَّة: <c>NavigationManager.Initialize</c> بِعُنوانِ الأَساسِ <c>{scheme}://{host}/</c>، ثُمَّ
    /// <c>new PathString(new Uri(Nav.Uri).AbsolutePath)</c> (<c>MainLayout.razor</c>) و<c>ToBaseRelativePath</c> و<c>ToAbsoluteUri</c>. كانَ «يُقرَأُ» يَعني
    /// <see cref="Uri.CheckSchemeName"/> وَحدَه فَمَرَّ الحَرفُ الواحِدُ وfile وnet.pipe وvsmacros وكَسَرَت الصَفحَة (500) — وهذا الماسِحُ يَحمَرُّ عَلى تِلكَ القاعِدَة
    /// (مَقيسٌ عَلَيها قَبلَ التَبديل). والاتِّجاهُ الآخَرُ يَمنَعُ أَن تُرَدَّ قيمَةٌ تَبني الصَفحَة. يَطبَعُ عَدَدَ ما فَحَصَه ويَفشَلُ صِفراً (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void What_the_front_passes_forms_the_page_on_every_readable_host_and_what_it_refuses_breaks_it()
    {
        var hosts = new[] { "localhost", "localhost:5050", "127.0.0.1:5150", "[::1]:5050", "ashare.localhost:5162", "ashare-vendor.example.test",
            "xn--mgbh0fb.localhost:5150", "example.test:443" };
        Assert.All(hosts, h => Assert.True(HostHeaderGuard.IsReadable(h), h));
        var letters = Enumerable.Range('a', 26).Select(c => ((char)c).ToString()).ToList();
        var schemes = letters.Concat(letters.Select(l => l.ToUpperInvariant())).Concat(
        [
            "http", "https", "HTTP", "HTTPS", "ws", "wss", "ftp", "file", "FILE", "File", "gopher", "nntp", "news", "mailto", "uuid", "telnet", "ldap",
            "net.tcp", "net.pipe", "NET.PIPE", "vsmacros", "javascript", "data", "about", "blob", "urn", "unc", "ms-appx", "tel", "git", "ssh",
            "ab", "a1", "AB", "zz", "z9", "a.b", "a+b", "x-y", "1http", "9", "+x", "java:script",
        ]).ToList();
        var policy = new ForwardedHeadersPolicy();
        int passed = 0, refused = 0, pages = 0;
        var wrong = new List<string>();
        foreach (var scheme in schemes)
        {
            var passes = HostHeaderGuard.ForwardedRefusal(policy, "localhost:5050", null, scheme) is null;
            var forms = hosts.Select(h => (Host: h, Forms: PageForms(scheme, h))).ToList();
            pages += forms.Count;
            if (passes) passed++; else refused++;
            if (passes && forms.FirstOrDefault(f => !f.Forms) is { Host: { } broken }) wrong.Add($"«{scheme}» يَمُرُّ ويَكسِرُ الصَفحَةَ عَلى {broken}");
            if (!passes && forms.All(f => f.Forms)) wrong.Add($"«{scheme}» يُرَدُّ ويَبني الصَفحَةَ عَلى كُلِّ مُضيف");
        }
        output.WriteLine($"مُخَطَّطات: {schemes.Count} · مُضيفات: {hosts.Length} · صَفَحاتٌ مَبنِيَّة: {pages} · يَمُرّ: {passed} · يُرَدّ: {refused} · خِلاف: {wrong.Count}");
        foreach (var w in wrong) output.WriteLine("  ✗ " + w);
        Assert.True(passed > 20 && refused > 50, $"فَحصٌ أَعمى: {passed}/{refused}");
        Assert.Empty(wrong);
    }

    private static bool PageForms(string scheme, string host)
    {
        try
        {
            var root = $"{scheme}://{host}/";
            foreach (var at in new[] { root, root + "ashare/explore?q=a%20b" })
            {
                var nav = new PageNav(root, at);
                _ = new PathString(new Uri(nav.Uri).AbsolutePath);   // MainLayout.razor
                _ = nav.ToBaseRelativePath(nav.Uri);
                _ = nav.ToAbsoluteUri("ashare/login");
            }
            return true;
        }
        catch (Exception e) when (e is UriFormatException or ArgumentException or InvalidOperationException) { return false; }
    }

    private sealed class PageNav : Microsoft.AspNetCore.Components.NavigationManager
    {
        public PageNav(string root, string at) => Initialize(root, at);
    }

    private static HttpRequest Req(string scheme)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString("localhost:5050");
        return ctx.Request;
    }

    [Theory]
    [InlineData("javascript", "https")]
    [InlineData("data", "https")]
    [InlineData("gopher", "https")]
    [InlineData("HTTP", "http")]
    [InlineData("http", "http")]
    [InlineData("https", "https")]
    [InlineData("", "https")]
    public async Task Every_absolute_url_built_from_the_request_takes_http_or_https(string scheme, string want)
    {
        Assert.Equal(want, AbsoluteScheme.Of(scheme));
        Assert.Equal($"{want}://localhost:5050", PayPalOrderSurface.OriginFrom(Req(scheme)));
        Assert.Equal($"{want}://localhost:5050", StoreUrl.PlatformOrigin(Req(scheme)));   // بِلا نِطاق: أَصلُ خَطّافَي Moyasar وOTO
        var req = Req(scheme);
        req.HttpContext.Response.Body = new MemoryStream();
        await SeoHandlers.Robots(req.HttpContext);
        req.HttpContext.Response.Body.Position = 0;
        var robots = await new StreamReader(req.HttpContext.Response.Body).ReadToEndAsync();
        Assert.Contains($"\nSitemap: {want}://localhost:5050/sitemap.xml\n", robots, StringComparison.Ordinal);
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    [Fact]
    public void The_shell_tags_take_their_scheme_from_the_rule()
    {
        var app = Regex.Replace(Read("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/App.razor"), @"@\*.*?\*@", "", RegexOptions.Singleline);
        app = Regex.Replace(app, @"^\s*//.*$", "", RegexOptions.Multiline);
        Assert.Contains("var seoBase      = seoReq is null ? \"\" : $\"{ACommerce.Kit.Tenants.AbsoluteScheme.Of(seoReq.Scheme)}://{seoReq.Host}\";", app, StringComparison.Ordinal);
        Assert.DoesNotContain("{seoReq.Scheme}", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>الماسِح</b> (القاعِدَة ٢): كُلُّ سَطرِ كودٍ تَحتَ <c>libs/</c> و<c>apps/</c> يَقرَأُ مُخَطَّطَ الطَلَبِ إمّا يَمُرُّ بِالقاعِدَة (<c>AbsoluteScheme.Of(</c>
    /// أَو <c>SchemeOf(</c>) أَو هُوَ واحِدٌ مِن ثَلاثَةٍ مُسَمّاةٍ لا تَبني عُنواناً يَخرُج: فَحصُ <c>Origin</c> (مُقارَنَة)، ومُطابَقَةُ التَبويبِ في <c>MainLayout</c> (لا تُرسَم)،
    /// ومُعامِلُ <c>AnswerStoreHost</c> (يُمَرِّرُه إلى <c>SchemeOf</c>). بانٍ سادِسٌ يَنسَخُه بِحَرفِه يُحمِّرُه. يَطبَعُ ما مَسَحَه ويَفشَلُ عَلى الصِفر.</para>
    /// </summary>
    [Fact]
    public void No_url_builder_copies_the_request_scheme_raw()
    {
        var reader = new Regex(@"\b[A-Za-z]*(Request|req|request|Req)\.Scheme\b", RegexOptions.CultureInvariant);
        string[] named =
        [
            "CrossOriginWriteGate.cs: var refusal = CrossOriginWriteGate.Validate(req.Method, origin, req.Scheme,",
            "MainLayout.razor: ? $\"{navCtx.Request.Scheme}://{navCtx.Request.Host}",
            "TenantResolverMiddleware.cs: req.Host.Value, _options.BaseDomain, req.Method, req.Path.Value, req.QueryString.Value, req.Scheme, _rolePages);",
        ];
        var files = new[] { "libs", "apps" }.Select(d => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .ToList();
        var readers = new List<string>();
        foreach (var f in files)
            foreach (var line in File.ReadLines(f))
            {
                var code = line.Trim();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("@*", StringComparison.Ordinal) || code.StartsWith('*')) continue;
                if (reader.IsMatch(code)) readers.Add($"{Path.GetFileName(f)}: {code}");
            }
        var throughRule = readers.Where(r => r.Contains("AbsoluteScheme.Of(", StringComparison.Ordinal) || r.Contains("SchemeOf(", StringComparison.Ordinal)).ToList();
        var namedHits = readers.Where(r => named.Any(n => r.StartsWith(n, StringComparison.Ordinal))).ToList();
        var raw = readers.Except(throughRule).Except(namedHits).ToList();
        output.WriteLine($"مِلَفّاتٌ مَمسوحَة: {files.Count} · قُرّاءُ المُخَطَّط: {readers.Count} · بِالقاعِدَة: {throughRule.Count} · مُسَمّاة: {namedHits.Count} · بِحَرفِه: {raw.Count}");
        foreach (var r in raw) output.WriteLine("  ✗ " + r);
        Assert.True(files.Count > 100, $"فَحصٌ أَعمى: {files.Count}");
        Assert.Equal(named.Length, namedHits.Count);   // المُسَمّى مَوجودٌ لا اسمٌ بِلا قارِئ
        Assert.True(throughRule.Count >= 4, $"بِالقاعِدَة: {throughRule.Count}");
        Assert.Empty(raw);
    }

    /// <summary>
    /// <para><b>وِجهَةُ <c>/studio/new</c> بِمُخَطَّطٍ مَقروءٍ غَيرِ http(s) ‏<c>https://</c> — ولا رَأسَ في الجَوابِ يَحمِلُ مُخَطَّطَ الطَلَب</b> (‏2026-11-11،
    /// W13-4·C2f-R2·1 — R2-SEC-1). كانَ <c>Location: javascript://localhost:5050/studio/auth</c> و<c>gopher://…</c> و<c>ab://…</c> و<c>mailto:/studio/auth</c>
    /// (<c>NavigationManager</c> يَبني مِن <c>Request.Scheme</c> بِحَرفِه — مَقيسٌ عَلى هذا المُضيفِ قَبلَ التَبديل). والتَنَقُّلُ المُحَسَّنُ يَأخُذُ تَحويلاً مُعتِماً
    /// (الوِجهَةُ لا تُطابِقُ مُخَطَّطَ الطَلَب) يُتبَعُ هُنا بِالمُخَطَّطِ نَفسِه — فَيُقاسُ ما يُخَزِّنُه الإطارُ لا الرَأسُ الأَوَّلُ وَحدَه.</para>
    /// </summary>
    [Theory]
    [InlineData("javascript")]
    [InlineData("gopher")]
    [InlineData("mailto")]
    [InlineData("ab")]
    [InlineData("JavaScript")]
    [InlineData("https, javascript")]
    public async Task The_studio_new_redirect_takes_https_for_a_readable_scheme_that_is_not_http(string proto)
    {
        await using var guard = await StudioNewGuard.StartAsync();
        var raw = proto.Split(',')[^1].Trim();
        bool Carries(IReadOnlyList<(string Name, string Value)> headers)
            => headers.Any(h => h.Value.Contains(raw + ":", StringComparison.OrdinalIgnoreCase));

        var (status, headers) = await guard.GetAsync("/studio/new", proto);
        output.WriteLine($"{proto}: {status} {string.Join(" | ", headers.Where(h => h.Name is "Location").Select(h => h.Value))}");
        Assert.Equal(302, status);
        Assert.Equal("https://localhost:5050/studio/auth", Assert.Single(headers, h => h.Name == "Location").Value);
        Assert.False(Carries(headers));

        var (enhancedStatus, enhanced) = await guard.GetAsync("/studio/new", proto, enhanced: true);
        Assert.Equal(200, enhancedStatus);
        Assert.False(Carries(enhanced));
        var opaque = Assert.Single(enhanced, h => h.Name == "blazor-enhanced-nav-redirect-location").Value;
        var (followStatus, followed) = await guard.GetAsync("/" + opaque, proto);
        output.WriteLine($"{proto} (مُحَسَّن ← مُعتِم): {followStatus} {string.Join(" | ", followed.Where(h => h.Name is "Location").Select(h => h.Value))}");
        Assert.Equal(302, followStatus);
        Assert.Equal("https://localhost:5050/studio/auth", Assert.Single(followed, h => h.Name == "Location").Value);
        Assert.False(Carries(followed));
    }

    /// <summary>
    /// <para><b>الماسِحُ الثاني — قُرّاءُ <c>NavigationManager</c></b> (القاعِدَة ٢؛ ‏2026-11-11، W13-4·C2f-R2·1 — R2-SEC-1): الماسِحُ أَعلاه يَعُدُّ مَن يَقرَأُ
    /// <c>Request.Scheme</c> بِحَرفِه، و<c>NavigationManager</c> يَقرَؤُه داخِلَ الإطار — فَكانَ <c>Nav.NavigateTo(StudioAuthPage)</c> في <c>StudioNewStore</c> يَكتُبُ
    /// <c>javascript://…</c> والماسِحُ «بِحَرفِه: 0». هُنا كُلُّ عُضوٍ يُعطي عُنواناً مُطلَقاً (<c>NavigateTo(</c> و<c>.Uri</c> و<c>.BaseUri</c> و<c>ToAbsoluteUri(</c>)
    /// عَلى كُلِّ اسمٍ يُحقَنُ بِه <c>NavigationManager</c> تَحتَ <c>libs/</c> و<c>apps/</c> (وتَعليقاتُ Razor <c>@* … *@</c> تُنزَعُ قَبلَ القِراءَة): إمّا
    /// <c>NavigateTo</c> بِوِجهَةٍ تَبنيها دالَّةٌ في المِلَفِّ نَفسِه بِـ<c>AbsoluteScheme.Of(</c>، أَو واحِدٌ مِن أَربَعَةٍ مُسَمّاةٍ لا تَبني عُنواناً يَخرُجُ بِمُخَطَّطِ
    /// الطَلَب: مَسارُ الصَفحَةِ في <c>MainLayout</c> مَرَّتَين (<c>AbsolutePath</c> وَحدَه)، والانتِقالُ في جَزيرَتَي الوَكيلِ التَفاعُلِيَّتَين (الدائِرَةُ تَأخُذُ أَساسَها
    /// مِن المُتَصَفِّح، ويُنادى مِن مُعالِجِ فِعلٍ لا في التَصييرِ الأَوَّل). وما سِواها أَحمَر — ومَقيسٌ: عَلى <c>StudioNewStore</c> قَبلَ التَبديلِ «بِحَرفِه: 2».
    /// يَطبَعُ ما مَسَحَه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void No_navigation_manager_builds_an_outgoing_url_from_the_raw_scheme()
    {
        var injected = new Regex(@"@inject\s+(?:Microsoft\.AspNetCore\.Components\.)?NavigationManager\s+(\w+)|\bNavigationManager\s+(\w+)\s*(?:\{|;|=|,|\))", RegexOptions.CultureInvariant);
        string[] named =
        [
            "MainLayout.razor: var path = new Uri(Nav.Uri).AbsolutePath;",
            "MainLayout.razor: var currentPath = new Uri(Nav.Uri).AbsolutePath;",
            "AgentChatPanel.razor: Nav.NavigateTo(\"/studio/auth\", forceLoad: true);",
            "StudioAgent.razor: Nav.NavigateTo(\"/studio/auth\", forceLoad: true);",
        ];
        var files = new[] { "libs", "apps" }.Select(d => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d))
            .SelectMany(d => Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .ToList();
        int injecting = 0;
        var readers = new List<string>();
        var throughRule = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            if (f.EndsWith(".razor", StringComparison.Ordinal))
                text = Regex.Replace(text, @"@\*.*?\*@", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
            var code = string.Join('\n', text.Split('\n').Select(l => l.Trim())
                .Where(l => !(l.StartsWith("//", StringComparison.Ordinal) || l.StartsWith('*'))));
            var names = injected.Matches(code).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Distinct().ToList();
            if (names.Count == 0) continue;
            injecting++;
            var member = new Regex($@"\b(?:{string.Join('|', names.Select(Regex.Escape))})\.(?:NavigateTo\(|Uri\b|BaseUri\b|ToAbsoluteUri\()", RegexOptions.CultureInvariant);
            foreach (var line in code.Split('\n').Where(l => member.IsMatch(l)))
            {
                var hit = $"{Path.GetFileName(f)}: {line}";
                readers.Add(hit);
                // NavigateTo(Builder(…)) و«string Builder(…) => … AbsoluteScheme.Of(…) …;» في المِلَفِّ نَفسِه
                if (Regex.Match(line, @"\.NavigateTo\((\w+)\(") is { Success: true } call
                    && Regex.Match(code, $@"\bstring\s+{Regex.Escape(call.Groups[1].Value)}\s*\([^;]*?AbsoluteScheme\.Of\(", RegexOptions.Singleline).Success)
                    throughRule.Add(hit);
            }
        }
        var namedHits = readers.Where(r => named.Any(n => r.StartsWith(n, StringComparison.Ordinal))).ToList();
        var raw = readers.Except(throughRule).Except(namedHits).ToList();
        output.WriteLine($"مِلَفّاتٌ مَمسوحَة: {files.Count} · تَحقِنُ NavigationManager: {injecting} · قُرّاءٌ مُطلَقون: {readers.Count} · بِالقاعِدَة: {throughRule.Count} · مُسَمّاة: {namedHits.Count} · بِحَرفِه: {raw.Count}");
        foreach (var r in throughRule) output.WriteLine("  ✓ " + r);
        foreach (var r in raw) output.WriteLine("  ✗ " + r);
        Assert.True(files.Count > 100 && injecting > 5, $"فَحصٌ أَعمى: {files.Count}/{injecting}");
        Assert.Equal(named.Length, namedHits.Count);   // المُسَمّى مَوجودٌ لا اسمٌ بِلا قارِئ
        Assert.True(throughRule.Count >= 2, $"بِالقاعِدَة: {throughRule.Count}");
        Assert.Empty(raw);
    }

    [Theory]
    [InlineData("/ashare/مثال", "/ashare/%D9%85%D8%AB%D8%A7%D9%84")]
    [InlineData("/مثال", "/%D9%85%D8%AB%D8%A7%D9%84")]
    [InlineData("/ashare/explore?q=سكن", "/ashare/explore?q=%D8%B3%D9%83%D9%86")]
    [InlineData("/ashare/%D9%85", "/ashare/%D9%85")]
    [InlineData("/ashare/explore", "/ashare/explore")]
    [InlineData("/r/vendor/😀", "/r/vendor/%F0%9F%98%80")]
    public void A_local_redirect_target_leaves_in_ascii(string candidate, string want)
    {
        Assert.Equal(want, LocalRedirect.Ascii(candidate));
        Assert.Equal(want, LocalRedirect.Resolve(candidate, "/fallback"));
        Assert.Equal(want, LocalRedirect.Ascii(want));   // مَرَّةً ثانِيَةً لا يَتَغَيَّر
    }

    [Fact]
    public void The_role_home_redirects_leave_in_ascii()
    {
        var mte = Read("libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs");
        Assert.Contains("=> Services.LocalRedirect.Ascii(Services.Ux.StoreUrl.For(Services.Ux.StoreUrl.ViewOf(req.HttpContext), slug, null, path));", mte, StringComparison.Ordinal);
        Assert.Contains("? Services.LocalRedirect.Ascii(Services.Ux.StoreUrl.App(view, slug, role, homeRoute ?? \"\"))", mte, StringComparison.Ordinal);
        Assert.Contains("string At(string? role, string path = \"\") => Services.LocalRedirect.Ascii(Services.Ux.StoreUrl.For(view, slug, role, path));", mte, StringComparison.Ordinal);
    }
}
