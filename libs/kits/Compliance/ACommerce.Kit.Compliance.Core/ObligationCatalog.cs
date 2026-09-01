namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>كاتالوجُ الالتِزامات</b> — يُقرَأُ مَرَّةً عِندَ أَوَّلِ
/// لَمسَة، ويَرمي عِندَ أَيِّ خَرقٍ في أَيِّ مِلَفّ. نَفسُ شَكلِ
/// <c>RoleCatalog</c> و<c>ProviderCatalog</c> حَرفاً.</para>
///
/// <para><b>ولا تَخصيصَ لِمُستَأجِرٍ بَعد، وذلكَ مَقولٌ لا
/// مَبتولَع</b>: كُلُّ مَتجَرٍ يُفحَصُ اليَومَ بِنَفسِ التِزاماتِ
/// مُستَوى المُستَأجِر — لِأَنّ نَصَّهُ القانونيَّ نَفسَه مُوَحَّدٌ
/// في قامُوسِ المَنَصَّة، ويَعتَرِفُ بِذلكَ <c>Terms.razor</c>
/// صَراحَةً. ويَومَ يُؤَلِّفُ مُستَأجِرٌ وَثيقَتَه تُضافُ
/// مِلَفّاتُه فَوقَ هذا الكاتالوجِ بِنَفسِ نَمَطِ
/// <c>RoleDefinitionValidator.ValidateTenantDefinition</c> — ولا
/// يُظَلِّلُ التِزامَ مَنَصَّةٍ قائِماً.</para>
/// </summary>
public static class ObligationCatalog
{
    /// <summary>كُلُّ الالتِزاماتِ بِتَرتيبِ الفِهرِس.</summary>
    public static readonly IReadOnlyList<ObligationDefinition> All =
        ObligationDefinitionLoader.LoadEmbedded();

    /// <summary>التِزامٌ بِمُعَرِّفِه، أَو <c>null</c>.</summary>
    public static ObligationDefinition? Find(string id) =>
        All.FirstOrDefault(o => o.Id == id);

    /// <summary>التِزاماتُ مُستَوىً بِعَينِه.</summary>
    public static IReadOnlyList<ObligationDefinition> ForLevel(string level) =>
        All.Where(o => o.Level == level).ToList();

    /// <summary>كُلُّ رُموزِ الرَفضِ المُعَرَّفَةِ في الكاتالوج —
    /// <b>المَعجَمُ المُغلَقُ الَّذي تَقرَؤُه الاختِبارات واللوغ</b>.
    /// وهُوَ مُشتَقٌّ مِن المِلَفّاتِ لا مَكتوبٌ في الكود: فَرَمزٌ
    /// يُضافُ في مِلَفٍّ يَظهَرُ هُنا بِلا لَمسِ سَطر.</summary>
    public static IReadOnlyList<string> RejectionCodes =>
        All.SelectMany(o => o.Evidence)
           .Select(e => e.RejectionCode)
           .Distinct(StringComparer.Ordinal)
           .OrderBy(c => c, StringComparer.Ordinal)
           .ToList();
}
