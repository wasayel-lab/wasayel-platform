using System.Security.Cryptography;
using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using Microsoft.AspNetCore.Http;
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
//
// **ومَسارُ التَسجيلِ (‏ADR-033 الخُطوَة 8، ‏2026-10-12) في المِلَفِّ نَفسِه** — المَدخَلُ الآخَرُ لِلسَطحِ نَفسِه: استِمارَةٌ يَملَؤُها البائِعُ
// عَن نَفسِه ومُدَقِّقُها (مَعجَمٌ مُغلَق)، و`RegisterAsync` الَّتي تُنادي «Marketplace › Register» بِالاعتِمادِ السوقِيِّ وتَختِمُ رَمزَ حِسابِه
// الجَديدَ في مَدخَلِه بِالمَسارِ الَّذي يَختِمُ بِه اللَصق (`ConnectAsync`) — مَسارٌ واحِدٌ لِلخَتمِ وتَسجيلِ الوارِد، لا اثنان.

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

// ═══ جِهَةُ تَواصُلِ البائِعِ المُتَحَقَّقُ مِنها — بابُ التَسجيلِ وَحدَه (مُراجَعَةُ تَيّارِ S8، الجولة 2، ‏2026-10-12؛ ‏ADR-033 §٧‑ف) ═══════
//
// الخُطوَة 8 تُسقِطُ حارِسَ المُديرِ عَن بابِ التَسجيلِ لِأَنّ «بَياناتُ التَسجيلِ بَياناتُ البائِعِ فَالفِعلُ فِعلُه» — وكانَ الكودُ يَقولُها ولا يُلزِمُها:
// بَريدٌ وجَوّالٌ يَكتُبُهُما أَيُّ بائِعٍ عَن أَيِّ أَحَدٍ يُرسَلانِ إلى OTO بِالرَمزِ السوقِيّ (تَوصيفُه `dcfebfe`). **فَالمُرسَلُ مِن جِهَتَيِ التَواصُلِ ما
// تَحَقَّقنا أَنَّه لِصاحِبِ الجَلسَة**: البَريدُ أَو الجَوّالُ الَّذي دَخَلَ بِه المَتجَرَ بِرَمزٍ أُرسِلَ إلَيه (`User.EmailVerified`/`PhoneVerified` — يَرفَعُهُما
// `AuthHandlers` عِندَ نَجاحِ الرَمزِ وَحدَه). والدُخولُ بِقَناةٍ واحِدَة، فَلِلمُستَخدِمِ جِهَةٌ مُتَحَقَّقٌ مِنها واحِدَةٌ في الغالِب: تُقفَلُ هِيَ، والأُخرى
// يَكتُبُها — وما يَبقى مَفتوحاً يُقالُ في ‏ADR-033 §٧‑ف وسِجِلِّ المالِكِ «ب» S8·٢١.

/// <summary>
/// <para><b>سِجِلُّ السُلطَة: بائِعٌ يُسَجَّلُ عِندَ OTO بِجِهَةِ تَواصُلٍ تَحَقَّقنا أَنَّها لَه.</b> لا يُبنى إلّا بِبَوّابَتِه
/// (<see cref="VendorContactGate.Admit"/>)، و<see cref="VendorShippingService.RegisterAsync"/> تَطلُبُه في تَوقيعِها — نَسَقُ <see cref="ListingVendor"/>.</para>
/// <para><b>ولا يُطبَعُ</b>: فيه بَياناتُ تَواصُل — <c>ToString</c> بِلاها.</para>
/// </summary>
public sealed class VendorRegistrant
{
    internal VendorRegistrant(ListingVendor vendor, string? verifiedEmail, string? verifiedMobile)
    {
        Vendor = vendor;
        VerifiedEmail = verifiedEmail;
        VerifiedMobile = verifiedMobile;
    }

    public ListingVendor Vendor { get; }

    /// <summary>بَريدُه المُتَحَقَّقُ مِنه مُطَبَّعاً بِدالَّةِ الدُخولِ نَفسِها (<c>EmailAddress.Normalize</c>) — أَو <c>null</c>.</summary>
    public string? VerifiedEmail { get; }

    /// <summary>جَوّالُه المُتَحَقَّقُ مِنه أَرقاماً كَما دَخَلَ بِه — بِلا تَحويلِ صيغَة (‏ADR-033 §٧‑س.5) — أَو <c>null</c>.</summary>
    public string? VerifiedMobile { get; }

