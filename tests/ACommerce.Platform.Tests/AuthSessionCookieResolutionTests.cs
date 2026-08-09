using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// حَلّ جَلسَة المُصادَقَة عَبر نَمَطَي المَسار: بِدَور (<c>/{slug}/r/{role}/…</c>)
/// وَبِلا دَور (<c>/{slug}/…</c>).
///
/// <para><b>العَيب المُلتَقَط:</b> الدُخول عَبر <c>/{slug}/r/{role}/login</c>
/// يَكتُب <c>.acommerce.auth.{slug}.{role}</c> وَحدَه، بَينَما كُلّ نِقاط الـ
/// POST (‏listings/create، ‏listings/{id}/offers، ‏cart، ‏checkout، ‏deals…)
/// مُسَجَّلَة عَلى مَسارات بِلا <c>/r/{role}/</c> وَكانَت تَقرَأ الاسم العامّ
/// حَرفيّاً — فَتَراهُ مَعدوماً وَتُعيد توجيهاً إلى login رَغم دُخول ناجِح.
/// <see cref="RoleCookie_IsFound_FromRolelessPath"/> هُوَ الالتِقاط: كانَ
/// يَفشَل قَبلَ الإصلاح.</para>
///
/// <para>وَ<see cref="OtherRoleCookie_IsNotAccepted_OnRoleScopedPath"/> يَحرُس
/// الاتِّجاه المُعاكِس: التَّوسيع يَجِب ألّا يَكسِر عَزل الأَدوار.</para>
/// </summary>
public class AuthSessionCookieResolutionTests
{
    private const string Slug = "ashare";

    // لا نَمَسّ AuthHandlers.TokenSecret: هُوَ حالَة عامَّة (static) وَxunit
    // يُوازي بَينَ الأَصناف — تَغييرُه هُنا قَد يُفسِد اختِباراً آخَر. وَلا
    // حاجَة: MakeToken وَParseToken يَستَخدِمان نَفس السِرّ أَيّاً كانَ.

