using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// الدَور الفَعّال لِنُقطَة كِتابَة بِلا <c>/r/{role}/</c> في مَسارِها.
///
/// <para><b>العَيب المُعالَج:</b> <c>User.ActiveRole</c> حَقل واحِد مُخَزَّن.
/// المَسارات المُسَوَّرَة بِدَور تَتَفَوَّق بِالـ URL، لكِنّ نِقاط الكِتابَة
/// بِلا دَور (‏listings/create، ‏listings/{id}/offers…) كانَت تَسقُط إلى هذا
/// الحَقل المُشتَرَك — فَآخِر تَسجيل دُخول يَفوز، ومُستَخدِم بِدَورَين عَلى
/// نَفس المَتجَر لا يَعمَل بِهِما مُتَزامِنَينِ.</para>
///
/// <para><b>الحَلّ:</b> نَمَط <c>as</c> صَريح — النَّموذَج عَلى الصَفحَة
/// المُسَوَّرَة بِدَور يَحمِل <c>as=&lt;الدَور&gt;</c>، فَتَقرَؤه نُقطَة الكِتابَة
/// كَ«الدَور الفَعّال المَطلوب». تَرتيب الحَسم: الصَّريح ثُمَّ URL ثُمَّ
/// المُخَزَّن. <b>لا تُكتَب <c>ActiveRole</c> مِن نِقاط الكِتابَة</b> — بِلا
/// أَثَر جانِبيّ عَلى الحَقل المُشتَرَك.</para>
///
/// <para><b>الأَمان:</b> <c>as</c> مُجَرَّد اختِيار بَينَ أَدوار يَملِكُها
/// المُستَخدِم فِعلاً. المِلكِيَّة يُثبِتُها وُجود cookie الجَلسَة المَخصوصَة
/// بِذلكَ الدَور لِنَفس المُستَخدِم — وَهُوَ نَفس نَموذَج عَزل الجَلَسات
/// المُتَوازِيَة (تَحمِل دَوراً ⟺ تَحمِل cookie جَلسَتِه). قيمَة <c>as</c>
/// لِدَور لا يَملِكُه المُستَخدِم <b>تُهمَل</b> فَيَسقُط الحَسم إلى دَورِه
/// الحَقيقيّ، ولا تُمنَح بِها صَلاحِيَّة جَديدَة: التَّفويض يَبقَى
/// <c>RolePermissions.Has(tenant.Roles, effectiveRole, perm)</c>.</para>
/// </summary>
public static class EffectiveRole
{
    /// <summary>القَرار النَّقيّ: الصَّريح <paramref name="formAs"/> (فَقَط إن
    /// كانَ <paramref name="ownsFormAs"/>) ثُمَّ دَور الـ URL ثُمَّ المُخَزَّن.
    /// قيمَة <c>as</c> غَير المَملوكَة تُهمَل — لا تَصعيد.</summary>
    public static string? Resolve(string? formAs, bool ownsFormAs,
                                  string? urlRole, string? activeRole)
    {
        if (!string.IsNullOrEmpty(formAs) && ownsFormAs) return formAs;
        if (!string.IsNullOrEmpty(urlRole)) return urlRole;
        return string.IsNullOrEmpty(activeRole) ? null : activeRole;
    }

    /// <summary>الدَور الوَحيد غَير القابِل لِلاختِيار الذاتيّ. كُلّ الأَدوار
    /// الأُخرى يَمنَحُها المُستَخدِم لِنَفسِه بِدُخول <c>as={role}</c>
    /// (<c>AssignRoleAsync</c>)، أَمّا هذا فَيُمنَح يَدَويّاً مِن
    /// <c>/admin/tenants/{slug}/users</c> فَقَط.</summary>
    private const string AdminRole = SelfGrantPolicy.AdminSlug;

    /// <summary>بُرهان المِلكِيَّة: هَل يَحمِل المُتَصَفِّح cookie جَلسَة
    /// صالِحَة لِهذا <paramref name="role"/> تَخُصّ <paramref name="userId"/>
    /// في هذا المَتجَر؟ الـ token لا يَحمِل الدَور، فَاسم الـ cookie
    /// (<c>.acommerce.auth.{slug}.{role}</c>) هُوَ ادِّعاء الدَور.
    ///
    /// <para><b>حَدّ أَمنيّ:</b> <c>tenant_admin</c> لا يُملَك أَبَداً عَبر
    /// <c>as</c>. الـ token مُشتَرَك بَينَ أَدوار المُستَخدِم الواحِد
    /// (لا يَحمِل دَوراً)، فَلَو اعتَمَدنا مُجَرَّد اسم الـ cookie لَأَمكَن
    /// لِمُستَخدِم أَن يَنسَخ توكِنَه تَحت اسم <c>.tenant_admin</c> ويَنتَحِلَه.
    /// لِبَقِيَّة الأَدوار هذا غَير ذي أَثَر (كُلُّها اختِياريَّة ذاتيّاً)، أَمّا
    /// الإداريّ فَيُمنَح مِن الخادِم وَحدَه — فَنَستَثنيه هُنا مُطابَقَةً
    /// لِـ<c>AssignRoleAsync</c>. مَسار الإدارَة يَقرَأ <c>ActiveRole</c>
    /// مُباشَرَةً لا <c>as</c>.</para></summary>
    public static bool OwnsRole(HttpRequest req, string slug, Guid userId, string? role)
    {
        if (string.IsNullOrEmpty(role) || role == AdminRole) return false;
        var cookie = req.Cookies[AuthSession.CookieName(slug, role)];
        var parsed = AuthHandlers.ParseToken(cookie);
        return parsed is not null
            && parsed.Value.TenantSlug == slug
            && parsed.Value.UserId == userId;
    }

    /// <summary>الحَسم المَربوط بِالـ HTTP: يَقرَأ <c>as</c> مِن النَّموذَج،
    /// يَتَحَقَّق مِن المِلكِيَّة، ثُمَّ يُرَكِّب القَرار. يُستَدعَى مِن
    /// <see cref="PermissionFilter"/> ونِقاط الكِتابَة بِلا دَور.</summary>
    public static async Task<string?> ResolveAsync(HttpContext http, string slug,
                                                   Guid userId, string? activeRole)
    {
        var formAs = "";
        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync();
            formAs = form["as"].ToString().Trim().ToLowerInvariant();
        }
        var owns = OwnsRole(http.Request, slug, userId, formAs);
        return Resolve(formAs, owns, http.Role(), activeRole);
    }
}