    /// <summary>
    /// <para><b>الاستِمارَةُ كَما تُدَقَّقُ وتُرسَل — دالَّةٌ نَقِيَّة</b>: كُلُّ جِهَةِ تَواصُلٍ مُتَحَقَّقٍ مِنها تَحِلُّ مَحَلَّ ما في خانَتِها، فَما كُتِبَ فيها لا
    /// يُقرَأُ أَصلاً (والصَفحَةُ تَرسُمُها مَملوءَةً مُقفَلَة)؛ والأُخرى كَما كَتَبَها البائِع. بَوّابَةٌ تَقرَأُ حالَةً مَخزونَةً وتَحويلٌ نَقِيٌّ لا يَتَشارَكانِ أُنبوباً
    /// (القاعِدَة ٧): البَوّابَةُ تَبني السِجِلَّ، وهذِه تَقرَؤُه.</para>
    /// </summary>
    public VendorRegistrationForm Bind(VendorRegistrationForm form)
        => form with { Email = VerifiedEmail ?? form.Email, Mobile = VerifiedMobile ?? form.Mobile };

    public override string ToString()
        => $"VendorRegistrant(email={(VerifiedEmail is null ? "typed" : "verified")}, mobile={(VerifiedMobile is null ? "typed" : "verified")})";
}

/// <summary>ما خَرَجَ مِن بَوّابَةِ جِهَةِ التَواصُل: سِجِلٌّ أَو خَرقٌ — واحِدٌ لا اثنان.</summary>
public sealed record VendorRegistrantAdmission(VendorRegistrant? Registrant, ListingVendorViolation? Violation);

/// <summary>
/// <para><b>بَوّابَةُ جِهَةِ التَواصُلِ المُتَحَقَّقِ مِنها — دالَّةٌ نَقِيَّةٌ بِاسمِها</b>، تَلي <see cref="ListingVendorGate"/> في نُقطَةِ التَسجيلِ
/// وتَسبِقُ قِراءَةَ الاستِمارَة (القاعِدَة ٦)، وتَقرَؤُها الصَفحَةُ نَفسُها فَلا تَرسُمُ استِمارَةً تَرُدُّها. وثيقَةُ المُستَخدِمِ يَقرَؤُها المُنادي
/// (<c>AccountQueries.ProfileAsync</c>) ويُمَرِّرُها. <b>خَرقٌ أَوَّلٌ لا قائِمَة</b> — عائِلَةُ بَوّابات التَخويلِ عَلى حالَةٍ مَخزونَة (سِجِلُّ المالِكِ «أ» S8·١).</para>
/// </summary>
public static class VendorContactGate
{
    /// <summary>لا بَريدَ ولا جَوّالَ تَحَقَّقنا أَنَّه لِلبائِع — أَو لا وَثيقَةَ لَه، أَو لِغَيرِه، أَو مَحذوفَة.</summary>
    public const string ContactUnverified = "vendor_contact_unverified";

    public static readonly IReadOnlyList<string> All = new[] { ContactUnverified };

    public static VendorRegistrantAdmission Admit(ListingVendor vendor, ACommerce.Kit.Auth.User? account)
    {
        if (account is null || account.Id != vendor.UserId || account.DeletedAt is not null)
            return new(null, new(ContactUnverified, "لا حِسابَ لِلبائِعِ في المَتجَرِ يُعرَفُ بِه — فَلا جِهَةَ تَواصُلٍ مُتَحَقَّقٌ مِنها."));

        var email = account.EmailVerified && ACommerce.Kit.Auth.EmailAddress.IsValid(account.Email)
            ? ACommerce.Kit.Auth.EmailAddress.Normalize(account.Email) : null;
        var mobile = account.PhoneVerified ? DigitsOrNull(account.Phone) : null;
        if (email is null && mobile is null)
            return new(null, new(ContactUnverified, "التَسجيلُ عِندَ OTO بِبَريدٍ أَو جَوّالٍ تَحَقَّقنا أَنَّه لِلبائِع — ولا شَيءَ مِنهُما في حِسابِه."));

        return new(new VendorRegistrant(vendor, email, mobile), null);
    }

    public static bool IsAllowed(ListingVendor vendor, ACommerce.Kit.Auth.User? account) => Admit(vendor, account).Registrant is not null;

    /// <summary>الجَوّالُ أَرقاماً — بِلا مَسافاتٍ ولا «+» في أَوَّلِه، كَما يُرسَلُ (<see cref="VendorRegistrationForm.MobileDigits"/>) — أَو <c>null</c>
    /// لِما لَيسَ أَرقاماً (مُعَرِّفُ نَفاذ <c>NID-…</c> مَثَلاً، ولا يُرفَعُ لَه <c>PhoneVerified</c> أَصلاً).</summary>
    private static string? DigitsOrNull(string? phone)
    {
        var digits = (phone ?? "").Replace(" ", "", StringComparison.Ordinal).TrimStart('+');
        return digits.Length > 0 && digits.All(c => c is >= '0' and <= '9') ? digits : null;
    }
}

