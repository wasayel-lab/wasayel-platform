using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ACommerce.Templates.Customer.Marketplace.Services.Shipping;

// ═══ حِسابُ الشَحنِ لِلبائِعِ داخِلَ المَتجَر — ‏ADR-033 §٢‑ج‑٣ والخُطوَة 7 (تَيّارُ S8، ‏2026-10-12) ═══════════════════
//
// **السَطحُ سَطحُ البائِعِ لا الاستوديو**: البائِعُ (مالِكُ إعلانٍ في المَتجَر) يَلصِقُ رَمزَ تَحديثِ حِسابِه عِندَ OTO في صَفحَتِه
// («إعلاناتي»)، فَيُختَمُ في مَدخَلِه (`TenantProviderBinding` بِمُعَرِّفِ `delivery:{listing_owner}`)، ونُصدِرُ لَه سِرَّ وارِدٍ
// نُسَجِّلُه عِندَ OTO بِرَمزِه هُوَ لِتَوقِّعَ بِه إشعاراتِ طَلَباتِه إلى عُنوانِ هذا المَتجَر. ولا يُرسَمُ في `StudioProviders.razor`:
// ذاكَ رَبطٌ واحِدٌ لِكُلِّ قُدرَةٍ يَملَؤُه مالِكُ المَتجَر — البَديلُ المَرفوضُ في §٤.
//
// **ولا واجِهَةَ ولا طَبَقَة** (القاعِدَة ١): دالّاتٌ نَقِيَّةٌ (أَيُّ المَدخَلَين، والسِياق، وبَوّابَةُ الهُوِيَّة) وخِدمَةٌ واحِدَةٌ لَها
// مُستَهلِكانِ في وَقتِ التَشغيل — الصَفحَةُ والنُقطَة. وشَرطُ استِخراجِ أَيِّ شَيءٍ مِنها ثالِثُ مُستَهلِك.

/// <summary>أَيُّ مَدخَلٍ يُرسَمُ لِلبائِع — واحِدٌ لا اثنان (‏ADR-033 §٢‑أ).</summary>
public enum VendorShippingInput
{
    /// <summary>لا مَدخَل: بيئَةُ OTO مُغلَقَة، أَو لا خِزانَةَ تَختِم، أَو لا تَعريفَ لِلقُدرَة — ولا «قَريباً» (القاعِدَة ١٢).</summary>
    None,

    /// <summary>البائِعُ يَلصِقُ رَمزَ تَحديثِه (النَموذَجُ ج) — حَتّى يُمنَحَ الرَمزُ السوقِيّ.</summary>
    Paste,

    /// <summary>البائِعُ يُسَجِّلُ حِسابَه عِندَ OTO مِن المَتجَرِ (النَموذَجُ ب) — حينَ يُضبَطُ الرَمزُ السوقِيّ. واللَصقُ مُطفَأ.</summary>
    Register,
}

/// <summary>
/// <para><b>قَراراتُ السَطحِ النَقِيَّة</b> — تَقرَؤُها الصَفحَةُ والنُقطَةُ والخِدمَةُ وخِدمَةُ شَحنِ الصَفَقاتِ مَعاً، فَلا يَنجَرِفُ شَكلان.</para>
/// </summary>
public static class VendorShippingSurface
{
    /// <summary><b>تَعريفُ حِسابِ الشَحنِ لِلبائِع</b> — الوَحيدُ في قُدرَةِ <c>delivery</c>، ويُقرَأُ بِالقُدرَةِ لا بِاسمِه: الكودُ لا
    /// يُسَمّي مُزَوِّداً (‏<c>ProviderCatalogTests</c>). و<c>null</c> حَيثُ لا تَعريف — فَلا مَدخَل.</summary>
    public static ProviderDefinition? Definition
        => ProviderCatalog.ForCapability(ProviderCapabilities.Delivery).SingleOrDefault();

    /// <summary>حَقلُ رَمزِ التَحديث — حَقلُ التَعريفِ مِن نَوعِ <c>secret_key</c>.</summary>
    public static string? RefreshField
        => Definition?.Credential.Fields.FirstOrDefault(f => f.Kind == CredentialKinds.SecretKey)?.Code;

