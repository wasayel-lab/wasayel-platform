using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Kit.Theme;

/// <summary>
/// <para><b>قارِئ تَعريفات الثيم</b> — يُحَمِّل الثيم الافتِراضيّ مِن
/// مِلَفّ JSON <b>مَضمون مَورِداً</b> في هذه العُدَّة
/// (<c>Definitions/default.theme.json</c>)، ويَقرَأ نُصوص ثيمات
/// المُستَأجِرين بِـ<b>نَفس خِيارات القِراءَة السّاكِنَة</b>.</para>
///
/// <para><b>لِماذا مَضمون لا عَلى القُرص</b>: نَفس مُبَرِّر
/// <c>RoleDefinitionLoader</c> حَرفاً — القارِئ يَعمَل تَحت مُضيفَين
/// مُختَلِفَي مَسار (تَطبيق ASP.NET بِـContentRoot، ومُشَغِّل اختِبارات
/// بِمُجَلَّد عَمَل آخَر)، والمَورِد المَضمون لا مَسار لَه. والمِلَفّ
/// يَبقى <b>ظاهِراً في المُستودَع</b> يُقرَأ ويُحَرَّر ويَظهَر فَرقُه
/// في الـdiff.</para>
///
/// <para><b>ونَفس الدالَّة لِلمَسارَين</b>: <see cref="ParseDefinition"/>
/// هي ما يَقرَأ بِه المَورِد المَضمون <b>وما تَقرَأ بِه وَثيقَة
/// Marten</b> — بِنَفس الحَقل السّاكِن لا بِنُسخَة مُشابِهَة. فَما
/// يَصِحّ في مِلَفّ يَصِحّ في وَثيقَة <b>بِالبِناء لا
/// بِالمُصادَفَة</b>.</para>
/// </summary>
public static class ThemeDefinitionLoader
{
    private const string DefaultResourceSuffix = ".Definitions.default.theme.json";

