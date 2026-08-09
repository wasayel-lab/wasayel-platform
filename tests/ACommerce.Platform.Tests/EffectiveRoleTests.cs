using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// الدَور الفَعّال عَلى نِقاط الكِتابَة بِلا دَور (تَعَدُّد الأَدوار المُتَزامِن).
///
/// <para><b>العَيب المُلتَقَط:</b> نِقاط الكِتابَة بِلا دَور كانَت تَسقُط إلى
/// <c>User.ActiveRole</c> المُشتَرَك = آخِر دُخول يَفوز. الآن الدَور الصَّريح
/// <c>as</c> (لِمَن يَملِكُه فِعلاً) يَتَفَوَّق دون كِتابَة الحَقل المُشتَرَك.
/// حِراسَة الأَمان في
/// <see cref="UnownedAs_CannotEscalate_DeniedByPermission"/> — قيمَة <c>as</c>
/// لِدَور لا يَملِكُه المُستَخدِم لا تُصَعِّد الصَلاحِيَّة.</para>
///
/// <para>إصلاح الـ403 (المَرحَلَة 1) في <see cref="ForbidResultTests"/>.</para>
/// </summary>
public class EffectiveRoleTests
{
    private const string Slug = "ashare";

    private static string TokenFor(Guid userId, string slug = Slug)
        => AuthHandlers.MakeToken(userId, slug);

