using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// <para><b>قَرارُ «أَيُّ سلاجٍ في هذا المُضيف؟» — دالَّةٌ
    /// نَقِيَّةٌ أُختُ <see cref="SlugFromPath"/>، تُقاسُ بِلا
    /// مُضيفٍ ولا قاعِدَةِ بَيانات.</b> تُعيدُ المِلصَقَ الأَوَّلَ
    /// مِن <c>{slug}.{baseDomain}</c>، و<c>null</c> لِكُلِّ ما
    /// عَداه.</para>
    ///
    /// <para><b>وهذِه الدالَّةُ قُفلٌ لا تَجميل.</b> المُضيفُ نَصٌّ
    /// <b>يُرسِلُه العَميل</b> — فَلَولا اشتِراطُ اللاحِقَةِ لَصارَ
    /// رَأسُ <c>Host</c> باباً يَختارُ بِه أَيُّ أَحَدٍ سِياقَ أَيِّ
    /// مُستَأجِر. <b>وتَصحيحٌ لِما كانَ مَكتوباً هُنا</b>: قيلَ إنَّ
    /// المَنَصَّةَ تُشَغِّلُ <c>XForwardedHost</c> بِلا وُكَلاءَ
    /// مَوثوقين، وذلكَ صَحيحٌ حَتّى ‏2026-08-30 ولَيسَ صَحيحاً بَعدَها:
    /// الرَأسُ <b>لا يُقرَأُ إطلاقاً</b> بِلا قائِمَةٍ مُهَيَّأَة
    /// (‏<c>ForwardedHeadersPolicy</c> وADR-023). <b>والقُفلُ يَبقى
    /// لِأَنَّ سَبَبَه يَبقى</b>: بِلا وَسيطٍ بَينَ العَميلِ
    /// والخادِمِ يَصِلُ رَأسُ <c>Host</c> كَما كَتَبَه العَميل.
    /// والاشتِراطُ يَقَعُ على <b>«‏نُقطَة + النِطاق‏»</b>
    /// لا على النِطاقِ وَحدَه: <c>notexample.com</c>
    /// و<c>xexample.com</c> يَنتَهِيانِ بِـ<c>example.com</c>
    /// حَرفِيّاً، فَـ<c>EndsWith</c> الساذِجُ يَقبَلُهُما.</para>
    ///
    /// <para><b>وبِلا نِطاقٍ أَساسٍ مُهَيَّأٍ لا يُحَلُّ مُضيفٌ
    /// قَطّ</b> — وهذا هُوَ الوَضعُ الافتِراضيّ. فَالتَطويرُ على
    /// <c>localhost</c> والنَشرُ الحالِيُّ على
    /// <c>*.hf.space</c> يَبقَيانِ بِالمَسارِ وَحدَه،
    /// <b>والنَقلَةُ صِفريَّةُ الأَثَرِ حَتّى يُهَيَّأَ النِطاقُ
    /// عَمداً</b>.</para>
    /// </summary>
    /// <param name="host">قيمَةُ <c>Request.Host</c> — قَد تَحمِلُ
    /// مَنفَذاً ونُقطَةً أَخيرَةً وحُروفاً كَبيرَة.</param>
    /// <param name="baseDomain">النِطاقُ الأَساسُ مِن التَهيِئَة —
    /// <b>لا يُكتَبُ ثابِتاً في الكود</b>
    /// (<see cref="SubdomainTenancyOptions"/>).</param>
    public static string? SlugFromHost(string? host, string? baseDomain)
    {
        var apex = NormaliseHost(baseDomain);
        if (apex is null) return null;

        var name = NormaliseHost(host);
        if (name is null) return null;

        // الجَذرُ نَفسُه لَيسَ مُستَأجِراً.
        if (name.Length == apex.Length) return null;

        // **النُقطَةُ جُزءٌ مِن الشَرط** — وبِدونِها تَعبُرُ
        // `notexample.com` و`xexample.com`.
        if (name.Length < apex.Length + 2) return null;
        if (!name.EndsWith(apex, StringComparison.Ordinal)) return null;
        if (name[name.Length - apex.Length - 1] != '.') return null;

        var label = name[..(name.Length - apex.Length - 1)];

        // العُمقُ الثاني لَيسَ مُستَأجِراً: شَهادَةُ المُستَوى
        // الأَوَّلِ لا تُغَطّيه، وقُبولُه يَفتَحُ أَسماءً لا تُحصى
        // تَحتَ سلاجٍ واحِد.
        if (label.Contains('.')) return null;

        if (!IsHostLabel(label)) return null;

        return ReservedPaths.Contains(label) ? null : label;
    }

    /// <summary>
    /// <para><b>حَقنُ سلاجِ المُضيفِ في أَوَّلِ المَسار — قَرارٌ
    /// نَقِيٌّ يُقاسُ وَحدَه.</b> يُعيدُ المَسارَ الجَديد، أَو
    /// <c>null</c> حينَ <b>لا</b> يَجوزُ الحَقن.</para>
    ///
    /// <para><b>ولِماذا حَقنٌ لا إعادَةُ كِتابَةِ جَدوَلِ
    /// المَسارات</b>: ‏<c>RouteValues["slug"]</c> و
    /// <c>opts.TenantId.IsRouteArgumentNamed("slug")</c> —
    /// <b>وعَلَيهِ يَقومُ عَزلُ صُفوفِ Marten كُلُّه</b> —
    /// و<c>AuthSession.ExtractRoleFromPath</c> وصَفَحاتُ Razor
    /// وقَوالِبُ المَسارات: كُلُّها تَقرَأُ السلاجَ <b>مَقطَعَ
    /// مَسارٍ أَوَّل</b>. فَالحَقنُ يُبقيها عامِلَةً بِلا حَرفٍ
    /// واحِد، ويَحصُرُ عَمَلَ المُضيفِ في المَدخَل.</para>
    ///
    /// <para><b>وثَلاثَةُ امتِناعاتٍ كُلُّها مَقيسَة</b>:</para>
    /// <list type="number">
    ///   <item><b>مَقطَعٌ مَحجوز</b> — لَولاه لَصارَت
    ///   <c>/css/site.css</c> و<c>/_framework/blazor.web.js</c>
    ///   و<c>/uploads/x.png</c> تَحتَ سلاجٍ، <b>فَتَسقُطُ
    ///   الأَنماطُ والإطارُ والصُوَرُ مَعاً</b> تَحتَ كُلِّ نِطاقٍ
    ///   فَرعِيّ.</item>
    ///   <item><b>مَسارٌ يَحمِلُ السلاجَ سَلَفاً</b> — الرَوابِطُ
    ///   المُطلَقَةُ اليَومَ <c>/{slug}/…</c> بِالمِئات، ولَولا
    ///   هذا الامتِناعِ لَصارَت <c>/ashare/ashare/listings</c>.
    ///   <b>وهذا بِعَينِه ما يَجعَلُ المَسارَ والمُضيفَ يَعمَلانِ
    ///   مَعاً لا بَدَلاً</b> — فَلا رابِطَ واحِدٌ يَحتاجُ
    ///   تَرحيلاً قَبلَ تَشغيلِ النِطاق.</item>
    ///   <item><b>لا سلاجَ مُضيفٍ</b> — الحالَةُ
    ///   الافتِراضِيَّة.</item>
    /// </list>
    /// </summary>
    public static string? PathWithSlug(string? path, string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        path ??= "/";
        if (path.Length == 0 || path[0] != '/') path = "/" + path;

        var firstSlash = path.IndexOf('/', 1);
        var first = firstSlash > 0 ? path[1..firstSlash] : path[1..];

        if (first.Length > 0)
        {
            if (ReservedPaths.Contains(first)) return null;
            if (string.Equals(first, slug, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return path == "/" ? "/" + slug : "/" + slug + path;
    }

    /// <summary>يُطَبِّعُ مُضيفاً: حُروفٌ صَغيرَة، بِلا مَنفَذٍ ولا
    /// نُقطَةٍ أَخيرَة. و<c>null</c> لِلفارِغِ ولِعَنوانِ ‏IPv6
    /// الحَرفِيّ — <b>ولا تَعَدُّدَ مُستَأجِرينَ على عَنوانٍ
    /// رَقَمِيّ</b>.</summary>
    private static string? NormaliseHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var s = value.Trim();
        if (s.StartsWith('[')) return null;            // ‏[::1]:5050

        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];

        s = s.Trim().Trim('.').ToLowerInvariant();
        return s.Length == 0 ? null : s;
    }

    /// <summary><b>‏RFC 1123</b>: مِن ١ إلى ٦٣ مِحرَفاً، حُروفٌ
    /// وأَرقامٌ وشَرطَة، <b>ولا شَرطَةَ طَرَفاً</b>. والشَرطَةُ
    /// السُفلِيَّةُ لَيسَت مِحرَفَ مُضيفٍ صالِحاً — <b>وفاحِصُ
    /// شَكلِ السلاجِ عِندَ الإنشاءِ يَقبَلُها اليَوم</b>، فَالحَدُّ
    /// هُنا يَحرُسُ الحَلَّ ولَو أُنشِئَ الاسمُ سَلَفاً.</summary>
    private static bool IsHostLabel(string label)
    {
        if (label.Length is 0 or > 63) return false;
        if (label[0] == '-' || label[^1] == '-') return false;

        foreach (var c in label)
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '-'))
                return false;

        return true;
    }

    private readonly SubdomainTenancyOptions _subdomains;

    public TenantResolverMiddleware(RequestDelegate next, IDocumentStore store,
                                    IMemoryCache cache, SubdomainTenancyOptions subdomains)
    {
        _next = next; _store = store; _cache = cache; _subdomains = subdomains;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // **المُضيفُ أَوَّلاً ثُمَّ المَسار — والمَسارُ لا يُحذَف.**
        // وتَحتَ نِطاقٍ فَرعِيٍّ يَكونُ الجَوابانِ واحِداً أَصلاً،
        // لِأَنَّ `SubdomainTenantPathMiddleware` حَقَنَ المَقطَعَ
        // قَبلَ التَوجيه. فَالسُقوطُ هُنا يَحرُسُ التَركيبَ الَّذي
        // لا يُرَكِّبُ ذاكَ الوَسيط.
        var slug = SlugFromHost(ctx.Request.Host.Value, _subdomains.BaseDomain)
                   ?? SlugFromPath(ctx.Request.Path.Value);
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

/// <summary>
/// <para><b>النِطاقُ الأَساسُ الَّذي تَحتَه يَسكُنُ المُستَأجِرون —
/// مِن التَهيِئَةِ لا مِن الكود.</b> فارِغاً (وهُوَ الافتِراضيّ)
/// <b>لا يُحَلُّ مُضيفٌ قَطّ</b>، ويَبقى الحَلُّ بِالمَسارِ
/// وَحدَه.</para>
///
/// <para><b>ولِماذا لا يُكتَبُ ثابِتاً</b>: النِطاقُ المَملوكُ
/// <b>غَيرُ مُثبَتٍ في المُستَودَعِ أَصلاً</b> — <c>wasayel.app</c>
/// يَرِدُ في تَعليقٍ فيه «مَثَلاً» وفي ثابِتِ اختِبار،
/// و<c>Wasayel.SA</c> يُناقِضُه. فَكِتابَةُ أَحَدِهِما في الكودِ
/// اختِيارٌ مُنتَجِيٌّ يُتَّخَذُ صامِتاً (القاعِدَة ٥)، وقِراءَتُه
/// مِن التَهيِئَةِ تُبقيه سُؤالاً مَفتوحاً لِصاحِبِ
/// المَشروع.</para>
/// </summary>
public sealed class SubdomainTenancyOptions
{
    /// <summary>مِفتاحُ التَهيِئَة. ومُتَغَيِّرُ البيئَةِ
    /// <c>MultiTenancy__BaseDomain</c> يَملَؤُه كَذلِك.</summary>
    public const string ConfigurationKey = "MultiTenancy:BaseDomain";

    /// <summary>مُتَغَيِّرُ بيئَةٍ مُسَطَّحٌ — <b>لِمُستَضيفٍ لا
    /// يَقبَلُ الشَرطَتَينِ السُفلِيَّتَين</b> في أَسماءِ
    /// الأَسرار.</summary>
    public const string EnvironmentVariable = "ACOMMERCE_BASE_DOMAIN";

    /// <summary><c>null</c> = لا نِطاقَ مُهَيَّأً = لا حَلَّ
    /// بِالمُضيف.</summary>
    public string? BaseDomain { get; init; }

    public static SubdomainTenancyOptions FromConfiguration(IConfiguration? config)
    {
        var value = config?[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(value))
            value = config?[EnvironmentVariable]
                    ?? Environment.GetEnvironmentVariable(EnvironmentVariable);

        return new SubdomainTenancyOptions
        {
            BaseDomain = string.IsNullOrWhiteSpace(value) ? null : value.Trim()
        };
    }
}

/// <summary>
/// <para><b>يَحقِنُ سلاجَ المُضيفِ في أَوَّلِ المَسار — قَبلَ
/// التَوجيهِ وقَبلَ المَلَفّاتِ الساكِنَة.</b> القَرارُ كُلُّه في
/// <see cref="TenantResolverMiddleware.PathWithSlug"/>، وهذا
/// الصِنفُ يُنَفِّذُه ولا يُقَرِّر.</para>
///
/// <para><b>ولِماذا هُنا لا في <c>TenantResolverMiddleware</c></b>:
/// ذاكَ يُرَكَّبُ <b>بَعدَ</b> <c>UseRouting()</c>، والتَوجيهُ
/// يَكونُ قَد طابَقَ نُقطَتَه سَلَفاً — فَإعادَةُ كِتابَةِ
/// المَسارِ هُناكَ تَصِلُ مُتَأَخِّرَةً بِنُقطَةٍ واحِدَةٍ
/// وتُصَيِّرُ ‏404. والحَقنُ قَبلَ الأُنبوبِ كُلِّه يَجعَلُ
/// الطَلَبَ يَصِلُ بِالشَكلِ الَّذي يَتَوَقَّعُه كُلُّ ما بُنِيَ
/// حَتّى اليَوم.</para>
///
/// <para><b>والأَصلُ يُحفَظُ</b> في <see cref="OriginalPathItem"/>
/// و<see cref="HostSlugItem"/>: بِناءُ الرَوابِطِ المُطلَقَةِ
/// يَحتاجُ يَوماً أَن يَعرِفَ أَنَّ الطَلَبَ جاءَ بِمُضيفٍ —
/// <b>وحَذفُ المَعلومَةِ هُنا يَجعَلُ استِرجاعَها
/// مُستَحيلاً</b>.</para>
/// </summary>
public sealed class SubdomainTenantPathMiddleware
{
    public const string HostSlugItem = "acommerce.tenant.host-slug";
    public const string OriginalPathItem = "acommerce.tenant.original-path";

    private readonly RequestDelegate _next;
    private readonly SubdomainTenancyOptions _options;

    public SubdomainTenantPathMiddleware(RequestDelegate next, SubdomainTenancyOptions options)
    {
        _next = next; _options = options;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var slug = TenantResolverMiddleware.SlugFromHost(
            ctx.Request.Host.Value, _options.BaseDomain);

        if (slug is not null)
        {
            ctx.Items[HostSlugItem] = slug;
            ctx.Items[OriginalPathItem] = ctx.Request.Path.Value;

            var rewritten = TenantResolverMiddleware.PathWithSlug(ctx.Request.Path.Value, slug);
            if (rewritten is not null) ctx.Request.Path = rewritten;
        }

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
        services.AddSingleton(sp => SubdomainTenancyOptions.FromConfiguration(
            sp.GetService<IConfiguration>()));
        return services;
    }

    public static IApplicationBuilder UsePlatformMultiTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolverMiddleware>();

    /// <summary><b>يُرَكَّبُ في أَوَّلِ الأُنبوب</b> — قَبلَ
    /// <c>UseStaticFiles</c> و<c>UseRouting</c>. راجِع
    /// <see cref="SubdomainTenantPathMiddleware"/>.</summary>
    public static IApplicationBuilder UseSubdomainTenantPath(this IApplicationBuilder app)
        => app.UseMiddleware<SubdomainTenantPathMiddleware>();
}
