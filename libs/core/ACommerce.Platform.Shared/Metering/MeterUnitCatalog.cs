using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>مُعجَمُ الوَحَداتِ المُحَمَّل</b> — مِن مَورِدٍ مَضمونٍ في هذه المَكتَبَة (<c>Metering/Definitions/meter-units.json</c>)، بِخِياراتِ
/// <c>RoleDefinitionLoader</c> و<c>ProviderDefinitionLoader</c> حَرفاً: <c>CamelCase</c>، و<c>UnmappedMemberHandling.Disallow</c> — <b>مِفتاحٌ
/// مَجهولٌ (ومِنه أَيُّ «price») يَرمي ولا يُتَجاهَل</b> — وبِلا تَعليقاتٍ ولا فاصِلَةٍ زائِدَة. ثُمَّ <see cref="MeterUnitValidator"/> لِكُلِّ صَفٍّ
/// ولِلمُعجَم، ويَرمي بِالرُموز.</para>
/// <para><b>ولِماذا مَورِدٌ مَضمون</b>: بِحُجَّةِ الأَدوارِ والمُزَوِّدينَ نَفسِها — القارِئُ يَعمَلُ تَحتَ التَطبيقِ والمُجَمِّعِ ومُشَغِّلِ الاختِبارات،
/// ولِكُلٍّ مَسارُ أَساسٍ آخَر؛ والمَورِدُ لا مَسارَ لَه.</para>
/// </summary>
public sealed class MeterUnitCatalog
{
    private const string ResourceSuffix = ".Metering.Definitions.meter-units.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private sealed record UnitsFile
    {
        public IReadOnlyList<MeterUnit> Units { get; init; } = [];
    }

    private static readonly Lazy<MeterUnitCatalog> Embedded = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, MeterUnit> _byCode;

    public IReadOnlyList<MeterUnit> Units { get; }

    private MeterUnitCatalog(IReadOnlyList<MeterUnit> units)
    {
        Units = units;
        _byCode = units.ToDictionary(u => u.Code, StringComparer.Ordinal);
    }

    /// <summary>المُعجَمُ المَضمون، يُحَمَّلُ مَرَّةً في العَمَلِيَّة — ويَرمي (ويَبقى يَرمي) إن فَسَد.</summary>
    public static MeterUnitCatalog Default => Embedded.Value;

    public MeterUnit? Find(string code) => _byCode.GetValueOrDefault(code);

    /// <summary><b>مِفتاحُ القامُوسِ لِتَسمِيَةِ الوَحدَة</b> — <c>meter.unit.</c> + الرَمزُ بِشَرطَةٍ سُفلِيَّةٍ مَكانَ النُقطَة: نَمَطُ مَفاتيحِ
    /// القامُوسِ ثَلاثَةُ مَقاطِعَ بِالضَبط (<c>LocaleValidator</c>)، والرَمزُ قَد يَحمِلُ نُقطَتَين فَأَكثَر (RULES-15). ورَمزانِ يَقَعانِ عَلى مِفتاحٍ
    /// واحِدٍ خَرقٌ في المُعجَم (<c>units_label_key_collision</c>).</summary>
    public static string LabelKey(string code) => "meter.unit." + code.Replace('.', '_');

    /// <summary>يُحَمِّلُ المَورِدَ المَضمونَ ويُصادِقُه — ويَرمي بِرُموزِ كُلِّ خَرق.</summary>
    public static MeterUnitCatalog LoadEmbedded()
    {
        var asm = typeof(MeterUnitCatalog).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"مَورِدُ مُعجَمِ الوَحَدات «{ResourceSuffix}» غَيرُ مَضمونٍ في {asm.GetName().Name}.");
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"تَعَذَّرَ فَتحُ المَورِد «{name}».");
        using var reader = new StreamReader(stream);
        return From(Parse(reader.ReadToEnd()));
    }

    /// <summary>يَقرَأُ نَصَّ المُعجَمِ بِخِياراتِ <see cref="LoadEmbedded"/> نَفسِها <b>ولا يُصادِق</b> — لِيَبقى الفَصلُ بَينَ «تَعَذَّرَت القِراءَة»
    /// (مِفتاحٌ مَجهول ← استِثناء) و«قُرِئَ وخالَف» (رُموز). نَمَطُ <c>RoleDefinitionLoader.ParseDefinition</c>.</summary>
    public static IReadOnlyList<MeterUnit> Parse(string json)
        => (JsonSerializer.Deserialize<UnitsFile>(json, Options) ?? throw new InvalidOperationException("نَصُّ مُعجَمِ الوَحَداتِ أَعطى null.")).Units;

    /// <summary>يُصادِقُ كُلَّ صَفٍّ والمُعجَمَ كُلَّه، ويَرمي بِالرُموزِ كُلِّها؛ ومُعجَمٌ فارِغٌ خَرقٌ أَيضاً — عَدٌّ بِلا وَحدَةٍ لا يَعُدّ.</summary>
    public static MeterUnitCatalog From(IReadOnlyList<MeterUnit> units)
    {
        if (units.Count == 0)
            throw new InvalidOperationException("مُعجَمُ الوَحَداتِ فارِغ — عَدٌّ بِلا وَحدَةٍ واحِدَة.");
        var violations = units.SelectMany(u => MeterUnitValidator.Validate(u).Select(x => $"{u.Code}: {x.Code}: {x.MessageAr}"))
            .Concat(MeterUnitValidator.ValidateCatalog(units).Select(x => $"{x.Code}: {x.MessageAr}"))
            .ToList();
        if (violations.Count > 0)
            throw new InvalidOperationException("مُعجَمُ الوَحَداتِ لا يَجتازُ المُصادَقَة: " + string.Join(" | ", violations));
        return new MeterUnitCatalog(units);
    }
}
