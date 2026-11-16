using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفٌ: استِثناءُ المُستَورِدِ في المُطابَقَة — ما لا يَتَغَيَّرُ حينَ يَشمَلُ كُلَّ وَحدَةِ حَدَث ══════════════════════════════════════════════════
// (‏2026-11-14، الموجَة ١٣ W13-6·E-R3·١ — مُراجَعَةُ إغلاقِ المَرحَلَةِ هـ الثالِثَة: M6E-CNT-R3-1؛ ADR-051 §٢٤؛ التَصميم §9، القاعِدَتانِ ٣ و١٣).
// **لِماذا هذا المِلَفّ**: `MeterSourceReaders.ImporterReconcilers` قائِمَةٌ مَكتوبَةٌ بِأَنواعِ ما يَكتُبُه المُستَورِدُ بِلا مُستَمِع — وإعادَةُ الاستيرادِ
// (`TargetWriter.ResetTenantAsync`) تَحذِفُ **كُلَّ** أَحداثِ المَتجَرِ بِجُملَةٍ خامّة (`DELETE FROM platform.mt_events WHERE tenant_id = @t`)، فَعَرضٌ عَدَّه المُستَمِعُ
// في مَتجَرٍ مُستَورَدٍ يَبقى سَطرُه يَتيماً بَعدَ الإعادَة. **والتَبديلُ التالي يَشتَقُّ شَطرَ الأَحداثِ مِن `MeterRules.StreamRules`** فَتَدخُلُ
// `event_ids:OfferSubmitted` — وهذا المِلَفُّ يُثَبِّتُ قَبلَه ما يَجِبُ أَلّا يَتَغَيَّر، ولا يُمَسُّ بَعدَه حَرفاً (القاعِدَة ٣):
//   (أ) كُلُّ قيمَةِ مُطابَقَةٍ غَيرِ `event_ids:OfferSubmitted` عَلى حالِها: السِتُّ المُستَثناةُ اليَومَ مُستَثناة، والخَمسُ الباقِيَةُ (الصَفقَةُ وحِسابُ
//       الاستوديو والتَقييمُ والنِيّاتُ والحُضور) تُقارَنُ في مَتجَرِ المُستَورِدِ كَغَيرِه؛
//   (ب) جَداوِلُ `ImportedRecord` الَّتي تَكتُبُها المَنَصَّةُ لا تَجعَلُ مَتجَراً مُستَورَداً — أَسماؤُها الأَربَعَةُ بِحُروفِها؛
//   (ج) الحُكمُ النَقِيُّ عَلى مَتجَرٍ مُستَثنًى: مَصادِرُه وسُطورُه لا تُقارَن، ومَصادِرُه تُعَدُّ «importer-excluded» في سَطرِ الوَحدَة، وغَيرُه يُقارَنُ بِحَرفِه.
// ولا يُثَبِّتُ ما يَتَغَيَّرُ عَمداً: عُضوِيَّةُ `event_ids:OfferSubmitted` (صَفُّها في `MeterReconciliationTests` و`LiveMeterReconciliationProofTests` مَعَ التَبديل).
public class MeterImporterExclusionCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>كُلُّ قيمَةِ مُطابَقَةٍ في المُعجَمِ إلّا الَّتي يُغَيِّرُها التَبديل — وعُضوِيَّتُها في الاستِثناءِ اليَوم.</summary>
    private static readonly (string Reconcile, bool Excluded)[] Pinned =
    [
        (MeterReconcilers.TenantIds, true),
        (MeterReconcilers.UserIds, true),
        (MeterReconcilers.MessageIds, true),
        (MeterReconcilers.EventIdsPrefix + "ListingCreated", true),
        (MeterReconcilers.EventIdsPrefix + "SubscriptionCreated", true),
        (MeterReconcilers.EventIdsPrefix + "TicketCreated", true),
        (MeterReconcilers.DealIds, false),
        (MeterReconcilers.StudioUserIds, false),
        (MeterReconcilers.ReviewIds, false),
        (MeterReconcilers.Intents, false),
        (MeterReconcilers.PresenceClaims, false),
    ];

    /// <summary>(أ) السِتُّ مُستَثناةٌ والخَمسُ تُقارَن — وكُلُّ قيمَةٍ مُثَبَّتَةٍ قيمَةُ وَحدَةٍ في المُعجَم، فَلا يُثَبَّتُ ما لا يوجَد.</summary>
    [Fact]
    public void Every_reconcile_value_but_the_offer_keeps_its_place_in_or_out_of_the_importer_exclusion()
    {
        var catalog = MeterUnitCatalog.Default.Units.Select(u => u.Reconcile).ToHashSet(StringComparer.Ordinal);
        var actual = Pinned.Select(p => (p.Reconcile, Excluded: MeterSourceReaders.ImporterReconcilers.Contains(p.Reconcile, StringComparer.Ordinal))).ToList();
        foreach (var (r, e) in actual) output.WriteLine($"[char·importer] {r} · {(e ? "مُستَثنًى" : "يُقارَن")}");
        output.WriteLine($"[char·importer] قِيَمٌ مُثَبَّتَة {Pinned.Length} · في المُعجَم {Pinned.Count(p => catalog.Contains(p.Reconcile))} · مُستَثناة {actual.Count(a => a.Excluded)}");
        Assert.True(Pinned.Length >= 11, $"أَداةٌ عَمياء: {Pinned.Length} قيمَة.");
        Assert.All(Pinned, p => Assert.Contains(p.Reconcile, catalog));
        Assert.Equal(Pinned.Select(p => (p.Reconcile, p.Excluded)), actual);
    }

    /// <summary>(ب) جَداوِلُ المَنَصَّةِ الأَربَعَةُ بِحُروفِها.</summary>
    [Fact]
    public void The_platform_written_imported_record_tables_stay_the_four_that_do_not_make_a_store_imported()
    {
        output.WriteLine($"[char·importer] جَداوِلُ المَنَصَّة: {string.Join(", ", MeterSourceReaders.PlatformImportedTables)}");
        Assert.Equal(["AttributeDefinitions", "CategoryAttributeMappings", "AttributeValues", "DiscoveryRegions"], MeterSourceReaders.PlatformImportedTables);
    }

    /// <summary>(ج) المُستَثنى لا يُقارَن ويُعَدُّ مَصدَرُه، وغَيرُه بِحَرفِه — وسَطرُ الوَحدَةِ بِحُروفِه.</summary>
    [Fact]
    public void An_excluded_store_is_neither_missing_nor_orphan_and_its_sources_are_counted_on_the_unit_line()
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal) { "imp" };
        var sources = new MeterSource[] { new("imp", "listing.create:imp:1", true), new("imp", "listing.create:imp:2", true), new("own", "listing.create:own:3", true) };
        var entries = new MeterLedgerRow[] { new("listing.create:imp:9", "listing.create", "imp", "_none"), new("listing.create:own:3", "listing.create", "own", "_none") };
        var c = MeterReconciliation.Compare("listing.create", MeterReconcilers.EventIdsPrefix + "ListingCreated", true, sources, entries, excluded);
        var none = MeterReconciliation.Compare("listing.create", MeterReconcilers.EventIdsPrefix + "ListingCreated", true, sources, entries, new HashSet<string>(StringComparer.Ordinal));
        var lines = new MeterReconciliationReport(MeterReconcileState.Data, null, [c], [], null, 0, [],
            new MeterPresenceCheck(1, 1, [], 0), [], ["imp"], 1).Lines();
        foreach (var l in lines) output.WriteLine($"[char·importer] {l}");
        Assert.Equal((1, 0, 0, 2), (c.Compared, c.Missing, c.Orphans, c.Excluded));
        Assert.Equal((3, 2, 1, 0), (none.Compared, none.Missing, none.Orphans, none.Excluded));
        Assert.Contains("[meter-reconcile] listing.create · compared 1 · missing 0 · orphans 0 · zero-entry stores 0 · seed 0 · importer-excluded 2", lines);
        Assert.Contains("[meter-reconcile] exclusions · seed 0 · importer tenants 1 (imp)", lines);
    }
}
