using ACommerce.Kit.Delivery;
using Microsoft.Extensions.Options;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في تَخويلِ شَحنِ الصَفقَة — نَفسُ شَكلِ
/// <c>DealAcceptViolation</c> (القاعِدَة ٤).</summary>
public sealed record DealShipViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مَن يَشحَنُ الطَلَب</b> — سِجِلُّ السُلطَةِ في <b>تَوقيعِ</b>
/// <see cref="DealShipping.ShipAsync"/> (القاعِدَة ٦)، عَلى نَسَقِ <see cref="DealAcceptor"/>
/// حَرفاً: لا يُنادى الشَحنُ بِلا هذا السِجِلّ، فَلا مَسارَ «يَنسى» الحارِس.</para>
///
/// <para><b>والحارِسُ نَفسُه حارِسُ <c>/accept</c> — لا أُنبوبٌ جَديد</b> (القاعِدَة ٨): هُوِيَّةُ
/// مالِكِ الإعلانِ مِن <see cref="DealAcceptAuthorization.OwnerIdOf"/> الواحِدَة، ورَمزا الغِيابِ
/// بِحَرفِهِما. فَالبائِعُ الَّذي يَقبَلُ هُوَ الَّذي يَشحَن، والحِسابُ عِندَ OTO حِسابُه
/// (‏ADR-033 §٢‑ج: «الوحدةُ المسجَّلةُ عندَ OTO هي مالكُ الإعلان»).</para>
/// </summary>
public sealed record DealShipper(Guid ActorId, string ActorName);

/// <summary>
/// <para><b>بَوّابَةُ الشَحنِ — دالَّةٌ نَقِيَّة.</b> صَفقَةٌ وفاعِلٌ وجاهِزِيَّةُ الناقِل، ورَمزٌ مِن
/// مَعجَمٍ مُغلَقٍ أَو <c>null</c>. تَقرَؤُها النُقطَةُ قَبلَ أَن تُنادى OTO، والشاشَةُ لِتَرسُمَ
/// استِمارَةَ «إنشاء بوليصَة» أَو لا تَرسُمَها (القاعِدَة ١٢: ما يُرسَمُ هُوَ ما يُقبَل).</para>
///
/// <para><b>والتَرتيبُ مَقصود</b> (القاعِدَة ٦): الغِياب، ثُمَّ الهُوِيَّة (مالِكُ الإعلانِ وَحدَه —
/// فَغَيرُه لا يُجابُ عَن حالِ صَفقَةٍ لَيسَت لَه)، ثُمَّ الحالَة (فَعّالَة، نَقديَّة، تاليها
/// <c>Shipping</c>)، ثُمَّ المُحَرِّكُ (يُخرِجُها هذا الفاعِل — <see cref="DealsService.CanMove"/>)،
/// ثُمَّ الناقِل، ثُمَّ البَيانات. وحَقلُ المَدينَةِ — مُدخَلُ الاستِمارَةِ لا صِفَةُ الصَفقَة — بَعدَ
/// ذلك كُلِّه (<see cref="ValidateCity"/>)، ولا يُسأَلُ حينَ يوجَدُ الطَلَبُ مِن قَبل.</para>
///
/// <para><b>ولا رَمزَ لِـ«أُنشِئَت بوليصَةٌ مِن قَبل»</b>: صَفقَةٌ عِندَ خُطوَةِ الشَحنِ ولَها طَلَبٌ عِندَ OTO
/// هي مُحاوَلَةٌ سابِقَةٌ أُنشِئَ طَلَبُها ولَم يَقَع انتِقالُها (تَعارُضُ نُسخَة، أَو انقِطاع) — فَتُستَأنَفُ
/// بِلا طَلَبٍ ثانٍ (<see cref="DealShipping.IsPlaced"/>). ورَفضُها كانَ يَجعَلُ «أَعِد المُحاوَلَة» —
/// نَصَّ <c>deal_changed_meanwhile</c> — طَريقاً مَسدوداً.</para>
///
/// <para><b>ولِماذا النَقدُ وَحدَه في هذِه المَوجَة</b>: الهَيكَلُ الماشي «‏createOrder بِـcreateShipment:
/// true وcod_amount مِن صَفقَةٍ pay_method = cash» (‏ADR-033 §٢‑هـ القاعِدَة ٩). وصَفقَةُ البِطاقَةِ
/// تَنتَظِرُ عَميلَ البَوّابَة (‏ADR-032 الخُطوات 8–10) لِتَصدُقَ «مَدفوعَة» — فَتُرَدُّ بِرَمزِها
/// لا تُشحَنُ بِـ<c>paid</c> عَلى ظَنّ.</para>
/// </summary>
public static class DealShipAuthorization
{
    // ─── المُشتَرَكُ مَعَ القَبولِ بِحَرفِه — مِفتاحُ نَصٍّ واحِدٌ لِكُلٍّ ─────────
    public const string DealNotFound        = DealAcceptAuthorization.DealNotFound;
    public const string ListingOwnerMissing = DealAcceptAuthorization.ListingOwnerMissing;
    public const string DealNotActive       = DealAcceptAuthorization.DealNotActive;

