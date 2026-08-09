using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// حالَة المُستَخدِم في الـ Blazor circuit. تُحَمَّل مِن cookie في كُلّ
/// طَلَب SSR. كَتابَة الـ cookie تَجري في endpoints الـ SSR forms.
///
/// <para>دَعم تَطبيقات فَرعيَّة (دَور لِكُلّ تَبويب): الـ cookie يَأخُذ
/// اسماً يَشمَل الدَور <c>.acommerce.auth.{slug}.{role}</c>، فَكُلّ دَور
/// لَه session مُستَقِلّ في نَفس المُتَصَفِّح. الـ Path يَبقَى <c>/</c>
/// (المُتَصَفِّح يُرسِل كُلّ الـ cookies)، لكِنّ AuthSession يَختار أَيُّها
/// يَقرَأ بِناءً عَلى الدَور المُستَخرَج مِن URL (نَمَط <c>/{slug}/r/{role}/…</c>).
/// إن كانَ المَسار بِلا <c>/r/{role}/</c> — وَهُوَ حالُ كُلّ نِقاط الـ POST
/// تَقريباً — يُقبَل الـ cookie العامّ <c>.acommerce.auth.{slug}</c>
/// (مَتاجِر ashare/ejar) ثُمَّ أَيّ cookie دَور لِنَفس المَتجَر.
/// التَّفصيل وَالمُبَرِّر في <see cref="ResolveCookieBase"/>.</para>
/// </summary>
public sealed class AuthSession
{
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? Token { get; private set; }
    public string? TenantSlug { get; private set; }
    public string? RoleScope { get; private set; }    // الدَور النَّشِط لِهذا الطَلَب
    public bool IsAuthenticated => UserId.HasValue;

    public event Action? Changed;

