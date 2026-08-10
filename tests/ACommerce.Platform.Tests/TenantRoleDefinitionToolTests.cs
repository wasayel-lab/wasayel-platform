using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── أَداة define_role وطَبَقَة الوَثائِق فَوق الكاتالوج ────────────────
// ما يُثبِتُه هذا المِلَفّ (وما لا يُثبِتُه): كُلّ ما هُنا **نَقِيّ** —
// نَصّ ← تَعريف ← مُصادِق ← لَقطَة. لا Marten ولا HTTP. الجُزء الَّذي
// يَلزَمُه خادِم حَيّ (الوَثيقَة تُكتَب، فَتُعتَمَد، فَتَظهَر البِطاقَة
// بِلا إعادَة تَشغيل) مُبرهَن بِالبُرهان الحَيّ لا هُنا — وهذا الفَصل
// مَقصود: الوَحدَويّ يَحرُس القَواعِد، والحَيّ يَحرُس الوَصل.

public class TenantRoleDefinitionToolTests
{
    /// <summary>تَعريف «خَيّاط» — كُلّ قيمَة فيه مِن مَعجَم مُغلَق قائِم:
    /// أَربَع صَلاحِيّات مِن الثَّمانِ، ونَوعا حَقل مِن السَبعَة، وخَمس
    /// فَتَحات تَركيب مِن أَسماء المُكَوِّنات القائِمَة. لا مُكَوِّن
    /// جَديد ولا صَلاحِيَّة جَديدَة.</summary>
    public const string KhayyatJson = """
    {
      "slug": "khayyat",
      "icon": "🧵",
      "homeRoute": "/me/listings",
      "label": { "ar": "خَيّاط", "en": null },
      "description": { "ar": "حِرَفيّ يَخيط ويُفَصِّل — يَنشُر أَعمالَه ويَرُدّ عَلى الطَلَبات.", "en": null },
      "permissions": ["listing.create", "listing.edit", "listing.delete", "chat.respond"],
      "fields": [
        {
          "code": "workshop_name",
          "label": { "ar": "اسم الوَرشَة", "en": null },
          "type": "Text",
          "isRequired": true,
          "options": []
        },
        {
          "code": "specialty",
          "label": { "ar": "التَخَصُّص", "en": null },
          "type": "SingleSelect",
          "isRequired": false,
          "options": [
            { "value": "men",   "label": { "ar": "رِجاليّ", "en": null } },
            { "value": "women", "label": { "ar": "نِسائيّ", "en": null } },
            { "value": "abaya", "label": { "ar": "عَبايات", "en": null } }
          ]
        }
      ],
      "composition": {
        "home": "sellerHome",
        "createListing": "defaultCreateForm",
        "nav": "vendorNav",
        "explore": "defaultExplore",
        "publicProfile": "vendorProfile",
        "extras": []
      },
      "dealPatternAffinity": null
    }
    """;