    /// <summary>الفاعِلُ لَيسَ مالِكَ الإعلان — ولا يَشحَنُ بِحِسابِ غَيرِه. (لا يُعادُ
    /// <c>actor_not_listing_owner</c>: نَصُّه «لا يَقبَلُ العَرضَ إلّا مالِكُ الإعلان».)</summary>
    public const string ActorNotShipper = "actor_not_shipper";

    /// <summary>الصَفقَةُ لَيسَت دَفعاً عِندَ الاستِلام — والبوليصَةُ في هذِه المَوجَةِ لِلنَقدِ وَحدَه.</summary>
    public const string DealNotCash = "deal_not_cash";

    /// <summary>تاليها لَيسَ <c>Shipping</c> — لَم تُؤَكَّد بَعد، أَو تَجاوَزَت الشَحن.</summary>
    public const string DealNotReadyToShip = "deal_not_ready_to_ship";

    /// <summary>المُحَرِّكُ لا يُخرِجُها بِهذا الفاعِل — طَرَفُها المُقابِلُ غَيرُه (عَيَّنَ المَسارُ
    /// الإداريُّ مالِكَ التَطبيق). يُسأَلُ قَبلَ OTO كَي لا تُنشَأَ بوليصَةٌ لِانتِقالٍ مَردود.</summary>
    public const string ActorCannotShip = "actor_cannot_ship";

    /// <summary>لا رَمزَ ناقِلٍ يُحَلُّ لِهذا المَتجَر — فَالاستِمارَةُ لا تُرسَمُ أَصلاً.</summary>
    public const string CarrierUnavailable = "carrier_unavailable";

    /// <summary>الصَفقَةُ بِلا عُنوانِ تَوصيلٍ أَو هاتِف — حالُ كُلِّ عَرضٍ عَلى إعلان.</summary>
    public const string DeliveryDataMissing = "delivery_data_missing";

    /// <summary>حَقلُ مَدينَةِ المُستَلِمِ فارِغ — ‏<c>city</c> إلزامِيٌّ في «Create Order».</summary>
    public const string DeliveryCityMissing = "delivery_city_missing";

    /// <summary>المَعجَمُ المُغلَق — عَشَرَةٌ لا حادِيَ عَشَر.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        DealNotFound, ListingOwnerMissing, ActorNotShipper, DealNotActive, DealNotCash,
        DealNotReadyToShip, ActorCannotShip, CarrierUnavailable,
        DeliveryDataMissing, DeliveryCityMissing,
    };

    /// <summary><c>null</c> يَعني يُشحَن. والمَدينَةُ خارِجَها (<see cref="ValidateCity"/>).</summary>
    public static DealShipViolation? Validate(Deal? deal, DealShipper by, bool carrierReady)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        var ownerId = DealAcceptAuthorization.OwnerIdOf(deal);
        if (ownerId is null)
            return new(ListingOwnerMissing, "الصَفقَةُ بِلا مَرجِعِ مالِكِ إعلان — فَلا حِسابَ ناقِلٍ يُشحَنُ بِه.");

        if (ownerId != by.ActorId)
            return new(ActorNotShipper, "الفاعِلُ لَيسَ مالِكَ الإعلان — والبوليصَةُ بِحِسابِ مالِكِه.");

        if (deal.Status != DealStatus.Active)
            return new(DealNotActive, $"الصَفقَةُ في حالَة {deal.Status} — لا شَحن.");

        if (!DealsPolicy.IsCashOnDelivery(deal))
            return new(DealNotCash, "البوليصَةُ في هذِه المَوجَةِ لِلدَفعِ عِندَ الاستِلامِ وَحدَه.");

        if (DealsPolicy.Next(deal) != DealStage.Shipping)
            return new(DealNotReadyToShip, $"تالي «{deal.Stage}» لَيسَ الشَحن.");

        if (!DealsService.CanMove(deal, by.ActorId))
            return new(ActorCannotShip, "المُحَرِّكُ لا يُخرِجُ الصَفقَةَ إلى الشَحنِ بِهذا الفاعِل.");

        if (!carrierReady)
            return new(CarrierUnavailable, "لا رَمزَ ناقِلٍ يُحَلُّ لِهذا المَتجَر.");

        if (string.IsNullOrWhiteSpace(DealShipping.AttributeOf(deal, Deal.DeliveryAddressAttribute))
            || string.IsNullOrWhiteSpace(DealShipping.AttributeOf(deal, Deal.DeliveryPhoneAttribute)))
            return new(DeliveryDataMissing, "الصَفقَةُ بِلا عُنوانِ تَوصيلٍ أَو هاتِف.");

        return null;
    }

    /// <summary>حَقلُ الاستِمارَة — بَعدَ التَخويلِ لا قَبلَه (القاعِدَة ٦).</summary>
    public static DealShipViolation? ValidateCity(string? city)
        => string.IsNullOrWhiteSpace(city)
            ? new(DeliveryCityMissing, "مَدينَةُ المُستَلِمِ إلزامِيَّةٌ في طَلَبِ OTO.")
            : null;

    public static bool IsAllowed(Deal? deal, DealShipper by, bool carrierReady)
        => Validate(deal, by, carrierReady) is null;
}

