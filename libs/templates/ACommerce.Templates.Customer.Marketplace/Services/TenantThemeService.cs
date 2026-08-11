using System.Collections.Concurrent;
using ACommerce.Kit.Theme;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>المَنفَذ الوَحيد إلى ثيم مُستَأجِر وَقتَ التَشغيل</b> —
/// يَقرَأ وَثائِق <see cref="TenantThemeDefinition"/> ويَبني
/// <see cref="TenantThemeSet"/> فَوق الثيم الافتِراضيّ المَضمون. مَوضِع
/// الالتِقاط واحِد (‏<c>App.razor</c>) يَسأَلُه ولا يَعرِف مِن أَينَ
/// يُجيب.</para>
///
/// <para><b>العَزل بُنيَويّ لا اتِّفاقيّ</b>: كُلّ قِراءَة تُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c>، والوَثيقَة مُتَعَدِّدَة الإيجار
/// بِسِياسَة <c>AllDocumentsAreMultiTenanted</c> — فَـMarten يَضَع
/// <c>tenant_id</c> في الاستِعلام. لا سَطر شَرط مَكتوب بِاليَد يُمكِن
/// نِسيانُه.</para>
///
/// <para><b>والكاش بِمِفتاح المُستَأجِر حَصراً</b> — لا لَقطَة ساكِنَة
/// واحِدَة تُشارِكُها المَتاجِر. وهذا هُنا <b>أَشَدّ إلزاماً</b> مِنه في
/// الأَدوار: كاش مُشتَرَك يُسَرِّب لَون مَتجَر إلى صَفحَة مَتجَر آخَر —
/// أَي خَطَأً <b>يُرى</b>. يُبطَل عِندَ كُلّ كِتابَة تَمَسّ الثيم، وهو
/// ما يَجعَل بُرهان «فَوراً» مُمكِناً: الاعتِماد يُبطِل مِفتاح
/// المُستَأجِر، فَالطَلَب التالي يَقرَأ مِن جَديد — بِلا بِناء وبِلا
/// إعادَة تَشغيل.</para>
///
/// <para><b>وسُقوط آمِن عِندَ تَعَذُّر القِراءَة</b> (جَدوَل غَير مُنشَأ
/// بَعد، أَو خَلَل عابِر): يُرجَع <see cref="TenantThemeSet.Platform"/>
/// — أَي مَظهَر اليَوم حَرفاً — و<b>لا يُخَزَّن الفَشَل في الكاش</b> كَي
/// لا يَتَجَمَّد خَلَل عابِر حالَةً دائِمَة. الاختِيار مُعلَن: مَتجَر
/// يَفقِد لَونَه لِثَوانٍ أَهوَن مِن مَتجَر يَسقُط بِـ500.</para>
/// </summary>
public sealed class TenantThemeService
{
    private readonly IDocumentStore _store;

    /// <summary>مِفتاحُه سلاج المُستَأجِر — شَرط لا تَحسين.</summary>
    private readonly ConcurrentDictionary<string, TenantThemeSet> _cache =
        new(StringComparer.Ordinal);

    public TenantThemeService(IDocumentStore store) => _store = store;

    /// <summary>لَقطَة ثيم المُستَأجِر. سلاج فارِغ أَو <c>null</c>
    /// (سِياق بِلا مُستَأجِر) ← قاعِدَة المَنصَّة.</summary>
    public async Task<TenantThemeSet> ForAsync(string? tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return TenantThemeSet.Platform;
        if (_cache.TryGetValue(tenantSlug, out var hit)) return hit;

        var set = await ReadUncachedAsync(_store, tenantSlug, ct);

        // الفَشَل لا يُخَزَّن — يُعرَف بِأَنَّه سَقَطَ إلى لَقطَة المَنصَّة
        // بِلا سلاج.
        if (set.TenantSlug is not null) _cache[tenantSlug] = set;
        return set;
    }

