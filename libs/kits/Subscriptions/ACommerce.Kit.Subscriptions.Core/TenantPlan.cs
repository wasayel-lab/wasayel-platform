using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACommerce.Kit.Subscriptions;

// ═══ باقَةُ المُستَأجِر نَفسِه — اشتِراكُ المَتجَرِ في **وَسايِل** ══════
//
// **ولا يُخلَط بِـ`TenantPlanDefinition`/`TenantPlanSet`/`TenantPlanService`**
// في نَفس المِلَفّ المُجاوِر: تِلكَ **باقاتٌ يُؤَلِّفُها المُستَأجِرُ
// لِمُستَخدِمي مَتجَرِه** ويَراها الزائِر على `/{slug}/plans`. وهذِه
// **عَلاقَةُ المَتجَرِ بِالمَنَصَّة**: مَن يَدفَع لِوَسايِل، ومَتى
// يَنتَهي، وماذا يَحدُث بَعدَ الانتِهاء. الطَرَفانِ لا يَلتَقِيان.
//
// **وقَرارُ المالِك الَّذي كَتَبَ هذا المِلَفّ (‏2026-08-23)، حَرفيّاً**:
// «لا تَسمَح لِلتاجِر بِاستِلام حَوالات» و«إمّا بَيعٌ بِلا رُسوم أَو
// تَكامُلُ بَوّابَةِ دَفعٍ خاصَّةٍ بِه لاحِقاً» و«يُمكِنُني البَدءُ
// بِقَبول المَدفوعات كَحَوالاتٍ بَنكِيَّة» — إلى **وَسايِل**. فَالإيرادُ
// يَنتَقِل مِن مُستَوى مُستَخدِمِ المَتجَر إلى مُستَوى المُستَأجِر،
// وتَعليماتُ التَحويلِ تَخُصُّ حِسابَ وَسايِل لا حِسابَ التاجِر.

/// <summary>حالَةُ الباقَة **كَما يَضبُطُها مُشرِفُ المَنَصَّة** — مَعجَمٌ
/// مُغلَقٌ بِقيمَتَين. وهي غَيرُ <see cref="TenantPlanState"/> المُشتَقَّةِ
/// مِن الوَقت: هذِه نِيَّةٌ مُخَزَّنَة، وتِلكَ واقِعٌ يُحسَب.</summary>
public static class PlatformPlanStatuses
{
    /// <summary>الباقَةُ سارِيَة — والانتِهاءُ يُحسَب مِن التَواريخ.</summary>
    public const string Active = "active";

    /// <summary>أَوقَفَها المُشرِفُ يَدَوِيّاً قَبلَ انتِهائِها.</summary>
    public const string Stopped = "stopped";

    public static readonly IReadOnlyList<string> All = new[] { Active, Stopped };

