namespace ACommerce.Kit.Theme;

using ACommerce.Platform.Flows;

/// <summary>
/// <para><b>حالات وَثيقَة ثيم المُستَأجِر</b> — نَفس الدَورَة القَصيرَة
/// المُغلَقَة الَّتي لِتَعريفات الأَدوار: <c>pending</c> عِندَ الكِتابَة،
/// ثُمَّ <c>approved</c> أَو <c>rejected</c> بِقَرار بَشَريّ. لا حالَة
/// رابِعَة.</para>
///
/// <para><b>والمَقروء واحِد فَقَط</b>: <c>approved</c>. المُعَلَّق
/// والمَرفوض لا يُبَثّان في أَيّ صَفحَة — وهذا بِعَينِه ما يَجعَل
/// السالِب الحَيّ (ثيم بِلَون فاسِد) قابِلاً لِلبُرهان: يُرفَض عِندَ
/// الكِتابَة، وحَتَّى لَو كُتِبَ فَلا قارِئ لَه.</para>
/// </summary>
/// <remarks>
/// <para><b>مُحال إلى نَفس التَعريف الواحِد</b>
/// (<see cref="ACommerce.Platform.Flows.ApprovalFlow"/>) الَّذي
/// يُحيل إلَيه <c>TenantRoleStatuses</c>. تَطابُق المَعجَمَين لَم
/// يَعُد مُصادَفَةً تُحرَس بِاليَقَظَة، بَل <b>مَوضِعاً واحِداً</b>
/// لا يُمكِن أَن يَنحَرِف نِصفُه عَن نِصفِه.</para>
/// </remarks>
public static class TenantThemeStatuses
{
    public const string Pending  = ApprovalFlow.Pending;
    public const string Approved = ApprovalFlow.Approved;
    public const string Rejected = ApprovalFlow.Rejected;

    public static IReadOnlyList<string> All => ApprovalFlow.All;

    public static bool Contains(string status) => ApprovalFlow.Contains(status);
}

/// <summary>
/// <para><b>وَثيقَة ثيم لِمُستَأجِر واحِد</b> — الطَبَقَة الَّتي تُضاف
/// <b>فَوق</b> الثيم الافتِراضيّ المَضمون، لا الَّتي تَحُلّ مَحَلَّه. هي
/// وَثيقَة Marten <b>بِإيجار مُقتَرِن</b> كَبَقِيَّة وَثائِق المُستَأجِر،
/// فَعَزلُها بُنيَويّ: <c>tenant_id</c> في كُلّ صَفّ، والجَلسَة تُفتَح
/// بِسلاج المُستَأجِر — فَلا يُقرَأ ثيم مُستَأجِر مِن سِياق آخَر ولَو
/// أَخطَأَ الاستِعلام.</para>
///
/// <para><b>ولِماذا يُخزَّن النَّصّ لا الكائِن</b>: <c>DefinitionJson</c>
/// هو ما كُتِبَ حَرفِيّاً، وتَخزينُه نَصّاً يَجعَل مَسار القِراءَة
/// <b>واحِداً</b> مَع مَسار المِلَفّ المَضمون —
/// <see cref="ThemeDefinitionLoader.ParseDefinition"/> نَفسُها بِنَفس
/// الخِيارات. ولَو خُزِّنَ الكائِن مُفَكَّكاً لَصارَ لِلتَعريف شَكلانِ
/// يَنحَرِفان.</para>
/// </summary>
public sealed class TenantThemeDefinition
{
    /// <summary>هُوِيَّة الوَثيقَة = <see cref="Slug"/>. الفَرادَة داخِل
    /// المُستَأجِر مَضمونَة بِالإيجار المُقتَرِن.</summary>
    public string Id { get; set; } = "";

    public string Slug { get; set; } = "";

    /// <summary>نَصّ تَعريف الثيم كَما كُتِبَ.</summary>
    public string DefinitionJson { get; set; } = "";