    /// <summary>ما يُبَثّ في <c>&lt;head&gt;</c> — اختِصار المَوضِع
    /// الوَحيد الَّذي يَستَعمِلُه التَصيير.</summary>
    public async Task<EffectiveTheme> EffectiveAsync(
        string? tenantSlug, CancellationToken ct = default) =>
        (await ForAsync(tenantSlug, ct)).Theme;

    /// <summary>نَفس القِراءَة، بِلا كاش وبِلا نُسخَة مِن الجِسم —
    /// لِمَسارات تَملِك <c>IDocumentStore</c> ولا تَملِك هذه الخِدمَة.
    /// نَفس مُفتَرَق <c>TenantRoleService.ReadUncachedAsync</c>
    /// حَرفاً.</summary>
    public static async Task<TenantThemeSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return TenantThemeSet.Platform;

        try
        {
            await using var s = store.QuerySession(tenantSlug);
            var docs = await s.Query<TenantThemeDefinition>()
                .Where(d => d.Status == TenantThemeStatuses.Approved)
                .ToListAsync(ct);
            return TenantThemeSet.FromDocuments(tenantSlug, docs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[theme] تَعَذَّرَت قِراءَة ثيم «{tenantSlug}» — " +
                $"سُقوط إلى الثيم الافتِراضيّ: {ex.Message}");
            return TenantThemeSet.Platform;
        }
    }

    /// <summary>كُلّ ثيمات المُستَأجِر بِكُلّ حالاتِها — لِسَطح الإدارَة
    /// وَحدَه. المَبثوث هو المُعتَمَد فَقَط.</summary>
    public async Task<IReadOnlyList<TenantThemeDefinition>> ListAllAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(tenantSlug);
            var all = await s.Query<TenantThemeDefinition>().ToListAsync(ct);
            return all.OrderBy(d => d.CreatedAt).ThenBy(d => d.Slug, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[theme] تَعَذَّرَ سَرد ثيمات «{tenantSlug}»: {ex.Message}");
            return Array.Empty<TenantThemeDefinition>();
        }
    }

    /// <summary><b>يَكتُب ثيماً مُعَلَّقاً</b> — لا حالَة أُخرى مِن هُنا.
    /// المُصادَقَة تَتِمّ هُنا صَراحَةً قَبل الكِتابَة، فَلا يُخزَّن ثيم
    /// فاسِد أَصلاً (وهذا هو السالِب الحَيّ في البُرهان).</summary>
    public async Task<(bool Ok, string Message)> ProposeAsync(
        string tenantSlug, string themeSlug, string definitionJson,
        string by, CancellationToken ct = default)
    {
        ThemeDefinition parsed;
        try { parsed = ThemeDefinitionLoader.ParseDefinition(definitionJson); }
        catch (Exception ex) { return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message); }

        var violations = ThemeDefinitionValidator.ValidateTenantDefinition(parsed);
        if (violations.Count > 0)
            return (false, "لا يَجتاز المُصادَقَة: " +
                           string.Join(" | ", violations.Select(v => v.Code)));

        if (!string.Equals(parsed.Slug, themeSlug, StringComparison.Ordinal))
            return (false, $"الوَثيقَة «{themeSlug}» تُعلِن slug مُختَلِفاً: «{parsed.Slug}».");

        await using var s = _store.LightweightSession(tenantSlug);

        var existing = await s.LoadAsync<TenantThemeDefinition>(themeSlug, ct);
        if (existing is { Status: TenantThemeStatuses.Approved })
            return (false,
                $"الثيم «{themeSlug}» مُعتَمَد بِالفِعل في «{tenantSlug}» — " +
                "لا يُعاد تَعريفُه مِن الوَكيل.");

        s.Store(new TenantThemeDefinition
        {
            Id             = themeSlug,
            Slug           = themeSlug,
            DefinitionJson = definitionJson,
            Status         = TenantThemeStatuses.Pending,
            CreatedBy      = by,
            CreatedAt      = DateTime.UtcNow
        });
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return (true, $"سُجِّلَ ثيم «{themeSlug}» مُعَلَّقاً.");
    }

    /// <summary>قَرار بَشَريّ: اعتِماد أَو رَفض. <b>هُنا وَحدَه</b> يَصير
    /// ثيم مَبثوثاً، وهُنا وَحدَه يُبطَل الكاش. والاعتِماد يُعيد
    /// المُصادَقَة عَلى النَّصّ المُخَزَّن — لا يَثِق بِأَنَّها جَرَت
    /// عِندَ الكِتابَة.</summary>
    public async Task<(bool Ok, string Message)> DecideAsync(
        string tenantSlug, string themeSlug, string status,
        string by, CancellationToken ct = default)
    {
        // نَفس الحارِس بِعَينِه — ومِن نَفس التَعريف. كانَ الشَرط
        // مَنسوخاً بَينَ هذه الخِدمَة وخِدمَة الأَدوار؛ صارَ سُؤالاً
        // واحِداً لِمَوضِع واحِد.
        if (!ACommerce.Platform.Flows.ApprovalFlow.IsDecision(status))
            return (false, $"قَرار غَير مَعروف: «{status}».");

        await using var s = _store.LightweightSession(tenantSlug);
        var doc = await s.LoadAsync<TenantThemeDefinition>(themeSlug, ct);
        if (doc is null)
            return (false, $"لا ثيم بِاسم «{themeSlug}» في «{tenantSlug}».");

        if (status == TenantThemeStatuses.Approved)
        {
            ThemeDefinition parsed;
            try { parsed = ThemeDefinitionLoader.ParseDefinition(doc.DefinitionJson); }
            catch (Exception ex) { return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message); }

            var violations = ThemeDefinitionValidator.ValidateTenantDefinition(parsed);
            if (violations.Count > 0)
                return (false, "لا يَجتاز المُصادَقَة: " +
                               string.Join(" | ", violations.Select(v => v.Code)));

            if (!string.Equals(parsed.Slug, doc.Slug, StringComparison.Ordinal))
                return (false, $"الوَثيقَة «{doc.Slug}» تُعلِن slug مُختَلِفاً: «{parsed.Slug}».");
        }

        doc.Status    = status;
        doc.DecidedBy = by;
        doc.DecidedAt = DateTime.UtcNow;
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);

        return (true, status == TenantThemeStatuses.Approved
            ? $"اعتُمِدَ ثيم «{themeSlug}» — صارَ مَبثوثاً في «{tenantSlug}»."
            : $"رُفِضَ ثيم «{themeSlug}».");
    }

    /// <summary>
    /// <para><b>تَطبيق حُزمَة جاهِزَة</b> — وهو ما يُنَفِّذُه زِرّ واحِد
    /// في سَطح الإدارَة: اقتِراح ثُمَّ اعتِماد، بِنَفس
    /// <see cref="ProposeAsync"/> و<see cref="DecideAsync"/> لا بِمَسار
    /// كِتابَة ثالِث. المُصادَقَة تَجري مَرَّتَين كَما لِأَيّ ثيم آخَر،
    /// والإبطال يَقَع في <c>DecideAsync</c> — فَـ«فَوراً» هُنا هي
    /// «فَوراً» هُناك بِعَينِها.</para>
    ///
    /// <para><b>وما يَصِل مِن الطَلَب اسمُ حُزمَة لا نَصّ تَعريف</b>:
    /// الجِسم يُقرَأ مِن <see cref="ThemePresetCatalog"/> — أَي مِن
    /// المُستودَع. لا يَملِك صاحِبُ جَلسَةٍ مُخَوَّلَة أَن يَحقِن ثيماً
    /// بِصِياغَة طَلَب، ولَيسَ لِأَنّ المُصادِق سَيَرُدُّه، بَل لِأَنَّه
    /// لا يُقرَأ أَصلاً.</para>
    ///
    /// <para><b>وإعادَة التَطبيق مَقصودَة وسَهلَة</b>: وَثيقَة الحُزمَة
    /// إن كانَت مُعتَمَدَة تُعاد <b>مُعَلَّقَة</b> قَبل الاقتِراح، فَتَمُرّ
    /// بِحارِس «لا يُعاد تَعريف ثيم مُعتَمَد». وذلك الحارِس مَوضوع لِيَمنَع
    /// الوَكيل مِن تَغيير مَعنى ثيم قائِم مِن تَحت المُشرِف — لا لِيَمنَع
    /// مُشرِفاً يَضغَط «تَطبيق» مَرَّتَين. العَرض يُبَدَّل ذَهاباً
    /// وإياباً أَمامَ العَين، فَـ«طُبِّقَت مِن قَبل» لا تَصلُح جَواباً.</para>
    ///
    /// <para><b>ولا تُلمَس وَثائِق الحُزَم الأُخرى</b>: تَبقى مُعتَمَدَةً
    /// أَقدَمَ تاريخاً، والقاعِدَة المُعلَنَة في
    /// <see cref="TenantThemeSet"/> — آخِر قَرار يَغلِب — تَحسِم أَيُّها
    /// يُبَثّ. تَرتيب كامِل، فَالتَبديل ذَهاباً وإياباً حَتميّ.</para>
    /// </summary>
    public async Task<(bool Ok, string Message)> ApplyPresetAsync(
        string tenantSlug, string presetSlug, string by, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug))
            return (false, "لا مُستَأجِر.");

        var preset = ThemePresetCatalog.Find(presetSlug);
        if (preset is null)
            return (false, $"لا حُزمَة بِاسم «{presetSlug}» في كاتالوج المَنصَّة.");

        await ReopenIfApprovedAsync(tenantSlug, preset.Slug, by, ct);

        var (proposed, proposeMsg) =
            await ProposeAsync(tenantSlug, preset.Slug, preset.Json, by, ct);
        if (!proposed) return (false, proposeMsg);

        var (decided, decideMsg) = await DecideAsync(
            tenantSlug, preset.Slug, TenantThemeStatuses.Approved, by, ct);
        if (!decided) return (false, decideMsg);

        return (true, $"طُبِّقَت حُزمَة «{preset.Label.Ar}» عَلى «{tenantSlug}». {decideMsg}");
    }

    /// <summary>يُعيد وَثيقَة حُزمَة مُعتَمَدَة إلى «مُعَلَّق» — الخُطوَة
    /// الوَحيدَة الَّتي تُميِّز إعادَة التَطبيق عَن التَطبيق الأَوَّل.
    /// لا تُنشِئ ولا تَحذِف: غِياب الوَثيقَة لا شَيء يُفعَل بِه.</summary>
    private async Task ReopenIfApprovedAsync(
        string tenantSlug, string themeSlug, string by, CancellationToken ct)
    {
        try
        {
            await using var s = _store.LightweightSession(tenantSlug);
            var doc = await s.LoadAsync<TenantThemeDefinition>(themeSlug, ct);
            if (doc is not { Status: TenantThemeStatuses.Approved }) return;

            doc.Status    = TenantThemeStatuses.Pending;
            doc.DecidedBy = by;
            doc.DecidedAt = null;
            s.Store(doc);
            await s.SaveChangesAsync(ct);
            Invalidate(tenantSlug);
        }
        catch (Exception ex)
        {
            // لا يُفشِل التَطبيق: لَو تَعَذَّرَت هذه الخُطوَة فَـ
            // ProposeAsync سَيَرُدّ بِرِسالَتِه الواضِحَة، وهي أَنفَع مِن
            // استِثناء مِن دالَّة تَمهيدِيَّة.
            Console.Error.WriteLine(
                $"[theme] تَعَذَّرَ إعادَة فَتح «{themeSlug}» في «{tenantSlug}»: {ex.Message}");
        }
    }

    /// <summary>إبطال لَقطَة مُستَأجِر واحِد. مِفتاح واحِد لا مَسح
    /// شامِل.</summary>
    public void Invalidate(string tenantSlug) => _cache.TryRemove(tenantSlug, out _);
}
