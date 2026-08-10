namespace ACommerce.Kit.Roles;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// مَكتَبَة الأَدوار القِياسيَّة — يَختار مُصَمِّم المَتجَر مِنها بَدَل
/// تَعريف كُلّ شَيء مِن الصِّفر. كُلّ قالِب يُوَفِّر:
/// <list type="bullet">
///   <item>Label/Icon افتراضيّ يُمكِن لِلمُصَمِّم تَخصيصُه</item>
///   <item>Permissions: قائِمَة صَلاحِيّات تُضبَط في الـ endpoints</item>
///   <item>HomeRoute: المَسار المَفتوح بَعد الدُخول لِلمُستَخدِم الَّذي
///         يَتَّخِذ هذا الدَور</item>
///   <item>Fields: حُقول بَيانات تُجمَع في الـ onboarding وَ ProfileEdit</item>
/// </list>
/// لا تُغَيَّر هذه القَوالِب بِالـ runtime — التَّخصيص عَلى مُستَوى المَتجَر
/// يَتِم بِنَسخ القالِب إلى <see cref="Role"/> ثُمّ تَعديل الحُقول هُناك.
///
/// <para><b>المَصدَر: مِلَفّات (2026-08-10)</b>. كانَت القَوالِب السَبعَة
/// مَصفوفَة <c>RoleTemplate</c> مَكتوبَة في هذا المِلَفّ ومُجَمَّعَة مَعَه؛
/// صارَت <see cref="RoleDefinition"/> تُقرَأ مِن
/// <c>Definitions/*.role.json</c> عَبر <see cref="RoleDefinitionLoader"/>.
/// <b>الواجِهَة العامَّة لَم تَتَغَيَّر حَرفاً</b> — <see cref="All"/>
/// و<see cref="Find"/> و<see cref="InstantiateRole"/> بِتَواقيعِها
/// وأَنواعِها، فَلا مُستَهلِك واحِد مِن التِّسعَة احتاجَ تَعديلاً.
/// والتَّطابُق مُبرهَن بِـ <c>RoleCatalogCharacterizationTests</c> الَّذي
/// كُتِبَ واخضَرَّ قَبل النَّقل ولَم يُمَسّ بَعدَه.</para>
///
/// <para><b>حَدّ المَوجَة، مُعلَناً</b>: التَّعريفات <b>مَضمونَة في
/// العُدَّة</b> لا وَثائِق Marten لِكُلّ مُستَأجِر — نَفس حَدّ الخُطوَة ٤
/// في <c>DealPatternCatalog</c>، وحينَ يُنَفَّذ التَّخزين يَتَغَيَّر
/// <see cref="RoleDefinitionLoader"/> وَحدَه.</para>
/// </summary>
public static class RoleCatalog
{
    /// <summary>التَّعريفات كَما قُرِئَت مِن المِلَفّات — بِحاوِيات
    /// التَّوطين وقِسم التَّركيب كامِلَين. <see cref="All"/> إسقاط
    /// مِنها إلى العَقد القَديم.</summary>
    public static readonly IReadOnlyList<RoleDefinition> Definitions =
        RoleDefinitionLoader.LoadEmbedded();

    public static readonly IReadOnlyList<RoleTemplate> All =
        Definitions.Select(ToTemplate).ToArray();

    public static RoleTemplate? Find(string slug) =>
        All.FirstOrDefault(r => r.Slug == slug);

    /// <summary>التَّعريف الكامِل لِـ slug — لِمَن يَحتاج التَّركيب أَو
    /// حاوِيات التَّوطين (المَوجَة الثانِيَة). <see cref="Find"/> يَبقى
    /// المَدخَل لِمَن يَحتاج القالِب القَديم.</summary>
    public static RoleDefinition? FindDefinition(string slug) =>
        Definitions.FirstOrDefault(d => d.Slug == slug);

