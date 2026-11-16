using System.Text.RegularExpressions;

namespace ACommerce.Platform.Shared.Metering;

/// <summary>خَرقٌ واحِدٌ في صَفِّ وَحدَةٍ أَو في المُعجَمِ كُلِّه — شَكلُ القاعِدَة ٤: <c>Code</c> ثابِتٌ لِلاختِباراتِ واللوغ، و<c>MessageAr</c>
/// لِلمُراجِع.</summary>
public sealed record MeterUnitViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مُصادِقُ مُعجَمِ الوَحَدات</b> — دَوالُّ نَقِيَّة بِلا قاعِدَةٍ ولا وَقت. <b>ومُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه</b> (‏ADR-038):
/// صَفٌّ فاسِدٌ يُقالُ بِكُلِّ رُموزِه لا بِأَوَّلِها.</para>
/// <para>مُستَويان: <see cref="Validate"/> لِلصَفِّ (<see cref="Codes"/>، عَشَرَة)، و<see cref="ValidateCatalog"/> لِلمُعجَمِ كُلِّه
/// (<see cref="CatalogCodes"/>، اثنان). ولِكُلِّ رَمزٍ اختِبارٌ مُوجِبٌ وسالِب، وحارِسُ عَمىً يُثبِتُ أَنّ كُلَّ رَمزٍ يُنتِجُه خَرقٌ ما
/// (<c>MeterUnitValidatorTests</c>).</para>
/// <para><b>وهو مَفروضٌ لا مُتاح</b>: <see cref="MeterUnitCatalog.LoadEmbedded"/> يُمَرِّرُ كُلَّ صَفٍّ والمُعجَمَ مِن هُنا ويَرمي — فَصَفٌّ فاسِدٌ
/// يُفشِلُ الإقلاعَ (‏<c>MeterBoot</c> قَبلَ البَذر) بِرُموزِه.</para>
/// </summary>
public static class MeterUnitValidator
{
    /// <summary>رَمزُ الوَحدَة: مَقطَعانِ فَأَكثَرُ بِنُقطَة، كُلٌّ بِحَرفٍ صَغيرٍ ثُمَّ حُروفٍ وأَرقامٍ وشَرطَةٍ سُفلِيَّة.</summary>
    private static readonly Regex CodePattern =
        new(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>رُموزُ خَرقِ الصَفّ — عَشَرَة.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        "unit_code_empty",
        "unit_code_pattern",
        "unit_side_out_of_vocabulary",
        "unit_boundary_out_of_vocabulary",
        "unit_measure_out_of_vocabulary",
        "unit_side_contradicts_boundary",
        "unit_feature_missing",
        "unit_feature_anchor_unknown",
        "unit_reconcile_unknown",
        "unit_source_missing",
    ];

    /// <summary>رُموزُ خَرقِ المُعجَم — اثنان.</summary>
    public static readonly IReadOnlyList<string> CatalogCodes =
    [
        "units_code_duplicate",
        "units_label_key_collision",
    ];

