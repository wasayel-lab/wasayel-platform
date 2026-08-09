using System.Text;
using ACommerce.Kit.Roles;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبار تَوصيف RoleCatalog (Characterization) ───────────────────
// المَوجَة الأولى مِن «الأَدوار مِلَفّات»: نَقل كاتالوج الأَدوار السَبعَة
// مِن C# مُجَمَّع إلى تَعريفات JSON خَلف نَفس الواجِهَة، بِسُلوك مُطابِق
// حَرفاً بِحَرف — نَفس مَنهَجيَّة الخُطوَة ٤ (رَفع DealsPolicy).
//
// هذا المِلَفّ يَلتَقِط السَّطح **كامِلاً** كَبَيانات ذَهَبيَّة حَرفيَّة:
//   • القَوالِب السَبعَة بِتَرتيبِها، وكُلّ خاصِّيَّة في كُلّ قالِب
//     (Slug/Label/Icon/Description/HomeRoute).
//   • الصَلاحِيّات **بِتَرتيبِها** لا كَمَجموعَة.
//   • الحُقول بِأَنواعِها وإلزامِها وخِياراتِها، **بِنُصوصِها المُشَكولَة
//     حَرفِيّاً** — التَّشكيل جُزء مِن اللَّقطَة، وأَيّ فَقد لِحَرَكَة
//     واحِدَة يُفشِل الاختِبار.
//   • Find لِكُلّ slug + slug مَجهول (null).
//   • InstantiateRole — مَسار المُستَهلِكين الفِعليّ (النَّسخ إلى
//     Tenant.Roles) بِكُلّ حُقولِه بِما فيها SortOrder وCatalogSlug.
//   • RolePermissions.Has — مَصفوفَة كامِلَة (كُلّ دَور × كُلّ صَلاحِيَّة)
//     مُوجَبَة وسالِبَة، مَع الحالات الحَدِّيَّة: مَتجَر بِلا أَدوار
//     (legacy = كُلّ شَيء مَسموح)، وActiveRole فارِغ، ودَور مَجهول،
//     وصَلاحِيَّة مَجهولَة.
//
// كُتِبَ واخضَرَّ **قَبل** تَبديل الكاتالوج إلى مِلَفّات، ولَم يُمَسّ سَطر
// واحِد مِنه بَعدَه — فَمُروره عَلى الكودَين هو بُرهان التَّطابُق، ويَبقى
// حارِساً دائِماً ضِدّ أَيّ انحِراف صامِت لاحِق.

public class RoleCatalogCharacterizationTests
{
    /// <summary>الصَلاحِيّات المَفحوصَة — كُلّ ما يَظهَر في الكاتالوج
    /// اليَوم، مُرَتَّبَة أَبجَدِيّاً، زائِد صَلاحِيَّة مَجهولَة قَصداً
    /// لِتَوثيق سُلوك الرَّفض.</summary>
    private static readonly string[] ProbedPermissions =
    {
        "chat.respond", "chat.start",
        "listing.browse", "listing.create", "listing.delete", "listing.edit",
        "offer.submit", "tenant.manage",
        "permission.from.the.future"
    };

    /// <summary>الأَدوار المَفحوصَة في مَصفوفَة Has — السَبعَة، ثُمَّ
    /// دَور غَير مُسَجَّل، ثُمَّ فارِغ وnull.</summary>
    private static readonly string?[] ProbedRoles =
    {
        "customer", "rider", "vendor", "driver", "host", "shipper", "tenant_admin",
        "role_from_the_future", "", null
    };