/// <summary>نَتيجَةُ الشَحن — الرَفضُ يَصِلُ المُنادي بِرَمزِه (مِن مَعجَمِ البَوّابَة، أَو
/// <see cref="OtoFailures"/>، أَو <see cref="DealsService.ChangedMeanwhile"/>).</summary>
public sealed record DealShipResult(bool Ok, string? Code, Deal? Deal)
{
    public static DealShipResult Refused(string code, Deal? deal) => new(false, code, deal);
    public static DealShipResult Shipped(Deal deal) => new(true, null, deal);
}

/// <summary>
/// <para><b>شَحنُ الصَفَقاتِ عَبرَ OTO</b> — المُستَهلِكُ الأَوَّلُ لِـ<see cref="OtoClient"/>
/// (‏ADR-033 §٢‑هـ القاعِدَة ١: «المستهلكُ الأوّلُ هو <c>DealStage.Shipping</c>»). يُنشِئُ
/// طَلَباً بِشُحنَتِه ومَبلَغِ تَحصيلِه، ويَكتُبُ مُعَرِّفَيه في <see cref="Deal.Refs"/> المُصَمَّمِ
/// لِذلك («PaymentId، ShipmentId …»)، ثُمَّ يُخرِجُ الصَفقَةَ إلى <c>Shipping</c> <b>عَبرَ
/// <see cref="DealsService.AdvanceAsync"/> نَفسِها</b> — فاعِلُها البائِعُ والسِياسَةُ تَحكُم.</para>
///
/// <para><b>ورَمزُ البائِعِ في هذِه المَوجَةِ مِن التَهيئَةِ وَحدَها</b> (‏ADR-033 §٢‑هـ القاعِدَة ٩):
/// <c>Shipping__Oto__RefreshToken</c> لِمَتجَرِ <see cref="DemoStore"/> عَلى <c>staging</c>. ومَدخَلُ
/// الخِزانَةِ لِكُلِّ بائِعٍ (<c>delivery:{listing_owner}</c>) خُطوَةُ ‏ADR-033 السابِعَة — يَومَئِذٍ
/// تَتَبَدَّلُ <see cref="VendorTokenFor"/> وَحدَها.</para>
/// </summary>
public sealed class DealShipping
{
    // ─── مَفاتيحُ المَراجِع — الكاتِبُ والقارِئُ يَقرَآنِ نَفسَ الحَرف ─────────────

    /// <summary>مُعَرِّفُنا كَما أُرسِلَ إلى OTO (<c>orderId</c>) — وحُضورُه «لِلصَفقَةِ طَلَبٌ هُناك»
    /// (<see cref="IsPlaced"/>).</summary>
    public const string OrderRef = "oto_order_id";

    /// <summary>مُعَرِّفُ OTO كَما عادَ مِن «Create Order» (<c>otoId</c>).</summary>
    public const string OtoIdRef = "oto_id";

    /// <summary>رابِطُ البوليصَة (<c>printAWBURL</c>) — يُعرَضُ لِلطَرَفَين.</summary>
    public const string LabelRef = "oto_label_url";

