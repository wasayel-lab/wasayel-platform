using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ `slug_shadows_role_host` يُسأَلُ حَيثُ يُقتَرَحُ الدَورُ ويُعتَمَد، لا حَيثُ يُقرَأ (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الثانِيَة · C2z-PATHS-R2-01) ═══
//
// **المَقيسُ قَبل** (عِندَ d573b39، والصُفوفُ أَدناه حَمراء): `TenantRoleSet.FromDocuments("shop", [admin، staff])` يَطبَعُ «تُجوهِلَ: slug_shadows_role_host» ويُلتَقَطُ staff
// وَحدَه — فَدَورٌ مُؤَلَّفٌ اعتُمِدَ قَبلَ 97ba402 يَختَفي عِندَ النَشرِ مِن `Tenant.Roles` المُجَسَّدِ ومِن أَعضائِه، ولَو بِلا نِطاق. **والآن**: الحِزامُ يَسأَلُ
// `ValidateStoredTenantDefinition` (قَواعِدُ 7ce945e بِحَرفِها: الشَكلُ وظِلُّ الكَتالوج)، وبابا الكِتابَةِ (اقتِراحُ الوَكيلِ واعتِمادُ المُشرِف) يَسأَلانِ
// `ValidateTenantDefinition` = تِلكَ وظِلُّ مُضيفِ الدَور. **ولا يَتَصادَمُ المُضيفُ بِذلك**: الدَورُ المُؤَلَّفُ لا مُضيفَ لَه (`HoldsRoleHost` يَطلُبُ اسمَ الكَتالوجِ
// وسلاجَه مَعاً، و`RoleHostNames` مَفاتيحُه أَدوارُ الكَتالوج) — فَـ`{slug}-admin.R` لا يَعني إلّا `tenant_admin`. وما لا يَتَغَيَّرُ يُثَبِّتُه
// `RoleReadBeltCharacterizationTests` بِبايتاتِه.

