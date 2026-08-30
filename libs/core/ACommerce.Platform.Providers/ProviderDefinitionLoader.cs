using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>قارِئُ تَعريفاتِ المُزَوِّدين</b> — مِرآةُ
/// <c>RoleDefinitionLoader</c> حَرفاً: مَوارِدُ مَضمونَة، وفِهرِس
/// <c>providers.index.json</c>، و<c>PropertyNamingPolicy.CamelCase</c>،
/// و<c>UnmappedMemberHandling.Disallow</c>،
/// و<c>AllowTrailingCommas = false</c>، ومُصادَقَةٌ <b>تُفشِلُ الإقلاعَ
/// بِرَمزِها</b>.</para>
///
/// <para><b>لِماذا مَضمونَةٌ لا ظاهِرَةٌ عَلى القُرص</b> — بِنَفسِ
/// حُجَّةِ الأَدوارِ حَرفاً: القارِئُ يَعمَل تَحتَ مُضيفَينِ مُختَلِفَي
/// مَسار (تَطبيقُ ASP.NET بِـ ContentRoot، ومُشَغِّلُ اختِباراتٍ
/// بِمُجَلَّدِ عَمَلٍ آخَر)، فَقِراءَةُ «مِلَفٍّ عَلى القُرص» تَحتاج
/// مَسارَ أَساسٍ يَختَلِف بَينَهُما — والمَورِدُ المَضمونُ لا مَسارَ
/// لَه. والمِلَفّاتُ تَبقى ظاهِرَةً في المُستَودَعِ يَظهَر فَرقُها في
/// الـ diff.</para>
///
/// <para><b>ولِماذا كُلُّها في مَكتَبَةٍ واحِدَةٍ لا في كُلِّ عُدَّة</b>
/// — وهذا انحِرافٌ عَن الصورَةِ الأولى لِلتَصميمِ يُقالُ ولا يُبتَلَع
/// (‏ADR-012): تَوزيعُ المِلَفّاتِ عَلى العُدَدِ يُوجِب مَسحَ عِدَّةِ
/// تَجميعات، وذلكَ <b>أُنبوبٌ رابِع</b> يُخالِف القاعِدَةَ ٨ ولا يَشتَري
/// شَيئاً: التَعريفُ بَياناتٌ لا كود، ولا يُحيلُ نَوعاً واحِداً مِن
/// أَيّ عُدَّة.</para>
/// </summary>
public static class ProviderDefinitionLoader
{
    private const string IndexResourceSuffix = ".Definitions.providers.index.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        // مِفتاحٌ مَجهولٌ في مِلَفِّ تَعريفٍ = خَطَأٌ صَريحٌ لا تَجاهُلٌ صامِت.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private sealed record ProvidersIndex
    {
        public IReadOnlyList<string> Providers { get; init; } = [];
    }

    public static IReadOnlyList<ProviderDefinition> LoadEmbedded()
    {
        var asm = typeof(ProviderDefinitionLoader).Assembly;
        var index = Read<ProvidersIndex>(asm, IndexResourceSuffix);

        if (index.Providers.Count == 0)
            throw new InvalidOperationException(
                "providers.index.json فارِغ — كاتالوج المُزَوِّدين بِلا مُزَوِّدٍ واحِد.");

        var list = new List<ProviderDefinition>(index.Providers.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var slug in index.Providers)
        {
            var d = Read<ProviderDefinition>(asm, $".Definitions.{slug}.provider.json");

            if (!string.Equals(d.Slug, slug, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"تَعريف المُزَوِّد «{slug}» يُعلِن slug مُختَلِفاً: «{d.Slug}».");

            if (!seen.Add(slug))
                throw new InvalidOperationException(
                    $"المُزَوِّد «{slug}» مُكَرَّرٌ في الفِهرِس.");

            var violations = ProviderDefinitionValidator.Validate(d);
            if (violations.Count > 0)
                throw new InvalidOperationException(
                    $"تَعريف المُزَوِّد «{slug}» لا يَجتاز المُصادَقَة: " +
                    string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

            list.Add(d);
        }

        return list;
    }

    public static ProviderDefinition ParseDefinition(string json) =>
        JsonSerializer.Deserialize<ProviderDefinition>(json, Options)
        ?? throw new InvalidOperationException("نَصّ تَعريف المُزَوِّد أَعطى null.");

    private static T Read<T>(Assembly asm, string resourceSuffix)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"مَورِد التَعريف «{resourceSuffix}» غَير مَضمون في {asm.GetName().Name}.");

        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"تَعَذَّرَ فَتح المَورِد «{name}».");

        return JsonSerializer.Deserialize<T>(stream, Options)
            ?? throw new InvalidOperationException($"المَورِد «{name}» أَعطى null.");
    }
}

/// <summary>
/// <para><b>الكاتالوجُ الساكِن</b> — يُحَمَّل مَرَّةً عِندَ أَوَّلِ
/// لَمسَة، فَتَعريفٌ فاسِدٌ يُفشِلُ الإقلاعَ لا يَصِلُ مُستَأجِراً.</para>
/// </summary>
public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderDefinition> Definitions =
        ProviderDefinitionLoader.LoadEmbedded();

    public static ProviderDefinition? Find(string? slug) =>
        string.IsNullOrEmpty(slug)
            ? null
            : Definitions.FirstOrDefault(d => d.Slug == slug);

    public static IReadOnlyList<ProviderDefinition> ForCapability(string capability) =>
        Definitions.Where(d => d.Capability == capability).ToArray();

    /// <summary><b>ما يُعرَض لِلمُستَأجِرِ في شاشَتِه</b> — وقُدرَةٌ بِلا
    /// مُزَوِّدٍ يَربِطُه مُستَأجِرٌ <b>لا تُرسَم إطلاقاً</b>، ولا
    /// «قَريباً» (القاعِدَة ١٢).</summary>
    public static IReadOnlyList<ProviderDefinition> TenantBindable(string capability) =>
        Definitions.Where(d => d.Capability == capability && d.IsTenantBindable).ToArray();

    /// <summary>القُدُراتُ الَّتي تُرسَم — بِتَرتيبِ المَعجَمِ لا
    /// بِتَرتيبِ المِلَفّات.</summary>
    public static IReadOnlyList<string> BindableCapabilities =>
        ProviderCapabilities.All.Where(c => TenantBindable(c).Count > 0).ToArray();
}