    private static HttpRequest Request(string path, params (string Name, string Value)[] cookies)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (cookies.Length > 0)
            ctx.Request.Headers["Cookie"] =
                string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        return ctx.Request;
    }

    private static string TokenFor(Guid userId, string slug = Slug)
        => AuthHandlers.MakeToken(userId, slug);

    // ── النَّمَط المُصاب: مَسار بِلا دَور وَcookie بِدَور ────────────────────

    [Fact]
    public void RoleCookie_IsFound_FromRolelessPath()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(user)));

        var token = AuthSession.ResolveToken(req, Slug);

        Assert.NotNull(token);
        Assert.Equal(user, AuthHandlers.ParseToken(token)!.Value.UserId);
    }

    [Theory]
    [InlineData("/ashare/listings/create")]
    [InlineData("/ashare/listings/8a1f0f4e-0000-4000-8000-000000000001/offers")]
    [InlineData("/ashare/checkout/submit")]
    [InlineData("/ashare/cart/clear")]
    [InlineData("/api/ashare/push/subscribe")]
    public void RolelessPostPaths_SeeRoleSession(string path)
    {
        var user = Guid.NewGuid();
        var req = Request(path, (AuthSession.CookieName(Slug, "host"), TokenFor(user)));

        Assert.Equal(user, AuthHandlers.ParseToken(
            AuthSession.ResolveToken(req, Slug))!.Value.UserId);
    }

    // ── النَّمَط السَّليم أَصلاً: مَسار بِدَور ─────────────────────────────

    [Fact]
    public void RoleCookie_IsFound_FromRoleScopedPath()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/r/host/listings",
            (AuthSession.CookieName(Slug, "host"), TokenFor(user)));

        Assert.Equal(user, AuthHandlers.ParseToken(
            AuthSession.ResolveToken(req, Slug))!.Value.UserId);
    }

    [Fact]
    public void LegacyGenericCookie_StillWorks_OnRolelessPath()
    {
        var user = Guid.NewGuid();
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug), TokenFor(user)));

        Assert.Equal(user, AuthHandlers.ParseToken(
            AuthSession.ResolveToken(req, Slug))!.Value.UserId);
    }

    // ── حِراسَة عَزل الأَدوار: التَّوسيع لا يَتَسَرَّب لِلمَسارات المُسَوَّرَة ──

    [Fact]
    public void OtherRoleCookie_IsNotAccepted_OnRoleScopedPath()
    {
        var req = Request($"/{Slug}/r/guest/listings",
            (AuthSession.CookieName(Slug, "host"), TokenFor(Guid.NewGuid())));

        Assert.Null(AuthSession.ResolveToken(req, Slug));
    }

    [Fact]
    public void GenericCookie_IsNotAccepted_OnRoleScopedPath()
    {
        var req = Request($"/{Slug}/r/host/listings",
            (AuthSession.CookieName(Slug), TokenFor(Guid.NewGuid())));

        Assert.Null(AuthSession.ResolveToken(req, Slug));
    }

    [Fact]
    public void RoleScopedPath_PrefersItsOwnRole_OverGeneric()
    {
        var host = Guid.NewGuid();
        var other = Guid.NewGuid();
        var req = Request($"/{Slug}/r/host/listings",
            (AuthSession.CookieName(Slug), TokenFor(other)),
            (AuthSession.CookieName(Slug, "host"), TokenFor(host)));

        Assert.Equal(host, AuthHandlers.ParseToken(
            AuthSession.ResolveToken(req, Slug))!.Value.UserId);
    }

    // ── عَزل المَتاجِر وَالتَّوكِن الفاسِد ──────────────────────────────────

    [Fact]
    public void CookieOfAnotherTenant_IsRejected()
    {
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(Guid.NewGuid(), "ejar")));

        Assert.Null(AuthSession.ResolveToken(req, Slug));
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), "bm90LWEtdG9rZW4="));

        Assert.Null(AuthSession.ResolveToken(req, Slug));
    }

    [Fact]
    public void NoCookies_ResolveToNull()
        => Assert.Null(AuthSession.ResolveToken(Request($"/{Slug}/listings/create"), Slug));

    // ── الاسم يَتبَع نَفس عائِلَة الـ token ───────────────────────────────

    [Fact]
    public void UserName_ComesFromSameCookieFamilyAsToken()
    {
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host"), TokenFor(Guid.NewGuid())),
            (AuthSession.CookieName(Slug, "host") + ".name", "Hoor"));

        Assert.Equal("Hoor", AuthSession.ResolveUserName(req, Slug));
    }

    [Fact]
    public void NameCookieAlone_IsNotMistakenForAToken()
    {
        var req = Request($"/{Slug}/listings/create",
            (AuthSession.CookieName(Slug, "host") + ".name", "Hoor"));

        Assert.Null(AuthSession.ResolveToken(req, Slug));
    }

    // ── جانِب الكِتابَة: دُخول بِدَور يَكتُب النُّسخَة العامَّة كَذلِك ─────────

    [Fact]
    public void WriteCookie_WithRole_AlsoWritesGenericCopy()
    {
        var ctx = new DefaultHttpContext();
        var user = Guid.NewGuid();
        AuthSession.WriteCookie(ctx.Response, Slug,
            new AuthResult(user, "Hoor", "0500000000", TokenFor(user), "host"), role: "host");

        var written = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthSession.CookieName(Slug, "host") + "=", written);
        Assert.Contains(AuthSession.CookieName(Slug) + "=", written);
    }

    [Fact]
    public void WriteCookie_WithoutRole_WritesOnlyGeneric()
    {
        var ctx = new DefaultHttpContext();
        var user = Guid.NewGuid();
        AuthSession.WriteCookie(ctx.Response, Slug,
            new AuthResult(user, "Hoor", "0500000000", TokenFor(user), "host"), role: null);

        var written = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthSession.CookieName(Slug) + "=", written);
        Assert.DoesNotContain(".acommerce.auth.ashare.host=", written);
    }
}
