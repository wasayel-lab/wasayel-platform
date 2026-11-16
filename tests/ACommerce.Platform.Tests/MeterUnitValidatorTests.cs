using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G1 — مُصادِقُ مُعجَمِ الوَحَدات: عَشَرَةُ رُموزٍ لِلصَفّ واثنانِ لِلمُعجَم، لِكُلٍّ مُوجِبٌ وسالِب، وحارِسُ عَمىً لِكُلِّ مَجموعَة ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §3.3). الصُفوفُ بِحَرفِ جَدوَلِ التَصميم؛ والحارِسُ يَبني ما أُنتِجَ مِن الخُروقِ
// نَفسِها لا مِن نَصٍّ مَكتوبٍ بِاليَد (نَمَطُ ProviderDefinitionValidatorTests).

public class MeterUnitValidatorTests(ITestOutputHelper output)
{
    private static MeterUnit Valid() => new()
    {
        Code = "deal.create", Side = MeterSides.Operation, Boundary = MeterBoundaries.Persistence, Measure = MeterMeasures.Count,
        Feature = "deal_status", Reconcile = MeterReconcilers.DealIds,
        Source = "DealsService.StartAsync — Deal stored with VersionFor = null",
    };

    private static IReadOnlyList<string> CodesOf(MeterUnit u) => MeterUnitValidator.Validate(u).Select(v => v.Code).ToList();

    private static IReadOnlyList<string> CatalogCodesOf(params MeterUnit[] units)
        => MeterUnitValidator.ValidateCatalog(units).Select(v => v.Code).ToList();

    /// <summary>الصَفُّ السالِبُ والمُوجِبُ لِكُلِّ رَمز — بِحَرفِ جَدوَلِ التَصميم §3.3.</summary>
    private static IReadOnlyList<(string Code, MeterUnit Negative, MeterUnit Positive)> RowCases() => RowCaseList().ToList();

    private static IEnumerable<(string Code, MeterUnit Negative, MeterUnit Positive)> RowCaseList()
    {
        yield return Case("unit_code_empty", Valid() with { Code = "" }, Valid() with { Code = "deal.create" });
        yield return Case("unit_code_pattern", Valid() with { Code = "Deal" }, Valid() with { Code = "deal.create" });
        yield return Case("unit_code_pattern", Valid() with { Code = "deal" }, Valid() with { Code = "deal.create" });
        yield return Case("unit_side_out_of_vocabulary", Valid() with { Side = "tech" },
            Valid() with { Side = MeterSides.Technology, Boundary = MeterBoundaries.Presence });
        yield return Case("unit_boundary_out_of_vocabulary", Valid() with { Boundary = "http" }, Valid() with { Boundary = MeterBoundaries.Outbound });
        yield return Case("unit_measure_out_of_vocabulary", Valid() with { Measure = "seconds" }, Valid() with { Measure = MeterMeasures.Bytes });
        yield return Case("unit_side_contradicts_boundary", Valid() with { Side = MeterSides.Operation, Boundary = MeterBoundaries.Presence },
            Valid() with { Side = MeterSides.Operation, Boundary = MeterBoundaries.Persistence });
        yield return Case("unit_side_contradicts_boundary", Valid() with { Side = MeterSides.Technology, Boundary = MeterBoundaries.Persistence },
            Valid() with { Side = MeterSides.Technology, Boundary = MeterBoundaries.Presence });
        yield return Case("unit_feature_missing", Valid() with { Feature = null }, Valid() with { Feature = "listings" });
        yield return Case("unit_feature_anchor_unknown", Valid() with { Feature = "@role" }, Valid() with { Feature = MeterFeatureAnchors.ProviderCapability });
        yield return Case("unit_reconcile_unknown", Valid() with { Reconcile = "ledger" }, Valid() with { Reconcile = MeterReconcilers.DealIds });
        yield return Case("unit_reconcile_unknown", Valid() with { Reconcile = "event_ids:listingCreated" }, Valid() with { Reconcile = "event_ids:ListingCreated" });
        yield return Case("unit_source_missing", Valid() with { Source = "" }, Valid() with { Source = "MarketplaceTemplateExtensions — any site" });
    }

    private static (string, MeterUnit, MeterUnit) Case(string code, MeterUnit negative, MeterUnit positive) => (code, negative, positive);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    public void Each_row_code_fires_on_its_negative_row_and_not_on_its_positive_row(int row)
    {
        var (code, negative, positive) = RowCases()[row];
        output.WriteLine($"[G1] {code}: سالِبٌ «{negative.Code}/{negative.Side}/{negative.Boundary}/{negative.Measure}/{negative.Feature}/{negative.Reconcile}»");
        Assert.Contains(code, CodesOf(negative));
        Assert.DoesNotContain(code, CodesOf(positive));
        Assert.Empty(CodesOf(positive));
    }

