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
/// <para><b>ومَكانيكاهُ صارَت مُشتَرَكَة</b>
/// (<see cref="TenantDefinitionService{TDoc,TSet}"/>): الكاش بِمِفتاح
/// المُستَأجِر، وعَزلُ الجَلسَة، والسُقوط الآمِن، وحارِس القَرار،
/// والإبطال — كُلُّها هُناك مَرَّةً واحِدَة. وما بَقِيَ هُنا
/// <b>مُفرَدات الأَدوار</b>: كَيفَ تُبنى اللَقطَة، ومَتى تُصادَق،
/// وبِأَيّ عِبارَة تُجيب. <b>ولَم يَتَغَيَّر حَرفٌ مِمّا يُجيب بِه</b> —
/// وذلك شَرط التَبديل لا أَثَرُه.</para>
///
/// <para><b>العَزل بُنيَويّ لا اتِّفاقيّ</b>: كُلّ قِراءَة تُفتَح بِـ
/// <c>QuerySession(tenantSlug)</c>، والوَثيقَة مُتَعَدِّدَة الإيجار
/// بِسِياسَة <c>AllDocumentsAreMultiTenanted</c> — فَـ Marten يَضَع
/// <c>tenant_id</c> في الاستِعلام. لا سَطر شَرط مَكتوب بِاليَد يُمكِن
/// نِسيانُه، ولا استِعلام عابِر لِلمُستَأجِرين مُمكِن أَصلاً مِن هُنا.</para>
///
/// <para><b>والكاش بِمِفتاح المُستَأجِر حَصراً</b> — لا لَقطَة ساكِنَة
/// واحِدَة تُشارِكُها المَتاجِر. يُبطَل عِندَ كُلّ كِتابَة تَمَسّ
/// الأَدوار: الاقتِراح والاعتِماد والرَّفض. وهذا بِالضَبط ما يَجعَل
/// بُرهان «فَوراً» مُمكِناً: الاعتِماد يُبطِل مِفتاح المُستَأجِر،
/// فَالطَلَب التالي يَقرَأ الوَثائِق مِن جَديد — بِلا بِناء وبِلا
/// إعادَة تَشغيل.</para>
///
/// <para><b>وسُقوط آمِن عِندَ تَعَذُّر القِراءَة</b> (جَدوَل غَير
/// مُنشَأ بَعد، أَو خَلَل عابِر): يُرجَع
/// <see cref="TenantRoleSet.Platform"/> — أَي سُلوك اليَوم حَرفاً —
/// و<b>لا يُخَزَّن الفَشَل في الكاش</b> كَي لا يَتَجَمَّد خَلَل عابِر
/// حالَةً دائِمَة. الاختِيار مُعلَن: مَتجَر يَفقِد دَوراً مُؤَلَّفاً
/// لِثَوانٍ أَهوَن مِن مَتجَر يَسقُط بِـ 500.</para>
/// </summary>
public sealed class TenantRoleService : TenantDefinitionService<TenantRoleDefinition, TenantRoleSet>
{
    public TenantRoleService(IDocumentStore store) : base(store) { }

    // ─── مُفرَدات الأَدوار ────────────────────────────────────────────

    protected override TenantRoleSet PlatformSet => TenantRoleSet.Platform;

    protected override string? SlugOf(TenantRoleSet set) => set.TenantSlug;

    protected override TenantRoleSet Build(string tenantSlug, IReadOnlyList<TenantRoleDefinition> docs)
        => TenantRoleSet.FromDocuments(tenantSlug, docs);

    protected override string LogTag => "roles";

    protected override Task<TenantRoleSet> ReadUncachedCoreAsync(string tenantSlug, CancellationToken ct)
        => ReadUncachedAsync(Store, tenantSlug, ct);

    protected override string ListFailureAr(string tenantSlug, string error)
        => $"[roles] تَعَذَّرَ سَرد تَعريفات «{tenantSlug}»: {error}";

    /// <summary><b>الأَدوار لا تُصادِق هُنا</b> — المُصادَقَة تَمَّت
    /// قَبلَه في المُنَفِّذ (شَكلاً ثُمَّ مَعجَماً)، وهذه الدالَّة
    /// تَكتُب فَقَط. وهذا هو الفَرق السُلوكيّ الوَحيد عَن المَظهَر،
    /// <b>مُعلَناً في التَوقيع لا مَخفِيّاً في وَسيط</b>.</summary>
    protected override (bool Ok, string Message) ValidateBeforeStore(string definitionJson, string slug)
        => (true, "");