// ═══ استِمارَةُ التَسجيلِ عِندَ OTO ومُدَقِّقُها (‏ADR-033 الخُطوَة 8، تَيّارُ S8، ‏2026-10-12) ═══════════════════════

/// <summary>
/// <para><b>ما يَملَؤُه البائِعُ عَن نَفسِه لِيُفتَحَ لَه حِسابُ شَحنٍ عِندَ OTO</b> — حُقولُ «Marketplace › Register» لِلبائِعِ السُعودِيِّ غَيرِ
/// المُسَجَّلِ في الضَريبَة (‏<c>24-SHIPPING.md</c> §٣‑أ: «تاجرٌ غيرُ مسجَّلٍ في الضريبة يُسجَّل باسمٍ وبريدٍ وجوّالٍ ومدينةٍ وعنوانِ فوترة»)،
/// وحُقولُ الضَريبَةِ تُقرَأُ وتُدَقَّقُ وتُرسَلُ <b>حينَ <see cref="IsVatRegistered"/> وَحدَه</b>. وموافَقَتُه عَلى إرسالِها إلى OTO
/// (‏ADR-033 §٣‑١٠: «تُكتَبُ من القاموسِ مع المستهلكِ الأوّل») حَقلٌ مِنها لا افتِراض.</para>
/// <para><b>ولا يُطبَعُ</b>: فيه بَياناتٌ شَخصِيَّة — <c>ToString</c> بِلاها.</para>
/// </summary>
public sealed record VendorRegistrationForm(
    string FirstName, string LastName, string Email, string Mobile, string City, string BillingAddress,
    bool IsVatRegistered, string CompanyName, string CrNumber, string VatNumber, string District,
    string ZipCode, string BuildingNo, string StreetName, bool Consent)
{
    /// <summary>البَلَد — «‏Country tag (e.g., “TR”, “SA”)»: بائِعو المَتجَرِ في السُعودِيَّة (‏ADR-033 §٢‑ج؛ سِجِلُّ المالِكِ «أ» S8·١٢).</summary>
    public const string SaudiCountryTag = "SA";

    // ─── أَسماءُ الحُقولِ في الاستِمارَة — مَوضِعٌ واحِدٌ تَقرَؤُه الصَفحَةُ والقارِئ ─────────
    public const string FirstNameField = "first_name";
    public const string LastNameField = "last_name";
    public const string EmailField = "email";
    public const string MobileField = "mobile";
    public const string CityField = "city";
    public const string BillingAddressField = "billing_address";
    public const string IsVatField = "is_vat_registered";
    public const string CompanyNameField = "company_name";
    public const string CrNumberField = "cr_number";
    public const string VatNumberField = "vat_number";
    public const string DistrictField = "district";
    public const string ZipCodeField = "zip_code";
    public const string BuildingNoField = "building_no";
    public const string StreetNameField = "street_name";
    public const string ConsentField = "consent";

    /// <summary>قيمَةُ مُرَبَّعِ الاختيارِ حينَ يُعَلَّم — مَكتوبَةٌ في الاستِمارَةِ صَراحَةً لا افتِراضُ المُتَصَفِّح.</summary>
    public const string Checked = "true";

    /// <summary>الاستِمارَةُ مِن الطَلَب — كُلُّ حَقلٍ مُشَذَّب، والأَرقامُ بِأَرقامٍ لاتينِيَّةٍ (<see cref="LatinDigits"/>).</summary>
    public static VendorRegistrationForm Read(IFormCollection f) => new(
        Text(f, FirstNameField), Text(f, LastNameField), Text(f, EmailField), LatinDigits(Text(f, MobileField)),
        Text(f, CityField), Text(f, BillingAddressField), f[IsVatField].ToString() == Checked,
        Text(f, CompanyNameField), LatinDigits(Text(f, CrNumberField)), LatinDigits(Text(f, VatNumberField)),
        Text(f, DistrictField), LatinDigits(Text(f, ZipCodeField)), LatinDigits(Text(f, BuildingNoField)),
        Text(f, StreetNameField), f[ConsentField].ToString() == Checked);

    private static string Text(IFormCollection f, string name) => f[name].ToString().Trim();

    /// <summary>
    /// <para><b>الأَرقامُ العَرَبِيَّةُ الهِندِيَّةُ (٠–٩ و۰–۹) لاتينِيَّة</b> — لَوحَةُ مَفاتيحِ الجَوّالِ العَرَبِيَّةُ تَكتُبُها، وOTO تَطلُبُ أَرقاماً
    /// («‏10 digits»). تَحويلُ رَسمٍ لا تَخمينُ قيمَة: كُلُّ رَقَمٍ يَبقى هُوَ.</para>
    /// </summary>
    public static string LatinDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            chars[i] = chars[i] switch
            {
                >= '\u0660' and <= '\u0669' => (char)('0' + (chars[i] - '\u0660')),
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (chars[i] - '\u06F0')),
                _ => chars[i],
            };
        return new string(chars);
    }

    /// <summary>الجَوّالُ كَما يُرسَل: بِلا مَسافاتٍ وبِلا «+» في أَوَّلِه — ولا تَحويلَ لِصيغَتِه (OTO «‏validated per country»؛ «ب» S8·١٣).</summary>
    public string MobileDigits => Mobile.Replace(" ", "", StringComparison.Ordinal).TrimStart('+');

    /// <summary>جِسمُ «Register» — وحُقولُ الضَريبَةِ <c>null</c> لِغَيرِ المُسَجَّلِ فيها، فَلا تُرسَلُ ولَو مُلِئَت.</summary>
    public OtoVendorRegistration ToOto() => new(
        SaudiCountryTag, City, ACommerce.Kit.Auth.EmailAddress.Normalize(Email), MobileDigits, FirstName, LastName,
        IsVatRegistered, BillingAddress,
        IsVatRegistered ? CompanyName : null, IsVatRegistered ? VatNumber : null, IsVatRegistered ? CrNumber : null,
        IsVatRegistered ? District : null, IsVatRegistered ? ZipCode : null, IsVatRegistered ? BuildingNo : null,
        IsVatRegistered ? StreetName : null);

    public override string ToString() => $"VendorRegistrationForm(vat={IsVatRegistered}, consent={Consent})";
}