    /// <summary>الصُفوفُ ثَلاثَةَ عَشَرَ والنَظَريَّةُ تَمشيها كُلَّها — صَفٌّ يُضافُ بِلا رَقمٍ في النَظَريَّةِ يَحمَرُّ هُنا.</summary>
    [Fact]
    public void The_theory_walks_every_row_case() => Assert.Equal(13, RowCases().Count);

    [Fact]
    public void The_reference_row_and_the_embedded_lexicon_are_valid()
    {
        Assert.Empty(CodesOf(Valid()));
        Assert.True(MeterUnitValidator.IsValid(Valid()));
        var units = MeterUnitCatalog.Default.Units;
        output.WriteLine($"[G1] وَحَداتُ المُعجَمِ المَضمون: {units.Count} — {string.Join(", ", units.Select(u => u.Code))}");
        Assert.True(units.Count > 0, "أَداةٌ عَمياء: صِفرُ وَحدَة.");
        Assert.All(units, u => Assert.Empty(CodesOf(u)));
        Assert.Empty(MeterUnitValidator.ValidateCatalog(units));
    }

    /// <summary><b>مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه</b> (‏ADR-038): صَفٌّ بِأَربَعَةِ خُروقٍ يُقالُ بِأَربَعَتِها.</summary>
    [Fact]
    public void A_row_with_several_breaches_answers_all_of_them()
    {
        var codes = CodesOf(Valid() with { Code = "Deal", Side = "tech", Measure = "seconds", Source = "" });
        Assert.Equal(new[] { "unit_code_pattern", "unit_side_out_of_vocabulary", "unit_measure_out_of_vocabulary", "unit_source_missing" }, codes);
    }

    [Fact]
    public void The_two_sets_have_their_declared_sizes_and_no_duplicate()
    {
        Assert.Equal(10, MeterUnitValidator.Codes.Count);
        Assert.Equal(10, MeterUnitValidator.Codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, MeterUnitValidator.CatalogCodes.Count);
        Assert.Empty(MeterUnitValidator.Codes.Intersect(MeterUnitValidator.CatalogCodes));
    }

    // ─── المُعجَم: رَمزانِ، مُوجِبٌ وسالِب ─────────────────────────────────────────────────────────────

    [Fact]
    public void units_code_duplicate_fires_on_two_rows_with_one_code_and_not_on_distinct_codes()
    {
        Assert.Contains("units_code_duplicate", CatalogCodesOf(Valid(), Valid()));
        Assert.Empty(CatalogCodesOf(Valid(), Valid() with { Code = "listing.create" }));
    }

    [Fact]
    public void units_label_key_collision_fires_when_two_codes_map_to_one_dictionary_key_and_not_otherwise()
    {
        Assert.Equal("meter.unit.a_b_c", MeterUnitCatalog.LabelKey("a_b.c"));
        Assert.Equal("meter.unit.a_b_c", MeterUnitCatalog.LabelKey("a.b_c"));
        Assert.Equal(new[] { "units_label_key_collision" }, CatalogCodesOf(Valid() with { Code = "a_b.c" }, Valid() with { Code = "a.b_c" }));
        Assert.Empty(CatalogCodesOf(Valid() with { Code = "a.b" }, Valid() with { Code = "a.c" }));
    }

    // ─── حارِسُ العَمى: كُلُّ رَمزٍ مُعلَنٍ يُنتِجُه خَرقٌ ما ───────────────────────────────────────────────────

    [Fact]
    public void Every_declared_row_code_is_produced_by_some_breaching_row()
    {
        var produced = RowCases().SelectMany(c => CodesOf(c.Negative)).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"[G1] رُموزُ الصَفِّ المُعلَنَة {MeterUnitValidator.Codes.Count} · المُنتَجَةُ مِن {RowCases().Count()} صَفّاً سالِباً: {produced.Count}");
        Assert.True(produced.Count > 0, "أَداةٌ عَمياء: صِفرُ خَرقٍ أُنتِج.");
        var never = MeterUnitValidator.Codes.Where(c => !produced.Contains(c)).ToArray();
        Assert.True(never.Length == 0, "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ صَفّ — يُحذَفُ أَو يُحرَس:\n  " + string.Join("\n  ", never));
        Assert.Empty(produced.Except(MeterUnitValidator.Codes));
    }

    [Fact]
    public void Every_declared_catalog_code_is_produced_by_some_breaching_catalog()
    {
        var produced = new[]
        {
            CatalogCodesOf(Valid(), Valid()),
            CatalogCodesOf(Valid() with { Code = "a_b.c" }, Valid() with { Code = "a.b_c" }),
        }.SelectMany(c => c).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"[G1] رُموزُ المُعجَمِ المُعلَنَة {MeterUnitValidator.CatalogCodes.Count} · المُنتَجَة {produced.Count}");
        Assert.True(produced.Count > 0, "أَداةٌ عَمياء: صِفرُ خَرقٍ أُنتِج.");
        var never = MeterUnitValidator.CatalogCodes.Where(c => !produced.Contains(c)).ToArray();
        Assert.True(never.Length == 0, "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ مُعجَم:\n  " + string.Join("\n  ", never));
    }
}
