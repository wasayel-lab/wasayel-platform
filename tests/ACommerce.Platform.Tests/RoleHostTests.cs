using System.Net;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُضيفاتُ الأَدوار — {slug}-{rolehost}.R اسمٌ آخَرُ لِـ/{slug}/r/{role} في صَفَحاتِ GET وَحدَها (‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4) ═══
//
// **المَقيسُ قَبل** (عِندَ 9635c83): `ashare-vendor.R` يُجابُ 404 (المَرحَلَةُ الأُولى)؛ ودَورُ الرابِطِ الإداريُّ يُقَدَّمُ بِلا فَحصِ مِلك؛ وحِفظُ الأَدوارِ لا يَسأَلُ
// عَن مُضيفِها. **والآن**: `RoleFromHost` و`AnswerStoreHost` بِقَوالِبِ صَفَحاتِ الدَورِ (G6 بِصُفوفِ الدَور)، وG5 (لِكُلِّ صَفحَةِ دَورٍ تَوأَمُها)، والحالُّ 404 لِدَورٍ
// لا يَملِكُه المَتجَر، و`StoreUrl.Link` لِلرَوابِطِ العابِرَة، وG7 (`EffectiveRole` — والحَيُّ في `LiveRoleHostClaimProofTests`)، و`RoleHostCollision`.
// وتَوصيفُ ما يَنجو `RoleHostCharacterizationTests` بِبَصمَتِه.

public class RoleHostTests(ITestOutputHelper output)
{
    private const string Base = "example.test";
    private static readonly RolePageTemplates Pages = RoleHostPages.Templates;

    [Theory]
    [InlineData("ashare-vendor.example.test", "ashare", "vendor")]
    [InlineData("ashare-admin.example.test", "ashare", "tenant_admin")]
    [InlineData("best-vendor-shipper.example.test", null, null)]       // البادِئَةُ best-vendor لا تَصلُحُ مَتجَراً
    [InlineData("theme-demo-customer.example.test:8443", "theme-demo", "customer")]
    [InlineData("ashare.example.test", null, null)]
    [InlineData("ashare-sales.example.test", null, null)]
    [InlineData("shop--vendor.example.test", null, null)]
    [InlineData("www-vendor.example.test", null, null)]
    [InlineData("vendor.ashare.example.test", null, null)]
    [InlineData("ashare-vendor.evil.test", null, null)]
    public void A_role_host_parses_to_its_store_and_catalog_role(string host, string? slug, string? role)
    {
        var got = TenantResolverMiddleware.RoleFromHost(host, Base);
        output.WriteLine($"{host} ⇒ {got?.Slug ?? "—"} · {got?.CatalogRole ?? "—"}");
        Assert.Equal((slug, role), (got?.Slug, got?.CatalogRole));
    }

