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
    /// <summary>
    /// <para><b>مَقاطِعُ أَوَّلُ المَسار الَّتي لَيسَت سلاجاً</b> —
    /// <b>ولَيسَت قائِمَةً هُنا</b>: هي
    /// <see cref="ReservedTenantSlugs.All"/>، مَصدَرٌ واحِدٌ يَقرَؤُه
    /// هذا الوَسيطُ ومُنشِئُ المَتاجِرِ وخَريطَةُ المَوقِعِ مَعاً.</para>
    ///
    /// <para><b>والعِلَّةُ الَّتي أَخرَجَتها</b>: كانَت
    /// <c>internal</c> بِمُستَهلِكٍ واحِد، فَكانَ المُنشِئُ يَقبَلُ
    /// سلاجاً مَحجوزاً ثُمَّ لا يُحَلُّ أَبَداً — <b>مَتجَرٌ يُبنى
    /// ولا يُبلَغ</b>. التَفصيلُ الكامِلُ وأَسبابُ كُلِّ اسمٍ في
    /// <see cref="ReservedTenantSlugs"/>.</para>
    /// </summary>
    internal static IReadOnlySet<string> ReservedPaths => ReservedTenantSlugs.All;

    /// <summary>
    /// <para><b>قَرارُ «أَيُّ سلاجٍ في هذا المَسار؟» — دالَّةٌ
    /// نَقِيَّة.</b> تُعيد <c>null</c> لِمَسارٍ لا سلاجَ فيه: الجَذر،
    /// والقَصير، والمَحجوز. وهذا هُوَ كُلُّ ما كانَ مَكتوباً في
    /// أَوَّل أَربَعَةِ أَسطُرٍ مِن <see cref="InvokeAsync"/>،
    /// مَنقولاً بِلا تَغييرِ حَرف — <b>لِيُختَبَر بِلا مُضيفٍ ولا
    /// قاعِدَةِ بَيانات</b> (القاعِدَة ٢: الحَدُّ الَّذي لا يُقاس
    /// آلِيّاً يَنهار).</para>
    /// </summary>
    public static string? SlugFromPath(string? path)
    {
        path ??= "/";
        if (path == "/" || path.Length < 2) return null;

        var firstSlash = path.IndexOf('/', 1);
        var slug = firstSlash > 0 ? path[1..firstSlash] : path[1..];

        return string.IsNullOrEmpty(slug) || ReservedPaths.Contains(slug) ? null : slug;
    }

    public TenantResolverMiddleware(RequestDelegate next, IDocumentStore store, IMemoryCache cache)
    {
        _next = next; _store = store; _cache = cache;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var slug = SlugFromPath(ctx.Request.Path.Value);
        if (slug is null) { await _next(ctx); return; }

        var cacheKey = $"tenant:{slug.ToLowerInvariant()}";
        if (!_cache.TryGetValue(cacheKey, out Tenant? entity))
        {
            await using var session = _store.QuerySession();
            entity = await session.LoadAsync<Tenant>(slug.ToLowerInvariant());
            if (entity is not null)
                _cache.Set(cacheKey, entity, TimeSpan.FromMinutes(5));
        }

        if (entity is not null)
            ctx.SetTenant(entity.Slug, entity.Name, entity.BrandColor, entity.AuthChannel,
                          entity.TagLine, entity.City,
                          hasRoles: entity.Roles.Count > 0);

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
