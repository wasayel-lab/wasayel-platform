namespace ACommerce.Platform.Flows;

/// <summary>شَكلُ القاعِدَة ٤ حَرفاً: رَمزٌ ثابِتٌ ورِسالَةٌ عَرَبِيَّة.</summary>
public sealed record FlowMeterViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>ما يَحتاجُه المُصادِقُ مِن مُعجَمِ الوَحَدات — يُعطيه المُنادي</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٨ — ‏ADR-051 §٢٢): حَدُّ كُلِّ وَحدَةٍ
/// بِرَمزِها، واسمُ حَدِّ الحِفظ.</para>
/// <para><b>ولِماذا يُعطى ولا يُقرَأ</b>: هذه المَكتَبَةُ وَرَقَةٌ بِلا مَرجِعِ مَشروعٍ ولا حُزمَة — «شَرطٌ لا تَزيين» في مِلَفِّ مَشروعِها، لِأَنّ عُدَدَ المَظهَرِ
/// والاشتِراكاتِ والمُزَوِّدينَ تُشيرُ إلَيها. والمُعجَمُ في <c>ACommerce.Platform.Shared</c> يَحمِلُ إطارَ ASP.NET؛ فَلَو أَشارَت إلَيه لَتَسَرَّبَ الإطارُ إلى
/// كُلِّ مَن يُشيرُ إلَيها. فَالمُنادي (مُستَمِعُ العَدِّ في القالِب، والاختِبار) يَبني هذا السِجِلَّ مِن المُعجَمِ ويُعطيه — قِيَمٌ لا مَرجِع.</para>
/// </summary>
/// <param name="BoundaryOf">حَدُّ كُلِّ وَحدَةٍ في المُعجَمِ بِرَمزِها (حَسّاساً لِلحالَة) — والرَمزُ الغائِبُ لَيسَ وَحدَة.</param>
/// <param name="Persistence">اسمُ حَدِّ الحِفظِ في ذلكَ المُعجَم — الحَدُّ الوَحيدُ الَّذي يُعَدُّ عِندَ انتِقالِ تَدَفُّق.</param>
public sealed record FlowMeterUnits(IReadOnlyDictionary<string, string> BoundaryOf, string Persistence);

/// <summary>
/// <para><b>مِترُ انتِقالِ التَدَفُّق ومُصادِقُه</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٨ — ‏ADR-051 §٢٢؛ التَصميم §7 و§12 G7): كُلُّ انتِقالٍ في مِلَفِّ
/// تَدَفُّقٍ يُعلِنُ ما يَعُدُّه — رَمزُ وَحدَةٍ حَدُّها الحِفظ، أَو <see cref="None"/>. ورُموزُ خَرقِه ثَلاثَة، ولِكُلٍّ اختِبارٌ موجِبٌ وسالِب (القاعِدَة ٤):
/// <c>meter_missing</c> (لا مِتر)، و<c>meter_unit_unknown</c> (لَيسَ وَحدَةً في المُعجَم)، و<c>meter_unit_not_persistence</c> (وَحدَةٌ تُعَدُّ عِندَ حَدٍّ آخَر:
/// صادِرٌ أَو حُضور — فَانتِقالٌ لا يَعُدُّ نِداءَ مُزَوِّدٍ ولا حُضورَ يَوم).</para>
/// <para><b>دالَّةٌ مُنفَصِلَةٌ لا عَلَم</b>: <see cref="FlowDefinitionValidator"/> بِرُموزِه العِشرين لا يُمَسّ، والقارِئُ (<c>FlowDefinitionLoader</c>) لا يُصادِقُ
/// بِقَصدِه المَكتوب — فَيَسأَلُ هذا المُصادِقَ مَن يَقرَأُ المِتر: مُستَمِعُ العَدِّ عِندَ تَركيبِه (يَرمي بِكُلِّ الخُروقِ فَيَفشَلُ الإقلاع، كَمُحَمِّلَي الأَدوارِ
/// والمُزَوِّدين)، و<c>FlowMeterTests</c>. ‏(ADR-038: مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه — خَرقٌ لِكُلِّ انتِقالٍ خارِق.)</para>
/// <para><b>وما يُقرَأُ في وَقتِ التَشغيلِ مِترُ الوِلادَةِ وَحدَه</b> (<see cref="GenesisMeters"/>): الانتِقالاتُ الأُخرى <see cref="None"/> كُلُّها — عَدُّها
/// وَحدَةٌ لاحِقَةٌ بِمُعَرِّفِ مَوضِعِها في المَجرى (التَصميم §5.6)، ويُثَبِّتُ ذلكَ <c>MeterEmitterTests</c> (G15).</para>
/// </summary>
public static class FlowMeter
{
    /// <summary>لا يُعَدُّ هذا الانتِقال — قيمَةٌ تُكتَبُ صَراحَةً لا غِياب.</summary>
    public const string None = "none";