    /// <summary>يُحَوِّل قالِباً إلى <see cref="Role"/> جاهِز لِلتَّخزين
    /// في <c>Tenant.Roles</c>. المُصَمِّم يُمكِنه تَعديل Label/Icon لاحِقاً.</summary>
    public static Role InstantiateRole(RoleTemplate template, int sortOrder = 0)
    {
        return new Role
        {
            Slug        = template.Slug,
            Label       = template.Label,
            Icon        = template.Icon,
            Description = template.Description,
            SortOrder   = sortOrder,
            CatalogSlug = template.Slug,
            Permissions = template.Permissions.ToList(),
            HomeRoute   = template.HomeRoute,
            Fields      = template.Fields.Select(f => new RoleField
            {
                Code = f.Code, Label = f.Label, Type = f.Type,
                IsRequired = f.IsRequired,
                Options = f.Options.Select(o => new RoleFieldOption
                {
                    Value = o.Value, Label = o.Label
                }).ToList()
            }).ToList()
        };
    }

    /// <summary>إسقاط التَّعريف إلى القالِب القَديم. <b>القِراءَة
    /// عَرَبيَّة كَما اليَوم</b> (<see cref="LocalizedText.Current"/>)،
    /// وقِسم التَّركيب لا يَظهَر في القالِب لِأَنّ لا مُستَهلِك لَه بَعد.
    ///
    /// <para><c>internal</c> لا <c>private</c> مُنذُ مَوجَة «الأَدوار
    /// وَثائِق»: <see cref="TenantRoleSet"/> يُسقِط تَعريفات المُستَأجِر
    /// إلى قَوالِب <b>بِنَفس هذه الدالَّة</b> لا بِنُسخَة مِنها — وإلّا
    /// لَانحَرَفَ إسقاط دَور مُؤَلَّف عَن إسقاط دَور كاتالوج، وهو
    /// انحِراف لا يَراه أَحَد حَتّى يَظهَر في بِطاقَة بَوّابَة.</para></summary>
    internal static RoleTemplate ToTemplate(RoleDefinition d) => new(
        Slug:        d.Slug,
        Label:       d.Label.Current,
        Icon:        d.Icon,
        Description: d.Description.Current,
        HomeRoute:   d.HomeRoute,
        Permissions: d.Permissions.ToArray(),
        Fields:      d.Fields.Select(f => new RoleField
        {
            Code = f.Code,
            Label = f.Label.Current,
            Type = f.Type,
            IsRequired = f.IsRequired,
            Options = f.Options.Select(o => new RoleFieldOption
            {
                Value = o.Value, Label = o.Label.Current
            }).ToList()
        }).ToArray());
}

public sealed record RoleTemplate(
    string Slug,
    string Label,
    string Icon,
    string Description,
    string HomeRoute,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<RoleField> Fields);

/// <summary>أَدوات صَلاحِيّات — تَستَخدِمها الـ endpoints لِفَحص ما إذا
/// كانَ الـ ActiveRole لِلمُستَخدِم يَملِك صَلاحِيَّة مُعَيَّنَة.
///
/// <para><b>لَم تُمَسّ في مَوجَة «الأَدوار مِلَفّات»</b>، وذلك مَقصود:
/// هذه الدالَّة لا تَقرَأ الكاتالوج أَصلاً — تَقرَأ <c>tenant.Roles</c>،
/// وهي بَيانات مُنذُ البِدايَة (تُنسَخ مِن الكاتالوج عِندَ
/// <c>set_roles</c>). فَنَقل الكاتالوج إلى مِلَفّات لا يَمَسّ سَطراً
/// مِنها، والتَّوصيف يَحرُس ذلك بِمَصفوفَة كامِلَة.</para></summary>
public static class RolePermissions
{
    /// <summary>هَل المُستَخدِم بِدَورِه النَّشِط يَملِك هذه الصَلاحِيَّة؟
    /// مَتجَر بِلا أَدوار (Tenant.Roles فارِغَة) = legacy mode، كُلّ
    /// الصَلاحِيّات مَمنوحَة.</summary>
    public static bool Has(IReadOnlyCollection<Role> tenantRoles, string? activeRole, string permission)
    {
        if (tenantRoles.Count == 0) return true;
        if (string.IsNullOrEmpty(activeRole)) return false;
        var role = tenantRoles.FirstOrDefault(r => r.Slug == activeRole);
        return role?.Permissions.Contains(permission) == true;
    }
}
