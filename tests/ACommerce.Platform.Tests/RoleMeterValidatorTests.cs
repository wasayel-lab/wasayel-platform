using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>G7 — مِترُ الدَور</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٧ — ‏ADR-051 §٢١؛ التَصميم §7 و§12): لِكُلِّ رَمزٍ مِن الثَلاثَةِ ولِرَمزِ المُستَأجِرِ
/// اختِبارٌ موجِبٌ وسالِب (القاعِدَة ٤)، وحارِسُ عَمىً لِكُلِّ مَجموعَة (كُلُّ رَمزٍ مُعلَنٍ يُنتِجُه خَرق)، ومِلَفّاتُ الكاتالوجِ تُفحَصُ واحِداً واحِداً ويُطبَعُ
/// عَدَدُها ويَفشَلُ عَلى الصِفر، والمِترُ المُشتَقُّ (<see cref="TenantRoleSet.MeterOf"/>) بِحالاتِه الثَلاث، وبابا الكِتابَةِ وَحدَهُما يَسأَلانِ عَن مِفتاحِ
/// المُستَأجِر — لا حِزامُ القِراءَة.</para>
/// </summary>
public class RoleMeterValidatorTests(ITestOutputHelper output)
{
    private static RoleDefinition Role(string? meter, string slug = "meter_probe") => new()
    {
        Slug = slug, Icon = "🧪", HomeRoute = "",
        Label = new LocalizedText("مِجَسّ"), Description = new LocalizedText("دَورٌ لِلمِجَسّ."),
        Permissions = ["listing.browse"], Fields = [], Composition = new RoleComposition(),
        Meter = meter,
    };

    private static string[] CodesOf(RoleDefinition d) => RoleMeterValidator.Validate(d).Select(v => v.Code).ToArray();

    private static string[] CodesOf(RoleDefinition d, MeterUnitCatalog units) => RoleMeterValidator.Validate(d, units).Select(v => v.Code).ToArray();

    private static string[] TenantCodesOf(string json) => RoleMeterValidator.ValidateTenantMeter(json).Select(v => v.Code).ToArray();

    /// <summary>وَحدَةُ حُضورٍ مُصطَنَعَةٌ تَجتازُ مُصادِقَ المُعجَم — لِمُعجَمٍ يُبنى في الاختِبارِ بِحالَتِه بَعدَ البَند ١٩ أَو بِخَطَإٍ فيها.</summary>
    private static MeterUnit Unit(string code, string side, string boundary) => new()
    {
        Code = code, Side = side, Boundary = boundary, Measure = MeterMeasures.Count,
        Feature = "tenants", Reconcile = MeterReconcilers.TenantIds, Source = "مِجَسٌّ في الاختِبار",
    };

    // ‏(‏2026-11-13، W13-6·١٩) والوَحدَةُ المُضافَةُ تَحِلُّ مَحَلَّ سَمِيِّها في المُعجَم: وَحَداتُ الحُضورِ دَخَلَته، فَإضافَتُها ثانِيَةً كانَت `units_code_duplicate`.
    private static MeterUnitCatalog With(params MeterUnit[] extra)
        => MeterUnitCatalog.From([.. MeterUnitCatalog.Default.Units.Where(u => !extra.Any(e => e.Code == u.Code)), .. extra]);

    /// <summary>تَعريفُ «خَيّاط» كَما يَكتُبُه الوَكيل، ومَعَه مِفتاحُ مِترٍ بِقيمَةٍ مُعطاة.</summary>
    private static string KhayyatWithMeter(string jsonValue) =>
        TenantRoleDefinitionToolTests.KhayyatJson.Replace("\"dealPatternAffinity\": null", $"\"dealPatternAffinity\": null,\n  \"meter\": {jsonValue}");

    // ─── المَعجَمُ والرُموز ─────────────────────────────────────────────