    /// <summary>رُموزُ <see cref="Validate"/> الثَلاثَة — مُعلَنَةً لِيُقاسَ أَنّ لِكُلٍّ اختِبارَين.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        "meter_missing",
        "meter_unit_unknown",
        "meter_unit_not_persistence",
    ];

    /// <summary>كُلُّ خُروقِ مِترِ انتِقالاتِ تَدَفُّقٍ بِمُعجَمٍ مُعطىً — والقائِمَةُ الفارِغَةُ مِترٌ صالِحٌ لِكُلِّ انتِقال.</summary>
    public static IReadOnlyList<FlowMeterViolation> Validate(FlowDefinition d, FlowMeterUnits units)
    {
        var v = new List<FlowMeterViolation>();
        foreach (var t in d.Transitions)
        {
            var meter = t.Meter;
            var at = $"«{t.From} → {t.To}» في التَدَفُّق «{d.Flow}»";

            if (string.IsNullOrWhiteSpace(meter))
            {
                v.Add(new("meter_missing",
                    $"الانتِقال {at} بِلا مِتر — كُلُّ انتِقالٍ يُعلِنُ ما يَعُدُّه (وَحدَةٌ حَدُّها «{units.Persistence}» أَو \"{None}\")، و«{None}» قيمَةٌ تُكتَبُ لا غِياب."));
                continue;
            }

            if (string.Equals(meter, None, StringComparison.Ordinal))
                continue;

            if (!units.BoundaryOf.TryGetValue(meter, out var boundary))
                v.Add(new("meter_unit_unknown",
                    $"مِترُ الانتِقال {at} = «{meter}» لَيسَ وَحدَةً في مُعجَمِ الوَحَدات ولا «{None}»."));
            else if (!string.Equals(boundary, units.Persistence, StringComparison.Ordinal))
                v.Add(new("meter_unit_not_persistence",
                    $"مِترُ الانتِقال {at} = «{meter}» وَحدَةٌ تُعَدُّ عِندَ الحَدّ «{boundary}» — مِترُ الانتِقالِ وَحدَةٌ حَدُّها «{units.Persistence}» أَو «{None}»."));
        }
        return v;
    }

    public static bool IsValid(FlowDefinition d, FlowMeterUnits units) => Validate(d, units).Count == 0;

    /// <summary>
    /// <para><b>مِترُ وِلادَةِ التَدَفُّق</b>: مِترُ كُلِّ انتِقالٍ مُنشِئٍ (<see cref="FlowVocabulary.Genesis"/>) بِتَرتيبِه كَما كُتِب — وهُوَ ما يَقرَؤُه مُستَمِعُ
    /// العَدِّ عِندَ تَركيبِه لِيَعرِفَ بِأَيِّ وَحدَةٍ تُعَدُّ وِلادَةُ هذا التَدَفُّق. والمِترُ الغائِبُ نَصٌّ فارِغٌ لا يُتَخَطّى: يَراه المُنادي ولا يَبتَلِعُه.</para>
    /// </summary>
    public static IReadOnlyList<string> GenesisMeters(FlowDefinition d)
        => d.Transitions
            .Where(t => string.Equals(t.From, FlowVocabulary.Genesis, StringComparison.Ordinal))
            .Select(t => t.Meter ?? "")
            .ToArray();
}