/// <summary>خَرقٌ في استِمارَةِ التَسجيل — شَكلُ القاعِدَة ٤.</summary>
public sealed record VendorRegistrationViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مُدَقِّقُ استِمارَةِ التَسجيل — مَعجَمٌ مُغلَقٌ مِن أَربَعَةَ عَشَرَ رَمزاً</b>، وقَواعِدُه مِن جَدوَلِ الوَثيقَةِ لا مِن ذاكِرَة:
/// الاسمانِ «‏max 30 chars»، والبَريدُ بِفاحِصِ المِنَصَّةِ القائِم (<c>ACommerce.Kit.Auth.EmailAddress</c> — لا ثانٍ)، والجَوّالُ أَرقاماً،
/// وحُقولُ الضَريبَةِ «‏CRNumber (10 digits), vatNumber (15 digits, starts with ‘3’), district, zipCode (5 digits), buildingNo (4
/// digits), streetName required» و<c>companyName</c> «‏Required if isVatRegistered is true» — <b>ولا تُسأَلُ إلّا حينَ يُعَلِّمُ البائِعُ
/// أَنَّه مُسَجَّلٌ في الضَريبَة</b>. ولا حَدَّ لِما لَم تَكتُب الوَثيقَةُ حَدَّه (القاعِدَة ١٦).</para>
/// <para>والقائِمَةُ بِتَرتيبِ الحُقولِ في الاستِمارَة، والمُوافَقَةُ آخِرُها — والنُقطَةُ تَرُدُّ بِأَوَّلِها.</para>
/// </summary>
public static class VendorRegistrationValidator
{
    public const string FirstNameInvalid = "first_name_invalid";
    public const string LastNameInvalid = "last_name_invalid";
    public const string EmailInvalid = "email_invalid";
    public const string MobileInvalid = "mobile_invalid";
    public const string CityMissing = "city_missing";
    public const string BillingAddressMissing = "billing_address_missing";
    public const string CompanyNameMissing = "company_name_missing";
    public const string CrNumberInvalid = "cr_number_invalid";
    public const string VatNumberInvalid = "vat_number_invalid";
    public const string DistrictMissing = "district_missing";
    public const string ZipCodeInvalid = "zip_code_invalid";
    public const string BuildingNoInvalid = "building_no_invalid";
    public const string StreetNameMissing = "street_name_missing";
    public const string ConsentMissing = "consent_missing";

    public static readonly IReadOnlyList<string> All = new[]
    {
        FirstNameInvalid, LastNameInvalid, EmailInvalid, MobileInvalid, CityMissing, BillingAddressMissing,
        CompanyNameMissing, CrNumberInvalid, VatNumberInvalid, DistrictMissing, ZipCodeInvalid, BuildingNoInvalid,
        StreetNameMissing, ConsentMissing,
    };

    /// <summary>«‏User’s first name (max 30 chars)» — والاسمُ الأَخيرُ كَذلك.</summary>
    public const int NameMaxLength = 30;

