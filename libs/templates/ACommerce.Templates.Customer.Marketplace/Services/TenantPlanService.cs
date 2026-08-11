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
