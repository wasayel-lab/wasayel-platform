using ACommerce.Kit.Subscriptions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>المَنفَذ إلى باقات مُستَأجِر وَقتَ التَشغيل</b> — والمُستَهلِك
/// الثالِث لِلقالِب المُشتَرَك، وهو الَّذي استَوفى شَرطَ استِخراجِه
/// (القاعِدَة ١: ثَلاثَة مُستَهلِكين قَبل التَجريد).</para>
///
/// <para><b>والتَكافُؤ الصِفريّ هو العَقد</b>: مُستَأجِر بِلا وَثيقَة
/// تَعريف باقَة واحِدَة — أَي <b>كُلّ مُستَأجِر قائِم اليَوم</b> —
/// يَحصُل عَلى <see cref="TenantPlanSet.Platform"/>، و<c>Merge</c>
/// تُرجِع لَه <b>نَفس مَرجِع</b> قائِمَة الباقات المُخَزَّنَة. أَي أَنّ
/// صَفحَة الباقات لا تَمُرّ بِسَطر مَنطِق واحِد إضافيّ، ولا تَتَغَيَّر
/// بايتاً.</para>
///
/// <para><b>ولِماذا يُضاف فَوق الوَثائِق ولا يَحُلّ مَحَلَّها</b>:
/// <c>Plan</c> وَثيقَة حَيَّة يَكتُبُها البَذر ويَقرَؤُها مَسار
/// الاشتِراك. تَحويلُها إلى تَعريفات دَفعَةً واحِدَة تَغييرُ سُلوك عَلى
/// مَسار يَبلُغُه المُستَخدِم — وهو ما تَمنَعُه القاعِدَة ٣ في مَوجَة
/// شَرطُها أَن تُضيفَ لا أَن تُبَدِّل. الطَبَقَة تُضاف اليَوم،
/// والتَوحيد قَرارٌ لَه مَوجَتُه.</para>
/// </summary>
public sealed class TenantPlanService
    : TenantDefinitionService<TenantPlanDefinition, TenantPlanSet>
{
    public TenantPlanService(IDocumentStore store) : base(store) { }

    // ─── مُفرَدات الباقات ────────────────────────────────────────────

    protected override TenantPlanSet PlatformSet => TenantPlanSet.Platform;

    protected override string? SlugOf(TenantPlanSet set) => set.TenantSlug;

    protected override TenantPlanSet Build(string tenantSlug, IReadOnlyList<TenantPlanDefinition> docs)
        => TenantPlanSet.FromDocuments(tenantSlug, docs);

    protected override string LogTag => "plans";

    protected override Task<TenantPlanSet> ReadUncachedCoreAsync(string tenantSlug, CancellationToken ct)
        => ReadUncachedAsync(Store, tenantSlug, ct);

    protected override string ListFailureAr(string tenantSlug, string error)
        => $"[plans] تَعَذَّرَ سَرد باقات «{tenantSlug}»: {error}";

    /// <summary><b>الباقَة تُصادَق قَبل التَخزين</b> — كَالمَظهَر لا
    /// كَالأَدوار. والسَبَب أَنّ الباقَة <b>مال وحِصَّة</b>: حِصَّةٌ
    /// سالِبَة تُخَزَّن اليَوم تُعطي رَصيداً سالِباً يَوم يُعتَمَد،
    /// ولا يَشتَكي مِنها شَيء بَينَهُما.</summary>
    protected override (bool Ok, string Message) ValidateBeforeStore(string definitionJson, string slug)
    {
        PlanDefinition parsed;
        try { parsed = PlanDefinitionLoader.ParseDefinition(definitionJson); }
        catch (Exception ex) { return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message); }

        var violations = PlanDefinitionValidator.ValidateTenantDefinition(parsed);
        if (violations.Count > 0)
            return (false, "لا يَجتاز المُصادَقَة: " +
                           string.Join(" | ", violations.Select(v => v.Code)));

        if (!string.Equals(parsed.Slug, slug, StringComparison.Ordinal))
            return (false, $"الوَثيقَة «{slug}» تُعلِن slug مُختَلِفاً: «{parsed.Slug}».");

        return (true, "");
    }

    /// <summary>والاعتِماد يُعيد المُصادَقَة عَلى النَصّ المُخَزَّن.</summary>
    protected override (bool Ok, string Message) ValidateBeforeApprove(TenantPlanDefinition doc)
        => ValidateBeforeStore(doc.DefinitionJson, doc.Slug);

    protected override string AlreadyApprovedAr(string slug, string tenantSlug)
        => $"الباقَة «{slug}» مُعتَمَدَة بِالفِعل في «{tenantSlug}» — " +
           "لا يُعاد تَعريفُها مِن الوَكيل.";

    protected override string ProposedAr(string slug)
        => $"سُجِّلَت باقَة «{slug}» مُعَلَّقَةً.";

    protected override string NotFoundAr(string slug, string tenantSlug)
        => $"لا باقَة بِاسم «{slug}» في «{tenantSlug}».";

    protected override string DecidedAr(string slug, string tenantSlug, bool approved)
        => approved
            ? $"اعتُمِدَت الباقَة «{slug}» — صارَت مَعروضَةً في «{tenantSlug}»."
            : $"رُفِضَت الباقَة «{slug}».";

    // ─── ما يَخُصّ الباقات وَحدَها ───────────────────────────────────

    /// <summary>
    /// <para><b>يُؤَلِّف صاحِبُ المَتجَرِ باقَةً لِمُستَخدِمي
    /// مَتجَرِه — أَوَّلُ كاتِبٍ لِهذِه الوَثيقَةِ في
    /// المُستَودَع.</b></para>
    ///
    /// <para><b>ولِماذا يَقتَرِح ويَعتَمِد مَعاً، ويُقالُ لِماذا</b>
    /// (‏ADR-021): مِعيارُ بَقاءِ الاعتِمادِ بَشَرِيّاً في هذا
    /// المُستَودَعِ هُوَ <b>نِطاقُ الأَثَر</b> لا نَوعُ الوَثيقَة —
    /// وثِقُلُه مَقيسٌ في جارَتَيه:
    /// <list type="bullet">
    ///   <item><b>الثيم</b> يُبَثُّ في <c>&lt;head&gt;</c> لِكُلِّ
    ///   زائِرٍ، فَاعتِمادُه قَرارُ مَنَصَّة.</item>
    ///   <item><b>الدَورُ المُؤَلَّف</b> يُضيفُ صَلاحِيّاتٍ
    ///   <b>خارِجَ كاتالوجِ المَنَصَّة</b>، فَاعتِمادُه قَرارُ
    ///   مَنَصَّة.</item>
    ///   <item><b>وباقَةُ المَتجَر</b> لا تَفعَل أَيّاً مِنهُما:
    ///   تُعرَضُ على <c>/{slug}/plans</c> وَحدَها — نَفسُ نِطاقِ
    ///   فِئاتِ المَتجَرِ ومُدُنِه وأَدوارِه، وكُلُّها يَكتُبُها
    ///   صاحِبُه اليَومَ مِن <c>/studio/apps/{slug}/*</c>
    ///   <b>بِلا اعتِمادٍ إطلاقاً</b>.</item>
    /// </list></para>
    ///
    /// <para><b>ولا مالَ يَتَحَرَّك</b>: الباقَةُ بِسِعرٍ
    /// <b>لا تُمنَحُ ذاتِيّاً</b> (<c>PlanPurchasePolicy</c> · ‏ADR-003)
    /// ولا تُعرَضُ أَصلاً لِزائِرِ مَتجَرٍ لا يَقبِض
    /// (<c>PlanPurchasePolicy.Visible</c>). فَأَقصى ما يَفعَلُه
    /// التَأليفُ: صَفٌّ في صَفحَةِ باقاتِ مَتجَرٍ واحِد.</para>
    ///
    /// <para><b>والمُصادِقُ يَعمَلُ مَرَّتَينِ كَما هُوَ</b> — عِندَ
    /// التَخزينِ وعِندَ الاعتِماد — ولا يُلمَسُ حَرفٌ مِنه. وإعادَةُ
    /// الفَتحِ قَبلَ الاقتِراحِ هي بِعَينِها ما تَفعَلُه
    /// <c>TenantThemeService.ApplyPresetAsync</c>: تَحريرُ باقَةٍ
    /// قائِمَةٍ عَمَلٌ عادِيٌّ لِصاحِبِها، لا «إعادَةُ تَعريفٍ مِن
    /// تَحتِ المُشرِف».</para>
    /// </summary>
    public async Task<(bool Ok, string Message)> AuthorAsync(
        string tenantSlug, PlanDefinition definition, string by, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return (false, "لا مُستَأجِر.");

        await ReopenIfApprovedAsync(tenantSlug, definition.Slug, by, ct);

        var json = PlanDefinitionLoader.ToJson(definition);
        var (proposed, proposeMsg) = await ProposeAsync(tenantSlug, definition.Slug, json, by, ct);
        if (!proposed) return (false, proposeMsg);

        return await DecideAsync(tenantSlug, definition.Slug, TenantPlanStatuses.Approved, by, ct);
    }

    /// <summary>يُعيد وَثيقَةَ باقَةٍ مُعتَمَدَةٍ إلى «مُعَلَّق» — وهي
    /// الخُطوَةُ الوَحيدَةُ الَّتي تُمَيِّزُ تَحريرَ باقَةٍ قائِمَةٍ
    /// عَن تَأليفِ أُولى. لا تُنشِئ ولا تَحذِف: غِيابُ الوَثيقَةِ لا
    /// شَيءَ يُفعَلُ بِه. نَفسُ جِسمِ
    /// <c>TenantThemeService.ReopenIfApprovedAsync</c> — <b>نُسخَةٌ
    /// ثانِيَةٌ لا ثالِثَة</b>، فَلا تُستَخرَج بَعد (القاعِدَة ١).</summary>
    private async Task ReopenIfApprovedAsync(
        string tenantSlug, string slug, string by, CancellationToken ct)
    {
        try
        {
            await using var s = Store.LightweightSession(tenantSlug);
            var doc = await s.LoadAsync<TenantPlanDefinition>(slug, ct);
            if (doc is not { Status: TenantPlanStatuses.Approved }) return;

            doc.Status    = TenantPlanStatuses.Pending;
            doc.DecidedBy = by;
            doc.DecidedAt = null;
            s.Store(doc);
            await s.SaveChangesAsync(ct);
            Invalidate(tenantSlug);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[plans] تَعَذَّرَ إعادَةُ فَتحِ «{slug}» في «{tenantSlug}»: {ex.Message}");
        }
    }

    /// <summary>نَفس القِراءَة بِلا كاش — لِمَسارات تَملِك المَخزَن
    /// ولا تَملِك الخِدمَة.</summary>
    public static Task<TenantPlanSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
        => QueryApprovedAsync(
            store, tenantSlug,
            TenantPlanSet.FromDocuments,
            TenantPlanSet.Platform,
            ex => $"[plans] تَعَذَّرَت قِراءَة باقات «{tenantSlug}» — " +
                  $"سُقوط إلى الباقات المُخَزَّنَة: {ex.Message}",
            ct);

    /// <summary>
    /// <para><b>الباقات المَعروضَة</b> — المُخَزَّنَة، مُلحَقاً بِها كُلّ
    /// تَعريف مُعتَمَد لا يُظَلِّل سلاجاً مَوجوداً. هذا هو <b>المَوضِع
    /// الحَيّ</b> الَّذي يَجعَل التَعريف باقَةً يَراها المُستَخدِم على
    /// <c>/{slug}/plans</c>.</para>
    ///
    /// <para><b>والتَرتيب هو تَرتيب اليَوم حَرفاً</b>: بِالسِعر
    /// تَصاعُدِيّاً، كَما في <c>Plans.razor</c> قَبل هذه المَوجَة.</para>
    /// </summary>
    public async Task<IReadOnlyList<Plan>> VisiblePlansAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        IReadOnlyList<Plan> stored;
        await using (var s = Store.QuerySession(tenantSlug))
            stored = (await s.Query<Plan>().Where(p => p.IsActive)
                             .OrderBy(p => p.Price).ToListAsync(ct)).ToList();

        var set = await ForAsync(tenantSlug, ct);
        var merged = set.Merge(stored);

        // بِلا تَعريفات: نَفس المَرجِع، فَلا فَرزَ ولا نَسخ.
        return ReferenceEquals(merged, stored)
            ? stored
            : merged.OrderBy(p => p.Price).ToList();
    }
}
