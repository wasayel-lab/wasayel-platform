namespace ACommerce.Kit.Roles;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// <para><b>حالات وَثيقَة تَعريف دَور المُستَأجِر</b> — دَورَة قَصيرَة
/// ومُغلَقَة: <c>pending</c> عِندَ الكِتابَة، ثُمَّ <c>approved</c> أَو
/// <c>rejected</c> بِقَرار بَشَريّ. لا حالَة رابِعَة، ولا عَودَة مِن
/// <c>approved</c> إلى <c>pending</c> — المُراجَعَة تُعيد الكِتابَة
/// بِوَثيقَة جَديدَة.</para>
///
/// <para><b>والمَقروء واحِد فَقَط</b>: <c>approved</c>. المُعَلَّق
/// والمَرفوض <b>لا يَبلُغان أَيّ سَطح لاعِب</b> — لا بَوّابَة ولا
/// تَسجيل ولا تَصيير — وهذا هو بِعَينِه ما يَجعَل السالِب الحَيّ
/// (تَعريف بِصَلاحِيَّة خارِج المَعجَم) قابِلاً لِلبُرهان: يُرفَض عِندَ
/// الكِتابَة، وحَتَّى لَو كُتِبَ فَلا قارِئ لَه.</para>
/// </summary>
public static class TenantRoleStatuses
{
    public const string Pending  = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = new[] { Pending, Approved, Rejected };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);
    public static bool Contains(string status) => Set.Contains(status);
}

/// <summary>
/// <para><b>وَثيقَة تَعريف دَور لِمُستَأجِر واحِد</b> — الطَبَقَة الَّتي
/// تُضاف <b>فَوق</b> الكاتالوج المَضمون، لا الَّتي تَحُلّ مَحَلَّه. هي
/// وَثيقَة Marten <b>بِإيجار مُقتَرِن</b> (conjoined) كَبَقِيَّة وَثائِق
/// المُستَأجِر، فَعَزلُها بُنيَويّ: <c>tenant_id</c> في كُلّ صَفّ،
/// والجَلسَة تُفتَح بِسلاج المُستَأجِر، فَلا يُقرَأ تَعريف مُستَأجِر مِن
/// سِياق مُستَأجِر آخَر ولَو أَخطَأَ الاستِعلام.</para>
///
/// <para><b>ولِماذا يُخزَّن النَّصّ لا الكائِن</b>: <c>DefinitionJson</c>
/// هو ما كَتَبَه الوَكيل حَرفِيّاً. تَخزينُه نَصّاً يَجعَل مَسار
/// القِراءَة <b>واحِداً</b> مَع مَسار المِلَفّات المَضمونَة —
/// <see cref="RoleDefinitionLoader.ParseDefinition"/> نَفسُها بِنَفس
/// خِيارات القِراءَة، فَما يَصِحّ في مِلَفّ يَصِحّ في وَثيقَة
/// <b>بِالبِناء لا بِالمُصادَفَة</b> — وهو المَقعَد الَّذي وُضِعَ في
/// المَوجَة السابِقَة لِهذه بِالضَبط. ولَو خُزِّنَ الكائِن مُفَكَّكاً
/// لَصارَ لِلتَعريف شَكلانِ يَنحَرِفان.</para>
/// </summary>
public sealed class TenantRoleDefinition
{
    /// <summary>هُوِيَّة الوَثيقَة = <see cref="Slug"/>. الفَرادَة
    /// داخِل المُستَأجِر مَضمونَة بِالإيجار المُقتَرِن (نَفس الـ Id في
    /// مُستَأجِرَين وَثيقَتان مُستَقِلَّتان).</summary>
    public string Id { get; set; } = "";

    public string Slug { get; set; } = "";

    /// <summary>نَصّ تَعريف الدَور كَما كُتِبَ — يُقرَأ بِـ
    /// <see cref="RoleDefinitionLoader.ParseDefinition"/>.</summary>
    public string DefinitionJson { get; set; } = "";

    /// <summary>مِن <see cref="TenantRoleStatuses"/> حَصراً.</summary>
    public string Status { get; set; } = TenantRoleStatuses.Pending;

