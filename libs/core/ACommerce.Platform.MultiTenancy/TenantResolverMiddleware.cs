using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Platform.MultiTenancy;

/// <summary>
/// يَستَخرِج tenant slug من أَوّل segment في الـ URL ويُحَمِّل
/// <see cref="Tenant"/> من Marten. لو وُجِد، يَملَأ
/// <see cref="TenantContext"/> الـ scoped الذي تَستَهلِكه باقي
/// الطَبَقات.
///
/// مثال:
///   GET /ashare/listings  →  tenant.Slug = "ashare"
///   GET /admin/...         →  bypassed (admin paths)
///   GET /                  →  no tenant (landing page)
/// </summary>
public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDocumentStore _store;
    private readonly IMemoryCache _cache;
    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "_blazor", "_framework", "_content",
        "css", "js", "lib", "favicon.ico", "health"
    };

    public TenantResolverMiddleware(RequestDelegate next, IDocumentStore store, IMemoryCache cache)
    {
        _next = next; _store = store; _cache = cache;
    }

    public async Task InvokeAsync(HttpContext ctx, TenantContext tenant)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (path == "/" || path.Length < 2) { await _next(ctx); return; }

        var firstSlash = path.IndexOf('/', 1);
        var slug = firstSlash > 0 ? path[1..firstSlash] : path[1..];

        if (string.IsNullOrEmpty(slug) || ReservedPaths.Contains(slug))
        {
            await _next(ctx); return;
        }

        var cacheKey = $"tenant:{slug.ToLowerInvariant()}";
        if (!_cache.TryGetValue(cacheKey, out Tenant? entity))
        {
            await using var session = _store.QuerySession();
            entity = await session.LoadAsync<Tenant>(slug.ToLowerInvariant());
            if (entity is not null)
                _cache.Set(cacheKey, entity, TimeSpan.FromMinutes(5));
        }

        if (entity is not null)
        {
            tenant.Resolve(entity.Slug, entity.Name, entity.BrandColor);
            ctx.Items["TenantSlug"] = entity.Slug;
        }

        await _next(ctx);
    }
}

public static class MultiTenancyExtensions
{
    public static IServiceCollection AddPlatformMultiTenancy(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        return services;
    }

    public static IApplicationBuilder UsePlatformMultiTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolverMiddleware>();
}
