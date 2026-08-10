using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Kit.Roles;

/// <summary>
/// <para><b>قارِئ تَعريفات الأَدوار</b> — يُحَمِّل السَبعَة مِن مِلَفّات
/// JSON <b>مَضمونَة مَوارِدَ</b> في هذه العُدَّة
/// (<c>Definitions/*.role.json</c>)، بِتَرتيب
/// <c>Definitions/roles.index.json</c>.</para>
///
/// <para><b>لِماذا مَضمونَة لا ظاهِرَة عَلى القُرص</b> — والقَرار مُعلَن
/// لِأَنّ لَه بَديلاً مَعقولاً: المِلَفّات <b>ظاهِرَة في المُستودَع</b>
/// (تُقرَأ وتُحَرَّر ويَظهَر فَرقُها في الـ diff — وهذا كُلّ ما يُطلَب
/// مِن «انظُر، هذا مِلَفّ»)، و<b>مَضمونَة في التَّجميع</b> عِندَ النَّشر.
/// السَبَب: القارِئ نَفسُه يَعمَل تَحت مُضيفَين مُختَلِفَي مَسار — تَطبيق
/// ASP.NET بِـ ContentRoot، ومُشَغِّل اختِبارات بِمُجَلَّد عَمَل آخَر —
/// فَقِراءَة «مِلَفّ عَلى القُرص» تَحتاج مَسار أَساس يَختَلِف بَينَهُما،
/// وهو هَشاشَة حَقيقيَّة تَظهَر عِندَ النَّشر لا عِندَ التَّطوير. والمَورِد
/// المَضمون لا مَسار لَه.</para>
///
/// <para><b>ولِماذا لا سُقوط مِن قُرص إلى مَضمون</b>: مَصدَران لِلحَقيقَة
/// يَعنيان انحِرافاً صامِتاً بِالتَّعريف — نُسخَة عَلى القُرص تُعَدَّل
/// ونُسخَة مَضمونَة تُقرَأ (أَو العَكس)، ولا شَيء يَقول أَيُّهُما فازَ.
/// وهذا هو بِعَينِه ما جاءَت هذه المَوجَة لِتُزيلَه. مَصدَر واحِد،
/// ومَوضِعُه مُعلَن.</para>
///
/// <para><b>والتَّحميل بَوّابَة لا نَقل</b>: كُلّ تَعريف يَمُرّ مِن
/// <see cref="RoleDefinitionValidator"/>، وأَيّ خَرق يَرمي بِرَمزِه —
/// فَتَعريف فاسِد يُفشِل الإقلاع بِرِسالَة تُسَمّي الدَور والرَّمز، ولا
/// يَصِل مَتجَراً صامِتاً.</para>
/// </summary>
public static class RoleDefinitionLoader
{
    private const string IndexResourceSuffix = ".Definitions.roles.index.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        // مِفتاح مَجهول في مِلَفّ تَعريف = خَطأ صَريح لا تَجاهُل صامِت.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private sealed record RolesIndex
    {
        public IReadOnlyList<string> Roles { get; init; } = [];
    }

    /// <summary>يُحَمِّل كُلّ التَّعريفات بِتَرتيب المِلَفّ الفِهرِس،
    /// ويُصادِق كُلّ واحِد. يَرمي عِند أَيّ نَقص أَو خَرق.</summary>
    public static IReadOnlyList<RoleDefinition> LoadEmbedded()
    {
        var asm = typeof(RoleDefinitionLoader).Assembly;
        var index = Read<RolesIndex>(asm, IndexResourceSuffix);

        if (index.Roles.Count == 0)
            throw new InvalidOperationException(
                "roles.index.json فارِغ — كاتالوج الأَدوار بِلا دَور واحِد.");

        var list = new List<RoleDefinition>(index.Roles.Count);
        foreach (var slug in index.Roles)
        {
            var d = Read<RoleDefinition>(asm, $".Definitions.{slug}.role.json");

            if (!string.Equals(d.Slug, slug, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"تَعريف الدَور «{slug}» يُعلِن slug مُختَلِفاً: «{d.Slug}».");

            var violations = RoleDefinitionValidator.Validate(d);
            if (violations.Count > 0)
                throw new InvalidOperationException(
                    $"تَعريف الدَور «{slug}» لا يَجتاز المُصادَقَة: " +
                    string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

            list.Add(d);
        }
        return list;
    }

    /// <summary>
    /// <para><b>قِراءَة تَعريف دَور مِن نَصّ JSON</b> — بِـ
    /// <see cref="Options"/> نَفسِها الَّتي يَقرَأ بِها
    /// <see cref="LoadEmbedded"/> (نَفس الحَقل السّاكِن، لا نُسخَة
    /// مُشابِهَة)، فَما يَصِحّ هُنا يَصِحّ هُناك بِالبِناء لا
    /// بِالمُصادَفَة.</para>
    ///
    /// <para><b>لِماذا مَدخَل عامّ وليسَ تَفصيلاً خاصّاً</b>: مَصدَر
    /// النَّصّ اليَوم مَورِد مَضمون، وغَداً وَثيقَة Marten لِكُلّ
    /// مُستَأجِر (<c>define_role</c>) — والقِراءَة واحِدَة في الحالَتَين.
    /// وهو أَيضاً ما يَسمَح بِاختِبار <b>مِلَفّ دَور مُصطَنَع</b> عَبر
    /// مَسارِه الكامِل (نَصّ ← تَعريف ← مُصادِق) لا عَبر كائِن يُبنى
    /// في الاختِبار ويَتَخَطّى التَّسَلسُل.</para>
    ///
    /// <para><b>ولا يُصادِق</b> بِقَصد: يُرجِع التَّعريف كَما قُرِئ،
    /// والمُصادَقَة تُطلَب صَريحَةً مِن
    /// <see cref="RoleDefinitionValidator"/> — لِيَبقى الفَصل بَين
    /// «تَعَذَّرَت القِراءَة» (مِفتاح مَجهول، JSON تالِف ← استِثناء)
    /// و«قُرِئَ وخالَفَ» (قائِمَة خُروقات بِرُموزِها).</para>
    /// </summary>
    public static RoleDefinition ParseDefinition(string json) =>
        JsonSerializer.Deserialize<RoleDefinition>(json, Options)
        ?? throw new InvalidOperationException("نَصّ تَعريف الدَور أَعطى null.");

    private static T Read<T>(Assembly asm, string resourceSuffix)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"مَورِد التَّعريف «{resourceSuffix}» غَير مَضمون في {asm.GetName().Name}.");

        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"تَعَذَّرَ فَتح المَورِد «{name}».");

        return JsonSerializer.Deserialize<T>(stream, Options)
            ?? throw new InvalidOperationException($"المَورِد «{name}» أَعطى null.");
    }
}
