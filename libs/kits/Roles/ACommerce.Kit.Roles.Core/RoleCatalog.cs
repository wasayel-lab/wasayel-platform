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
/// </summary>
public static class RoleCatalog
{
    public static readonly IReadOnlyList<RoleTemplate> All = new[]
    {
        new RoleTemplate(
            Slug:        "customer",
            Label:       "عَميل",
            Icon:        "🛒",
            Description: "يَتَصَفَّح، يَطلُب، وَيَنشُر طَلَبات (مَثَلاً مِشوار في إنجيز).",
            HomeRoute:   "",
            // listing.create مَمنوحَة لِأَنّ نَموذَج "العَميل يَنشُر طَلَب،
            // المُزَوِّدون يُقَدِّمون عُروض" شائِع (إنجيز، طَلَب خِدمَة، …).
            // المَتاجِر الَّتي لا تَحتاج هذه القُدرَة لا تَعرِض زِرّ "إعلان
            // جَديد" لِلعُمَلاء — الـ UI يَتَحَكَّم بِالظُّهور.
            Permissions: new[] { "listing.browse", "listing.create", "offer.submit", "chat.start" },
            Fields:      new RoleField[] { }),

        new RoleTemplate(
            Slug:        "vendor",
            Label:       "تاجِر",
            Icon:        "🏪",
            Description: "صاحِب نَشاط تِجاريّ — يَنشُر إعلانات وَ يَرُدّ عَلى الاستِفسارات.",
            HomeRoute:   "/me/listings",
            Permissions: new[] { "listing.create", "listing.edit", "listing.delete", "chat.respond" },
            Fields:      new[]
            {
                new RoleField { Code = "business_name", Label = "اسم النَّشاط", Type = "Text", IsRequired = true },
                new RoleField { Code = "business_type", Label = "نَوع النَّشاط", Type = "Text", IsRequired = false },
            }),

        new RoleTemplate(
            Slug:        "driver",
            Label:       "سائِق",
            Icon:        "🚗",
            Description: "سائِق مَركَبَة يُقَدِّم عُروضاً عَلى مَشاوير العُمَلاء.",
            HomeRoute:   "/explore",
            Permissions: new[] { "listing.browse", "offer.submit", "chat.respond" },
            Fields:      new[]
            {
                new RoleField
                {
                    Code = "vehicle_type", Label = "نَوع المَركَبَة",
                    Type = "SingleSelect", IsRequired = true,
                    Options = new()
                    {
                        new() { Value = "economy", Label = "اقتِصاديّ" },
                        new() { Value = "comfort", Label = "مُريح" },
                        new() { Value = "luxury",  Label = "فاخِر" },
                        new() { Value = "family",  Label = "عائِليّ" },
                    }
                },
                new RoleField { Code = "vehicle_plate",  Label = "لَوحَة المَركَبَة",     Type = "Text", IsRequired = true },
                new RoleField { Code = "license_number", Label = "رَقم رُخصَة القِيادَة", Type = "Text", IsRequired = true },
            }),

        new RoleTemplate(
            Slug:        "host",
            Label:       "مالِك سَكَن",
            Icon:        "🏠",
            Description: "مالِك عَقار يَنشُر إعلان سَكَن لِلإيجار أَو المُشارَكَة.",
            HomeRoute:   "/me/listings",
            Permissions: new[] { "listing.create", "listing.edit", "listing.delete", "chat.respond" },
            Fields:      new[]
            {
                new RoleField { Code = "contact_phone", Label = "هاتِف تَواصُل بَديل", Type = "Text", IsRequired = false },
            }),

        new RoleTemplate(
            Slug:        "shipper",
            Label:       "شَركَة شَحن",
            Icon:        "📦",
            Description: "شَركَة تُقَدِّم خَدَمات نَقل بَضائِع.",
            HomeRoute:   "/explore",
            Permissions: new[] { "listing.browse", "offer.submit", "chat.respond" },
            Fields:      new[]
            {
                new RoleField { Code = "company_name", Label = "اسم الشَركَة",     Type = "Text",   IsRequired = true },
                new RoleField { Code = "fleet_size",   Label = "حَجم الأُسطول",     Type = "Number", IsRequired = false },
            }),

        new RoleTemplate(
            Slug:        "tenant_admin",
            Label:       "إداريّ المَتجَر",
            Icon:        "👔",
            Description: "إداريّ يُدير مُحتَوى وَ مُستَخدِمي المَتجَر بِالكامِل.",
            HomeRoute:   "/manage",
            Permissions: new[]
            {
                "listing.create", "listing.edit", "listing.delete",
                "offer.submit", "chat.respond", "chat.start",
                "tenant.manage", "listing.browse"
            },
            Fields:      new RoleField[] { }),
    };

    public static RoleTemplate? Find(string slug) =>
        All.FirstOrDefault(r => r.Slug == slug);

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
/// كانَ الـ ActiveRole لِلمُستَخدِم يَملِك صَلاحِيَّة مُعَيَّنَة.</summary>
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