    public static IReadOnlyList<VendorRegistrationViolation> Validate(VendorRegistrationForm f)
    {
        var v = new List<VendorRegistrationViolation>();
        if (f.FirstName.Length is 0 or > NameMaxLength) v.Add(new(FirstNameInvalid, "الاسمُ الأَوَّلُ مَطلوبٌ وأَقصاه ثَلاثونَ مِحرَفاً."));
        if (f.LastName.Length is 0 or > NameMaxLength) v.Add(new(LastNameInvalid, "اسمُ العائِلَةِ مَطلوبٌ وأَقصاه ثَلاثونَ مِحرَفاً."));
        if (!ACommerce.Kit.Auth.EmailAddress.IsValid(f.Email)) v.Add(new(EmailInvalid, "البَريدُ غائِبٌ أَو لَيسَ بَريداً."));
        if (!Digits(f.MobileDigits)) v.Add(new(MobileInvalid, "الجَوّالُ غائِبٌ أَو فيه غَيرُ الأَرقام."));
        if (f.City.Length == 0) v.Add(new(CityMissing, "المَدينَةُ مَطلوبَة."));
        if (f.BillingAddress.Length == 0) v.Add(new(BillingAddressMissing, "عُنوانُ الفَوتَرَةِ مَطلوب."));

        if (f.IsVatRegistered)
        {
            if (f.CompanyName.Length == 0) v.Add(new(CompanyNameMissing, "اسمُ المَنشَأَةِ مَطلوبٌ لِلمُسَجَّلِ في الضَريبَة."));
            if (!Digits(f.CrNumber, 10)) v.Add(new(CrNumberInvalid, "رَقَمُ السِجِلِّ التِجارِيِّ عَشَرَةُ أَرقام."));
            if (!Digits(f.VatNumber, 15) || f.VatNumber[0] != '3') v.Add(new(VatNumberInvalid, "الرَقَمُ الضَريبِيُّ خَمسَةَ عَشَرَ رَقَماً أَوَّلُها ٣."));
            if (f.District.Length == 0) v.Add(new(DistrictMissing, "الحَيُّ مَطلوبٌ لِلمُسَجَّلِ في الضَريبَة."));
            if (!Digits(f.ZipCode, 5)) v.Add(new(ZipCodeInvalid, "الرَمزُ البَريدِيُّ خَمسَةُ أَرقام."));
            if (!Digits(f.BuildingNo, 4)) v.Add(new(BuildingNoInvalid, "رَقَمُ المَبنى أَربَعَةُ أَرقام."));
            if (f.StreetName.Length == 0) v.Add(new(StreetNameMissing, "اسمُ الشارِعِ مَطلوبٌ لِلمُسَجَّلِ في الضَريبَة."));
        }

        if (!f.Consent) v.Add(new(ConsentMissing, "لا يُرسَلُ إلى OTO شَيءٌ بِلا مُوافَقَةِ صاحِبِه."));
        return v;
    }

    public static bool IsValid(VendorRegistrationForm f) => Validate(f).Count == 0;

    /// <summary>أَرقامٌ لاتينِيَّةٌ وَحدَها — بِطولٍ مُعَيَّنٍ أَو بِأَيِّ طولٍ غَيرِ صِفر.</summary>
    private static bool Digits(string s, int? length = null)
        => s.Length > 0 && (length is null || s.Length == length) && s.All(c => c is >= '0' and <= '9');
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
/// <para><b>ومَسارُ التَسجيل</b> (<see cref="RegisterAsync"/>، ‏ADR-033 الخُطوَة 8، ‏2026-10-12): المَدخَلُ الآخَرُ حينَ يُضبَطُ الرَمزُ السوقِيّ —
/// والخَتمُ وتَسجيلُ الوارِدِ بَعدَه بِالمَسارِ نَفسِه (<c>ConnectAsync</c>).</para>
/// </summary>
public sealed class VendorShippingService
{
    /// <summary>السَطحُ مُغلَقٌ هُنا — بيئَةُ OTO مُغلَقَة، أَو الرَمزُ السوقِيُّ مَضبوطٌ فَالمَدخَلُ مَسارُ التَسجيل.</summary>
    public const string PasteClosed = "vendor_paste_closed";

    /// <summary>حَقلُ الرَمزِ فارِغ.</summary>
    public const string TokenMissing = "vendor_token_missing";

    /// <summary>رَمزُ الخِزانَةِ نَفسُه — لا خِزانَةَ تَختِمُ، فَلا يُحفَظُ سِرٌّ.</summary>
    public const string VaultUnavailable = ProviderVault.Unavailable;