    /// <summary>مَن كَتَبَ — اسم الوَكيل أَو المُستَخدِم. لِلتَدقيق لا
    /// لِلقَرار.</summary>
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>مَن قَرَّرَ ومَتى — يُملَآن عِندَ الاعتِماد أَو الرَّفض.</summary>
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>
/// <para><b>طَبَقَة قَرار الأَدوار لِمُستَأجِر واحِد</b> — لَقطَة ساكِنَة
/// غَير قابِلَة لِلتَغيير تُبنى مِن مَصدَرَين: كاتالوج المَنصَّة المَضمون
/// (<see cref="RoleCatalog"/>) وتَعريفات المُستَأجِر <b>المُعتَمَدَة</b>
/// وَحدَها. واجِهَتُها <b>مِرآة</b> لِواجِهَة <see cref="RoleCatalog"/>
/// عَمداً — <see cref="Definitions"/>, <see cref="All"/>,
/// <see cref="Find"/>, <see cref="FindDefinition"/> — لِيَكون التَّبديل
/// في مَواضِع الالتِقاط سَطراً لا إعادَة كِتابَة.</para>
///
/// <para><b>التَّكافُؤ الصِفريّ هو العَقد</b>: مُستَأجِر بِلا وَثيقَة
/// واحِدَة يُعطي هذه الطَبَقَة <b>حَرفِيّاً</b> ما يُعطيه الكاتالوج
/// الساكِن — نَفس العَشَرَة بِتَرتيبِها وكُلّ حَقل فيها، ونَفس
/// التَّركيب لِكُلّ سلاج، ونَفس نَمَط الصَفقَة. مُبرهَن في
/// <c>TenantRoleZeroEquivalenceTests</c> الَّذي كُتِبَ واخضَرَّ
/// <b>قَبل</b> أَيّ تَبديل ولا يُمَسّ بَعدَه.</para>
///
/// <para><b>وقاعِدَة عَدَم الظِلّ</b>: سلاج مُستَأجِر يُصادِم سلاج
/// كاتالوج <b>يُرفَض عِندَ المُصادَقَة</b>
/// (<see cref="RoleDefinitionValidator.ValidateTenantDefinition"/>،
/// رَمز <c>slug_shadows_platform_catalog</c>) — فَلا حاجَة إلى قاعِدَة
/// أَولَوِيَّة هُنا، ولا يُمكِن لِمُستَأجِر أَن يُغَيِّر مَعنى
/// <c>vendor</c> عَلى المَنصَّة. الإضافَة فَوق فَقَط.</para>
/// </summary>
public sealed class TenantRoleSet
{
    /// <summary>اللَقطَة بِلا أَيّ وَثيقَة مُستَأجِر — <b>قاعِدَة
    /// المَنصَّة</b>. وهي جَواب كُلّ مُستَأجِر لَم يُؤَلِّف دَوراً،
    /// وجَواب كُلّ سِياق بِلا مُستَأجِر (لَوحَة المَنصَّة، الاختِبارات،
    /// أَدَوات البَذر).</summary>
    public static readonly TenantRoleSet Platform = new(null, Array.Empty<RoleDefinition>());

    private readonly Dictionary<string, RoleDefinition> _bySlug;

    private TenantRoleSet(string? tenantSlug, IReadOnlyList<RoleDefinition> tenantAuthored)
    {
        TenantSlug     = tenantSlug;
        TenantAuthored = tenantAuthored;

        Definitions = tenantAuthored.Count == 0
            // نَفس المَرجِع لا نُسخَة — التَّكافُؤ الصِفريّ بِالهُوِيَّة
            // لا بِالمُقارَنَة.
            ? RoleCatalog.Definitions
            : RoleCatalog.Definitions.Concat(tenantAuthored).ToArray();

        All = tenantAuthored.Count == 0
            ? RoleCatalog.All
            : Definitions.Select(RoleCatalog.ToTemplate).ToArray();

        _bySlug = new Dictionary<string, RoleDefinition>(StringComparer.Ordinal);
        foreach (var d in Definitions) _bySlug[d.Slug] = d;
    }

    /// <summary>سلاج المُستَأجِر، أَو <c>null</c> لِـ
    /// <see cref="Platform"/>. لِلوغ ولِمِفتاح الكاش لا لِلقَرار.</summary>
    public string? TenantSlug { get; }

    /// <summary>تَعريفات هذا المُستَأجِر المُعتَمَدَة وَحدَها — بِلا
    /// الكاتالوج. هذا ما يُضاف إلى <c>Tenant.Roles</c> في
    /// <see cref="Materialize"/>، ولِذلك لا يُغرِق مُستَأجِراً بِعَشَرَة
    /// أَدوار لَم يَختَرها.</summary>
    public IReadOnlyList<RoleDefinition> TenantAuthored { get; }

    /// <summary>الكاتالوج ثُمَّ تَعريفات المُستَأجِر — بِهذا التَّرتيب.
    /// مِرآة <see cref="RoleCatalog.Definitions"/>.</summary>
    public IReadOnlyList<RoleDefinition> Definitions { get; }

    /// <summary>مِرآة <see cref="RoleCatalog.All"/>.</summary>
    public IReadOnlyList<RoleTemplate> All { get; }

    public RoleTemplate? Find(string slug) =>
        All.FirstOrDefault(r => r.Slug == slug);

    public RoleDefinition? FindDefinition(string slug) =>
        _bySlug.TryGetValue(slug, out var d) ? d : null;

