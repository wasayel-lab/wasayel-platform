using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ G7 — دَورُ المُضيفِ دَعوى لا مِنحَة، عَلى بابِ القالِبِ الحَيّ (‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4) ═══
//
// **المَقيسُ قَبل** (عِندَ c8dd471): `EffectiveRole.Resolve` يُقَدِّمُ دَورَ الرابِطِ بِلا فَحصِ مِلكِيَّة، و`TenantManage` يَسأَلُ `RolePermissions.Has(…, Role ?? storedRole, …)`
// — فَعُضوٌ دَورُه المُخَزَّنُ «مُشتَرٍ» يَفتَحُ `/{slug}/r/tenant_admin/manage` (يَحمِلُ كوكيَّ ذلكَ الدَورِ مِن دُخولٍ بِـ`as=tenant_admin`: الكاتِبُ يَكتُبُه ولَو رَدَّ
// `AssignRoleAsync` المَنح) فَيَرى لَوحَةَ الإدارَةِ بِأَعدادِها. ومُضيفُ الدَورِ `{slug}-admin.R` يَحقِنُ ذلكَ المَسارَ نَفسَه لِـ`GET /manage`. **والآن**: دَورُ الرابِطِ
// أَو المُضيفِ الإداريُّ لا يَمنَحُ إلّا مَن دَورُه المُخَزَّنُ هُوَ (`EffectiveRole.Resolve`)، والصَفحَةُ تَسأَلُ `PermissionDoor.Opens` نَفسَه؛ وطَلَبُ الكِتابَةِ عَلى
// مُضيفِ الدَورِ لا يَحمِلُ `/r/` أَصلاً فَيُقَيِّمُه `PermissionFilter` بِالدَورِ المُخَزَّن. يُرسَمُ الطَلَبُ كَما يُخرِجُه الحاقِنُ (`AnswerStoreHost` بِقَوالِبِ القالِبِ نَفسِها).
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveRoleHostClaimProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);
    private const string Base = "example.test";

    private static async Task<(User Member, User Admin)> PutStoreAsync(LiveTemplateDoorHost host, string slug)
    {
        await using (var s = host.Store.LightweightSession())
        {
            s.Store(new Tenant
            {
                Id = slug, Name = "مُضيفُ الدَور", BrandColor = "#0f766e",
                Roles = [RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0), RoleCatalog.InstantiateRole(RoleCatalog.Find("tenant_admin")!, 1)],
            });
            await s.SaveChangesAsync();
        }
        User Person(string role, string name) => new()
        {
            Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, ActiveRole = role,
            Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999), PhoneVerified = true,
            // الشُروطُ مَقبولَة — فَلا يَرُدُّ بابَ الكِتابَةِ حارِسُ الشُروطِ قَبلَ حارِسِ الصَلاحِيَّة (الحارِسُ المَقيسُ هُنا).
            AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
        };
        var (member, admin) = (Person("customer", "عُضوٌ مُشتَرٍ"), Person("tenant_admin", "مُديرُ المَتجَر"));
        await using (var s = host.Store.LightweightSession(slug)) { s.Store(member); s.Store(admin); await s.SaveChangesAsync(); }
        return (member, admin);
    }

    /// <summary>صَفحَةُ <c>/manage</c> عَلى <c>{slug}-admin.R</c> كَما يَحقِنُها الحاقِن، بِكوكيِّ الدَورِ الإداريِّ والعامِّ لِـ<paramref name="who"/>.</summary>
    private static async Task<(string Path, string Html)> RenderManageOnAdminHostAsync(LiveTemplateDoorHost host, string slug, User who)
    {
        var hostName = $"{slug}-admin.{Base}";
        var answer = TenantResolverMiddleware.AnswerStoreHost(hostName, Base, "GET", "/manage", "",
            "https", host.Services.GetRequiredService<RolePageTemplates>());
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString(hostName);
        ctx.Request.Path = answer.Path ?? "/";
        ctx.Items[StoreHostItems.Apex] = Base;
        ctx.Items[StoreHostItems.Slug] = answer.Slug;
        if (answer.Role is { } r) ctx.Items[StoreHostItems.Role] = r;
        ctx.Items[StoreHostItems.OriginalPath] = "/manage";
        var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(who.Id, slug, who.SessionVersion);
        // ‏(‏2026-11-07، W13-4·C2c·2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) الاسمُ بِقَرارِ الكاتِبِ والقارِئِ نَفسِه لِهذا الطَلَب (`CookieNameOn`) — ما يَحمِلُه مُتَصَفِّحٌ عَلى HTTPS.
        ctx.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(ctx.Request, slug, "tenant_admin")}={token}; {AuthSession.CookieNameOn(ctx.Request, slug)}={token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize($"https://{hostName}/", $"https://{hostName}{ctx.Request.Path}");
        var auth = scope.ServiceProvider.GetRequiredService<AuthSession>();
        auth.SetTenant(slug);
        auth.Load(ctx, slug);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        var html = System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TenantManage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TenantManage.Slug)] = slug,
                [nameof(TenantManage.Role)] = AuthSession.ExtractRoleFromPath(ctx.Request.Path),
            }))).ToHtmlString()));
        return (ctx.Request.Path.Value!, html);
    }

    [Fact]
    public async Task A_member_on_the_admin_host_is_refused_the_manage_page_and_the_admin_is_not_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13g7-");
        var (member, admin) = await PutStoreAsync(host, slug);
        try
        {
            var (path, asMember) = await RenderManageOnAdminHostAsync(host, slug, member);
            var (_, asAdmin) = await RenderManageOnAdminHostAsync(host, slug, admin);
            var refused = asMember.Contains("لا صَلاحِيَّة", StringComparison.Ordinal);
            var memberSees = asMember.Contains("acm-wizard-title", StringComparison.Ordinal);
            var adminSees = asAdmin.Contains("acm-wizard-title", StringComparison.Ordinal) && !asAdmin.Contains("لا صَلاحِيَّة", StringComparison.Ordinal);
            output.WriteLine($"[live] {slug}-admin.{Base}/manage ⇒ {path} · العُضو: {(refused ? "مَرفوض" : "يَرى")} (لَوحَة {memberSees}) · المُدير: {(adminSees ? "يَرى" : "مَرفوض")}");
            Assert.Equal($"/{slug}/r/tenant_admin/manage", path);
            Assert.True(refused);
            Assert.False(memberSees);
            Assert.True(adminSees);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.Delete<User>(member.Id); s.Delete<User>(admin.Id); await s.SaveChangesAsync(); }
            await CleanAsync(host, [], slug);
        }
    }

    [Fact]
    public async Task A_write_on_the_admin_host_carries_no_role_and_is_judged_by_the_stored_role_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13g7w-");
        var (member, admin) = await PutStoreAsync(host, slug);
        try
        {
            var answer = TenantResolverMiddleware.AnswerStoreHost($"{slug}-admin.{Base}", Base, "POST", "/listings/create", "",
                "https", host.Services.GetRequiredService<RolePageTemplates>());
            var route = new Dictionary<string, string> { ["slug"] = slug };
            var asMember = await host.PostFormAsync("/{slug}/listings/create", route, slug,
                ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion), ("as", "tenant_admin"), ("title", "x"));
            var asAdmin = await host.PostFormAsync("/{slug}/listings/create", route, slug,
                ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(admin.Id, slug, admin.SessionVersion), ("as", "tenant_admin"));
            output.WriteLine($"[live] POST {slug}-admin.{Base}/listings/create ⇒ {answer.Verdict} {answer.Path} (دَور {answer.Role}) · العُضو ⇒ {asMember.Status} · المُدير ⇒ {asAdmin.Status}");
            Assert.Equal((StoreHostVerdict.Rewrite, $"/{slug}/listings/create"), (answer.Verdict, answer.Path));
            Assert.Null(AuthSession.ExtractRoleFromPath(answer.Path));
            Assert.Equal(StatusCodes.Status403Forbidden, asMember.Status);
            Assert.NotEqual(StatusCodes.Status403Forbidden, asAdmin.Status);
        }
        finally
        {
            await using (var s = host.Store.LightweightSession(slug)) { s.Delete<User>(member.Id); s.Delete<User>(admin.Id); await s.SaveChangesAsync(); }
            await CleanAsync(host, [], slug);
        }
    }
}