    /// <summary>حَقلُ سِرِّ الوارِد — ما يُسَمّيه <c>webhook.verify</c> في التَعريف (مِن نَوعِ <c>issued_secret</c>).</summary>
    public static string? WebhookField => Definition?.Webhook?.Verify;

    /// <summary><b>سِياقُ قيمَةٍ في مَدخَلِ البائِع</b>: <c>tenant|delivery|field|listing_owner</c> (‏ADR-033 §٢‑ج‑٢) — يَبنيه الخاتِمُ
    /// هُنا ومَن يَفُكُّ في خِدمَةِ الشَحنِ بِالدالَّةِ نَفسِها.</summary>
    public static string AadFor(string tenantSlug, string fieldCode, Guid listingOwner)
        => ProviderVault.AadOf(tenantSlug, ProviderCapabilities.Delivery, fieldCode, listingOwner.ToString("D"));

    /// <summary>أَضُبِطَ الرَمزُ السوقِيُّ بِالبيئَة؟</summary>
    public static bool MarketplaceTokenConfigured(OtoOptions? options)
        => !string.IsNullOrWhiteSpace(options?.MarketplaceToken);

    /// <summary>
    /// <para><b>أَيُّ المَدخَلَين — دالَّةٌ نَقِيَّةٌ واحِدَةٌ تَقرَؤُها الشاشَةُ والنُقطَة</b> (‏ADR-033 §٢‑أ: «القرارُ "أيُّ المدخلَينِ يُرسَم"
    /// دالّةٌ نقيّةٌ تقرؤُها الشاشةُ والنقطةُ معًا — على سابقةِ <c>CheckoutPaymentPolicy.CardOffered</c>»): بيئَةٌ مَفتوحَةٌ وخِزانَةٌ تَختِمُ
    /// وتَعريفٌ لِلقُدرَة، ثُمَّ <see cref="VendorShippingInput.Register"/> حينَ يُضبَطُ الرَمزُ السوقِيُّ وإلّا <see cref="VendorShippingInput.Paste"/>.
    /// <b>فَاللَصقُ يُطفَأُ آلِيّاً حينَ يُمنَحُ الرَمزُ السوقِيّ</b>، والفاحِصُ النَصِّيُّ يَحمَرُّ إن ظَهَرَ المَدخَلانِ مَعاً.</para>
    /// </summary>
    public static VendorShippingInput InputFor(OtoOptions? options, ProviderVault? vault)
        => options is null || !OtoEnvironment.IsOpen(options.Environment)
           || vault is not { IsAvailable: true } || Definition is null
            ? VendorShippingInput.None
            : MarketplaceTokenConfigured(options) ? VendorShippingInput.Register : VendorShippingInput.Paste;

    /// <summary><b>عُنوانُ الوارِدِ الَّذي يُسَجَّلُ عِندَ OTO لِهذا المَتجَر</b> — أَصلُ الطَلَبِ (<c>PayPalOrderSurface.OriginFrom</c>)
    /// والمَسارُ الحَرفِيُّ نَفسُه (<c>OtoShippingEndpoints.OrderStatusPath</c>) — فَلا يُسَجَّلُ عُنوانٌ لا نُقطَةَ عَلَيه.</summary>
    public static string InboundUrl(string origin, string tenantSlug)
        => origin.TrimEnd('/') +
           global::ACommerce.Templates.Customer.Marketplace.Shipping.OtoShippingEndpoints.OrderStatusPath.Replace("{slug}", tenantSlug, StringComparison.Ordinal);
}