    /// <summary>رابِطُ تَتَبُّعِ الشُحنَة (<c>trackingUrl</c>) — يُعرَضُ لِلطَرَفَين.</summary>
    public const string TrackingRef = "oto_tracking_url";

    /// <summary>المَتجَرُ الوَحيدُ الَّذي يُحَلُّ لَه رَمزٌ في هذِه المَوجَة — مَتجَرُ لَقطَةِ المَظهَر
    /// (‏ADR-033 §٢‑هـ القاعِدَة ٩: «بمتجرِ <c>theme-demo</c> … و<c>ashare</c> لا يُمسّ»).</summary>
    public const string DemoStore = "theme-demo";

    /// <summary>عُملَةُ الطَلَبِ وبَلَدُ المُستَلِم: الصَفقَةُ بِالرِيالِ (<see cref="Deal.AmountSar"/>)
    /// والمُستَلِمُ في المَملَكَة — ‏ISO2 كَما في أَمثِلَةِ الوَثيقَة (<c>"country": "SA"</c>).</summary>
    public const string Currency = "SAR";
    public const string Country  = "SA";

    private readonly DealsService _deals;
    private readonly OtoClient _oto;
    private readonly OtoOptions _opts;

    public DealShipping(DealsService deals, OtoClient oto, IOptions<OtoOptions> opts)
    {
        _deals = deals;
        _oto = oto;
        _opts = opts.Value;
    }

    /// <summary>
    /// <para><b>رَمزُ البائِعِ لِهذا المَتجَر — أَو <c>null</c> فَلا زِرَّ ولا نِداء.</b> دالَّةٌ نَقِيَّة:
    /// بيئَةٌ مَفتوحَة (<see cref="OtoEnvironment.IsOpen"/> — <c>staging</c> وَحدَه)، ورَمزٌ مَضبوط، والمَتجَرُ
    /// <see cref="DemoStore"/>. فَالرَمزُ المَضبوطُ لا يُستَعمَلُ لِمَتجَرٍ آخَرَ بِحِسابِ غَيرِه.</para>
    /// </summary>
    public static string? VendorTokenFor(OtoOptions? options, string? tenantSlug)
        => options is not null
           && OtoEnvironment.IsOpen(options.Environment)
           && !string.IsNullOrWhiteSpace(options.RefreshToken)
           && string.Equals(tenantSlug, DemoStore, StringComparison.Ordinal)
            ? options.RefreshToken.Trim()
            : null;

    /// <summary>أَيُحَلُّ رَمزُ ناقِلٍ لِهذا المَتجَر؟ — تَقرَؤُه الشاشَةُ والنُقطَة.</summary>
    public bool CarrierReady(string tenantSlug) => VendorTokenFor(_opts, tenantSlug) is not null;

    /// <summary>مُعَرِّفُ الطَلَبِ عِندَ OTO: مُعَرِّفُ الصَفقَةِ بِصيغَةِ <c>N</c> — فَإشعارُ الحالِ يَعودُ إلى
    /// صَفقَتِه بِلا جَدوَلِ رَبط، والمُعَرِّفُ واحِدٌ في كُلِّ مُحاوَلَة: OTO تَرُدُّ الثانِيَ بِـ«‏OTO1063 —
    /// Order Id is already exist» (جَدوَلُ «Error Codes» في وَثيقَتِها) فَلا يَتَضاعَفُ طَلَب.</summary>
    public static string OrderIdOf(Deal deal) => deal.Id.ToString("N");

    /// <summary>لِلصَفقَةِ طَلَبٌ عِندَ OTO كَتَبَته مُحاوَلَةٌ سابِقَة — فَالشَحنُ استِئنافٌ بِلا طَلَبٍ ثانٍ
    /// ولا سُؤالِ مَدينَة. تَقرَؤُه الشاشَةُ والخِدمَة.</summary>
    public static bool IsPlaced(Deal deal)
        => deal.Refs.TryGetValue(OrderRef, out var id) && !string.IsNullOrWhiteSpace(id);

