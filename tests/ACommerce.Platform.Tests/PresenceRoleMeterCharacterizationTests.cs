using ACommerce.Kit.Roles;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيف: سُطورُ الأَدوارِ بِتَعريفاتِ المَنَصَّةِ وأَجوِبَةُ MeterOf — قَبلَ أَن يَقرَأَ مِترَ الدَورِ مِن قائِمَةِ تَعريفاتٍ يَحمِلُها المُعطى (‏2026-11-13، W13-6·D-R3·١) ═══
// ‏(M6D-RULES-R3-1؛ ADR-051 §٢٣؛ التَصميم §7 و§12 G12/G15؛ القاعِدَتانِ ٢ و٣) عِندَ 4ba30bd يَحمِلُ `PresenceDefinitions` دالَّةً لِمِترِ الدَور
// (`RoleMeter`، و`Platform` = `set.MeterOf(slug)`)، فَصَفُّ «نُسخَةُ دَورِ البائِعِ بِمِترٍ none ⇐ صِفرُ سَطرِ دَور» (G12 وتَوأَمُه في `PresenceReaderTests`)
// يُمَرِّرُ دالَّتَه هُوَ ولا يَقرَأُ `MeterOf` النُسخَة — وحَقنُ `MeterOf => RoleMeterUnits.Derived` أَخضَرُ في الوَحدَةِ والحَيّ. والتَبديلُ التالي يَجعَلُ
// المُعطى قائِمَةَ تَعريفاتِ أَدوارٍ (مِرآةَ `Providers`) يَقرَؤُها `MeterOf` نَفسُه. ووَعدُه أَنّ ما يُعَدُّ بِتَعريفاتِ المَنَصَّةِ لا يَتَغَيَّرُ حَرفاً،
// ولا جَوابُ `MeterOf(slug)` عَن أَيِّ سلاج — فَهذا المِلَفُّ يُثَبِّتُه قَبلَه ولا يُمَسُّ بَعدَه.

public class PresenceRoleMeterCharacterizationTests(ITestOutputHelper output)
{
    private static readonly DateOnly Day = new(2026, 11, 13);
    private static readonly Guid Owner = Guid.Parse("7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f");

    /// <summary>دَورٌ مُؤَلَّفٌ مُعتَمَدٌ تَحمِلُ وَثيقَتُه مِتراً «none» — وَثيقَةٌ كُتِبَت بِاليَدِ أَو سَبَقَت البابَ؛ حِزامُ القِراءَةِ يَقبَلُها، والمِترُ يُسنَدُ ولا يُقرَأُ مِنها.</summary>
    private static TenantRoleSet Authored() => TenantRoleSet.FromDocuments("store-r", [new TenantRoleDefinition
    {
        Id = "khayyat", Slug = "khayyat",
        DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson.Replace("\"slug\": \"khayyat\",", "\"slug\": \"khayyat\", \"meter\": \"none\",", StringComparison.Ordinal),
        Status = TenantRoleStatuses.Approved, CreatedAt = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
    }]);

    /// <summary>مَتجَرانِ: «ر» بِكُلِّ دَورِ كاتالوجٍ ودَورٍ مُؤَلَّفٍ ودَورٍ مُستَورَدٍ بِلا سلاجِ كاتالوجٍ ودَورٍ مُخَصَّصٍ بِلا تَعريف؛ و«ص» بِلَقطَةِ المَنَصَّةِ ودَورٍ لا تَعرِفُه.</summary>
    private static IReadOnlyList<PresenceStoreFacts> Fixture()
    {
        var authored = Authored();
        IReadOnlyList<PresenceRoleFact> roles =
            [.. RoleCatalog.Definitions.Select(d => new PresenceRoleFact(d.Slug, d.Slug)),
             new("khayyat", "khayyat"), new("imported_role", ""), new("custom_x", "custom_x")];
        return
        [
            new("store-r", Owner, false, "phone", authored, roles, authored.TenantAuthored.Count, 0, 0, [], 0),
            new("store-p", Guid.Empty, false, "", TenantRoleSet.Platform, [new("vendor", "vendor"), new("khayyat", "khayyat")], 0, 0, 0, [], 0),
        ];
    }

    private static string Row(UsageEntry e) => $"{e.Id} | {e.App} {e.Unit} {e.Entrepreneur} {e.Role} {e.Provider} {e.Feature} {e.Quantity} {e.SourceType}:{e.SourceId}";