    public void Load(HttpContext http, string requiredTenantSlug)
    {
        RoleScope = ExtractRoleFromPath(http.Request.Path);
        var name = ResolveCookieBase(http.Request, requiredTenantSlug);
        if (name is null) { Clear(); return; }
        var token = http.Request.Cookies[name];
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null) { Clear(); return; }
        var (uid, slug, _) = parsed.Value;
        UserId = uid; TenantSlug = slug; Token = token;
        UserName = http.Request.Cookies[name + ".name"] ?? "—";
        Changed?.Invoke();
    }

    public void SetTenant(string slug) => TenantSlug = slug;
    public void Clear() { UserId = null; UserName = null; Token = null; }

    // ─── اسم الـ cookie ──────────────────────────────────────────────
    public static string CookieName(string tenantSlug)
        => $".acommerce.auth.{tenantSlug}";

    public static string CookieName(string tenantSlug, string? role)
        => string.IsNullOrEmpty(role)
            ? CookieName(tenantSlug)
            : $".acommerce.auth.{tenantSlug}.{role}";

    // ─── حَلّ الجَلسَة: القاعِدَة الواحِدَة ───────────────────────────────
    //
    // كُلّ قارِئ لِلجَلسَة (AuthSession.Load، AuthFilter، وكُلّ form endpoint)
    // يَمُرّ مِن هُنا — لا يَقرَأ أَحَدٌ اسم cookie بِيَدِه. كانَ العَكس هُوَ
    // العَيب: عَشَرات النِّقاط تَقرَأ الاسم العامّ <c>.acommerce.auth.{slug}</c>
    // حَرفيّاً، بَينَما الدُخول عَبر <c>/{slug}/r/{role}/login</c> يَكتُب
    // <c>.acommerce.auth.{slug}.{role}</c> فَقَط — فَكُلّ POST عَلى مَسار بِلا
    // <c>/r/{role}/</c> (‏listings/create، ‏listings/{id}/offers، ‏cart،
    // ‏checkout، ‏deals…) يَراهُ المُستَخدِم مُسَجَّلاً وَيَراهُ الخادِم
    // مَجهولاً، فَيُعيد توجيهاً لِـ login أَو يَفشَل صامِتاً.
    //
    // القاعِدَة:
    //   • مَسار بِدَور  → cookie ذلكَ الدَور وَحدَه، بِلا سُقوط. (عَزل الأَدوار
    //     يَبقَى كَما صُمِّم: جَلسَة راكِب في تَبويب وَسائِق في آخَر.)
    //   • مَسار بِلا دَور → الـ cookie العامّ أَوَّلاً (تَوافُق مَع المَتاجِر
    //     أُحادِيَّة الدَور ashare/ejar)، ثُمَّ أَيّ cookie دَور لِنَفس المَتجَر
    //     يَحمِلُه المُتَصَفِّح أَصلاً.
    //
    // لِماذا التَّوسيع آمِن: الـ token لا يَحمِل دَوراً إطلاقاً — هُوَ
    // <c>{userId}|{tenantSlug}|{exp}|{sig}</c> (‏AuthHandlers.MakeToken). فَكُلّ
    // cookies الأَدوار لِنَفس المُستَخدِم مُتَكافِئَة هُوِيَّةً، وَقُبولُ أَيِّها
    // عَلى مَسار بِلا دَور لا يَمنَح صَلاحِيَّةً جَديدَة: التَّفويض يَجري بَعدَها
    // عَبر <c>RolePermissions.Has(tenant.Roles, user.ActiveRole, …)</c>. وَما
    // يُفحَص هُنا هِيَ cookies المُتَصَفِّح نَفسِه — لا يَستَطيع طَرَف ثالِث
    // زَرعَ جَلسَة غَيرِه فيها.

    /// <summary>اسم الـ cookie الَّذي يَحمِل جَلسَةً صالِحَة لِهذا المَتجَر في
    /// هذا الطَّلَب، أَو <c>null</c>. الاسم — لا القيمَة — لِيَقرَأ المُتَّصِل
    /// الـ token وَ<c>.name</c> مِن نَفس العائِلَة فَلا يَختَلِط اسمُ دَور
    /// بِتوكِن دَور آخَر.</summary>
    public static string? ResolveCookieBase(HttpRequest req, string tenantSlug)
    {
        var role = ExtractRoleFromPath(req.Path);
        if (role is not null)
        {
            var scoped = CookieName(tenantSlug, role);
            return IsValidFor(req.Cookies[scoped], tenantSlug) ? scoped : null;
        }

        var generic = CookieName(tenantSlug);
        if (IsValidFor(req.Cookies[generic], tenantSlug)) return generic;

        var prefix = generic + ".";
        foreach (var (cookieName, value) in req.Cookies)
        {
            if (!cookieName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (cookieName.EndsWith(".name", StringComparison.Ordinal)) continue;
            if (IsValidFor(value, tenantSlug)) return cookieName;
        }
        return null;
    }

    /// <summary>الـ token الصالِح لِهذا الطَّلَب، أَو <c>null</c>.</summary>
    public static string? ResolveToken(HttpRequest req, string tenantSlug)
    {
        var name = ResolveCookieBase(req, tenantSlug);
        return name is null ? null : req.Cookies[name];
    }

    /// <summary>اسم صاحِب الجَلسَة المَحلولَة — مِن نَفس عائِلَة الـ token.</summary>
    public static string? ResolveUserName(HttpRequest req, string tenantSlug)
    {
        var name = ResolveCookieBase(req, tenantSlug);
        return name is null ? null : req.Cookies[name + ".name"];
    }

    private static bool IsValidFor(string? token, string tenantSlug)
    {
        var parsed = AuthHandlers.ParseToken(token);
        return parsed is not null && parsed.Value.TenantSlug == tenantSlug;
    }

    /// <summary>يَستَخرِج الدَور مِن <c>/{slug}/r/{role}/…</c>. <c>null</c>
    /// إن كانَ المَسار قَديماً (بِلا <c>/r/</c>).</summary>
    public static string? ExtractRoleFromPath(PathString path)
    {
        var s = path.Value;
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // parts[0] = slug, parts[1] = "r", parts[2] = role
        if (parts.Length >= 3 && parts[1].Equals("r", StringComparison.OrdinalIgnoreCase))
            return parts[2].ToLowerInvariant();
        return null;
    }

    /// <summary>يُكتَب مِن SSR endpoint بَعد نَجاح المُصادَقَة. حِنَّ يُعطَى
    /// <paramref name="role"/>، يُكتَب cookie role-scoped؛ بِلا role يُكتَب
    /// الـ cookie القَديم (لِـ ashare/ejar).</summary>
    /// <summary>الـ HttpContext الحاليّ — يُستَخدَم لِكَشف هَل الطَّلَب على
    /// HTTPS فَنَضَع Secure تِلقائيّاً. يُحقَن مَرَّةً في Program عَبر
    /// IHttpContextAccessor.</summary>
    public static IHttpContextAccessor? HttpAccessor { get; set; }

    /// <summary>هَل نَضَع الـ Secure flag؟ القاعِدَة الذَّكِيَّة: نَعَم فَقَط
    /// لَو الطَّلَب الحاليّ HTTPS فِعليّاً. هكذا يَعمَل HTTP المَحَلّيّ
    /// (تَطوير) تِلقائيّاً بِلا ENV، ويَبقى آمِناً خَلف HTTPS (إنتاج).
    /// كانَ الافتِراضيّ Secure=true دائِماً، فَكَسَرَ تَسجيل الدُّخول على
    /// HTTP المَحَلّيّ صامِتاً (المُتَصَفِّح يَرفُض cookie آمِناً على http).
    /// مِفتاح <c>ACOMMERCE_FORCE_INSECURE_COOKIES=1</c> يُجبِر الإطفاء.</summary>
    private static bool ShouldUseSecure
    {
        get
        {
            if (Environment.GetEnvironmentVariable("ACOMMERCE_FORCE_INSECURE_COOKIES") == "1")
                return false;
            return HttpAccessor?.HttpContext?.Request.IsHttps ?? false;
        }
    }

    private static CookieOptions BuildOpts() => new()
    {
        HttpOnly    = true,
        IsEssential = true,
        Expires     = DateTimeOffset.UtcNow.AddDays(30),
        SameSite    = SameSiteMode.Lax,
        Secure      = ShouldUseSecure,
        Path        = "/"
    };

    public static void WriteCookie(HttpResponse res, string tenantSlug, AuthResult auth,
                                    string? role = null)
    {
        var opts = BuildOpts();
        var name = CookieName(tenantSlug, role);
        res.Cookies.Append(name, auth.Token, opts);
        res.Cookies.Append(name + ".name", auth.FullName, opts);

        // ونُسخَة بِالاسم العامّ كَذلك. الدُخول بِدَور كانَ يَكتُب cookie الدَور
        // وَحدَه، فَيَبقَى كُلّ مَسار بِلا /r/{role}/ بِلا جَلسَة. الكِتابَة هُنا
        // تَجعَل الحَلّ حَتمِيّاً (بِلا مَسح cookies) لِكُلّ دُخول جَديد، وَلا
        // تَمَسّ عَزل الأَدوار: المَسارات المُسَوَّرَة بِدَور لا تَقرَأ العامّ
        // أَبَداً، وَالـ token بِلا دَور فَلا هُوِيَّةَ جَديدَة تُمنَح.
        // وَ ClearAllCookiesForTenant يَمسَح العامّ وَكُلّ الأَدوار مَعاً.
        if (!string.IsNullOrEmpty(role))
        {
            var generic = CookieName(tenantSlug);
            res.Cookies.Append(generic, auth.Token, opts);
            res.Cookies.Append(generic + ".name", auth.FullName, opts);
        }
    }

    public static void UpdateNameCookie(HttpResponse res, string tenantSlug, string newName,
                                         string? role = null)
    {
        var opts = BuildOpts();
        res.Cookies.Append(CookieName(tenantSlug, role) + ".name", newName, opts);
        if (!string.IsNullOrEmpty(role))
            res.Cookies.Append(CookieName(tenantSlug) + ".name", newName, opts);
    }

    public static void ClearCookie(HttpResponse res, string tenantSlug, string? role = null)
    {
        var opts = new CookieOptions { Path = "/", Secure = ShouldUseSecure };
        var name = CookieName(tenantSlug, role);
        res.Cookies.Delete(name, opts);
        res.Cookies.Delete(name + ".name", opts);
    }

    /// <summary>يَمسَح كُلّ cookies المُستَخدِم لِكُلّ الأَدوار المُحتَمَلَة في
    /// هذا المَتجَر — يَستَخدِمها endpoint الـ logout. كانَ يَمسَح cookie
    /// واحِد فَقَط فَيَتَسَرَّب جَلسَة /r/{role}/.</summary>
    public static void ClearAllCookiesForTenant(HttpResponse res, string tenantSlug,
                                                 IEnumerable<string> roleSlugs)
    {
        ClearCookie(res, tenantSlug, role: null);
        foreach (var r in roleSlugs) ClearCookie(res, tenantSlug, r);
    }

    /// <summary>يَبني URL مُنبَثِق مِن tenant slug + role اختياريّ.</summary>
    public static string LinkFor(string tenantSlug, string? role, string path)
    {
        path = path.TrimStart('/');
        return string.IsNullOrEmpty(role)
            ? $"/{tenantSlug}/{path}".TrimEnd('/')
            : $"/{tenantSlug}/r/{role}/{path}".TrimEnd('/');
    }
}