    private static TenantRoleDefinition Approved(string slug, string json) => new()
    {
        Id = slug, Slug = slug, DefinitionJson = json,
        Status = TenantRoleStatuses.Approved,
        CreatedBy = "agent:define_role", CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    // ─── ١. المَسار الَّذي تَسلُكُه الأَداة: نَصّ ← تَعريف ← مُصادِق ──

    [Fact]
    public void KhayyatDefinition_PassesTheTenantGate()
    {
        var d = RoleDefinitionLoader.ParseDefinition(KhayyatJson);

        Assert.Equal("khayyat", d.Slug);
        Assert.Equal("خَيّاط", d.Label.Ar);
        Assert.Empty(RoleDefinitionValidator.ValidateTenantDefinition(d));
    }

    [Fact]
    public void PermissionOutsideVocabulary_IsRejectedByItsStableCode()
    {
        var json = KhayyatJson.Replace("\"listing.create\"", "\"listing.teleport\"");
        var d = RoleDefinitionLoader.ParseDefinition(json);

        var codes = RoleDefinitionValidator.ValidateTenantDefinition(d)
            .Select(v => v.Code).ToArray();

        Assert.Contains("permission_out_of_vocabulary", codes);
    }

    [Fact]
    public void ComponentOutsideVocabulary_IsRejectedByItsStableCode()
    {
        var json = KhayyatJson.Replace("\"sellerHome\"", "\"tailorHome\"");
        var d = RoleDefinitionLoader.ParseDefinition(json);

        Assert.Contains("composition_component_out_of_vocabulary",
            RoleDefinitionValidator.ValidateTenantDefinition(d).Select(v => v.Code));
    }

    /// <summary>قاعِدَة عَدَم الظِلّ — الزِيادَة الوَحيدَة عَلى بَوّابَة
    /// المَنصَّة. ويُفحَص مَعَها أَنّ نَفس التَعريف <b>يَجتاز</b>
    /// <c>Validate</c> العادِيَّة، فَالفَرق مَحصور حَيثُ قُصِدَ.</summary>
    [Fact]
    public void SlugThatShadowsTheCatalog_IsRejectedForTenants_ButNotByThePlatformGate()
    {
        var json = KhayyatJson.Replace("\"slug\": \"khayyat\"", "\"slug\": \"vendor\"");
        var d = RoleDefinitionLoader.ParseDefinition(json);

        Assert.Empty(RoleDefinitionValidator.Validate(d));
        Assert.Equal(
            new[] { "slug_shadows_platform_catalog" },
            RoleDefinitionValidator.ValidateTenantDefinition(d).Select(v => v.Code).ToArray());
    }

    // ─── ٢. مُخَطَّط الأَداة ─────────────────────────────────────────────

    // تُفحَص مِن الواجِهَة العامَّة (<c>AgentToolValidator</c>) لا مِن
    // تَعريفات الأَدَوات مُباشَرَةً — فَهي المَسار الَّذي يَمُرّ مِنه
    // الوَكيل فِعلاً، والاختِبار يَقيس ما يَقَع لا ما هو مَكتوب.

    [Fact]
    public void DefineRole_IsARegisteredTool_AndAcceptsAWellShapedPayload()
    {
        var payload = $$"""{"slug":"adwar-demo","definition":{{KhayyatJson}}}""";
        var result = AgentToolValidator.Validate("define_role", payload);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));

