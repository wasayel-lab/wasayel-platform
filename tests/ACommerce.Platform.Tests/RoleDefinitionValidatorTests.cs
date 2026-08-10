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
    [Fact]
    public void Loads_seven_definitions_in_index_order()
        => Assert.Equal(
            new[] { "customer", "rider", "vendor", "driver", "host", "shipper", "tenant_admin" },
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

    /// <summary>التَّقييمات مُحايِدَة الدَور: نُقطَة اللَمس الوَحيدَة هي
    /// الصَفحَة العامَّة، ولا يَملِكُها إلّا <c>vendor</c> و<c>host</c>.</summary>
    [Fact]
    public void Only_vendor_and_host_have_a_public_profile()
        => Assert.Equal(
            new[] { "vendor", "host" },
            RoleCatalog.Definitions
                .Where(d => d.Composition.PublicProfile is not null)
                .Select(d => d.Slug).ToArray());

    // ─── انجِذاب نَمَط الصَفقَة ───────────────────────────────────────
    /// <summary>الانجِذاب المُسنَد في المِلَفّات — ثَلاثَة أَدوار لا
    /// رابِع، والبَقِيَّة <c>null</c>. وهو <b>تَثبيت لِعَدَم التَماثُل
    /// المُوَثَّق</b>: <c>shipper</c> بِلا انجِذاب رَغم أَنَّه دَور سائِق
    /// في كُلّ فَتحَة تَركيب — لِأَنّ <c>PatternFromTenant</c> لَم يَكُن
    /// يَعُدُّه <c>trip</c>، وتَصحيحُه تَغيير سُلوك لا نَقل.</summary>
    [Fact]
    public void Deal_pattern_affinity_is_assigned_to_exactly_three_roles()
        => Assert.Equal(
            new[] { ("rider", "trip"), ("driver", "trip"), ("host", "rental") },
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
}