/// <summary>خَرقٌ في هُوِيَّةِ البائِع — نَفسُ شَكلِ <c>DealShipViolation</c> (القاعِدَة ٤).</summary>
public sealed record ListingVendorViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>سِجِلُّ السُلطَة: بائِعٌ في هذا المَتجَر</b> — مُستَخدِمٌ يَملِكُ إعلاناً واحِداً عَلى الأَقَلِّ غَيرَ مَحذوفٍ فيه
/// (<c>Attributes["owner_id"] == userId</c>، ‏ADR-033 §٢‑ج‑٣). <b>ولا يُبنى إلّا بِبَوّابَتِه</b>: المُنشِئُ داخِلِيٌّ و
/// <see cref="ListingVendorGate.Admit"/> بابُه الوَحيد، والخِدمَةُ تَطلُبُه في تَوقيعِها — نَسَقُ <c>DealShipper</c> و<c>OtoOrderStatusNotice</c>.</para>
/// </summary>
public sealed class ListingVendor
{
    internal ListingVendor(Guid userId, string name)
    {
        UserId = userId;
        Name = name;
    }

    /// <summary>البائِعُ — وهُوَ <c>listing_owner</c> في مَدخَلِه وفي صَفَقاتِه.</summary>
    public Guid UserId { get; }

    public string Name { get; }
}

/// <summary>ما خَرَجَ مِن البَوّابَة: بائِعٌ أَو خَرقٌ — واحِدٌ لا اثنان.</summary>
public sealed record ListingVendorAdmission(ListingVendor? Vendor, ListingVendorViolation? Violation);

/// <summary>
/// <para><b>بَوّابَةُ هُوِيَّةِ البائِعِ — دالَّةٌ نَقِيَّةٌ بِاسمِها</b> (‏ADR-033 §٢‑ج‑٣: «تُكتَبُ حارسًا مسمًّى يُعلَنُ في توقيعِ نقطتَي
/// الخطوتَين 7 و8»). <c>RequireListingOwner</c> القائِمُ يَسأَلُ عَن مِلكِيَّةِ إعلانٍ بِعَينِه في المَسار (<c>{id}</c>)، وهذا يَسأَلُ
/// «أَيَملِكُ إعلاناً في المَتجَر؟» — سُؤالٌ آخَرُ فَحارِسٌ آخَر. والعَدُّ تَقرَؤُه النُقطَةُ (<c>AccountQueries.MyListingsAsync</c>) قَبلَها.</para>
/// <para><b>خَرقٌ أَوَّلٌ لا قائِمَة</b> — عائِلَةُ بَوّاباتِ التَخويلِ عَلى حالَةٍ مَخزونَة (سِجِلُّ المالِكِ «أ» S8·١): الهُوِيَّةُ ثُمَّ المِلكِيَّة.</para>
/// </summary>
public static class ListingVendorGate
{
    /// <summary>لا جَلسَة.</summary>
    public const string SignedOut = "vendor_signed_out";

    /// <summary>لا إعلانَ غَيرَ مَحذوفٍ لَه في هذا المَتجَر — فَلا حِسابَ شَحنٍ لَه هُنا.</summary>
    public const string OwnsNoListing = "vendor_owns_no_listing";

    public static readonly IReadOnlyList<string> All = new[] { SignedOut, OwnsNoListing };

    public static ListingVendorAdmission Admit(Guid? userId, string? userName, int ownedListings)
    {
        if (userId is not { } id || id == Guid.Empty)
            return new(null, new(SignedOut, "لا جَلسَة — البائِعُ يُعرَفُ بِجَلسَتِه في المَتجَر."));

        if (ownedListings <= 0)
            return new(null, new(OwnsNoListing, "لا إعلانَ لِهذا المُستَخدِمِ في المَتجَر — فَلَيسَ بائِعاً فيه."));

        return new(new ListingVendor(id, userName ?? ""), null);
    }

    public static bool IsAllowed(Guid? userId, int ownedListings) => Admit(userId, null, ownedListings).Vendor is not null;
}

/// <summary>ما يَراه البائِعُ مِن مَدخَلِه — ذَيلُ الرَمزِ وحالُ تَسجيلِ الوارِد، لا سِرَّ.</summary>
public sealed record VendorShippingView(string? TokenTail, string? WebhookId, string? WebhookRefusal);