    /// <summary>الاعتِماد يُعيد المُصادَقَة عَلى النَّصّ المُخَزَّن — لا
    /// يَثِق بِأَنَّها جَرَت عِندَ الكِتابَة. الوَثيقَة قَد تَكون كُتِبَت
    /// بِيَد أَو نَجَت مِن تَرحيل، والاعتِماد هو آخِر بَوّابَة قَبل أَن
    /// يَراها لاعِب.</summary>
    protected override (bool Ok, string Message) ValidateBeforeApprove(TenantRoleDefinition doc)
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

        return (true, "");
    }

    protected override string AlreadyApprovedAr(string slug, string tenantSlug)
        => $"الدَور «{slug}» مُعتَمَد بِالفِعل في «{tenantSlug}» — " +
           "لا يُعاد تَعريفُه مِن الوَكيل.";

    protected override string ProposedAr(string slug)
        => $"سُجِّلَ تَعريف الدَور «{slug}» مُعَلَّقاً.";

    protected override string NotFoundAr(string slug, string tenantSlug)
        => $"لا تَعريف بِاسم «{slug}» في «{tenantSlug}».";

    protected override string DecidedAr(string slug, string tenantSlug, bool approved)
        => approved
            ? $"اعتُمِدَ الدَور «{slug}» — صارَ حَيّاً في «{tenantSlug}»."
            : $"رُفِضَ الدَور «{slug}».";

    // ─── ما يَخُصّ الأَدوار وَحدَها ───────────────────────────────────

    /// <summary>
    /// <para><b>نَفس القِراءَة، بِلا كاش وبِلا نُسخَة مِن الجِسم</b> —
    /// <see cref="TenantDefinitionService{TDoc,TSet}.ForAsync"/> يُفَوِّض
    /// إلَيها. مَوجودَة لِمَسارات تَملِك <c>IDocumentStore</c> ولا تَملِك
    /// هذه الخِدمَة: مُعالِجات <c>minimal-API</c> السّاكِنَة في
    /// <c>MarketplaceTemplateExtensions</c> (تَوثيق الدُخول، تَسكين
    /// الدَور، فَحص الصَلاحِيَّة).</para>
    ///
    /// <para><b>مُفتَرَق مُعلَن</b>: كانَ البَديل تَمرير الخِدمَة عَبر
    /// ثَلاث دَوالّ خاصَّة وثَلاث lambdas. الاختِيار وَقَعَ عَلى مَدخَل
    /// ساكِن <b>بِنَفس الجِسم</b> — فَلا مَسار قِراءَة ثانٍ يَنحَرِف —
    /// والثَمَن استِعلام إضافيّ عَلى مَسارات الدُخول وَحدَها، وهي نادِرَة
    /// بِطَبيعَتِها ومُقارَنَةً بِكُلّ عَرض صَفحَة.</para>
    /// </summary>
    public static Task<TenantRoleSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
        => QueryApprovedAsync(
            store, tenantSlug,
            TenantRoleSet.FromDocuments,
            TenantRoleSet.Platform,
            ex => $"[roles] تَعَذَّرَت قِراءَة تَعريفات أَدوار «{tenantSlug}» — " +
                  $"سُقوط إلى كاتالوج المَنصَّة: {ex.Message}",
            ct);

    /// <summary>المُستَأجِر بِأَدوارِه المُجَسَّدَة — يُحَمِّل الوَثيقَة
    /// العامَّة ثُمَّ يُلحِق بِـ <c>Roles</c> دَوراً لِكُلّ تَعريف
    /// مُؤَلَّف مُعتَمَد. <b>المُستَأجِر بِلا تَأليف يُرجَع كَما هو،
    /// بِنَفس مَرجِع قائِمَتِه</b>.</summary>
    public async Task<TenantWithRoles> LoadAsync(string slug, CancellationToken ct = default)
    {
        Tenant? tenant;
        await using (var g = Store.QuerySession())
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
}
