namespace ACommerce.Kit.Subscriptions;

/// <summary>خَرق واحِد في مَعجَم القُدُرات. <c>Code</c> مِفتاح ثابِت
/// لِلاختِبارات واللوغ، و<c>MessageAr</c> لِلمُراجِع البَشَريّ. نَفس شَكل
/// <c>RoleDefinitionViolation</c> و<c>DealPatternViolation</c> — القالِب
/// المَرجِعيّ في القاعِدَة ٤.</summary>
public sealed record CapabilityViolation(string Code, string MessageAr);

/// <summary>نَوع الحَدّ الَّذي تَحمِلُه القُدرَة. <b>مَعجَم مُغلَق
/// بِنَفسِه</b> — ونَوعٌ ثالِث يَحتاج سَطراً هُنا وقَراراً.</summary>
public static class CapabilityKinds
{
    /// <summary>حِصَّة عَدَدِيَّة تُستَهلَك وَحدَةً وَحدَة
    /// (<c>Plan.ListingsQuota</c>, <c>TierLimits.AnalysesPerMonth</c>…).</summary>
    public const string Quota = "quota";

    /// <summary>رايَة ثُنائيَّة: مَسموح أَو لا، بِلا عَدّ
    /// (<c>TierLimits.AllowExport</c>).</summary>
    public const string Flag = "flag";

    public static readonly IReadOnlyList<string> All = new[] { Flag, Quota };

    public static bool Contains(string kind) => All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>نِطاقُ القُدرَة — <b>مَن يُسأَلُ عَنها</b>. مَعجَمٌ مُغلَقٌ
/// بِقيمَتَين، وثالِثَةٌ تَحتاج سَطراً هُنا وقَراراً.</summary>
public static class CapabilityScopes
{
    /// <summary>حَدٌّ عَلى <b>مُستَخدِمٍ</b> بِعَينِه — رَصيدُ باقَتِه أَو
    /// رايَةُ مِفتاحِه. وهي كُلُّ ما كانَ في المَعجَم حَتّى ‏2026-08-23.</summary>
    public const string User = "user";

    /// <summary>حَدٌّ عَلى <b>المَتجَرِ كُلِّه</b> — لا يَختَلِف بِاختِلاف
    /// المُستَخدِم، ويُسأَل عَنه حَتّى بِلا جَلسَة. مَصدَرُه وَثيقَةُ
    /// `TenantPlan` (‏ADR-003).</summary>
    public const string Tenant = "tenant";

    public static readonly IReadOnlyList<string> All = new[] { Tenant, User };