public class RoleReadBeltTests(ITestOutputHelper output)
{
    private static TenantRoleDefinition Doc(string slug, int minute) =>
        new() { Id = slug, Slug = slug, Status = TenantRoleStatuses.Approved, DefinitionJson = Json(slug),
                CreatedAt = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc) };

    private static string Json(string slug) => $$"""
    {
      "slug": "{{slug}}",
      "icon": "🛡",
      "homeRoute": "",
      "label": { "ar": "مُديرُ الفَرع", "en": null },
      "description": { "ar": "دَورٌ أَلَّفَه المَتجَرُ واعتُمِد.", "en": null },
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
    public void An_approved_role_named_admin_survives_the_read()
    {
        var set = TenantRoleSet.FromDocuments("shop", [Doc("admin", 1), Doc("staff", 2)]);
        var kept = set.TenantAuthored.Select(d => d.Slug).ToArray();
        output.WriteLine($"المُلتَقَط: {string.Join(" ", kept)}");
        Assert.Equal(["admin", "staff"], kept);
    }

    [Fact]
    public void The_surviving_admin_role_has_no_host_and_the_admin_host_stays_tenant_admin()
    {
        var set = TenantRoleSet.FromDocuments("shop", [Doc("admin", 1)]);
        var merged = set.Materialize([new Role { Slug = "customer", CatalogSlug = "customer", SortOrder = 0 }]);
        var admin = Assert.Single(merged, r => r.Slug == "admin");
        Assert.Equal("admin", admin.CatalogSlug);
        var tenant = new Tenant { Id = "shop", Roles = [.. merged] };
        Assert.False(TenantResolverMiddleware.HoldsRoleHost(tenant, "tenant_admin"));
        Assert.Equal(["customer"], TenantResolverMiddleware.HostedRoles(tenant));
        Assert.Equal("tenant_admin", RoleHostNames.CatalogSlugOf("admin"));
        Assert.False(RoleHostNames.ByCatalogSlug.ContainsKey("admin"));
    }

    /// <summary>القاعِدَتانِ تَفتَرِقانِ بِرَمزٍ واحِد: لِكُلِّ اسمِ مُضيفِ دَورٍ في المُعجَمِ ولِأَسماءٍ سِواها — قِراءَةُ المُخَزَّنِ = بابُ الكِتابَةِ ناقِصاً
    /// <c>slug_shadows_role_host</c> بِحَرفِه وتَرتيبِه. يَطبَعُ ما عَدَّه.</summary>
    [Fact]
    public void The_stored_rules_are_the_write_rules_without_the_role_host_shadow()
    {
        var slugs = RoleHostNames.Values.Concat(["staff", "admins", "tenant_admin", "Bad-Slug", "x"]).Distinct().ToArray();
        var shadows = 0;
        foreach (var slug in slugs)
        {
            var d = RoleDefinitionLoader.ParseDefinition(Json(slug));
            var write = RoleDefinitionValidator.ValidateTenantDefinition(d).Select(v => (v.Code, v.MessageAr)).ToList();
            var stored = RoleDefinitionValidator.ValidateStoredTenantDefinition(d).Select(v => (v.Code, v.MessageAr)).ToList();
            if (write.Any(v => v.Code == RoleDefinitionValidator.SlugShadowsRoleHost)) shadows++;
            output.WriteLine($"{slug}: الكِتابَة [{string.Join(" ", write.Select(v => v.Code))}] · المُخَزَّن [{string.Join(" ", stored.Select(v => v.Code))}]");
            Assert.Equal(write.Where(v => v.Code != RoleDefinitionValidator.SlugShadowsRoleHost), stored);
            Assert.DoesNotContain(stored, v => v.Code == RoleDefinitionValidator.SlugShadowsRoleHost);
        }
        Assert.True(slugs.Length >= 12, $"فَحصٌ أَعمى: {slugs.Length}");
        Assert.Equal(1, shadows);   // admin وَحدَه — اسمُ مُضيفٍ لَيسَ اسمَ كَتالوجِه
    }

    /// <summary>
    /// <para><b>مَن يَسأَلُ أَيَّهُما</b> (ماسِحٌ بِأَسماءِ الأَبواب): الحِزامُ <c>FromDocuments</c> يَسأَلُ <c>ValidateStoredTenantDefinition</c> ولا يَسأَلُ بابَ الكِتابَة؛
    /// واقتِراحُ الوَكيلِ (<c>DefineRoleAsync</c>) واعتِمادُ المُشرِف (<c>ValidateBeforeApprove</c>) يَسأَلانِ <c>ValidateTenantDefinition</c>. وكُلُّ قارِئٍ آخَرَ لِلقاعِدَتَينِ
    /// في الكودِ يُعَدُّ ويُطبَع — فَبابٌ رابِعٌ يُضافُ بِلا قَرارٍ يُحَمِّرُه.</para>
    /// </summary>
    [Fact]
    public void The_read_belt_asks_the_stored_rules_and_the_two_write_doors_ask_the_write_rules()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        string Body(string file, string from, string to)
        {
            var src = File.ReadAllText(Path.Combine(root, file));
            var body = src[src.IndexOf(from, StringComparison.Ordinal)..];
            return body[..body.IndexOf(to, StringComparison.Ordinal)];
        }
        var belt = Body("libs/kits/Roles/ACommerce.Kit.Roles.Core/TenantRoleDefinition.cs", "public static TenantRoleSet FromDocuments(", "return accepted.Count");
        var approve = Body("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/TenantRoleService.cs", "ValidateBeforeApprove(TenantRoleDefinition doc)", "AlreadyApprovedAr");
        var propose = Body("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/AgentService.cs", "DefineRoleAsync(JsonElement root", "_store.QuerySession()");
        Assert.Contains("RoleDefinitionValidator.ValidateStoredTenantDefinition(parsed)", belt, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateTenantDefinition(", belt, StringComparison.Ordinal);
        Assert.Contains("RoleDefinitionValidator.ValidateTenantDefinition(parsed)", approve, StringComparison.Ordinal);
        Assert.Contains(".ValidateTenantDefinition(parsed)", propose, StringComparison.Ordinal);

        var callers = Directory.GetFiles(Path.Combine(root, "libs"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "apps"), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(x => !x.Line.StartsWith("//", StringComparison.Ordinal)
                        && (x.Line.Contains("RoleDefinitionValidator.ValidateTenantDefinition(", StringComparison.Ordinal)
                            || x.Line.Contains(".ValidateTenantDefinition(parsed)", StringComparison.Ordinal) && x.File == "AgentService.cs"
                            || x.Line.Contains("ValidateStoredTenantDefinition(", StringComparison.Ordinal)))
            .Where(x => x.File != "RoleDefinitionValidator.cs")
            .Select(x => x.File).OrderBy(f => f, StringComparer.Ordinal).ToList();
        output.WriteLine($"قُرّاءُ القاعِدَتَين: {callers.Count} — {string.Join(" ", callers)}");
        Assert.Equal(["AgentService.cs", "TenantRoleDefinition.cs", "TenantRoleService.cs"], callers);
    }
}