    /// <summary>سِمَة اسم مَورِد الحُزمَة الجاهِزَة. المُجَلَّد جُزء مِن
    /// السِمَة عَمداً: مِلَفّ يُوضَع في <c>Definitions/</c> مُباشَرَةً لا
    /// يَصير حُزمَةً بِالمُصادَفَة.</summary>
    private const string PresetResourceInfix  = ".Definitions.Presets.";
    private const string PresetResourceSuffix = ".preset.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        // مِفتاح مَجهول عَلى مُستَوى الوَثيقَة (لا داخِل tokens) = خَطأ
        // صَريح لا تَجاهُل صامِت. مَفاتيح tokens تُفحَص بِالمَعجَم
        // فَتُعطي رَمز خَرق بَدَل استِثناء.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>يُحَمِّل الثيم الافتِراضيّ ويُصادِقُه <b>مُكتَمِلاً</b>.
    /// يَرمي عِند أَيّ خَرق — فَثيم افتِراضيّ فاسِد يُفشِل الإقلاع
    /// بِرَمزِه، ولا يَصِل صَفحَةً صامِتاً.</summary>
    public static ThemeDefinition LoadEmbeddedDefault()
    {
        var asm = typeof(ThemeDefinitionLoader).Assembly;
        var d = Read(asm, DefaultResourceSuffix);

        var violations = ThemeDefinitionValidator.ValidateDefault(d);
        if (violations.Count > 0)
            throw new InvalidOperationException(
                "الثيم الافتِراضيّ لا يَجتاز المُصادَقَة: " +
                string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

        return d;
    }

    /// <summary>قِراءَة تَعريف ثيم مِن نَصّ JSON. <b>لا تُصادِق</b>
    /// بِقَصد — لِيَبقى الفَصل بَين «تَعَذَّرَت القِراءَة» (استِثناء)
    /// و«قُرِئَ وخالَفَ» (قائِمَة خُروقات بِرُموزِها).</summary>
    public static ThemeDefinition ParseDefinition(string json) =>
        JsonSerializer.Deserialize<ThemeDefinition>(json, Options)
        ?? throw new InvalidOperationException("نَصّ تَعريف الثيم أَعطى null.");

    /// <summary>
    /// <para><b>يُحَمِّل كُلّ الحُزَم الجاهِزَة المَضمونَة</b> ويُصادِقُها
    /// بِبَوّابَة المُستَأجِر — لِأَنّ هذا بِالضَبط ما تَصيرُه الحُزمَة
    /// حينَ تُطَبَّق: وَثيقَة ثيم لِمُستَأجِر. أَيّ خَرق يَرمي، فَحُزمَة
    /// فاسِدَة تُفشِل الإقلاع بِرَمزِها ولا تَنتَظِر أَوَّل نَقرَة عَلى
    /// «تَطبيق» في عَرضٍ أَمامَ مُستَثمِر.</para>
    ///
    /// <para><b>ويُحفَظ النَّصّ كَما هُوَ</b> بِجانِب الكائِن المُفَكَّك:
    /// التَطبيق <b>نَسخ</b> لا إحالَة، فَما يُخَزَّن في وَثيقَة المُستَأجِر
    /// هو هذه البايتات — لا إعادَة تَسَلسُل لِلكائِن. ولَو أُعيدَ
    /// تَسَلسُلُه لَصارَ لِلحُزمَة الواحِدَة شَكلان: مِلَفٌّ في المُستودَع
    /// ونَصٌّ في قاعِدَة البَيانات، يَنحَرِفان بِأَوَّل تَغيير في
    /// خِيارات المُسَلسِل.</para>
    ///
    /// <para>والتَرتيب <b>بِالسلاج</b> لا بِتَرتيب المَوارِد: تَرتيب
    /// <c>GetManifestResourceNames</c> غَير مَضمون، وسَطح الإدارَة
    /// يَعرِض هذه القائِمَة — فَبِطاقاتُها لا تُبَدِّل أَماكِنَها بَين
    /// إقلاعَين.</para>
    /// </summary>
    public static IReadOnlyList<ThemePreset> LoadEmbeddedPresets()
    {
        var asm = typeof(ThemeDefinitionLoader).Assembly;

        var names = asm.GetManifestResourceNames()
            .Where(n => n.Contains(PresetResourceInfix, StringComparison.Ordinal) &&
                        n.EndsWith(PresetResourceSuffix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
            throw new InvalidOperationException(
                $"لا حُزمَة واحِدَة مَضمونَة في {asm.GetName().Name} — " +
                "المَوارِد مَفقودَة أَو المُجَلَّد غَير مُضَمَّن.");

        var presets = new List<ThemePreset>(names.Length);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"تَعَذَّرَ فَتح المَورِد «{name}».");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            ThemeDefinition d;
            try { d = ParseDefinition(json); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"تَعَذَّرَت قِراءَة الحُزمَة «{name}»: {ex.Message}", ex);
            }

            var violations = ThemeDefinitionValidator.ValidateTenantDefinition(d);
            if (violations.Count > 0)
                throw new InvalidOperationException(
                    $"الحُزمَة «{d.Slug}» لا تَجتاز المُصادَقَة: " +
                    string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

            presets.Add(new ThemePreset(d.Slug, d.Label, json, d));
        }

        var duplicate = presets.GroupBy(p => p.Slug, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"الحُزمَة «{duplicate.Key}» مُعَرَّفَة أَكثَر مِن مَرَّة.");

        return presets.OrderBy(p => p.Slug, StringComparer.Ordinal).ToArray();
    }

    private static ThemeDefinition Read(Assembly asm, string resourceSuffix)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"مَورِد الثيم «{resourceSuffix}» غَير مَضمون في {asm.GetName().Name}.");

        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"تَعَذَّرَ فَتح المَورِد «{name}».");

        return JsonSerializer.Deserialize<ThemeDefinition>(stream, Options)
            ?? throw new InvalidOperationException($"المَورِد «{name}» أَعطى null.");
    }
}