    [Fact]
    public void The_platform_definitions_plan_one_role_row_per_materialized_role()
    {
        var rows = PresenceReader.Entries(Day, Fixture(), PresenceDefinitions.Platform, MeterUnitCatalog.Default).Select(Row).ToList();
        foreach (var r in rows) output.WriteLine($"[D-R3·char] {r}");
        output.WriteLine($"[D-R3·char] سُطور {rows.Count} · سُطورُ دَور {rows.Count(r => r.Contains(" presence.role ", StringComparison.Ordinal))}");
        Assert.Equal(Expected, rows);
    }

    private static readonly string[] Expected =
    [
        "presence.store:store-r:2026-11-13:store-r | store-r presence.store 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f _system _none tenants 1 store:store-r",
        "presence.role:store-r:2026-11-13:customer | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f customer _none roles 1 role:customer",
        "presence.role:store-r:2026-11-13:rider | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f rider _none roles 1 role:rider",
        "presence.role:store-r:2026-11-13:vendor | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f vendor _none roles 1 role:vendor",
        "presence.role:store-r:2026-11-13:driver | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f driver _none roles 1 role:driver",
        "presence.role:store-r:2026-11-13:host | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f host _none roles 1 role:host",
        "presence.role:store-r:2026-11-13:shipper | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f shipper _none roles 1 role:shipper",
        "presence.role:store-r:2026-11-13:tenant_admin | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f tenant_admin _none roles 1 role:tenant_admin",
        "presence.role:store-r:2026-11-13:broker | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f broker _none roles 1 role:broker",
        "presence.role:store-r:2026-11-13:mover | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f mover _none roles 1 role:mover",
        "presence.role:store-r:2026-11-13:organizer | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f organizer _none roles 1 role:organizer",
        "presence.role:store-r:2026-11-13:khayyat | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f khayyat _none roles 1 role:khayyat",
        "presence.role:store-r:2026-11-13:imported_role | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f imported_role _none roles 1 role:imported_role",
        "presence.role:store-r:2026-11-13:custom_x | store-r presence.role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f custom_x _none roles 1 role:custom_x",
        "presence.authored_role:store-r:2026-11-13:store-r | store-r presence.authored_role 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f _system _none roles 1 tenant_role_definition:store-r",
        "presence.auth_channel:store-r:2026-11-13:phone | store-r presence.auth_channel 7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f _system _none auth 1 auth_channel:phone",
        "presence.store:store-p:2026-11-13:store-p | store-p presence.store _unowned _system _none tenants 1 store:store-p",
        "presence.role:store-p:2026-11-13:vendor | store-p presence.role _unowned vendor _none roles 1 role:vendor",
        "presence.role:store-p:2026-11-13:khayyat | store-p presence.role _unowned khayyat _none roles 1 role:khayyat",
    ];

    [Fact]
    public void MeterOf_answers_every_slug_the_same_on_three_sets()
    {
        var zero = TenantRoleSet.FromDocuments("store-z", Array.Empty<TenantRoleDefinition>());
        var authored = Authored();
        string?[] slugs = [.. RoleCatalog.Definitions.Select(d => d.Slug), "khayyat", "", null, "imported_role", "custom_x"];
        var answers = slugs.Select(s => $"{s ?? "<null>"}={TenantRoleSet.Platform.MeterOf(s)}/{zero.MeterOf(s)}/{authored.MeterOf(s)}").ToList();
        output.WriteLine($"[D-R3·char] MeterOf ({answers.Count}): {string.Join(" · ", answers)}");
        Assert.Single(authored.TenantAuthored);
        Assert.Equal(ExpectedMeterOf, answers);
    }

    private static readonly string[] ExpectedMeterOf =
    [
        "customer=presence.role/presence.role/presence.role",
        "rider=presence.role/presence.role/presence.role",
        "vendor=presence.role/presence.role/presence.role",
        "driver=presence.role/presence.role/presence.role",
        "host=presence.role/presence.role/presence.role",
        "shipper=presence.role/presence.role/presence.role",
        "tenant_admin=presence.role/presence.role/presence.role",
        "broker=presence.role/presence.role/presence.role",
        "mover=presence.role/presence.role/presence.role",
        "organizer=presence.role/presence.role/presence.role",
        "khayyat=presence.role/presence.role/presence.role",
        "=presence.role/presence.role/presence.role",
        "<null>=presence.role/presence.role/presence.role",
        "imported_role=presence.role/presence.role/presence.role",
        "custom_x=presence.role/presence.role/presence.role",
    ];

    [Fact]
    public void The_platform_definitions_read_the_provider_catalogue_itself()
        => Assert.Same(ProviderCatalog.Definitions, PresenceDefinitions.Platform.Providers);
}
