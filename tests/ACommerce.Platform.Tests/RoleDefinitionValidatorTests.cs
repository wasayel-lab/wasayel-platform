using ACommerce.Kit.Roles;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بَوّابَة تَعريفات الأَدوار — الاتِّجاهان ────────────────────────
// مُوجَب: التَّعريفات السَبعَة المَقروءَة مِن المِلَفّات تَجتاز.
// سالِب: أَدوار فاسِدَة مُصطَنَعَة تُرفَض بِالرَّمز المَقصود لا بِأَيّ رَمز.
// نَفس مَنهَج DealPatternValidatorTests.

public class RoleDefinitionValidatorTests
{
    // ─── التَّحميل والتَّرتيب ─────────────────────────────────────────
    /// <summary>عَشَرَة تَعريفات بِتَرتيب الفِهرِس. كانَت سَبعَة، وأُلحِقَت
    /// بِها ثَلاثَة أُلِّفَت مِلَفّاتٍ خالِصَة في مَوجَة «تَأليف الأَدوار»
    /// — السَبعَة الأُولى بِأَسمائِها وتَرتيبِها كَما هي.</summary>
    [Fact]
    public void Loads_ten_definitions_in_index_order()
        => Assert.Equal(
            new[] { "customer", "rider", "vendor", "driver", "host", "shipper", "tenant_admin",
                    "broker", "mover", "organizer" },
            RoleCatalog.Definitions.Select(d => d.Slug).ToArray());

    [Fact]
    public void Definitions_and_templates_agree_one_to_one()
        => Assert.Equal(
            RoleCatalog.Definitions.Select(d => d.Slug).ToArray(),
            RoleCatalog.All.Select(t => t.Slug).ToArray());

    [Fact]
    public void FindDefinition_is_case_sensitive_like_Find()
    {
        Assert.NotNull(RoleCatalog.FindDefinition("driver"));
        Assert.Null(RoleCatalog.FindDefinition("Driver"));
        Assert.Null(RoleCatalog.FindDefinition("driver_from_the_future"));
    }