    /// <summary>اللَّقطَة الذَّهَبيَّة — نَصّ حَرفيّ لا يُشتَقّ مِن الكود
    /// المَفحوص بِأَيّ حال (وإلّا صارَ الاختِبار دَورَة فارِغَة).</summary>
    private const string Golden =
"""
# RoleCatalog characterization snapshot

[order]
customer > rider > vendor > driver > host > shipper > tenant_admin

[templates]
--- customer
label       = عَميل
icon        = 🛒
description = مُشتَري عامّ — يَتَصَفَّح ويَتَواصَل ويَشتَري. لا يَنشُر إعلانات.
homeRoute   = []
permissions = listing.browse,offer.submit,chat.start
fields      = 0
--- rider
label       = راكِب
icon        = 🧍
description = يَنشُر طَلَب مِشوار + يَختار مِن عُروض السائِقين (نَموذَج إنجيز).
homeRoute   = [/me/listings]
permissions = listing.browse,listing.create,chat.start
fields      = 0
--- vendor
label       = تاجِر
icon        = 🏪
description = صاحِب نَشاط تِجاريّ — يَنشُر إعلانات وَ يَرُدّ عَلى الاستِفسارات.
homeRoute   = [/me/listings]
permissions = listing.create,listing.edit,listing.delete,chat.respond
fields      = 2
  field business_name | اسم النَّشاط | Text | required=True | options=0
  field business_type | نَوع النَّشاط | Text | required=False | options=0
--- driver
label       = سائِق
icon        = 🚗
description = سائِق مَركَبَة يُقَدِّم عُروضاً عَلى مَشاوير العُمَلاء.
homeRoute   = [/explore]
permissions = listing.browse,offer.submit,chat.respond
fields      = 3
  field vehicle_type | نَوع المَركَبَة | SingleSelect | required=True | options=4
    option economy = اقتِصاديّ
    option comfort = مُريح
    option luxury = فاخِر
    option family = عائِليّ
  field vehicle_plate | لَوحَة المَركَبَة | Text | required=True | options=0
  field license_number | رَقم رُخصَة القِيادَة | Text | required=True | options=0
--- host
label       = مالِك سَكَن
icon        = 🏠
description = مالِك عَقار يَنشُر إعلان سَكَن لِلإيجار أَو المُشارَكَة.
homeRoute   = [/me/listings]
permissions = listing.create,listing.edit,listing.delete,chat.respond
fields      = 1
  field contact_phone | هاتِف تَواصُل بَديل | Text | required=False | options=0
--- shipper
label       = شَركَة شَحن
icon        = 📦
description = شَركَة تُقَدِّم خَدَمات نَقل بَضائِع.
homeRoute   = [/explore]
permissions = listing.browse,offer.submit,chat.respond
fields      = 2
  field company_name | اسم الشَركَة | Text | required=True | options=0
  field fleet_size | حَجم الأُسطول | Number | required=False | options=0
--- tenant_admin
label       = إداريّ المَتجَر
icon        = 👔
description = إداريّ يُدير مُحتَوى وَ مُستَخدِمي المَتجَر بِالكامِل.
homeRoute   = [/manage]
permissions = listing.create,listing.edit,listing.delete,offer.submit,chat.respond,chat.start,tenant.manage,listing.browse
fields      = 0

[find]
find(customer) -> customer
find(rider) -> rider
find(vendor) -> vendor
find(driver) -> driver
find(host) -> host
find(shipper) -> shipper
find(tenant_admin) -> tenant_admin
find(Customer) -> (null)
find() -> (null)
find(slug_from_the_future) -> (null)

[instantiate]
--- customer
label        = عَميل
icon         = 🛒
description  = مُشتَري عامّ — يَتَصَفَّح ويَتَواصَل ويَشتَري. لا يَنشُر إعلانات.
sortOrder    = 0
isDefault    = False
catalogSlug  = customer
homeRoute    = []
permissions  = listing.browse,offer.submit,chat.start
pwaIcon      = (null)
pwaName      = (null)
--- rider
label        = راكِب
icon         = 🧍
description  = يَنشُر طَلَب مِشوار + يَختار مِن عُروض السائِقين (نَموذَج إنجيز).
sortOrder    = 1
isDefault    = False
catalogSlug  = rider
homeRoute    = [/me/listings]
permissions  = listing.browse,listing.create,chat.start
pwaIcon      = (null)
pwaName      = (null)
--- vendor
label        = تاجِر
icon         = 🏪
description  = صاحِب نَشاط تِجاريّ — يَنشُر إعلانات وَ يَرُدّ عَلى الاستِفسارات.
sortOrder    = 2
isDefault    = False
catalogSlug  = vendor
homeRoute    = [/me/listings]
permissions  = listing.create,listing.edit,listing.delete,chat.respond
pwaIcon      = (null)
pwaName      = (null)
  field business_name | اسم النَّشاط | Text | required=True | options=0
  field business_type | نَوع النَّشاط | Text | required=False | options=0
--- driver
label        = سائِق
icon         = 🚗
description  = سائِق مَركَبَة يُقَدِّم عُروضاً عَلى مَشاوير العُمَلاء.
sortOrder    = 3
isDefault    = False
catalogSlug  = driver
homeRoute    = [/explore]
permissions  = listing.browse,offer.submit,chat.respond
pwaIcon      = (null)
pwaName      = (null)
  field vehicle_type | نَوع المَركَبَة | SingleSelect | required=True | options=4
    option economy = اقتِصاديّ
    option comfort = مُريح
    option luxury = فاخِر
    option family = عائِليّ
  field vehicle_plate | لَوحَة المَركَبَة | Text | required=True | options=0
  field license_number | رَقم رُخصَة القِيادَة | Text | required=True | options=0
--- host
label        = مالِك سَكَن
icon         = 🏠
description  = مالِك عَقار يَنشُر إعلان سَكَن لِلإيجار أَو المُشارَكَة.
sortOrder    = 4
isDefault    = False
catalogSlug  = host
homeRoute    = [/me/listings]
permissions  = listing.create,listing.edit,listing.delete,chat.respond
pwaIcon      = (null)
pwaName      = (null)
  field contact_phone | هاتِف تَواصُل بَديل | Text | required=False | options=0
--- shipper
label        = شَركَة شَحن
icon         = 📦
description  = شَركَة تُقَدِّم خَدَمات نَقل بَضائِع.
sortOrder    = 5
isDefault    = False
catalogSlug  = shipper
homeRoute    = [/explore]
permissions  = listing.browse,offer.submit,chat.respond
pwaIcon      = (null)
pwaName      = (null)
  field company_name | اسم الشَركَة | Text | required=True | options=0
  field fleet_size | حَجم الأُسطول | Number | required=False | options=0
--- tenant_admin
label        = إداريّ المَتجَر
icon         = 👔
description  = إداريّ يُدير مُحتَوى وَ مُستَخدِمي المَتجَر بِالكامِل.
sortOrder    = 6
isDefault    = False
catalogSlug  = tenant_admin
homeRoute    = [/manage]
permissions  = listing.create,listing.edit,listing.delete,offer.submit,chat.respond,chat.start,tenant.manage,listing.browse
pwaIcon      = (null)
pwaName      = (null)

[instantiate-is-a-copy]
template.permissions = listing.browse,offer.submit,chat.respond
template.label       = سائِق
template.option0     = اقتِصاديّ

[has]
customer.chat.respond = False
customer.chat.start = True
customer.listing.browse = True
customer.listing.create = False
customer.listing.delete = False
customer.listing.edit = False
customer.offer.submit = True
customer.tenant.manage = False
customer.permission.from.the.future = False
rider.chat.respond = False
rider.chat.start = True
rider.listing.browse = True
rider.listing.create = True
rider.listing.delete = False
rider.listing.edit = False
rider.offer.submit = False
rider.tenant.manage = False
rider.permission.from.the.future = False
vendor.chat.respond = True
vendor.chat.start = False
vendor.listing.browse = False
vendor.listing.create = True
vendor.listing.delete = True
vendor.listing.edit = True
vendor.offer.submit = False
vendor.tenant.manage = False
vendor.permission.from.the.future = False
driver.chat.respond = True
driver.chat.start = False
driver.listing.browse = True
driver.listing.create = False
driver.listing.delete = False
driver.listing.edit = False
driver.offer.submit = True
driver.tenant.manage = False
driver.permission.from.the.future = False
host.chat.respond = True
host.chat.start = False
host.listing.browse = False
host.listing.create = True
host.listing.delete = True
host.listing.edit = True
host.offer.submit = False
host.tenant.manage = False
host.permission.from.the.future = False
shipper.chat.respond = True
shipper.chat.start = False
shipper.listing.browse = True
shipper.listing.create = False
shipper.listing.delete = False
shipper.listing.edit = False
shipper.offer.submit = True
shipper.tenant.manage = False
shipper.permission.from.the.future = False
tenant_admin.chat.respond = True
tenant_admin.chat.start = True
tenant_admin.listing.browse = True
tenant_admin.listing.create = True
tenant_admin.listing.delete = True
tenant_admin.listing.edit = True
tenant_admin.offer.submit = True
tenant_admin.tenant.manage = True
tenant_admin.permission.from.the.future = False
role_from_the_future.chat.respond = False
role_from_the_future.chat.start = False
role_from_the_future.listing.browse = False
role_from_the_future.listing.create = False
role_from_the_future.listing.delete = False
role_from_the_future.listing.edit = False
role_from_the_future.offer.submit = False
role_from_the_future.tenant.manage = False
role_from_the_future.permission.from.the.future = False
.chat.respond = False
.chat.start = False
.listing.browse = False
.listing.create = False
.listing.delete = False
.listing.edit = False
.offer.submit = False
.tenant.manage = False
.permission.from.the.future = False
(null).chat.respond = False
(null).chat.start = False
(null).listing.browse = False
(null).listing.create = False
(null).listing.delete = False
(null).listing.edit = False
(null).offer.submit = False
(null).tenant.manage = False
(null).permission.from.the.future = False

[has-legacy-empty-tenant]
customer.listing.create = True
rider.listing.create = True
vendor.listing.create = True
driver.listing.create = True
host.listing.create = True
shipper.listing.create = True
tenant_admin.listing.create = True
role_from_the_future.listing.create = True
.listing.create = True
(null).listing.create = True
""";