    [Fact]
    public void Three_codes_one_tenant_code_and_two_values_are_declared_and_none_repeats()
    {
        Assert.Equal(new[] { "meter_missing", "meter_unit_unknown", "meter_unit_not_role_presence" }, RoleMeterValidator.Codes);
        Assert.Equal(new[] { "meter_authored_by_tenant" }, RoleMeterValidator.TenantCodes);
        Assert.Equal(new[] { "presence.role", "none" }, RoleMeterUnits.All);
        Assert.Equal(new[] { "presence.role" }, RoleMeterUnits.RolePresence);
        Assert.Equal(RoleMeterUnits.PresenceRole, RoleMeterUnits.Derived);
        Assert.Empty(RoleMeterValidator.Codes.Intersect(RoleMeterValidator.TenantCodes));
        // ووَحدَةُ حُضورِ الدَورِ بِنَمَطِ رُموزِ المُعجَم، ومِفتاحُ قامُوسِها لا يَتَصادَمُ مَعَ وَحدَةٍ قائِمَة.
        Assert.Empty(MeterUnitValidator.Validate(Unit(RoleMeterUnits.PresenceRole, MeterSides.Technology, MeterBoundaries.Presence)));
        Assert.DoesNotContain(MeterUnitCatalog.Default.Units, u => u.Code != RoleMeterUnits.PresenceRole
            && MeterUnitCatalog.LabelKey(u.Code) == MeterUnitCatalog.LabelKey(RoleMeterUnits.PresenceRole));
    }

    // ─── ١. meter_missing ─────────────────────────────────────────────

    [Fact]
    public void meter_missing_fires_on_an_absent_or_blank_meter_and_only_then()
    {
        foreach (var blank in new[] { null, "", "  " })
            Assert.Equal(new[] { "meter_missing" }, CodesOf(Role(blank)));
        Assert.Contains("meter_probe", RoleMeterValidator.Validate(Role(null))[0].MessageAr, StringComparison.Ordinal);

        Assert.Empty(CodesOf(Role("none")));
        Assert.Empty(CodesOf(Role("presence.role")));
        Assert.True(RoleMeterValidator.IsValid(Role("none")));
        Assert.False(RoleMeterValidator.IsValid(Role(null)));
    }

    // ─── ٢. meter_unit_unknown ─────────────────────────────────────────

