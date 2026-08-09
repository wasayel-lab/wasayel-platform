using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// الطَّبَقَة الثالِثَة: صَلاحِيَّة. يَأخُذ permission code (مَثَلاً
/// <c>"listing.create"</c> أَو <c>"offer.submit"</c>). يَفتَرِض
/// <see cref="AuthFilter"/> سَبَقَه. لَو المَتجَر بِلا أَدوار = legacy
/// مَفتوح (يَسمَح). لَو الدَور النَّشِط لا يَملِك الصَلاحِيَّة = 403.
/// </summary>
public sealed class PermissionFilter : IEndpointFilter
{
    private readonly string _permission;

    public PermissionFilter(string permission) => _permission = permission;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http  = ctx.HttpContext;
        var store = http.RequestServices.GetRequiredService<IDocumentStore>();
        var slug  = http.Slug();
        var userId = http.UserIdOrNull();
        if (userId is null) return Results.Unauthorized();

        await using var g = store.QuerySession();
        var tenant = await g.LoadAsync<Tenant>(slug);
        if (tenant is null) return Results.NotFound();
        if (tenant.Roles.Count == 0) return await next(ctx);   // legacy mode

        await using var t = store.QuerySession(slug);
        var user = await t.LoadAsync<User>(userId.Value);
        // 403 مُباشَر (StatusCode) لا Results.Forbid(): الأَخير يَطلُب
        // IAuthenticationService غَير المُسَجَّل في المِنَصَّة فَيَرمي 500
        // بَدَل 403 عِندَ كُلّ رَفض صَلاحِيَّة.
        if (user is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

        // الدَور الفَعّال لِلتَّفويض: الصَّريح (as على نُقطَة الكِتابَة، لِمَن
        // يَملِكُه فِعلاً) ثُمَّ دَور الـ URL ثُمَّ ActiveRole المُخَزَّن —
        // يُحَرِّر تَعَدُّد الأَدوار المُتَزامِن دون كِتابَة الحَقل المُشتَرَك.
        var effectiveRole =
            await EffectiveRole.ResolveAsync(http, slug, userId.Value, user.ActiveRole);
        if (!RolePermissions.Has(tenant.Roles, effectiveRole, _permission))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return await next(ctx);
    }
}