    public static string? AttributeOf(Deal deal, string key)
        => deal.Attributes.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>
    /// <para><b>مُسَوَّدَةُ الطَلَبِ مِن الصَفقَةِ — دالَّةٌ نَقِيَّة.</b> نَقدٌ عِندَ الاستِلام:
    /// <c>payment_method = cod</c> و<c>amount_due = amount = AmountSar</c>؛ والمُستَلِمُ المُشتَري
    /// باسمِه وهاتِفِه وعُنوانِه كَما كَتَبَها عِندَ الباب، والمَدينَةُ مِن استِمارَةِ البائِع.</para>
    /// </summary>
    public static OtoOrderDraft DraftFor(Deal deal, string city) => new(
        OrderIdOf(deal), OtoClient.CashOnDelivery, deal.AmountSar, deal.AmountSar, Currency,
        new OtoCustomer(
            deal.InitiatorName,
            AttributeOf(deal, Deal.DeliveryPhoneAttribute) ?? "",
            AttributeOf(deal, Deal.DeliveryAddressAttribute) ?? "",
            city.Trim(),
            Country),
        deal.ListingTitle);

    /// <summary>
    /// <para><b>يَشحَن</b>: البَوّابَةُ أَوَّلاً عَلى الصَفقَةِ كَما في المَخزَن، ثُمَّ «Create Order» (أَو تَبَنّي
    /// طَلَبٍ تَحمِلُه OTO بِمُعَرِّفِنا — <see cref="PlaceAsync"/>)، ثُمَّ المُعَرِّفانِ في المَراجِع، ثُمَّ الانتِقالُ
    /// إلى <c>Shipping</c> بِالبائِعِ مِن المَرحَلَةِ الَّتي رَأَتها البَوّابَة (<c>from</c>)، ثُمَّ — بِلا شَرط —
    /// رابِطا البوليصَةِ والتَتَبُّعِ إن أَعطَتهُما OTO الآن (وإلّا يَأتِيانِ مَعَ إشعارِ الحال).</para>
    ///
    /// <para><b>والتَرتيبُ مَقصود</b>: المَراجِعُ قَبلَ الانتِقال — لَو رُدَّ الانتِقالُ (أَلغى المُشتَري
    /// أَثناءَ نِداءِ OTO، أَو تَعارَضَت نُسخَة) بَقِيَ مُعَرِّفُ الطَلَبِ مَكتوباً، فَالإعادَةُ تَستَأنِفُ ولا تُنشِئُ
    /// ثانِياً (<see cref="IsPlaced"/>). والإلحاقُ يُعادُ تَطبيقُه عِندَ التَعارُض (‏ADR-036 §٧). <b>وبَعدَ الأَثَرِ
    /// لا إلغاء</b>: ما يَلي طَلَباً أُنشِئَ يَجري بِـ<see cref="CancellationToken.None"/> — مَرجِعُه يَجِبُ أَن
    /// يَقَع.</para>
    ///
    /// <para><b>وما لا يُغلِقُه — يُقالُ ولا يُبتلَع</b> (‏ADR-036 §٣‑١): الأَثَرُ الخارِجيُّ يَسبِقُ الحِفظَ
    /// المَفحوص؛ فَانتِقالٌ مَردودٌ لِأَنّ المُشتَري أَلغى يَترُكُ طَلَباً عِندَ OTO لِصَفقَةٍ مُلغاة —
    /// مَرجِعُه في الصَفقَة، ويُلغى مِن لَوحَةِ البائِعِ عِندَ OTO.</para>
    /// </summary>
    public async Task<DealShipResult> ShipAsync(
        string tenantSlug, Guid dealId, DealShipper by, string? city, CancellationToken ct = default)
    {
        var deal = await _deals.LoadAsync(tenantSlug, dealId, ct);
        var token = VendorTokenFor(_opts, tenantSlug);

        var refusal = DealShipAuthorization.Validate(deal, by, token is not null);
        if (refusal is not null) return DealShipResult.Refused(refusal.Code, deal);

        var seen = deal!.Stage;
        if (!IsPlaced(deal))
        {
            var noCity = DealShipAuthorization.ValidateCity(city);
            if (noCity is not null) return DealShipResult.Refused(noCity.Code, deal);

            var placed = await PlaceAsync(token!, deal, city!, ct);
            if (placed.Value is not { } order) return DealShipResult.Refused(placed.Failure!.Code, deal);

            await _deals.AttachRefAsync(tenantSlug, dealId, OrderRef, order.OrderId, CancellationToken.None);
            await _deals.AttachRefAsync(tenantSlug, dealId, OtoIdRef, order.OtoId, CancellationToken.None);
        }

        // كُلُّ رَدٍّ هُنا «تَغَيَّرَت في الأَثناء»: البَوّابَةُ مَرَّت عَلى ما قُرِئ، والانتِقالُ رَأى غَيرَه
        // (تَعارُضُ نُسخَة، أَو مَرحَلَةٌ غَيرُ `seen`، أَو صَفقَةٌ أُلغِيَت بَينَهُما).
        var moved = await _deals.AdvanceAsync(tenantSlug, dealId, by.ActorId, by.ActorName, null, CancellationToken.None, seen);
        if (!moved.Ok) return DealShipResult.Refused(DealsService.ChangedMeanwhile, moved.Deal);

        var state = await _oto.GetOrderStatusAsync(token!, OrderIdOf(deal), CancellationToken.None);
        var shipped = state.Value is { } s
            ? await AttachLinksAsync(tenantSlug, moved.Deal!, s, CancellationToken.None)
            : moved.Deal!;
        return DealShipResult.Shipped(shipped);
    }

