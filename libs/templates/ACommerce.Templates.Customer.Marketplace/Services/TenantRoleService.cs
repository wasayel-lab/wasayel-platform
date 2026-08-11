using System.Collections.Concurrent;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>المُستَأجِر ولَقطَة أَدوارِه مَعاً — ما تَحتاجُه كُلّ صَفحَة
/// تَقرَأ <c>Tenant.Roles</c> وتُقَرِّر تَركيباً. نِداء واحِد بَدَل
/// نِدائَين، ولَقطَة واحِدَة لِلطَلَب بَدَل قِراءَتَين قَد تَختَلِفان.</summary>
public readonly record struct TenantWithRoles(Tenant? Tenant, TenantRoleSet Roles);

/// <summary>
/// <para><b>المَنفَذ الوَحيد إلى أَدوار مُستَأجِر وَقتَ التَّشغيل</b> —
/// يَقرَأ وَثائِق <see cref="TenantRoleDefinition"/> ويَبني
/// <see cref="TenantRoleSet"/> فَوق الكاتالوج المَضمون. مَواضِع
/// الالتِقاط تَسأَلُه ولا تَعرِف مِن أَينَ يُجيب — تَماماً كَما
/// تَسأَل <see cref="RoleCompositionResolver"/> اليَوم.</para>
///
/// <para><b>العَزل بُنيَويّ لا اتِّفاقيّ</b>: كُلّ قِراءَة تُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c>، والوَثيقَة مُتَعَدِّدَة الإيجار
/// بِسِياسَة <c>AllDocumentsAreMultiTenanted</c> — فَـ Marten يَضَع
/// <c>tenant_id</c> في الاستِعلام. لا سَطر شَرط مَكتوب بِاليَد يُمكِن
/// نِسيانُه، ولا استِعلام عابِر لِلمُستَأجِرين مُمكِن أَصلاً مِن هُنا.</para>
///
/// <para><b>والكاش بِمِفتاح المُستَأجِر حَصراً</b>
/// (<see cref="_cache"/>) — لا لَقطَة ساكِنَة واحِدَة تُشارِكُها
/// المَتاجِر. يُبطَل عِندَ كُلّ كِتابَة تَمَسّ الأَدوار: الاقتِراح
/// والاعتِماد والرَّفض. وهذا بِالضَبط ما يَجعَل بُرهان «فَوراً»
/// مُمكِناً: الاعتِماد يُبطِل مِفتاح المُستَأجِر، فَالطَلَب التالي
/// يَقرَأ الوَثائِق مِن جَديد — بِلا بِناء وبِلا إعادَة تَشغيل.</para>
///
/// <para><b>وسُقوط آمِن عِندَ تَعَذُّر القِراءَة</b> (جَدوَل غَير
/// مُنشَأ بَعد، أَو خَلَل عابِر): يُرجَع
/// <see cref="TenantRoleSet.Platform"/> — أَي سُلوك اليَوم حَرفاً —
/// و<b>لا يُخَزَّن الفَشَل في الكاش</b> كَي لا يَتَجَمَّد خَلَل عابِر
/// حالَةً دائِمَة. الاختِيار مُعلَن: مَتجَر يَفقِد دَوراً مُؤَلَّفاً
/// لِثَوانٍ أَهوَن مِن مَتجَر يَسقُط بِـ 500.</para>
/// </summary>
public sealed class TenantRoleService
{
    private readonly IDocumentStore _store;

    /// <summary>مِفتاحُه سلاج المُستَأجِر — وهذا شَرط لا تَحسين.</summary>
    private readonly ConcurrentDictionary<string, TenantRoleSet> _cache =
        new(StringComparer.Ordinal);

    public TenantRoleService(IDocumentStore store) => _store = store;

    /// <summary>لَقطَة أَدوار المُستَأجِر. سلاج فارِغ أَو <c>null</c>
    /// (سِياق بِلا مُستَأجِر) ← قاعِدَة المَنصَّة.</summary>
    public async Task<TenantRoleSet> ForAsync(string? tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return TenantRoleSet.Platform;
        if (_cache.TryGetValue(tenantSlug, out var hit)) return hit;

        var set = await ReadUncachedAsync(_store, tenantSlug, ct);

        // الفَشَل لا يُخَزَّن — يُعرَف بِأَنَّه سَقَطَ إلى لَقطَة المَنصَّة
        // بِلا سلاج، وتَخزينُه كانَ سَيُجَمِّد خَلَلاً عابِراً.
        if (set.TenantSlug is not null) _cache[tenantSlug] = set;
        return set;
    }

