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
/// <para><b>ومَكانيكاهُ صارَت مُشتَرَكَة</b>
/// (<see cref="TenantDefinitionService{TDoc,TSet}"/>) — نَفس مُشارَكَة
/// خِدمَة الأَدوار حَرفاً. وما بَقِيَ هُنا <b>مُفرَدات المَظهَر</b>،
/// ومِنها فَرقٌ سُلوكيّ واحِد مُعلَن: <b>المَظهَر يُصادِق قَبل
/// التَخزين</b> (‏<see cref="ValidateBeforeStore"/>) والأَدوار لا
/// تَفعَل. كانَ ذلك الفَرق مَخفِيّاً في تَشابُهِ جِسمَين؛ صارَ
/// مُعلَناً في تَوقيع.</para>
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
public sealed class TenantThemeService : TenantDefinitionService<TenantThemeDefinition, TenantThemeSet>
{
    public TenantThemeService(IDocumentStore store) : base(store) { }

    // ─── مُفرَدات المَظهَر ────────────────────────────────────────────

    protected override TenantThemeSet PlatformSet => TenantThemeSet.Platform;

    protected override string? SlugOf(TenantThemeSet set) => set.TenantSlug;

    protected override TenantThemeSet Build(string tenantSlug, IReadOnlyList<TenantThemeDefinition> docs)
        => TenantThemeSet.FromDocuments(tenantSlug, docs);

    protected override string LogTag => "theme";

    protected override Task<TenantThemeSet> ReadUncachedCoreAsync(string tenantSlug, CancellationToken ct)
        => ReadUncachedAsync(Store, tenantSlug, ct);

    protected override string ListFailureAr(string tenantSlug, string error)
        => $"[theme] تَعَذَّرَ سَرد ثيمات «{tenantSlug}»: {error}";

    /// <summary><b>المُصادَقَة تَتِمّ هُنا صَراحَةً قَبل الكِتابَة</b>،
    /// فَلا يُخزَّن ثيم فاسِد أَصلاً (وهذا هو السالِب الحَيّ في
    /// البُرهان).</summary>
    protected override (bool Ok, string Message) ValidateBeforeStore(string definitionJson, string slug)
    {
        ThemeDefinition parsed;
        try { parsed = ThemeDefinitionLoader.ParseDefinition(definitionJson); }
        catch (Exception ex) { return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message); }

        var violations = ThemeDefinitionValidator.ValidateTenantDefinition(parsed);
        if (violations.Count > 0)
            return (false, "لا يَجتاز المُصادَقَة: " +
                           string.Join(" | ", violations.Select(v => v.Code)));

        if (!string.Equals(parsed.Slug, slug, StringComparison.Ordinal))
            return (false, $"الوَثيقَة «{slug}» تُعلِن slug مُختَلِفاً: «{parsed.Slug}».");

        return (true, "");
    }

    /// <summary>والاعتِماد يُعيد المُصادَقَة عَلى النَّصّ المُخَزَّن — لا
    /// يَثِق بِأَنَّها جَرَت عِندَ الكِتابَة.</summary>
    protected override (bool Ok, string Message) ValidateBeforeApprove(TenantThemeDefinition doc)
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

        return (true, "");
    }

    protected override string AlreadyApprovedAr(string slug, string tenantSlug)
        => $"الثيم «{slug}» مُعتَمَد بِالفِعل في «{tenantSlug}» — " +
           "لا يُعاد تَعريفُه مِن الوَكيل.";

    protected override string ProposedAr(string slug)
        => $"سُجِّلَ ثيم «{slug}» مُعَلَّقاً.";

    protected override string NotFoundAr(string slug, string tenantSlug)
        => $"لا ثيم بِاسم «{slug}» في «{tenantSlug}».";

    protected override string DecidedAr(string slug, string tenantSlug, bool approved)
        => approved
            ? $"اعتُمِدَ ثيم «{slug}» — صارَ مَبثوثاً في «{tenantSlug}»."
            : $"رُفِضَ ثيم «{slug}».";

    // ─── ما يَخُصّ المَظهَر وَحدَه ────────────────────────────────────

    /// <summary>نَفس القِراءَة، بِلا كاش وبِلا نُسخَة مِن الجِسم —
    /// لِمَسارات تَملِك <c>IDocumentStore</c> ولا تَملِك هذه الخِدمَة.
    /// نَفس مُفتَرَق <c>TenantRoleService.ReadUncachedAsync</c>
    /// حَرفاً.</summary>
    public static Task<TenantThemeSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
        => QueryApprovedAsync(
            store, tenantSlug,
            TenantThemeSet.FromDocuments,
            TenantThemeSet.Platform,
            ex => $"[theme] تَعَذَّرَت قِراءَة ثيم «{tenantSlug}» — " +
                  $"سُقوط إلى الثيم الافتِراضيّ: {ex.Message}",
            ct);

    /// <summary>ما يُبَثّ في <c>&lt;head&gt;</c> — اختِصار المَوضِع
    /// الوَحيد الَّذي يَستَعمِلُه التَصيير.</summary>
    public async Task<EffectiveTheme> EffectiveAsync(
        string? tenantSlug, CancellationToken ct = default) =>
        (await ForAsync(tenantSlug, ct)).Theme;

    /// <summary>
    /// <para><b>تَطبيق حُزمَة جاهِزَة</b> — وهو ما يُنَفِّذُه زِرّ واحِد
    /// في سَطح الإدارَة: اقتِراح ثُمَّ اعتِماد، بِنَفس
    /// <c>ProposeAsync</c> و<c>DecideAsync</c> لا بِمَسار كِتابَة ثالِث.
    /// المُصادَقَة تَجري مَرَّتَين كَما لِأَيّ ثيم آخَر، والإبطال يَقَع
    /// في <c>DecideAsync</c> — فَـ«فَوراً» هُنا هي «فَوراً» هُناك
    /// بِعَينِها.</para>
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
            await using var s = Store.LightweightSession(tenantSlug);
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
}