    /// <summary>مَسارُ التَسجيلِ مُغلَقٌ هُنا — لا رَمزَ سوقِيّاً بِالبيئَة، أَو بيئَةُ OTO مُغلَقَة (‏2026-10-12، الخُطوَة 8).</summary>
    public const string RegisterClosed = "vendor_register_closed";

    /// <summary>مَدخَلُ البائِعِ القائِمُ لا يُفَكُّ هُنا (مِفتاحُ الخِزانَةِ تَبَدَّلَ، أَو سِياقٌ لا يُطابِق) — فَلا يُعادُ بِه تَسجيلُ الوارِد.</summary>
    public const string EntryUnreadable = "vendor_entry_unreadable";

    /// <summary>بَلَغَ البائِعُ حَدَّ مُحاوَلاتِ «Register» في السّاعَة — حِصَّةُ الإرسالِ القائِمَةُ نَفسُها (مُراجَعَةُ تَيّارِ S8، الجولة 2، ‏2026-10-12).</summary>
    public const string RegisterRateLimited = "vendor_register_rate_limited";

    public static readonly IReadOnlyList<string> Codes = new[] { PasteClosed, TokenMissing, VaultUnavailable, RegisterClosed, EntryUnreadable, RegisterRateLimited };

    /// <summary>
    /// <para><b>مِفتاحُ حَدِّ المُحاوَلاتِ في حِصَّةِ الإرسالِ القائِمَة</b> (<c>AuthHandlers.TryConsumeSendQuota</c>، «‏tenant|subject»): بائِعٌ في مَتجَر.
    /// والحِصَّةُ أُنبوبُ المِنَصَّةِ لِما يُكَلِّفُ نِداءً خارِجِيّاً بِكُلِّ مُحاوَلَة — عَشرٌ في السّاعَة، يُناديها بابُ الاستوديو مِن خارِجِ مِلَفِّها كَهذا
    /// (القاعِدَة ٨) — فَلا رَقَمَ جَديد (القاعِدَة ١٦). <b>حَدُّ مُحاوَلاتٍ لا حِصَّةُ باقَة</b>: لا يُعَدُّ عَلى باقَةِ المُستَأجِرِ ولا يَمَسُّ الاستِحقاقات.</para>
    /// </summary>
    public static string RegisterAttemptKey(string tenantSlug, Guid listingOwner) => $"{tenantSlug}|oto-register:{listingOwner:D}";

    /// <summary>فِعلُ سَطرِ الأَثَرِ قَبلَ نِداءِ التَسجيل — يَكتُبُه كاتِبُ الأَثَرِ القائِم (‏ADR-033 الخُطوَة 8).</summary>
    public const string RegisterAuditAction = "provider.vendor.register";

    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    private readonly OtoClient _oto;
    private readonly OtoOptions _opts;
    private readonly Audit.AuditWriter _audit;
    private readonly Api.ApiKeyRevealStore _reveal;
    private readonly ILogger<VendorShippingService> _log;

    /// <summary>و<paramref name="audit"/> و<paramref name="reveal"/> مُنذُ الخُطوَة 8 (‏2026-10-12): كاتِبُ الأَثَرِ القائِمُ قَبلَ نِداءِ التَسجيل،
    /// ووِعاءُ العَرضِ مَرَّةً القائِمُ لِرابِطِ التَفعيل — أُنبوبانِ قائِمانِ لا ثالِث (القاعِدَة ٨).</summary>
    public VendorShippingService(TenantProviderService providers, ProviderVault vault, OtoClient oto,
        IOptions<OtoOptions> opts, Audit.AuditWriter audit, Api.ApiKeyRevealStore reveal,
        ILogger<VendorShippingService>? log = null)
    {
        _providers = providers;
        _vault = vault;
        _oto = oto;
        _opts = opts.Value;
        _audit = audit;
        _reveal = reveal;
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

        var before = await _providers.VendorEntryAsync(tenantSlug, ProviderCapabilities.Delivery, vendor.UserId, ct);
        var saved = await ConnectAsync(tenantSlug, vendor.UserId, token, inboundUrl, before is { IsActive: true } ? before : null, ct);

        _log.LogInformation("[OTO] vendor {Slug}/{Owner} ← saved (webhook: {Webhook})",
            tenantSlug, vendor.UserId.ToString("D"), saved.WebhookRefusal ?? (saved.WebhookId is null ? "—" : "registered"));
        return new(null, before, saved);
    }