    /// <summary>تَركيب الواجِهَة لِسلاج — مِرآة
    /// <see cref="RoleCompositionResolver.Resolve"/> بِنَفس الحالات
    /// الحَدِّيَّة حَرفِيّاً: <c>null</c> والفارِغ والمَجهول ←
    /// <see cref="RoleCompositionResolver.Fallback"/>.</summary>
    public RoleComposition ResolveComposition(string? catalogSlug) =>
        string.IsNullOrEmpty(catalogSlug)
            ? RoleCompositionResolver.Fallback
            : FindDefinition(catalogSlug)?.Composition ?? RoleCompositionResolver.Fallback;

    /// <summary>نَمَط الصَفقَة المُشتَقّ — مِرآة
    /// <see cref="RoleDealPatternAffinity.Resolve"/> بِنَفس تَرتيب
    /// الغَلَبَة، وبِبَحث يَرى أَدوار هذا المُستَأجِر.</summary>
    public string DealPattern(IEnumerable<string?> catalogSlugs) =>
        RoleDealPatternAffinity.Resolve(catalogSlugs, FindDefinition);

    /// <summary>
    /// <para><b>تَجسيد أَدوار المُستَأجِر المُؤَلَّفَة فَوق أَدوارِه
    /// المُخزَّنَة</b> — يُرجِع <c>Tenant.Roles</c> كَما هي، مُلحَقاً بِها
    /// دَور لِكُلّ تَعريف مُعتَمَد ليسَ لَه سلاج مُقابِل فيها. هذا هو
    /// المَوضِع الوَحيد الَّذي يَجعَل تَعريفاً وَثيقَةً يُصبِح
    /// <c>Role</c> يَراه اللاعِب.</para>
    ///
    /// <para><b>وبِلا وَثائِق يُرجِع نَفس المَرجِع</b> لا نُسخَة —
    /// فَمُستَأجِر بِلا تَأليف لا يَمُرّ بِسَطر مَنطِق واحِد
    /// إضافيّ.</para>
    /// </summary>
    public IReadOnlyList<Role> Materialize(IReadOnlyList<Role> tenantRoles)
    {
        if (TenantAuthored.Count == 0) return tenantRoles;

        var known = new HashSet<string>(tenantRoles.Select(r => r.Slug), StringComparer.Ordinal);
        var merged = new List<Role>(tenantRoles);
        var order  = tenantRoles.Count == 0 ? 0 : tenantRoles.Max(r => r.SortOrder) + 1;

        foreach (var d in TenantAuthored)
        {
            if (!known.Add(d.Slug)) continue;
            merged.Add(RoleCatalog.InstantiateRole(RoleCatalog.ToTemplate(d), order++));
        }
        return merged;
    }

    /// <summary>
    /// <para>يَبني لَقطَة مِن وَثائِق مُستَأجِر. <b>يَقبَل
    /// المُعتَمَد وَحدَه</b>، ويُمَرِّر كُلّ نَصّ مِن
    /// <see cref="RoleDefinitionLoader.ParseDefinition"/> ثُمَّ
    /// <see cref="RoleDefinitionValidator.ValidateTenantDefinition"/> —
    /// فَوَثيقَة فاسِدَة أَو ظالَّة تُتَجاهَل بِسَطر تَحذير ولا تُفشِل
    /// الطَلَب. (البَوّابَة الحَقيقيَّة عِندَ الكِتابَة؛ هذه حِزام
    /// أَمان ثانٍ لِوَثيقَة كُتِبَت بِيَد أَو نَجَت مِن تَرحيل.)</para>
    /// </summary>
    public static TenantRoleSet FromDocuments(
        string? tenantSlug, IEnumerable<TenantRoleDefinition> docs)
    {
        var accepted = new List<RoleDefinition>();

        foreach (var doc in docs
                     .Where(d => d.Status == TenantRoleStatuses.Approved)
                     .OrderBy(d => d.CreatedAt)
                     .ThenBy(d => d.Slug, StringComparer.Ordinal))
        {
            RoleDefinition parsed;
            try
            {
                parsed = RoleDefinitionLoader.ParseDefinition(doc.DefinitionJson);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[roles] تَعَذَّرَت قِراءَة تَعريف «{doc.Slug}» لِلمُستَأجِر " +
                    $"«{tenantSlug}» — تُجوهِلَ: {ex.Message}");
                continue;
            }

            var violations = RoleDefinitionValidator.ValidateTenantDefinition(parsed);
            if (violations.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[roles] تَعريف «{doc.Slug}» لِلمُستَأجِر «{tenantSlug}» " +
                    "لا يَجتاز المُصادَقَة — تُجوهِلَ: " +
                    string.Join(" | ", violations.Select(v => v.Code)));
                continue;
            }

            accepted.Add(parsed);
        }

        return accepted.Count == 0 && tenantSlug is null
            ? Platform
            : new TenantRoleSet(tenantSlug, accepted);
    }
}
