using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// الطَّبَقَة الأَولى: تَوثيق. تَقرَأ الـ cookie المُناسِب لِلدَور المُستَخرَج
/// مِن URL (نَمَط <c>/{slug}/r/{role}/…</c>)، تَفُكّ token، تَتَحَقَّق
/// مِن مُطابَقَة الـ tenant، ثُمَّ تَكتُب <c>userId</c> في
/// <see cref="HttpContext.Items"/> لِيَستَخدِمها الـ handler بِدون إعادَة
/// فَكّ. عَلى الفَشَل تُعيد توجيه لِصَفحَة الدُخول (POST forms) أَو 401
/// (JSON APIs).
/// </summary>
public sealed class AuthFilter : IEndpointFilter
{
    private readonly bool _redirectOnFailure;

    public AuthFilter(bool redirectOnFailure = true)
    {
        _redirectOnFailure = redirectOnFailure;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var slug = http.Request.RouteValues["slug"]?.ToString() ?? "";
        if (string.IsNullOrEmpty(slug)) return Results.BadRequest();

        var role  = AuthSession.ExtractRoleFromPath(http.Request.Path);
        var name  = AuthSession.CookieName(slug, role);
        var token = http.Request.Cookies[name]
                 ?? (role is not null ? null : http.Request.Cookies[AuthSession.CookieName(slug)]);
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null || parsed.Value.TenantSlug != slug)
        {
            return _redirectOnFailure
                ? Results.Redirect(AuthSession.LinkFor(slug, role,
                    $"login?returnUrl={Uri.EscapeDataString(http.Request.Path + http.Request.QueryString)}"))
                : Results.Unauthorized();
        }

        http.Items[GateKeys.UserId]   = parsed.Value.UserId;
        http.Items[GateKeys.SlugItem] = slug;
        http.Items[GateKeys.Role]     = role;
        return await next(ctx);
    }
}