    public static bool Contains(string scope) => All.Contains(scope, StringComparer.Ordinal);
}

/// <summary>قُدرَة واحِدَة: رَمزُها، ونَوع حَدِّها، ونِطاقُها، ومَصدَر
/// رَقمِها المَقيس. <see cref="SourceRef"/> ليسَ زينَةً — هو الشَرط
/// الَّذي يَمنَع نُمُوَّ المَعجَم بِالخَيال: قُدرَة بِلا مَوضِع
/// يَحُدُّها اليَوم لا تَدخُل. و<see cref="Scope"/> يَسقُط إلى
/// <see cref="CapabilityScopes.User"/> — فَالقُدُراتُ السِتّ الَّتي
/// سَبَقَتهُ لا يُمَسّ سَطرٌ مِنها.</summary>
public sealed record Capability(
    string Code, string Kind, string SourceRef, string Scope = CapabilityScopes.User);

/// <summary>
/// <para><b>مَعجَم القُدُرات المُغلَق — مَوضِع واحِد</b>. كُلّ سِلسِلَة
/// تُمَرَّر إلى فَحص الاستِحقاق (<c>RequireEntitlement</c>،
/// <c>PeekAsync</c>، <c>ConsumeAsync</c>) تَأتي مِن هُنا <b>حَصراً</b>.</para>
///
/// <para><b>ولِماذا يُغلَق الطَرَفانِ مِن يَومِه</b>: سَلَفُه
/// <c>ACommerce.Kit.Roles.PermissionCatalog</c> أَغلَقَ طَرَف
/// <i>التَعريف</i> وأَعلَنَ صَراحَةً (في تَعليقِه) أَنَّه تَرَكَ طَرَف
/// <i>مَواضِع الفَحص</i> مَفتوحاً — فَـ<c>RequirePermission("…")</c>
/// يَقبَل أَيّ سِلسِلَة إلى اليَوم. كانَ ذلك قَراراً صَحيحاً هُناك
/// (المَواضِع قائِمَة سَلَفاً، وإغلاقُها تَغيير سُلوك). وهُنا الحِساب
/// مَقلوب: <b>لا مَوضِع فَحص استِحقاق واحِد قائِم</b> — فَإغلاق الطَرَف
/// اليَوم <b>مَجّانيّ</b>، وتَأجيلُه يَشتَري نَفس الدَين بِثَمَنِه
/// كامِلاً. القاعِدَة ٤: الحَدّ الَّذي لا يُقاس آليّاً يَنهار.</para>
///
/// <para><b>والفَحص الآليّ طَرَفان</b>:</para>
/// <list type="bullet">
///   <item><b>وَقت التَركيب</b>: <c>RequireEntitlement</c> يَرمي عِندَ
///   بِناء المَسار لَو كانَ الرَمز خارِج المَعجَم — فَالخَطَأ يُفشِل
///   الإقلاع، لا طَلَباً واحِداً في اللَيل.</item>
///   <item><b>وَقت الاختِبار</b>: مَسحُ شَجَرَة يُثبِت أَنّ كُلّ سِلسِلَة
///   حَرفِيَّة في مَوضِع فَحص عُضوٌ في <see cref="All"/> — يُمسِك ما
///   لا يَبلُغُه إقلاعُ الاختِبارات.</item>
/// </list>
///
/// <para><b>وما ليسَ مِنه، بِقَصد ومَقيساً</b>:</para>
/// <list type="bullet">
///   <item><c>auth.request</c>/<c>auth.verify</c> —
///   <c>MaxRequestPerHour</c> و<c>MaxVerifyPerMinute</c>
///   (<c>AuthHandlers.cs:33-34</c>) حَدّ <b>مُعَدَّل في الذاكِرَة</b>:
///   حِمايَة مِن إساءَة لا استِحقاق باقَة. لا يُباع، ولا يُشترى
///   بِتَرقِيَة، ولا يُخَزَّن.</item>
///   <item>الإشعارات · المُحادَثات · العُروض · الدَعم · التَذاكِر ·
///   الأَدوار · الثيمات — <b>لا حَدّ مُعلَناً لَها في أَيّ مَوضِع مِن
///   المُستودَع</b> (مَسحُ <c>Quota|Limit|Max[A-Z]|PerMonth|Allow[A-Z]</c>
///   عَلى <c>libs</c>+<c>apps</c> لا يُعطي واحِداً). فَإضافَتُها اليَوم
///   <b>اختِراع بَيانات مُنتَج</b> — القاعِدَة ١٦.</item>
///   <item><c>studio.custom_pattern</c> — <b>كانَ سادِساً وسَقَطَ
///   بِالقِياس</b>. وَصَفَه تَصميم هذه الطَبَقَة رايَةً «تُباع في ثَلاث
///   شاشات ولا يَفحَصُها سَطر»؛ والمُستودَع اليَوم لا يَحوي
///   <c>AllowCustomPattern</c> إلّا في تَعليق واحِد يَشرَح سَحبَه
///   (<c>StudioTier.cs:8-26</c>). سُحِبَ الحَقل وسَطراه في
///   <c>StudioBilling.razor</c> و<c>UpgradePrompt.razor</c> قَبل هذه
///   المَوجَة. فَلا يُبعَث هُنا: <b>المَعجَم يَصِف ما يُحَدّ اليَوم لا ما
///   وُصِفَ يَوماً.</b></item>
/// </list>
/// </summary>
public static class CapabilityCatalog
{
    // ─── الرُموز — ثَوابِت لا سَلاسِل مَنثورَة ────────────────────────

    /// <summary>نَشر إعلان. مَصدَرُه <c>Plan.ListingsQuota</c>، ورَصيدُه
    /// <c>Subscription.QuotaRemaining</c> مِن تَيار أَحداث.</summary>
    public const string ListingCreate = "listing.create";

    /// <summary>تَحليل جَدوى في الاستوديو.</summary>
    public const string StudioAnalyze = "studio.analyze";

    /// <summary>تَحسين دِراسَة في الاستوديو.</summary>
    public const string StudioRefine = "studio.refine";

    /// <summary>بِناء مَتجَر مِن الاستوديو.</summary>
    public const string StudioBuild = "studio.build";

    /// <summary>تَصدير دِراسَة — رايَة لا حِصَّة.</summary>
    public const string StudioExport = "studio.export";

