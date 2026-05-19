namespace ACommerce.Kit.Roles;

/// <summary>
/// دَور (role) داخِل مَتجَر: راكِب/سائِق، مالِك سَكَن/باحِث، مُؤَجِّر/مُستَأجِر…
/// يُعَرِّفها المُشرِف عَلى مُستَوى الـ Tenant. كُلّ مُستَخدِم لَه دَور
/// نَشِط واحِد عَلى الأَكثَر، يُمكِنه التَّبديل بَين الأَدوار المُتاحَة
/// لَه. لِكُلّ دَور خَصائِص ديناميكِيَّة مُنفَصِلَة (مَثَلاً: السائِق
/// عِنده نَوع سَيّارَة ولَوحَة، الراكِب لا).
///
/// <para>الـ Slug: مُعَرِّف فَريد ASCII داخِل المَتجَر. الـ Label:
/// نَصّ ظاهِر لِلمُستَخدِم. الـ ProfileScopeId: GUID مُحايِد لِخَزن
/// الخَصائِص الديناميكِيَّة الخاصَّة بِالدَور — يُشتَقّ كَ
/// <c>MD5("{tenantSlug}:role:{roleSlug}")</c> لِيَتَوافَق مَع نَمَط
/// scopes الفِئات.</para>
///
/// <para>الدَور <strong>اختِياريّ</strong>: مَتجَر بِلا أَدوار يَعمَل
/// بِنَمَط user-فَرد (الوَضع الحالي لِـ Ashare و Ejar). الأَدوار تُضاف
/// لِمَتاجِر مِثل إنجيز الَّتي تَحتاج تَمييز السائِق عَن الراكِب.</para>
/// </summary>
public sealed class Role
{
    public string Slug { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Icon { get; set; }

    /// <summary>وَصف قَصير يُعرَض لِلمُستَخدِم عِند اختِيار الدَور.</summary>
    public string? Description { get; set; }

    /// <summary>تَرتيب العَرض في القائِمَة (٠ أَوَّلاً).</summary>
    public int SortOrder { get; set; }

    /// <summary>دَور افتراضيّ — يُسَكَّن لِلمُستَخدِم الجَديد إن لَم
    /// يُحَدِّد أَيّاً بَعد التَّسجيل. مَتجَر واحِد قَد يَحوي عِدَّة أَدوار
    /// "افتراضيَّة" — أَوَّل واحِد بِالتَّرتيب يَنتَصِر.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>مَنطِق اشتِقاق scope_id لِبروفايل الدَور — مُطابِق لِنَمَط
/// الفِئات. مَفصول كَ static helper لِيَستَخدِمَه المُشرِف
/// (TenantAttributes scope picker)، الوَكيل (snapshot)، والـ runtime
/// (DynamicAttributesService) بِنَفس النَّتيجَة.</summary>
public static class RoleScopes
{
    public static System.Guid DeriveProfileScope(string tenantSlug, string roleSlug)
    {
        var raw = tenantSlug.ToLowerInvariant() + ":role:" + roleSlug.ToLowerInvariant();
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return new System.Guid(hash);
    }
}
