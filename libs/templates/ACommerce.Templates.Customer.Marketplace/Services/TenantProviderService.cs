using ACommerce.Platform.Providers;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>مُزَوِّدو المُستَأجِرِ وَقتَ التَشغيل</b> — رابِعُ
/// مُستَهلِكي <c>TenantDefinitionService</c> بَعدَ الأَدوارِ والثيمِ
/// والباقات. و<b>لا أُنبوبَ رابِعاً يُبنى</b> (القاعِدَة ٨): الكاشُ
/// بِمِفتاحِ السلاج، والقِراءَةُ بِجَلسَةِ المُستَأجِر، والسُقوطُ
/// الآمِن، والإبطال — كُلُّها مِن المَكانيكا القائِمَة.</para>
///
/// <para><b>وما لا يُورَث يُعلَن</b> (القاعِدَة ١٥): دَورَةُ الاعتِمادِ
/// (<c>ProposeAsync</c>/<c>DecideAsync</c>) <b>تَرمي هُنا</b>. رَبطُ
/// مُزَوِّدٍ لَيسَ تَعريفاً يُؤَلِّفُه المُستَأجِر ولا يَمُرّ
/// بِمُراجَعَة: يُربَط فَيَعمَل، ويُسحَب فَيَتَوَقَّفُ <b>الآن</b>.</para>
///
/// <para><b>ودَينٌ مُعلَنٌ يُقاس</b>: مُستَأجِرٌ بِلا رَبطٍ يُرجِعُ
/// <c>TenantProviderSet.Platform</c> بِسلاجٍ <c>null</c>، والمَكانيكا
/// لا تُخَزِّنُ لَقطَةً بِلا سلاج (تَمييزاً لِلفَشَلِ العابِرِ عَن
/// النَجاح) — فَكُلُّ قِراءَةٍ لِمُستَأجِرٍ غَيرِ مَربوطٍ استِعلامٌ.
/// وهذا مَقبولٌ بِالقياس: مُستَهلِكو هذِه المَوجَةِ ثَلاثَةٌ كُلُّها
/// بارِدَة (شاشَةُ الاستوديو، نُقطَتا الرَبطِ والسَحب، ونَقرَةُ
/// الشِراء) — ولَيسَ فيها مَسارُ تَصييرٍ عامّ.</para>
/// </summary>
public sealed class TenantProviderService
    : TenantDefinitionService<TenantProviderBinding, TenantProviderSet>
{
    public TenantProviderService(IDocumentStore store) : base(store) { }

    // ─── مُفرَداتُ الرَبط ─────────────────────────────────────────────

    protected override TenantProviderSet PlatformSet => TenantProviderSet.Platform;

    protected override string? SlugOf(TenantProviderSet set) => set.TenantSlug;

    protected override TenantProviderSet Build(
        string tenantSlug, IReadOnlyList<TenantProviderBinding> docs)
        => TenantProviderSet.FromDocuments(tenantSlug, docs);

    protected override string LogTag => "providers";

    protected override string ListFailureAr(string tenantSlug, string error)
        => $"[providers] تَعَذَّرَ سَرد رَبط «{tenantSlug}»: {error}";

    protected override Task<TenantProviderSet> ReadUncachedCoreAsync(
        string tenantSlug, CancellationToken ct)
        => ReadUncachedAsync(Store, tenantSlug, ct);

    // ─── ما لا يُورَث — ويُعلَن ولا يُبتَلَع ──────────────────────────

    private const string NoApprovalCycleAr =
        "رَبطُ المُزَوِّدِ لا يَمُرّ بِدَورَةِ اعتِماد: `ApprovalFlow.Approved` " +
        "نِهائِيَّةٌ مُعلَنَة، ولا تَصلُح لِاعتِمادٍ يَجِب أَن يَتَوَقَّفَ " +
        "الآن. الرَبطُ يُكتَب `active` ويُسحَب `revoked` — نَفسُ شَكل " +
        "`ApiKeyDocument` حَرفاً. (‏ADR-012)";

    protected override (bool Ok, string Message) ValidateBeforeStore(
        string definitionJson, string slug)
        => (false, NoApprovalCycleAr);

    protected override (bool Ok, string Message) ValidateBeforeApprove(TenantProviderBinding doc)
        => (false, NoApprovalCycleAr);

    protected override string AlreadyApprovedAr(string slug, string tenantSlug) => NoApprovalCycleAr;
    protected override string ProposedAr(string slug) => NoApprovalCycleAr;
    protected override string DecidedAr(string slug, string tenantSlug, bool approved) => NoApprovalCycleAr;

    protected override string NotFoundAr(string slug, string tenantSlug)
        => $"لا رَبطَ لِلقُدرَة «{slug}» في «{tenantSlug}».";

    /// <summary><b>يَرمي، ولا يَكتُب حالَةً مِن مَعجَمٍ آخَر.</b> الحَدُّ
    /// الَّذي لا يُقاس آلِيّاً يَنهار (القاعِدَة ٢) — والقياسُ هُنا
    /// أَقوى مِن اختِبارٍ نَصِّيّ: النِداءُ نَفسُه يَفشَل.</summary>
    public new Task<(bool Ok, string Message)> ProposeAsync(
        string tenantSlug, string slug, string definitionJson,
        string by, CancellationToken ct = default)
        => throw new NotSupportedException(NoApprovalCycleAr);

    public new Task<(bool Ok, string Message)> DecideAsync(
        string tenantSlug, string slug, string status,
        string by, CancellationToken ct = default)
        => throw new NotSupportedException(NoApprovalCycleAr);

    // ─── ما يَخُصُّ الرَبطَ وَحدَه ────────────────────────────────────

    /// <summary>القِراءَةُ الحَيَّة — <b>بِمَعجَمِ الرَبطِ لا
    /// بِـ<c>ApprovalFlow</c></b>، ولِذلكَ لا تُستَعمَلُ
    /// <c>QueryApprovedAsync</c> المُشتَرَكَة.</summary>
    public static async Task<TenantProviderSet> ReadUncachedAsync(
        IDocumentStore store, string tenantSlug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tenantSlug)) return TenantProviderSet.Platform;

        try
        {
            await using var s = store.QuerySession(tenantSlug);
            var docs = await s.Query<TenantProviderBinding>()
                .Where(d => d.Status == TenantProviderBinding.StatusActive)
                .ToListAsync(ct);
            return TenantProviderSet.FromDocuments(tenantSlug, docs);
        }
        catch (Exception ex)
        {
            // فَشَلٌ مُغلَق: السُقوطُ إلى لَقطَةِ المَنَصَّة يَعني «لا
            // مُزَوِّدَ مَربوط» — أَي **لا تُعرَض باقَةٌ مَدفوعَة** ولا
            // تُصَيَّر صَفحَةُ دَفع. ولا سُقوطَ صامِتاً إلى مُزَوِّدٍ
            // آخَر.
            Console.Error.WriteLine(
                $"[providers] تَعَذَّرَت قِراءَة رَبط «{tenantSlug}» — " +
                $"سُقوطٌ إلى «بِلا مُزَوِّد»: {ex.Message}");
            return TenantProviderSet.Platform;
        }
    }

    /// <summary>يَكتُبُ رَبطاً فَعّالاً لِقُدرَةٍ واحِدَة — ويَسحَبُ ما
    /// قَبلَه بِالكِتابَةِ فَوقَه (لا تَحريرَ في المَوضِع: الوَثيقَةُ
    /// تُستَبدَل، والقَديمُ لا يَبقى فَعّالاً).</summary>
    public async Task<TenantProviderBinding> BindProviderAsync(
        string tenantSlug, string capability, string providerSlug,
        IReadOnlyDictionary<string, StoredValue> values,
        string by, CancellationToken ct = default)
    {
        ProviderCapabilities.Require(capability);

        var doc = new TenantProviderBinding
        {
            Id           = capability,
            Slug         = capability,
            TenantSlug   = tenantSlug,
            ProviderSlug = providerSlug,
            Status       = TenantProviderBinding.StatusActive,
            Values       = new Dictionary<string, StoredValue>(values, StringComparer.Ordinal),
            BoundBy      = by,
            BoundAt      = DateTime.UtcNow,
            RevokedAt    = null,
        };

        await using var s = Store.LightweightSession(tenantSlug);
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return doc;
    }

    /// <summary>السَحبُ يُوقِفُ <b>الآن</b> — بِخَتمِ وَقتٍ لا بِحَذف،
    /// فَيَبقى الأَثَرُ لِلتَدقيق.</summary>
    public async Task<TenantProviderBinding?> RevokeAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
    {
        await using var s = Store.LightweightSession(tenantSlug);
        var doc = await s.LoadAsync<TenantProviderBinding>(capability, ct);
        if (doc is null || doc.Status == TenantProviderBinding.StatusRevoked) return null;

        doc.Status    = TenantProviderBinding.StatusRevoked;
        doc.RevokedAt = DateTime.UtcNow;
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return doc;
    }

    /// <summary>الرَبطُ الحاليُّ لِقُدرَةٍ — فَعّالاً كانَ أَو
    /// مَسحوباً — لِتَرسُمَه الشاشَة.</summary>
    public async Task<TenantProviderBinding?> CurrentAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
    {
        try
        {
            await using var s = Store.QuerySession(tenantSlug);
            return await s.LoadAsync<TenantProviderBinding>(capability, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ListFailureAr(tenantSlug, ex.Message));
            return null;
        }
    }
}