    public static bool Contains(string? status)
        => status is not null && All.Contains(status, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>الحالَةُ المُشتَقَّةُ مِن الوَقت — لا تُخَزَّن أَبَداً.</b></para>
///
/// <para><b>ولِماذا مُشتَقَّةٌ لا مَحفوظَة</b>: حالَةٌ مَحفوظَةٌ تَحتاج
/// مَن يُقَلِّبُها عِندَ انتِهاء المُدَّة — أَي وَظيفَةً دَورِيَّة، وهي
/// الآلَةُ الَّتي إن تَوَقَّفَت بَقِيَ مَتجَرٌ مُنتَهٍ يَكتُب شَهراً بِلا
/// أَن يَشتَكِيَ شَيء. والاشتِقاقُ مِن <c>ExpiresAt</c> لا يَحتاج
/// آلَةً: كُلُّ طَلَبٍ يَحسُبُها مِن جَديد.</para>
/// </summary>
public enum TenantPlanState
{
    /// <summary><b>لا وَثيقَةَ باقَةٍ لِهذا المُستَأجِر</b> — وهي حالَةُ
    /// كُلّ مَتجَرٍ قائِمٍ اليَوم. <b>التَكافُؤُ الصِفريّ هو العَقد</b>:
    /// يَكتُب ويُعرَض كَما كانَ بِالضَبط، ولا يَمُرّ بِسَطر قَرارٍ
    /// واحِدٍ إضافيّ.</summary>
    None,

    /// <summary>سارِيَة — كُلُّ شَيءٍ مَفتوح.</summary>
    Active,

    /// <summary>انتَهَت، ونَحنُ داخِلَ مُهلَة السَماح: <b>قِراءَةٌ نَعَم،
    /// كِتابَةٌ لا</b>.</summary>
    Grace,

    /// <summary>انقَضَت المُهلَة (أَو أَوقَفَها المُشرِف): المَتجَرُ
    /// <b>يُخفى</b> — والواجِهَةُ تَرُدّ صَفحَةَ «مُعَلَّق».
    /// <b>ولا يُحذَف شَيء.</b></summary>
    Suspended
}

/// <summary>
/// <para><b>وَثيقَةُ باقَةِ المُستَأجِر</b> — على مُستَوى المَنَصَّة
/// (‏<c>SingleTenanted</c> كَوَثيقَة <c>Tenant</c> ولِنَفس السَبَب:
/// تُقرَأ <b>قَبلَ</b> أَن يُعرَف مُستَأجِر الطَلَب). ومُعَرِّفُها سلاجُ
/// المُستَأجِر، فَلا فَهرَسَةَ ثانِيَة ولا احتِمالُ وَثيقَتَين
/// لِمَتجَرٍ واحِد.</para>
/// </summary>
public sealed class TenantPlan
{
    /// <summary>سلاجُ المُستَأجِر — المِفتاحُ الأَوَّليّ.</summary>
    public string Id { get; set; } = "";

    /// <summary>سلاجُ الباقَة مِن كاتالوج المَنَصَّة
    /// (<see cref="PlatformPlanCatalog"/>).</summary>
    public string PlanId { get; set; } = "";

    /// <summary>مِن <see cref="PlatformPlanStatuses.All"/> حَصراً.</summary>
    public string Status { get; set; } = PlatformPlanStatuses.Active;

    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>أَيّامُ السَماح بَعدَ <see cref="ExpiresAt"/>. قيمَةُ
    /// بَياناتٍ يُغَيِّرُها المُشرِف، وافتِراضُها مِن الكاتالوج لا مِن
    /// الكود.</summary>
    public int GraceDays { get; set; } = PlatformPlanCatalog.DefaultGraceDays;

    /// <summary><b>السِعرُ حَقلٌ يَملَؤُه المُشرِف</b> — لا رَقمَ سِعرٍ
    /// مَكتوبٌ في الكود ولا في الكاتالوج (القاعِدَة ١٦). صِفرٌ يَعني
    /// «لَم يُسَجَّل بَعد»، ولا يُغَيِّر شَيئاً في القَرار.</summary>
    public decimal Price { get; set; }

    /// <summary>مَن ضَبَطَها ومَتى — لِلتَدقيق لا لِلقَرار.</summary>
    public string SetBy { get; set; } = "";
    public DateTime SetAt { get; set; } = DateTime.UtcNow;
}

/// <summary>خَرقٌ واحِدٌ في ضَبط باقَةِ مُستَأجِر. نَفسُ شَكل
/// <c>PlanDefinitionViolation</c> — القالِبُ المَرجِعيّ (القاعِدَة ٤).</summary>
public sealed record TenantPlanViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>قَرارُ باقَةِ المُستَأجِر — دَوالُّ نَقِيَّة.</b> لا Marten،
/// ولا HTTP، ولا <c>DateTime.UtcNow</c>: الوَقتُ يُمَرَّر. فَتُنادى مِن
/// اختِبارٍ بِلا قاعِدَةِ بَيانات، وهذا شَرطُ أَن يُبرهَنَ
/// الإغلاقُ أَصلاً.</para>
/// </summary>
public static class TenantPlanPolicy
{
    // ─── رُموزُ الخَرق ────────────────────────────────────────────────

    public const string PlanUnknown   = "tenant_plan_unknown";
    public const string StatusUnknown = "tenant_plan_status_unknown";
    public const string PeriodInvalid = "tenant_plan_period_invalid";
    public const string GraceNegative = "tenant_plan_grace_negative";
    public const string PriceNegative = "tenant_plan_price_negative";

    /// <summary>سَقفٌ مُعلَنٌ لِمُهلَة السَماح — ثَلاثَةُ أَشهُر. ما
    /// فَوقَه خَطَأُ إدخالٍ لا سِياسَة.</summary>
    public const int MaxGraceDays = 90;

    // ─── البَوّابَة ──────────────────────────────────────────────────

    /// <summary>القائِمَةُ الفارِغَةُ تَعني ضَبطاً صالِحاً.</summary>
    public static IReadOnlyList<TenantPlanViolation> Validate(TenantPlan plan)
    {
        var v = new List<TenantPlanViolation>();

        if (!PlatformPlanCatalog.Contains(plan.PlanId))
            v.Add(new(PlanUnknown,
                $"الباقَة «{plan.PlanId}» خارِج كاتالوج المَنَصَّة. " +
                $"المُتاح: {string.Join("، ", PlatformPlanCatalog.Slugs)}."));

        if (!PlatformPlanStatuses.Contains(plan.Status))
            v.Add(new(StatusUnknown,
                $"الحالَة «{plan.Status}» خارِج المَعجَم: " +
                $"{string.Join("، ", PlatformPlanStatuses.All)}."));

        if (plan.ExpiresAt <= plan.StartsAt)
            v.Add(new(PeriodInvalid,
                "تاريخُ الانتِهاء لا يَتَجاوَز تاريخَ البَدء — باقَةٌ " +
                "تَنتَهي قَبلَ أَن تَبدَأ."));

        if (plan.GraceDays < 0 || plan.GraceDays > MaxGraceDays)
            v.Add(new(GraceNegative,
                $"أَيّامُ السَماح {plan.GraceDays} خارِجَ المَدى 0..{MaxGraceDays}."));

        if (plan.Price < 0m)
            v.Add(new(PriceNegative, $"السِعر سالِب: {plan.Price}."));

        return v;
    }

    public static bool IsValid(TenantPlan plan) => Validate(plan).Count == 0;

    // ─── الاشتِقاق ───────────────────────────────────────────────────

    /// <summary>
    /// <para><b>الحالَةُ الفِعلِيَّةُ الآن.</b> و<c>null</c> يُعطي
    /// <see cref="TenantPlanState.None"/> — أَي المَتجَرَ القائِمَ بِلا
    /// باقَةٍ مَضبوطَة: لا يَتَغَيَّر لَه شَيء.</para>
    ///
    /// <para><b>وتَرتيبُ الفُروع مَقصود</b>: الإيقافُ اليَدَوِيُّ يَسبِق
    /// حِسابَ التَواريخ، فَإيقافُ باقَةٍ سارِيَةٍ يَقَع مِن لَحظَتِه ولا
    /// يَنتَظِر انتِهاءَها.</para>
    /// </summary>
    public static TenantPlanState Derive(TenantPlan? plan, DateTime now)
    {
        if (plan is null) return TenantPlanState.None;
        if (plan.Status == PlatformPlanStatuses.Stopped) return TenantPlanState.Suspended;
        if (now <= plan.ExpiresAt) return TenantPlanState.Active;
        return now <= plan.ExpiresAt.AddDays(Math.Max(plan.GraceDays, 0))
            ? TenantPlanState.Grace
            : TenantPlanState.Suspended;
    }

    /// <summary>أَتُقبَلُ الكِتابَةُ في المَتجَر؟ <b>السَماحُ يَمنَع
    /// الكِتابَةَ ويُبقي القِراءَة</b> — وهذا هو الفَرقُ الوَحيدُ بَينَه
    /// وبَينَ السَرَيان.</summary>
    public static bool AllowsWrite(TenantPlanState state)
        => state is TenantPlanState.None or TenantPlanState.Active;

    /// <summary>أَيُعرَضُ المَتجَرُ لِلزائِر؟ يَبقى مَعروضاً في السَماح
    /// (لِيَرى الزائِرُ ما فيه ويَرى صاحِبُه لافِتَةَ التَجديد)،
    /// ويُخفى بَعدَه.</summary>
    public static bool IsVisible(TenantPlanState state)
        => state is not TenantPlanState.Suspended;

    /// <summary>آخِرُ يَومٍ يُقبَلُ فيه التَجديدُ قَبلَ الإخفاء —
    /// لِلعَرض في لافِتَة الاستوديو.</summary>
    public static DateTime? HiddenAt(TenantPlan? plan)
        => plan is null ? null : plan.ExpiresAt.AddDays(Math.Max(plan.GraceDays, 0));

    // ─── قِراءَةُ ما يَكتُبُه المُشرِف ────────────────────────────────

    /// <summary>
    /// <para><b>حُقولُ النَموذَج مَقروءَةً — دالَّةٌ نَقِيَّة لا تَعرِف
    /// HTTP.</b> تَأخُذ سَلاسِلَ وتُعطي قِيَماً بِأَنواعِها؛ والنُقطَةُ
    /// تُمَرِّر ما قَرَأَتهُ مِن النَموذَج. فَلا مُهايِئَ HTTP في
    /// مُجَلَّد الخِدمَة (وذاكَ شَرطٌ مَفروضٌ بِفاحِص)، ولا تَحليلَ
    /// تَواريخَ في جِسمِ نُقطَة.</para>
    ///
    /// <para><b>والسُقوطُ عِندَ كُلّ حَقلٍ مَقصود</b>: تاريخُ بَدءٍ
    /// غائِبٌ = اليَوم، وانتِهاءٌ غائِبٌ = <b>لا شَيء</b> فَيُرَدُّ
    /// بِـ<see cref="PeriodInvalid"/> — لا «سَنَةٌ افتِراضِيَّة»
    /// تُخترَع. ومُهلَةٌ غائِبَةٌ = افتِراضُ الكاتالوج.</para>
    /// </summary>
    public static (string PlanId, DateTime StartsAt, DateTime ExpiresAt, int GraceDays, decimal Price)
        ReadSetting(string? planId, string? startsAt, string? expiresAt,
                    string? graceDays, string? price, DateTime now)
    {
        var starts = DateTime.TryParse(startsAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
            | System.Globalization.DateTimeStyles.AdjustToUniversal, out var s)
            ? s : now.Date;

        var expires = DateTime.TryParse(expiresAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
            | System.Globalization.DateTimeStyles.AdjustToUniversal, out var e)
            ? e : DateTime.MinValue;

        var grace = int.TryParse(graceDays, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var g)
            ? g : PlatformPlanCatalog.DefaultGraceDays;

        var amount = decimal.TryParse(price, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0m;

        return ((planId ?? "").Trim(), starts, expires, grace, amount);
    }
}

// ═══ كاتالوجُ باقات المَنَصَّة — بَياناتٌ لا أَرقامٌ في كود ═══════════

/// <summary>تَعريفُ باقَةِ مَنَصَّةٍ واحِدَة، مَقروءٌ مِن مِلَفّ JSON
/// على نَمَط <c>*.role.json</c>. <b>ولا سِعرَ فيه</b>: السِعرُ حَقلٌ
/// يَملَؤُه المُشرِفُ لِكُلّ مُستَأجِر، ورَقمٌ يُكتَب هُنا اليَومَ
/// اختِراعُ بَياناتِ مُنتَج (القاعِدَة ١٦).</summary>
public sealed record PlatformPlanDefinition(
    string Slug,
    string LabelAr,
    string DescriptionAr,
    int    DefaultGraceDays);

/// <summary>
/// <para><b>كاتالوجُ باقات المَنَصَّة — مَعجَمٌ مُغلَقٌ مَصدَرُه
/// مِلَفّاتٌ مُضَمَّنَة.</b> يُقرَأ مِن `Definitions/plans.index.json`
/// وما يُشير إلَيه، ويُصادَق عِندَ التَحميل: مِلَفٌّ فاسِدٌ **يُفشِل
/// الإقلاع** بِرِسالَةٍ تُسَمّي الباقَةَ والسَبَب، ولا يَمُرّ صامِتاً.</para>
///
/// <para><b>ولِماذا مِلَفٌّ لا ثابِتٌ في C#</b>: الباقَةُ الثانِيَةُ
/// قادِمَةٌ حينَ يَقرِّرُ المالِكُ سِعرَها وشُروطَها، وإضافَتُها يَجِب
/// أَلّا تَكونَ إصداراً. نَفسُ حُجَّة <c>*.role.json</c> حَرفاً.</para>
/// </summary>
public static class PlatformPlanCatalog
{
    /// <summary>مُهلَةُ السَماحِ الافتِراضِيَّة حينَ لا يَقولُ الكاتالوج
    /// غَيرَها — <b>أَربَعَةَ عَشَرَ يَوماً</b>، قَرارُ صَباحٍ مَكتوبٌ لا
    /// رَقمٌ مُخترَع، ويُغَيِّرُه المُشرِفُ لِكُلّ مُستَأجِر.</summary>
    public const int DefaultGraceDays = 14;

    private const string IndexResourceSuffix = ".Definitions.plans.index.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy         = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive  = false,
        // مِفتاحٌ مَجهولٌ في مِلَفّ تَعريف = خَطَأٌ صَريحٌ لا تَجاهُلٌ صامِت.
        UnmappedMemberHandling       = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling          = JsonCommentHandling.Disallow,
        AllowTrailingCommas          = false,
    };

    private sealed record PlansIndex
    {
        public IReadOnlyList<string> Plans { get; init; } = [];
    }

    /// <summary><b>الكاتالوجُ مُحَمَّلاً كَسولاً</b> — أَوَّلُ لَمسَةٍ
    /// تَقرَؤُه، ومِلَفٌّ فاسِدٌ يُفشِل الإقلاعَ بِرِسالَتِه لا يَمُرّ
    /// صامِتاً. نَفسُ شَكل <c>ThemeCatalog</c>.</summary>
    private static readonly Lazy<IReadOnlyList<PlatformPlanDefinition>> _all =
        new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<PlatformPlanDefinition> All => _all.Value;

    public static IReadOnlyList<string> Slugs => All.Select(p => p.Slug).ToArray();

    public static bool Contains(string? slug)
        => slug is not null && All.Any(p => string.Equals(p.Slug, slug, StringComparison.Ordinal));

    public static PlatformPlanDefinition? Find(string? slug)
        => slug is null ? null : All.FirstOrDefault(p => p.Slug == slug);

    /// <summary>قِراءَةُ تَعريفٍ مِن نَصّ — بِنَفس <see cref="Options"/>
    /// الَّتي يَقرَأ بِها <see cref="LoadEmbedded"/>، فَما يَصِحّ هُنا
    /// يَصِحّ هُناك بِالبِناء لا بِالمُصادَفَة.</summary>
    public static PlatformPlanDefinition ParseDefinition(string json)
        => JsonSerializer.Deserialize<PlatformPlanDefinition>(json, Options)
           ?? throw new InvalidOperationException("نَصُّ تَعريفِ باقَةِ المَنَصَّة أَعطى null.");

    /// <summary>يُحَمِّلُ التَعريفاتِ بِتَرتيب المِلَفّ الفِهرِس. يَرمي
    /// عِندَ أَيّ نَقصٍ أَو خَرق — <b>فَكاتالوجٌ فاسِدٌ يُفشِل الإقلاعَ
    /// بِاسمِه</b>، ولا يُكتَشَف مِن سِجِلٍّ لَيلاً.</summary>
    public static IReadOnlyList<PlatformPlanDefinition> LoadEmbedded()
    {
        var asm = typeof(PlatformPlanCatalog).Assembly;
        var index = Read<PlansIndex>(asm, IndexResourceSuffix);

        if (index.Plans.Count == 0)
            throw new InvalidOperationException(
                "plans.index.json فارِغ — كاتالوجُ المَنَصَّة بِلا باقَةٍ واحِدَة.");

        var list = new List<PlatformPlanDefinition>(index.Plans.Count);
        foreach (var slug in index.Plans)
        {
            var d = Read<PlatformPlanDefinition>(asm, $".Definitions.{slug}.plan.json");

            if (!string.Equals(d.Slug, slug, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"تَعريفُ الباقَة «{slug}» يُعلِن slug مُختَلِفاً: «{d.Slug}».");
            if (string.IsNullOrWhiteSpace(d.LabelAr))
                throw new InvalidOperationException(
                    $"تَعريفُ الباقَة «{slug}» بِلا تَسمِيَةٍ عَرَبِيَّة.");
            if (d.DefaultGraceDays < 0 || d.DefaultGraceDays > TenantPlanPolicy.MaxGraceDays)
                throw new InvalidOperationException(
                    $"تَعريفُ الباقَة «{slug}»: أَيّامُ السَماح {d.DefaultGraceDays} " +
                    $"خارِجَ المَدى 0..{TenantPlanPolicy.MaxGraceDays}.");

            list.Add(d);
        }
        return list;
    }

    private static T Read<T>(System.Reflection.Assembly asm, string resourceSuffix)
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