    /// <summary>
    /// <para><b>الوُصول عَبر الـAPI</b> — <b>رايَة لا حِصَّة</b>،
    /// وهذا قَرارٌ مُعلَنٌ لا تَقصير: الحِصَّةُ تَحتاج <b>رَقماً</b>،
    /// ولا رَقمَ لِحِصَّةِ API في المُستَودَع ولا في <c>docs/</c>
    /// (‏مَسحُ <c>Quota|Limit|Max[A-Z]|PerMonth</c> لا يُعطي واحِداً)
    /// — فَاختِراعُه اختِراعُ بَياناتِ مُنتَج (القاعِدَة ١٦).
    /// والحِصَّةُ العَدَدِيَّةُ قَرارُ مُنتَجٍ مُنفَصِل، مُؤَجَّلٌ
    /// بِاسمِه في ‏§٨ مِن وَثيقَة التَصميم.</para>
    ///
    /// <para><b>ومَصدَرُ حَدِّها اليَوم وَثيقَةُ المِفتاح نَفسُها</b>:
    /// <c>Status</c> و<c>ExpiresAt</c> و<c>Scopes</c> — وهي المَواضِعُ
    /// الثَلاثَةُ الَّتي تَمنَع طَلَباً فِعلاً. فَـ<c>SourceRef</c>
    /// يُشير إلى ما يَحُدُّ حَقّاً، لا إلى حَقلٍ يُخترَع في
    /// <c>Plan</c> لِيَملَأَ خانَة.</para>
    ///
    /// <para><b>والرايَةُ مَفروضَةٌ لا مَعروضَة</b>: لا تُذكَر في
    /// شاشَةِ باقاتٍ واحِدَة — الخَطَرُ ٧ في وَثيقَة التَصميم
    /// (<c>AllowCustomPattern</c> تُباع في ثَلاثِ شاشات وصِفرُ
    /// مَوضِعٍ يَفحَصُها) مَقلوبٌ هُنا عَمداً: تُفحَص في
    /// <c>ApiKeyFilter</c> على كُلّ نُقطَة، ولا تُباع في شاشَة.</para>
    /// </summary>
    public const string ApiCall = "api.call";

    /// <summary>
    /// <para><b>الكِتابَةُ في المَتجَر</b> — رايَةٌ <b>على المُستَأجِر
    /// لا على المُستَخدِم</b>، وهي أَوَّلُ قُدرَةٍ بِنِطاقٍ
    /// <see cref="CapabilityScopes.Tenant"/>.</para>
    ///
    /// <para><b>ولِماذا في هذا المَعجَم لا في حارِسٍ جَديد</b>: المُستَودَعُ
    /// فيه أَربَعَةُ أَنابيبِ اعتِراضٍ مَبنِيَّةٍ ومَهجورَة (القاعِدَة ٨).
    /// فَالحَدُّ الجَديدُ رَكِبَ الأُنبوبَ القائِمَ بِلا سَطرٍ واحِدٍ
    /// جَديدٍ فيه: نَفسُ <c>EntitlementFilter</c>، ونَفسُ
    /// <c>IEntitlements</c>، ونَفسُ البَوّابَةِ عِندَ التَركيب.</para>
    ///
    /// <para><b>ومَصدَرُ حَدِّها وَثيقَةُ <c>TenantPlan</c></b>: بَعدَ
    /// <c>ExpiresAt</c> تُمنَع الكِتابَةُ وتَبقى القِراءَة، وبَعدَ
    /// <c>GraceDays</c> يُخفى المَتجَر. <b>ولا وَثيقَةَ = لا حَدّ</b> —
    /// وهو حالُ كُلّ مَتجَرٍ قائِم.</para>
    /// </summary>
    public const string TenantWrite = "tenant.write";

    /// <summary><b>القُدُرات السَبع</b> — كُلّ ما يُحَدّ أَو يُباع اليَوم،
    /// مُرَتَّبَة أَبجَدِيّاً بِرَمزِها. الزِيادَة عَلَيها قَرار مُعلَن:
    /// سَطر هُنا + مَوضِع فَحص حَيّ + اختِبار سالِب يُثبِت المَنع.</summary>
    public static readonly IReadOnlyList<Capability> All = new[]
    {
        new Capability(ApiCall, CapabilityKinds.Flag,
            "ApiKeyDocument.Status/ExpiresAt/Scopes (ApiKey.cs) → ApiKeyFilter"),
        new Capability(ListingCreate, CapabilityKinds.Quota,
            "Plan.ListingsQuota (Subscription.cs:9) → Subscription.QuotaRemaining"),
        new Capability(StudioAnalyze, CapabilityKinds.Quota,
            "TierLimits.AnalysesPerMonth (StudioTier.cs:30) → StudioUser.AnalysesUsed"),
        new Capability(StudioBuild, CapabilityKinds.Quota,
            "TierLimits.StoresMax (StudioTier.cs:30) → StudioUser.StoresBuilt"),
        new Capability(StudioExport, CapabilityKinds.Flag,
            "TierLimits.AllowExport (StudioTier.cs:31) → MTE.cs:2462"),
        new Capability(StudioRefine, CapabilityKinds.Quota,
            "TierLimits.RefinesPerMonth (StudioTier.cs:30) → StudioUser.RefinesUsed"),
        new Capability(TenantWrite, CapabilityKinds.Flag,
            "TenantPlan.ExpiresAt/GraceDays (TenantPlan.cs) → TenantPlanPolicy.Derive",
            CapabilityScopes.Tenant),
    };