    [Fact]
    public void Catalog_surface_is_unchanged()
        => Assert.Equal(
            Golden.ReplaceLineEndings("\n").Trim(),
            Render().ReplaceLineEndings("\n").Trim());

    // ─── المُصَيِّر: نَصّ حَتميّ يُغَطّي كُلّ سَطح الكاتالوج ─────────────
    internal static string Render()
    {
        var sb = new StringBuilder();
        sb.Append("# RoleCatalog characterization snapshot\n");

        sb.Append("\n[order]\n");
        sb.Append(string.Join(" > ", RoleCatalog.All.Select(t => t.Slug))).Append('\n');

        sb.Append("\n[templates]\n");
        foreach (var t in RoleCatalog.All)
        {
            sb.Append($"--- {t.Slug}\n");
            sb.Append($"label       = {t.Label}\n");
            sb.Append($"icon        = {t.Icon}\n");
            sb.Append($"description = {t.Description}\n");
            sb.Append($"homeRoute   = [{t.HomeRoute}]\n");
            sb.Append($"permissions = {string.Join(",", t.Permissions)}\n");
            sb.Append($"fields      = {t.Fields.Count}\n");
            foreach (var f in t.Fields)
            {
                sb.Append($"  field {f.Code} | {f.Label} | {f.Type} | required={f.IsRequired} | options={f.Options.Count}\n");
                foreach (var o in f.Options)
                    sb.Append($"    option {o.Value} = {o.Label}\n");
            }
        }

        sb.Append("\n[find]\n");
        foreach (var slug in new[]
                 { "customer", "rider", "vendor", "driver", "host", "shipper",
                   "tenant_admin", "Customer", "", "slug_from_the_future" })
            sb.Append($"find({slug}) -> {RoleCatalog.Find(slug)?.Slug ?? "(null)"}\n");

        sb.Append("\n[instantiate]\n");
        var order = 0;
        foreach (var t in RoleCatalog.All)
        {
            var r = RoleCatalog.InstantiateRole(t, order++);
            sb.Append($"--- {r.Slug}\n");
            sb.Append($"label        = {r.Label}\n");
            sb.Append($"icon         = {r.Icon ?? "(null)"}\n");
            sb.Append($"description  = {r.Description ?? "(null)"}\n");
            sb.Append($"sortOrder    = {r.SortOrder}\n");
            sb.Append($"isDefault    = {r.IsDefault}\n");
            sb.Append($"catalogSlug  = {r.CatalogSlug}\n");
            sb.Append($"homeRoute    = [{r.HomeRoute}]\n");
            sb.Append($"permissions  = {string.Join(",", r.Permissions)}\n");
            sb.Append($"pwaIcon      = {r.PwaIconDataUrl ?? "(null)"}\n");
            sb.Append($"pwaName      = {r.PwaName ?? "(null)"}\n");
            foreach (var f in r.Fields)
            {
                sb.Append($"  field {f.Code} | {f.Label} | {f.Type} | required={f.IsRequired} | options={f.Options.Count}\n");
                foreach (var o in f.Options)
                    sb.Append($"    option {o.Value} = {o.Label}\n");
            }
        }

        // نَسخَة مُستَقِلَّة: تَعديل الدَور المَنسوخ لا يَمَسّ القالِب.
        sb.Append("\n[instantiate-is-a-copy]\n");
        var tmpl = RoleCatalog.Find("driver")!;
        var copy = RoleCatalog.InstantiateRole(tmpl);
        copy.Permissions.Add("mutated.permission");
        copy.Label = "mutated";
        copy.Fields[0].Options[0].Label = "mutated";
        sb.Append($"template.permissions = {string.Join(",", RoleCatalog.Find("driver")!.Permissions)}\n");
        sb.Append($"template.label       = {RoleCatalog.Find("driver")!.Label}\n");
        sb.Append($"template.option0     = {RoleCatalog.Find("driver")!.Fields[0].Options[0].Label}\n");

        // مَصفوفَة الصَلاحِيّات — مَتجَر يَحمِل الأَدوار السَبعَة كُلَّها.
        var tenantRoles = RoleCatalog.All
            .Select((t2, i) => RoleCatalog.InstantiateRole(t2, i))
            .ToList();

        sb.Append("\n[has]\n");
        foreach (var role in ProbedRoles)
            foreach (var p in ProbedPermissions)
                sb.Append($"{role ?? "(null)"}.{p} = {RolePermissions.Has(tenantRoles, role, p)}\n");

        sb.Append("\n[has-legacy-empty-tenant]\n");
        var noRoles = new List<Role>();
        foreach (var role in ProbedRoles)
            sb.Append($"{role ?? "(null)"}.listing.create = {RolePermissions.Has(noRoles, role, "listing.create")}\n");

        return sb.ToString();
    }
}
