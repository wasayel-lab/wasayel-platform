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
/// <see cref="Tenant"/> من Marten. عند النَجاح يَضَع المُعَرِّفات
/// في <see cref="HttpContext.Items"/> فتَكون مَرئيّة لِكُلّ scopes الطَلَب
/// (بما فيها nested scopes التي يُنشِئها Wolverine).
/// </summary>
public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDocumentStore _store;
    private readonly IMemoryCache _cache;
    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "_blazor", "_framework", "_content",
        "css", "js", "lib", "favicon.ico", "health", "realtime"
    };

    public TenantResolverMiddleware(RequestDelegate next, IDocumentStore store, IMemoryCache cache)
    {
        _next = next; _store = store; _cache = cache;
    }

    public async Task InvokeAsync(HttpContext ctx)
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
            ctx.SetTenant(entity.Slug, entity.Name, entity.BrandColor);

        await _next(ctx);
    }
}

public static class MultiTenancyExtensions
{
    public static IServiceCollection AddPlatformMultiTenancy(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpItemTenantContext>();
        return services;
    }

    public static IApplicationBuilder UsePlatformMultiTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolverMiddleware>();
}