    /// <summary>مِن <see cref="TenantThemeStatuses"/> حَصراً.</summary>
    public string Status { get; set; } = TenantThemeStatuses.Pending;

    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>
/// <para><b>طَبَقَة قَرار المَظهَر لِمُستَأجِر واحِد</b> — لَقطَة ساكِنَة
/// تُبنى مِن مَصدَرَين: الثيم الافتِراضيّ المَضمون
/// (<see cref="ThemeCatalog.Default"/>) وثيم المُستَأجِر
/// <b>المُعتَمَد</b> وَحدَه.</para>
///
/// <para><b>التَكافُؤ الصِفريّ هو العَقد</b>: مُستَأجِر بِلا وَثيقَة
/// واحِدَة يُعطي هذه الطَبَقَة <b>نَفس المَرجِع</b> الَّذي يُعطيه
/// الكاتالوج — لا نُسخَة مُتَساوِيَة بَل الكائِن ذاتُه، فَنَصّ CSS
/// المَبثوث هو ذات السِلسِلَة. مُبرهَن في
/// <c>ThemeZeroEquivalenceTests</c>.</para>
///
/// <para><b>ولِماذا ثيم واحِد مُعتَمَد لا قائِمَة</b>: الصَفحَة تَبُثّ
/// كُتلَة <c>:root</c> واحِدَة، فَثيمانِ مُعتَمَدان يَعنيان سُؤال
/// «أَيُّهُما؟» بِلا جَواب في البَيانات. القاعِدَة مُعلَنَة: <b>آخِر
/// مُعتَمَد بِتاريخ القَرار يَغلِب</b>، وعِندَ التَساوي يُرَتَّب
/// بِالسلاج — تَرتيب كامِل، فَلا يَتَغَيَّر المَظهَر بَين طَلَبَين
/// بِتَرتيب صُفوف قاعِدَة البَيانات. (ومِنَصَّة الاعتِماد تَملِك أَن
/// تَرفُض الثاني — وهذا سُؤال مَوجَة المُبَدِّل، لا هذه.)</para>
/// </summary>
public sealed class TenantThemeSet
{
    /// <summary>اللَقطَة بِلا أَيّ وَثيقَة — <b>قاعِدَة المَنصَّة</b>.</summary>
    public static readonly TenantThemeSet Platform = new(null, null);

    private TenantThemeSet(string? tenantSlug, ThemeDefinition? authored)
    {
        TenantSlug     = tenantSlug;
        TenantAuthored = authored;
        // بِلا تَأليف: نَفس المَرجِع لا نُسخَة — التَكافُؤ الصِفريّ
        // بِالهُوِيَّة لا بِالمُقارَنَة.
        Theme = authored is null
            ? ThemeCatalog.Default
            : EffectiveTheme.Compose(ThemeCatalog.Default, authored);
    }

    /// <summary>سلاج المُستَأجِر، أَو <c>null</c> لِـ
    /// <see cref="Platform"/>. لِلوغ ومِفتاح الكاش لا لِلقَرار.</summary>
    public string? TenantSlug { get; }

    /// <summary>ثيم هذا المُستَأجِر المُعتَمَد، أَو <c>null</c>.</summary>
    public ThemeDefinition? TenantAuthored { get; }

    /// <summary><b>ما يُبَثّ</b> — الافتِراضيّ، أَو الافتِراضيّ وقَد
    /// غُلِّبَت عَلَيه رُموز المُستَأجِر.</summary>
    public EffectiveTheme Theme { get; }

    /// <summary>
    /// <para>يَبني لَقطَة مِن وَثائِق مُستَأجِر. <b>يَقبَل المُعتَمَد
    /// وَحدَه</b>، ويُمَرِّر النَّصّ مِن
    /// <see cref="ThemeDefinitionLoader.ParseDefinition"/> ثُمَّ
    /// <see cref="ThemeDefinitionValidator.ValidateTenantDefinition"/> —
    /// فَوَثيقَة فاسِدَة أَو ظالَّة تُتَجاهَل بِسَطر تَحذير ولا تُفشِل
    /// الطَلَب. (البَوّابَة الحَقيقيَّة عِندَ الكِتابَة؛ هذه حِزام أَمان
    /// ثانٍ لِوَثيقَة كُتِبَت بِيَد أَو نَجَت مِن تَرحيل.)</para>
    /// </summary>
    public static TenantThemeSet FromDocuments(
        string? tenantSlug, IEnumerable<TenantThemeDefinition> docs)
    {
        ThemeDefinition? winner = null;

        foreach (var doc in docs
                     .Where(d => d.Status == TenantThemeStatuses.Approved)
                     .OrderByDescending(d => d.DecidedAt ?? d.CreatedAt)
                     .ThenBy(d => d.Slug, StringComparer.Ordinal))
        {
            ThemeDefinition parsed;
            try
            {
                parsed = ThemeDefinitionLoader.ParseDefinition(doc.DefinitionJson);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[theme] تَعَذَّرَت قِراءَة ثيم «{doc.Slug}» لِلمُستَأجِر " +
                    $"«{tenantSlug}» — تُجوهِلَ: {ex.Message}");
                continue;
            }

            var violations = ThemeDefinitionValidator.ValidateTenantDefinition(parsed);
            if (violations.Count > 0)
            {
                Console.Error.WriteLine(
                    $"[theme] ثيم «{doc.Slug}» لِلمُستَأجِر «{tenantSlug}» " +
                    "لا يَجتاز المُصادَقَة — تُجوهِلَ: " +
                    string.Join(" | ", violations.Select(v => v.Code)));
                continue;
            }

            winner = parsed;
            break;
        }

        return winner is null && tenantSlug is null
            ? Platform
            : new TenantThemeSet(tenantSlug, winner);
    }
}
