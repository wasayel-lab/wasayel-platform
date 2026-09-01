namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>مَصدَرُ النَصِّ النِظاميّ — يُنقَلُ ولا يُختَصَر.</b>
/// القاعِدَة ١٦ تَمنَعُ اختِراعَ نَصٍّ نِظامِيٍّ أَو مُدَّةٍ أَو رَقَم؛
/// وهذا القِسمُ هُوَ ما يَجعَلُ المَنعَ <b>مَفروضاً</b> لا مَنصوحاً
/// بِه: كُلُّ التِزامٍ يَحمِلُ اقتِباسَه وجِهَتَه ومَرجِعَه، فَمَن
/// يُضيفُ مِلَفّاً بِلا مَصدَرٍ يُرَدُّ عِندَ الإقلاعِ
/// بِـ<c>source_incomplete</c>.</para>
/// </summary>
public sealed record ObligationSource
{
    /// <summary>الجِهَةُ الَّتي أَصدَرَت النَصّ (هَيئَةُ الخُبَراءِ
    /// بِمَجلِسِ الوُزَراء، ‏Apple App Store Review Guidelines، …).</summary>
    public string Authority { get; init; } = "";

    /// <summary>المَوضِعُ داخِلَ المَصدَر (‏«المادَّةُ السادِسَة»،
    /// ‏«‏1.2 User-Generated Content»).</summary>
    public string Reference { get; init; } = "";

    /// <summary>رابِطُ المَصدَرِ المَقروء. <c>null</c> مَسموحٌ لِمَصدَرٍ
    /// بِلا عُنوانٍ ثابِت.</summary>
    public string? Url { get; init; }

    /// <summary><b>النَصُّ مَنقولاً</b> — وهُوَ ما تَعرِضُه اللَوحَةُ
    /// بِجِوارِ كُلِّ نَقص، لِيَقرَأَ صاحِبُ المَتجَرِ <b>لِماذا</b>
    /// يُطلَبُ مِنه هذا لا أَن يُصَدِّقَ أَنَّه مَطلوب.</summary>
    public string QuotedAr { get; init; } = "";

    /// <summary>العُقوبَةُ المَنشورَةُ إن وُجِدَت — نَصّاً كَما
    /// وَرَدَت. <c>null</c> حَيثُ لا عُقوبَةَ مَنصوصَة (بُنودُ
    /// المَتجَرِ عُقوبَتُها الرَفضُ لا الغَرامَة).</summary>
    public string? PenaltyAr { get; init; }
}

/// <summary>
/// <para><b>شاهِدٌ واحِدٌ مَطلوب — ورَمزُ رَفضِه عِندَ غِيابِه.</b>
/// هذا هُوَ العُنصُرُ الَّذي يَجعَلُ الالتِزامَ بَياناً: نَوعٌ مِن
/// مَعجَمٍ مُغلَق (<see cref="EvidenceKinds"/>)، وهَدَفٌ، ورَمزُ
/// رَفضٍ ثابِت. والفاحِصُ يُقَيِّمُ الأَربَعَةَ بِمَنطِقٍ واحِد.</para>
/// </summary>
public sealed record EvidenceRequirement
{
    /// <summary>رَمزُ الشاهِدِ داخِلَ الالتِزام — فَريدٌ فيه.</summary>
    public string Code { get; init; } = "";

    /// <summary>مِن <see cref="EvidenceKinds"/> حَصراً.</summary>
    public string Kind { get; init; } = "";

    /// <summary>مِفتاحُ القامُوسِ لِلأَنواعِ النَصِّيَّة، ومَسارٌ
    /// يَبدَأُ بِـ<c>/</c> لِـ<c>route_reachable</c>.</summary>
    public string Target { get; init; } = "";

    /// <summary><b>رَمزُ الرَفضِ عِندَ الغِياب</b> — ثابِتٌ لِلاختِبارات
    /// ولِلوغ ولِلَوحَة. لا يُشتَقُّ مِن الرِسالَة: الرِسالَةُ تُحَرَّرُ
    /// والرَمزُ لا.</summary>
    public string RejectionCode { get; init; } = "";

    /// <summary>تَسمِيَةُ الشاهِدِ لِلقارِئِ البَشَريّ.</summary>
    public IReadOnlyDictionary<string, string?> Label { get; init; } = ComplianceText.Empty;

    /// <summary>العِباراتُ المَمنوعَة — <b>إلزامِيَّةٌ
    /// لِـ<c>text_free_of</c> ومَمنوعَةٌ عَلى غَيرِه</b>. وُجودُ
    /// واحِدَةٍ مِنها في القيمَةِ يَعني رَفضاً.</summary>
    public IReadOnlyList<string> ForbiddenPhrases { get; init; } = [];

