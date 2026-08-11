using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Platform.Flows;

/// <summary>
/// <para><b>قارِئ تَعريفات التَدَفُّق</b> — يُحَمِّل مِلَفّات
/// <c>Definitions/*.flow.json</c> <b>مَضمونَةً مَوارِدَ</b> في هذه
/// المَكتَبَة، بِنَفس خِيارات القِراءَة الَّتي لِـ
/// <c>RoleDefinitionLoader</c> حَرفاً — ومِنها
/// <c>UnmappedMemberHandling.Disallow</c>: مِفتاح مَجهول في مِلَفّ
/// تَعريف <b>خَطَأ صَريح لا تَجاهُل صامِت</b>.</para>
///
/// <para><b>ولِماذا مَضمونَة لا ظاهِرَة عَلى القُرص</b>: نَفس مُبَرِّر
/// تَعريفات الأَدوار حَرفاً — المِلَفّات ظاهِرَة في المُستودَع
/// (تُقرَأ وتُحَرَّر ويَظهَر فَرقُها في الـ diff) ومَضمونَة في
/// التَجميع، فَلا مَسار أَساس يَختَلِف بَين مُضيف الاختِبارات
/// ومُضيف التَطبيق.</para>
///
/// <para><b>وخِلافاً لِقارِئ الأَدوار في نُقطَة واحِدَة مَقصودَة</b>:
/// هذا القارِئ <b>لا يُصادِق ولا يَرمي عِندَ الخَرق</b>. السَبَب أَنّ
/// المِلَفّات في هذه المَوجَة تَصِف <b>الواقِع كَما هو</b>، والواقِع
/// فيه خَمس حالات مَيِّتَة — فَلَو رَمى القارِئ لَامتَنَعَ وَصفُ
/// الواقِع أَصلاً، ولَما بَقِيَ لِلمُصادِق ما يَكشِفُه. المُصادَقَة
/// تُستَدعى صَراحَةً مِن المُستَهلِك، ونَتيجَتُها <b>مُثَبَّتَة</b> في
/// <c>FlowInventoryTests</c>.</para>
/// </summary>
public static class FlowDefinitionLoader
{
    private const string ResourcePrefix = "ACommerce.Platform.Flows.Definitions.";
    private const string ResourceSuffix = ".flow.json";

    /// <summary>نَفس خِيارات <c>RoleDefinitionLoader</c> حَرفاً — فَما
    /// يَصِحّ في مِلَفّ دَور يَصِحّ في مِلَفّ تَدَفُّق بِالبِناء لا
    /// بِالمُصادَفَة.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>أَسماء كُلّ التَدَفُّقات المَوصوفَة، مُرَتَّبَة.</summary>
    public static IReadOnlyList<string> Names()
        => typeof(FlowDefinitionLoader).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                     && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^ResourceSuffix.Length])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>يُحَمِّل تَدَفُّقاً بِاسمِه. يَرمي إن لَم يوجَد
    /// المَورِد أَو تَعَذَّرَت قِراءَتُه — وهذا خَطَأ بِناء لا
    /// بَيانات.</summary>
    public static FlowDefinition Load(string name)
    {
        var asm = typeof(FlowDefinitionLoader).Assembly;
        var resource = ResourcePrefix + name + ResourceSuffix;

        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"لا مَورِد بِاسم «{resource}». المَوجود: " +
                string.Join(", ", Names()));

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<FlowDefinition>(json, Options)
            ?? throw new InvalidOperationException($"تَعريف التَدَفُّق «{name}» فارِغ.");
    }

    /// <summary>كُلّ التَدَفُّقات المَوصوفَة، بِتَرتيب أَسمائِها.</summary>
    public static IReadOnlyList<FlowDefinition> LoadAll()
        => Names().Select(Load).ToArray();

    /// <summary>يَقرَأ تَعريفاً مِن نَصّ — لِلاختِبارات ولِوَثيقَة
    /// مُستَأجِر مُستَقبَلاً. <b>نَفس الخِيارات</b>، فَلا مَسار
    /// قِراءَة ثانٍ يَنحَرِف.</summary>
    public static FlowDefinition Parse(string json)
        => JsonSerializer.Deserialize<FlowDefinition>(json, Options)
            ?? throw new InvalidOperationException("تَعريف التَدَفُّق فارِغ.");
}
