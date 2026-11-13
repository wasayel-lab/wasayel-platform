using ACommerce.Kit.Roles;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُخَطِّطُ سُطورِ الحُضور — دالَّةٌ نَقِيَّة: الوَحدَةُ مِن التَعريفات، والأَبعادُ، والمُعَرِّف، وما لا يُكتَب (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣) ═══
// ‏`PresenceReader.Entries` بِلا قاعِدَة: مَتاجِرُ مَقروءَةٌ مُعطاةٌ ← سُطورُ يَومٍ واحِد. والحَيُّ (`LivePresenceSnapshotProofTests`، G12) يَقيسُ القِراءَةَ والمُعامَلَةَ والطابور؛
// وهُنا ما يُقَرِّرُه المُخَطِّطُ وَحدَه — صَفٌّ لِكُلِّ قاعِدَة، ولِكُلِّ صَفٍّ ضابِطٌ سالِب.

public class PresenceReaderTests(ITestOutputHelper output)
{
    private static readonly DateOnly Day = new(2026, 11, 13);
    private static readonly Guid Owner = Guid.Parse("2b8f0c3d-4e5a-4b6c-8d7e-9f0a1b2c3d4e");
    private static readonly Guid VendorA = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    private static readonly Guid VendorB = Guid.Parse("5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b");

