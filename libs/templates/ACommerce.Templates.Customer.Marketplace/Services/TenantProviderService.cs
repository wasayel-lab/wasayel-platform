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
    /// <remarks>(‏2026-10-29، قَرارُ المالِك د5 (٢) — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه: و<paramref name="webhookId"/> و<paramref name="webhookRefusal"/> تَسجيلُ إشعارِ البَوّابَةِ
    /// لِهذا الرَبط — حَقلا مَدخَلِ البائِعِ نَفسُهُما (<see cref="BindVendorAsync"/>)؛ يَكتُبُهُما <c>GatewayPayments.ConnectNoticesAsync</c> وَحدَه، والافتِراضُ لا تَسجيل.)</remarks>
    public async Task<TenantProviderBinding> BindProviderAsync(
        string tenantSlug, string capability, string providerSlug,
        IReadOnlyDictionary<string, StoredValue> values,
        string by, CancellationToken ct = default, string? webhookId = null, string? webhookRefusal = null)
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
            WebhookId      = webhookId,
            WebhookRefusal = webhookRefusal,
        };

        await using var s = Store.LightweightSession(tenantSlug);
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        Invalidate(tenantSlug);
        return doc;
    }

    /// <summary>السَحبُ يُوقِفُ <b>الآن</b> — بِخَتمِ وَقتٍ لا بِحَذف،
    /// فَيَبقى الأَثَرُ لِلتَدقيق.</summary>
    /// <remarks><b>والمُعَرِّفُ قُدرَةٌ مِن المَعجَمِ وإلّا فَلا شَيء</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (i)): مَدخَلُ البائِعِ
    /// (<c>{capability}:{owner}</c>، <see cref="TenantProviderBinding.VendorEntryId"/>) في المَجموعَةِ نَفسِها، وبابُ السَحبِ في الاستوديو
    /// يُمَرِّرُ ما في العُنوانِ — فَكانَ صاحِبُ المَتجَرِ يَسحَبُ مَدخَلَ بائِعٍ مَختوماً و‏ADR-033 §٢‑ج‑٣ يُبقيه خارِجَ الاستوديو.
    /// والرَدُّ <c>null</c> قَبلَ أَيِّ جَلسَة — ما يُجيبُه البابُ لِرَبطٍ غَيرِ مَوجود (<c>binding_not_found</c>) — لا استِثناءٌ يُجابُ ‏500.</remarks>
    public async Task<TenantProviderBinding?> RevokeAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
    {
        if (!ProviderCapabilities.Contains(capability)) return null;

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

    // ─── مَدخَلُ البائِع — استِعلامٌ بِمِفتاحِه لا بِـ`TenantProviderSet.For` (‏ADR-033 §٢‑ج‑٢ والخُطوَة 7؛ تَيّارُ S8، ‏2026-10-12) ───
    //
    // الوَثيقَةُ نَفسُها بِمُعَرِّفٍ مُرَكَّبٍ `{capability}:{listing_owner}` (`TenantProviderBinding.VendorEntryId`)، ومُجموعَةُ المَتجَرِ
    // تَتَجاوَزُها بِقاعِدَةٍ مُسَمّاة — فَلا يَمَسُّ مَدخَلُ بائِعٍ لَقطَةَ المَتجَرِ ولا كاشَها. والقِراءَةُ هُنا بِجَلسَةِ المُستَأجِرِ نَفسِها.

    // ‏(‏2026-10-24 — تَيّارُ S16، بَقايا مُراجَعَةِ S15: C(19)) **حُذِفَت `VendorEntryAsync`** — كانَت تُجيبُ عُطلَ القِراءَةِ `null` («لا مَدخَل»)، ولَم
    // يَبقَ لَها مُستَهلِكٌ في وَقتِ التَشغيلِ بَعدَ `216d61e` (العَرضُ صارَ يَقرَأُ بِـ`ReadVendorEntryAsync`)، وقُرّاؤُها السَبعَةُ في الاختِباراتِ نُقِلوا
    // إلى `ReadVendorEntryAsync(...).Entry` في `9c38003`. فَمَدخَلُ البائِعِ يُقرَأُ بِالدالَّةِ الَّتي تُفَرِّقُ العُطلَ مِن الغِيابِ وَحدَها.

    /// <summary>
    /// <para><b>قِراءَةُ مَدخَلِ البائِعِ تُفَرِّقُ «لا مَدخَل» مِن «تَعَذَّرَت القِراءَة»</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (iii)؛ ‏ADR-033 §٧‑ص‑٦):
    /// <see cref="VendorEntryRead.Failed"/> صادِقَةٌ حينَ رَمَت القِراءَة — يُكتَبُ سَطرُها كَما كان — و<see cref="VendorEntryRead.Entry"/> حينَئِذٍ <c>null</c>
    /// لا يُقرَأُ غِياباً. قُرّاؤُها: الناقِلُ (<c>DealShipping.CarrierReadyForAsync</c>/<c>CarrierForAsync</c> — «لا ناقِلَ» بِلا احتِياط)، والتَسجيلُ واللَصقُ
    /// (<c>VendorShippingService</c> — رَمزٌ يُرَدُّ ولا نِداءَ ولا كِتابَة).</para>
    /// </summary>
    public async Task<VendorEntryRead> ReadVendorEntryAsync(
        string tenantSlug, string capability, Guid listingOwner, CancellationToken ct = default)
    {
        try
        {
            await using var s = Store.QuerySession(tenantSlug);
            return new(await s.LoadAsync<TenantProviderBinding>(
                TenantProviderBinding.VendorEntryId(capability, listingOwner), ct), false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ListFailureAr(tenantSlug, ex.Message));
            return new(null, true);
        }
    }

    /// <summary>أَفي المَتجَرِ مَدخَلُ بائِعٍ فَعّالٌ لِهذِه القُدرَة؟ — يَسأَلُه وارِدُ الناقِلِ قَبلَ أَن يَقرَأَ جِسماً. والفَشَلُ «لا».
    /// <para>(‏2026-10-13، G5 (v) — ولا يُعادُ حَرفٌ فَوقَه: والشَرطُ <see cref="TenantProviderBinding.ActiveVendorEntryOf"/> بِحَرفِه — يُقاسُ في CI
    /// مُجَمَّعاً عَلى وَثائِقَ في الذاكِرَة، ويُتَرجِمُه Marten هُنا كَما كان.)</para></summary>
    public async Task<bool> HasVendorEntriesAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
        => (await ReadVendorEntriesAsync(tenantSlug, capability, ct)).Any;

    /// <summary>
    /// <para><b>والسُؤالُ نَفسُه يُفَرِّقُ «لا مَداخِل» مِن «تَعَذَّرَت القِراءَة»</b> (‏2026-10-29، قَرارُ المالِك د9 (٢) «ب»؛ تَوصيفُه <c>7acae84</c>): نَسَقُ
    /// <see cref="ReadVendorEntryAsync"/> حَرفاً — <see cref="VendorEntriesRead.Failed"/> صادِقَةٌ حينَ رَمَى الاستِعلامُ ويُكتَبُ سَطرُه كَما كان،
    /// و<see cref="VendorEntriesRead.Any"/> حينَئِذٍ <c>false</c> لا يُقرَأُ غِياباً. يَقرَؤُه وارِدُ الناقِلِ فَيُجيبُ ‏503 «أَعِد المُحاوَلَة» لا ‏404 «لا نُقطَة»؛
    /// و<see cref="HasVendorEntriesAsync"/> بِجَوابِه كَما كان: «لا».</para>
    /// </summary>
    public async Task<VendorEntriesRead> ReadVendorEntriesAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
    {
        var activeVendorEntry = TenantProviderBinding.ActiveVendorEntryOf(capability);
        try
        {
            await using var s = Store.QuerySession(tenantSlug);
            return new(await s.Query<TenantProviderBinding>().AnyAsync(activeVendorEntry, ct), false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ListFailureAr(tenantSlug, ex.Message));
            return new(false, true);
        }
    }

    /// <summary>يَكتُبُ مَدخَلَ بائِعٍ فَعّالاً — يَحِلُّ مَحَلَّ ما قَبلَه (الوَثيقَةُ تُستَبدَل) — بِقيَمٍ مَختومَةٍ يَبنيها المُنادي.
    /// ولا يُبطِلُ كاشَ المَتجَر: المَدخَلُ لا يَدخُلُ لَقطَتَه.</summary>
    public async Task<TenantProviderBinding> BindVendorAsync(
        string tenantSlug, string capability, Guid listingOwner, string providerSlug,
        IReadOnlyDictionary<string, StoredValue> values, string? webhookId, string? webhookRefusal,
        string by, CancellationToken ct = default)
    {
        var doc = new TenantProviderBinding
        {
            Id             = TenantProviderBinding.VendorEntryId(capability, listingOwner),
            Slug           = capability,
            TenantSlug     = tenantSlug,
            ProviderSlug   = providerSlug,
            Status         = TenantProviderBinding.StatusActive,
            Values         = new Dictionary<string, StoredValue>(values, StringComparer.Ordinal),
            BoundBy        = by,
            BoundAt        = DateTime.UtcNow,
            RevokedAt      = null,
            WebhookId      = webhookId,
            WebhookRefusal = webhookRefusal,
        };

        await using var s = Store.LightweightSession(tenantSlug);
        s.Store(doc);
        await s.SaveChangesAsync(ct);
        return doc;
    }

    /// <summary>الرَبطُ الحاليُّ لِقُدرَةٍ — فَعّالاً كانَ أَو
    /// مَسحوباً — لِتَرسُمَه الشاشَة.</summary>
    /// <remarks>قُدرَةٌ مِن المَعجَمِ وإلّا <c>null</c> قَبلَ أَيِّ جَلسَة (‏2026-10-13، G5 (i)) — فَلا يَقرَأُ سَطرُ «قَبلُ» في أَثَرِ السَحبِ
    /// مَدخَلَ بائِعٍ بِمُعَرِّفِه؛ ومَدخَلُ البائِعِ يُقرَأُ بِـ<c>VendorEntryAsync</c> وَحدَها.
    /// (‏2026-10-24 — تَيّارُ S16، يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) وحُذِفَت تِلكَ؛ يُقرَأُ بِـ<see cref="ReadVendorEntryAsync"/>.</remarks>
    public async Task<TenantProviderBinding?> CurrentAsync(
        string tenantSlug, string capability, CancellationToken ct = default)
    {
        if (!ProviderCapabilities.Contains(capability)) return null;

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

/// <summary>جَوابُ <see cref="TenantProviderService.ReadVendorEntryAsync"/>: المَدخَلُ أَو <c>null</c> لِغِيابِه — و<see cref="Failed"/> حينَ تَعَذَّرَت
/// القِراءَةُ فَلا يُقرَأُ <c>null</c> غِياباً (‏2026-10-13، G5 (iii)).</summary>
public readonly record struct VendorEntryRead(TenantProviderBinding? Entry, bool Failed);

/// <summary>جَوابُ <see cref="TenantProviderService.ReadVendorEntriesAsync"/>: مَدخَلُ بائِعٍ فَعّالٌ واحِدٌ عَلى الأَقَلّ، أَو تَعَذَّرَت القِراءَة
/// (‏2026-10-29، قَرارُ المالِك د9 (٢) «ب») — نَسَقُ <see cref="VendorEntryRead"/>.</summary>
public readonly record struct VendorEntriesRead(bool Any, bool Failed);
