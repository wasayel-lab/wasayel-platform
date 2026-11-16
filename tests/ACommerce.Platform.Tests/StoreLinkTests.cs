using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using ACommerce.Widgets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَوابِطُ المَتجَرِ نَظيفَةٌ عَلى مُضيفِه — والمَسارُ بِحَرفِه في ما سِواه (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1) ═══
//
// **المَقيسُ قَبل** (عِندَ 50d6293): عَلى `ashare.R` كُلُّ رابِطٍ يَبنيه `LinkFor` أَو `Link(req, …)` أَو يُكتَبُ بِيَدٍ `/ashare/…` — يَعمَلُ (الحاقِنُ لا يَحقِنُ مَساراً
// يَحمِلُ المَتجَر) ويُظهِرُ `ashare.R/ashare/explore` في الشَريطِ والـcanonical يُعلِنُ `ashare.R/explore`؛ و«افتَح المَتجَر» الأَربَعَةُ في الاستوديو والإشرافِ
// `/{slug}` عَلى الجَذر. **والآن** `StoreUrl` (قَرارٌ نَقِيٌّ واحِد) و`MatchNavLink` لِلتَبويبِ النَشِط. وتَوصيفُ ما يَنجو `StoreLinkCharacterizationTests` بِبَصمَتِه.

public class StoreLinkTests(ITestOutputHelper output)
{
    private static HostView OnHost(string slug, string? port = "") => new("example.test", slug, "https", port);
    private static readonly HostView Apex = new("example.test", null, "https", "");

