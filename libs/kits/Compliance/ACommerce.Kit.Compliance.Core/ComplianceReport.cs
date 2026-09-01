namespace ACommerce.Kit.Compliance;

/// <summary>حُكمُ شاهِدٍ واحِد.</summary>
public enum EvidenceVerdict
{
    /// <summary>الشاهِدُ قائِم.</summary>
    Met,

    /// <summary>الشاهِدُ غائِبٌ أَو نائِبٌ أَو مَنقوضٌ بِنَصِّه.</summary>
    Missing,
}

/// <summary>
/// <para>نَتيجَةُ شاهِدٍ واحِد. <c>RejectionCode</c> يُملَأُ عِندَ
/// <see cref="EvidenceVerdict.Missing"/> وَحدَه — <b>ورَمزٌ يُملَأُ
/// مَعَ النَجاحِ رَمزٌ لا يَدُلّ</b>.</para>
/// </summary>
public sealed record EvidenceResult(
    EvidenceRequirement Requirement,
    EvidenceVerdict Verdict,
    string? RejectionCode,
    string DetailAr)
{
    public bool IsMissing => Verdict == EvidenceVerdict.Missing;
}

/// <summary>نَتيجَةُ التِزامٍ واحِدٍ بِكُلِّ شُهودِه.</summary>
public sealed record ObligationResult(
    ObligationDefinition Obligation,
    IReadOnlyList<EvidenceResult> Evidence)
{
    public IReadOnlyList<EvidenceResult> Missing =>
        Evidence.Where(e => e.IsMissing).ToList();

    /// <summary><b>الالتِزامُ مُستَوفىً إذا استُوفِيَ كُلُّ شاهِدٍ
    /// فيه</b> — لا أَغلَبُهم. ونِصفُ شاهِدٍ لَيسَ نِصفَ امتِثال.</summary>
    public bool IsSatisfied => Evidence.All(e => !e.IsMissing);

    public int MissingCount => Evidence.Count(e => e.IsMissing);
}

/// <summary>
/// <para><b>تَقريرُ فَحصٍ واحِد — ويَحمِلُ عَدّادَه.</b> القاعِدَة ١٠
/// صَريحَة: «كُلُّ أَداةِ تَحَقُّقٍ تَطبَعُ عَدَدَ ما فَحَصَته،
/// وتَفشَلُ إن كانَ صِفراً»، و«صِفرُ مُخالَفَة» بِلا عَدّادٍ لا
/// يُميَّزُ عَن أَداةٍ عَمياء. فَالعَدّادانِ هُنا <b>جُزءٌ مِن
/// العَقد</b> لا زينَةٌ في الشاشَة، و<see cref="IsBlind"/> هُوَ
/// الفَرقُ المُعلَنُ بَينَ «فَحَصتُ فَلَم أَجِد» و«لَم أَفحَص».</para>
/// </summary>
public sealed record ComplianceReport(
    ComplianceSubject Subject,
    IReadOnlyList<ObligationResult> Results)
{
    /// <summary>كَم التِزاماً فُحِص.</summary>
    public int ObligationsInspected => Results.Count;

    /// <summary>كَم شاهِداً قُيِّم.</summary>
    public int EvidenceChecked => Results.Sum(r => r.Evidence.Count);

    /// <summary>كَم شاهِداً سَقَط.</summary>
    public int EvidenceMissing => Results.Sum(r => r.MissingCount);

    /// <summary>الالتِزاماتُ المُستَوفاةُ بِكامِلِ شُهودِها.</summary>
    public int ObligationsSatisfied => Results.Count(r => r.IsSatisfied);

    /// <summary>البُنودُ المُعلَنُ أَنَّها لا تُفحَصُ لَفظِيّاً —
    /// تُعرَضُ ولا تُبتَلَع.</summary>
    public int UncheckableClauses => Results.Sum(r => r.Obligation.NotCheckable.Count);

    /// <summary><b>لَم يُفحَص شَيء.</b> ولَيسَ هذا «امتِثالاً
    /// كامِلاً» — هُوَ أَداةٌ لا تَرى، والفَرقُ بَينَهُما هُوَ كُلُّ
    /// قيمَةِ التَقرير.</summary>
    public bool IsBlind => EvidenceChecked == 0;

    /// <summary>الالتِزاماتُ الَّتي فيها نَقصٌ واحِدٌ فَأَكثَر —
    /// بِتَرتيبِ الكاتالوج.</summary>
    public IReadOnlyList<ObligationResult> Failing =>
        Results.Where(r => !r.IsSatisfied).ToList();

    /// <summary>كُلُّ رُموزِ الرَفضِ الَّتي وَقَعَت — لِلوغ
    /// ولِلاختِبارات.</summary>
    public IReadOnlyList<string> RejectionCodes =>
        Results.SelectMany(r => r.Evidence)
               .Where(e => e.IsMissing)
               .Select(e => e.RejectionCode!)
               .ToList();

    /// <summary>نَتيجَةُ التِزامٍ بِعَينِه، أَو <c>null</c> إن لَم
    /// يُفحَص (مُستَوىً آخَر).</summary>
    public ObligationResult? For(string obligationId) =>
        Results.FirstOrDefault(r => r.Obligation.Id == obligationId);
}
