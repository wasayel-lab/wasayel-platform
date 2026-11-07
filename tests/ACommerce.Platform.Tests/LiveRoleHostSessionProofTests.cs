using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Components.Layout;
using ACommerce.Templates.Customer.Marketplace.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveRoleOnboardingSaveCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَطبيقُ الدَورِ لا يُغادِرُ مُضيفَه — الصَفحَةُ المَرسومَةُ عَلى {slug}-{role}.R وتَحويلُ «حِفظِ بَياناتِ الدَور» (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2) ═══
//
// **المَقيسُ قَبل** (حَيّاً عِندَ a5958fa، `BaseDomain=localhost`): بائِعٌ دَخَلَ عَلى `ejar-vendor.localhost` (كَعكَتاه بِلا `Domain=`) يَرى في كُلِّ صَفحَةِ دَورٍ الجَرَسَ
// `http://ejar.localhost/notifications`، و«تَنزيلَ بَياناتي» `http://ejar.localhost/me/data/download` (يُحَوَّلُ إلى الدُخولِ هُناك، والنِسبِيُّ عَلى مُضيفِ الدَورِ
// 200)، و11 رابِطاً في `/explore` إلى مُضيفِ المَتجَر؛ وبائِعٌ دَخَلَ بِـ`as=vendor` عَلى مُضيفِ المَتجَرِ يُرسِلُ «حِفظَ بَياناتِ الدَورِ» فَيُحَوَّلُ إلى
// `ejar-vendor.localhost/me/listings` فَيُرسَمُ بِلا جَلسَة. **والآن**: الرابِطُ بِلا دَورٍ `StoreUrl.For` عَلى المُضيفِ نَفسِه، والتَحويلُ بَعدَ الإعداد `Home`.
// **والحارِسُ هُنا بِالرَسمِ لا بِالدالَّة**: كُلُّ صَفحَةٍ تُرسَمُ داخِلَ `MainLayout` بِخِدماتِ المُضيفِ المُرَكَّبِ كَما يَحقِنُها الحاقِن، وتُعَدُّ رَوابِطُها، وما خَرَجَ
// مِنها عَن مُضيفِ الدَورِ يُطبَعُ ويَحمَرّ — والعَدُّ صِفراً أَداةٌ عَمياء. يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres.

