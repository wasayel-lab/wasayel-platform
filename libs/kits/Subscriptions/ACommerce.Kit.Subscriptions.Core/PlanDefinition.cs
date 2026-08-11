using System.Text.Json;
using System.Text.Json.Serialization;
using ACommerce.Platform.Flows;

namespace ACommerce.Kit.Subscriptions;

/// <summary>حاوِيَة تَوطين — العَرَبِيَّة إلزامِيَّة بِالمُصادَقَة،
/// وما سِواها مَوضِع مَحجوز. نَفس شَكل <c>LocalizedText</c> في عُدَّة
/// الأَدوار حَرفاً.</summary>
public sealed record PlanText(string Ar, string? En = null)
{
    /// <summary>مُستَثنىً مِن التَسَلسُل بِقَصد: الاختِيار يَقَع عِندَ
    /// <b>التَصيير</b> لا عِندَ التَخزين — ولَو وَقَعَ عِندَ التَخزين
    /// لَفُقِدَت اللُغَة الثانِيَة مِن الوَثيقَة المُخَزَّنَة.</summary>
    [JsonIgnore]
    public string Current => Ar ?? "";
}

/// <summary>
/// <para><b>الباقَة تَعريفاً كَبَيانات</b> — سِجِلّ خالِص، لا Marten ولا
/// HTTP ولا وَقت ولا عَشوائيَّة. نَفس عَقد <c>RoleDefinition</c> و
/// <c>ThemeDefinition</c> حَرفاً.</para>
///
/// <para><b>وعَلاقَتُه بِـ<see cref="Plan"/> مُعلَنَة</b>: <c>Plan</c>
/// وَثيقَة Marten حَيَّة يَقرَؤُها اليَوم <c>Plans.razor</c> ومَسار
/// الاشتِراك. وهذا التَعريف <b>لا يَحُلّ مَحَلَّها</b> بَل يُضاف
/// فَوقَها بِتَكافُؤٍ صِفريّ: مُستَأجِر بِلا وَثيقَة تَعريف واحِدَة
/// يُعطي <b>نَفس المَراجِع</b> الَّتي يُعطيها اليَوم. التَوحيد بَينَهُما
/// مَوجَة تالِيَة — والتَوحيد المُبَكِّر كانَ سَيَجعَل هذه المَوجَة
/// ثَلاثاً.</para>
/// </summary>
public sealed record PlanDefinition(
    string   Slug,
    PlanText Label,
    PlanText Description,
    decimal  Price,
    int      ListingsQuota,
    int      DaysPeriod,
    bool     IsActive = true)
{
    /// <summary>يُحَوَّل إلى الوَثيقَة الحَيَّة — وهذا هو المَوضِع
    /// الوَحيد الَّذي يَصير فيه تَعريفٌ باقَةً يَراها المُستَخدِم.</summary>
    public Plan ToPlan() => new()
    {
        Id            = Slug,
        Name          = Label.Current,
        Description   = string.IsNullOrWhiteSpace(Description.Current) ? null : Description.Current,
        Price         = Price,
        ListingsQuota = ListingsQuota,
        DaysPeriod    = DaysPeriod,
        IsActive      = IsActive,
    };
}

/// <summary>قِراءَة تَعريف باقَة مِن نَصّ — <b>بِنَفس خِيارات القِراءَة
/// في كُلّ مَوضِع</b>، فَما يَصِحّ في مِلَفّ يَصِحّ في وَثيقَة
/// بِالبِناء لا بِالمُصادَفَة. نَفس مُبَرِّر
/// <c>RoleDefinitionLoader</c>.</summary>
public static class PlanDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = false,
    };

    public static PlanDefinition ParseDefinition(string json)
        => JsonSerializer.Deserialize<PlanDefinition>(json, Options)
           ?? throw new InvalidOperationException("تَعريف باقَة فارِغ.");

    public static string ToJson(PlanDefinition d) => JsonSerializer.Serialize(d, Options);
}

/// <summary>حالات وَثيقَة تَعريف باقَة المُستَأجِر — <b>مُحالَة إلى
/// نَفس التَعريف الواحِد</b> الَّذي تُحيل إلَيه عُدَّتا الأَدوار
/// والمَظهَر. لا مَعجَم حالات رابِع.</summary>
public static class TenantPlanStatuses
{
    public const string Pending  = ApprovalFlow.Pending;
    public const string Approved = ApprovalFlow.Approved;
    public const string Rejected = ApprovalFlow.Rejected;

    public static IReadOnlyList<string> All => ApprovalFlow.All;

    public static bool Contains(string status) => ApprovalFlow.Contains(status);
}