    /// <summary>أَينَ يُسَدُّ النَقص — رابِطٌ تَعرِضُه اللَوحَةُ
    /// بِجِوارِ السَطرِ الأَحمَر. <c>null</c> حَيثُ لا شاشَةَ بَعد،
    /// و<see cref="RemedyAr"/> يَقولُ ماذا يُفعَل.</summary>
    public string? RemedyRoute { get; init; }

    /// <summary>ماذا يُفعَلُ حَرفِيّاً لِسَدِّ النَقص. <b>ولا يَحوي
    /// نَصّاً قانونِيّاً مُقتَرَحاً</b> (القاعِدَة ١٦): يَقولُ «يُملَأُ
    /// هذا المِفتاح» ولا يَقولُ بِماذا يُملَأ.</summary>
    public IReadOnlyDictionary<string, string?> Remedy { get; init; } = ComplianceText.Empty;
}

/// <summary>
/// <para><b>شَرطٌ مِن الالتِزامِ لا يُفحَصُ لَفظِيّاً — مُعلَناً في
/// المِلَفّ.</b> ووُجودُ هذا القِسمِ شَرطٌ مِعمارِيٌّ لا تَجميل:
/// بِدونِه يُخضِرُّ الفاحِصُ بَنداً لِأَنَّ نَصَّهُ مَنشورٌ بَينَما
/// فِعلُه غائِب — وذلكَ أَسوَأُ مِن غِيابِ الفاحِصِ أَصلاً، لِأَنَّه
/// غِيابٌ يَحمِلُ شَهادَةَ حُضور.</para>
/// </summary>
public sealed record UncheckableClause
{
    public string Code { get; init; } = "";

    /// <summary>لِماذا لا يُفحَصُ لَفظِيّاً، وبِماذا يُفحَصُ لَو
    /// فُحِص.</summary>
    public IReadOnlyDictionary<string, string?> Reason { get; init; } = ComplianceText.Empty;
}

/// <summary>
/// <para><b>تَعريفُ التِزام — مُواطِنٌ خامِسٌ في عائِلَةِ مِلَفّاتِ
/// السِياسَة</b> بَعدَ <c>*.role.json</c> و<c>*.plan.json</c>
/// والثيمِ و<c>*.provider.json</c>: مَورِدٌ مَضمون + فِهرِس +
/// <c>UnmappedMemberHandling.Disallow</c> + مُصادِقٌ بِرُموزٍ ثابِتَة.</para>
///
/// <para><b>ولِماذا بَياناتٌ لا كود — والسَبَبُ لَيسَ الأَناقَة</b>:
/// قائِمَةُ الالتِزاماتِ <b>تَنمو وتَتَغَيَّرُ بِتَغَيُّرِ الأَنظِمَة</b>
/// وبِتَغَيُّرِ شُروطِ المَتاجِر. فَإن كُتِبَت كوداً لَزِمَ إصدارٌ
/// لِكُلِّ مادَّةٍ جَديدَة؛ وإن كانَت بَياناتٍ أُضيفَ مِلَفٌّ وانتَهى
/// الأَمر. وهذا هُوَ المِعيارُ الفاصِلُ في القاعِدَة ٤ مُطَبَّقاً عَلى
/// الامتِثالِ نَفسِه.</para>
///
/// <para><b>وحَدُّ المَوجَةِ مُعلَن</b>: التَعريفاتُ <b>مَضمونَةٌ في
/// العُدَّة</b> لا وَثائِقُ Marten لِكُلِّ مُستَأجِر — نَفسُ حَدِّ
/// <c>RoleDefinitionLoader</c> و<c>ProviderDefinition</c>. ويَومَ
/// يُؤَلِّفُ مُستَأجِرٌ التِزاماً يَتَغَيَّرُ
/// <see cref="ObligationDefinitionLoader"/> وَحدَه.</para>
/// </summary>
public sealed record ObligationDefinition
{
    /// <summary>مُعَرِّفٌ فَريدٌ عَبرَ الكاتالوجِ كُلِّه.</summary>
    public string Id { get; init; } = "";

    /// <summary>مِن <see cref="ComplianceLevels"/> حَصراً.</summary>
    public string Level { get; init; } = "";

    public IReadOnlyDictionary<string, string?> Label { get; init; } = ComplianceText.Empty;

    public ObligationSource Source { get; init; } = new();

    public IReadOnlyList<EvidenceRequirement> Evidence { get; init; } = [];

    public IReadOnlyList<UncheckableClause> NotCheckable { get; init; } = [];
}
