using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>قارِئُ تَعريفاتِ الالتِزامات</b> — يُحَمِّلُها مِن مِلَفّاتِ
/// JSON <b>مَضمونَةً مَوارِدَ</b> في هذِه العُدَّة
/// (<c>Definitions/*.obligation.json</c>) بِتَرتيبِ
/// <c>Definitions/obligations.index.json</c>.</para>
///
/// <para><b>لِماذا مَضمونَةٌ لا ظاهِرَةٌ عَلى القُرص</b>: نَفسُ مُبَرِّرِ
/// <c>RoleDefinitionLoader</c> حَرفاً — المِلَفّاتُ ظاهِرَةٌ في
/// المُستودَعِ ويَظهَرُ فَرقُها في الـdiff، ومَضمونَةٌ عِندَ النَشرِ
/// فَلا مَسارَ أَساسٍ يَختَلِفُ بَينَ مُضيفٍ ومُشَغِّلِ اختِبارات.</para>
///
/// <para><b>والزِيادَةُ عَلى قارِئِ الأَدوار — بَوّابَةُ الانجِرافِ
/// بَينَ الفِهرِسِ والمُجَلَّد</b>: قارِئُ الأَدوارِ يَقرَأُ ما في
/// الفِهرِسِ ولا يَسأَلُ عَمّا في المُجَلَّدِ وليسَ فيه. وذلكَ يَكفي
/// لِأَدوارٍ سَبعَةٍ ثابِتَة، <b>ولا يَكفي لِالتِزاماتٍ يُقصَدُ بِها
/// أَن تَنمُوَ</b>: مِلَفُّ التِزامٍ يُضافُ ولا يُدرَجُ في الفِهرِس
/// <b>يَختَفي صامِتاً</b> — واللَوحَةُ تَقولُ «صِفرُ نَقص» وهي عَمياءُ
/// عَنه. وذلكَ بِعَينِه العَطَبُ الَّذي حَذَّرَت مِنه القاعِدَة ١٠:
/// أَداةٌ بِلا عَدّادٍ لا تُميَّزُ عَن أَداةٍ لا تَرى.</para>
///
/// <para>فَـ<see cref="LoadEmbedded"/> يُقابِلُ <b>المَجموعَتَين</b>
/// ويَرمي عِندَ أَيِّ فَرقٍ في الاتِّجاهَين. والنَتيجَة: إضافَةُ
/// التِزامٍ <b>مِلَفٌّ يُضافُ وسَطرٌ في الفِهرِس</b>، ونِسيانُ
/// السَطرِ <b>يُفشِلُ الإقلاعَ بِاسمِ المِلَفّ</b> بَدَلَ أَن
/// يَختَفي.</para>
/// </summary>
public static class ObligationDefinitionLoader
{
    private const string IndexResourceSuffix = ".Definitions.obligations.index.json";
    private const string DefinitionSuffix = ".obligation.json";
    private const string DefinitionsMarker = ".Definitions.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        // مِفتاحٌ مَجهولٌ في مِلَفِّ تَعريفٍ = خَطَأٌ صَريحٌ لا تَجاهُلٌ صامِت.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private sealed record ObligationsIndex
    {
        public IReadOnlyList<string> Obligations { get; init; } = [];
    }

    /// <summary>يُحَمِّلُ كُلَّ التَعريفاتِ بِتَرتيبِ الفِهرِس،
    /// ويُصادِقُ كُلَّ واحِد، <b>ويُقابِلُ الفِهرِسَ بِالمُجَلَّد</b>.
    /// يَرمي عِندَ أَيِّ نَقصٍ أَو خَرقٍ أَو انجِراف.</summary>
    public static IReadOnlyList<ObligationDefinition> LoadEmbedded()
    {
        var asm = typeof(ObligationDefinitionLoader).Assembly;
        var index = Read<ObligationsIndex>(asm, IndexResourceSuffix);

        if (index.Obligations.Count == 0)
            throw new InvalidOperationException(
                "obligations.index.json فارِغ — كاتالوجُ الامتِثالِ بِلا التِزامٍ واحِد.");

        // ─── الفِهرِسُ مُقابَلاً بِالمُجَلَّد، في الاتِّجاهَين ─────────
        var onDisk = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(DefinitionSuffix, StringComparison.Ordinal))
            .Select(NameOf)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var indexed = new HashSet<string>(index.Obligations, StringComparer.Ordinal);