    /// <summary><b>عَلى مُضيفِ المَتجَرِ نَفسِه نَظيف، وما سِواه بِشَكلِ المَسار</b> — <c>StoreUrl.For</c>.</summary>
    [Theory]
    [InlineData("host", "ashare", null, "", "/")]
    [InlineData("host", "ashare", null, "explore", "/explore")]
    [InlineData("host", "ashare", null, "/explore/", "/explore")]
    [InlineData("host", "ashare", null, "login?err=x&as=vendor", "/login?err=x&as=vendor")]
    [InlineData("host", "ashare", null, "?deleted=1", "/?deleted=1")]
    [InlineData("host", "ashare", "", "me", "/me")]
    [InlineData("host", "ashare", "vendor", "", "/r/vendor")]
    [InlineData("host", "ashare", "vendor", "me/listings", "/r/vendor/me/listings")]
    [InlineData("host", "ASHARE", "customer", "/explore", "/r/customer/explore")]
    // مَتجَرٌ آخَرُ عَلى مُضيفِ ashare، والجَذر، وبِلا نِطاق، وبِلا سِياق — شَكلُ المَسارِ بِحَرفِه
    [InlineData("host", "ejar", null, "explore", "/ejar/explore")]
    [InlineData("host", "ejar", "vendor", "", "/ejar/r/vendor")]
    [InlineData("apex", "ashare", null, "explore", "/ashare/explore")]
    [InlineData("apex", "ashare", "vendor", "me", "/ashare/r/vendor/me")]
    [InlineData("none", "ashare", null, "", "/ashare")]
    [InlineData("slug-only", "ashare", null, "explore", "/ashare/explore")]
    public void For_is_clean_on_the_stores_own_host_and_the_path_form_elsewhere(string where, string slug, string? role, string path, string expected)
    {
        var view = where switch
        {
            "host" => OnHost("ashare"),
            "apex" => Apex,
            "slug-only" => new HostView(null, "ashare", "https", ""),   // مَتجَرُ مُضيفٍ بِلا نِطاق: لَم يَمُرَّ بِالحاقِن
            _ => default,
        };
        var got = StoreUrl.For(view, slug, role, path);
        output.WriteLine($"{where}: ({slug}, {role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary><b>«افتَح المَتجَر»</b> — مِن الجَذرِ والنِطاقُ مُهَيَّأٌ عُنوانُ مُضيفِه المُطلَق؛ وعَلى مُضيفِه <c>/</c>؛ ومُعَرِّفٌ لا يَصلُحُ مُضيفاً أَو بِلا نِطاقٍ <c>/{slug}</c>.</summary>
    [Theory]
    [InlineData("apex", "ashare", "https://ashare.example.test/")]
    [InlineData("apex-port", "theme-demo", "http://theme-demo.example.test:5050/")]
    [InlineData("host", "ashare", "/")]
    [InlineData("host", "ejar", "https://ejar.example.test/")]
    [InlineData("apex", "best-vendor", "/best-vendor")]   // يَنتَهي بِاسمِ مُضيفِ دَور — لا مُضيفَ لَه (ADR-050 §٣)
    [InlineData("apex", "shop-", "/shop-")]
    [InlineData("apex", "lang", "/lang")]
    [InlineData("none", "ashare", "/ashare")]
    public void Open_names_the_stores_own_host_when_the_domain_is_set(string where, string slug, string expected)
    {
        var view = where switch
        {
            "apex" => Apex,
            "apex-port" => new HostView("example.test", null, "http", ":5050"),
            "host" => OnHost("ashare"),
            _ => default,
        };
        var got = StoreUrl.Open(view, slug);
        output.WriteLine($"{where}: {slug} ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary><c>ViewOf</c> يَقرَأُ ما كَتَبَه الحاقِنُ والمَنفَذَ والمُخَطَّط — وبِلا طَلَبٍ فارِغ.</summary>
    [Fact]
    public void The_view_reads_what_the_injector_wrote()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("ashare.localhost:5050");
        ctx.Items[StoreHostItems.Apex] = "localhost";
        ctx.Items[StoreHostItems.Slug] = "ashare";
        Assert.Equal(new HostView("localhost", "ashare", "http", ":5050"), StoreUrl.ViewOf(ctx));
        Assert.Equal(default, StoreUrl.ViewOf(null));
        var bare = new DefaultHttpContext();
        bare.Request.Scheme = "https";
        bare.Request.Host = new HostString("example.test");
        Assert.Equal(new HostView(null, null, "https", ""), StoreUrl.ViewOf(bare));
    }

    private static readonly MethodInfo LinkMethod = typeof(MarketplaceTemplateExtensions)
        .GetMethod("Link", BindingFlags.NonPublic | BindingFlags.Static, [typeof(HttpRequest), typeof(string), typeof(string)])
        ?? throw new InvalidOperationException("‏`MarketplaceTemplateExtensions.Link` غَيرُ مَوجودَة — الأَداةُ عَمياء.");

    /// <summary><b>التَحويلُ بَعدَ استِمارَةٍ عَلى مُضيفِ المَتجَر</b> — <c>Link</c> بِجِسمِها: نَظيفٌ، والدَورُ مِن الـReferer كَما قَرَأَ الحاقِنُ صَفحَتَه.</summary>
    [Theory]
    [InlineData("http://rewalk-071351.localhost:5151/r/customer/login", "rewalk-071351", "login?stage=verify&attempt=x", "/r/customer/login?stage=verify&attempt=x")]
    [InlineData("http://rewalk-071351.localhost:5151/r/vendor/listings/new", "rewalk-071351", "me/listings", "/r/vendor/me/listings")]
    [InlineData("http://rewalk-071351.localhost:5151/rewalk-071351/r/driver/deals", "rewalk-071351", "deals/1", "/r/driver/deals/1")]
    [InlineData("http://rewalk-071351.localhost:5151/login", "rewalk-071351", "me/name", "/me/name")]
    [InlineData("", "rewalk-071351", "login?err=phone_required", "/login?err=phone_required")]
    [InlineData("http://rewalk-071351.localhost:5151/explore", "ejar", "me", "/ejar/me")]   // مَتجَرٌ آخَرُ عَلى هذا المُضيف — بِشَكلِ المَسار
    public void A_redirect_after_a_post_on_the_store_host_is_clean(string referer, string slug, string path, string location)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Method = "POST";
        ctx.Request.Host = new HostString("rewalk-071351.localhost:5151");
        ctx.Items[StoreHostItems.Apex] = "localhost";
        ctx.Items[StoreHostItems.Slug] = "rewalk-071351";
        if (referer.Length > 0) ctx.Request.Headers.Referer = referer;
        var got = (string)LinkMethod.Invoke(null, [ctx.Request, slug, path])!;
        output.WriteLine($"«{referer}» ⇒ {got}");
        Assert.Equal(location, got);
    }

    /// <summary><b>الحاقِنُ يَكتُبُ النِطاقَ عَلى كُلِّ طَلَبٍ مَتى هُيِّئ</b> — عَلى الجَذرِ ومُضيفِ المَتجَرِ ومُضيفٍ خارِجَه؛ وبِلا نِطاقٍ لا يَكتُبُ شَيئاً
    /// (فَوَضعُ المَسارِ بِلا عَنصُرٍ جَديد).</summary>
    [Theory]
    [InlineData("Example.Test.", "example.test", "/", "example.test|")]
    [InlineData("example.test:8443", "ashare.example.test:8443", "/explore", "example.test|ashare")]
    [InlineData("example.test", "ashare.evil.test", "/", "example.test|")]
    [InlineData(null, "ashare.example.test", "/", "|")]
    [InlineData("", "localhost:5050", "/ashare", "|")]
    public async Task The_injector_leaves_the_apex_for_those_after_it(string? baseDomain, string host, string path, string expected)
    {
        await using var app = await ItemsProbe.StartAsync(baseDomain);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Host = host;
        using var res = await app.Client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        output.WriteLine($"{baseDomain ?? "—"} · {host}{path} ⇒ {(int)res.StatusCode} {body}");
        Assert.Equal((HttpStatusCode.OK, expected), (res.StatusCode, body));
    }

    /// <summary><b>التَبويبُ النَشِطُ عَلى مُضيفِ المَتجَر</b>: <c>NavigationManager</c> يَرى المَسارَ بَعدَ الحَقنِ (<c>/ashare/explore</c>) والرابِطُ نَظيف (<c>/explore</c>) —
    /// <c>MatchNavLink</c> بِالعُنوانِ الظاهِرِ يُمَيِّزُه، وبِلا <c>MatchUri</c> لا (وهو ما يَفعَلُه <c>NavLink</c>)؛ وفي وَضعِ المَسارِ كَما كان.</summary>
    [Theory]
    [InlineData("http://ashare.example.test/ashare/explore", "/explore", "http://ashare.example.test/explore", true)]
    [InlineData("http://ashare.example.test/ashare/explore", "/explore", null, false)]
    [InlineData("http://ashare.example.test/ashare/explore", "/chats", "http://ashare.example.test/explore", false)]
    [InlineData("http://localhost/ashare/explore", "/ashare/explore", null, true)]
    [InlineData("http://localhost/ashare/explore", "/ashare/chats", null, false)]
    public async Task The_active_tab_is_matched_against_the_visible_url(string navUri, string href, string? matchUri, bool active)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager>(new FixedNav(navUri));
        await using var sp = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<AcTopNav>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(AcTopNav.Items)] = new List<AcTopNav.TopNavItem> { new(href, "تَبويب") },
            [nameof(AcTopNav.MatchUri)] = matchUri,
        }))).ToHtmlString());
        var isActive = html.Contains("ac-topnav-link-active", StringComparison.Ordinal);
        output.WriteLine($"{navUri} · {href} · {matchUri ?? "—"} ⇒ {(isActive ? "نَشِط" : "غَيرُ نَشِط")}");
        Assert.Equal(active, isActive);
    }

    private sealed class FixedNav : NavigationManager
    {
        public FixedNav(string uri) => Initialize(new Uri(new Uri(uri), "/").ToString(), uri);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlatformV1.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("جَذرُ المُستَودَعِ غَيرُ مَوجود — الأَداةُ عَمياء.");
    }

    /// <summary><b>الحارِس</b> (القاعِدَة ٢): لا رابِطَ ولا تَحويلَ إلى صَفحَةِ مَتجَرٍ يُكتَبُ بِيَدٍ <c>$"/{slug}…"</c> — يَمُرُّ بِـ<c>LinkFor</c> أَو <c>StoreUrl</c>
    /// العارِفَينِ بِالمُضيف. والاستِماراتُ (<c>action</c>) والرَوابِطُ المُخزَّنَةُ خارِجَه عَمداً: تَعمَلُ عَلى كُلِّ مُضيفٍ لِأَنّ مَسارَها يَحمِلُ المَتجَر (G15).
    /// يَطبَعُ ما فَحَصَه، ويَحمَرُّ إن كانَ صِفراً.</summary>
    [Fact]
    public void No_store_link_or_redirect_is_written_by_hand()
    {
        var root = Path.Combine(RepoRoot(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var razor = Directory.GetFiles(root, "*.razor", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        var code = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        var href = new Regex(@"\b(?:href|Href|ActionHref)=""@\(\$""/\{(?:Slug|slug|tenant\.Slug|t\.Slug|Tenant\.Slug)\}");
        var redirect = new Regex(@"(?:Results\.Redirect\(|return\s)\$""/\{(?:slug|Slug)\}");
        var hits = razor.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)).Where(x => href.IsMatch(x.l)))
            .Concat(code.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)).Where(x => redirect.IsMatch(x.l) && !x.l.TrimStart().StartsWith("//"))))
            .Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.i + 1}").ToList();
        output.WriteLine($"مِلَفّاتُ Razor: {razor.Count} · مِلَفّاتُ C#: {code.Count} · رَوابِطُ وتَحويلاتٌ بِيَد: {hits.Count}");
        foreach (var h in hits) output.WriteLine("  " + h);
        Assert.True(razor.Count > 50 && code.Count > 50, "أَداةٌ عَمياء");
        Assert.Empty(hits);
    }

    /// <summary><b>G15 — الرَوابِطُ المُخزَّنَةُ تَبقى بِشَكلِ المَسار</b>: كُلُّ كاتِبٍ لِـ<c>RelatedUrl</c> (وما يُقارَنُ بِه) لا يَمُرُّ بِبانٍ عارِفٍ بِالمُضيف
    /// (<c>LinkFor</c>، <c>StoreUrl</c>، <c>Link(req</c>)، والحَرفِيُّ مِنها <c>/{slug}/…</c> أَو مَسارُ مَنَصَّة — فَمَنعُ تَكرارِ المُحادَثَةِ بِالمُقارَنَةِ النَصِّيَّةِ
    /// (كاتِبُها وقارِئُها بِالحَرفِ نَفسِه) لا يَنكَسِرُ صامِتاً حينَ تَصيرُ الرَوابِطُ المَرسومَةُ نَظيفَة.</summary>
    [Fact]
    public void Stored_related_urls_stay_path_canonical()
    {
        var root = Path.Combine(RepoRoot(), "libs");
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToList();
        var writer = new Regex(@"\bRelatedUrl\s*(?:==?)\s*(?<rhs>[^,;\r\n]+)");
        var found = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l))).Select(x => (x.f, x.i, m: writer.Match(x.l))).Where(x => x.m.Success)
            .Select(x => (Where: $"{Path.GetRelativePath(root, x.f)}:{x.i + 1}", Rhs: x.m.Groups["rhs"].Value.Trim())).ToList();
        var hostAware = found.Where(x => x.Rhs.Contains("LinkFor(") || x.Rhs.Contains("StoreUrl.") || x.Rhs.Contains("Link(req")).ToList();
        var literal = found.Where(x => x.Rhs.StartsWith("$\"", StringComparison.Ordinal) || x.Rhs.StartsWith("\"", StringComparison.Ordinal)).ToList();
        var offShape = literal.Where(x => !(x.Rhs.StartsWith("$\"/{slug}/", StringComparison.Ordinal) || x.Rhs.StartsWith("$\"/studio/", StringComparison.Ordinal)
                                            || x.Rhs.StartsWith("\"/admin/", StringComparison.Ordinal))).ToList();
        // الشَكلُ بِلا أَسماءِ المُتَغَيِّرات (`{conv.Id}` و`{conversationId}` مُعَرِّفُ المُحادَثَةِ نَفسُه): الكاتِبانِ والمُقارِنُ بِشَكلٍ واحِد.
        var chats = found.Where(x => x.Rhs.Contains("/chats/", StringComparison.Ordinal))
            .Select(x => Regex.Replace(x.Rhs.TrimEnd(' ', '&'), @"\{[^}]*\}", "{}")).Distinct().ToList();
        output.WriteLine($"كُتّابُ RelatedUrl ومُقارِنوه: {found.Count} · حَرفِيّ: {literal.Count} · بِبانٍ عارِفٍ بِالمُضيف: {hostAware.Count} · بِغَيرِ الشَكل: {offShape.Count} · " +
                         $"أَشكالُ رابِطِ المُحادَثَة: {chats.Count}");
        foreach (var x in hostAware.Concat(offShape)) output.WriteLine($"  {x.Where} ⇐ {x.Rhs}");
        Assert.True(found.Count >= 20 && literal.Count >= 15, "أَداةٌ عَمياء");
        Assert.Empty(hostAware);
        Assert.Empty(offShape);
        Assert.Single(chats);
    }
}

/// <summary>الحاقِنُ الحَقيقيُّ عَلى مُضيفٍ مُصَغَّرٍ حَيّ، وبَعدَه طَرَفٌ يَردُّ ما تَرَكَه في <c>Items</c>: النِطاقُ ومَتجَرُ المُضيف.</summary>
file sealed class ItemsProbe : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Client { get; }
    private ItemsProbe(WebApplication app, HttpClient client) { _app = app; Client = client; }

    public static async Task<ItemsProbe> StartAsync(string? baseDomain)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            int port;
            using (var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Items[StoreHostItems.Apex]}|{ctx.Items[StoreHostItems.Slug]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new ItemsProbe(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