    /// <summary>كُلُّ خُروقِ الصَفّ — والقائِمَةُ الفارِغَةُ صَفٌّ صالِح.</summary>
    public static IReadOnlyList<MeterUnitViolation> Validate(MeterUnit u)
    {
        var v = new List<MeterUnitViolation>();

        if (string.IsNullOrWhiteSpace(u.Code))
            v.Add(new("unit_code_empty", "وَحدَةٌ بِلا رَمز."));
        else if (!CodePattern.IsMatch(u.Code))
            v.Add(new("unit_code_pattern", $"رَمزُ الوَحدَة «{u.Code}» خارِجَ النَمَط {CodePattern}."));

        var sideKnown = MeterSides.All.Contains(u.Side, StringComparer.Ordinal);
        var boundaryKnown = MeterBoundaries.All.Contains(u.Boundary, StringComparer.Ordinal);
        if (!sideKnown)
            v.Add(new("unit_side_out_of_vocabulary", $"جانِبُ «{u.Code}» = «{u.Side}» خارِجَ المُعجَم: {string.Join("، ", MeterSides.All)}."));
        if (!boundaryKnown)
            v.Add(new("unit_boundary_out_of_vocabulary", $"حَدُّ «{u.Code}» = «{u.Boundary}» خارِجَ المُعجَم: {string.Join("، ", MeterBoundaries.All)}."));
        if (!MeterMeasures.All.Contains(u.Measure, StringComparer.Ordinal))
            v.Add(new("unit_measure_out_of_vocabulary", $"مِقياسُ «{u.Code}» = «{u.Measure}» خارِجَ المُعجَم: {string.Join("، ", MeterMeasures.All)}."));

        // التِقنِيَةُ حُضورٌ والحُضورُ تِقنِيَة — والخَلطُ هُوَ ما يَمنَعُه مَبدَأُ المالِك.
        if (sideKnown && boundaryKnown && (u.Side == MeterSides.Technology) != (u.Boundary == MeterBoundaries.Presence))
            v.Add(new("unit_side_contradicts_boundary",
                $"«{u.Code}»: الجانِبُ «{u.Side}» يُناقِضُ الحَدَّ «{u.Boundary}» — التِقنِيَةُ حُضورٌ والحُضورُ تِقنِيَة."));

        if (string.IsNullOrWhiteSpace(u.Feature))
            v.Add(new("unit_feature_missing", $"«{u.Code}» بِلا مِيزَة."));
        else if (MeterFeatureAnchors.IsAnchor(u.Feature) && !MeterFeatureAnchors.All.Contains(u.Feature, StringComparer.Ordinal))
            v.Add(new("unit_feature_anchor_unknown",
                $"مِرساةُ «{u.Code}» = «{u.Feature}» خارِجَ المُعجَم: {string.Join("، ", MeterFeatureAnchors.All)}."));

        if (!MeterReconcilers.IsKnown(u.Reconcile))
            v.Add(new("unit_reconcile_unknown", $"قارِئُ مُطابَقَةِ «{u.Code}» = «{u.Reconcile}» خارِجَ القائِمَةِ المُغلَقَة."));

        if (string.IsNullOrWhiteSpace(u.Source))
            v.Add(new("unit_source_missing", $"«{u.Code}» بِلا مَوضِعِ عَدّ — وَحدَةٌ بِلا مَوضِعٍ يَعُدُّها لا تَدخُل."));

        return v;
    }

    public static bool IsValid(MeterUnit u) => Validate(u).Count == 0;

    /// <summary>خُروقُ المُعجَمِ كُلِّه: رَمزٌ مُكَرَّر، ورَمزانِ يَقَعانِ عَلى مِفتاحِ قامُوسٍ واحِد (<see cref="MeterUnitCatalog.LabelKey"/>).</summary>
    public static IReadOnlyList<MeterUnitViolation> ValidateCatalog(IReadOnlyList<MeterUnit> units)
    {
        var v = new List<MeterUnitViolation>();
        foreach (var dup in units.GroupBy(u => u.Code, StringComparer.Ordinal).Where(g => g.Count() > 1))
            v.Add(new("units_code_duplicate", $"الوَحدَةُ «{dup.Key}» مُكَرَّرَةٌ {dup.Count()} مَرّات."));
        foreach (var clash in units.Select(u => u.Code).Distinct(StringComparer.Ordinal)
                     .GroupBy(MeterUnitCatalog.LabelKey, StringComparer.Ordinal).Where(g => g.Count() > 1))
            v.Add(new("units_label_key_collision",
                $"الوَحَداتُ {string.Join("، ", clash.Select(c => $"«{c}»"))} تَقَعُ عَلى مِفتاحِ القامُوسِ نَفسِه «{clash.Key}»."));
        return v;
    }
}