    /// <summary>ثَلاثَةُ مَتاجِر: «أ» بِكُلِّ ما يُعَدّ (ورَبطٌ بِلا تَعريف)، و«م» مُعَلَّقٌ بِكُلِّ شَيء، و«ب» بِلا مالِكٍ ولا شَيءَ إلّا قَناةَ البَريد.</summary>
    internal static IReadOnlyList<PresenceStoreFacts> Fixture() =>
    [
        new("store-a", Owner, false, "phone", TenantRoleSet.Platform,
            [new("tenant_admin", "tenant_admin"), new("vendor", "vendor"), new("customer", "customer"), new("khayyat", "khayyat")],
            AuthoredRoles: 1, AuthoredThemes: 2, MemberPlans: 3,
            [new(ProviderCapabilities.Payments, "moyasar_api"), new(ProviderCapabilities.Files, "local_files"), new(ProviderCapabilities.Cache, "ghost_cache"),
             new(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, VendorA), "oto_vendor_account"),
             new(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, VendorB), "oto_vendor_account")],
            ApiKeys: 2),
        new("store-m", Owner, true, "phone", TenantRoleSet.Platform, [new("vendor", "vendor")], 1, 1, 1,
            [new(ProviderCapabilities.Payments, "moyasar_api")], 1),
        new("store-b", Guid.Empty, false, "email", TenantRoleSet.Platform, [], 0, 0, 0, [], 0),
    ];

    private static IReadOnlyList<UsageEntry> Plan(PresenceDefinitions? definitions = null, ICollection<string>? unresolved = null)
        => PresenceReader.Entries(Day, Fixture(), definitions ?? PresenceDefinitions.Platform, MeterUnitCatalog.Default, unresolved);

    private static string Row(UsageEntry e) => $"{e.App} {e.Unit} {e.Role} {e.Provider} {e.Feature} {e.Quantity} {e.SourceType}:{e.SourceId}";

    [Fact]
    public void Every_presence_unit_is_planned_from_stored_truth_with_its_dimensions()
    {
        var unresolved = new List<string>();
        var entries = Plan(unresolved: unresolved);
        foreach (var e in entries) output.WriteLine($"[G12·plan] {e.Id} ← {Row(e)}");
        output.WriteLine($"[G12·plan] سُطور {entries.Count} · وَحَدات {entries.Select(e => e.Unit).Distinct().Count()} مِن {PresenceUnits.All.Count} · بِلا تَعريف [{string.Join(", ", unresolved)}]");

        Assert.Equal(PresenceUnits.All.Order(StringComparer.Ordinal), entries.Select(e => e.Unit).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(new[]
        {
            "store-a presence.store _system _none tenants 1 store:store-a",
            "store-a presence.role tenant_admin _none roles 1 role:tenant_admin",
            "store-a presence.role vendor _none roles 1 role:vendor",
            "store-a presence.role customer _none roles 1 role:customer",
            "store-a presence.role khayyat _none roles 1 role:khayyat",
            "store-a presence.authored_role _system _none roles 1 tenant_role_definition:store-a",
            "store-a presence.authored_theme _system _none theme 2 tenant_theme_definition:store-a",
            "store-a presence.member_plan _system _none subscriptions 3 tenant_plan_definition:store-a",
            "store-a presence.provider_binding _system moyasar_api payments 1 provider_binding:payments",
            "store-a presence.vendor_binding _system oto_vendor_account delivery 2 vendor_binding:oto_vendor_account",
            "store-a presence.api_key _system _none api.call 2 api_key:store-a",
            "store-a presence.auth_channel _system _none auth 1 auth_channel:phone",
            "store-b presence.store _system _none tenants 1 store:store-b",
            "store-b presence.auth_channel _system _none auth 1 auth_channel:email",
        }, entries.Select(Row));
        // ‏المُعَرِّفُ `{unit}:{app}:{day}:{subject}`، ورائِدُ الأَعمالِ المالِكُ أَو `_unowned`، والجانِبُ تِقنِيَةٌ في المُعجَم.
        Assert.All(entries, e => Assert.Equal($"{e.Unit}:{e.App}:2026-11-13:{e.SourceId}", e.Id));
        Assert.All(entries, e => Assert.Equal(e.App == "store-a" ? Owner.ToString("D") : MeterSentinels.Unowned, e.Entrepreneur));
        Assert.All(entries, e => Assert.Equal((MeterSides.Technology, MeterBoundaries.Presence), (MeterUnitCatalog.Default.Find(e.Unit)!.Side, MeterUnitCatalog.Default.Find(e.Unit)!.Boundary)));
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
        Assert.Equal(new[] { "store-a:cache:ghost_cache" }, unresolved);
    }

    [Fact]
    public void A_suspended_store_has_no_row_and_the_unsuspended_one_has_every_row()
    {
        var entries = Plan();
        var asActive = PresenceReader.Entries(Day, [Fixture()[1] with { IsSuspended = false }], PresenceDefinitions.Platform, MeterUnitCatalog.Default);
        output.WriteLine($"[G12·plan] المُعَلَّق: {entries.Count(e => e.App == "store-m")} سَطراً · وغَيرَ مُعَلَّقٍ (الضابِطُ السالِب): {asActive.Count}");
        Assert.DoesNotContain(entries, e => e.App == "store-m");
        Assert.Equal(8, asActive.Count);
    }

    [Fact]
    public void The_vendor_entrys_listing_owner_never_enters_an_id_or_a_source()
    {
        var entries = Plan();
        var fields = entries.SelectMany(e => new[] { e.Id, e.SourceId }).ToList();
        output.WriteLine($"[G12·plan] حُقولٌ مَفحوصَة {fields.Count} · مَدخَلا بائِعٍ في المَتجَر 2 · سَطرُ مَداخِلِ البائِعين {entries.Count(e => e.Unit == PresenceUnits.VendorBinding)}");
        Assert.True(fields.Count > 20);
        foreach (var vendor in new[] { VendorA, VendorB })
            Assert.All(fields, f => Assert.DoesNotContain(vendor.ToString("D"), f, StringComparison.OrdinalIgnoreCase));
        // ‏الضابِطُ السالِب: مُعَرِّفُ المَدخَلِ نَفسُه يَحمِلُ البائِع — لَو كانَ هُوَ المَصدَرَ لاحمَرَّ الصَفّ.
        Assert.Contains(VendorA.ToString("D"), TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, VendorA), StringComparison.Ordinal);
    }

    [Fact]
    public void A_meter_changed_in_a_copied_definition_changes_the_plan()
    {
        var moyasar = ProviderCatalog.Find("moyasar_api")!;
        var quiet = moyasar with { Meter = new ProviderMeterDefinition { Presence = ProviderMeterPresences.None } };
        Assert.Empty(ProviderMeterValidator.Validate(quiet));
        var oto = ProviderCatalog.Find("oto_vendor_account")!;
        var otoNone = oto with { Meter = new ProviderMeterDefinition { Presence = ProviderMeterPresences.None } };
        Assert.Empty(ProviderMeterValidator.Validate(otoNone));
        var vendor = RoleCatalog.FindDefinition("vendor")! with { Meter = RoleMeterUnits.None };
        Assert.Empty(RoleMeterValidator.Validate(vendor));

        var providers = new PresenceDefinitions([.. ProviderCatalog.Definitions.Select(d => d.Slug == quiet.Slug ? quiet : d.Slug == otoNone.Slug ? otoNone : d)],
            PresenceDefinitions.Platform.RoleMeter);
        var roles = new PresenceDefinitions(ProviderCatalog.Definitions, (set, slug) => slug == vendor.Slug ? vendor.Meter! : set.MeterOf(slug));
        int Count(IReadOnlyList<UsageEntry> es, string unit, string? role = null) => es.Count(e => e.Unit == unit && (role is null || e.Role == role));

        var asIs = Plan();
        var noBindings = Plan(providers);
        var noVendor = Plan(roles);
        output.WriteLine($"[G12·plan] رَبطُ المَتجَر: كَما هُوَ {Count(asIs, PresenceUnits.ProviderBinding)} · ميسّر none {Count(noBindings, PresenceUnits.ProviderBinding)} — "
                         + $"مَداخِلُ البائِعين: {Count(asIs, PresenceUnits.VendorBinding)} · OTO none {Count(noBindings, PresenceUnits.VendorBinding)} — "
                         + $"دَورُ البائِع: {Count(asIs, PresenceUnits.Role, "vendor")} · none {Count(noVendor, PresenceUnits.Role, "vendor")}");
        Assert.Equal((1, 0), (Count(asIs, PresenceUnits.ProviderBinding), Count(noBindings, PresenceUnits.ProviderBinding)));
        Assert.Equal((1, 0), (Count(asIs, PresenceUnits.VendorBinding), Count(noBindings, PresenceUnits.VendorBinding)));
        Assert.Equal((1, 0), (Count(asIs, PresenceUnits.Role, "vendor"), Count(noVendor, PresenceUnits.Role, "vendor")));
        Assert.Equal(Count(asIs, PresenceUnits.Role) - 1, Count(noVendor, PresenceUnits.Role));
        // ‏ومِترُ «platform» لا رَبطَ لِمَتجَرٍ يُعَدُّ بِه: PayPal مَربوطاً (لا يَربِطُه مُستَأجِرٌ في الحَقيقَة) لا سَطرَ لَه.
        var paypal = PresenceReader.Entries(Day, [Fixture()[2] with { Bindings = [new(ProviderCapabilities.Payments, "paypal_subscriptions")] }], PresenceDefinitions.Platform, MeterUnitCatalog.Default);
        Assert.DoesNotContain(paypal, e => e.Unit is PresenceUnits.ProviderBinding or PresenceUnits.VendorBinding);
    }

    [Fact]
    public void Every_role_meter_value_reads_through_MeterOf_and_an_authored_role_is_derived()
    {
        var set = TenantRoleSet.Platform;
        var catalog = RoleCatalog.Definitions.Select(d => (d.Slug, Meter: set.MeterOf(d.Slug))).ToList();
        output.WriteLine($"[G12·plan] أَدوارُ الكاتالوج {catalog.Count}: {string.Join(" · ", catalog.Select(c => $"{c.Slug}={c.Meter}"))} · khayyat={set.MeterOf("khayyat")}");
        Assert.True(catalog.Count >= 10);
        Assert.All(catalog, c => Assert.Equal(RoleCatalog.FindDefinition(c.Slug)!.Meter, c.Meter));
        Assert.Equal(RoleMeterUnits.Derived, set.MeterOf("khayyat"));
        Assert.Equal(PresenceUnits.Role, RoleMeterUnits.PresenceRole);
    }

    [Theory]
    [InlineData("2026-11-12", "2026-11-13", PresenceTiming.Late)]
    [InlineData("2026-11-01", "2026-11-13", PresenceTiming.Late)]
    [InlineData("2026-11-13", "2026-11-13", PresenceTiming.Take)]
    [InlineData("2026-11-14", "2026-11-13", PresenceTiming.Early)]
    public void A_late_message_is_refused_an_early_one_waits_and_today_is_taken(string day, string today, PresenceTiming expected)
        => Assert.Equal(expected, PresenceSnapshot.Timing(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture),
                                                          DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void The_taken_line_names_the_day_the_claim_and_each_unit()
    {
        var entries = Plan();
        var won = new PresenceTaken(Day, true, 3, 1, entries, ["store-a:cache:ghost_cache"]).Line;
        var lost = new PresenceTaken(Day, false, 3, 1, [], []).Line;
        output.WriteLine($"[G12·plan] {won}");
        output.WriteLine($"[G12·plan] {lost}");
        Assert.StartsWith("[meter] presence day=2026-11-13 claimed=true stores=3 suspended=1 entries=14 (presence.api_key 1 · presence.auth_channel 2 · ", won, StringComparison.Ordinal);
        Assert.EndsWith(" unresolved=store-a:cache:ghost_cache", won, StringComparison.Ordinal);
        Assert.Equal("[meter] presence day=2026-11-13 claimed=false stores=3 suspended=1 entries=0", lost);
    }

    // ─── مَفاتيحُ API: يُعَدُّ ما يَقبَلُه الاعتِمادُ لا ما حالُه «active» (‏2026-11-13، W13-6·D-R2·١ — M6D-CNT-R2-1) ───────────────────────

    private static readonly DateTime At = new(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>مَفاتيحُ مَتجَرٍ بِكُلِّ حالٍ وانتِهاء، ومِفتاحٌ نَشِطٌ لِمَتجَرٍ آخَر.</summary>
    private static readonly (string Name, ApiKeyDocument Key, bool Usable)[] Keys =
    [
        ("نَشِطٌ بِلا انتِهاء", new() { Id = "k1", TenantSlug = "store-a", Status = ApiKeyDocument.StatusActive }, true),
        ("نَشِطٌ يَنتَهي بَعدَ اللَحظَةِ بِنَبضَة", new() { Id = "k2", TenantSlug = "store-a", Status = ApiKeyDocument.StatusActive, ExpiresAt = At.AddTicks(1) }, true),
        ("نَشِطٌ يَنتَهي عِندَ اللَحظَة", new() { Id = "k3", TenantSlug = "store-a", Status = ApiKeyDocument.StatusActive, ExpiresAt = At }, false),
        ("نَشِطٌ انتَهى قَبلَ يَوم", new() { Id = "k4", TenantSlug = "store-a", Status = ApiKeyDocument.StatusActive, ExpiresAt = At.AddDays(-1) }, false),
        ("مُبطَلٌ بِلا انتِهاء", new() { Id = "k5", TenantSlug = "store-a", Status = ApiKeyDocument.StatusRevoked }, false),
        ("مُبطَلٌ يَنتَهي غَداً", new() { Id = "k6", TenantSlug = "store-a", Status = ApiKeyDocument.StatusRevoked, ExpiresAt = At.AddDays(1) }, false),
        ("نَشِطٌ لِمَتجَرٍ آخَر", new() { Id = "k7", TenantSlug = "store-b", Status = ApiKeyDocument.StatusActive }, true),
    ];

    /// <summary><c>IsUsableAt</c> بِحَدِّ الاعتِمادِ بِحَرفِه (<c>ExpiresAt &lt;= now</c> ⇐ مُنتَهٍ): المُساوي لِلَّحظَةِ مُنتَهٍ، وما بَعدَها بِنَبضَةٍ صالِح، والمُبطَلُ لا يُنقِذُه انتِهاءٌ بَعيد.</summary>
    [Fact]
    public void A_key_is_usable_when_active_and_not_expired_at_the_instant_and_the_boundary_is_the_authenticators()
    {
        foreach (var (name, key, usable) in Keys)
        {
            output.WriteLine($"[G12·keys] {name}: IsUsableAt={key.IsUsableAt(At)} (المُثَبَّت {usable})");
            Assert.Equal(usable, key.IsUsableAt(At));
        }
        Assert.True(Keys.Length >= 7, $"أَداةٌ عَمياء: {Keys.Length} مِفتاحاً.");
    }

    /// <summary>مِفتاحا «أ» الصالِحانِ وَحدَهُما يُعَدّان — والسُؤالُ القَديمُ (الحالُ وَحدَها) كانَ يَعُدُّ أَربَعَة، فَالفَرقُ المُنتَهِيانِ النَشِطان.</summary>
    [Fact]
    public void Only_the_keys_the_authenticator_accepts_at_the_read_instant_are_counted_for_their_store()
    {
        var keys = Keys.Select(k => k.Key).ToList();
        var counted = PresenceReader.ApiKeysOf(keys, "store-a", At);
        var other = PresenceReader.ApiKeysOf(keys, "store-b", At);
        var statusOnly = keys.Count(k => k.TenantSlug == "store-a" && k.Status == ApiKeyDocument.StatusActive);
        var later = PresenceReader.ApiKeysOf(keys, "store-a", At.AddTicks(1));
        output.WriteLine($"[G12·keys] store-a: يُعَدّ {counted} · بِالحالِ وَحدَها {statusOnly} · بَعدَ اللَحظَةِ بِنَبضَة {later} · store-b {other}");
        Assert.Equal(2, counted);
        Assert.Equal(4, statusOnly);
        Assert.Equal(1, later);
        Assert.Equal(1, other);
        Assert.Equal(0, PresenceReader.ApiKeysOf(keys, "store-z", At));
    }
}