    /// <summary>الرُموز وَحدَها، بِنَفس التَرتيب.</summary>
    public static readonly IReadOnlyList<string> Codes =
        All.Select(c => c.Code).ToArray();

    private static readonly Dictionary<string, Capability> ByCode =
        All.ToDictionary(c => c.Code, StringComparer.Ordinal);

    /// <summary>هَل هذه السِلسِلَة قُدرَة مُعتَرَف بِها؟ المُقارَنَة
    /// <b>حَسّاسَة لِلحالَة</b> (Ordinal) كَما في
    /// <c>PermissionCatalog.Contains</c>.</summary>
    public static bool Contains(string capability) => ByCode.ContainsKey(capability);

    /// <summary>القُدرَة بِرَمزِها، أَو <c>null</c>.</summary>
    public static Capability? Find(string capability) =>
        ByCode.TryGetValue(capability, out var c) ? c : null;

    /// <summary>هَل هذه القُدرَة حِصَّة عَدَدِيَّة تُستَهلَك؟ (رايَة
    /// <see cref="CapabilityKinds.Flag"/> تُفحَص ولا تُستَهلَك.)</summary>
    public static bool IsQuota(string capability) =>
        Find(capability)?.Kind == CapabilityKinds.Quota;

    /// <summary>هَل هذِه القُدرَةُ حَدٌّ عَلى <b>المَتجَرِ كُلِّه</b>؟
    /// عِندَها لا يُسأَلُ عَن مُستَخدِم — والمُرَشِّحُ لا يَشتَرِط جَلسَةً
    /// قَبلَ أَن يَرُدّ. غِيابُ هذا السَطر كانَ سَيَجعَل كُلّ نُقطَةِ
    /// كِتابَةٍ بِلا `RequireAuth` تَرُدّ ‏401 بَدَلَ أَن تُفحَص.</summary>
    public static bool IsTenantScoped(string capability) =>
        Find(capability)?.Scope == CapabilityScopes.Tenant;

    /// <summary>
    /// <para><b>البَوّابَة</b> — تُنادى مِن كُلّ مَوضِع يَقبَل رَمز قُدرَة
    /// مِن خارِجِه. القائِمَة الفارِغَة تَعني رَمزاً صالِحاً.</para>
    /// </summary>
    public static IReadOnlyList<CapabilityViolation> Validate(string? capability)
    {
        var v = new List<CapabilityViolation>();

        if (string.IsNullOrWhiteSpace(capability))
        {
            v.Add(new("capability_empty", "رَمز القُدرَة فارِغ."));
            return v;
        }

        if (!Contains(capability))
            v.Add(new("capability_out_of_vocabulary",
                $"القُدرَة «{capability}» خارِج مَعجَم CapabilityCatalog. " +
                $"المَعجَم: {string.Join("، ", Codes)}."));

        return v;
    }

    /// <summary>هَل يَجتاز البَوّابَة؟</summary>
    public static bool IsValid(string? capability) => Validate(capability).Count == 0;

    /// <summary>
    /// <para><b>يَرمي عِندَ الخَرق</b> — لِمَواضِع التَركيب
    /// (<c>RequireEntitlement</c>) حَيثُ الخَطَأ يَجِب أَن يُفشِل
    /// <b>الإقلاع</b> لا طَلَباً واحِداً. وهذا بِعَينِه الطَرَف الَّذي
    /// تَرَكَه <c>PermissionCatalog</c> مَفتوحاً.</para>
    /// </summary>
    public static string Require(string capability)
    {
        var violations = Validate(capability);
        if (violations.Count > 0)
            throw new ArgumentException(
                string.Join(" | ", violations.Select(x => $"{x.Code}: {x.MessageAr}")),
                nameof(capability));
        return capability;
    }
}
