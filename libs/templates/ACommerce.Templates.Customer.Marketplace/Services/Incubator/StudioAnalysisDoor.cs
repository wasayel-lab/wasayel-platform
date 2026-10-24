namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>بابُ التَحليلِ في الاستوديو — دالَّةٌ نَقِيَّةٌ تَسأَلُها الصَفحَةُ والنُقطَةُ مَعاً</b> (‏2026-10-24، تَيّارُ S15 البَند A؛
/// عَلى سابِقَةِ <c>CheckoutPaymentPolicy.CardOffered</c> و<see cref="StudioAuthDoor"/>).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (‏RELEASE-READINESS §٣.١، وتَوصيفُها <c>StudioAnalysisDoorTests</c>): بِلا مِفتاحٍ لِوَكيلِ
/// <c>Analysis</c> كانَت صَفحَتا الفِكرَةِ تَرسُمانِ الاستِمارَة، و<c>/studio/begin</c> يَأخُذُ رائِدَ الأَعمالِ إلى الدُخولِ ثُمَّ الشُروط،
/// ثُمَّ يُنفِقُ تَحليلاً مِن حِصَّتِه ويُنادي المُزَوِّدَ بِلا مِفتاحٍ فَيُرَدُّ ‏401 — «فَشِلَ التَّحليل». و<see cref="FeasibilityAnalysisService.IsConfigured"/>
/// قائِمٌ مُنذُ قَبل ولا يَسأَلُه إلّا صَفحَتا الإدارَة. فَالبابُ هُنا <b>يَقرَأُه ولا يُعيدُ حِسابَه</b> (القاعِدَة ٨).</para>
///
/// <para><b>ومَن يَسأَلُه</b>: صَفحَتا الفِكرَة (<c>Landing</c>، <c>StudioNewIdea</c>) وزِرُّ الإعادَةِ في <c>StudioStudy</c> — فَلا تُرسَمُ
/// استِمارَةٌ تُفضي إلى بابٍ لا يُفتَح (القاعِدَة ١٢) — ونُقطَتا <c>/studio/begin</c> و<c>/studio/s/{id}/analyze</c> والاستِئنافُ
/// <c>ResumeStudioPromptAsync</c>، قَبلَ الدُخولِ والشُروطِ وقَبلَ إنفاقِ الحِصَّةِ وأَيِّ نِداءٍ لِلمُزَوِّد؛ وسَطرُ الإقلاعِ في
/// <c>Program.cs</c> الَّذي تَقرَؤُه بَوّابَةُ الإقلاعِ الإنتاجِيّ. <b>ومِن دالَّةٍ واحِدَة</b>، وإلّا رُسِمَ زِرٌّ يَقودُ إلى رَفض.</para>
///
/// <para><b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): مُدخَلُه تَهيئَةُ العَمَلِيَّة. وبَوّابَةُ الحِصَّةِ
/// (<c>StudioTierService.CheckAnalyzeAsync</c>) تَقرَأُ القاعِدَةَ فَتَبقى خارِجَه، بَعدَه.</para>
/// </summary>
public static class StudioAnalysisDoor
{
    /// <summary>رَمزُ الغِيابِ في سَطرِ الإقلاعِ وفي بَوّابَةِ الإقلاع — نَفسُ لاحِقَةِ <c>*_unavailable</c> الَّتي
    /// تَرُدُّها الخِزانَةُ (<c>vault_unavailable</c>) وبابُ الدُخول (<see cref="StudioAuthDoor.UnavailableError"/>).</summary>
    public const string UnavailableCode = "analysis_unavailable";

    /// <summary>مِفتاحُ القامُوسِ الَّذي يُرسَمُ مَكانَ الاستِمارَة. <b>والنَصُّ مِن كِتابَةِ الوَكيلِ إلى أَن يَكتُبَه المالِك</b>
    /// («ب» S12·٤(٣) في سِجِلِّه).</summary>
    public const string ClosedMessageKey = "studio.analysis.unavailable";

    /// <summary>سَطرُ الإقلاعِ الَّذي تَقرَؤُه بَوّابَةُ الإقلاع — بِالرَمزِ لا بِنَصٍّ عَرَبيٍّ يَنجَرِف (‏<c>scripts/verify-production-boot.sh</c>
    /// الكُتلَة <c>analysis_door</c>).</summary>
    public const string BootLogPrefix = "[studio] analysis_door=";

    /// <summary>أَيُفتَحُ البابُ؟ — نَعَم حينَ تَكونُ خَلفِيَّةُ وَكيلِ <c>Analysis</c> مَضبوطَةً
    /// (<see cref="FeasibilityAnalysisService.IsConfigured"/>: مِفتاحٌ مَحلولٌ، أَو Ollama مَحَلِّيّ).</summary>
    public static bool Opens(bool analysisBackendConfigured) => analysisBackendConfigured;

    /// <summary>قيمَةُ سَطرِ الإقلاع: <c>open</c> أَو <see cref="UnavailableCode"/>.</summary>
    public static string BootLogValue(bool analysisBackendConfigured)
        => Opens(analysisBackendConfigured) ? "open" : UnavailableCode;
}
