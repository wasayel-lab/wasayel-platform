using ACommerce.Kit.Roles;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يَعودَ حِزامُ القِراءَةِ (TenantRoleSet.FromDocuments) إلى قَواعِدِه قَبلَ 97ba402 (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ
//     الثانِيَة · C2z-PATHS-R2-01) ═══
//
// **ما سَيَتَغَيَّر**: `slug_shadows_role_host` أُضيفَ إلى `ValidateTenantDefinition` (97ba402) — والقارِئُ يُعيدُ المُصادَقَةَ عَلى كُلِّ وَثيقَةٍ مُعتَمَدَةٍ ويُسقِطُ ما يَخرِق،
// فَدَورٌ مُؤَلَّفٌ اسمُه `admin` اعتُمِدَ قَبلَ القاعِدَةِ يَختَفي عِندَ النَشرِ ولَو بِلا نِطاق (مَقيسٌ عِندَ المُراجِع: «تُجوهِلَ: slug_shadows_role_host» وTenantAuthored: staff)،
// وADR-050 يَقولُ «يُرَدُّ عِندَ الاعتِماد». **وهذا المِلَفُّ يُثَبِّتُ ما لا يَتَغَيَّرُ** ويَبقى بايتاتِه (القاعِدَة ٣): بابُ الاقتِراحِ والاعتِمادِ يَرُدُّ `admin` بِرَمزِه؛
// والقارِئُ يَلتَقِطُ المُعتَمَدَ وَحدَه، ويُسقِطُ ظِلَّ الكَتالوجِ والنَصَّ الفاسِدَ والشَكلَ الباطِل، ويُبقي السَليم؛ والتَجسيدُ يُلحِقُ المُؤَلَّفَ بِأَدوارِ المَتجَر.

public class RoleReadBeltCharacterizationTests(ITestOutputHelper output)
{
    private static TenantRoleDefinition Doc(string slug, string status, string json, int minute = 0) =>
        new() { Id = slug, Slug = slug, Status = status, DefinitionJson = json,
                CreatedAt = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc) };

    private static string Json(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "icon": "🧪",
      "homeRoute": "",
      "label": { "ar": "دَورٌ لِلتَوصيف", "en": null },
      "description": { "ar": "دَورٌ يُؤَلِّفُه مَتجَر.", "en": null },
      "permissions": [ "listing.browse" ],
      "fields": [],
      "composition": {
        "home": "defaultHome",
        "createListing": "defaultCreateForm",
        "nav": "defaultNav",
        "explore": "defaultExplore",
        "publicProfile": null,
        "extras": []
      },
      "dealPatternAffinity": null
    }
    """;

    [Fact]
    public void The_write_gate_refuses_admin_with_its_code()
    {
        var admin = RoleDefinitionLoader.ParseDefinition(Json("admin"));
        var codes = RoleDefinitionValidator.ValidateTenantDefinition(admin).Select(v => v.Code).ToList();
        output.WriteLine($"admin ⇒ {string.Join(" ", codes)}");
        Assert.Equal([RoleDefinitionValidator.SlugShadowsRoleHost], codes);
        Assert.False(RoleDefinitionValidator.IsValidTenantDefinition(admin));
        Assert.Equal("slug_shadows_role_host", RoleDefinitionValidator.SlugShadowsRoleHost);
        Assert.Empty(RoleDefinitionValidator.ValidateTenantDefinition(RoleDefinitionLoader.ParseDefinition(Json("staff"))));
    }

    [Fact]
    public void The_read_belt_keeps_the_sound_approved_roles_and_drops_the_rest()
    {
        var set = TenantRoleSet.FromDocuments("shop",
        [
            Doc("staff", TenantRoleStatuses.Approved, Json("staff"), 1),
            Doc("vendor", TenantRoleStatuses.Approved, Json("vendor"), 2),          // ظِلُّ الكَتالوج
            Doc("broken", TenantRoleStatuses.Approved, "{ not json", 3),            // نَصٌّ فاسِد
            Doc("Bad-Slug", TenantRoleStatuses.Approved, Json("Bad-Slug"), 4),      // شَكلٌ باطِل
            Doc("admin", TenantRoleStatuses.Pending, Json("admin"), 5),             // مُعَلَّقٌ لا يُقرَأ
            Doc("helper", TenantRoleStatuses.Approved, Json("helper"), 6),
        ]);
        var kept = set.TenantAuthored.Select(d => d.Slug).ToArray();
        output.WriteLine($"المُلتَقَط: {string.Join(" ", kept)}");
        Assert.Equal(["staff", "helper"], kept);
        Assert.Equal("shop", set.TenantSlug);
    }

    [Fact]
    public void Materialize_appends_the_authored_roles_to_the_store_roles()
    {
        var set = TenantRoleSet.FromDocuments("shop", [Doc("staff", TenantRoleStatuses.Approved, Json("staff"))]);
        var merged = set.Materialize([new Role { Slug = "customer", CatalogSlug = "customer", SortOrder = 0 }]);
        Assert.Equal(["customer", "staff"], merged.Select(r => r.Slug).ToArray());
        Assert.Equal("staff", merged[1].CatalogSlug);
    }
}