[Collection(LiveProofCollection.Name)]
public class LiveRoleHostSessionProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);
    private const string Base = "example.test";
    private const string Door = "/{slug}/me/role/onboarding/save";

    private static readonly Regex Href = new(@"\bhref=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>الصَفحَةُ داخِلَ <c>MainLayout</c> عَلى <paramref name="hostName"/> كَما يَترُكُها الحاقِن، بِكَعكَتَي الدَورِ والعامَّةِ لِـ<paramref name="who"/>.</summary>
    internal static async Task<(string Path, string Html)> RenderOnHostAsync(LiveTemplateDoorHost host, string hostName, string slug, string visiblePath,
        Type page, User who, string? cookieRole)
    {
        var answer = TenantResolverMiddleware.AnswerStoreHost(hostName, Base, "GET", visiblePath, "", "https",
            host.Services.GetRequiredService<RolePageTemplates>());
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Method = "GET";
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(hostName);
        ctx.Request.Path = answer.Path ?? visiblePath;
        ctx.Items[StoreHostItems.Apex] = Base;
        ctx.Items[StoreHostItems.Slug] = answer.Slug;
        if (answer.Role is { } r) ctx.Items[StoreHostItems.Role] = r;
        ctx.Items[StoreHostItems.OriginalPath] = visiblePath;
        ctx.SetTenant(slug, "حِفظُ بَياناتِ الدَور", "#0f766e", "phone", hasRoles: true);
        var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(who.Id, slug, who.SessionVersion);
        // ‏(‏2026-11-07، W13-4·C2c·2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) الاسمُ بِقَرارِ الكاتِبِ والقارِئِ نَفسِه لِهذا الطَلَب (`CookieNameOn`) — ما يَحمِلُه مُتَصَفِّحٌ عَلى HTTPS.
        ctx.Request.Headers.Cookie = cookieRole is null
            ? $"{AuthSession.CookieNameOn(ctx.Request, slug)}={token}"
            : $"{AuthSession.CookieNameOn(ctx.Request, slug, cookieRole)}={token}; {AuthSession.CookieNameOn(ctx.Request, slug)}={token}";
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = ctx;
        // ‏`AuthSession.LinkFor` يَقرَأُ الطَلَبَ مِن الوَصّالِ الساكِنِ الَّذي يَضَعُه `Program.cs` — وبِدونِه يُجيبُ بِشَكلِ المَسارِ فَلا يَرى الرَسمُ المُضيفَ أَصلاً
        // (مَقيس: كُلُّ رابِطٍ `/{slug}/r/vendor/…` والفاحِصُ أَخضَرُ أَعمى). والقيمَةُ نَفسُها `AsyncLocal` لِكُلِّ نُسخَة، فَلا تَبلُغُ اختِباراً آخَر.
        var previous = AuthSession.HttpAccessor;
        AuthSession.HttpAccessor = accessor;
        try { return (ctx.Request.Path.Value!, await RenderAsync(scope.ServiceProvider, ctx, hostName, slug, page)); }
        finally { AuthSession.HttpAccessor = previous; }
    }

    private static async Task<string> RenderAsync(IServiceProvider sp, HttpContext ctx, string hostName, string slug, Type page)
    {
        if (sp.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize($"https://{hostName}/", $"https://{hostName}{ctx.Request.Path}");
        var auth = sp.GetRequiredService<AuthSession>();
        auth.SetTenant(slug);
        auth.Load(ctx, slug);
        var role = AuthSession.ExtractRoleFromPath(ctx.Request.Path);
        RenderFragment body = b =>
        {
            b.OpenComponent(0, page);
            b.AddAttribute(1, "Slug", slug);
            b.AddAttribute(2, "Role", role);
            b.CloseComponent();
        };
        await using var renderer = new HtmlRenderer(sp, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MainLayout>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(LayoutComponentBase.Body)] = body,
            }))).ToHtmlString()));
    }

    /// <summary>رَوابِطُ الصَفحَةِ الخارِجَةُ عَن <paramref name="hostName"/> — مُطلَقَةٌ (أَو بِلا مُخَطَّط) ومُضيفُها غَيرُه. والعَدَدُ الكُلِّيُّ لِيُطبَع.</summary>
    internal static (int All, List<string> OffHost) Leaving(string html, string hostName)
    {
        var hrefs = Href.Matches(html).Select(m => m.Groups[1].Value).ToList();
        var off = hrefs.Where(h =>
        {
            if (!(h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || h.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || h.StartsWith("//", StringComparison.Ordinal)))
                return false;
            return !Uri.TryCreate(h.StartsWith("//", StringComparison.Ordinal) ? "https:" + h : h, UriKind.Absolute, out var u)
                   || !string.Equals(u.Authority, hostName, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        return (hrefs.Count, off);
    }

    [Fact]
    public async Task A_vendors_role_app_pages_link_only_to_their_own_host_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13rhs-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        var roleHost = $"{slug}-vendor.{Base}";
        var pages = new (string Path, Type Page)[]
        {
            ("/", typeof(TenantHome)), ("/explore", typeof(TenantExplore)), ("/me", typeof(Me)), ("/me/data", typeof(MyData)),
            ("/me/listings", typeof(MyListings)), ("/notifications", typeof(Notifications)),
        };
        try
        {
            var (all, pathForm, leaving) = (0, 0, new List<string>());
            foreach (var (visible, page) in pages)
            {
                var (path, html) = await RenderOnHostAsync(host, roleHost, slug, visible, page, vendor, "vendor");
                var (n, off) = Leaving(html, roleHost);
                all += n;
                leaving.AddRange(off.Select(o => $"{visible} → {o}"));
                var download = html.Contains("href=\"/me/data/download\"", StringComparison.Ordinal);
                // والأَداةُ تُقاسُ قَبلَ أَن يوثَقَ بِها: رابِطُ صَفحَةِ الدَورِ بِشَكلِ المَسار (`/{slug}/r/vendor/…`) يَعني أَنّ الرَسمَ لَم يَرَ المُضيف — فَيُعَدُّ ويَحمَرّ.
                pathForm += Href.Matches(html).Count(m => m.Groups[1].Value.StartsWith($"/{slug}/r/", StringComparison.Ordinal));
                output.WriteLine($"[live] {roleHost}{visible} ⇒ {path} · رَوابِط: {n} · خارِجَ المُضيف: {off.Count}{(visible == "/me/data" ? $" · التَنزيلُ نِسبِيّ: {download}" : "")}");
                Assert.StartsWith($"/{slug}/r/vendor", path, StringComparison.Ordinal);
                if (visible == "/me/data") Assert.True(download, "رابِطُ «تَنزيلِ بَياناتي» لَيسَ /me/data/download عَلى مُضيفِ الدَور");
            }
            foreach (var l in leaving) output.WriteLine("  ✗ " + l);
            output.WriteLine($"[live] الصَفَحات: {pages.Length} · الرَوابِطُ المَفحوصَة: {all} · خارِجَ مُضيفِ الدَور: {leaving.Count} · بِشَكلِ المَسارِ لِلدَور: {pathForm}");
            Assert.True(all >= pages.Length * 5, $"فَحصٌ أَعمى: {all} رابِطاً");
            Assert.Equal(0, pathForm);
            Assert.Empty(leaving);
        }
        finally { await DropAsync(host, slug, vendor); }
    }

    /// <summary>‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-02) <b>وصَفَحاتُ الدَورِ عَلى مُضيفِ المَتجَر</b> (<c>/r/vendor/…</c> — حَيثُ يَنزِلُ دُخولُ الدَورِ
    /// بِتَحويلِ <c>For</c>، وكَعكَتُه عَلى مُضيفِ المَتجَر): كانَ شَريطُ التَنَقُّلِ كُلُّه (<c>LinkFor(slug, Role, …)</c>) يُسَمّي مُضيفَ البائِعِ مُطلَقاً — مَقيسٌ حَيّاً
    /// 19 هَدَفاً خارِجَ المُضيف. والآن دَورُ الصَفحَةِ كَدَورِ المُضيف: عَلى المُضيفِ نَفسِه.</summary>
    [Fact]
    public async Task A_role_page_on_the_store_host_links_only_to_the_store_host_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13rsp-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        var storeHost = $"{slug}.{Base}";
        var pages = new (string Path, Type Page)[]
        {
            ("/r/vendor", typeof(TenantHome)), ("/r/vendor/explore", typeof(TenantExplore)), ("/r/vendor/me", typeof(Me)),
            ("/r/vendor/me/listings", typeof(MyListings)), ("/r/vendor/notifications", typeof(Notifications)),
        };
        try
        {
            var (all, roleClean, leaving) = (0, 0, new List<string>());
            foreach (var (visible, page) in pages)
            {
                var (path, html) = await RenderOnHostAsync(host, storeHost, slug, visible, page, vendor, "vendor");
                var (n, off) = Leaving(html, storeHost);
                all += n;
                leaving.AddRange(off.Select(o => $"{visible} → {o}"));
                // والأَداةُ لا تَعمى: صَفحَةُ الدَورِ تَرسُمُ رَوابِطَ دَورِها نَظيفَةً عَلى المُضيف (`/r/vendor/…`) — فَتُعَدُّ.
                roleClean += Href.Matches(html).Count(m => m.Groups[1].Value.StartsWith("/r/vendor", StringComparison.Ordinal));
                output.WriteLine($"[live] {storeHost}{visible} ⇒ {path} · رَوابِط: {n} · خارِجَ المُضيف: {off.Count}");
                Assert.Equal($"/{slug}{visible}", path);
            }
            foreach (var l in leaving) output.WriteLine("  ✗ " + l);
            output.WriteLine($"[live] الصَفَحات: {pages.Length} · الرَوابِطُ المَفحوصَة: {all} · خارِجَ مُضيفِ المَتجَر: {leaving.Count} · /r/vendor نَظيفَةً: {roleClean}");
            Assert.True(all >= pages.Length * 5, $"فَحصٌ أَعمى: {all} رابِطاً");
            Assert.True(roleClean >= pages.Length, $"فَحصٌ أَعمى: {roleClean} رابِطَ دَورٍ نَظيفاً");
            Assert.Empty(leaving);
        }
        finally { await DropAsync(host, slug, vendor); }
    }

    private static Action<HttpContext> OnHost(string hostName, string slug, string? hostRole) => ctx =>
    {
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(hostName);
        ctx.Items[StoreHostItems.Apex] = Base;
        ctx.Items[StoreHostItems.Slug] = slug;
        if (hostRole is not null) ctx.Items[StoreHostItems.Role] = hostRole;
    };

    /// <summary>المُوجَب: «حِفظُ بَياناتِ الدَور» عَلى مُضيفِ المَتجَرِ وعَلى مُضيفِ الدَورِ يُحَوِّلُ إلى بَيتِ الدَورِ <b>عَلى المُضيفِ نَفسِه</b> (نِسبِيّاً) — كانَ مُضيفَ الدَورِ
    /// مُطلَقاً مِن مُضيفِ المَتجَر. والسالِب: بِشَكلِ المَسارِ كَما كان (<c>LiveRoleOnboardingSaveCharacterizationTests</c> بِبَصمَتِه).</summary>
    [Fact]
    public async Task The_onboarding_save_lands_on_the_host_that_holds_the_session_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13rho-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        try
        {
            var route = new Dictionary<string, string> { ["slug"] = slug };
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(vendor.Id, slug, vendor.SessionVersion);
            var generic = $"{AuthSession.CookieNameFor(secure: true, slug)}={token}";   // ‏(‏2026-11-07، W13-4·C2c·2) ما يَحمِلُه مُتَصَفِّحٌ عَلى HTTPS (`OnHost`)
            var onStore = await host.PostFormOnHostAsync(Door, route, generic, OnHost($"{slug}.{Base}", slug, null), ("role_x_attr_y", "1"));
            var onRole = await host.PostFormOnHostAsync(Door, route, generic, OnHost($"{slug}-vendor.{Base}", slug, "vendor"), ("role_x_attr_y", "2"));
            var path = await SaveAsync(host, slug, vendor);
            output.WriteLine($"[live] {slug}.{Base} ⇒ {onStore.Status} {onStore.Location} · {slug}-vendor.{Base} ⇒ {onRole.Status} {onRole.Location} · المَسار ⇒ {path.Status} {path.Location}");
            Assert.Equal((302, "/me/listings"), onStore);
            Assert.Equal((302, "/me/listings"), onRole);
            Assert.Equal((302, $"/{slug}/me/listings"), path);
        }
        finally { await DropAsync(host, slug, vendor); }
    }
}
