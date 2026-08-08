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
        var role  = http.Role();
        var userId = http.UserIdOrNull();
        if (userId is null) return Results.Unauthorized();

        await using var g = store.QuerySession();
        var tenant = await g.LoadAsync<Tenant>(slug);
        if (tenant is null) return Results.NotFound();
        if (tenant.Roles.Count == 0) return await next(ctx);   // legacy mode

        await using var t = store.QuerySession(slug);
        var user = await t.LoadAsync<User>(userId.Value);
        if (user is null) return Results.Forbid();

        // الدَور الفَعّال: URL يَتَفَوَّق عَلى user.ActiveRole (لِجَلَسات
        // مُتَعَدِّدَة في نَفس المُتَصَفِّح).
        var activeRole = role ?? user.ActiveRole;
        if (!RolePermissions.Has(tenant.Roles, activeRole, _permission))
            return Results.Forbid();

        return await next(ctx);
    }
}