    /// <summary><b>G6 بِصُفوفِ الدَور</b> — الدَورُ في مَسارِ GET/HEAD قَوالِبِ صَفَحاتِ الدَورِ وَحدَها، وما سِواه كَمُضيفِ المَتجَر.</summary>
    [Theory]
    [InlineData("ashare-vendor.example.test", "GET", "/", "Rewrite|ashare|/ashare/r/vendor|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/me/listings", "Rewrite|ashare|/ashare/r/vendor/me/listings|vendor")]
    [InlineData("ashare-vendor.example.test", "HEAD", "/explore", "Rewrite|ashare|/ashare/r/vendor/explore|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/listings/3f2c4a1e-0000-4000-8000-000000000001", "Rewrite|ashare|/ashare/r/vendor/listings/3f2c4a1e-0000-4000-8000-000000000001|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/listings/not-a-guid", "Rewrite|ashare|/ashare/listings/not-a-guid|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/ashare/me", "Redirect|ashare|/me|vendor")]                  // W13-4·C2f·6: شَكلُ المَسارِ عَلى مُضيفِه ← 302 إلى النَظيف
    [InlineData("ashare-vendor.example.test", "GET", "/ashare/r/vendor/me", "Redirect|ashare|/me|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/r/customer/explore", "Rewrite|ashare|/ashare/r/customer/explore|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/me/data/download", "Rewrite|ashare|/ashare/me/data/download|vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "/listings/create", "Rewrite|ashare|/ashare/listings/create|vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "/auth/phone/login", "Rewrite|ashare|/ashare/auth/phone/login|vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "/me", "Rewrite|ashare|/ashare/me|vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/api/ashare/r/vendor/manifest.json", "Pass|ashare||vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/api/ejar/unread-counts", "NotFound|||")]
    [InlineData("ashare-vendor.example.test", "GET", "/sw.js", "Pass|ashare||vendor")]
    [InlineData("ashare-vendor.example.test", "GET", "/studio", "Redirect|ashare|https://example.test/studio|vendor")]
    [InlineData("ashare-vendor.example.test", "POST", "/studio/apps/create", "NotFound|||")]
    [InlineData("ashare-vendor.example.test", "GET", "/terms", "Redirect|ashare|https://example.test/terms|vendor")]
    [InlineData("ashare-admin.example.test", "GET", "/manage", "Rewrite|ashare|/ashare/r/tenant_admin/manage|tenant_admin")]
    [InlineData("ashare-admin.example.test", "POST", "/listings/create", "Rewrite|ashare|/ashare/listings/create|tenant_admin")]
    [InlineData("ashare-admin.example.test", "GET", "/admin/tenants/ashare/users", "Pass|ashare||tenant_admin")]
    [InlineData("ashare.example.test", "GET", "/me/listings", "Rewrite|ashare|/ashare/me/listings|")]
    [InlineData("shop--vendor.example.test", "GET", "/", "NotFound|||")]
    public void The_role_host_answer_matrix(string host, string method, string path, string expected)
    {
        var a = TenantResolverMiddleware.AnswerStoreHost(host, Base, method, path, "", "https", Pages);
        var shown = $"{a.Verdict}|{a.Slug}|{a.Path ?? a.Location}|{a.Role}";
        output.WriteLine($"{method} {host}{path} ⇒ {shown}");
        Assert.Equal(expected, shown);
    }

    /// <summary><b>G7 (النِصفُ النَقِيّ)</b>: لِكُلِّ قالِبِ صَفحَةِ دَورٍ ولِكُلِّ طَريقَةٍ غَيرِ GET/HEAD — لا يَحمِلُ المَسارُ بَعدَ الحَقنِ دَوراً عَلى مُضيفِ الدَور.
    /// والقَوالِبُ تُعَدُّ وتُطبَع.</summary>
    [Fact]
    public void A_write_on_a_role_host_never_carries_the_role()
    {
        var samples = Pages.Templates.Select(t => t[RolePageTemplates.Prefix.Length..].Replace("{id:guid}", Guid.Empty.ToString())
            .Replace("{conversationId:guid}", Guid.Empty.ToString()).Replace("{vendorId:guid}", Guid.Empty.ToString())
            .Replace("{section}", "terms").Replace("{planId}", "p1")).Select(r => r.Length == 0 ? "/" : r).ToList();
        var rows = 0;
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        foreach (var path in samples)
        {
            var a = TenantResolverMiddleware.AnswerStoreHost("ashare-admin.example.test", Base, method, path, "", "https", Pages);
            rows++;
            Assert.True(a.Path is null || ACommerce.Templates.Customer.Marketplace.AuthSession.ExtractRoleFromPath(a.Path) is null, $"{method} {path} ⇒ {a.Path}");
        }
        var gets = samples.Count(p => TenantResolverMiddleware.AnswerStoreHost("ashare-admin.example.test", Base, "GET", p, "", "https", Pages).Path?.Contains("/r/tenant_admin") == true);
        // ما أَوَّلُ مَقطَعِه مَنطِقَةُ مَنَصَّةٍ (`/terms` — شُروطُ المَنَصَّة، «ب» W13-4·C2a·5) يُحَوَّلُ إلى الجَذرِ كَعَلى مُضيفِ المَتجَر، فَلا دَورَ يُحقَن.
        var platform = samples.Where(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } seg && ReservedTenantSlugs.PlatformAreas.Contains(seg[0])).ToList();
        output.WriteLine($"قَوالِبُ صَفَحاتِ الدَور: {Pages.Templates.Count} · صُفوفُ الكِتابَة: {rows} · GET بِالدَور: {gets} · مَنطِقَةُ مَنَصَّة: {string.Join(", ", platform)}");
        Assert.True(Pages.Templates.Count >= 30, "أَداةٌ عَمياء");
        Assert.Equal(Pages.Templates.Count - platform.Count, gets);
        Assert.Equal(["/terms"], platform);
    }

    /// <summary><b>G5 — لِكُلِّ صَفحَةِ دَورٍ تَوأَمُها بِلا دَور</b>، وكُلُّ مَقطَعٍ في القَوالِبِ شَكلٌ تَعرِفُه المُطابَقَة. يَطبَعُ العَدَدَين.</summary>
    [Fact]
    public void Every_role_page_has_its_roleless_twin()
    {
        var all = RoleHostPages.AllRoutes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = Pages.Templates;
        var twins = roles.Select(t => "/{slug}" + t[RolePageTemplates.Prefix.Length..]).ToList();
        var missing = twins.Where(t => !all.Contains(t)).ToList();
        var odd = roles.SelectMany(t => t.Split('/', StringSplitOptions.RemoveEmptyEntries)).Where(s => !RolePageTemplates.IsSupportedSegment(s)).Distinct().ToList();
        output.WriteLine($"صَفَحاتُ الدَور: {roles.Count} · تَوائِمُها: {twins.Count - missing.Count} · ناقِص: {missing.Count} · مَقاطِعُ غَيرُ مَعروفَة: {odd.Count}");
        Assert.True(roles.Count >= 30, "أَداةٌ عَمياء");
        Assert.Empty(missing);
        Assert.Empty(odd);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/me/role/onboarding", true)]
    [InlineData("/ME/Listings", true)]
    [InlineData("/me/data/download", false)]
    [InlineData("/listings/x", false)]
    [InlineData("/nope", false)]
    public void The_matcher_knows_literals_parameters_and_guids(string rest, bool matches) => Assert.Equal(matches, Pages.Matches(rest));

    // ─── الرَوابِط ──────────────────────────────────────────────────────────────

    private static HostView RoleHost(string role) => new("example.test", "ashare", "https", "") { HostRole = role };
    private static readonly HostView StoreHost = new("example.test", "ashare", "https", "");

    /// <summary><b>الرَوابِطُ العابِرَةُ تُسَمّي مُضيفَ تَطبيقِها، وما في التَطبيقِ نَفسِه نَظيف</b> (<c>StoreUrl.Link</c>) — وفي وَضعِ المَسارِ بِحَرفِه.</summary>
    [Theory]
    [InlineData("store", "ashare", null, "explore", "/explore")]
    [InlineData("store", "ashare", "vendor", "me/listings", "https://ashare-vendor.example.test/me/listings")]
    [InlineData("store", "ashare", null, "r/vendor/login", "https://ashare-vendor.example.test/login")]
    [InlineData("store", "ashare", "tenant_admin", "manage", "https://ashare-admin.example.test/manage")]
    [InlineData("store", "ashare", "mandoob", "me", "/r/mandoob/me")]
    [InlineData("vendor", "ashare", "vendor", "me/listings", "/me/listings")]
    [InlineData("vendor", "ashare", "vendor", "", "/")]
    // ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2) كانَ «https://ashare.example.test/explore» — رابِطٌ بِلا دَورٍ يُغادِرُ مُضيفَ الدَورِ إلى مُضيفٍ لا جَلسَةَ فيه.
    [InlineData("vendor", "ashare", null, "explore", "/explore")]
    [InlineData("vendor", "ashare", "customer", "", "https://ashare-customer.example.test/")]
    [InlineData("vendor", "ashare", "mandoob", "me", "https://ashare.example.test/r/mandoob/me")]
    [InlineData("vendor", "ejar", "vendor", "me", "/ejar/r/vendor/me")]
    [InlineData("apex", "ashare", "vendor", "me", "/ashare/r/vendor/me")]
    [InlineData("none", "ashare", null, "r/vendor/login", "/ashare/r/vendor/login")]
    public void A_link_names_its_apps_host(string where, string slug, string? role, string path, string expected)
    {
        var view = where switch
        {
            "store" => StoreHost,
            "vendor" => RoleHost("vendor"),
            "apex" => new HostView("example.test", null, "https", ""),
            _ => default,
        };
        var got = StoreUrl.Link(view, slug, role, path);
        output.WriteLine($"{where}: ({slug}, {role ?? "—"}, «{path}») ⇒ {got}");
        Assert.Equal(expected, got);
    }

    /// <summary>ما لا يُغادِرُ المُضيف (تَحويلُ ما بَعدَ الدُخولِ والاستِمارات — <c>StoreUrl.For</c>) عَلى مُضيفِ الدَور، والبَيانُ لِكُلِّ مُضيفِ دَور.</summary>
    [Theory]
    [InlineData("ashare", "vendor", "me", "/me")]
    [InlineData("ashare", null, "me", "/me")]
    [InlineData("ashare", "customer", "me", "/r/customer/me")]
    [InlineData("ejar", "vendor", "me", "/ejar/r/vendor/me")]
    public void A_redirect_stays_on_the_role_host(string slug, string? role, string path, string expected)
    {
        Assert.Equal(expected, StoreUrl.For(RoleHost("vendor"), slug, role, path));
        Assert.Equal("", PwaManifestDecision.AppBase(RoleHost("vendor"), "ashare", "vendor"));
    }

    [Theory]
    [InlineData("https://ashare-vendor.example.test/me/listings", "ashare-vendor.example.test", "vendor")]
    [InlineData("https://ashare-vendor.example.test/r/customer/login", "ashare-vendor.example.test", "customer")]
    [InlineData("https://ashare-customer.example.test/me", "ashare-vendor.example.test", null)]
    [InlineData("", "ashare-vendor.example.test", null)]
    public void The_referer_on_a_role_host_is_the_hosts_role(string referer, string requestHost, string? role)
        => Assert.Equal(role, StoreHostPage.RoleFromReferer(referer, requestHost, "ashare", "vendor"));

    // ─── G7: دَعوى لا مِنحَة ─────────────────────────────────────────────────────

    /// <summary>دَورُ الرابِطِ الإداريُّ لا يُقَدَّمُ إلّا لِمَن دَورُه المُخَزَّنُ هُوَ — والسالِبُ والمُوجَبُ لِكُلِّ بابٍ يَسأَلُه.</summary>
    [Theory]
    [InlineData("tenant_admin", "customer", "customer")]
    [InlineData("tenant_admin", "", null)]
    [InlineData("tenant_admin", null, null)]
    [InlineData("tenant_admin", "tenant_admin", "tenant_admin")]
    [InlineData("vendor", "customer", "vendor")]
    public void An_admin_claim_in_the_url_is_not_a_grant(string urlRole, string? stored, string? expected)
    {
        Assert.Equal(expected, EffectiveRole.Resolve(null, false, urlRole, stored));
        var roles = new[] { "customer", "vendor", "tenant_admin" }.Select((s, i) => RoleCatalog.InstantiateRole(RoleCatalog.Find(s)!, i)).ToList();
        Assert.Equal(stored == "tenant_admin", PermissionDoor.Opens(roles, urlRole, stored, "tenant.manage"));
    }

    /// <summary>صَفحَتا الإدارَةِ في المَتجَرِ تَسأَلانِ الدَورَ الفَعّالَ مِن <c>EffectiveRole.Resolve</c> لا <c>Role ?? stored</c> — وكانَتا الثَغرَة.</summary>
    [Fact]
    public void The_manage_pages_ask_the_effective_role()
    {
        var root = Path.Combine(RepoRootOf(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");
        foreach (var page in new[] { "TenantManage.razor", "Me.razor" })
        {
            var code = File.ReadAllText(Path.Combine(root, page));
            Assert.Contains("Gates.EffectiveRole.Resolve(null, false, Role,", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Role ?? storedRole, \"tenant.manage\"", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Role ?? me?.ActiveRole, \"tenant.manage\"", code, StringComparison.Ordinal);
        }
    }

    private static string RepoRootOf()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlatformV1.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    // ─── الحالُّ: دَورٌ لا يَملِكُه المَتجَرُ 404 ──────────────────────────────────────

    [Fact]
    public async Task A_role_host_for_a_role_the_store_lacks_answers_404_on_the_wire()
    {
        var world = new DocWorld()
            .Put(new Tenant { Id = "ashare", Name = "عَشير" })
            .Put(new Tenant { Id = "rolly", Name = "أَدوار", Roles = [RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!, 0)] });
        await using var p = await RoleHostPipeline.StartAsync("example.test", world.Store);
        var rows = new (string Method, string Host, string Path, int Status, string Body)[]
        {
            ("GET", "rolly-vendor.example.test", "/me/listings", 200, "/rolly/r/vendor/me/listings|/me/listings|rolly"),
            ("POST", "rolly-vendor.example.test", "/listings/create", 200, "/rolly/listings/create|/listings/create|rolly"),
            ("GET", "rolly-customer.example.test", "/", 404, ""),
            ("GET", "ashare-vendor.example.test", "/", 404, ""),
            ("GET", "nosuch-vendor.example.test", "/", 404, ""),
            ("GET", "rolly.example.test", "/me/listings", 200, "/rolly/me/listings|/me/listings|rolly"),
        };
        foreach (var r in rows)
        {
            var (status, body) = await p.SendAsync(r.Method, r.Host, r.Path);
            output.WriteLine($"{r.Method} {r.Host}{r.Path} ⇒ {status} {body}");
            Assert.Equal((r.Status, r.Body), (status, body));
        }
        output.WriteLine($"الصُفوف: {rows.Length}");
    }

    // ─── التَصادُم ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_role_whose_host_is_an_existing_store_is_refused_and_writes_nothing()
    {
        var world = new DocWorld()
            .Put(new Tenant { Id = "best", Name = "best", Roles = [RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0)] })
            .Put(new Tenant { Id = "best-vendor", Name = "مَتجَرٌ قَديم" });
        var refused = await RolesSaveService.SaveAsync(world.Session, "best", new RolesSaveRequest(["customer", "vendor"], "customer"));
        var allowed = await RolesSaveService.SaveAsync(world.Session, "best", new RolesSaveRequest(["customer", "driver"], "customer"));
        output.WriteLine($"best + vendor ⇒ {refused} · best + driver ⇒ {allowed}");
        Assert.Equal(TenantConfigResult.Reject(TenantConfigCodes.RoleHostTaken), refused);
        Assert.Equal(TenantConfigStatus.Saved, allowed.Status);
        Assert.Equal(["best-vendor", "best-admin"], RoleHostCollision.Hosts("best", ["vendor", "tenant_admin", "mandoob"]));
        Assert.Equal("best-vendor", await RoleHostCollision.FirstTakenForNewStoreAsync(world.Session, "best"));
        Assert.Null(await RoleHostCollision.FirstTakenForNewStoreAsync(world.Session, "fresh"));
        // والاتِّجاهُ الآخَر: مُعَرِّفٌ هُوَ مُضيفُ دَورِ مَتجَرٍ آخَرَ لا يولَدُ في الأَبوابِ الثَلاثَة.
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.Incubator.TenantFromAnalysisFactory.SlugReserved,
            ACommerce.Templates.Customer.Marketplace.Services.Incubator.TenantFromAnalysisFactory.SlugRefusal("ashare-vendor"));
    }

    /// <summary>الأَبوابُ الثَلاثَةُ والحِفظانِ يَسأَلونَ <c>RoleHostCollision</c> — ماسِحٌ يَطبَعُ ما وَجَد.</summary>
    [Fact]
    public void Every_door_that_adds_a_role_or_a_store_asks_the_collision_gate()
    {
        var root = Path.Combine(RepoRootOf(), "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        string Read(params string[] p) => File.ReadAllText(Path.Combine([root, .. p]));
        var sites = new[]
        {
            ("factory", Read("Services", "Incubator", "TenantFromAnalysisFactory.cs").Contains("RoleHostCollision.FirstTakenForNewStoreAsync(")),
            ("admin create", Read("MarketplaceTemplateExtensions.cs").Contains("RoleHostCollision.FirstTakenForNewStoreAsync(s, slug)")),
            ("agent create", Read("Services", "AgentService.cs").Contains("RoleHostCollision.FirstTakenForNewStoreAsync(s, slug, ct)")),
            ("roles save", Read("Services", "TenantConfig", "RolesSaveService.cs").Contains("RoleHostCollision.FirstTakenAsync(session, slug, added, ct)")),
            ("agent set_roles", Read("Services", "AgentService.cs").Contains("RoleHostCollision.FirstTakenAsync(s, slug,")),
        };
        output.WriteLine($"الأَبوابُ الَّتي تَسأَلُ بابَ التَصادُم: {sites.Count(s => s.Item2)} مِن {sites.Length}");
        Assert.All(sites, s => Assert.True(s.Item2, s.Item1));
    }
}

/// <summary>الحاقِنُ والحالُّ الحَقيقِيّانِ بِقَوالِبِ القالِبِ وبِمَخزَنٍ في الذاكِرَة.</summary>
file sealed class RoleHostPipeline : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private RoleHostPipeline(WebApplication app, HttpClient client) { _app = app; _client = client; }

    public static async Task<RoleHostPipeline> StartAsync(string baseDomain, Marten.IDocumentStore store)
    {
        for (var attempt = 1; ; attempt++)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton(new SubdomainTenancyOptions { BaseDomain = baseDomain });
            builder.Services.AddSingleton(RoleHostPages.Templates);
            builder.Services.AddSingleton(store);
            builder.Services.AddMemoryCache();
            int port;
            using (var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { l.Start(); port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            var app = builder.Build();
            app.Urls.Add($"http://127.0.0.1:{port}");
            app.UseSubdomainTenantPath();
            app.UsePlatformMultiTenancy();
            app.Run(ctx => ctx.Response.WriteAsync($"{ctx.Request.Path}|{ctx.Items[SubdomainTenantPathMiddleware.OriginalPathItem]}|{ctx.Items[ACommerce.Platform.Shared.TenantKeys.Slug]}"));
            try { await app.StartAsync(); }
            catch (IOException) when (attempt < 5) { await app.DisposeAsync(); continue; }
            return new RoleHostPipeline(app, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(20),
            });
        }
    }

    public async Task<(int Status, string Body)> SendAsync(string method, string host, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        req.Headers.Host = host;
        if (method == "POST") req.Content = new FormUrlEncodedContent([]);
        using var res = await _client.SendAsync(req);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