    /// <summary>
    /// <para><b>نَفس القِراءَة، بِلا كاش وبِلا نُسخَة مِن الجِسم</b> —
    /// <see cref="ForAsync"/> يُفَوِّض إلَيها. مَوجودَة لِمَسارات تَملِك
    /// <c>IDocumentStore</c> ولا تَملِك هذه الخِدمَة: مُعالِجات
    /// <c>minimal-API</c> السّاكِنَة في <c>MarketplaceTemplateExtensions</c>
    /// (تَوثيق الدُخول، تَسكين الدَور، فَحص الصَلاحِيَّة).</para>
    ///
    /// <para><b>مُفتَرَق مُعلَن</b>: كانَ البَديل تَمرير الخِدمَة عَبر
    /// ثَلاث دَوالّ خاصَّة وثَلاث lambdas. الاختِيار وَقَعَ عَلى مَدخَل
    /// ساكِن <b>بِنَفس الجِسم</b> — فَلا مَسار قِراءَة ثانٍ يَنحَرِف —
    /// والثَمَن استِعلام إضافيّ عَلى مَسارات الدُخول وَحدَها، وهي نادِرَة
    /// بِطَبيعَتِها ومُقارَنَةً بِكُلّ عَرض صَفحَة.</para>
    /// </summary>
    public static async Task<TenantRoleSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return TenantRoleSet.Platform;