/// <summary>نَتيجَةُ الحِفظ: رَمزُ رَفضٍ أَو المَدخَلُ قَبلُ وبَعدُ (لِسَطرِ التَدقيقِ المُعَتَّم).</summary>
public sealed record VendorShippingResult(string? Code, TenantProviderBinding? Before, TenantProviderBinding? After)
{
    public bool Ok => Code is null;
}

/// <summary>
/// <para><b>حِفظُ رَمزِ البائِعِ وتَسجيلُ وارِدِه</b> — الخِدمَةُ الواحِدَة. التَرتيب: السَطحُ مَفتوح ← خِزانَةٌ تَختِم ← رَمزٌ غَيرُ فارِغ ←
/// يُختَمُ الرَمزُ ويُحفَظُ المَدخَلُ <b>بِسِرِّ الوارِدِ القائِمِ وتَسجيلِه</b> ← يُسَجَّلُ (أَو يُحَدَّثُ) الوارِدُ عِندَ OTO بِسِرٍّ جَديدٍ وبِرَمزِ
/// البائِع ← <b>ولا يُستَبدَلُ السِرُّ المَختومُ إلّا حينَ تَقبَلُه OTO</b>، وإلّا يُكتَبُ رَمزُ فَشَلِها ويَبقى القَديمُ — فَلا يَفتَرِقُ ما عِندَنا
/// عَمّا يُوَقِّعُ بِه OTO.</para>
/// <para><b>وما لا يُغلِقُه — يُقالُ ولا يُبتلَع</b>: انقِطاعٌ بَينَ قَبولِ OTO وحِفظِ السِرِّ الجَديدِ يَترُكُها تُوَقِّعُ بِسِرٍّ لا نَملِكُه، فَتُرَدُّ
/// إشعاراتُها بِـ<c>signature_mismatch</c> حَتّى يَحفَظَ البائِعُ رَمزَه مَرَّةً أُخرى — والرَدُّ مَطبوعٌ في اللوغِ بِرَمزِه.</para>
/// </summary>
public sealed class VendorShippingService
{
    /// <summary>السَطحُ مُغلَقٌ هُنا — بيئَةُ OTO مُغلَقَة، أَو الرَمزُ السوقِيُّ مَضبوطٌ فَالمَدخَلُ مَسارُ التَسجيل.</summary>
    public const string PasteClosed = "vendor_paste_closed";

    /// <summary>حَقلُ الرَمزِ فارِغ.</summary>
    public const string TokenMissing = "vendor_token_missing";

    /// <summary>رَمزُ الخِزانَةِ نَفسُه — لا خِزانَةَ تَختِمُ، فَلا يُحفَظُ سِرٌّ.</summary>
    public const string VaultUnavailable = ProviderVault.Unavailable;

    public static readonly IReadOnlyList<string> Codes = new[] { PasteClosed, TokenMissing, VaultUnavailable };

    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    private readonly OtoClient _oto;
    private readonly OtoOptions _opts;
    private readonly ILogger<VendorShippingService> _log;

    public VendorShippingService(TenantProviderService providers, ProviderVault vault, OtoClient oto,
        IOptions<OtoOptions> opts, ILogger<VendorShippingService>? log = null)
    {
        _providers = providers;
        _vault = vault;
        _oto = oto;
        _opts = opts.Value;
        _log = log ?? NullLogger<VendorShippingService>.Instance;
    }

    /// <summary>أَيُّ مَدخَلٍ يُرسَمُ — <see cref="VendorShippingSurface.InputFor"/> بِتَهيئَةِ هذِه النُسخَةِ وخِزانَتِها.</summary>
    public VendorShippingInput Input => VendorShippingSurface.InputFor(_opts, _vault);

    /// <summary>مَدخَلُ البائِعِ لِلعَرض — ذَيلُ رَمزِه المَحفوظُ عِندَ الخَتم (بِلا فَكّ) وحالُ وارِدِه، أَو <c>null</c>.</summary>
    public async Task<VendorShippingView?> ViewAsync(string tenantSlug, Guid listingOwner, CancellationToken ct = default)
    {
        var entry = await _providers.VendorEntryAsync(tenantSlug, ProviderCapabilities.Delivery, listingOwner, ct);
        if (entry is not { IsActive: true }) return null;
        var tail = VendorShippingSurface.RefreshField is { } f && entry.Values.TryGetValue(f, out var v) ? v.Tail4 : null;
        return new(tail, entry.WebhookId, entry.WebhookRefusal);
    }

