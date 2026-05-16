using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.V1.App.Auth;

/// <summary>
/// حالَة المُستَخدِم في الـ Blazor circuit. مَملوكَة per-request.
/// تُحَمَّل من cookie في كلّ طَلَب. الـ MainLayout يَستَهلِكها لِيُقَرِّر
/// عَرض login button أو user info.
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

    public void SignIn(HttpContext http, AuthResult result)
    {
        UserId = result.UserId; UserName = result.FullName; Token = result.Token; TenantSlug ??= "";
        var name = CookieName(TenantSlug ?? "");
        var opts = new CookieOptions
        {
            HttpOnly = true, IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            SameSite = SameSiteMode.Lax,
            Path = $"/{TenantSlug}"
        };
        http.Response.Cookies.Append(name, result.Token, opts);
        http.Response.Cookies.Append(name + ".name", result.FullName, opts);
        Changed?.Invoke();
    }

    public void SignOut(HttpContext http)
    {
        var slug = TenantSlug ?? "";
        Clear();
        http.Response.Cookies.Delete(CookieName(slug), new CookieOptions { Path = $"/{slug}" });
        http.Response.Cookies.Delete(CookieName(slug) + ".name", new CookieOptions { Path = $"/{slug}" });
        Changed?.Invoke();
    }

    public void SetTenant(string slug) => TenantSlug = slug;
    public void Clear() { UserId = null; UserName = null; Token = null; }

    public static string CookieName(string tenantSlug) => $".acommerce.auth.{tenantSlug}";
}