        var unindexed = onDisk.Where(n => !indexed.Contains(n)).ToList();
        if (unindexed.Count > 0)
            throw new InvalidOperationException(
                $"مِلَفّاتُ التِزامٍ مَضمونَةٌ ولَيسَت في الفِهرِس: " +
                $"{string.Join("، ", unindexed)}. " +
                "ومِلَفٌّ خارِجَ الفِهرِسِ لا يُفحَصُ ولا يُعَدّ — وذلكَ عَمىً " +
                "يَحمِلُ شَهادَةَ حُضور. أَضِفهُ إلى obligations.index.json.");

        var onDiskSet = new HashSet<string>(onDisk, StringComparer.Ordinal);
        var ghosts = index.Obligations.Where(n => !onDiskSet.Contains(n)).ToList();
        if (ghosts.Count > 0)
            throw new InvalidOperationException(
                $"الفِهرِسُ يَذكُرُ التِزاماتٍ بِلا مِلَفّات: {string.Join("، ", ghosts)}.");

        // ─── التَحميلُ بَوّابَةٌ لا نَقل ────────────────────────────
        var list = new List<ObligationDefinition>(index.Obligations.Count);
        var seenId = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in index.Obligations)
        {
            var d = Read<ObligationDefinition>(asm, $"{DefinitionsMarker}{id}{DefinitionSuffix}");

            if (!string.Equals(d.Id, id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"تَعريفُ الالتِزام «{id}» يُعلِنُ مُعَرِّفاً مُختَلِفاً: «{d.Id}».");

            if (!seenId.Add(d.Id))
                throw new InvalidOperationException(
                    $"المُعَرِّف «{d.Id}» مُكَرَّرٌ في الفِهرِس.");

            var violations = ObligationDefinitionValidator.Validate(d);
            if (violations.Count > 0)
                throw new InvalidOperationException(
                    $"تَعريفُ الالتِزام «{id}» لا يَجتازُ المُصادَقَة: " +
                    string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

            list.Add(d);
        }

        return list;
    }

    /// <summary>
    /// <para><b>قِراءَةُ تَعريفٍ مِن نَصّ JSON</b> — بِـ<see cref="Options"/>
    /// نَفسِها الَّتي يَقرَأُ بِها <see cref="LoadEmbedded"/> (نَفسُ الحَقلِ
    /// السّاكِن، لا نُسخَةٌ مُشابِهَة)، فَما يَصِحُّ هُنا يَصِحُّ هُناكَ
    /// بِالبِناءِ لا بِالمُصادَفَة.</para>
    ///
    /// <para><b>وهُوَ مَدخَلُ المِجَسّ</b>: حَقنُ التِزامٍ ناقِصٍ
    /// ونَظيرِه المُكتَمِلِ يَمُرُّ مِن هُنا — أَي عَبرَ التَسَلسُلِ
    /// الكامِلِ (نَصّ ← تَعريف ← مُصادِق ← فاحِص) لا عَبرَ كائِنٍ
    /// يُبنى في الاختِبارِ ويَتَخَطّاه.</para>
    ///
    /// <para><b>ولا يُصادِقُ</b> بِقَصد: يُرجِعُ التَعريفَ كَما قُرِئ،
    /// والمُصادَقَةُ تُطلَبُ صَريحَةً — لِيَبقى الفَصلُ بَينَ
    /// «تَعَذَّرَت القِراءَة» و«قُرِئَ وخالَف».</para>
    /// </summary>
    public static ObligationDefinition ParseDefinition(string json) =>
        JsonSerializer.Deserialize<ObligationDefinition>(json, Options)
        ?? throw new InvalidOperationException("نَصُّ تَعريفِ الالتِزامِ أَعطى null.");

    /// <summary>اسمُ الالتِزامِ مِن اسمِ المَورِدِ الكامِل.</summary>
    private static string NameOf(string resourceName)
    {
        var marker = resourceName.LastIndexOf(DefinitionsMarker, StringComparison.Ordinal);
        var start = marker < 0 ? 0 : marker + DefinitionsMarker.Length;
        return resourceName[start..^DefinitionSuffix.Length];
    }

    private static T Read<T>(Assembly asm, string resourceSuffix)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"مَورِدُ التَعريف «{resourceSuffix}» غَير مَضمونٍ في {asm.GetName().Name}.");

        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"تَعَذَّرَ فَتحُ المَورِد «{name}».");

        return JsonSerializer.Deserialize<T>(stream, Options)
            ?? throw new InvalidOperationException($"المَورِد «{name}» أَعطى null.");
    }
}
