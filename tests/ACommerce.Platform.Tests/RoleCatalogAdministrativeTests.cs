using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>مَتجَرٌ بِلا أَدوارٍ لا يُعطي صَلاحِيَّةً إدارِيَّةً لِأَحَد</b> (‏2026-10-28، ‏ADR-040، قَرارُ المالِك د62 — الخيار ب: «لا تَتَهاوَن في
/// الأَدوار، خُصوصاً وأَنَّه لا يوجَدُ مَتجَرٌ فِعلِيٌّ في وَضعِ الإنتاجِ إلى الآن»). ثَلاثَةُ حُدودٍ تُقاسُ هُنا ولا تُوعَد (القاعِدَة ٢):</para>
/// <list type="number">
///   <item><b>المَعجَمُ الإدارِيُّ مَقيسٌ مِن الكاتالوج</b>: <see cref="PermissionCatalog.Administrative"/> يُساوي ما يَمنَحُه <c>tenant_admin</c>
///   وَحدَه مِن تَعريفاتِ الأَدوار — فَصَلاحِيَّةٌ إدارِيَّةٌ جَديدَةٌ لا تَدخُلُ صامِتَةً في «صَلاحِيّاتِ العُضو» الَّتي يُعطيها صِفرُ الأَدوار.</item>
///   <item><b>والدالَّةُ تُطَبِّقُه</b>: <see cref="RolePermissions.Has"/> عَلى صِفرِ أَدوارٍ تَرُدُّ كُلَّ إدارِيَّةٍ وتُعطي كُلَّ ما سِواها.</item>
///   <item><b>ولا بَوّابَةَ تَختَصِرُ الطَريق</b>: كُلُّ فَرعِ <c>Roles.Count == 0</c> في الحُرّاسِ الثَلاثَة (<c>PermissionFilter</c> و
///   <c>GatePipeline</c> و<c>HasPermissionAsync</c>) يَسأَلُ الدالَّةَ نَفسَها — كانَت ثَلاثَتُها <c>return true</c>/<c>next</c> «legacy mode».</item>
/// </list>
/// </summary>
public class RoleCatalogAdministrativeTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>ما يَمنَحُه دَورُ الإدارَةِ وَحدَه، وما يُعلِنُه المَعجَمُ إدارِيّاً ولا يُطابِقُه — بِلا مَصدَرٍ ولا ثابِت، فَيُحقَنُ بِعَيِّنَة.</summary>
    internal static IReadOnlyList<string> AdministrativeMismatches(
        IEnumerable<(string Role, IReadOnlyCollection<string> Permissions)> definitions, IEnumerable<string> declared, string adminRole = "tenant_admin")
    {
        var defs = definitions.ToList();
        var adminOnly = defs.Where(d => d.Role == adminRole).SelectMany(d => d.Permissions)
            .Where(p => !defs.Any(o => o.Role != adminRole && o.Permissions.Contains(p)))
            .ToHashSet(StringComparer.Ordinal);
        var named = declared.ToHashSet(StringComparer.Ordinal);
        return adminOnly.Except(named).Select(p => $"{p}: يَمنَحُه {adminRole} وَحدَه ولا يُسَمّيه المَعجَمُ الإدارِيّ")
            .Concat(named.Except(adminOnly).Select(p => $"{p}: مُعلَنٌ إدارِيّاً ودَورُ عُضوٍ يَحمِلُه أَو لا يَمنَحُه {adminRole}"))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void The_administrative_permissions_are_exactly_those_only_tenant_admin_grants_in_the_catalog()
    {
        var defs = RoleCatalog.Definitions.Select(d => (d.Slug, (IReadOnlyCollection<string>)d.Permissions.ToList())).ToList();
        var mismatches = AdministrativeMismatches(defs, PermissionCatalog.Administrative);
        output.WriteLine($"تَعريفاتُ الأَدوار: {defs.Count} · صَلاحِيّاتُ المَعجَم: {PermissionCatalog.All.Count} · الإدارِيَّة: {PermissionCatalog.Administrative.Count} ({string.Join(", ", PermissionCatalog.Administrative)}) · خِلاف: {mismatches.Count}");
        Assert.True(defs.Count >= 5 && PermissionCatalog.All.Count >= 5, $"أَداةٌ عَمياء: {defs.Count} تَعريفاً و{PermissionCatalog.All.Count} صَلاحِيَّة.");
        Assert.Contains(defs, d => d.Slug == "tenant_admin");
        Assert.NotEmpty(PermissionCatalog.Administrative);
        Assert.True(mismatches.Count == 0, string.Join("؛ ", mismatches));
    }

    /// <summary>القاعِدَة ١٠ — الفاحِصُ يُحمِرُّ بِعَيِّنَتَين مَحقونَتَين ويَخضَرُّ بِالمُطابِقَة.</summary>
    [Fact]
    public void The_administrative_checker_goes_red_on_injected_samples()
    {
        var defs = new List<(string, IReadOnlyCollection<string>)>
        {
            ("tenant_admin", new[] { "tenant.manage", "tenant.audit", "listing.create" }),
            ("vendor",       new[] { "listing.create" }),
        };
        Assert.Equal(new[] { "tenant.audit: يَمنَحُه tenant_admin وَحدَه ولا يُسَمّيه المَعجَمُ الإدارِيّ" },
            AdministrativeMismatches(defs, new[] { "tenant.manage" }));
        Assert.Equal(new[] { "listing.create: مُعلَنٌ إدارِيّاً ودَورُ عُضوٍ يَحمِلُه أَو لا يَمنَحُه tenant_admin" },
            AdministrativeMismatches(defs, new[] { "tenant.manage", "tenant.audit", "listing.create" }));
        Assert.Empty(AdministrativeMismatches(defs, new[] { "tenant.manage", "tenant.audit" }));
    }

    [Theory]
    [InlineData("tenant.manage", true)]
    [InlineData("listing.create", false)]
    [InlineData("chat.respond", false)]
    [InlineData("Tenant.Manage", false)]
    [InlineData("", false)]
    public void IsAdministrative_is_the_closed_list_case_sensitive(string permission, bool expected)
        => Assert.Equal(expected, PermissionCatalog.IsAdministrative(permission));

    /// <summary>صِفرُ الأَدوار: الإدارِيَّةُ تُرَدُّ لِكُلِّ دَور، وكُلُّ صَلاحِيَّةٍ سِواها تُمنَحُ لِكُلِّ دَور — والمَتجَرُ ذو الأَدوارِ كَما كان.</summary>
    [Fact]
    public void Has_on_zero_roles_refuses_each_administrative_permission_and_grants_every_other()
    {
        var none = Array.Empty<Role>();
        string?[] roles = { null, "", "tenant_admin", "customer" };
        var cells = 0;
        foreach (var role in roles)
            foreach (var p in PermissionCatalog.All)
            {
                cells++;
                Assert.Equal(!PermissionCatalog.IsAdministrative(p), RolePermissions.Has(none, role, p));
            }
        output.WriteLine($"خاناتٌ مَفحوصَة: {cells}");
        Assert.True(cells >= 30, $"أَداةٌ عَمياء: {cells}");

        var admin = RoleCatalog.InstantiateRole(RoleCatalog.Find("tenant_admin")!);
        Assert.True(RolePermissions.Has(new[] { admin }, "tenant_admin", "tenant.manage"));
    }

    // ─── الحُرّاسُ الثَلاثَة: لا فَرعَ «legacy mode» يَختَصِرُ الدالَّة ──────────────────────────────

    /// <summary>كُلُّ سَطرِ <c>Roles.Count == 0</c> في المَصدَر يَتبَعُه في الجُملَةِ نَفسِها (إلى <c>;</c> أَو <c>{</c>) سُؤالُ
    /// <c>RolePermissions.Has</c> — وإلّا فَهو اختِصار. يُرجِعُ (عَدَدَ الأَسطُر، المُخالِفَة).</summary>
    internal static (int Sites, IReadOnlyList<string> Shortcuts) ZeroRoleShortcuts(string code)
    {
        var sites = 0;
        var bad = new List<string>();
        foreach (Match m in Regex.Matches(code, @"if \((?:tenant\.)?Roles\.Count == 0\)"))
        {
            sites++;
            var rest = code[(m.Index + m.Length)..];
            var end = rest.IndexOfAny(new[] { ';', '{' });
            var head = end < 0 ? rest : rest[..(end + 1)];
            var block = head.EndsWith('{') ? rest[..Math.Max(0, rest.IndexOf('}'))] : head;
            if (!block.Contains("RolePermissions.Has(", StringComparison.Ordinal))
                bad.Add(Regex.Replace(m.Value + head, @"\s+", " ").Trim());
        }
        return (sites, bad);
    }

    public static readonly TheoryData<string> GateFiles = new()
    {
        $"{TemplateRoot}/Gates/PermissionFilter.cs",
        $"{TemplateRoot}/Gates/GatePipeline.cs",
        $"{TemplateRoot}/MarketplaceTemplateExtensions.cs",
    };

    [Theory]
    [MemberData(nameof(GateFiles))]
    public void Every_zero_role_branch_in_a_permission_gate_asks_the_permission_function(string file)
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, file)));
        var gate = file.EndsWith("MarketplaceTemplateExtensions.cs", StringComparison.Ordinal)
            ? code[code.IndexOf("private static async Task<bool> HasPermissionAsync(", StringComparison.Ordinal)..]
            : code;
        // ‏(‏2026-10-28، د42) حَدُّ المَسحِ: صارَت `internal` لِيَقرَأَها PermissionFilter — والحَدُّ هُوَ هُوَ.
        if (!ReferenceEquals(gate, code)) gate = gate[..gate.IndexOf("internal static async Task<ACommerce.Kit.Tenants.Tenant?> LoadTenantWithRolesAsync(", StringComparison.Ordinal)];
        var (sites, shortcuts) = ZeroRoleShortcuts(gate);
        output.WriteLine($"{Path.GetFileName(file)}: فُروعُ صِفرِ الأَدوار {sites} · اختِصار {shortcuts.Count}");
        Assert.True(sites >= 1, $"أَداةٌ عَمياء: لا فَرعَ صِفرٍ في {file}.");
        Assert.True(shortcuts.Count == 0, string.Join("؛ ", shortcuts));
    }

    /// <summary>القاعِدَة ١٠ — الأَشكالُ الثَلاثَةُ الَّتي كانَت (‏<c>return true</c>، <c>return await next(ctx)</c>، <c>return;</c>) تُعَدُّ اختِصاراً،
    /// وفَرعٌ يَسأَلُ الدالَّةَ — بِسَطرٍ أَو بِكُتلَة — لا يُعَدّ.</summary>
    [Theory]
    [InlineData("if (tenant.Roles.Count == 0) return true;   // legacy mode", 1, 1)]
    [InlineData("if (tenant.Roles.Count == 0) return await next(ctx);", 1, 1)]
    [InlineData("if (tenant.Roles.Count == 0) return;", 1, 1)]
    [InlineData("if (tenant.Roles.Count == 0) return RolePermissions.Has(tenant.Roles, null, p);", 1, 0)]
    [InlineData("if (tenant.Roles.Count == 0)\n    return RolePermissions.Has(tenant.Roles, null, _permission)\n        ? await next(ctx) : Results.StatusCode(403);", 1, 0)]
    [InlineData("if (tenant.Roles.Count == 0)\n{\n    if (RolePermissions.Has(tenant.Roles, null, perm.Permission)) return;\n    throw new X();\n}", 1, 0)]
    [InlineData("var n = tenant.Roles.Count;", 0, 0)]
    public void The_zero_role_shortcut_scanner_counts_every_shape(string sample, int sites, int shortcuts)
    {
        var (s, bad) = ZeroRoleShortcuts(sample);
        Assert.Equal((sites, shortcuts), (s, bad.Count));
    }
}