    private static HttpRequest Request(string path, params (string Name, string Value)[] cookies)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (cookies.Length > 0)
            ctx.Request.Headers["Cookie"] =
                string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        return ctx.Request;
    }

    // الأَدوار الفِعليّة مِن الكَتالوج: host يَملِك listing.create، customer لا.
    private static IReadOnlyList<Role> TenantRoles() => new List<Role>
    {
        RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0),
        RoleCatalog.InstantiateRole(RoleCatalog.Find("host")!,     1),
    };

    // ─── القَرار النَّقيّ: الصَّريح (المَملوك) ثُمَّ URL ثُمَّ المُخَزَّن ──────

    [Fact]
    public void ExplicitOwnedAs_BeatsActiveRole()
        => Assert.Equal("host", EffectiveRole.Resolve("host", ownsFormAs: true, urlRole: null, activeRole: "customer"));

    [Fact]
    public void UnownedAs_IsIgnored_FallsToActiveRole()
        => Assert.Equal("customer", EffectiveRole.Resolve("host", ownsFormAs: false, urlRole: null, activeRole: "customer"));

    [Fact]
    public void UnownedAs_FallsToUrlRole_WhenPresent()
        => Assert.Equal("driver", EffectiveRole.Resolve("host", ownsFormAs: false, urlRole: "driver", activeRole: "customer"));

    [Fact]
    public void UrlRole_BeatsActiveRole_WhenNoAs()
        => Assert.Equal("driver", EffectiveRole.Resolve("", ownsFormAs: false, urlRole: "driver", activeRole: "customer"));

    [Fact]
    public void ActiveRole_IsLastResort()
        => Assert.Equal("customer", EffectiveRole.Resolve("", ownsFormAs: false, urlRole: null, activeRole: "customer"));

    [Fact]
    public void EmptyAs_IsIgnored_EvenIfMarkedOwned()
        => Assert.Equal("customer", EffectiveRole.Resolve("", ownsFormAs: true, urlRole: null, activeRole: "customer"));

    [Fact]
    public void AllEmpty_ResolvesToNull()
        => Assert.Null(EffectiveRole.Resolve("", ownsFormAs: false, urlRole: null, activeRole: ""));

    // ─── بُرهان المِلكِيَّة: cookie جَلسَة الدَور لِنَفس المُستَخدِم ──────────

    [Fact]
    public void OwnsRole_True_ForValidRoleCookie_SameUser()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(user)));
        Assert.True(EffectiveRole.OwnsRole(req, Slug, user, "host"));
    }

    [Fact]
    public void OwnsRole_False_WhenNoCookieForThatRole()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "customer"), TokenFor(user)));
        Assert.False(EffectiveRole.OwnsRole(req, Slug, user, "host"));
    }

    [Fact]
    public void OwnsRole_False_ForDifferentUser()
    {
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(Guid.NewGuid())));
        Assert.False(EffectiveRole.OwnsRole(req, Slug, Guid.NewGuid(), "host"));
    }

    [Fact]
    public void OwnsRole_False_ForCookieOfAnotherTenant()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(user, "ejar")));
        Assert.False(EffectiveRole.OwnsRole(req, Slug, user, "host"));
    }

    [Fact]
    public void OwnsRole_False_ForEmptyRole()
    {
        var req = Request($"/{Slug}/listings/create");
        Assert.False(EffectiveRole.OwnsRole(req, Slug, Guid.NewGuid(), ""));
    }

    [Fact]
    public void OwnsRole_False_ForTamperedToken()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), "bm90LWEtdG9rZW4="));
        Assert.False(EffectiveRole.OwnsRole(req, Slug, user, "host"));
    }

    [Fact]
    public void OwnsRole_False_ForTenantAdmin_EvenWithValidSelfToken()
    {
        // الـ token مُشتَرَك بَينَ أَدوار المُستَخدِم، فَقَد يَنسَخُه تَحت اسم
        // .tenant_admin. الحَدّ الأَمنيّ: as لا يَملِك الإداريّ أَبَداً.
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "tenant_admin"), TokenFor(user)));
        Assert.False(EffectiveRole.OwnsRole(req, Slug, user, "tenant_admin"));
    }

    // ─── التَّركيب مَع التَّفويض: العِلاج وَالحِراسَة السالِبَة ──────────────

    [Fact]
    public void ConcurrentRole_OwnedHostAs_Authorizes_ListingCreate_DespiteCustomerActiveRole()
    {
        // نَفس سيناريو التَّسابُق: ActiveRole=customer لكِنّ المُستَخدِم يَملِك host.
        var role = EffectiveRole.Resolve("host", ownsFormAs: true, urlRole: null, activeRole: "customer");
        Assert.True(RolePermissions.Has(TenantRoles(), role, "listing.create"));
    }

    [Fact]
    public void UnownedAs_CannotEscalate_DeniedByPermission()
    {
        // as=host لِمُستَخدِم لا يَملِك host → يُهمَل → يَسقُط إلى customer →
        // التَّفويض يَرفُض (لا يُخدَع النِّظام بِـ as).
        var role = EffectiveRole.Resolve("host", ownsFormAs: false, urlRole: null, activeRole: "customer");
        Assert.Equal("customer", role);
        Assert.False(RolePermissions.Has(TenantRoles(), role, "listing.create"));
    }

    // ─── الحَسم المَربوط بِالـ HTTP: النَّموذَج + الـ cookie مَعاً ──────────

    [Fact]
    public async Task ResolveAsync_ExplicitAs_WithMatchingCookie_Wins()
    {
        var user = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = $"/{Slug}/listings/create";
        ctx.Request.Headers["Cookie"] = $"{AuthSession.CookieName(Slug, "host")}={TokenFor(user)}";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new()
        {
            ["as"] = new StringValues("host")
        });

        var role = await EffectiveRole.ResolveAsync(ctx, Slug, user, activeRole: "customer");
        Assert.Equal("host", role);
    }

    [Fact]
    public async Task ResolveAsync_ExplicitAs_WithoutOwnedCookie_FallsToActiveRole()
    {
        var user = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = $"/{Slug}/listings/create";
        // يَحمِل customer فَقَط، لا host.
        ctx.Request.Headers["Cookie"] = $"{AuthSession.CookieName(Slug, "customer")}={TokenFor(user)}";
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Form = new FormCollection(new()
        {
            ["as"] = new StringValues("host")
        });

        var role = await EffectiveRole.ResolveAsync(ctx, Slug, user, activeRole: "customer");
        Assert.Equal("customer", role);
    }
}
