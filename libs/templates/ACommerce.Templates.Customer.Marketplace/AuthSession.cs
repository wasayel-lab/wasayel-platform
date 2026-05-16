using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// حالَة المُستَخدِم في الـ Blazor circuit. تُحَمَّل من cookie في كلّ
/// طَلَب SSR. كَتابَة الـ cookie تَجري في endpoints الـ SSR forms.
/// </summary>
public sealed class AuthSession
{
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? Token { get; private set; }
    public string? TenantSlug { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public event Action? Changed;

    public void Load(HttpContext http, string requiredTenantSlug)
    {
        var token = http.Request.Cookies[CookieName(requiredTenantSlug)];
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null) { Clear(); return; }
        var (uid, slug, _) = parsed.Value;
        if (slug != requiredTenantSlug) { Clear(); return; }
        UserId = uid; TenantSlug = slug; Token = token;
        UserName = http.Request.Cookies[CookieName(slug) + ".name"] ?? "—";
        Changed?.Invoke();
    }

    public void SetTenant(string slug) => TenantSlug = slug;
    public void Clear() { UserId = null; UserName = null; Token = null; }

    public static string CookieName(string tenantSlug) => $".acommerce.auth.{tenantSlug}";

    /// <summary>يُكتَب من SSR endpoint بَعد نَجاح المُصادَقَة.</summary>
    public static void WriteCookie(HttpResponse res, string tenantSlug, AuthResult auth)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax,
            Path = "/"           // مَتاح لكلّ مَسارات الـ tenant
        };
        res.Cookies.Append(CookieName(tenantSlug), auth.Token, opts);
        res.Cookies.Append(CookieName(tenantSlug) + ".name", auth.FullName, opts);
    }

    public static void UpdateNameCookie(HttpResponse res, string tenantSlug, string newName)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true, IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax, Path = "/"
        };
        res.Cookies.Append(CookieName(tenantSlug) + ".name", newName, opts);
    }

    public static void ClearCookie(HttpResponse res, string tenantSlug)
    {
        var opts = new CookieOptions { Path = "/" };
        res.Cookies.Delete(CookieName(tenantSlug), opts);
        res.Cookies.Delete(CookieName(tenantSlug) + ".name", opts);
    }
}
