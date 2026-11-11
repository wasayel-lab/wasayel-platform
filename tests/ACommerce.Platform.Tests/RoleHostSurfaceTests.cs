using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Gates;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُضيفاتُ الأَدوار: لا فَهرَسَة، وحُضورُ الدَورِ طازَج، ودَعوى الإدارَةِ لِغَيرِ مُديرِها لا تَرسُمُ ما يُرَدّ، والعَودَةُ نَظيفَة (‏2026-11-11، W13-4·C2z·5) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِعين): `{store}-vendor.localhost/sitemap.xml` ← أَربَعُ `<loc>` تَحتَ مُضيفِ الدَورِ وcanonical ذاتِيّ؛ دَورٌ أُضيفَ ← بِطاقَتُه فَوراً ومُضيفُه
// 404 حَتّى ‏201 ثانِيَة؛ عُضوٌ عَلى `{store}-admin.localhost/` ← بَطَلُ الإدارَةِ وزِرُّه `/manage` يُرَدّ، ودُخولُه هُناكَ يَكتُبُ كَعكَةَ `tenant_admin`؛ عَودَةُ المُفَضَّلَةِ
// `/{store}/explore` عَلى المُضيف ← التَبويبُ النَشِطُ مُطفَأ؛ ونَصُّ تَصادُمِ الوَكيلِ جُملَةٌ حَرفِيَّة. وما لا يَتَغَيَّرُ في `RoleHostSurfaceCharacterizationTests` بِبايتاتِه.

