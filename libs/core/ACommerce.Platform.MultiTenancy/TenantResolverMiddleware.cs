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
    /// <para><b>مَقاطِعُ أَوَّلُ المَسار الَّتي لَيسَت سلاجاً</b>.
    /// <c>internal</c> لا <c>private</c>: <c>SlugFromPath</c> يَقرَؤُها
    /// والاختِبارُ يَقرَأُ <c>SlugFromPath</c> — فَالقائِمَةُ
    /// مَقيسَةٌ لا مَظنونَة.</para>
    /// </summary>
    internal static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "_blazor", "_framework", "_content",
        "css", "js", "lib", "favicon.ico", "health", "realtime",
        // وَثائِق الزَحف عَلى الجَذر — لَيسَت slugs، فَلا داعِيَ
        // لِاستِعلام Marten فاشِل عِندَ كُلّ زِيارَة زاحِف.
        "robots.txt", "sitemap.xml",

        // ═══ «api» — سَبَبٌ بِنيَوِيّ لا تَفصيل ═════════════════════
        //
        // سَطحُ الـAPI مَساراتُه `/api/v1/…` — <b>بِلا مَقطَعِ سلاج
        // إطلاقاً، وذاكَ مَقصود</b>: المُستَأجِرُ يُشتَقّ مِن
        // الاعتِماد (وَثيقَةِ المِفتاح) ولا يُقبَل مِن الطَلَب
        // أَبَداً. وبِلا هذا السَطر يُحاوِلُ الوَسيطُ حَلَّ
        // مُستَأجِرٍ اسمُه `api` عِندَ <b>كُلّ</b> طَلَبِ API
        // فَيَفشَل — استِعلامُ Marten ضائِعٌ في كُلّ نِداء.
        //
        // <b>والتَغييرُ صِفريُّ الأَثَر على ما كانَ يَعمَل</b>، وهذا
        // مَقيسٌ لا مَظنون: مَسارات `/api/{slug}/manifest.json`
        // وإخوانُها كانَ الوَسيطُ يَقرَأُ مِنها المَقطَعَ الأَوَّل
        // `api` — لا سلاجَ المُستَأجِر — فَيَستَعلِم عَن مُستَأجِرٍ
        // بِهذا الاسم ولا يَجِدُه، فَلا يَضَع مُستَأجِراً. أَي أَنّ
        // تِلكَ النِقاطَ تَعمَلُ اليَومَ <b>بِلا مُستَأجِرٍ
        // مَحلول</b> وتَقرَأُ السلاجَ مِن وَسيطِ المَسار بِنَفسِها.
        // فَالحَجزُ يُبَدِّل «استِعلامٌ يَفشَل» بِـ«لا استِعلام»،
        // والنَتيجَةُ واحِدَة — مُثَبَّتَةً في
        // `TenantSlugResolutionTests`.
        "api",

        // ═══ «billing» — صَفحَتا عَودَةِ الدَفعِ وإلغائِه (‏ADR-006) ═══
        //
        // ‏`/billing/paypal/return` و`/billing/paypal/cancel` **صَفحَتا
        // قِراءَةٍ عامَّتان** يَبلُغُهُما الدافِعُ قادِماً مِن PayPal —
        // بِلا جَلسَةٍ عِندَنا ولا مُستَأجِرٍ في المَسار. وبِلا هذا
        // السَطرِ يَستَعلِم الوَسيطُ عَن مُستَأجِرٍ اسمُه «‏billing»
        // عِندَ كُلِّ عَودَةِ دافِعٍ فَيَفشَل — نَفسُ عِلَّةِ «‏api»
        // حَرفاً، ونَفسُ نَتيجَتِها: «استِعلامٌ يَفشَل» يَصير «لا
        // استِعلام».
        "billing",

        // ═══ صَفَحاتُ المَنَصَّةِ الخَمس — شَرطُ اعتِمادِ النِطاق ═══════
        //
        // ‏`/terms` و`/privacy` و`/refunds` و`/pricing` و`/contact` —
        // <b>صَفَحاتُ المَنَصَّةِ نَفسِها لا صَفَحاتُ مَتجَر</b>، فَلا
        // مَقطَعَ سلاجٍ فيها إطلاقاً. وبِلا هذا السَطرِ يَقرَأُ الوَسيطُ
        // المَقطَعَ الأَوَّلَ سلاجاً فَيَستَعلِمُ عَن مُستَأجِرٍ اسمُه
        // «‏terms» عِندَ كُلِّ زِيارَة — نَفسُ عِلَّةِ «‏api» و«‏billing»
        // حَرفاً، ونَفسُ نَتيجَتِها: «استِعلامٌ يَفشَل» يَصيرُ «لا
        // استِعلام».
        //
        // <b>والأَثَرُ عَلى ما كانَ يَعمَلُ صِفرٌ، وهذا مَقيسٌ لا
        // مَظنون</b>: طُلِبَتِ الخَمسُ عَلى الخادِمِ قَبلَ الحَجزِ
        // فَرَدَّت كُلُّها <b>الصَفحَةَ الاحتِياطِيَّةَ نَفسَها</b> الَّتي
        // يَرُدُّها مَسارٌ مُختَرَع (‏`/nonexistent-xyz`) — أَي أَنَّه لا
        // مُستَأجِرَ بِأَيٍّ مِن هذِه الأَسماء، والقيمَةُ المُشتَقَّةُ
        // واحِدَةٌ قَبلَ الحَجزِ وبَعدَه: <c>null</c>. مُثَبَّتٌ في
        // <c>TenantSlugResolutionTests</c>.
        "terms", "privacy", "refunds", "pricing", "contact",
    };

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
                          entity.TagLine, entity.City);

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