    [Theory]
    [InlineData("role.authored")]
    [InlineData("presence.rol")]
    [InlineData("Presence.Role")]
    [InlineData("presence_role")]
    [InlineData("None")]
    [InlineData("seat")]
    public void meter_unit_unknown_fires_on_a_value_that_is_no_unit_at_all(string meter)
    {
        Assert.Equal(new[] { "meter_unit_unknown" }, CodesOf(Role(meter)));
        Assert.Contains(meter, RoleMeterValidator.Validate(Role(meter))[0].MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public void meter_unit_unknown_never_fires_on_a_lexicon_unit_or_on_a_role_value()
    {
        foreach (var u in MeterUnitCatalog.Default.Units)
            Assert.DoesNotContain("meter_unit_unknown", CodesOf(Role(u.Code)));
        foreach (var value in RoleMeterUnits.All)
            Assert.Empty(CodesOf(Role(value)));
    }

    // ─── ٣. meter_unit_not_role_presence ───────────────────────────────

    /// <summary>كُلُّ وَحدَةٍ في المُعجَمِ اليَومَ عَمَلِيَّةٌ (حِفظٌ أَو صادِر) — وكُلٌّ مِنها عَلى دَورٍ يُرَدُّ بِهذا الرَمزِ وَحدَه، لا بِـ«مَجهولَة».</summary>
    [Fact]
    public void meter_unit_not_role_presence_fires_on_every_operation_unit_of_the_lexicon()
    {
        var units = MeterUnitCatalog.Default.Units;
        foreach (var u in units.Where(u => !RoleMeterUnits.RolePresence.Contains(u.Code)))
            Assert.Equal(new[] { "meter_unit_not_role_presence" }, CodesOf(Role(u.Code)));
        output.WriteLine($"[G7] وَحَداتُ المُعجَمِ مَردودَةً عَلى دَور: {units.Count(u => !RoleMeterUnits.RolePresence.Contains(u.Code))} مِن {units.Count}");
        Assert.True(units.Count > 0, "أَداةٌ عَمياء: مُعجَمٌ بِلا وَحدَة.");
        Assert.Contains("deal.create", RoleMeterValidator.Validate(Role("deal.create"))[0].MessageAr, StringComparison.Ordinal);
    }

    /// <summary><b>المُعجَمُ بَعدَ البَند ١٩</b>: وَحدَةُ حُضورِ الدَورِ تَدخُلُه حُضوراً فَتُقبَل، ووَحدَةُ حُضورٍ مَوضوعُها غَيرُ الدَورِ (المَتجَرُ نَفسُه) تُرَدّ،
    /// <b>وإن دَخَلَته عَمَلِيَّةً بِخَطَإٍ رُدَّت</b> — فَالمُصادِقُ لا يَثِقُ بِالاسمِ وَحدَه حينَ يَعرِفُ المُعجَمُ الوَحدَة.</summary>
    [Fact]
    public void A_lexicon_that_carries_presence_units_accepts_the_role_one_and_refuses_the_rest()
    {
        var after19 = With(Unit("presence.role", MeterSides.Technology, MeterBoundaries.Presence),
                           Unit("presence.store", MeterSides.Technology, MeterBoundaries.Presence));
        Assert.Empty(CodesOf(Role("presence.role"), after19));
        Assert.Equal(new[] { "meter_unit_not_role_presence" }, CodesOf(Role("presence.store"), after19));
        Assert.Empty(CodesOf(Role("none"), after19));

        var misfiled = With(Unit("presence.role", MeterSides.Operation, MeterBoundaries.Persistence));
        Assert.Equal(new[] { "meter_unit_not_role_presence" }, CodesOf(Role("presence.role"), misfiled));
        Assert.Contains(MeterBoundaries.Persistence, RoleMeterValidator.Validate(Role("presence.role"), misfiled)[0].MessageAr, StringComparison.Ordinal);
    }

    /// <summary><b>الجِسرُ إلى البَند ١٩ مَكتوبٌ لا مُفتَرَض</b>: وَحدَةُ حُضورِ الدَورِ لَيسَت في المُعجَمِ قَبلَ مَوضِعِ عَدِّها (القاعِدَة ١)، فَإن دَخَلَته
    /// فَحُضوراً لِجانِبِ التِقنِيَة — وإلّا رَدَّ المُحَمِّلُ كُلَّ مِلَفِّ دَورٍ عِندَ الإقلاع.</summary>
    [Fact]
    public void The_role_presence_unit_enters_the_lexicon_only_as_technology_presence()
    {
        var unit = MeterUnitCatalog.Default.Find(RoleMeterUnits.PresenceRole);
        output.WriteLine(unit is null
            ? "[G7] presence.role في المُعجَم: لا — يَدخُلُه مَعَ لَقطِ الحُضورِ (البَند ١٩)، ومِلَفّاتُ الأَدوارِ تَحمِلُه مِن RoleMeterUnits"
            : $"[G7] presence.role في المُعجَم: {unit.Side} · {unit.Boundary}");
        if (unit is not null)
            Assert.Equal((MeterSides.Technology, MeterBoundaries.Presence), (unit.Side, unit.Boundary));
    }

    // ─── حارِسُ العَمى لِلرُموز ──────────────────────────────────────────

    /// <summary>كُلُّ رَمزٍ مُعلَنٍ — في المَجموعَتَين — يُنتِجُه مُدخَلٌ خارِق، والقائِمَةُ مِن نَتائِجِ المُدخَلاتِ لا مِن نَصٍّ بِاليَد.</summary>
    [Fact]
    public void Every_declared_code_in_both_sets_is_actually_produced_by_some_breach()
    {
        var produced = new[] { Role(null), Role("role.authored"), Role("deal.create") }
            .SelectMany(CodesOf).ToHashSet(StringComparer.Ordinal);
        var tenantProduced = new[] { KhayyatWithMeter("\"presence.role\"") }
            .SelectMany(TenantCodesOf).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"[G7] رُموزٌ أُنتِجَت: {produced.Count} مِن {RoleMeterValidator.Codes.Count} · رُموزُ المُستَأجِر: {tenantProduced.Count} مِن {RoleMeterValidator.TenantCodes.Count}");
        Assert.True(produced.Count > 0 && tenantProduced.Count > 0, "أَداةٌ عَمياء: صِفرُ خَرقٍ أُنتِج.");
        var never = RoleMeterValidator.Codes.Where(c => !produced.Contains(c))
            .Concat(RoleMeterValidator.TenantCodes.Where(c => !tenantProduced.Contains(c))).ToArray();
        Assert.True(never.Length == 0, "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ مُدخَل: " + string.Join("، ", never));
    }

    // ─── الكاتالوج ─────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>كُلُّ مِلَفِّ دَورٍ في الكاتالوجِ يَحمِلُ مِتراً صالِحاً</b> — والقيمَةُ لِكُلِّ مِلَفٍّ مُثَبَّتَةٌ هُنا، فَتَبديلُها صَفٌّ أَحمَرُ يُرى في المُراجَعَة.
    /// والعَشَرَةُ <c>presence.role</c>: افتِراضُ السُؤالِ ٤ (التَصميم §17) أَنّ الأَدوارَ تُعَدُّ لِكُلِّ مَتجَرٍ في اليَوم، إلى أَن يُسَمّيَ المالِكُ ما لا يُعَدّ.
    /// والمُحَمِّلُ نَفسُه يَرفُضُ عِندَ الإقلاع، فَهذا الصَفُّ يَقرَأُ ما مَرَّ مِنه.</para>
    /// </summary>
    [Fact]
    public void Every_catalogue_role_carries_a_valid_meter_and_its_value_is_pinned()
    {
        var expected = new[] { "broker", "customer", "driver", "host", "mover", "organizer", "rider", "shipper", "tenant_admin", "vendor" }
            .ToDictionary(s => s, _ => "presence.role", StringComparer.Ordinal);
        var checkedCount = 0;
        var violations = new List<string>();
        foreach (var d in RoleCatalog.Definitions)
        {
            checkedCount++;
            violations.AddRange(RoleMeterValidator.Validate(d).Select(v => $"{d.Slug}: {v.Code}"));
            output.WriteLine($"[G7]   {d.Slug,-14} مِتر {d.Meter}");
        }
        output.WriteLine($"[G7] تَعريفاتٌ مَفحوصَة {checkedCount} · خُروق {violations.Count}");
        Assert.True(checkedCount > 0, "أَداةٌ عَمياء: صِفرُ تَعريفٍ مَفحوص.");
        Assert.Empty(violations);
        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal),
                     RoleCatalog.Definitions.ToDictionary(d => d.Slug, d => d.Meter!).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    /// <summary><b>الحَقلُ يُقرَأُ ولا يَبلُغُ المُصادِقاتِ القائِمَة</b>: نَصٌّ يَحمِلُ المِفتاحَ يُقرَأُ إلى <see cref="RoleDefinition.Meter"/>، وغِيابُه
    /// <c>null</c> يَرُدُّه <see cref="RoleMeterValidator"/> وَحدَه — <c>Validate</c> وبابُ الكِتابَةِ وحِزامُ القِراءَةِ بِرُموزِها نَفسِها بِالمِفتاحِ وبِدونِه.</summary>
    [Fact]
    public void The_field_reads_and_the_existing_validators_answer_the_same_with_or_without_it()
    {
        var bare = RoleDefinitionLoader.ParseDefinition(TenantRoleDefinitionToolTests.KhayyatJson);
        var carried = RoleDefinitionLoader.ParseDefinition(KhayyatWithMeter("\"presence.role\""));
        Assert.Null(bare.Meter);
        Assert.Equal("presence.role", carried.Meter);
        Assert.Equal(new[] { "meter_missing" }, CodesOf(bare));
        Assert.Empty(CodesOf(carried));

        static string Codes(IEnumerable<RoleDefinitionViolation> v) => string.Join(",", v.Select(x => x.Code));
        Assert.Equal(Codes(RoleDefinitionValidator.Validate(bare)), Codes(RoleDefinitionValidator.Validate(carried)));
        Assert.Equal(Codes(RoleDefinitionValidator.ValidateTenantDefinition(bare)), Codes(RoleDefinitionValidator.ValidateTenantDefinition(carried)));
        Assert.Equal(Codes(RoleDefinitionValidator.ValidateStoredTenantDefinition(bare)), Codes(RoleDefinitionValidator.ValidateStoredTenantDefinition(carried)));

        // ومِفتاحٌ بِحالَةٍ أُخرى مَجهولٌ تَرُدُّه القِراءَةُ قَبلَ أَيِّ مُصادِق.
        Assert.Throws<JsonException>(() => RoleDefinitionLoader.ParseDefinition(
            TenantRoleDefinitionToolTests.KhayyatJson.Replace("\"dealPatternAffinity\": null", "\"dealPatternAffinity\": null,\n  \"Meter\": \"none\"")));
    }

    // ─── رَمزُ المُستَأجِر: meter_authored_by_tenant ──────────────────────

    [Theory]
    [InlineData("\"presence.role\"")]
    [InlineData("\"none\"")]
    [InlineData("null")]
    [InlineData("\"role.authored\"")]
    public void meter_authored_by_tenant_fires_on_the_key_whatever_its_value(string jsonValue)
    {
        var json = KhayyatWithMeter(jsonValue);
        Assert.Equal(new[] { "meter_authored_by_tenant" }, TenantCodesOf(json));
        Assert.Contains("khayyat", RoleMeterValidator.ValidateTenantMeter(json)[0].MessageAr, StringComparison.Ordinal);
        Assert.False(RoleMeterValidator.IsValidTenantMeter(json));
    }

    /// <summary>السالِب: تَعريفُ الوَكيلِ بِلا مِفتاح، ووَثيقَةُ البَذرِ المُعتَمَدَة (<c>mandoob</c>)، والكَلِمَةُ في نَصٍّ أَو في مِفتاحٍ مُتَداخِلٍ — لا تُرَدّ:
    /// الرَمزُ يَرى مِفتاحَ الجَذرِ وَحدَه، وهو وَحدَه ما يَقرَؤُه <see cref="RoleDefinition.Meter"/>.</summary>
    [Fact]
    public void meter_authored_by_tenant_does_not_fire_without_the_root_key()
    {
        Assert.Empty(TenantCodesOf(TenantRoleDefinitionToolTests.KhayyatJson));
        Assert.True(RoleMeterValidator.IsValidTenantMeter(TenantRoleDefinitionToolTests.KhayyatJson));
        Assert.Empty(TenantCodesOf("""{ "slug": "x", "label": { "ar": "meter", "en": "meter" }, "composition": { "meter": "none" } }"""));
        Assert.Empty(TenantCodesOf("[]"));
    }

    /// <summary><b>حِزامُ القِراءَةِ لا يَسأَلُ عَن المِفتاح</b> (سابِقَةُ <c>slug_shadows_role_host</c>): وَثيقَةٌ مُعتَمَدَةٌ تَحمِلُه — كُتِبَت بِاليَدِ أَو سَبَقَت
    /// البابَ — تَبقى حَيَّةً ولا تَسقُط؛ <b>ومِترُها مِن المَنَصَّةِ لا مِنها</b>: «none» في الوَثيقَةِ لا يُعفيها مِن العَدّ.</summary>
    [Fact]
    public void The_read_belt_keeps_an_approved_document_that_carries_the_key_and_the_platform_still_derives_its_meter()
    {
        var doc = new TenantRoleDefinition
        {
            Id = "khayyat", Slug = "khayyat", DefinitionJson = KhayyatWithMeter("\"none\""),
            Status = TenantRoleStatuses.Approved, CreatedAt = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
        };
        var set = TenantRoleSet.FromDocuments("meter-belt", [doc]);
        Assert.Single(set.TenantAuthored);
        Assert.Equal("none", set.TenantAuthored[0].Meter);
        Assert.Equal(RoleMeterUnits.Derived, set.MeterOf("khayyat"));
    }

    // ─── المِترُ المُشتَقّ: TenantRoleSet.MeterOf ────────────────────────

    /// <summary>
    /// <para><b>الحالاتُ الثَلاث</b>: دَورُ الكاتالوجِ بِمِترِ مِلَفِّه، والمُؤَلَّفُ بِما تُسنِدُه المَنَصَّة، وما لا تَعريفَ لَه (فارِغٌ أَو مُستَورَد) بِما تُسنِدُه أَيضاً.
    /// والجَوابُ لا يَتَغَيَّرُ بِأَن يَحمِلَ المَتجَرُ وَثائِقَ — مَتجَرٌ بِلا وَثيقَةٍ ومَتجَرٌ بِدَورٍ مُؤَلَّفٍ يُجيبانِ عَن الكاتالوجِ واحِداً.</para>
    /// </summary>
    [Fact]
    public void MeterOf_answers_the_catalogue_file_and_derives_the_rest()
    {
        var authored = TenantRoleSet.FromDocuments("meter-of", [new TenantRoleDefinition
        {
            Id = "khayyat", Slug = "khayyat", DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson,
            Status = TenantRoleStatuses.Approved, CreatedAt = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
        }]);
        var zero = TenantRoleSet.FromDocuments("meter-of", Array.Empty<TenantRoleDefinition>());
        Assert.Same(RoleCatalog.Definitions, zero.Definitions);

        var rows = 0;
        foreach (var d in RoleCatalog.Definitions)
        {
            Assert.Equal(d.Meter, TenantRoleSet.Platform.MeterOf(d.Slug));
            Assert.Equal(d.Meter, zero.MeterOf(d.Slug));
            Assert.Equal(d.Meter, authored.MeterOf(d.Slug));
            rows++;
        }
        Assert.Equal(RoleMeterUnits.Derived, authored.MeterOf("khayyat"));
        Assert.Equal(RoleMeterUnits.Derived, authored.MeterOf(null));
        Assert.Equal(RoleMeterUnits.Derived, authored.MeterOf(""));
        Assert.Equal(RoleMeterUnits.Derived, authored.MeterOf("imported_role"));
        Assert.Equal(RoleMeterUnits.Derived, TenantRoleSet.Platform.MeterOf("khayyat"));
        output.WriteLine($"[G7] MeterOf: كاتالوج {rows} × لَقَطات 3 · مُؤَلَّف/فارِغ/مُستَورَد ⇐ {RoleMeterUnits.Derived}");
        Assert.True(rows > 0, "أَداةٌ عَمياء: صِفرُ دَور.");
    }

    // ─── بابا الكِتابَة — وَحدَهُما ─────────────────────────────────────────

    /// <summary>
    /// <para><b>مَن يَسأَلُ عَن مِفتاحِ المُستَأجِر</b> (ماسِحٌ بِأَسماءِ الأَبواب): اقتِراحُ الوَكيلِ (<c>DefineRoleAsync</c>) واعتِمادُ المُشرِف
    /// (<c>ValidateBeforeApprove</c>) يَسأَلانِ <see cref="RoleMeterValidator.ValidateTenantMeter"/> عَلى النَصِّ كَما كُتِب، والحِزامُ <c>FromDocuments</c> لا يَسأَلُه؛
    /// وكُلُّ نِداءٍ لَه في <c>libs</c> و<c>apps</c> يُعَدُّ ويُطبَع — فَبابٌ ثالِثٌ أَو حِزامٌ يُضافُ بِلا قَرارٍ يُحَمِّرُه، وبابٌ يَسقُطُ مِنه نِداؤُه يُحَمِّرُه.</para>
    /// </summary>
    [Fact]
    public void Only_the_two_write_doors_ask_for_the_tenant_meter_key()
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
        Assert.Contains("RoleMeterValidator.ValidateTenantMeter(doc.DefinitionJson)", approve, StringComparison.Ordinal);
        Assert.Contains("RoleMeterValidator.ValidateTenantMeter(definitionJson)", propose, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateTenantMeter", belt, StringComparison.Ordinal);

        var callers = Directory.GetFiles(Path.Combine(root, "libs"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "apps"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "libs"), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
            .Where(x => !x.Line.StartsWith("//", StringComparison.Ordinal) && !x.Line.StartsWith("///", StringComparison.Ordinal)
                        && x.Line.Contains("ValidateTenantMeter(", StringComparison.Ordinal))
            .Where(x => x.File != "RoleMeterValidator.cs")
            .Select(x => x.File).OrderBy(f => f, StringComparer.Ordinal).ToList();
        output.WriteLine($"[G7] مَن يَسأَلُ عَن مِفتاحِ المُستَأجِر: {callers.Count} — {string.Join(" ", callers)}");
        Assert.True(callers.Count > 0, "أَداةٌ عَمياء: صِفرُ نِداء.");
        Assert.Equal(["AgentService.cs", "TenantRoleService.cs"], callers);
    }

    // ─── القاعِدَة ١٦: عَدٌّ لا سِعر ─────────────────────────────────────

    /// <summary><b>لا عُضوَ ولا مِفتاحَ ولا قيمَةَ ولا رِسالَةَ في مِترِ الدَورِ تُسَمّي سِعراً</b> — بِالمُعجَمَينِ الإنجليزيِّ والعَرَبِيِّ نَفسِهِما
    /// (<c>MeterUnitCatalogTests.PriceIsh</c>/<c>NamesAPrice</c>)، والأَداةُ تُقاسُ أَوَّلاً عَلى اسمٍ مَحقون.</summary>
    [Fact]
    public void No_member_key_value_or_message_of_the_role_meter_names_a_price()
    {
        Assert.Matches(MeterUnitCatalogTests.PriceIsh, "RoleSeatFee");
        Assert.True(MeterUnitCatalogTests.NamesAPrice("دَورٌ بِسِعرِه"));

        var types = new[] { typeof(RoleMeterUnits), typeof(RoleMeterValidator), typeof(RoleMeterViolation) };
        var names = MeterUnitCatalogTests.MemberNames(types).Append($"{nameof(RoleDefinition)}.{nameof(RoleDefinition.Meter)}").ToList();

        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "kits", "Roles", "ACommerce.Kit.Roles.Core", "Definitions");
        var data = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.role.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var meter = doc.RootElement.GetProperty(RoleMeterValidator.MeterKey);
            data.Add(RoleMeterValidator.MeterKey);
            data.Add(meter.GetString() ?? "");
        }

        var texts = new[] { Role(null), Role("role.authored"), Role("deal.create") }
            .SelectMany(d => RoleMeterValidator.Validate(d).Select(v => v.MessageAr))
            .Concat(RoleMeterValidator.Validate(Role("presence.role"), With(Unit("presence.role", MeterSides.Operation, MeterBoundaries.Persistence))).Select(v => v.MessageAr))
            .Concat(RoleMeterValidator.ValidateTenantMeter(KhayyatWithMeter("\"none\"")).Select(v => v.MessageAr))
            .ToList();

        var priced = names.Where(n => MeterUnitCatalogTests.PriceIsh.IsMatch(n))
            .Concat(data.Where(MeterUnitCatalogTests.NamesAPrice))
            .Concat(texts.Where(MeterUnitCatalogTests.NamesAPrice)).ToList();
        output.WriteLine($"[G7] قاعِدَة ١٦: أَنواع {types.Length} · أَسماء {names.Count} · مَفاتيحُ وقِيَمُ المِترِ في المِلَفّات {data.Count} · نُصوص {texts.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(names.Count > 10 && data.Count == 20 && texts.Count == 5, $"أَداةٌ عَمياء: {names.Count} اسماً و{data.Count} مِفتاحاً وقيمَة و{texts.Count} نَصّاً.");
        Assert.True(priced.Count == 0, "مِترُ الدَورِ يُسَمّي سِعراً: " + string.Join("، ", priced));
    }
}