        try
        {
            await using var s = store.QuerySession(tenantSlug);
            var docs = await s.Query<TenantRoleDefinition>()
                .Where(d => d.Status == TenantRoleStatuses.Approved)
                .ToListAsync(ct);
            return TenantRoleSet.FromDocuments(tenantSlug, docs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[roles] تَعَذَّرَت قِراءَة تَعريفات أَدوار «{tenantSlug}» — " +
                $"سُقوط إلى كاتالوج المَنصَّة: {ex.Message}");
            return TenantRoleSet.Platform;
        }
    }

    /// <summary>المُستَأجِر بِأَدوارِه المُجَسَّدَة — يُحَمِّل الوَثيقَة
    /// العامَّة ثُمَّ يُلحِق بِـ <c>Roles</c> دَوراً لِكُلّ تَعريف
    /// مُؤَلَّف مُعتَمَد. <b>المُستَأجِر بِلا تَأليف يُرجَع كَما هو،
    /// بِنَفس مَرجِع قائِمَتِه</b>.</summary>
    public async Task<TenantWithRoles> LoadAsync(string slug, CancellationToken ct = default)
    {
        Tenant? tenant;
        await using (var g = _store.QuerySession())
            tenant = await g.LoadAsync<Tenant>(slug, ct);

        if (tenant is null) return new TenantWithRoles(null, TenantRoleSet.Platform);

        var set = await ForAsync(slug, ct);
        var merged = set.Materialize(tenant.Roles);
        // الوَثيقَة مُحَمَّلَة مِن جَلسَة قِراءَة لا تَحفَظ — التَجسيد
        // يَعيش في الذاكِرَة لِهذا الطَلَب ولا يُكتَب أَبَداً. وهذا
        // مَقصود: مَصدَر الحَقيقَة يَبقى الوَثائِق، لا نُسخَة مِنها
        // داخِل مُستَند المُستَأجِر تَنحَرِف عَنها.
        if (!ReferenceEquals(merged, tenant.Roles)) tenant.Roles = merged.ToList();

        return new TenantWithRoles(tenant, set);
    }

    /// <summary>كُلّ تَعريفات المُستَأجِر بِكُلّ حالاتِها — لِصَفحَة
    /// الإدارَة وَحدَها. المَقروء في السُطوح هو المُعتَمَد فَقَط.</summary>
    public async Task<IReadOnlyList<TenantRoleDefinition>> ListAllAsync(
        string tenantSlug, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(tenantSlug);
            var all = await s.Query<TenantRoleDefinition>().ToListAsync(ct);
            return all.OrderBy(d => d.CreatedAt).ThenBy(d => d.Slug, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[roles] تَعَذَّرَ سَرد تَعريفات «{tenantSlug}»: {ex.Message}");
            return Array.Empty<TenantRoleDefinition>();
        }
    }

    /// <summary>
    /// <para><b>يَكتُب تَعريفاً مُعَلَّقاً</b> — لا حالَة أُخرى مِن هُنا.
    /// المُصادَقَة تَمَّت قَبلَه في المُنَفِّذ (شَكلاً ثُمَّ مَعجَماً)،
    /// وهذه الدالَّة تَكتُب فَقَط.</para>
    /// </summary>
    public async Task<(bool Ok, string Message)> ProposeAsync(
        string tenantSlug, string roleSlug, string definitionJson,
        string by, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);

        var existing = await s.LoadAsync<TenantRoleDefinition>(roleSlug, ct);
        if (existing is { Status: TenantRoleStatuses.Approved })
            return (false,
                $"الدَور «{roleSlug}» مُعتَمَد بِالفِعل في «{tenantSlug}» — " +
                "لا يُعاد تَعريفُه مِن الوَكيل.");

        s.Store(new TenantRoleDefinition
        {
            Id             = roleSlug,
            Slug           = roleSlug,
            DefinitionJson = definitionJson,
            Status         = TenantRoleStatuses.Pending,
            CreatedBy      = by,
            CreatedAt      = DateTime.UtcNow
        });
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return (true, $"سُجِّلَ تَعريف الدَور «{roleSlug}» مُعَلَّقاً.");
    }

    /// <summary>قَرار بَشَريّ: اعتِماد أَو رَفض. <b>هُنا وَحدَه</b>
    /// يَصير تَعريف حَيّاً، وهُنا وَحدَه يُبطَل الكاش.</summary>
    public async Task<(bool Ok, string Message)> DecideAsync(
        string tenantSlug, string roleSlug, string status,
        string by, CancellationToken ct = default)
    {
        // الحارِس يَسأَل تَعريف التَدَفُّق، لا شَرطاً مَكتوباً بِاليَد:
        // هَل مِن pending انتِقال إلى هذه الحالَة يَملِكُه مُقَرِّر؟
        // النَتيجَة مُطابِقَة لِلشَرط القَديم حَرفاً — والفَرق أَنّ
        // مَوضِع الحَقيقَة صارَ واحِداً لِلأَدوار والمَظهَر مَعاً.
        if (!ACommerce.Platform.Flows.ApprovalFlow.IsDecision(status))
            return (false, $"قَرار غَير مَعروف: «{status}».");

        await using var s = _store.LightweightSession(tenantSlug);
        var doc = await s.LoadAsync<TenantRoleDefinition>(roleSlug, ct);
        if (doc is null)
            return (false, $"لا تَعريف بِاسم «{roleSlug}» في «{tenantSlug}».");

        // الاعتِماد يُعيد المُصادَقَة عَلى النَّصّ المُخَزَّن — لا يَثِق
        // بِأَنَّها جَرَت عِندَ الكِتابَة. الوَثيقَة قَد تَكون كُتِبَت
        // بِيَد أَو نَجَت مِن تَرحيل، والاعتِماد هو آخِر بَوّابَة
        // قَبل أَن يَراها لاعِب.
        if (status == TenantRoleStatuses.Approved)
        {
            RoleDefinition parsed;
            try { parsed = RoleDefinitionLoader.ParseDefinition(doc.DefinitionJson); }
            catch (Exception ex) { return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message); }

            var violations = RoleDefinitionValidator.ValidateTenantDefinition(parsed);
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

        return (true, status == TenantRoleStatuses.Approved
            ? $"اعتُمِدَ الدَور «{roleSlug}» — صارَ حَيّاً في «{tenantSlug}»."
            : $"رُفِضَ الدَور «{roleSlug}».");
    }

    /// <summary>إبطال لَقطَة مُستَأجِر واحِد. مِفتاح واحِد لا مَسح
    /// شامِل — مَتجَر يُعَدِّل أَدوارَه لا يُكَلِّف بَقِيَّة المَتاجِر
    /// قِراءَةً.</summary>
    public void Invalidate(string tenantSlug) => _cache.TryRemove(tenantSlug, out _);
}
