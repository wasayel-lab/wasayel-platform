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
/// <para><b>والحارِسُ الرابِع (‏2026-10-29، مُراجَعَةُ تَيّارِ S1 الجَولَة ٢؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: <c>GatedPage.razor</c> — حارِسُ الصَفحَةِ
/// المَحروسَةِ بِـ<c>Permission="…"</c> — لَم يَكُن في <see cref="GateFiles"/>، وكانَ يَسأَلُ البابَ داخِلَ <c>if (tenant is { Roles.Count: > 0 })</c> وَحدَه
/// فَيَفتَحُ صِفرُ الأَدوارِ كُلَّ صَلاحِيَّةٍ بِلا سُؤال، والماسِحُ لا يَرى ذلكَ الشَكل (تَوصيفُه <c>3e44314</c>/<c>6bd2c9c</c>). صارَ في القائِمَة، وصارَ الماسِحُ
/// يَعُدُّ <b>الشَكلَ المَوجِب</b> (<c>Roles.Count &gt; 0</c> / <c>Roles.Count: &gt; 0</c>) اختِصاراً مَتى حَمَلَ جِسمُه سُؤالَ الدالَّة — فَالسُؤالُ لِلمَتجَرِ ذي
/// الأَدوارِ وَحدَه — ويَقبَلُ <c>PermissionDoor.Opens</c> سُؤالاً لِأَنَّها سَطرٌ واحِدٌ يُنادي <c>RolePermissions.Has</c> (مُثَبَّتٌ بِحَرفِه أَدناه).</para>
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
    /// <c>RolePermissions.Has</c> — وإلّا فَهو اختِصار. يُرجِعُ (عَدَدَ الأَسطُر، المُخالِفَة).
    /// <para>(‏2026-10-29، الحارِسُ الرابِع): و<c>PermissionDoor.Opens(</c> سُؤالٌ لِلدالَّةِ نَفسِها (<see cref="The_door_the_scanner_accepts_as_a_question_is_one_call_to_the_permission_function"/>)؛
    /// وكُلُّ شَرطِ <c>if</c> فيه <c>Roles.Count &gt; 0</c> أَو <c>Roles.Count: &gt; 0</c> مَوضِعٌ يُعَدّ، واختِصارٌ مَتى حَمَلَ جِسمُه المَحكومُ (الكُتلَةُ المُتَوازِنَةُ أَو
    /// الجُملَةُ إلى <c>;</c>) سُؤالَ الدالَّة: السُؤالُ إذَن لِلمَتجَرِ ذي الأَدوارِ وَحدَه، وصِفرُ الأَدوارِ يَعبُرُ إلى ما بَعدَه بِلا سُؤال. فَالحارِسُ يَكتُبُ فَرعَ
    /// الصِفرِ صَريحاً (<c>== 0</c>) ولا يَترُكُه لِلسُقوط.</para></summary>
    internal static (int Sites, IReadOnlyList<string> Shortcuts) ZeroRoleShortcuts(string code)
    {
        static bool Asks(string text) => text.Contains("RolePermissions.Has(", StringComparison.Ordinal)
                                         || text.Contains("PermissionDoor.Opens(", StringComparison.Ordinal);
        var sites = 0;
        var bad = new List<string>();
        foreach (Match m in Regex.Matches(code, @"if \((?:tenant\.)?Roles\.Count == 0\)"))
        {
            sites++;
            var rest = code[(m.Index + m.Length)..];
            var end = rest.IndexOfAny(new[] { ';', '{' });
            var head = end < 0 ? rest : rest[..(end + 1)];
            var block = head.EndsWith('{') ? rest[..Math.Max(0, rest.IndexOf('}'))] : head;
            if (!Asks(block))
                bad.Add(Regex.Replace(m.Value + head, @"\s+", " ").Trim());
        }
        foreach (Match m in Regex.Matches(code, @"if \((?<cond>[^;{}]*?(?:\{\s*)?)(?:\w+\.)*Roles\.Count(?::\s*>|\s*>)\s*0"))
        {
            sites++;
            var cond = m.Groups["cond"].Value;
            var depth = 1 + cond.Count(c => c == '(') - cond.Count(c => c == ')');
            var i = m.Index + m.Length;
            for (; i < code.Length && depth > 0; i++)
                depth += code[i] == '(' ? 1 : code[i] == ')' ? -1 : 0;
            var bodyAt = i;
            while (bodyAt < code.Length && char.IsWhiteSpace(code[bodyAt])) bodyAt++;
            string body;
            if (bodyAt < code.Length && code[bodyAt] == '{')
            {
                var braces = 0;
                var j = bodyAt;
                for (; j < code.Length; j++)
                {
                    braces += code[j] == '{' ? 1 : code[j] == '}' ? -1 : 0;
                    if (braces == 0) break;
                }
                body = code[bodyAt..Math.Min(code.Length, j + 1)];
            }
            else
            {
                var semi = code.IndexOf(';', bodyAt);
                body = semi < 0 ? code[bodyAt..] : code[bodyAt..(semi + 1)];
            }
            if (Asks(body))
                bad.Add(Regex.Replace(code[m.Index..Math.Min(code.Length, i)], @"\s+", " ").Trim() + " — السُؤالُ داخِلَ الفَرعِ المَوجِبِ وَحدَه");
        }
        return (sites, bad);
    }

    public static readonly TheoryData<string> GateFiles = new()
    {
        $"{TemplateRoot}/Gates/PermissionFilter.cs",
        $"{TemplateRoot}/Gates/GatePipeline.cs",
        $"{TemplateRoot}/MarketplaceTemplateExtensions.cs",
        // ‏(‏2026-10-29، الحارِسُ الرابِع): صَفحَةٌ بِـ<GatedPage Permission="…"> — ADR-040 §٢‑3 بِسَطرِه المُؤَرَّخ.
        $"{TemplateRoot}/Components/GatedPage.razor",
    };

    [Theory]
    [MemberData(nameof(GateFiles))]
    public void Every_zero_role_branch_in_a_permission_gate_asks_the_permission_function(string file)
    {
        var text = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, file));
        // ‏(‏2026-10-29): تَعليقُ Razor (`@* … *@`) يُنزَعُ قَبلَ تَعليقِ C# — مِثالُ الاستِعمالِ في رَأسِ GatedPage لا يُعَدّ.
        if (file.EndsWith(".razor", StringComparison.Ordinal)) text = WriteEndpointGuardTests.StripMarkupComments(text);
        var code = WriteEndpointGuardTests.StripComments(text);
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
    // ‏(‏2026-10-29، الحارِسُ الرابِع) الشَكلُ المَوجِبُ: شَكلُ GatedPage الَّذي كان — السُؤالُ داخِلَ «لِلمَتجَرِ أَدوار» وَحدَه — اختِصار، بِكُتلَةٍ أَو بِجُملَة؛
    // ومَوجِبٌ لا يَحمِلُ السُؤالَ (قِراءَةُ الدَورِ وَحدَها) لَيسَ اختِصاراً؛ وفَرعُ الصِفرِ الَّذي يَسأَلُ البابَ يُقبَل، والَّذي يُجيزُ بِلا سُؤالٍ يُرَدّ.
    [InlineData("if (tenant is { Roles.Count: > 0 })\n{\n    var storedRole = await Accounts.ActiveRoleAsync(Slug, uid2);\n    if (!PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission))\n    {\n        _state = GateState.Forbidden;\n        return;\n    }\n}\n_state = GateState.Allowed;", 1, 1)]
    [InlineData("if (tenant.Roles.Count > 0 && Auth.UserId.HasValue)\n    return RolePermissions.Has(tenant.Roles, role, p);", 1, 1)]
    [InlineData("if (tenant.Roles.Count > 0) storedRole = await Accounts.ActiveRoleAsync(Slug, uid2);\nif (!PermissionDoor.Opens(tenant.Roles, Role, storedRole, Permission)) return;", 1, 0)]
    [InlineData("if (tenant.Roles.Count == 0)\n{\n    if (!PermissionDoor.Opens(tenant.Roles, Role, null, Permission)) { Forbid(); return; }\n}", 1, 0)]
    [InlineData("if (tenant.Roles.Count == 0) { _state = GateState.Allowed; return; }", 1, 1)]
    public void The_zero_role_shortcut_scanner_counts_every_shape(string sample, int sites, int shortcuts)
    {
        var (s, bad) = ZeroRoleShortcuts(sample);
        Assert.Equal((sites, shortcuts), (s, bad.Count));
    }

    /// <summary><b>ما يَقبَلُه الماسِحُ سُؤالاً</b> (‏2026-10-29): <c>PermissionDoor.Opens</c> سَطرٌ واحِدٌ هُوَ نِداءُ <c>RolePermissions.Has</c> بِدَورٍ تَحسِمُه
    /// <c>EffectiveRole.Resolve</c> — فَإن صارَ جِسمُها غَيرَ ذلك (فَرعٌ يُجيزُ قَبلَ النِداء) حَمِرَ هذا قَبلَ أَن يَصيرَ قَبولُ الماسِحِ لَها ثَغرَة.
    /// وجَوابُها عَلى صِفرِ الأَدوارِ جَوابُ الدالَّةِ لِكُلِّ صَلاحِيَّةٍ في المَعجَمِ ولِكُلِّ دَور.</summary>
    [Fact]
    public void The_door_the_scanner_accepts_as_a_question_is_one_call_to_the_permission_function()
    {
        var door = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, TemplateRoot, "Gates", "PermissionDoor.cs")));
        var body = Regex.Matches(door, @"public static bool Opens\([^)]*\)\s*(?<body>=>[^;]*;)");
        output.WriteLine($"Opens في PermissionDoor.cs: {body.Count} · {(body.Count == 1 ? Regex.Replace(body[0].Groups["body"].Value, @"\s+", " ") : "—")}");
        Assert.Single(body);
        Assert.Equal("=> RolePermissions.Has(tenantRoles, EffectiveRole.Resolve(null, false, urlRole, storedRole), permission);",
            Regex.Replace(body[0].Groups["body"].Value, @"\s+", " "));

        var none = Array.Empty<Role>();
        var cells = 0;
        foreach (var role in new string?[] { null, "", "customer", "tenant_admin" })
            foreach (var p in PermissionCatalog.All)
            {
                cells++;
                Assert.Equal(RolePermissions.Has(none, null, p),
                    ACommerce.Templates.Customer.Marketplace.Gates.PermissionDoor.Opens(none, role, role, p));
            }
        Assert.True(cells >= 30, $"أَداةٌ عَمياء: {cells}");
    }
}
