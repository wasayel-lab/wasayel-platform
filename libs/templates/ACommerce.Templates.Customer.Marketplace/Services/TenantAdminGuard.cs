using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// بَوّابَة إدارَة المَتجَر — <b>التَّعريف الوَحيد</b> لِقَرار «يَجوز لَه
/// إدارَة هذا المَتجَر» في المُستَودَع. تَقرَؤُه نِقاط الـ POST تَحت
/// <c>/admin/tenants/{slug}/*</c> وَصَفَحات Razor المُقابِلَة مَعاً، فَلا
/// يَنفَرِد طَرَف القِراءَة بِتَعريف ثانٍ يَنجَرِف عَن طَرَف الكِتابَة.
///
/// <para><b>لِماذا وُجِدَت:</b> كانَ القَرار دالَّةً مَحَلِّيَّة داخِل
/// <c>MarketplaceTemplateExtensions.MapCustomerMarketplaceTemplate</c> لا
/// تَراها الصَفَحات أَصلاً، فَحُرِسَت الكِتابَة وَبَقِيَت القِراءَة
/// مَفتوحَة: طَلَب <c>curl</c> مَجهول بِلا أَيّ cookie كانَ يَستَخرِج مِن
/// ثَماني صَفَحات لِكُلّ مَتجَر اسمَه وَكاتالوج أَدوارِه بِصَلاحِيّاتِها
/// وَفِئاتِه وَمُدُنَه وَخَصائِصَه وَهُوِيَّتَه البَصَرِيَّة وَقائِمَة
/// مُستَخدِميه بِهَواتِفِهم وَأَرقام هُوِيّاتِهم.</para>
///
/// <para><b>القَرار لَم يُمَسّ بِحَرف عِندَ النَّقل</b> — هُوَ ذاتُه:
/// (أ) مالِك المَتجَر مُسَجَّل دُخولاً عَبر Studio، أَو (ب) مُستَخدِم
/// مُسَجَّل دُخولاً في نَفس المَتجَر دَورُه الفَعّال يَحمِل
/// <c>tenant.manage</c>. وَما عَدا ذلكَ رَفض.</para>
/// </summary>
public sealed class TenantAdminGuard
{
    private readonly IDocumentStore _store;
    private readonly StudioAuth _studioAuth;
    private readonly IHttpContextAccessor _http;

    // ذاكِرَة القَرار لِلطَّلَب الواحِد. صَفحَة الإدارَة تَسأَل مَرَّتَين
    // (مَرَّة في @code لِتَمنَع تَحميل البَيانات، وَمَرَّة في
    // RequireTenantAdmin لِتَمنَع التَّصيير) — فَنُوَفِّر الاستِعلام
    // الثاني بِلا تَغيير القَرار.
    //
    // المِفتاح هُوَ الـ HttpContext نَفسُه لا مُجَرَّد الـ slug: لَو حُقِنَت
    // الخِدمَة يَوماً في circuit تَفاعُليّ (‏InteractiveServer) فَإنّ
    // HttpContext يُصبِح null بَعد أَوَّل تَصيير — فَنَرفُض عِندَئِذٍ بَدَل
    // أَن نُسَلِّم قَراراً قَديماً. الصَفَحات المَحروسَة كُلُّها SSR ثابِت.
    private HttpContext? _cachedContext;
    private readonly Dictionary<string, bool> _cache = new(StringComparer.Ordinal);

    public TenantAdminGuard(IDocumentStore store, StudioAuth studioAuth, IHttpContextAccessor http)
    {
        _store = store;
        _studioAuth = studioAuth;
        _http = http;
    }

    /// <summary>نُسخَة الحَقن — تَقرَأ الطَّلَب الحاليّ مِن
    /// <see cref="IHttpContextAccessor"/>. تَستَخدِمها صَفَحات Razor.
    /// بِلا طَلَب حاليّ → رَفض (الافتِراض الآمِن).</summary>
    public async Task<bool> CanAdministerAsync(string slug)
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return false;

        if (!ReferenceEquals(ctx, _cachedContext))
        {
            _cachedContext = ctx;
            _cache.Clear();
        }
        if (_cache.TryGetValue(slug, out var cached)) return cached;

        var allowed = await CanAdministerAsync(_store, _studioAuth, ctx.Request, slug);
        _cache[slug] = allowed;
        return allowed;
    }

    /// <summary>القَرار نَفسُه، صَريح الطَّلَب — تَستَخدِمه نِقاط الـ POST
    /// الَّتي تَستَلِم <c>HttpRequest</c> مِن minimal API مُباشَرَةً.
    ///
    /// <para>يَقبَل: (أ) مالِك المَتجَر مِن Studio (‏auth cookie لِلـ studio)،
    /// (ب) مُستَخدِم مُسَجَّل دُخولُه بِدَور لَه <c>tenant.manage</c>.
    /// يَرفُض كُلّ شَيء آخَر.</para></summary>
    public static async Task<bool> CanAdministerAsync(
        IDocumentStore docStore, StudioAuth studioAuth, HttpRequest req, string slug)
    {
        await using var globalQs = docStore.QuerySession();
        var tenant = await globalQs.LoadAsync<Tenant>(slug);
        if (tenant is null) return false;

        // (أ) مالِك المَتجَر مُسَجَّل دُخولاً عَبر Studio.
        studioAuth.Load();
        if (IsStudioOwner(tenant, studioAuth.UserId)) return true;

        // (ب) مُستَخدِم مُسَجَّل دُخولاً في نَفس المَتجَر بِدَور إداريّ.
        var token = AuthSession.ResolveToken(req, slug);
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null) return false;
        var (userId, tenantSlug, _) = parsed.Value;
        if (tenantSlug != slug) return false;

        await using var tenantQs = docStore.QuerySession(slug);
        var me = await tenantQs.LoadAsync<User>(userId);
        return HasTenantManage(tenant, me);
    }

    // ─── نِصفا القَرار، نَقِيَّان بِلا I/O — عَلَيهِما تَقَع الوَحَدات ──────

    /// <summary>(أ) هَل صاحِب جَلسَة الـ studio هُوَ مالِك هذا المَتجَر؟
    /// <c>null</c> = لا جَلسَة studio → لا.</summary>
    public static bool IsStudioOwner(Tenant tenant, Guid? studioUserId)
        => studioUserId.HasValue && tenant.OwnerUserId == studioUserId.Value;

    /// <summary>(ب) هَل هذا المُستَخدِم — داخِل نَفس المَتجَر — دَورُه
    /// الفَعّال يَحمِل <c>tenant.manage</c>؟ <c>null</c> = لا مُستَخدِم
    /// بِذلكَ المُعَرِّف في المَتجَر → لا.</summary>
    public static bool HasTenantManage(Tenant tenant, User? user)
        => user is not null
        && RolePermissions.Has(tenant.Roles, user.ActiveRole, "tenant.manage");
}