/// <summary><b>وَثيقَة تَعريف باقَة لِمُستَأجِر واحِد</b> — بِإيجار
/// مُقتَرِن كَبَقِيَّة وَثائِق المُستَأجِر، والنَصّ يُخزَّن كَما كُتِبَ.
/// نَفس الأَعضاء الثَمانِيَة بِلا زِيادَة ولا نُقصان.</summary>
public sealed class TenantPlanDefinition : ITenantDefinitionDocument
{
    public string Id             { get; set; } = "";
    public string Slug           { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public string Status         { get; set; } = TenantPlanStatuses.Pending;
    public string CreatedBy      { get; set; } = "";
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public string? DecidedBy     { get; set; }
    public DateTime? DecidedAt   { get; set; }
}

/// <summary>
/// <para><b>طَبَقَة قَرار الباقات لِمُستَأجِر واحِد</b> — لَقطَة ساكِنَة
/// تُبنى مِن تَعريفات المُستَأجِر <b>المُعتَمَدَة</b> وَحدَها.</para>
///
/// <para><b>والتَكافُؤ الصِفريّ هو العَقد</b>: مُستَأجِر بِلا وَثيقَة
/// تَعريف واحِدَة يُعطي <see cref="Authored"/> فارِغَةً، فَتُرجِع
/// <see cref="Merge"/> <b>نَفس المَرجِع</b> الَّذي أُعطِيَ لَها — لا
/// نُسخَةً مُتَساوِيَة. أَي أَنّ صَفحَة الباقات لِكُلّ مُستَأجِر
/// قائِم اليَوم <b>لا تَمُرّ بِسَطر مَنطِق واحِد إضافيّ</b>.</para>
/// </summary>
public sealed class TenantPlanSet
{
    /// <summary>اللَقطَة بِلا أَيّ وَثيقَة — قاعِدَة المَنصَّة.</summary>
    public static readonly TenantPlanSet Platform = new(null, Array.Empty<PlanDefinition>());

    private TenantPlanSet(string? tenantSlug, IReadOnlyList<PlanDefinition> authored)
    {
        TenantSlug = tenantSlug;
        Authored   = authored;
    }

    /// <summary>سلاج المُستَأجِر، أَو <c>null</c> لِـ
    /// <see cref="Platform"/>. لِلوغ ومِفتاح الكاش لا لِلقَرار.</summary>
    public string? TenantSlug { get; }

    /// <summary>تَعريفات هذا المُستَأجِر المُعتَمَدَة وَحدَها.</summary>
    public IReadOnlyList<PlanDefinition> Authored { get; }

    /// <summary>
    /// <para>الباقات المُخَزَّنَة، مُلحَقاً بِها باقَةٌ لِكُلّ تَعريف
    /// مُعتَمَد ليسَ لَه سلاج مُقابِل فيها. <b>وبِلا تَعريفات يُرجَع
    /// نَفس المَرجِع</b> — التَكافُؤ الصِفريّ بِالهُوِيَّة لا
    /// بِالمُقارَنَة.</para>
    ///
    /// <para><b>ولا تُظَلَّل باقَةٌ مُخَزَّنَة</b>: السلاج المَوجود
    /// يَفوز لِلوَثيقَة الحَيَّة. الإضافَة فَوق فَقَط — نَفس قاعِدَة
    /// <c>TenantRoleSet.Materialize</c> حَرفاً.</para>
    /// </summary>
    public IReadOnlyList<Plan> Merge(IReadOnlyList<Plan> stored)
    {
        if (Authored.Count == 0) return stored;

        var known  = new HashSet<string>(stored.Select(p => p.Id), StringComparer.Ordinal);
        var merged = new List<Plan>(stored);

        foreach (var d in Authored)
            if (known.Add(d.Slug))
                merged.Add(d.ToPlan());

        return merged;
    }

    /// <summary>يَبني لَقطَة مِن وَثائِق مُستَأجِر. <b>يَقبَل المُعتَمَد
    /// وَحدَه</b>، ويُمَرِّر كُلّ نَصّ بِالمُحَمِّل ثُمَّ بِالمُصادِق —
    /// فَوَثيقَة فاسِدَة تُتَجاهَل بِسَطر تَحذير ولا تُفشِل الطَلَب.
    /// (البَوّابَة الحَقيقيَّة عِندَ الكِتابَة؛ هذه حِزام أَمان ثانٍ.)</summary>
    public static TenantPlanSet FromDocuments(
        string? tenantSlug, IEnumerable<TenantPlanDefinition> docs)
    {
        var accepted = new List<PlanDefinition>();

        foreach (var doc in docs
                     .Where(d => d.Status == TenantPlanStatuses.Approved)
                     .OrderBy(d => d.CreatedAt)
                     .ThenBy(d => d.Slug, StringComparer.Ordinal))
        {
            PlanDefinition parsed;
            try { parsed = PlanDefinitionLoader.ParseDefinition(doc.DefinitionJson); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[plans] تَعَذَّرَت قِراءَة تَعريف «{doc.Slug}» لِلمُستَأجِر " +
                    $"«{tenantSlug}» — تُجوهِلَ: {ex.Message}");
                continue;
            }

            var violations = PlanDefinitionValidator.ValidateTenantDefinition(parsed);
            if (violations.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[plans] تَعريف «{doc.Slug}» لِلمُستَأجِر «{tenantSlug}» " +
                    "لا يَجتاز المُصادَقَة — تُجوهِلَ: " +
                    string.Join(" | ", violations.Select(v => v.Code)));
                continue;
            }

            accepted.Add(parsed);
        }

        return accepted.Count == 0 && tenantSlug is null
            ? Platform
            : new TenantPlanSet(tenantSlug, accepted);
    }
}