    // ─── مُوجَب ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("customer")]
    [InlineData("rider")]
    [InlineData("vendor")]
    [InlineData("driver")]
    [InlineData("host")]
    [InlineData("shipper")]
    [InlineData("tenant_admin")]
    [InlineData("broker")]
    [InlineData("mover")]
    [InlineData("organizer")]
    public void Shipped_definition_passes(string slug)
    {
        var d = RoleCatalog.FindDefinition(slug)!;
        var violations = RoleDefinitionValidator.Validate(d);
        Assert.True(violations.Count == 0,
            string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));
    }

    // ─── مَعجَم الصَلاحِيّات — مُغلَق ومُستَعمَل بِالكامِل ─────────────
    [Fact]
    public void Permission_vocabulary_is_exactly_the_eight()
        => Assert.Equal(
            new[]
            {
                "chat.respond", "chat.start",
                "listing.browse", "listing.create", "listing.delete", "listing.edit",
                "offer.submit", "tenant.manage",
            },
            PermissionCatalog.All.ToArray());

    [Fact]
    public void Every_granted_permission_is_in_the_vocabulary()
    {
        var granted = RoleCatalog.Definitions.SelectMany(d => d.Permissions).Distinct();
        Assert.All(granted, p => Assert.True(PermissionCatalog.Contains(p), p));
    }

    /// <summary>المَعجَم ليسَ مَحشُوّاً: كُلّ صَلاحِيَّة فيه يَمنَحُها دَور
    /// واحِد عَلى الأَقَلّ. لَو أُضيفَت صَلاحِيَّة مُخترَعَة لِلمَعجَم
    /// وَحدَه، يَسقُط هذا الاختِبار.</summary>
    [Fact]
    public void Every_vocabulary_permission_is_granted_by_some_role()
    {
        var granted = RoleCatalog.Definitions
            .SelectMany(d => d.Permissions).ToHashSet(StringComparer.Ordinal);
        Assert.All(PermissionCatalog.All, p => Assert.True(granted.Contains(p), p));
    }

    // ─── مَعجَم المُكَوِّنات ───────────────────────────────────────────
    [Fact]
    public void Every_composed_component_is_in_the_vocabulary()
    {
        var used = RoleCatalog.Definitions
            .SelectMany(d => d.Composition.AllComponents()).Distinct();
        Assert.All(used, c => Assert.True(RoleComponents.Contains(c), c));
    }

    /// <summary>مُكَوِّن يَتيم مُوَثَّق: <c>RoleHomeHero.razor</c> مَوجود في
    /// الشَجَرَة ولا يُصَيِّرُه أَحَد، ولِذلك لا يُسنَد إلى أَيّ دَور.
    /// هذا الاختِبار يُثَبِّت الدَعوى بَدَل أَن يَترُكَها تَعليقاً.</summary>
    [Fact]
    public void RoleHomeHero_is_declared_but_composed_by_no_role()
    {
        Assert.Contains(RoleComponents.RoleHomeHero, RoleComponents.All);
        Assert.DoesNotContain(
            RoleComponents.RoleHomeHero,
            RoleCatalog.Definitions.SelectMany(d => d.Composition.AllComponents()));
    }

    /// <summary><para>التَّقييمات مُحايِدَة الدَور: نُقطَة اللَمس الوَحيدَة
    /// هي الصَفحَة العامَّة (<c>vendorProfile</c>) — المَوضِع الوَحيد في
    /// المُستودَع الَّذي يَعرِض النُّجوم وعَدّاد التَّقييمات.</para>
    ///
    /// <para><b>وُسِّعَت في مَوجَة «تَأليف الأَدوار»</b>: كانَت
    /// <c>vendor</c> و<c>host</c> وَحدَهُما، وانضَمَّ إلَيهِما
    /// <c>broker</c> و<c>organizer</c> — وكِلاهُما دَور <b>يُختار
    /// بِسُمعَتِه</b> قَبل التَّعاقُد، وهذا بِعَينِه ما تَخدِمُه الصَفحَة
    /// العامَّة. والدَعوى المُثَبَّتَة هُنا لَم تَتَبَدَّل: <b>مَن لا
    /// يَعرِض عَمَلَه لِلعامَّة لا صَفحَة عامَّة لَه</b> — <c>customer</c>
    /// و<c>rider</c> و<c>driver</c> و<c>shipper</c> و<c>mover</c>
    /// و<c>tenant_admin</c> كُلُّهُم بِـ <c>null</c> كَما كانوا.</para></summary>
    [Fact]
    public void Only_reputation_facing_roles_have_a_public_profile()
        => Assert.Equal(
            new[] { "vendor", "host", "broker", "organizer" },
            RoleCatalog.Definitions
                .Where(d => d.Composition.PublicProfile is not null)
                .Select(d => d.Slug).ToArray());

    // ─── انجِذاب نَمَط الصَفقَة ───────────────────────────────────────
    /// <summary><para>الانجِذاب المُسنَد في المِلَفّات، والبَقِيَّة
    /// <c>null</c>. وهو <b>تَثبيت لِعَدَم التَماثُل المُوَثَّق</b>:
    /// <c>shipper</c> بِلا انجِذاب رَغم أَنَّه دَور سائِق في كُلّ فَتحَة
    /// تَركيب — لِأَنّ <c>PatternFromTenant</c> لَم يَكُن يَعُدُّه
    /// <c>trip</c>، وتَصحيحُه تَغيير سُلوك لا نَقل.</para>
    ///
    /// <para><b>وانضَمَّ <c>mover</c> رابِعاً</b> في مَوجَة «تَأليف
    /// الأَدوار» بِـ <c>trip</c> — وهو <b>البُرهان بِعَينِه</b> عَلى أَنّ
    /// إضافَة دَور يَجُرّ نَمَطاً صارَت سَطراً في مِلَفّ لا شَرطاً في كود:
    /// الثَّلاثَة الأُولى بِأَسمائِها وقِيَمِها وتَرتيبِها كَما هي.</para></summary>
    [Fact]
    public void Deal_pattern_affinity_is_assigned_only_by_file()
        => Assert.Equal(
            new[] { ("rider", "trip"), ("driver", "trip"), ("host", "rental"),
                    ("mover", "trip") },
            RoleCatalog.Definitions
                .Where(d => d.DealPatternAffinity is not null)
                .Select(d => (d.Slug, d.DealPatternAffinity!)).ToArray());

    [Fact]
    public void Every_assigned_affinity_is_in_the_vocabulary()
        => Assert.All(
            RoleCatalog.Definitions
                .Select(d => d.DealPatternAffinity)
                .Where(a => a is not null),
            a => Assert.True(RoleDealPatternAffinity.Contains(a!), a));

    /// <summary>تَرتيب الغَلَبَة مُعلَن وبِترتيبِه — <c>trip</c> قَبل
    /// <c>rental</c>، وقيمَة الراحَة خارِج المَعجَم المُسنَد.</summary>
    [Fact]
    public void Affinity_priority_and_fallback_are_declared()
    {
        Assert.Equal(new[] { "trip", "rental" }, RoleDealPatternAffinity.Priority.ToArray());
        Assert.Equal(new[] { "trip", "rental" }, RoleDealPatternAffinity.All.ToArray());
        Assert.Equal("marketplace", RoleDealPatternAffinity.Fallback);
        Assert.False(RoleDealPatternAffinity.Contains(RoleDealPatternAffinity.Fallback));
    }

    // ─── سالِب — كُلّ رَمز خَرق بِمُدخَلِه المَقصود ───────────────────
    [Theory]
    [MemberData(nameof(CorruptCases))]
    public void Corrupt_definition_is_rejected_with_code(
        string expectedCode, RoleDefinition corrupt)
    {
        var codes = RoleDefinitionValidator.Validate(corrupt).Select(v => v.Code).ToArray();
        Assert.Contains(expectedCode, codes);
        Assert.False(RoleDefinitionValidator.IsValid(corrupt));
    }

    public static TheoryData<string, RoleDefinition> CorruptCases()
    {
        var data = new TheoryData<string, RoleDefinition>();

        data.Add("slug_empty", Sound() with { Slug = "  " });
        data.Add("slug_pattern", Sound() with { Slug = "Customer" });
        data.Add("slug_pattern", Sound() with { Slug = "1driver" });
        data.Add("slug_pattern", Sound() with { Slug = "tenant-admin" });
        data.Add("icon_missing", Sound() with { Icon = "" });

        data.Add("home_route_malformed", Sound() with { HomeRoute = "explore" });
        data.Add("home_route_malformed", Sound() with { HomeRoute = "/me listings" });
        data.Add("home_route_malformed", Sound() with { HomeRoute = "/explore?city=1" });

        data.Add("localized_arabic_missing", Sound() with { Label = new LocalizedText(null, "Driver") });
        data.Add("localized_arabic_missing", Sound() with { Description = new LocalizedText("   ") });
        data.Add("localized_arabic_missing", Sound() with
        {
            Fields = [Field() with { Label = new LocalizedText(null) }],
        });
        data.Add("localized_arabic_missing", Sound() with
        {
            Fields = [SelectField() with
            {
                Options = [new RoleFieldOptionDefinition { Value = "economy", Label = new LocalizedText(null) }],
            }],
        });

        data.Add("permission_out_of_vocabulary", Sound() with
        {
            Permissions = ["listing.browse", "listing.publish"],
        });
        data.Add("permission_out_of_vocabulary", Sound() with
        {
            // اسم إجراء في سِجِلّ التَّدقيق، ليسَ صَلاحِيَّة دَور.
            Permissions = ["tenant.roles_save"],
        });
        data.Add("permission_duplicate", Sound() with
        {
            Permissions = ["listing.browse", "listing.browse"],
        });

        data.Add("field_code_empty", Sound() with { Fields = [Field() with { Code = "" }] });
        data.Add("field_code_duplicate", Sound() with { Fields = [Field(), Field()] });
        data.Add("field_type_out_of_vocabulary", Sound() with
        {
            Fields = [Field() with { Type = "Slider" }],
        });
        data.Add("field_type_out_of_vocabulary", Sound() with
        {
            // التَعداد حَسّاس لِلحالَة — مُطابِق لِـ DynFieldType حَرفِيّاً.
            Fields = [Field() with { Type = "text" }],
        });
        data.Add("select_without_options", Sound() with
        {
            Fields = [SelectField() with { Options = [] }],
        });
        data.Add("select_without_options", Sound() with
        {
            Fields = [SelectField() with { Type = "MultiSelect", Options = [] }],
        });
        data.Add("option_value_empty", Sound() with
        {
            Fields = [SelectField() with
            {
                Options = [new RoleFieldOptionDefinition { Value = "", Label = new LocalizedText("اقتِصاديّ") }],
            }],
        });
        data.Add("option_value_duplicate", Sound() with
        {
            Fields = [SelectField() with
            {
                Options =
                [
                    new RoleFieldOptionDefinition { Value = "economy", Label = new LocalizedText("اقتِصاديّ") },
                    new RoleFieldOptionDefinition { Value = "economy", Label = new LocalizedText("مُريح") },
                ],
            }],
        });

        data.Add("composition_component_out_of_vocabulary", Sound() with
        {
            Composition = new RoleComposition { Home = "cosmicHome" },
        });
        data.Add("composition_component_out_of_vocabulary", Sound() with
        {
            Composition = new RoleComposition { PublicProfile = "ghostProfile" },
        });
        data.Add("composition_component_out_of_vocabulary", Sound() with
        {
            Composition = new RoleComposition { Extras = ["driverArea", "teleporter"] },
        });

        data.Add("deal_pattern_affinity_out_of_vocabulary", Sound() with
        {
            DealPatternAffinity = "teleportation",
        });
        // «marketplace» قيمَة الراحَة لا انجِذاباً يُسنَد — خارِج المَعجَم قَصداً.
        data.Add("deal_pattern_affinity_out_of_vocabulary", Sound() with
        {
            DealPatternAffinity = "marketplace",
        });

        return data;
    }

    // ─── مادَّة الاختِبار: دَور سَليم بِأَبسَط شَكل، ثُمَّ يُفسَد بِـ with ──
    private static RoleDefinition Sound() => new()
    {
        Slug = "driver",
        Icon = "🚗",
        HomeRoute = "/explore",
        Label = new LocalizedText("سائِق"),
        Description = new LocalizedText("سائِق مَركَبَة."),
        Permissions = ["listing.browse", "offer.submit"],
        Fields = [],
        Composition = new RoleComposition(),
    };

    private static RoleFieldDefinition Field() => new()
    {
        Code = "vehicle_plate",
        Label = new LocalizedText("لَوحَة المَركَبَة"),
        Type = "Text",
        IsRequired = true,
        Options = [],
    };

    private static RoleFieldDefinition SelectField() => new()
    {
        Code = "vehicle_type",
        Label = new LocalizedText("نَوع المَركَبَة"),
        Type = "SingleSelect",
        IsRequired = true,
        Options = [new RoleFieldOptionDefinition { Value = "economy", Label = new LocalizedText("اقتِصاديّ") }],
    };

    [Fact]
    public void Sound_definition_passes_so_negatives_prove_their_own_cause()
        => Assert.True(RoleDefinitionValidator.IsValid(Sound()));

    // ─── سالِب عَلى مَسار المِلَفّ كامِلاً — لا عَلى كائِن مَبنيّ ───────
    // <para>الحالات أَعلاه تَبني <c>RoleDefinition</c> بِـ <c>with</c>
    // وتُمَرِّرُه لِلمُصادِق: تُغَطّي <b>المُصادِق</b> تَغطِيَةً كامِلَة
    // (‏permission_out_of_vocabulary بِحالَتَين، و
    // composition_component_out_of_vocabulary بِثَلاث) — ولا تَمَسّ
    // <b>التَّسَلسُل</b> بِسَطر واحِد. وهذه المَوجَة دَعواها أَنّ الدَور
    // يُؤَلَّف <b>مِلَفّاً</b>، فَالسالِب الَّذي يَخدِمُها هو المِلَفّ
    // المُصطَنَع يَمُرّ مِن حَيث تَمُرّ المِلَفّات السَليمَة: نَصّ JSON ←
    // <c>RoleDefinitionLoader.ParseDefinition</c> بِنَفس خِيارات القِراءَة
    // ← المُصادِق. فَلا تَكرار لِلتَغطِيَة، بَل إغلاق طَبَقَة لَم تُغَطَّ.</para>

    /// <summary>مِلَفّ يَمنَح صَلاحِيَّة خارِج المَعجَم — يُقرَأ بِنَجاح
    /// (فَالبُنيَة سَليمَة) ويُرفَض بِالرَّمز المَقصود.</summary>
    [Fact]
    public void Synthetic_role_file_with_unknown_permission_is_rejected()
    {
        var d = RoleDefinitionLoader.ParseDefinition("""
        {
          "slug": "auditor",
          "icon": "🛡️",
          "homeRoute": "/explore",
          "label":       { "ar": "مُدَقِّق", "en": null },
          "description": { "ar": "دَور مُصطَنَع لِلاختِبار.", "en": null },
          "permissions": ["listing.browse", "listing.audit"],
          "fields": [],
          "composition": {
            "home": "defaultHome", "createListing": "defaultCreateForm",
            "nav": "defaultNav", "explore": "defaultExplore",
            "publicProfile": null, "extras": []
          },
          "dealPatternAffinity": null
        }
        """);

        // قُرِئَ فِعلاً — الرَّفض مِن المُصادِق لا مِن فَشَل تَسَلسُل.
        Assert.Equal("auditor", d.Slug);
        Assert.Equal(["listing.browse", "listing.audit"], d.Permissions);

        var codes = RoleDefinitionValidator.Validate(d).Select(v => v.Code).ToArray();
        Assert.Equal(["permission_out_of_vocabulary"], codes);
    }

    /// <summary>مِلَفّ بِقيمَة تَركيب مَجهولَة — الرَّمز الآخَر، ومِن
    /// فَتحَة <c>extras</c> تَحديداً لِأَنَّها المَوضِع الَّذي يُغري
    /// بِاختِراع سَطح جَديد.</summary>
    [Fact]
    public void Synthetic_role_file_with_unknown_component_is_rejected()
    {
        var d = RoleDefinitionLoader.ParseDefinition("""
        {
          "slug": "auditor",
          "icon": "🛡️",
          "homeRoute": "/explore",
          "label":       { "ar": "مُدَقِّق", "en": null },
          "description": { "ar": "دَور مُصطَنَع لِلاختِبار.", "en": null },
          "permissions": ["listing.browse"],
          "fields": [],
          "composition": {
            "home": "defaultHome", "createListing": "defaultCreateForm",
            "nav": "defaultNav", "explore": "defaultExplore",
            "publicProfile": null, "extras": ["auditPanel"]
          },
          "dealPatternAffinity": null
        }
        """);

        Assert.Equal(["auditPanel"], d.Composition.Extras);

        var codes = RoleDefinitionValidator.Validate(d).Select(v => v.Code).ToArray();
        Assert.Equal(["composition_component_out_of_vocabulary"], codes);
    }

    /// <summary>مِفتاح مَجهول في المِلَفّ لا يُتَجاهَل صامِتاً — يَرمي
    /// عِندَ القِراءَة (<c>UnmappedMemberHandling.Disallow</c>) قَبل أَن
    /// يَصِل المُصادِق أَصلاً. طَبَقَة ثالِثَة لا يَحرُسُها المُصادِق
    /// ولا يُمكِن أَن يَحرُسَها.</summary>
    [Fact]
    public void Synthetic_role_file_with_unknown_key_fails_to_read()
    {
        var ex = Record.Exception(() => RoleDefinitionLoader.ParseDefinition("""
        {
          "slug": "auditor",
          "icon": "🛡️",
          "homeRoute": "/explore",
          "label":       { "ar": "مُدَقِّق", "en": null },
          "description": { "ar": "دَور مُصطَنَع لِلاختِبار.", "en": null },
          "permissions": ["listing.browse"],
          "fields": [],
          "composition": {
            "home": "defaultHome", "createListing": "defaultCreateForm",
            "nav": "defaultNav", "explore": "defaultExplore",
            "publicProfile": null, "extras": []
          },
          "dealPatternAffinity": null,
          "superPowers": ["flight"]
        }
        """));

        Assert.IsType<System.Text.Json.JsonException>(ex);
    }

    /// <summary>والمُوجَب عَلى نَفس المَسار — لِئَلّا يَكون السالِبانِ
    /// أَعلاه ناجِحَين لِسَبَب آخَر (مِثل أَنّ القِراءَة تَفشَل دائِماً).</summary>
    [Fact]
    public void Synthetic_role_file_that_is_sound_passes()
    {
        var d = RoleDefinitionLoader.ParseDefinition("""
        {
          "slug": "auditor",
          "icon": "🛡️",
          "homeRoute": "/explore",
          "label":       { "ar": "مُدَقِّق", "en": null },
          "description": { "ar": "دَور مُصطَنَع لِلاختِبار.", "en": null },
          "permissions": ["listing.browse"],
          "fields": [],
          "composition": {
            "home": "defaultHome", "createListing": "defaultCreateForm",
            "nav": "defaultNav", "explore": "defaultExplore",
            "publicProfile": null, "extras": []
          },
          "dealPatternAffinity": null
        }
        """);

        Assert.True(RoleDefinitionValidator.IsValid(d));
    }
}
