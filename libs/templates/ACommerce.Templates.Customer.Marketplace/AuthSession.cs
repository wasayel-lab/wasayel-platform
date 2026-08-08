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
/// إن كانَ المَسار بِلا <c>/r/{role}/</c> نَسقُط لِلسُلوك القَديم: cookie
/// واحِد بِالاسم <c>.acommerce.auth.{slug}</c> (مَتاجِر ashare/ejar).</para>
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
        var role = ExtractRoleFromPath(http.Request.Path);
        RoleScope = role;
        var name = CookieName(requiredTenantSlug, role);
        var token = http.Request.Cookies[name];
        if (token is null && role is not null)
        {
            // إن كانَ الـ URL يَحمِل role لكِنّ لا cookie لَه، لا نَسقُط لِـ
            // legacy — نَترُك المُستَخدِم غَير-مُسَجَّل في هذا التَّطبيق الفَرعيّ.
            Clear(); return;
        }
        if (token is null) token = http.Request.Cookies[CookieName(requiredTenantSlug)];   // legacy fallback
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null) { Clear(); return; }
        var (uid, slug, _) = parsed.Value;
        if (slug != requiredTenantSlug) { Clear(); return; }
        UserId = uid; TenantSlug = slug; Token = token;
        UserName = http.Request.Cookies[name + ".name"]
                ?? http.Request.Cookies[CookieName(slug) + ".name"]
                ?? "—";
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
    }

    public static void UpdateNameCookie(HttpResponse res, string tenantSlug, string newName,
                                         string? role = null)
    {
        res.Cookies.Append(CookieName(tenantSlug, role) + ".name", newName, BuildOpts());
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