    /// <summary>
    /// <para><b>يَفتَحُ لِلبائِعِ حِسابَ شَحنٍ عِندَ OTO بِالاعتِمادِ السوقِيّ — أَو يُعيدُ تَسجيلَ وارِدِ حِسابِه القائِم</b> (‏ADR-033 الخُطوَة 8،
    /// ‏2026-10-12). التَرتيب: المَسارُ مَفتوحٌ (<see cref="VendorShippingInput.Register"/>) ← <b>لا مَدخَلَ لَه</b>: الاستِمارَةُ تُدَقَّق
    /// (<see cref="VendorRegistrationValidator"/>) ← <b>سَطرُ الأَثَرِ قَبلَ أَوَّلِ نِداءٍ خارِجِيّ</b> ← «Register» بِالاعتِمادِ السوقِيّ ←
    /// فَشَلُه رَمزُه <b>ولا يُحفَظُ شَيء</b> ← نَجاحُه: رابِطُ التَفعيلِ في وِعاءِ العَرضِ مَرَّةً، ورَمزُ الحِسابِ الجَديدُ مَختوماً في
    /// <c>delivery:{listing_owner}</c> مَعَ تَسجيلِ الوارِدِ بِالمَسارِ الَّذي يَسلُكُه اللَصق.</para>
    /// <para><b>ومَن لَه مَدخَلٌ فَعّال</b> — سَجَّلَ أَو لَصَقَ قَبلُ — لا يُسَجَّلُ حِسابٌ ثانٍ: سَطرُ الأَثَرِ ثُمَّ يُعادُ تَسجيلُ وارِدِه بِرَمزِه
    /// المَختوم (زِرُّ «أَعِد المُحاوَلَة» حينَ لَم يُسَجَّل). <b>ولا استِهلاكَ لِحِصَّة</b>: لا تَمُرُّ هذِه الخِدمَةُ بِالاستِحقاقات.</para>
    /// <para><b>ومُنذُ مُراجَعَةِ تَيّارِ S8، الجولة 2 (‏2026-10-12؛ ‏ADR-033 §٧‑ف)</b>: التَوقيعُ يَطلُبُ <see cref="VendorRegistrant"/> لا <see cref="ListingVendor"/> —
    /// فَلا تُنادى بِلا جِهَةِ تَواصُلٍ مُتَحَقَّقٍ مِنها — والاستِمارَةُ تُدَقَّقُ وتُرسَلُ <b>مَربوطَةً بِها</b> (<see cref="VendorRegistrant.Bind"/>)؛ و<b>حَدُّ
    /// المُحاوَلاتِ بَعدَ التَدقيقِ وقَبلَ الأَثَر</b>: استِمارَةٌ تُرَدُّ لا تَستَهلِكُ مُحاوَلَة، ومُحاوَلَةٌ فَوقَ الحَدِّ لا سَطرَ لَها ولا نِداء.</para>
    /// </summary>
    public async Task<VendorShippingResult> RegisterAsync(
        string tenantSlug, VendorRegistrant registrant, VendorRegistrationForm form, string inboundUrl,
        string? ip = null, CancellationToken ct = default)
    {
        if (Input != VendorShippingInput.Register)
            return new(_vault.IsAvailable ? RegisterClosed : VaultUnavailable, null, null);

        var vendor = registrant.Vendor;
        var owner = vendor.UserId;
        var entryId = TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner);
        var before = await _providers.VendorEntryAsync(tenantSlug, ProviderCapabilities.Delivery, owner, ct);
        var kept = before is { IsActive: true } ? before : null;

        string token;
        if (kept is null)
        {
            var sent = registrant.Bind(form);
            if (VendorRegistrationValidator.Validate(sent).FirstOrDefault() is { } invalid)
                return new(invalid.Code, before, null);

            if (!global::ACommerce.Kit.Auth.Server.AuthHandlers.TryConsumeSendQuota(RegisterAttemptKey(tenantSlug, owner)))
            {
                _log.LogInformation("[OTO] vendor {Slug}/{Owner} ← register refused ({Code})",
                    tenantSlug, owner.ToString("D"), RegisterRateLimited);
                return new(RegisterRateLimited, before, null);
            }

            await _audit.WriteAsync(tenantSlug, owner, vendor.Name, RegisterAuditAction, "provider", entryId,
                note: "register", ip: ip, ct: ct);
            var registered = await _oto.RegisterVendorAsync(_opts.MarketplaceToken, sent.ToOto(), ct);
            if (registered.Value is not { } account)
            {
                _log.LogInformation("[OTO] vendor {Slug}/{Owner} ← register refused ({Code})",
                    tenantSlug, owner.ToString("D"), registered.Failure!.Code);
                return new(registered.Failure!.Code, before, null);
            }

            _reveal.Stash(tenantSlug, RevealKey(owner), account.ActivationLink);
            token = account.RefreshToken;
        }
        else
        {
            if (VendorShippingSurface.RefreshField is not { } refreshField
                || !kept.Values.TryGetValue(refreshField, out var sealedToken))
                return new(EntryUnreadable, before, null);
            try { token = sealedToken.Unseal(_vault, VendorShippingSurface.AadFor(tenantSlug, refreshField, owner)); }
            catch (CryptographicException) { return new(EntryUnreadable, before, null); }

            await _audit.WriteAsync(tenantSlug, owner, vendor.Name, RegisterAuditAction, "provider", entryId,
                note: "inbound", ip: ip, ct: ct);
        }

