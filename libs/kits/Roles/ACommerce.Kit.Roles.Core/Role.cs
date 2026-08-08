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

    /// <summary>مَرجَع إلى slug في <see cref="RoleCatalog"/>. يُوَفِّر
    /// قَوالِب صَلاحِيّات + حُقول مَطلوبَة. فارِغ = دَور مُخَصَّص
    /// بالكامِل (نادِر).</summary>
    public string CatalogSlug { get; set; } = "";

    /// <summary>صَلاحِيّات هذا الدَور — تُسَكَّن مِن الكاتالوج عِندَ إضافَة
    /// الدَور. أَمثِلَة: "listing.create", "offer.submit", "chat.respond".
    /// مَتجَر بِلا أَدوار (Roles فارِغَة) يُعتَبَر مَفتوحاً — كُلّ الصَلاحِيّات
    /// مَمنوحَة لِلسلوك القَديم.</summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>المَسار المَفتوح بَعد تَسجيل الدُخول لِلمُستَخدِم بِهذا
    /// الدَور النَّشِط. مَثَلاً سائِق إنجيز → "/explore" لِيَرى المَشاوير
    /// المُتاحَة، تاجِر → "/me/listings". فارِغ = الصَفحَة الافتراضِيَّة.</summary>
    public string HomeRoute { get; set; } = "";

    /// <summary>حُقول بَيانات مَطلوبَة لِهذا الدَور — مَنسوخَة مِن
    /// الكاتالوج عِندَ إضافَة الدَور. تُجمَع في الـ onboarding وَتُخزَّن
    /// في <c>User.RoleAttributesJson[roleSlug][code]</c>.</summary>
    public List<RoleField> Fields { get; set; } = new();

    /// <summary>أَيقونَة مُخَصَّصَة (data: URL أَو URL خارِجيّ، يَفضَّل
    /// PNG 512x512) رَفَعَها مُصَمِّم المَتجَر لِـ PWA هذا الدَور. <c>null</c>
    /// = نَستَخدِم أَيقونَة تِلقائيَّة مَبنيَّة مِن لَون المَتجَر + الحَرف
    /// الأَوَّل لِـ Label.</summary>
    public string? PwaIconDataUrl { get; set; }

    /// <summary>اسم اختياريّ لِـ PWA هذا الدَور — يَتَجاوَز التَّوليد
    /// التِلقائي "{tenant.Name} {role.Label}".</summary>
    public string? PwaName { get; set; }
}

public sealed class RoleField
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";

    /// <summary>"Text" | "LongText" | "Number" | "Boolean" | "SingleSelect"
    /// | "MultiSelect" | "Date" — مُتَوافِق مَع DynFieldType.</summary>
    public string Type { get; set; } = "Text";

    public bool IsRequired { get; set; }
    public List<RoleFieldOption> Options { get; set; } = new();
}

public sealed class RoleFieldOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
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