    public async Task<VendorShippingResult> SaveTokenAsync(
        string tenantSlug, ListingVendor vendor, string? refreshToken, string inboundUrl, CancellationToken ct = default)
    {
        if (Input != VendorShippingInput.Paste)
            return new(_vault.IsAvailable ? PasteClosed : VaultUnavailable, null, null);

        var token = (refreshToken ?? "").Trim();
        if (token.Length == 0) return new(TokenMissing, null, null);

        var def = VendorShippingSurface.Definition!;
        var refreshField = def.Credential.Fields.First(f => f.Kind == CredentialKinds.SecretKey);
        var webhookField = def.Credential.Fields.First(f => f.Code == def.Webhook!.Verify);
        var owner = vendor.UserId;

        var before = await _providers.VendorEntryAsync(tenantSlug, ProviderCapabilities.Delivery, owner, ct);
        var kept = before is { IsActive: true } ? before : null;

        // ١) الرَمزُ مَختوماً، وسِرُّ الوارِدِ وتَسجيلُه كَما كانا — فَإن تَعَثَّرَ ما بَعدَه بَقِيَ الوارِدُ القائِمُ يُتَحَقَّقُ مِنه.
        var values = new Dictionary<string, StoredValue>(StringComparer.Ordinal)
        {
            [refreshField.Code] = StoredValue.Sealed(_vault, refreshField.Kind, token,
                VendorShippingSurface.AadFor(tenantSlug, refreshField.Code, owner)),
        };
        if (kept is not null && kept.Values.TryGetValue(webhookField.Code, out var oldSecret))
            values[webhookField.Code] = oldSecret;
        var by = owner.ToString("D");
        var saved = await _providers.BindVendorAsync(tenantSlug, ProviderCapabilities.Delivery, owner, def.Slug,
            values, kept?.WebhookId, kept?.WebhookRefusal, by, ct);

        // ٢) الوارِدُ بِسِرٍّ جَديدٍ وبِرَمزِ البائِعِ نَفسِه: يُحَدَّثُ تَسجيلُه القائِمُ، وتَسجيلٌ يَرُدُّه حِسابٌ آخَرُ يُنشَأُ مِن جَديد.
        var secret = OtoWebhookSignature.NewSecret();
        var registered = await _oto.RegisterWebhookAsync(token, inboundUrl, secret, kept?.WebhookId, ct);
        if (!registered.Ok && kept?.WebhookId is not null && registered.Failure!.Code == OtoFailures.Refused)
            registered = await _oto.RegisterWebhookAsync(token, inboundUrl, secret, null, ct);

        // ٣) السِرُّ يُستَبدَلُ حينَ تَقبَلُه OTO وَحدَه؛ وإلّا يُكتَبُ رَمزُ فَشَلِها ويَبقى القَديم.
        if (registered.Value is { } webhookId)
        {
            values[webhookField.Code] = StoredValue.Sealed(_vault, webhookField.Kind, secret,
                VendorShippingSurface.AadFor(tenantSlug, webhookField.Code, owner));
            saved = await _providers.BindVendorAsync(tenantSlug, ProviderCapabilities.Delivery, owner, def.Slug,
                values, webhookId, null, by, ct);
        }
        else
        {
            saved = await _providers.BindVendorAsync(tenantSlug, ProviderCapabilities.Delivery, owner, def.Slug,
                values, kept?.WebhookId, registered.Failure!.Code, by, ct);
        }

        _log.LogInformation("[OTO] vendor {Slug}/{Owner} ← saved (webhook: {Webhook})",
            tenantSlug, by, saved.WebhookRefusal ?? (saved.WebhookId is null ? "—" : "registered"));
        return new(null, before, saved);
    }
}
