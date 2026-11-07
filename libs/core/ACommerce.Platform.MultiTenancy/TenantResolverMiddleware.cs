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
            if (ServedAtRoot(first)) return null;
            if (string.Equals(first, slug, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return path == "/" ? "/" + slug : "/" + slug + path;
    }

    /// <summary><b>ما يُخدَمُ مِن الجَذرِ نَفسِه</b> (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·5 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): جُذورُ البِنيَةِ التَحتِيَّة
    /// ومَناطِقُ المَنَصَّةِ وسَطحُ الـAPI. <b>وكانَ الامتِناعُ أَعلاه عَلى المَحجوزِ كُلِّه</b> — فَأَسماءُ مِحوَرِ المُضيفِ (<c>support</c>، <c>help</c>،
    /// <c>app</c>…) لا تُحقَنُ وهي لَيسَت جُذوراً مَخدومَة: <c>ashare.R/support</c> كانَ <c>/support</c> ← 404، وصَفحَةُ <c>/{slug}/support</c> قائِمَة.
    /// والمَحجوزُ يَبقى مَحجوزاً اسمَ مَتجَر؛ وما يُخدَمُ مِن الجَذرِ هُوَ ما لا يُحقَن.</summary>
    /// <remarks>‏(‏2026-11-07، W13-4·C2b·1) الجِسمُ في <see cref="ReservedTenantSlugs.ServedAtRoot"/> بِحَرفِه — يَسأَلُه بانِي الرابِطِ النَظيفِ أَيضاً.</remarks>
    internal static bool ServedAtRoot(string first) => ReservedTenantSlugs.ServedAtRoot(first);

    /// <summary><b>مُعَرِّفُ المَتجَرِ مِن المُضيف</b> (‏2026-11-07، ADR-050 §٣): مِلصَقُ <see cref="SlugFromHost"/> إن صَلَحَ اسمَ مُضيفِ مَتجَرٍ
    /// (<see cref="StoreHostName"/>) — فَمَتجَرٌ قائِمٌ لا يَجتازُه لا مُضيفَ لَه ويَبقى بِمَسارِه، ومِلصَقٌ يَنتَهي بِاسمِ مُضيفِ دَورٍ لا يُقرَأُ مَتجَراً.</summary>
    public static string? StoreSlugFromHost(string? host, string? baseDomain)
        => SlugFromHost(host, baseDomain) is { } label && StoreHostName.IsValid(label) ? label : null;

    /// <summary>
    /// <para><b>مُضيفُ الدَور — <c>{slug}-{rolehost}.R</c> ← (المَتجَر، دَورُ الكَتالوج)</b> (‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4). قَرارٌ
    /// نَقِيّ: المِلصَقُ يَنتَهي بِـ<c>-{قيمَةٍ مِن RoleHostNames}</c> وما قَبلَها اسمُ مُضيفِ مَتجَرٍ صالِح (<see cref="StoreHostName"/>). <b>ولا لَبس</b>: اسمُ
    /// مَتجَرٍ صالِحٌ لا يَنتَهي بِاسمِ دَور (رَمزُ <c>host_name_role_suffix</c>)، فَالمِلصَقُ إمّا مَتجَرٌ وإمّا دَورُ مَتجَر. ووُجودُ الدَورِ في المَتجَرِ حالَةٌ
    /// مَخزونَة — يَسأَلُها <see cref="TenantResolverMiddleware"/> لا هذِه (القاعِدَة ٧).</para>
    /// </summary>
    public static (string Slug, string CatalogRole)? RoleFromHost(string? host, string? baseDomain)
    {
        if (SlugFromHost(host, baseDomain) is not { } label || StoreHostName.IsValid(label)) return null;
        foreach (var value in ACommerce.Kit.Roles.RoleHostNames.Values)
        {
            if (label.Length <= value.Length + 1 || !label.EndsWith("-" + value, StringComparison.Ordinal)) continue;
            var store = label[..^(value.Length + 1)];
            if (StoreHostName.IsValid(store) && ACommerce.Kit.Roles.RoleHostNames.CatalogSlugOf(value) is { } catalog)
                return (store, catalog);
        }
        return null;
    }

    /// <summary><b>أَيَحمِلُ المَتجَرُ دَورَ الكَتالوجِ هذا بِاسمِه؟</b> — نُسخَةُ دَورٍ <c>CatalogSlug</c> و<c>Slug</c> كِلاهُما اسمُه (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ
    /// الثانِيَة · SEC-C2b-R2-2 — جِسمُ شَرطِ الـ404 لِمُضيفِ الدَورِ بِحَرفِه، W13-4·C2b·4). فَدَورٌ مُؤَلَّفٌ بِاسمٍ آخَرَ لا مُضيفَ لَه.</summary>
    public static bool HoldsRoleHost(Tenant tenant, string catalogRole)
        => tenant.Roles.Any(r => string.Equals(r.CatalogSlug, catalogRole, StringComparison.Ordinal)
                                 && string.Equals(r.Slug, catalogRole, StringComparison.Ordinal));

    /// <summary><b>أَدوارُ المَتجَرِ الَّتي لَها مُضيف</b> — مِن <see cref="ACommerce.Kit.Roles.RoleHostNames"/> ما يَحمِلُه المَتجَرُ (<see cref="HoldsRoleHost"/>)، مُرَتَّبَةً
    /// تَرتيباً ثابِتاً (‏2026-11-07، SEC-C2b-R2-2). يَكتُبُها الوَسيطُ في <see cref="StoreHostItems.HostedRoles"/>.</summary>
    public static IReadOnlyList<string> HostedRoles(Tenant tenant)
        => ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug.Keys.Where(c => HoldsRoleHost(tenant, c)).Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// <para><b>ما يُفعَلُ بِطَلَبٍ عَلى مُضيفِه — قَرارٌ نَقِيٌّ يَقرَؤُه الحاقِن</b> (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·5). لا يَقرَأُ حالَةً مَخزونَة
    /// (القاعِدَة ٧): وُجودُ المَتجَرِ يَسأَلُه <see cref="TenantResolverMiddleware"/> بَعدَ التَوجيه.</para>
    /// <list type="bullet">
    ///   <item>نِطاقٌ فارِغ، أَو الجَذرُ <c>R</c>، أَو مُضيفٌ خارِجَه، أَو مِلصَقٌ مَحجوزٌ (<c>www</c>، <c>mail</c>…) ← يَمُرُّ بِلا مَتجَرٍ كَما اليَوم.</item>
    ///   <item>مِلصَقٌ تَحتَ <c>R</c> لَيسَ اسمَ مُضيفِ مَتجَر (عُمقٌ ثانٍ، مِلصَقٌ باطِل، مُضيفُ دَورٍ في المَرحَلَةِ الأُولى) ← 404.</item>
    ///   <item>عَلى <c>{slug}.R</c>: جُذورُ البِنيَةِ التَحتِيَّةِ كَما هي؛ مَناطِقُ المَنَصَّةِ ← 301 إلى <c>R</c> لِـGET/HEAD و404 لِما سِواهُما؛ <c>/api/{slug}/…</c>
    ///   و<c>/api/push/…</c> كَما هُما و<c>/api/{غَيرِه}/…</c> ← 404؛ مَسارٌ يَحمِلُ المَتجَرَ كَما هُوَ؛ وما سِواه يُحقَنُ فيه <c>/{slug}</c>.</item>
    /// </list>
    /// <para>‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>«<c>/api/push/…</c> كَما هو» صارَ
    /// <c>GET</c>/<c>HEAD</c> <c>/api/push/vapid-key</c> وَحدَه</b>، و<c>/api/{slug}/…</c> لِمَتجَرِ المُضيفِ ما لَم يَكُن اسمُه في
    /// <see cref="ReservedTenantSlugs.PlatformApiSegments"/> — وما سِواهُما 404.</para>
    /// </summary>
    public static StoreHostAnswer AnswerStoreHost(string? host, string? baseDomain, string? method, string? path, string? query, string? scheme)
        => AnswerStoreHost(host, baseDomain, method, path, query, scheme, rolePages: null);

    /// <summary>
    /// <para>‏(‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) <b>ومُضيفُ الدَور</b> (<see cref="RoleFromHost"/>)
    /// يُجابُ كَمُضيفِ مَتجَرِه بِقَواعِدِه كُلِّها (مَناطِقُ المَنَصَّة، وسَطحُ الـAPI، وجُذورُ البِنيَةِ التَحتِيَّة)، ودَورُه في <see cref="StoreHostAnswer.Role"/> —
    /// <b>ويوضَعُ <c>/r/{role}</c> في المَسارِ لِـGET/HEAD الَّذي يُطابِقُ قالِبَ صَفحَةِ دَورٍ وَحدَه</b> (<paramref name="rolePages"/>): <c>/me/listings</c> ←
    /// <c>/{slug}/r/{role}/me/listings</c>، و<c>/{slug}/me</c> ← <c>/{slug}/r/{role}/me</c>. وما سِواه — كُلُّ كِتابَة، وAPI، و<c>/me/data/download</c>،
    /// ومَسارٌ يَحمِلُ <c>/r/…</c> سَلَفاً — يُحقَنُ فيه <c>/{slug}</c> وَحدَه: فَالدَعوى لا تَبلُغُ تَخويلَ كِتابَةٍ أَبَداً (G7). وبِلا قَوالِبَ لا يُحقَنُ دَور.</para>
    /// </summary>
    public static StoreHostAnswer AnswerStoreHost(string? host, string? baseDomain, string? method, string? path, string? query, string? scheme,
        RolePageTemplates? rolePages)
    {
        var apex = NormaliseHost(baseDomain);
        var name = NormaliseHost(host);
        if (apex is null || name is null || name.Length == apex.Length) return StoreHostAnswer.Through;
        if (name.Length < apex.Length + 2 || !name.EndsWith(apex, StringComparison.Ordinal) || name[name.Length - apex.Length - 1] != '.')
            return StoreHostAnswer.Through;

        var label = name[..(name.Length - apex.Length - 1)];
        if (!label.Contains('.') && IsHostLabel(label) && ReservedPaths.Contains(label)) return StoreHostAnswer.Through;

        var slug = StoreSlugFromHost(host, baseDomain);
        string? role = null;
        if (slug is null)
        {
            if (RoleFromHost(host, baseDomain) is not { } roleHost) return StoreHostAnswer.NotFound;
            (slug, role) = roleHost;
        }
        return WithRole(AnswerOnStoreHost(slug, role, host, apex, method, path, query, scheme, rolePages), role);
    }

    private static StoreHostAnswer WithRole(StoreHostAnswer answer, string? role)
        => role is null || answer.Slug is null ? answer : answer with { Role = role };

    private static StoreHostAnswer AnswerOnStoreHost(string slug, string? role, string? host, string apex, string? method, string? path, string? query,
        string? scheme, RolePageTemplates? rolePages)
    {
        path = string.IsNullOrEmpty(path) ? "/" : path[0] == '/' ? path : "/" + path;
        var segments = path.Split('/', 4);
        var first = segments.Length > 1 ? segments[1] : "";

        if (ReservedTenantSlugs.PlatformAreas.Contains(first))
        {
            // ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · C2a-P-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) إدارَةُ مَتجَرِ المُضيفِ نَفسِه تُخدَمُ عَلى مُضيفِه كَما هي
            // (GET وكِتابَة): بِطاقاتُ `/manage` السِتُّ تَقودُ إلَيها، و`TenantAdminGuard` يَقبَلُ كوكيَّ المَتجَرِ وهو مَقصورٌ عَلى مُضيفِه — فَالتَحويلُ إلى
            // الجَذرِ كانَ يُسقِطُ الجَلسَةَ «غَير مُصَرَّح». ومَسارُ الإدارَةِ يَحمِلُ مَتجَرَ المُضيفِ نَفسَه، فَلا سِياقَ يَختَلِف؛ وما سِواه يَبقى 301/404.
            if (IsHostStoreAdmin(path, slug)) return new StoreHostAnswer(StoreHostVerdict.Pass, slug, null, null);
            if (!HttpMethods.IsGet(method ?? "") && !HttpMethods.IsHead(method ?? "")) return StoreHostAnswer.NotFound;
            // ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-2/C2a-P-03 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) الوِجهَةُ مِن المَسارِ **مُرَمَّزاً** لا مَفكوكاً:
            // `path` هُنا `req.Path.Value` المَفكوك، فَكانَ `/studio/%D9%85` يُكتَبُ «م» في `Location` ← 500 (Kestrel يَرفُضُ ما خارِجَ ASCII ومَحارِفَ
            // التَحَكُّم)، و`%3F` يَصيرُ `?` فَيَنقَلِبُ المَسارُ استِعلاماً، و`%25` يَصيرُ `%` باطِلاً. وتَطابُقُ المَقاطِعِ أَعلاه يَبقى عَلى المَفكوك.
            return new StoreHostAnswer(StoreHostVerdict.Redirect, slug, null,
                $"{(string.IsNullOrEmpty(scheme) ? "https" : scheme)}://{apex}{PortOf(host)}{new PathString(path).ToUriComponent()}{AsciiOnly(query)}");
        }

        if (string.Equals(first, ReservedTenantSlugs.ApiRoot, StringComparison.OrdinalIgnoreCase))
        {
            // ‏(‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ كُلُّ `/api/push/…` يَمُرّ، فَسَطحُ مَتجَرٍ اسمُه
            // `push` (‏`/api/{slug}/manifest.json`… والاشتِراك) يُخدَمُ عَلى مُضيفِ كُلِّ مَتجَرٍ آخَرَ بِسِياقِه؛ وكانَ `second == slug` وَحدَه، فَعَلى `v1.R` يَبلُغُ
            // `/api/v1/*` API المَنَصَّة. الآن: مِن `/api/push/` طَرَفُ المَنَصَّةِ الواحِدُ بِعَينِه (`GET`/`HEAD`)، وسَطحُ المَتجَرِ لِمَتجَرِ المُضيفِ ما لَم يَكُن اسمُه
            // مَقطَعاً حَرفِيّاً لِـAPI المَنَصَّة — وما سِواهُما 404 (`StoreApiServed`).
            return StoreApiServed(method, path, slug)
                ? new StoreHostAnswer(StoreHostVerdict.Pass, slug, null, null)
                : StoreHostAnswer.NotFound;
        }

        if (role is not null && rolePages is not null && (HttpMethods.IsGet(method ?? "") || HttpMethods.IsHead(method ?? ""))
            && RoleRest(path, slug) is { } rest && rolePages.Matches(rest))
            return new StoreHostAnswer(StoreHostVerdict.Rewrite, slug, $"/{slug}/r/{role}{(rest == "/" ? "" : rest)}", null);

        return PathWithSlug(path, slug) is { } rewritten
            ? new StoreHostAnswer(StoreHostVerdict.Rewrite, slug, rewritten, null)
            : new StoreHostAnswer(StoreHostVerdict.Pass, slug, null, null);
    }

    /// <summary>المَسارُ بَعدَ <c>/{slug}</c> إن حَمَلَه (وإلّا كَما هُوَ) — و<c>null</c> لِما يَحمِلُ <c>/r/…</c> سَلَفاً أَو جَذراً مَخدوماً: لا يُحقَنُ فيه دَورُ المُضيف.</summary>
    private static string? RoleRest(string path, string slug)
    {
        var rest = path;
        var firstSlash = path.IndexOf('/', 1);
        var first = firstSlash > 0 ? path[1..firstSlash] : path[1..];
        if (string.Equals(first, slug, StringComparison.OrdinalIgnoreCase)) rest = firstSlash > 0 ? path[firstSlash..] : "/";
        var segments = rest.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && (string.Equals(segments[0], "r", StringComparison.OrdinalIgnoreCase) || ServedAtRoot(segments[0]))) return null;
        return rest;
    }

    /// <summary>
    /// <para><b>أَيُخدَمُ طَلَبُ <c>/api/…</c> هذا عَلى مُضيفِ المَتجَرِ <paramref name="slug"/>؟ — قَرارٌ نَقِيٌّ يَسأَلُه <see cref="AnswerStoreHost"/></b> (‏2026-11-07،
    /// مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1). نَعَم لِاثنَينِ وَحدَهُما: <c>GET</c>/<c>HEAD</c> <c>/api/push/vapid-key</c> بِلا مَقطَعٍ بَعدَه (مِفتاحُ الإشعارِ
    /// العامّ)، و<c>/api/{slug}</c> وما تَحتَه لِمَتجَرِ المُضيفِ <b>ما لَم يَكُن اسمُه مَقطَعاً حَرفِيّاً لِـAPI المَنَصَّة</b>
    /// (<see cref="ReservedTenantSlugs.PlatformApiSegments"/>). والمُقارَنَةُ بِلا حالَةِ أَحرُفٍ كَما يُطابِقُ التَوجيه.</para>
    /// <para><b>والشَرطُ الأَخيرُ حاجِزٌ ثانٍ مَقيسٌ وَحدَه</b>: <see cref="StoreHostName"/> يَرُدُّ تِلكَ الأَسماءَ فَلا تَصيرُ مُضيفاً أَصلاً — وهذِه الدالَّةُ لا تَعتَمِدُ
    /// عَلَيه، فَمَتجَرٌ قائِمٌ بِها يَبقى بِلا API المَنَصَّةِ عَلى أَيِّ مُضيفٍ ولَو اتَّسَعَ المُصادِقُ يَوماً.</para>
    /// </summary>
    public static bool StoreApiServed(string? method, string? path, string slug)
    {
        var segments = (string.IsNullOrEmpty(path) ? "/" : path).Split('/', 4);
        if (segments.Length < 3 || !string.Equals(segments[1], ReservedTenantSlugs.ApiRoot, StringComparison.OrdinalIgnoreCase)) return false;

        if ((HttpMethods.IsGet(method ?? "") || HttpMethods.IsHead(method ?? ""))
            && segments.Length == 4
            && string.Equals(segments[2], ReservedTenantSlugs.ApiPushSegment, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segments[3], ReservedTenantSlugs.ApiPushVapidKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(segments[2], slug, StringComparison.OrdinalIgnoreCase) && !ReservedTenantSlugs.IsPlatformApiSegment(segments[2]);
    }

    /// <summary><b>صَفَحاتُ إدارَةِ مَتجَرِ المُضيف</b> — <c>/admin/tenants/{slug}</c> أَو <c>/admin/tenants/{slug}/…</c> ومُعَرِّفُها مَتجَرُ المُضيفِ بِعَينِه
    /// (‏2026-11-07، C2a-P-01). والمُقارَنَةُ بِلا حالَةِ أَحرُفٍ كَما يُطابِقُ التَوجيه، ولا بادِئَةَ تَكفي (<c>ashare-x</c> لَيسَ <c>ashare</c>).</summary>
    private static bool IsHostStoreAdmin(string path, string slug)
    {
        var s = path.Split('/', 5);
        return s.Length >= 4
               && string.Equals(s[1], "admin", StringComparison.OrdinalIgnoreCase)
               && string.Equals(s[2], "tenants", StringComparison.OrdinalIgnoreCase)
               && string.Equals(s[3], slug, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><b>أَيَحمِلُ هذا المَسارُ سِياقَ مَتجَرٍ عَلى مُضيفِه؟</b> لا لِمَنطِقَةِ المَنَصَّة: مَسارُها عَلى الجَذرِ بِلا سِياقِ مَتجَر (<see cref="SlugFromPath"/>
    /// يَرُدُّ <c>admin</c> مَحجوزاً)، والمُضيفُ اسمٌ آخَرُ لِلمَسارِ نَفسِه — فَصَفحَةُ إدارَةِ المَتجَرِ عَلى مُضيفِه تُرسَمُ بِقِشرَةِ المَنَصَّةِ كَما عَلى الجَذر، ولا
    /// يُصيبُها قُفلُ المَتجَرِ المُعَلَّقِ في <c>MainLayout</c> (‏2026-11-07، C2a-P-01).</summary>
    public static bool CarriesStoreContext(string? path)
    {
        var p = string.IsNullOrEmpty(path) ? "/" : path;
        var firstSlash = p.IndexOf('/', 1);
        var first = p.Length < 2 ? "" : firstSlash > 0 ? p[1..firstSlash] : p[1..];
        return !ReservedTenantSlugs.PlatformAreas.Contains(first);
    }

    /// <summary><b>مَنفَذُ المُضيفِ لِلوِجهَة</b> — <c>:</c> وأَرقامٌ وَحدَها (١–٥)، وما سِواه لا يُنقَلُ إلى <c>Location</c> (‏2026-11-07، SEC-C2a-2).</summary>
    private static string PortOf(string? host)
    {
        var raw = host?.Trim() ?? "";
        var colon = raw.LastIndexOf(':');
        if (colon < 0 || raw.StartsWith('[')) return "";
        var digits = raw[(colon + 1)..];
        return digits.Length is >= 1 and <= 5 && digits.All(char.IsAsciiDigit) ? ":" + digits : "";
    }

    /// <summary><b>الاستِعلامُ كَما أُرسِل، بِلا مَحرَفٍ لا يَحمِلُه رَأس</b>: ما خارِجَ ASCII المَطبوعِ (ومَعَه المَسافَةُ ومَحارِفُ التَحَكُّم) يُرَمَّزُ بِبايتاتِه
    /// UTF-8 بِنِسبَةٍ مِئَوِيَّة، وما سِواه لا يُمَسّ — فَالاستِعلامُ المُرَمَّزُ سَلَفاً يَبقى بِحَرفِه (‏2026-11-07، SEC-C2a-2).</summary>
    private static string AsciiOnly(string? query)
    {
        if (string.IsNullOrEmpty(query)) return "";
        if (query.All(c => c is > ' ' and < (char)0x7F)) return query;
        var sb = new System.Text.StringBuilder(query.Length * 3);
        Span<byte> utf8 = stackalloc byte[4];
        foreach (var rune in query.EnumerateRunes())
        {
            if (rune.Value is > ' ' and < 0x7F) { sb.Append((char)rune.Value); continue; }
            var n = rune.EncodeToUtf8(utf8);
            for (var i = 0; i < n; i++) sb.Append('%').Append(utf8[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>يُطَبِّعُ مُضيفاً: حُروفٌ صَغيرَة، بِلا مَنفَذٍ ولا
    /// نُقطَةٍ أَخيرَة. و<c>null</c> لِلفارِغِ ولِعَنوانِ ‏IPv6
    /// الحَرفِيّ — <b>ولا تَعَدُّدَ مُستَأجِرينَ على عَنوانٍ
    /// رَقَمِيّ</b>.</summary>
    internal static string? NormaliseHost(string? value)
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
        //
        // ‏(‏2026-11-07، ADR-050 · W13-4·C2a·5 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والمُضيفُ مُضيفُ مَتجَرٍ إن صَلَحَ اسمُه (`StoreSlugFromHost`)، و**مَتجَرٌ
        // لا وُجودَ لَه عَلى مُضيفِه 404** — كانَ يُرسَمُ بِـ200 (القاعِدَة ١٢: مَدخَلٌ لا يَفتَحُ لا يُعرَض). وهُنا لا في الحاقِن: الحاقِنُ لا يَقرَأُ
        // حالَةً مَخزونَة (القاعِدَة ٧)، وهذا الوَسيطُ يُحَمِّلُ وَثيقَةَ المَتجَرِ أَصلاً.
        var hostSlug = StoreSlugFromHost(ctx.Request.Host.Value, _subdomains.BaseDomain);
        // ‏(‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) ومُضيفُ الدَورِ مُضيفُ مَتجَرِه، و**مَتجَرٌ لا يَملِكُ ذلكَ الدَورَ 404 عَلى مُضيفِه**
        // (القاعِدَة ١٢): الدَورُ بِاسمِه في الكَتالوجِ وفي المَتجَرِ مَعاً، فَالحاقِنُ وَضَعَ `/r/{catalog}` في المَسارِ ولا يُدَّعى دَورٌ لَيسَ في المَتجَر.
        var roleHost = hostSlug is null ? RoleFromHost(ctx.Request.Host.Value, _subdomains.BaseDomain) : null;
        hostSlug ??= roleHost?.Slug;
        var slug = hostSlug ?? SlugFromPath(ctx.Request.Path.Value);
        if (slug is null) { await _next(ctx); return; }

        var cacheKey = $"tenant:{slug.ToLowerInvariant()}";
        if (!_cache.TryGetValue(cacheKey, out Tenant? entity))
        {
            await using var session = _store.QuerySession();
            entity = await session.LoadAsync<Tenant>(slug.ToLowerInvariant());
            if (entity is not null)
                _cache.Set(cacheKey, entity, TimeSpan.FromMinutes(5));
        }

        // ‏(‏2026-11-07، C2a-P-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) مَنطِقَةُ المَنَصَّةِ عَلى مُضيفِ المَتجَرِ (إدارَتُه وَحدَها تَبلُغُ هُنا) بِلا سِياقِ مَتجَرٍ كَالجَذر؛
        // والمَتجَرُ المَجهولُ عَلى مُضيفِه يَبقى 404.
        // ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) الشَرطُ في `HoldsRoleHost` بِحَرفِه — تَسأَلُه أَيضاً قائِمَةُ
        // مُضيفاتِ المَتجَرِ الَّتي يَمُرُّ بِها الخُروج، فَلا تَنجَرِفُ عَن هذا الـ404.
        if (entity is not null && roleHost is { } rh && !HoldsRoleHost(entity, rh.CatalogRole))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // ‏(‏2026-11-07، SEC-C2b-R2-2) ومُضيفاتُ المَتجَرِ عَلى طَلَبٍ يَخدِمُه مُضيفٌ لَه — يَقرَؤُها الخُروجُ لِيَمُرَّ بِها كُلِّها (الكَعكاتُ مَقصورَةٌ عَلى مُضيفِها).
        if (entity is not null && hostSlug is not null) ctx.Items[StoreHostItems.HostedRoles] = HostedRoles(entity);

        if (entity is not null)
        {
            if (hostSlug is null || CarriesStoreContext(ctx.Request.Path.Value))
                ctx.SetTenant(entity.Slug, entity.Name, entity.BrandColor, entity.AuthChannel,
                              entity.TagLine, entity.City,
                              hasRoles: entity.Roles.Count > 0);
        }
        else if (hostSlug is not null)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

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

    /// <summary>النِطاقُ مُطَبَّعاً (حُروفٌ صَغيرَة، بِلا مَنفَذٍ ولا نُقطَةٍ أَخيرَة) — أَو <c>null</c> بِلا نِطاق (‏2026-11-07، W13-4·C2b·1).</summary>
    public string? Apex => TenantResolverMiddleware.NormaliseHost(BaseDomain);

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
    public const string HostSlugItem = StoreHostItems.Slug;             // ‏(‏2026-11-07، W13-4·C2a·6) قُرّاؤُهُما في القالِبِ والعُدَّة
    public const string OriginalPathItem = StoreHostItems.OriginalPath;

    private readonly RequestDelegate _next;
    private readonly SubdomainTenancyOptions _options;
    private readonly RolePageTemplates? _rolePages;

    // ‏(‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) قَوالِبُ صَفَحاتِ الدَورِ مِن الحاوِيَةِ إن سَجَّلَها القالِب — وبِدونِها لا يُحقَنُ دَور.
    public SubdomainTenantPathMiddleware(RequestDelegate next, SubdomainTenancyOptions options, IServiceProvider services)
    {
        _next = next; _options = options; _rolePages = services.GetService<RolePageTemplates>();
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // ‏(‏2026-11-07، ADR-050 §٩ · W13-4·C2a·5 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) القَرارُ كُلُّه في `AnswerStoreHost` النَقِيَّة: كانَ هُنا حَقنٌ
        // وَحدَه، فَمَناطِقُ المَنَصَّةِ و`/api/{مَتجَرٍ آخَر}` تُخدَمُ بِسِياقِ مَتجَرِ المُضيف، ومِلصَقٌ لَيسَ اسمَ مَتجَرٍ يُرسَمُ صَفحَةَ المَنَصَّة.
        var req = ctx.Request;
        // ‏(‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) النِطاقُ نَفسُه لِمَن بَعدَه عَلى كُلِّ طَلَبٍ مَتى هُيِّئ:
        // «افتَح المَتجَر» مِن الجَذرِ يَبني عُنوانَ مُضيفِه المُطلَق. وبِلا نِطاقٍ لا يُكتَبُ شَيء.
        if (_options.Apex is { } apex) ctx.Items[StoreHostItems.Apex] = apex;
        var answer = TenantResolverMiddleware.AnswerStoreHost(
            req.Host.Value, _options.BaseDomain, req.Method, req.Path.Value, req.QueryString.Value, req.Scheme, _rolePages);

        switch (answer.Verdict)
        {
            case StoreHostVerdict.NotFound:
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            case StoreHostVerdict.Redirect:
                ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                ctx.Response.Headers.Location = answer.Location;
                return;
        }

        if (answer.Slug is not null)
        {
            ctx.Items[HostSlugItem] = answer.Slug;
            ctx.Items[OriginalPathItem] = req.Path.Value;
            if (answer.Role is { } hostRole) ctx.Items[StoreHostItems.Role] = hostRole;
            if (answer.Verdict == StoreHostVerdict.Rewrite) req.Path = answer.Path;
        }

        await _next(ctx);
    }
}

/// <summary>ما يُفعَلُ بِالطَلَبِ عَلى مُضيفِه (<see cref="TenantResolverMiddleware.AnswerStoreHost"/>).</summary>
public enum StoreHostVerdict { Pass, Rewrite, Redirect, NotFound }

/// <summary>الجَوابُ: الحُكم، ومَتجَرُ المُضيفِ إن كانَ مُضيفَ مَتجَر، والمَسارُ بَعدَ الحَقن، والوِجهَةُ لِلتَحويل.</summary>
public sealed record StoreHostAnswer(StoreHostVerdict Verdict, string? Slug, string? Path, string? Location)
{
    /// <summary>دَورُ الكَتالوجِ عَلى مُضيفِ الدَور (<c>{slug}-{rolehost}.R</c>) — <c>null</c> عَلى مُضيفِ المَتجَرِ وما سِواه (‏2026-11-07، W13-4·C2b·4).</summary>
    public string? Role { get; init; }

    /// <summary>بِلا مَتجَرٍ ولا مَساسٍ — الجَذرُ أَو مُضيفٌ خارِجَه أَو مِلصَقٌ مَحجوز أَو نِطاقٌ فارِغ.</summary>
    public static readonly StoreHostAnswer Through = new(StoreHostVerdict.Pass, null, null, null);
    public static readonly StoreHostAnswer NotFound = new(StoreHostVerdict.NotFound, null, null, null);
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