public class RoleHostSurfaceTests(ITestOutputHelper output)
{
    [Fact]
    public void A_role_host_robots_allows_crawling_and_names_no_sitemap()
    {
        // ‏(‏2026-11-11، W13-4·C2f·5 — C2z-PATHS-R1-05) كانَ «Disallow: /» فَيُخفي noindex عَن الزاحِف؛ الآنَ «Allow: /» وnoindex عَلى كُلِّ جَواب.
        Assert.Equal("User-agent: *\nAllow: /\n", SeoDocuments.RoleHostRobotsTxt);
        Assert.DoesNotContain("Disallow", SeoDocuments.RoleHostRobotsTxt, StringComparison.Ordinal);
        Assert.DoesNotContain("Sitemap:", SeoDocuments.RoleHostRobotsTxt, StringComparison.Ordinal);
        var empty = SeoDocuments.BuildSitemapXml([]);
        Assert.DoesNotContain("<loc>", empty, StringComparison.Ordinal);
        Assert.Contains("urlset", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void The_seo_handlers_answer_the_role_host_before_the_store_host()
    {
        var src = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/kits/Tenants/ACommerce.Kit.Tenants.Server/SeoHandlers.cs"));
        var role = src.IndexOf("context.Items[StoreHostItems.Role] is string)", StringComparison.Ordinal);
        var store = src.IndexOf("context.Items[StoreHostItems.Slug] is string hostSlug", StringComparison.Ordinal);
        Assert.True(role > 0 && store > role, "فَرعُ مُضيفِ الدَورِ يَجِبُ أَن يَسبِقَ فَرعَ مُضيفِ المَتجَرِ في الخَريطَة.");
        Assert.Contains("? SeoDocuments.RoleHostRobotsTxt", src, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ashare-vendor.example.test", "noindex")]
    [InlineData("ashare-customer.example.test", "noindex")]
    [InlineData("ashare.example.test", "")]
    [InlineData("example.test", "")]
    public async Task Every_answer_on_a_role_host_carries_noindex_and_nothing_else_does(string host, string robots)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString(host);
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/explore";
        var mw = new SubdomainTenantPathMiddleware(_ => Task.CompletedTask, new SubdomainTenancyOptions { BaseDomain = "example.test" },
            new ServiceCollection().BuildServiceProvider());
        await mw.InvokeAsync(ctx);
        output.WriteLine($"{host} ⇒ X-Robots-Tag «{ctx.Response.Headers["X-Robots-Tag"]}»");
        Assert.Equal(robots, ctx.Response.Headers["X-Robots-Tag"].ToString());
    }

    [Theory]
    [InlineData("tenant_admin", "customer", "customer")]
    [InlineData("tenant_admin", null, null)]
    [InlineData("tenant_admin", "vendor", "vendor")]
    [InlineData("tenant_admin", "tenant_admin", "tenant_admin")]
    public void An_unheld_admin_claim_renders_the_members_stored_role(string url, string? stored, string? rendered)
        => Assert.Equal(rendered, EffectiveRole.Resolve(null, false, url, stored));

    [Fact]
    public void The_home_the_shell_and_the_account_page_ask_the_one_decision()
    {
        string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace", rel));
        Assert.Contains("var effectiveRoleSlug = ACommerce.Templates.Customer.Marketplace.Gates.EffectiveRole.Resolve(null, false, Role, home.StoredActiveRole);",
            Read("Components/Pages/TenantHome.razor"), StringComparison.Ordinal);
        Assert.Contains("EffectiveRole.Resolve(null, false, urlRoleInit, shell.StoredActiveRole)", Read("Components/Layout/MainLayout.razor"), StringComparison.Ordinal);
        Assert.Contains("EffectiveRole.Resolve(null, false, Role, me.ActiveRole)", Read("Components/Pages/Me.razor"), StringComparison.Ordinal);
        var doors = Read("MarketplaceTemplateExtensions.cs");
        var claims = Regex.Matches(doors, @"var asRole = await HeldAdminClaimOrNoneAsync\(store, slug, result\.UserId, ").Count;
        var cookies = Regex.Matches(doors, @"AuthSession\.WriteCookie\(res, slug, result,\s*role: string\.IsNullOrEmpty\(asRole\)").Count;
        output.WriteLine($"أَبوابُ تَحَقُّقٍ تَكتُبُ كَعكَةَ الدَور: {cookies} · تُسقِطُ الدَعوى قَبلَها: {claims}");
        Assert.True(cookies >= 3, $"فَحصٌ أَعمى: {cookies}");
        Assert.Equal(cookies, claims);
    }

    [Fact]
    public void The_favourite_return_targets_and_the_active_tab_follow_the_host()
    {
        string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace", rel));
        var raw = new[] { "Components/Pages/TenantExplore.razor", "Components/Pages/TenantHome.razor", "Components/Pages/Favorites.razor", "Components/Pages/TenantListingDetail.razor" }
            .Sum(f => Regex.Matches(Read(f), @"(FavoriteFormReturn|name=""return"" value)=""@\(\$""/\{Slug\}").Count);
        output.WriteLine($"أَهدافُ عَودَةٍ خامٌ بِشَكلِ المَسار: {raw}");
        Assert.Equal(0, raw);
        Assert.Contains("StoreHostPage.CanonicalPath(ACommerce.Templates.Customer.Marketplace.Services.Ux.StoreHostPage.VisiblePath(navCtx)",
            Read("Components/Layout/MainLayout.razor"), StringComparison.Ordinal);
        // وعَلى المُضيفِ الهَدَفُ نَظيف، والمَسارُ القانونِيُّ لِوُصولٍ بِشَكلِ المَسارِ هُوَ النَظيف
        var view = new HostView("example.test", "ashare", "https", "");
        Assert.Equal("/explore", StoreUrl.For(view, "ashare", null, "explore"));
        Assert.Equal("/explore", StoreHostPage.CanonicalPath("/ashare/explore", "ashare"));
    }

    [Fact]
    public void The_agent_role_host_refusal_reads_the_dictionary_key_the_two_surfaces_read()
    {
        var src = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/AgentService.cs"));
        Assert.Contains("{LocaleCatalog.Text(LocaleCatalog.Arabic, RoleHostTakenKey)}", src, StringComparison.Ordinal);
        Assert.DoesNotContain("مُضيفُ الدَورِ «{takenHost}» اسمُ مَتجَرٍ قائِم", src, StringComparison.Ordinal);
        Assert.Equal("roles.save.err_role_host_taken", ACommerce.Templates.Customer.Marketplace.Services.AgentToolExecutor.RoleHostTakenKey);
        Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find("ar", ACommerce.Templates.Customer.Marketplace.Services.AgentToolExecutor.RoleHostTakenKey)));
    }
}

/// <summary>حُضورُ الدَورِ عَلى مُضيفِه يُقرَأُ طازَجاً عَلى Postgres — الوَسيطُ الحَقيقيُّ بِذاكِرَةٍ دافِئَة، والوَثيقَةُ تَتَغَيَّرُ تَحتَه بِلا إخلاء.</summary>
[Collection(LiveProofCollection.Name)]
public class LiveRoleHostPresenceProofTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_role_added_or_removed_answers_on_its_host_at_once_on_Postgres()
    {
        if (!LiveSessionLogoutProofTests.Enabled) return;
        var slug = "zz-rhp-" + Guid.NewGuid().ToString("N")[..8];
        await using var host = LiveTemplateDoorHost.Build();
        var store = host.Services.GetRequiredService<IDocumentStore>();
        async Task SaveRoles(params string[] roles)
        {
            await using var s = store.LightweightSession();
            s.Store(new Tenant { Id = slug, Name = "اختبار", Roles = roles.Select(r => new Role { Slug = r, CatalogSlug = r }).ToList() });
            await s.SaveChangesAsync();
        }
        await SaveRoles("customer");
        var mw = new TenantResolverMiddleware(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }, store,
            new MemoryCache(new MemoryCacheOptions()), new SubdomainTenancyOptions { BaseDomain = "example.test" });
        async Task<int> Hit(string label)
        {
            var ctx = new DefaultHttpContext { RequestServices = host.Services };
            ctx.Request.Host = new HostString($"{label}.example.test");
            ctx.Request.Path = $"/{slug}/r/x";
            await mw.InvokeAsync(ctx);
            return ctx.Response.StatusCode;
        }
        var before = (await Hit($"{slug}-customer"), await Hit($"{slug}-vendor"), await Hit(slug));
        await SaveRoles("vendor");   // بِلا إخلاءٍ لِلذاكِرَة — كَما يَكتُبُ أَيُّ كاتِبٍ لِلأَدوار
        var after = (await Hit($"{slug}-customer"), await Hit($"{slug}-vendor"), await Hit(slug));
        output.WriteLine($"قَبلُ (customer, vendor, المَتجَر) ⇒ {before} · بَعدَ الاستِبدالِ ⇒ {after}");
        Assert.Equal((200, 404, 200), before);
        Assert.Equal((404, 200, 200), after);
    }
}