        // وأَداة غَير مُسَجَّلَة تُعطي رِسالَة مُتَمَيِّزَة — فَنَجاح
        // أَعلاه ليسَ نَجاحاً بِالصُدفَة.
        Assert.Contains("أَداة غَير مَعروفَة",
            string.Join(" ", AgentToolValidator.Validate("define_roles", payload).Errors));
    }

    [Fact]
    public void DefineRole_RejectsAnUnknownKeyInsideTheDefinition()
    {
        // المِفتاح المَجهول يَسقُط عِندَ المُخَطَّط قَبل أَن يَصِل القارِئ —
        // الطَبَقَتان تَقولان الشَيء نَفسَه، وهذا هو المَقصود.
        var json = KhayyatJson.Replace("\"icon\": \"🧵\"", "\"icon\": \"🧵\", \"color\": \"#fff\"");
        var payload = $$"""{"slug":"adwar-demo","definition":{{json}}}""";

        Assert.False(AgentToolValidator.Validate("define_role", payload).IsValid);
    }

    [Fact]
    public void DefineRole_RejectsADefinitionMissingAMandatoryKey()
    {
        var json = KhayyatJson.Replace("\"icon\": \"🧵\",", "");
        var payload = $$"""{"slug":"adwar-demo","definition":{{json}}}""";

        Assert.False(AgentToolValidator.Validate("define_role", payload).IsValid);
    }

    [Fact]
    public void DefineRole_RejectsAMissingDefinition()
    {
        Assert.False(AgentToolValidator.Validate(
            "define_role", """{"slug":"adwar-demo"}""").IsValid);
    }

    // ─── ٣. اللَقطَة بِوَثيقَة واحِدَة مُعتَمَدَة ─────────────────────────

    [Fact]
    public void OneApprovedDocument_AddsOnTopOfTheCatalog_WithoutShadowingIt()
    {
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[] { Approved("khayyat", KhayyatJson) });

        Assert.Equal(11, set.Definitions.Count);
        Assert.Equal(RoleCatalog.Definitions.Count + 1, set.Definitions.Count);

        // العَشَرَة في مَواضِعِها ونَصِّها — الإضافَة فَوق لا وَسَط.
        for (var i = 0; i < RoleCatalog.Definitions.Count; i++)
            Assert.Equal(RoleCatalog.Definitions[i].Slug, set.Definitions[i].Slug);
        Assert.Equal("khayyat", set.Definitions[^1].Slug);

        Assert.Equal("خَيّاط", set.Find("khayyat")!.Label);
        Assert.Null(RoleCatalog.Find("khayyat"));   // الكاتالوج لَم يُمَسّ
    }

    [Fact]
    public void OneApprovedDocument_FeedsTheRenderDecision()
    {
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[] { Approved("khayyat", KhayyatJson) });
        var c = set.ResolveComposition("khayyat");

        Assert.Equal(RoleComponents.SellerHome, c.Home);
        Assert.Equal(RoleComponents.VendorNav,  c.Nav);
        Assert.Equal(RoleComponents.VendorProfile, c.PublicProfile);

        // والمَسار السّاكِن لا يَعرِفُه — وهذا هو الفَرق الَّذي تُحدِثُه
        // الوَثيقَة، مَقيساً لا مَدَّعىً.
        Assert.Same(RoleCompositionResolver.Fallback,
                    RoleCompositionResolver.Resolve("khayyat"));
    }

    [Fact]
    public void OneApprovedDocument_MaterializesAsARoleOnTopOfTheStoredOnes()
    {
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[] { Approved("khayyat", KhayyatJson) });

        var stored = new List<Role>
        {
            RoleCatalog.InstantiateRole(RoleCatalog.Find("customer")!, 0),
            RoleCatalog.InstantiateRole(RoleCatalog.Find("broker")!,   1),
        };

        var merged = set.Materialize(stored);

        Assert.Equal(3, merged.Count);
        Assert.Equal(new[] { "customer", "broker", "khayyat" }, merged.Select(r => r.Slug).ToArray());

        var k = merged[2];
        Assert.Equal("خَيّاط", k.Label);
        Assert.Equal("khayyat", k.CatalogSlug);     // بِه يُقرَأ التَركيب
        Assert.Equal("/me/listings", k.HomeRoute);
        Assert.Equal(2, k.SortOrder);
        Assert.False(k.IsDefault);
        Assert.Equal(4, k.Permissions.Count);
        Assert.Equal(2, k.Fields.Count);
        Assert.Equal("اسم الوَرشَة", k.Fields[0].Label);
        Assert.Equal(3, k.Fields[1].Options.Count);
    }

    [Fact]
    public void ADocumentThatShadowsTheCatalog_NeverEntersTheSet()
    {
        // حِزام الأَمان الثاني: حَتَّى لَو كُتِبَت الوَثيقَة بِيَد
        // وتَخَطَّت بَوّابَة الكِتابَة، لا تُظَلِّل الكاتالوج.
        var json = KhayyatJson.Replace("\"slug\": \"khayyat\"", "\"slug\": \"vendor\"");
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[] { Approved("vendor", json) });

        Assert.Empty(set.TenantAuthored);
        Assert.Equal("تاجِر", set.Find("vendor")!.Label);   // تَعريف المَنصَّة
    }

    // ─── ٤. تَعداد set_roles في سِياق مُستَأجِر ──────────────────────────

    [Fact]
    public void SetRolesEnum_WidensWithTheTenantsApprovedRoles()
    {
        var set = TenantRoleSet.FromDocuments("adwar-demo", new[] { Approved("khayyat", KhayyatJson) });

        // نَفس الحُمولَة: مَرفوضَة في سِياق المَنصَّة (التَعداد لا
        // يَعرِف الدَور)، مَقبولَة في سِياق المَتجَر الَّذي أَلَّفَه.
        const string payload = """{"slug":"adwar-demo","roles":["customer","khayyat"]}""";

        Assert.False(AgentToolValidator.Validate("set_roles", payload).IsValid);
        Assert.True(AgentToolValidator.Validate("set_roles", payload, set).IsValid,
            string.Join(" | ", AgentToolValidator.Validate("set_roles", payload, set).Errors));
    }

    [Fact]
    public void ZeroDocuments_LeaveTheToolSurfaceUnchanged()
    {
        var zero = TenantRoleSet.FromDocuments("any-tenant", Array.Empty<TenantRoleDefinition>());

        const string catalogPayload = """{"slug":"adwar-demo","roles":["customer","broker"]}""";
        const string authoredPayload = """{"slug":"adwar-demo","roles":["khayyat"]}""";

        // مُستَأجِر بِلا وَثيقَة: نَفس القَبول ونَفس الرَّفض حَرفاً.
        Assert.Equal(
            AgentToolValidator.Validate("set_roles", catalogPayload).IsValid,
            AgentToolValidator.Validate("set_roles", catalogPayload, zero).IsValid);
        Assert.Equal(
            AgentToolValidator.Validate("set_roles", authoredPayload).IsValid,
            AgentToolValidator.Validate("set_roles", authoredPayload, zero).IsValid);
        Assert.False(AgentToolValidator.Validate("set_roles", authoredPayload, zero).IsValid);
    }
}
