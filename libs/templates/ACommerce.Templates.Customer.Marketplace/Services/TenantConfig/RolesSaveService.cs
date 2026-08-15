using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>أَدوارُ المُستَأجِر كَما اختارَها: مَجموعَةُ
/// <c>CatalogSlug</c> مُختارَة، وأَيُّها الافتِراضيّ. والتَرتيبُ
/// لَيسَ مِن هُنا — هُوَ تَرتيبُ الكاتالوج، فَلا يَتَعَلَّق
/// بِتَرتيبِ حُقولِ نَموذَج.</summary>
public sealed record RolesSaveRequest(
    IReadOnlyCollection<string> SelectedCatalogSlugs,
    string? DefaultCatalogSlug);

/// <summary>
/// <para><b>حِفظُ أَدوار المُستَأجِر — والانحِراف هُنا كانَ فَقدَ
/// بَيانات، لا اختِلافَ ذَوق.</b> كانَ <c>/admin</c> ‏63 سَطراً
/// و<c>/studio</c> ‏25، والفَرقُ الجَوهَريّ أَنّ الاستوديو كانَ
/// <b>يُعيد إنشاءَ كُلّ دَورٍ مِن الكاتالوج</b>
/// (<c>InstantiateRole</c>) بَينَما الإدارَة تَحتَفِظ بِالدَور
/// القائِم وتُحَدِّث صَلاحِيّاتِه.</para>
///
/// <para><b>وثَمَنُ ذلك مَقيسٌ في وَثيقَةٍ أُخرى</b>: الدَور يَحمِل
/// <c>PwaName</c> و<c>PwaIconDataUrl</c> — يَرفَعُهُما صاحِبُ
/// التَطبيق مِن صَفحَة PWA. فَحِفظُ صَفحَةِ الأَدوار مِن الاستوديو
/// كانَ يَمحو الأَيقونَةَ الَّتي رَفَعَها بِنَفسِه قَبلَ دَقائِق،
/// ومَعَها <c>Label</c> و<c>Icon</c> المُخَصَّصَين. <b>فَالغالِبُ
/// هُوَ الَّذي لا يَفقِد</b> — سُلوك <c>/admin</c>.</para>
///
/// <para><b>وما يُحَدَّث مِن الكاتالوج عَمداً</b>: الصَلاحِيّات
/// والحُقول والمَسار الرَئيسيّ. فَتَحديثُ كاتالوج المَنَصَّة يَبلُغ
/// المَتاجِر القائِمَة، وذلك السَبَبُ المَكتوب في النُقطَة
/// الأَصلِيَّة ولَم يَتَغَيَّر.</para>
/// </summary>
public static class RolesSaveService
{
    public const string AuditAction = "tenant.roles_save";

    /// <summary>
    /// <para><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): أَدوارٌ قائِمَة +
    /// اختِيار ← أَدوارٌ جَديدَة. بِلا Marten وبِلا HTTP، فَتُنادى
    /// مِن اختِبارٍ يُثبِت <b>أَنّ التَخصيصات لا تُفقَد</b> بِلا
    /// قاعِدَةِ بَيانات.</para>
    ///
    /// <para>والتَرتيب تَرتيبُ <see cref="RoleCatalog.All"/> لا
    /// تَرتيبُ الاختِيار — فَنَفسُ الاختِيار يُعطي نَفسَ
    /// <c>SortOrder</c> مَهما اختَلَفَ تَرتيبُ حُقول النَموذَج.</para>
    /// </summary>
    public static List<Role> Compose(
        IReadOnlyList<Role> existing,
        IReadOnlyCollection<string> selected,
        string? defaultCatalogSlug)
    {
        var byCatalog = existing
            .Where(r => !string.IsNullOrEmpty(r.CatalogSlug))
            .GroupBy(r => r.CatalogSlug)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var picks = new List<Role>();
        var idx = 0;

        foreach (var tmpl in RoleCatalog.All)
        {
            if (!selected.Contains(tmpl.Slug)) continue;

            Role role;
            if (byCatalog.TryGetValue(tmpl.Slug, out var prev))
            {
                // اِحفَظ تَخصيصات المُصَمِّم (Label/Icon/Pwa*) وحَدِّث
                // ما يَملِكُه الكاتالوج.
                role = prev;
                role.Permissions = tmpl.Permissions.ToList();
                role.HomeRoute = tmpl.HomeRoute;
                role.Fields = tmpl.Fields.Select(f => new RoleField
                {
                    Code = f.Code, Label = f.Label, Type = f.Type,
                    IsRequired = f.IsRequired,
                    Options = f.Options.Select(o => new RoleFieldOption
                    {
                        Value = o.Value, Label = o.Label
                    }).ToList()
                }).ToList();
                role.SortOrder = idx++;
            }
            else
            {
                role = RoleCatalog.InstantiateRole(tmpl, idx++);
            }

            role.IsDefault = string.Equals(defaultCatalogSlug, tmpl.Slug, StringComparison.Ordinal);
            picks.Add(role);
        }

        return picks;
    }

    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, string slug, RolesSaveRequest r,
        CancellationToken ct = default)
    {
        var t = await session.LoadAsync<Tenant>(slug, ct);
        if (t is null) return TenantConfigResult.TenantMissing;

        t.Roles = Compose(t.Roles, r.SelectedCatalogSlugs, r.DefaultCatalogSlug);
        session.Store(t);
        return TenantConfigResult.Saved;
    }
}