        var saved = await ConnectAsync(tenantSlug, owner, token, inboundUrl, kept, ct);
        _log.LogInformation("[OTO] vendor {Slug}/{Owner} ← {Path} (webhook: {Webhook})", tenantSlug, owner.ToString("D"),
            kept is null ? "registered" : "inbound", saved.WebhookRefusal ?? (saved.WebhookId is null ? "—" : "registered"));
        return new(null, before, saved);
    }

    /// <summary>رابِطُ تَفعيلِ الحِسابِ الَّذي فُتِحَ لِلتَوّ — <b>مَرَّةً واحِدَة</b> (القِراءَةُ تَحذِف) لِلبائِعِ نَفسِه، أَو <c>null</c>.</summary>
    public string? TakeActivationLink(string tenantSlug, Guid listingOwner) => _reveal.TakeOnce(tenantSlug, RevealKey(listingOwner));

    /// <summary>مِفتاحُ رابِطِ التَفعيلِ في وِعاءِ العَرض — بائِعٌ في مَتجَر، فَلا يَقرَأُ بائِعٌ رابِطَ آخَر.</summary>
    public static string RevealKey(Guid listingOwner) => $"oto-activation:{listingOwner:D}";

    /// <summary>
    /// <para><b>المَسارُ الواحِدُ لِلخَتمِ وتَسجيلِ الوارِد</b> — يَسلُكُه اللَصقُ والتَسجيلُ مَعاً (كانَ جِسمَ <c>SaveTokenAsync</c> حَرفاً حَتّى
    /// الخُطوَة 8): ١) الرَمزُ مَختوماً والسِرُّ وتَسجيلُه القائِمانِ كَما هُما؛ ٢) الوارِدُ بِسِرٍّ جَديدٍ بِرَمزِ البائِعِ نَفسِه — يُحَدَّثُ تَسجيلُه القائِم،
    /// وتَسجيلٌ يَرُدُّه حِسابٌ آخَرُ يُنشَأُ مِن جَديد؛ ٣) السِرُّ يُستَبدَلُ حينَ تَقبَلُه OTO وَحدَه، وإلّا يُكتَبُ رَمزُ فَشَلِها ويَبقى القَديم.</para>
    /// </summary>
    private async Task<TenantProviderBinding> ConnectAsync(
        string tenantSlug, Guid owner, string token, string inboundUrl, TenantProviderBinding? kept, CancellationToken ct)
    {
        var def = VendorShippingSurface.Definition!;
        var refreshField = def.Credential.Fields.First(f => f.Kind == CredentialKinds.SecretKey);
        var webhookField = def.Credential.Fields.First(f => f.Code == def.Webhook!.Verify);

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

        // ٢) الوارِدُ بِسِرٍّ جَديدٍ وبِرَمزِ البائِعِ نَفسِه: يُحَدَّثُ تَسجيلُه القائِم، وتَسجيلٌ يَرُدُّه حِسابٌ آخَرُ يُنشَأُ مِن جَديد.
        var secret = OtoWebhookSignature.NewSecret();
        var registered = await _oto.RegisterWebhookAsync(token, inboundUrl, secret, kept?.WebhookId, ct);
        if (!registered.Ok && kept?.WebhookId is not null && registered.Failure!.Code == OtoFailures.Refused)
            registered = await _oto.RegisterWebhookAsync(token, inboundUrl, secret, null, ct);

        // ٣) السِرُّ يُستَبدَلُ حينَ تَقبَلُه OTO وَحدَه؛ وإلّا يُكتَبُ رَمزُ فَشَلِها ويَبقى القَديم.
        if (registered.Value is { } webhookId)
        {
            values[webhookField.Code] = StoredValue.Sealed(_vault, webhookField.Kind, secret,
                VendorShippingSurface.AadFor(tenantSlug, webhookField.Code, owner));
            return await _providers.BindVendorAsync(tenantSlug, ProviderCapabilities.Delivery, owner, def.Slug,
                values, webhookId, null, by, ct);
        }

        return await _providers.BindVendorAsync(tenantSlug, ProviderCapabilities.Delivery, owner, def.Slug,
            values, kept?.WebhookId, registered.Failure!.Code, by, ct);
    }
}