    /// <summary>
    /// <para><b>يُنشِئُ الطَلَب — أَو يَتَبَنّى ما تَحمِلُه OTO بِمُعَرِّفِنا.</b> رَدٌّ ضاعَ بَعدَ أَن أُنشِئَ الطَلَبُ
    /// هُناك (انقِطاعٌ أَو مُهلَة) يَجعَلُ الإعادَةَ تَلقى «‏OTO1063 — Order Id is already exist» — فَبِلا تَبَنٍّ
    /// لا تُكتَبُ مَراجِعُ أَبَداً ولا يُشحَنُ الطَلَبُ بِالزِرّ. فَعِندَ فَشَلٍ قَد يَكونُ الطَلَبُ وَقَعَ فيه يُسأَلُ
    /// «Order Tracking» بِمُعَرِّفِنا، ولا يُتَبَنّى إلّا جَوابٌ يُعيدُ المُعَرِّفَ نَفسَه ومَعَه <c>otoId</c>؛
    /// وكُلُّ ما سِواه يُعيدُ الفَشَلَ الأَصلِيَّ بِرَمزِه.</para>
    ///
    /// <para><b>ولا سُؤالَ حَيثُ لَم يَبلُغ الطَلَبُ OTO</b>: <c>oto_not_configured</c> و<c>oto_token_refused</c>
    /// يَقَعانِ قَبلَ نُقطَةِ الطَلَب.</para>
    /// </summary>
    private async Task<OtoResult<OtoCreatedOrder>> PlaceAsync(string token, Deal deal, string city, CancellationToken ct)
    {
        var created = await _oto.CreateOrderAsync(token, DraftFor(deal, city), ct);
        if (created.Ok || created.Failure!.Code is OtoFailures.NotConfigured or OtoFailures.TokenRefused)
            return created;

        var ours = OrderIdOf(deal);
        var held = await _oto.GetOrderStatusAsync(token, ours, CancellationToken.None);
        return held.Value is { OtoId: { } otoId } state
               && string.Equals(state.OrderId, ours, StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(otoId)
            ? OtoResult<OtoCreatedOrder>.Success(new OtoCreatedOrder(ours, otoId))
            : created;
    }

    /// <summary>
    /// <para><b>رابِطا البوليصَةِ والتَتَبُّعِ مِن حالٍ جَلَبناه</b> — يُكتَبُ ما تَغَيَّرَ وَحدَه (التَكرارُ بِلا
    /// أَثَر)، وما لَيسَ عُنواناً مُطلَقاً بِـ<c>https</c> لا يُكتَب: الرابِطُ يُرسَمُ <c>href</c> لِلطَرَفَين.</para>
    /// </summary>
    public async Task<Deal> AttachLinksAsync(string tenantSlug, Deal deal, OtoOrderState state, CancellationToken ct = default)
    {
        var current = deal;
        foreach (var (key, url) in new[] { (LabelRef, state.LabelUrl), (TrackingRef, state.TrackingUrl) })
        {
            if (!IsHttps(url) || (current.Refs.TryGetValue(key, out var had) && had == url)) continue;
            current = await _deals.AttachRefAsync(tenantSlug, deal.Id, key, url!, ct) ?? current;
        }
        return current;
    }

    /// <summary>رابِطا الصَفقَةِ لِلعَرض — <c>null</c> لِما غابَ أَو لَم يَكُن <c>https</c>. دالَّةٌ نَقِيَّةٌ
    /// تَقرَؤُها صَفحَةُ الصَفقَةِ بِلا جَلسَة (الطَبَقَةُ ٨).</summary>
    public static (string? Label, string? Tracking) LinksOf(Deal deal)
        => (deal.Refs.TryGetValue(LabelRef, out var l) && IsHttps(l) ? l : null,
            deal.Refs.TryGetValue(TrackingRef, out var t) && IsHttps(t) ? t : null);

    public static bool IsHttps(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}
