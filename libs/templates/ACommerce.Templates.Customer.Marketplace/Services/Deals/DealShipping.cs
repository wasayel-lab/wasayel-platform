using ACommerce.Kit.Delivery;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>
    /// <para><b>طَلَبُ الصَفقَةِ عِندَ OTO في حالٍ لا يَعودُ مِنه</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 §٧‑ح‑٣‑١):
    /// <see cref="DealShipping.ClosedAtOto"/> — الحالُ الَّذي كَتَبَه آخِرُ جَوابٍ مِن OTO في <see cref="DealShipping.ClosedRef"/>. فَلا
    /// «أَكمِل الشَّحن» يُرسَمُ لَه ولا يُقبَلُ — كانَ يُجابُ <c>shipment_unconfirmed</c> كُلَّ نَقرَةٍ بِلا نِهايَة (تَوصيفُه <c>d1a6b83</c>) — ومَخرَجُه
    /// الإلغاء. يَسبِقُ سُؤالَ الناقِل: حالُ الطَلَبِ عِندَ OTO لا يَتَغَيَّرُ بِغِيابِ رَمزِنا.</para>
    /// </summary>
    public const string OrderClosedAtOto = "oto_order_closed";

    /// <summary>لا رَمزَ ناقِلٍ يُحَلُّ لِهذا المَتجَر — فَالاستِمارَةُ لا تُرسَمُ أَصلاً.</summary>
    public const string CarrierUnavailable = "carrier_unavailable";

    /// <summary>الصَفقَةُ بِلا عُنوانِ تَوصيلٍ أَو هاتِف — حالُ كُلِّ عَرضٍ عَلى إعلان.</summary>
    public const string DeliveryDataMissing = "delivery_data_missing";

    /// <summary>حَقلُ مَدينَةِ المُستَلِمِ فارِغ — ‏<c>city</c> إلزامِيٌّ في «Create Order».</summary>
    public const string DeliveryCityMissing = "delivery_city_missing";

    /// <summary>المَعجَمُ المُغلَق — عَشَرَةٌ لا حادِيَ عَشَر. <b>صارَت أَحَدَ عَشَر ‏2026-10-12</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8):
    /// <see cref="OrderClosedAtOto"/> بَعدَ <see cref="ActorCannotShip"/> وقَبلَ <see cref="CarrierUnavailable"/> — ولا ثانِيَ عَشَر.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        DealNotFound, ListingOwnerMissing, ActorNotShipper, DealNotActive, DealNotCash,
        DealNotReadyToShip, ActorCannotShip, OrderClosedAtOto, CarrierUnavailable,
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

        if (DealShipping.ClosedAtOto(deal))
            return new(OrderClosedAtOto, $"طَلَبُ الصَفقَةِ عِندَ OTO في حالٍ لا يَعودُ مِنه («{deal.Refs[DealShipping.ClosedRef]}»).");

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

    /// <summary>
    /// <para><b>خُطوَةٌ مَحجوزَةٌ عِندَ OTO لا بابَ لَها — رَمزُ البَوّابَةِ الَّذي يُفَسِّرُها</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، البَندُ A0 (x)،
    /// ‏2026-10-12؛ ‏ADR-033 §٧‑ل): لِمَن يُخرِجُ الصَفقَةَ (<see cref="DealsService.CanMove"/>) وتاليها «شَحن» وطَلَبُها عِندَ OTO
    /// (<see cref="DealShipping.AwaitsCarrier"/>) — فَلا «تَأكيد: شَحن» لَه — <b>والبَوّابَةُ تَرُدُّ «أَكمِل الشَّحن»</b>: رَمزُ رَدِّها بِحَرفِه
    /// (<see cref="CarrierUnavailable"/> حينَ يَغيبُ الناقِل، <see cref="OrderClosedAtOto"/> حينَ أَغلَقَت OTO الطَلَب). و<c>null</c> لِكُلِّ ما
    /// سِواه، ومِنه خُطوَةٌ بابُها مَفتوح.</para>
    ///
    /// <para>تَقرَؤُها الصَفحَةُ فَلا تَقولُ «دَورُكَ الآن» عَن خُطوَةٍ لا بابَ لَها، وتَرسُمُ نَصَّ الرَمزِ نَفسِه الَّذي تَرتَدُّ بِه <c>/ship</c> —
    /// فَما يُقالُ هُوَ ما تُجيبُ بِه النُقطَة (القاعِدَة ١٢). وقَرارُ ما يُفعَلُ بِطَلَباتِ مَتجَرٍ غابَ ناقِلُه لِلمالِك («أ» S7·١٦ (ج)).</para>
    /// </summary>
    public static string? HeldRefusal(Deal deal, DealShipper by, bool carrierReady)
        => DealShipping.AwaitsCarrier(deal) && DealsService.CanMove(deal, by.ActorId)
            ? Validate(deal, by, carrierReady)?.Code
            : null;
}

/// <summary>نَتيجَةُ الشَحن — الرَفضُ يَصِلُ المُنادي بِرَمزِه (مِن مَعجَمِ البَوّابَة، أَو
/// <see cref="OtoFailures"/>، أَو <see cref="DealsService.ChangedMeanwhile"/>، أَو — بَعدَ أَن وُجِدَ الطَلَبُ عِندَ OTO —
/// <see cref="DealShipping.ShipmentUnconfirmed"/>).</summary>
public sealed record DealShipResult(bool Ok, string? Code, Deal? Deal)
{
    public static DealShipResult Refused(string code, Deal? deal) => new(false, code, deal);
    public static DealShipResult Shipped(Deal deal) => new(true, null, deal);

    /// <summary>كَلِمَةُ الحالِ الَّتي أَجابَت بِها OTO حينَ رُدَّ الشَحنُ بَعدَ سُؤالِها — لِلوغِ وَحدَه (مُراجَعَةُ المَوجَة 3a، ‏2026-10-12)؛
    /// و<c>null</c> حَيثُ لَم تُسأَل أَو لَم تُجِب.</summary>
    public string? OtoStatus { get; init; }
}

/// <summary>
/// <para><b>شَحنُ الصَفَقاتِ عَبرَ OTO</b> — المُستَهلِكُ الأَوَّلُ لِـ<see cref="OtoClient"/>
/// (‏ADR-033 §٢‑هـ القاعِدَة ١: «المستهلكُ الأوّلُ هو <c>DealStage.Shipping</c>»). يُنشِئُ
/// طَلَباً بِشُحنَتِه ومَبلَغِ تَحصيلِه، ويَكتُبُ مُعَرِّفَيه في <see cref="Deal.Refs"/> المُصَمَّمِ
/// لِذلك («PaymentId، ShipmentId …»)، ثُمَّ — <b>حينَ تُثبِتُ OTO شُحنَتَه وبوليصَتَها</b> (<see cref="ShipmentConfirmed"/>؛
/// مُراجَعَةُ تَيّارِ S7، ‏2026-10-12) — يُخرِجُ الصَفقَةَ إلى <c>Shipping</c> <b>عَبرَ
/// <see cref="DealsService.AdvanceAsync"/> نَفسِها</b> — فاعِلُها البائِعُ والسِياسَةُ تَحكُم.</para>
///
/// <para><b>ورَمزُ البائِعِ في هذِه المَوجَةِ مِن التَهيئَةِ وَحدَها</b> (‏ADR-033 §٢‑هـ القاعِدَة ٩):
/// <c>Shipping__Oto__RefreshToken</c> لِمَتجَرِ <see cref="DemoStore"/> عَلى <c>staging</c>. ومَدخَلُ
/// الخِزانَةِ لِكُلِّ بائِعٍ (<c>delivery:{listing_owner}</c>) خُطوَةُ ‏ADR-033 السابِعَة — يَومَئِذٍ
/// تَتَبَدَّلُ <see cref="VendorTokenFor"/> وَحدَها.</para>
///
/// <para><b>وجاءَ يَومُها ‏2026-10-12 (تَيّارُ S8، ‏ADR-033 الخُطوَة 7؛ ولا يُعادُ حَرفٌ فَوقَه)</b>: الرَمزُ رَمزُ <b>بائِعِ الصَفقَة</b> —
/// مَدخَلُه في الخِزانَةِ (<see cref="CarrierForAsync"/>: يُفَكُّ بِسِياقِ <c>tenant|delivery|field|listing_owner</c>) — ومَعَه سِرُّ الوارِدِ
/// الَّذي يُتَحَقَّقُ بِه مِن إشعارِ OTO. <b>ورَمزُ التَهيئَةِ احتِياطٌ لِمَتجَرِ <see cref="DemoStore"/> وَحدَه، ولِبائِعٍ لا مَدخَلَ لَه وَحدَه</b>
/// (<see cref="VendorTokenFor"/> كَما كانَت) — وهُوَ بِلا سِرِّ وارِد، فَإشعاراتُ صَفَقاتِه تُرَدُّ بِـ<c>notice_unverifiable</c>: فَشَلٌ مُغلَق.
/// ولَم تَتَبَدَّل <see cref="VendorTokenFor"/>؛ أُضيفَ قَبلَها المَدخَل.</para>
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

    /// <summary>
    /// <para><b>حالُ طَلَبٍ أَغلَقَته OTO</b> — كَلِمَةُ الحالِ النِهائِيِّ (<see cref="OtoClient.TerminalStatuses"/>) كَما أَجابَت بِها «Order
    /// Tracking» حينَ سُئِلَت في <see cref="ShipAsync"/> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12). يَقرَؤُه <see cref="ClosedAtOto"/>
    /// فَلا يُرسَمُ «أَكمِل الشَّحن» ولا يُقبَل.</para>
    /// </summary>
    public const string ClosedRef = "oto_closed_status";

    /// <summary>المَتجَرُ الوَحيدُ الَّذي يُحَلُّ لَه رَمزٌ في هذِه المَوجَة — مَتجَرُ لَقطَةِ المَظهَر
    /// (‏ADR-033 §٢‑هـ القاعِدَة ٩: «بمتجرِ <c>theme-demo</c> … و<c>ashare</c> لا يُمسّ»).</summary>
    public const string DemoStore = "theme-demo";

    /// <summary>عُملَةُ الطَلَبِ وبَلَدُ المُستَلِم: الصَفقَةُ بِالرِيالِ (<see cref="Deal.AmountSar"/>)
    /// والمُستَلِمُ في المَملَكَة — ‏ISO2 كَما في أَمثِلَةِ الوَثيقَة (<c>"country": "SA"</c>).</summary>
    public const string Currency = "SAR";
    public const string Country  = "SA";

    /// <summary>
    /// <para><b>لَم تُثبِت OTO شُحنَةَ الطَلَب</b> — رَمزُ <see cref="ShipAsync"/> حينَ يوجَدُ الطَلَبُ عِندَها (مَرجِعاه مَكتوبانِ
    /// في الصَفقَة) ولا يُثبِتُ «Order Tracking» شُحنَتَه وبوليصَتَها (<see cref="ShipmentConfirmed"/>) — أَو لا يُجيب. فَالصَفقَةُ
    /// عِندَ خُطوَتِها بِمَرجِعَيها، والنَقرَةُ التالِيَةُ تَستَأنِفُ (<see cref="IsPlaced"/>) فَتَسأَلُ مِن جَديد.</para>
    ///
    /// <para><b>ولا يُعادُ رَمزُ فَشَلِ OTO هُنا</b>: نُصوصُ <c>oto_refused</c> و<c>oto_token_refused</c> و<c>oto_unreadable</c> في
    /// القامُوسِ تَقولُ «ولَم يُكتَب في الصَّفقَة شَيء» — وقَد كُتِبَ المَرجِعان. ولَيسَ مِن مَعجَمِ البَوّابَة: يَقَعُ بَعدَ OTO لا قَبلَها.</para>
    /// </summary>
    public const string ShipmentUnconfirmed = "shipment_unconfirmed";

    /// <summary>
    /// <para><b>طَلَبُ الصَفقَةِ عِندَ OTO — فَلا يُدخِلُها «شَحن» بابٌ لا يَحمِلُ جَوابَ OTO</b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة،
    /// ‏2026-10-12؛ ‏ADR-033 §٧‑ح): رَمزُ <see cref="DealsService.AdvanceAsync"/> حينَ تَصدُقُ <see cref="AwaitsCarrier"/> عَلى
    /// الصَفقَةِ الَّتي سَتُكتَب ولا يُثبِتُ ما مَعَ النِداءِ شُحنَتَها (<see cref="ShipmentConfirmed"/>). يَبلُغُه زِرُّ «تَأكيد: شَحن»
    /// (<c>/advance</c> ← <c>?err=</c>)، والمَسارُ الإداريّ، ونُقطَةُ الـAPI (‏409 بِالرَمزِ نَفسِه)؛ ولا يَبلُغُه <see cref="ShipAsync"/>
    /// — يَسأَلُ OTO قَبلَه ويَحمِلُ جَوابَها.</para>
    /// </summary>
    public const string DealAwaitsCarrier = "deal_awaits_carrier";

    private readonly DealsService _deals;
    private readonly OtoClient _oto;
    private readonly OtoOptions _opts;
    private readonly ILogger<DealShipping> _log;
    private readonly TenantProviderService? _providers;
    private readonly ProviderVault? _vault;
    private readonly TimeProvider _clock;

    /// <summary><paramref name="log"/> اختِيارِيٌّ (مُراجَعَةُ المَوجَة 3a، ‏2026-10-12): سَطرُ الرَفضِ بَعدَ سُؤالِ OTO يَحمِلُ كَلِمَةَ حالِها
    /// وَحدَها — لا رَمزاً ولا جِسماً — فَيُقرَأُ في اللوغِ لِماذا لا تَتَقَدَّمُ صَفقَة. والحاوِيَةُ تُعطيه، والاختِباراتُ تُنشِئُ الخِدمَةَ بِلاه.
    /// <para>و<paramref name="providers"/> و<paramref name="vault"/> اختِيارِيّانِ كَذلك (تَيّارُ S8، ‏2026-10-12): مَداخِلُ البائِعينَ وخِزانَتُها
    /// — والحاوِيَةُ تُعطيهِما؛ وبِدونِهِما لا مَدخَلَ يُقرَأ، فَيَبقى الاحتِياطُ وَحدَه كَما كانَت الخِدمَةُ قَبلَ الخُطوَة 7. و<paramref name="clock"/>
    /// لِنافِذَةِ التَوقيع.</para></summary>
    public DealShipping(DealsService deals, OtoClient oto, IOptions<OtoOptions> opts, ILogger<DealShipping>? log = null,
        TenantProviderService? providers = null, ProviderVault? vault = null, TimeProvider? clock = null)
    {
        _deals = deals;
        _oto = oto;
        _opts = opts.Value;
        _log = log ?? NullLogger<DealShipping>.Instance;
        _providers = providers;
        _vault = vault;
        _clock = clock ?? TimeProvider.System;
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

    // ─── ناقِلُ بائِعِ الصَفقَة (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7) ───────────────────────────────
    //
    // كانَ هُنا `CarrierReady(tenantSlug)` — «أَيُحَلُّ رَمزُ ناقِلٍ لِهذا المَتجَر؟» بِالتَهيئَةِ وَحدَها — تَقرَؤُه الشاشَةُ والنُقطَةُ والوارِد.
    // والرَمزُ صارَ رَمزَ البائِع، فَالسُؤالُ سُؤالانِ: لِصَفقَةٍ بِعَينِها (بائِعُها) — الشاشَةُ والنُقطَة — ولِمَتجَرٍ قَبلَ أَن يُقرَأَ إشعار — الوارِد.

    /// <summary>
    /// <para><b>أَيُحَلُّ ناقِلٌ لِبائِعِ هذِه الصَفقَة؟ — بِلا فَكّ</b>: بيئَةٌ مَفتوحَة، ثُمَّ مَدخَلُ البائِعِ (<c>listing_owner</c>) إن وُجِدَ —
    /// فَعّالاً بِرَمزٍ مَختومٍ وخِزانَةٍ تَفُكّ — وإلّا رَمزُ التَهيئَةِ الاحتِياطيُّ لِمَتجَرِ <see cref="DemoStore"/>. تَقرَؤُه الشاشَةُ
    /// (<c>canShip</c>) والبَوّابَةُ في <see cref="ShipAsync"/> — فَما يُرسَمُ هُوَ ما يُقبَل. <b>ومَدخَلٌ قائِمٌ يَحكُمُ ولَو لَم يَكُن
    /// فَعّالاً</b>: الاحتِياطُ لِبائِعٍ <i>لا مَدخَلَ لَه</i> وَحدَه — فَمَدخَلٌ مَسحوبٌ أَو بِلا رَمزٍ مَختومٍ «لا ناقِل»، لا رَمزُ التَهيئَة.</para>
    /// </summary>
    /// <remarks>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (iii)؛ ‏ADR-033 §٧‑ص‑٦): <b>وقِراءَةٌ تَرمي لَيسَت «لا مَدخَل»</b> — «لا ناقِل»، فَلا تُرسَمُ
    /// «إنشاء بوليصَة» ولا يُسأَلُ الاحتِياط. كانَ العُطلُ يُجابُ <c>null</c> فَيَسقُطُ إلى رَمزِ التَهيئَة (تَوصيفُه <c>bd48a66</c>).</remarks>
    public async Task<bool> CarrierReadyForAsync(string tenantSlug, Deal? deal, CancellationToken ct = default)
    {
        if (deal is null || !OtoEnvironment.IsOpen(_opts.Environment)) return false;
        var read = await VendorEntryOfAsync(tenantSlug, deal, ct);
        if (read.Failed) return false;
        if (read.Entry is { } entry)
            return entry.IsActive && _vault is { IsAvailable: true }
                   && VendorShippingSurface.RefreshField is { } f && entry.Values.TryGetValue(f, out var v) && v.IsSealed;
        return VendorTokenFor(_opts, tenantSlug) is not null;
    }

    /// <summary>
    /// <para><b>ناقِلُ بائِعِ الصَفقَة — مَفكوكاً لِمَن يَطلُبُ بِه فِعلاً</b>: رَمزُ تَحديثِه وسِرُّ وارِدِه مِن مَدخَلِه، أَو رَمزُ التَهيئَةِ
    /// الاحتِياطيُّ بِلا سِرٍّ لِبائِعٍ لا مَدخَلَ لَه في <see cref="DemoStore"/>. و<c>null</c> لِما سِواه — <b>ومَدخَلٌ قائِمٌ لا يُفَكُّ</b>
    /// (خِزانَةٌ غائِبَة، أَو سِياقٌ أَو عَمودٌ لا يُطابِق) <b>لا يَسقُطُ إلى الاحتِياط</b>: فَشَلٌ مُغلَق.</para>
    /// </summary>
    /// <remarks>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (iii)؛ ‏ADR-033 §٧‑ص‑٦): <b>وقِراءَةُ المَدخَلِ تَرمي ⇒ <c>null</c> بِسَطرِ تَحذير</b> — لا احتِياط:
    /// فَيُجابُ <see cref="ShipAsync"/> وَالوارِدُ «لا ناقِل» (<c>carrier_unavailable</c>). كانَ العُطلُ «لا مَدخَل» فَيُشحَنُ طَلَبُ البائِعِ بِحِسابِ مَتجَرِ
    /// العَرض، ويُتَحَقَّقُ مِن وارِدِه بِلا سِرّ (تَوصيفُهُما <c>bd48a66</c> و<c>6830eb9</c>).</remarks>
    /// <remarks>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (viii)؛ ‏ADR-033 §٧‑ص‑٩ — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق): <b>وصارَت خُطوَتَين يُنادي الوارِدُ كُلّاً
    /// مِنهُما وَحدَها</b> — <see cref="ResolveCarrierAsync"/> (المَدخَلُ وشُروطُه بِلا فَكّ) ثُمَّ <see cref="TryOpenToken"/> و<see cref="TryOpenSecret"/>.
    /// وهذِه تَفُكُّهُما مَعاً كَما كانَت، بِالتَرتيبِ نَفسِه وسَطرِ التَحذيرِ نَفسِه — لِلشَحن.</remarks>
    public async Task<VendorCarrier?> CarrierForAsync(string tenantSlug, Deal deal, CancellationToken ct = default)
        => await ResolveCarrierAsync(tenantSlug, deal, ct) is { } carrier
           && TryOpenToken(tenantSlug, carrier, out var token) && TryOpenSecret(tenantSlug, carrier, out var secret)
            ? new VendorCarrier(token, secret)
            : null;

    /// <summary>
    /// <para><b>ناقِلٌ مَحلولٌ لَم يُفَكّ</b> (‏2026-10-13، G5 (viii)): رَمزُ الاحتِياطِ صَريحاً (<see cref="Fallback"/>) لِبائِعٍ لا مَدخَلَ لَه في
    /// <see cref="DemoStore"/>، أَو مَدخَلٌ فَعّالٌ (<see cref="Entry"/>) بِرَمزٍ مَختومٍ (<see cref="Token"/>) ومالِكِ إعلانٍ مَكتوب — وخِزانَةٌ تَفُكّ.</para>
    /// </summary>
    private sealed record SealedCarrier(string? Fallback, TenantProviderBinding? Entry, Guid Owner, StoredValue? Token);

    /// <summary>
    /// <para><b>الخُطوَةُ الأولى — المَدخَلُ وشُروطُه بِلا فَكّ</b> (‏2026-10-13، G5 (viii)): جِسمُ <see cref="CarrierForAsync"/> قَبلَ الفَكِّ بِحَرفِه —
    /// بيئَةٌ مَفتوحَة، وقِراءَةٌ لَم تَرمِ (‏G5 (iii))، ثُمَّ الاحتِياطُ لِمَن لا مَدخَلَ لَه، أَو مَدخَلٌ فَعّالٌ بِرَمزٍ مَختومٍ وخِزانَةٍ ومالِك؛ و<c>null</c> لِما سِواه.</para>
    /// </summary>
    private async Task<SealedCarrier?> ResolveCarrierAsync(string tenantSlug, Deal deal, CancellationToken ct)
    {
        if (!OtoEnvironment.IsOpen(_opts.Environment)) return null;

        var read = await VendorEntryOfAsync(tenantSlug, deal, ct);
        if (read.Failed)
        {
            _log.LogWarning("[OTO] vendor entry {Slug}/{Owner} could not be read — no carrier, no fallback",
                tenantSlug, DealAcceptAuthorization.OwnerIdOf(deal)?.ToString("D") ?? "—");
            return null;
        }

        var entry = read.Entry;
        if (entry is null)
            return VendorTokenFor(_opts, tenantSlug) is { } fallback ? new SealedCarrier(fallback, null, Guid.Empty, null) : null;

        if (!entry.IsActive || _vault is not { IsAvailable: true } || DealAcceptAuthorization.OwnerIdOf(deal) is not { } owner
            || VendorShippingSurface.RefreshField is not { } refreshField
            || !entry.Values.TryGetValue(refreshField, out var sealedToken) || !sealedToken.IsSealed)
            return null;

        return new SealedCarrier(null, entry, owner, sealedToken);
    }

    /// <summary><b>فَكُّ رَمزِ التَحديث</b> — رَمزُ الاحتِياطِ كَما هُوَ؛ ومَدخَلٌ لا يَنفَكُّ رَمزُه <c>false</c> بِسَطرِ التَحذيرِ القائِم.</summary>
    private bool TryOpenToken(string tenantSlug, SealedCarrier carrier, out string token)
    {
        token = carrier.Fallback ?? "";
        if (carrier.Fallback is not null) return true;
        try
        {
            token = carrier.Token!.Unseal(_vault!, VendorShippingSurface.AadFor(tenantSlug, VendorShippingSurface.RefreshField!, carrier.Owner));
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            _log.LogWarning("[OTO] vendor entry {Slug}/{Owner} does not open — no carrier", tenantSlug, carrier.Owner.ToString("D"));
            return false;
        }
    }

    /// <summary><b>فَكُّ سِرِّ الوارِد</b> — الاحتِياطُ ومَدخَلٌ بِلا سِرٍّ بَعدُ (تَسجيلٌ لَم يَنجَح) <c>null</c> بِلا فَشَل (فَيَرُدُّ التَحَقُّقُ «لا سِرّ»)؛
    /// وسِرٌّ لا يَنفَكُّ <c>false</c> بِسَطرِ التَحذيرِ القائِم.</summary>
    private bool TryOpenSecret(string tenantSlug, SealedCarrier carrier, out string? secret)
    {
        secret = null;
        if (carrier.Entry is null || VendorShippingSurface.WebhookField is not { } webhookField
            || !carrier.Entry.Values.TryGetValue(webhookField, out var sealedSecret))
            return true;
        try
        {
            secret = sealedSecret.Unseal(_vault!, VendorShippingSurface.AadFor(tenantSlug, webhookField, carrier.Owner));
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            _log.LogWarning("[OTO] vendor entry {Slug}/{Owner} does not open — no carrier", tenantSlug, carrier.Owner.ToString("D"));
            return false;
        }
    }

    /// <summary>
    /// <para><b>أَلِهذا المَتجَرِ ناقِلٌ أَصلاً؟ — يَسأَلُه الوارِدُ قَبلَ أَن يَقرَأَ جِسماً</b> (مُراجَعَةُ المَوجَة 3a البَندُ (ii)): بيئَةٌ مَفتوحَة،
    /// ثُمَّ رَمزُ التَهيئَةِ لِمَتجَرِ <see cref="DemoStore"/>، أَو مَدخَلُ بائِعٍ فَعّالٌ واحِدٌ عَلى الأَقَلّ وخِزانَةٌ تَفُكّ. ومَتجَرٌ بِلا شَيءٍ مِنها
    /// يُجابُ ‏404 فارِغاً ولا يُقرَأُ مِنه حَرف — كَما كان.</para>
    /// </summary>
    public async Task<bool> CarrierReadyForStoreAsync(string tenantSlug, CancellationToken ct = default)
    {
        if (!OtoEnvironment.IsOpen(_opts.Environment)) return false;
        if (VendorTokenFor(_opts, tenantSlug) is not null) return true;
        return _providers is not null && _vault is { IsAvailable: true }
               && await _providers.HasVendorEntriesAsync(tenantSlug, ProviderCapabilities.Delivery, ct);
    }

    /// <summary>مَدخَلُ بائِعِ الصَفقَة — فَعّالاً أَو مَسحوباً — أَو <c>null</c> (لا خِدمَةَ مَداخِل، أَو لا مالِكَ إعلانٍ مَكتوب، أَو لا مَدخَل).
    /// والحالُ يَقرَؤُه المُنادي: مَدخَلٌ قائِمٌ يَحكُمُ فَلا يُستَبدَلُ بِالاحتِياط.
    /// <para>(‏2026-10-13، G5 (iii) — ولا يُعادُ حَرفٌ فَوقَه: والجَوابُ <see cref="VendorEntryRead"/> — فَقِراءَةٌ رَمَت <c>Failed</c> لا «لا مَدخَل»؛
    /// وما لا يُقرَأُ أَصلاً (لا خِدمَة، لا مالِك) غِيابٌ كَما كان.)</para></summary>
    private async Task<VendorEntryRead> VendorEntryOfAsync(string tenantSlug, Deal deal, CancellationToken ct)
    {
        if (_providers is null || DealAcceptAuthorization.OwnerIdOf(deal) is not { } owner) return default;
        return await _providers.ReadVendorEntryAsync(tenantSlug, ProviderCapabilities.Delivery, owner, ct);
    }

    /// <summary>مُعَرِّفُ الطَلَبِ عِندَ OTO: مُعَرِّفُ الصَفقَةِ بِصيغَةِ <c>N</c> — فَإشعارُ الحالِ يَعودُ إلى
    /// صَفقَتِه بِلا جَدوَلِ رَبط، والمُعَرِّفُ واحِدٌ في كُلِّ مُحاوَلَة: OTO تَرُدُّ الثانِيَ بِـ«‏OTO1063 —
    /// Order Id is already exist» (جَدوَلُ «Error Codes» في وَثيقَتِها) فَلا يَتَضاعَفُ طَلَب.</summary>
    public static string OrderIdOf(Deal deal) => deal.Id.ToString("N");

    /// <summary>لِلصَفقَةِ طَلَبٌ عِندَ OTO كَتَبَته مُحاوَلَةٌ سابِقَة — فَالشَحنُ استِئنافٌ بِلا طَلَبٍ ثانٍ
    /// ولا سُؤالِ مَدينَة. تَقرَؤُه الشاشَةُ والخِدمَة.</summary>
    public static bool IsPlaced(Deal deal)
        => deal.Refs.TryGetValue(OrderRef, out var id) && !string.IsNullOrWhiteSpace(id);

    /// <summary>
    /// <para><b>تاليها «شَحن» وطَلَبُها عِندَ OTO — فَلا تَدخُلُها إلّا بِجَوابِ OTO — دالَّةٌ نَقِيَّة</b> (مُراجَعَةُ تَيّارِ S7 الثانِيَة،
    /// ‏2026-10-12؛ ‏ADR-033 §٧‑ح). تَسأَلُها <see cref="DealsService.AdvanceAsync"/> عَلى الصَفقَةِ الَّتي سَتُكتَب — فَيُرَدُّ بِـ
    /// <see cref="DealAwaitsCarrier"/> كُلُّ بابٍ عامٍّ لا يَحمِلُ جَواباً يُثبِتُ الشُحنَة — وصَفحَةُ الصَفقَة (لا تَرسُمُ «تَأكيد: شَحن»)،
    /// و<see cref="DealsService.AdminPathCanMove"/> (لا يَرسُمُ الاستوديو «→ شَحن»): ما يُرسَمُ هُوَ ما يُقبَل (القاعِدَة ١٢).</para>
    ///
    /// <para><b>لِماذا</b>: بَعدَ أَن تُجيبَ <see cref="ShipAsync"/> بِـ<see cref="ShipmentUnconfirmed"/> تَبقى الصَفقَةُ عِندَ خُطوَتِها
    /// بِمَرجِعَي طَلَبِها، وكانَت الأَبوابُ العامَّةُ الثَلاثَةُ (زِرُّ الصَفحَة، والمَسارُ الإداريّ، والـAPI) تُخرِجُها إلى «شَحن» بِلا
    /// سُؤالِ OTO ولا بوليصَة — العَطَبُ الَّذي وَصَفَه <c>843badc</c> بِبابٍ آخَر (تَوصيفُه <c>e52ba8f</c>). وصَفقَةٌ بِلا طَلَبٍ عِندَ OTO
    /// تَبقى كَما كانَت: «تَأكيد: شَحن» شَحنٌ بِغَيرِ OTO، أَو بَعدَ أَن رَدَّت OTO الطَلَبَ فَلَم يُكتَب مَرجِعُه.</para>
    /// </summary>
    public static bool AwaitsCarrier(Deal deal)
        => DealsPolicy.Next(deal) == DealStage.Shipping && IsPlaced(deal);

    /// <summary>
    /// <para><b>طَلَبُها عِندَ OTO وأَغلَقَته OTO — دالَّةٌ نَقِيَّةٌ واحِدَةٌ لِلصَفحَةِ ولِلنُقطَة</b> (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛
    /// ‏ADR-033 §٧‑ح‑٣‑١): لِلصَفقَةِ طَلَبٌ (<see cref="IsPlaced"/>) وفي <see cref="ClosedRef"/> حالٌ مِن <see cref="OtoClient.TerminalStatuses"/>.
    /// تَسأَلُها <see cref="DealShipAuthorization.Validate"/> — فَتَرُدُّ <c>/ship</c> بِـ<see cref="DealShipAuthorization.OrderClosedAtOto"/> قَبلَ سُؤالِ OTO،
    /// ولا تَرسُمُ الصَفحَةُ «أَكمِل الشَّحن» (<c>canShip</c> مِن البَوّابَةِ نَفسِها): ما يُرسَمُ هُوَ ما يُقبَل (القاعِدَة ١٢).</para>
    /// </summary>
    public static bool ClosedAtOto(Deal deal)
        => IsPlaced(deal)
           && deal.Refs.TryGetValue(ClosedRef, out var status)
           && OtoClient.TerminalStatuses.Contains(status, StringComparer.Ordinal);

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
    /// <para><b>أَتُثبِتُ OTO شُحنَةَ طَلَبِ الصَفقَةِ وبوليصَتَها؟ — دالَّةٌ نَقِيَّة.</b> جَوابُ «Order Tracking» عَن مُعَرِّفِنا
    /// (<see cref="OrderIdOf"/>) بِحالٍ مِن <see cref="OtoClient.ShipmentStatuses"/> ورابِطِ بوليصَةٍ <c>https</c>. وكُلُّ ما سِواه
    /// — لا جَواب، أَو جَوابٌ لِطَلَبٍ آخَر، أَو حالٌ قَبلَ الشُحنَةِ أَو خارِجَها، أَو بِلا بوليصَة — لا يُثبِت.</para>
    ///
    /// <para><b>لِماذا لا يَكفي نَجاحُ «Create Order»</b> (مُراجَعَةُ تَيّارِ S7، ‏2026-10-12؛ تَوصيفُه في <c>843badc</c>): قِسمُه في
    /// وَثيقَةِ OTO — «‏If you send the createShipment: true parameter and the delivery company returns an error during shipment
    /// creation, you must register the shipmentError webhook in order to receive and view the error details»؛ فَالنَجاحُ طَلَبٌ لا
    /// شُحنَة. <b>ولِماذا البوليصَةُ شَرطٌ مَعَ الحال</b>: هي العَينُ الَّتي يَخرُجُ بِها البائِع (القاعِدَة ١٧)، واستِمارَةُ الشَحنِ
    /// تَعِدُ بِها («وتَظهَرُ البوليصَةُ ورابِطُ التَّتَبُّع هُنا») — فَلا «شَحنَ» بِلا بوليصَةٍ يَطبَعُها.</para>
    /// </summary>
    public static bool ShipmentConfirmed(Deal deal, OtoOrderState? state)
        => state is not null
           && string.Equals(state.OrderId, OrderIdOf(deal), StringComparison.Ordinal)
           && state.HasShipment
           && IsHttps(state.LabelUrl);

    /// <summary>
    /// <para><b>يَشحَن</b>: البَوّابَةُ أَوَّلاً عَلى الصَفقَةِ كَما في المَخزَن، ثُمَّ «Create Order» (أَو تَبَنّي
    /// طَلَبٍ تَحمِلُه OTO بِمُعَرِّفِنا — <see cref="PlaceAsync"/>)، ثُمَّ المُعَرِّفانِ في المَراجِع، <b>ثُمَّ «Order Tracking»
    /// — ولا انتِقالَ حَتّى تُثبِتَ OTO شُحنَةَ الطَلَبِ وبوليصَتَها</b> (<see cref="ShipmentConfirmed"/>؛ وإلّا
    /// <see cref="ShipmentUnconfirmed"/> والصَفقَةُ عِندَ خُطوَتِها بِمَرجِعَيها، فَالنَقرَةُ التالِيَةُ تَستَأنِفُ وتَسأَلُ مِن جَديد)،
    /// ثُمَّ الانتِقالُ إلى <c>Shipping</c> بِالبائِعِ مِن المَرحَلَةِ الَّتي رَأَتها البَوّابَة (<c>from</c>)، ثُمَّ رابِطا البوليصَةِ
    /// والتَتَبُّعِ مِن الجَوابِ نَفسِه.</para>
    ///
    /// <para>(وكانَ هُنا — حَتّى مُراجَعَةِ تَيّارِ S7 ‏2026-10-12، وتَوصيفُه في <c>843badc</c> —: «ثُمَّ الانتِقالُ إلى <c>Shipping</c>
    /// بِالبائِعِ مِن المَرحَلَةِ الَّتي رَأَتها البَوّابَة (<c>from</c>)، ثُمَّ — بِلا شَرط — رابِطا البوليصَةِ والتَتَبُّعِ إن أَعطَتهُما
    /// OTO الآن (وإلّا يَأتِيانِ مَعَ إشعارِ الحال)». فَكانَت الصَفقَةُ تَتَحَرَّكُ عَلى نَجاحِ «Create Order» وَحدَه — وهُوَ طَلَبٌ لا
    /// شُحنَة — فَطَلَبٌ لَم تُنشَأ شُحنَتُه يَصيرُ «شَحناً» بِلا بوليصَةٍ ولا استِمارَةٍ ولا رَمز. ولا يُعادُ حَرفٌ فَوقَه.)</para>
    ///
    /// <para><b>والتَرتيبُ مَقصود</b>: المَراجِعُ قَبلَ الانتِقال — لَو رُدَّ الانتِقالُ (أَلغى المُشتَري
    /// أَثناءَ نِداءِ OTO، أَو تَعارَضَت نُسخَة) بَقِيَ مُعَرِّفُ الطَلَبِ مَكتوباً، فَالإعادَةُ تَستَأنِفُ ولا تُنشِئُ
    /// ثانِياً (<see cref="IsPlaced"/>). والإلحاقُ يُعادُ تَطبيقُه عِندَ التَعارُض (‏ADR-036 §٧). <b>وصَفقَةٌ أُلغِيَت أَو تَحَرَّكَت
    /// أَثناءَ نِداءِ OTO لا تُسأَلُ عَنها OTO</b>: انتِقالُها مَردودٌ لا مَحالَة، فَيُجابُ رَمزُه قَبلَ السُؤال. <b>وبَعدَ الأَثَرِ
    /// لا إلغاء</b>: ما يَلي طَلَباً أُنشِئَ يَجري بِـ<see cref="CancellationToken.None"/> — مَرجِعُه يَجِبُ أَن
    /// يَقَع.</para>
    ///
    /// <para><b>وما لا يُغلِقُه — يُقالُ ولا يُبتلَع</b> (‏ADR-036 §٣‑١): الأَثَرُ الخارِجيُّ يَسبِقُ الحِفظَ
    /// المَفحوص؛ فَانتِقالٌ مَردودٌ لِأَنّ المُشتَري أَلغى يَترُكُ طَلَباً عِندَ OTO لِصَفقَةٍ مُلغاة —
    /// مَرجِعُه في الصَفقَة، ويُلغى مِن لَوحَةِ البائِعِ عِندَ OTO. <b>ولا يُسَجَّلُ إشعارُ <c>shipmentError</c></b> في هذِه
    /// المَوجَة: سَبَبُ شُحنَةٍ لَم تُنشَأ يُقرَأُ في لَوحَةِ البائِعِ عِندَ OTO، والصَفقَةُ لا تَتَحَرَّكُ بِدونِها.</para>
    ///
    /// <para>(<b>ولَم يَصدُق آخِرُ ما سَبَقَ حَتّى مُراجَعَةِ تَيّارِ S7 الثانِيَة ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَه</b>: هذِه الدالَّةُ لا تُحَرِّكُها
    /// بِدونِها، وزِرُّ «تَأكيد: شَحن» والمَسارُ الإداريُّ والـAPI كانَت تُحَرِّكُها — تَوصيفُه <c>e52ba8f</c>. وصارَ يَصدُق: <c>AdvanceAsync</c>
    /// تَرُدُّ بِـ<see cref="DealAwaitsCarrier"/> صَفقَةً تَصدُقُ عَلَيها <see cref="AwaitsCarrier"/> ما لَم يَحمِل النِداءُ جَواباً يُثبِتُ
    /// شُحنَتَها — ولا يَحمِلُه إلّا هذا المَسار، بَعدَ <see cref="ShipmentConfirmed"/>.)</para>
    /// </summary>
    public async Task<DealShipResult> ShipAsync(
        string tenantSlug, Guid dealId, DealShipper by, string? city, CancellationToken ct = default)
    {
        var deal = await _deals.LoadAsync(tenantSlug, dealId, ct);

        // الناقِلُ يُسأَلُ بِلا فَكٍّ لِلبَوّابَة، ويُفَكُّ بَعدَها وَحدَها — فَلا يُفَكُّ سِرُّ بائِعٍ لِطَلَبٍ تَرُدُّه الهُوِيَّة (تَيّارُ S8، ‏2026-10-12).
        var refusal = DealShipAuthorization.Validate(deal, by, await CarrierReadyForAsync(tenantSlug, deal, ct));
        if (refusal is not null) return DealShipResult.Refused(refusal.Code, deal);
        if (await CarrierForAsync(tenantSlug, deal!, ct) is not { } carrier)
            return DealShipResult.Refused(DealShipAuthorization.CarrierUnavailable, deal);
        var token = carrier.RefreshToken;

        var seen = deal!.Stage;
        var current = deal;
        if (!IsPlaced(deal))
        {
            var noCity = DealShipAuthorization.ValidateCity(city);
            if (noCity is not null) return DealShipResult.Refused(noCity.Code, deal);

            var placed = await PlaceAsync(token, deal, city!, ct);
            if (placed.Value is not { } order) return DealShipResult.Refused(placed.Failure!.Code, deal);

            await _deals.AttachRefAsync(tenantSlug, dealId, OrderRef, order.OrderId, CancellationToken.None);
            current = await _deals.AttachRefAsync(tenantSlug, dealId, OtoIdRef, order.OtoId, CancellationToken.None);

            // أُلغِيَت أَو تَحَرَّكَت أَثناءَ نِداءِ OTO: انتِقالُها مَردودٌ لا مَحالَة — فَلا تُسأَلُ OTO عَن شُحنَتِها.
            if (current is not { Status: DealStatus.Active } || current.Stage != seen)
                return DealShipResult.Refused(DealsService.ChangedMeanwhile, current);
        }

        // نَجاحُ «Create Order» طَلَبٌ لا شُحنَة: تُسأَلُ OTO قَبلَ أَن تَتَحَرَّكَ الصَفقَة — ولا «شَحنَ» بِلا شُحنَةٍ وبوليصَة.
        var state = (await _oto.GetOrderStatusAsync(token, OrderIdOf(deal), CancellationToken.None)).Value;

        // وطَلَبٌ أَغلَقَته OTO لا يَعودُ إلى شُحنَة: يُكتَبُ حالُه فَيَختَفي «أَكمِل الشَّحن» ويُرَدُّ بِرَمزِه لا بِـ`shipment_unconfirmed` كُلَّ نَقرَة
        // (مُراجَعَةُ المَوجَة 3a، تَيّارُ S8، ‏2026-10-12؛ تَوصيفُه `d1a6b83`). وجَوابٌ لِطَلَبٍ غَيرِ طَلَبِنا لا يُغلِقُ شَيئاً.
        if (state is { IsTerminal: true } && string.Equals(state.OrderId, OrderIdOf(deal), StringComparison.Ordinal))
        {
            current = await _deals.AttachRefAsync(tenantSlug, dealId, ClosedRef, state.Status, CancellationToken.None) ?? current;
            return Logged(tenantSlug, DealShipResult.Refused(DealShipAuthorization.OrderClosedAtOto, current) with { OtoStatus = state.Status });
        }

        if (!ShipmentConfirmed(current, state))
            return Logged(tenantSlug, DealShipResult.Refused(ShipmentUnconfirmed, current) with { OtoStatus = state?.Status });

        // كُلُّ رَدٍّ هُنا «تَغَيَّرَت في الأَثناء»: البَوّابَةُ مَرَّت عَلى ما قُرِئ، والانتِقالُ رَأى غَيرَه
        // (تَعارُضُ نُسخَة، أَو مَرحَلَةٌ غَيرُ `seen`، أَو صَفقَةٌ أُلغِيَت بَينَهُما).
        // وجَوابُ OTO مَعَ النِداء — الانتِقالُ يُعيدُ `ShipmentConfirmed` عَلى الصَفقَةِ الَّتي سَيَكتُبُها، فَلا يَدخُلُ «شَحن» بابٌ بِغَيرِه
        // (`AwaitsCarrier`؛ مُراجَعَةُ تَيّارِ S7 الثانِيَة، ‏2026-10-12).
        var moved = await _deals.AdvanceAsync(tenantSlug, dealId, by.ActorId, by.ActorName, null, CancellationToken.None, seen, state);
        if (!moved.Ok) return DealShipResult.Refused(DealsService.ChangedMeanwhile, moved.Deal);

        return DealShipResult.Shipped(await AttachLinksAsync(tenantSlug, moved.Deal!, state!, CancellationToken.None));
    }

    /// <summary>سَطرُ الرَفضِ بَعدَ سُؤالِ OTO — الرَمزُ وكَلِمَةُ حالِها وَحدَهُما (لا رَمزَ وُصولٍ ولا جِسم)، ومُعَرِّفُ الصَفقَة؛ ويُعيدُ النَتيجَةَ كَما هي.</summary>
    private DealShipResult Logged(string tenantSlug, DealShipResult result)
    {
        _log.LogInformation("[OTO] ship {Slug}/{Deal} ← {Code} (OTO: {Status})",
            tenantSlug, result.Deal?.Id.ToString("N") ?? "—", result.Code, result.OtoStatus ?? "—");
        return result;
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
    ///
    /// <para>(<b>يُلحَقُ ‏2026-10-12 — مُراجَعَةُ المَوجَة 3a، تَيّارُ S8؛ ولا يُعادُ حَرفٌ فَوقَه</b>: ‏5xx مِن نُقطَةِ الرَمزِ صارَ
    /// <c>oto_unreachable</c> لا <c>oto_token_refused</c>، فَيَمُرُّ هُنا إلى سُؤالِ «Order Tracking» — وسُؤالُه يَطلُبُ رَمزاً أَوَّلاً فَيَرتَدُّ
    /// بِالعُطلِ نَفسِه قَبلَ أَن يَبلُغَ نُقطَةَ الحال، ويَعودُ الفَشَلُ الأَصلِيُّ بِرَمزِه. كُلفَتُه نِداءُ رَمزٍ ثانٍ لا طَلَبٌ ولا سُؤال —
    /// مَقيسٌ في <c>DealShipTests</c>.)</para>
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

    /// <summary>كَم مَرَّةً يُقَرَّرُ الانتِقالُ ويُكتَبُ حينَ يُعارِضُه حِفظٌ (‏ADR-036 الخُطوَة 3): الأولى، وإعادَتانِ عَلى ما
    /// كُتِبَ بَعدَها — عَدَدُ <c>AttachAttempts</c> في <see cref="DealsService"/>. وبَعدَها ‏503 فَتُعيدُ OTO الإرسال.</summary>
    public const int NoticeAttempts = 3;

    /// <summary>
    /// <para><b>وارِدُ <c>orderStatus</c></b> (‏ADR-033 §٢‑هـ القاعِدَة ٦): الإشعارُ المَقبولُ إشارَةٌ — يُطابَقُ عَلى الصَفقَةِ
    /// المَخزونَة، <b>ثُمَّ تُسأَلُ OTO عَن الطَلَبِ بِرَمزِ البائِعِ قَبلَ أَيِّ كِتابَة</b>، ثُمَّ يُقَرَّرُ عَلى جَوابِها:
    /// <c>Shipping → Delivered</c> بِالطَرَفِ المُقابِل، ورابِطا البوليصَةِ والتَتَبُّعِ حينَ يَتَغَيَّران. والإشعارُ
    /// المُكَرَّرُ بِلا أَثَر (<see cref="OtoOrderStatusGate.AlreadyDelivered"/>، والرابِطانِ لا يُكتَبانِ بِلا تَغيير).</para>
    ///
    /// <para><b>وعِندَ تَعارُضِ النُسخَةِ يُعادُ القَرارُ لا الكِتابَة</b> (‏ADR-036 الخُطوَة 3 والمُلحَقُ §٩): ما رَدَّه
    /// <see cref="DealsService.AdvanceAsync"/> يُعيدُ الصَفقَةَ كَما في المَخزَنِ الآن، فَتُسأَلُ <see cref="OtoOrderStatusGate.Decide"/>
    /// عَلَيها مِن جَديد — كِتابَةٌ لا تَمَسُّ التَسليمَ (رابِطٌ، مُلاحَظَة) يُكتَبُ بَعدَها الانتِقال، وتَسليمٌ يَدَوِيٌّ سَبَقَه
    /// يُجابُ «سَبَق» بِلا انتِقالٍ ثانٍ، وإلغاءٌ يُجابُ بِرَمزِه. و<c>from = Shipping</c> يَحرُسُ ما بَينَ القَرارِ والحِفظ.
    /// ولا يُناقِضُ ذلك رَفضَ §٤ «إعادَةَ المُحاوَلَةِ داخِلَ الخِدمَة»: عِلَّتُه قَرارُ صاحِبِ نَقرَةٍ اتُّخِذَ عَلى حالٍ قَديم،
    /// وهُنا لا نَقرَةَ ولا قَرارَ قَديم — الحَقيقَةُ مَجلوبَةٌ الآن مِن OTO، والقَرارُ يُحسَبُ عَلى المَخزونِ الآن.</para>
    /// </summary>
    public async Task<OtoNoticeOutcome> ReceiveAsync(
        string tenantSlug, OtoOrderStatusNotice notice, CancellationToken ct = default)
    {
        if (!OtoEnvironment.IsOpen(_opts.Environment)) return new(OtoOrderStatusGate.CarrierUnavailable, null);

        var deal = notice.DealId is { } id ? await _deals.LoadAsync(tenantSlug, id, ct) : null;
        if (OtoOrderStatusGate.Match(notice, deal) is { } unmatched) return new(unmatched.Code, deal);

        // ناقِلُ بائِعِ الصَفقَة — رَمزُه وسِرُّ وارِدِه — ثُمَّ التَوقيعُ بِذلك السِرّ **قَبلَ سُؤالِ OTO وقَبلَ أَيِّ كِتابَة**
        // (تَيّارُ S8، ‏2026-10-12؛ ‏ADR-033 الخُطوَة 7 و§٧‑م). والمُطابَقَةُ قَبلَه لِأَنّ السِرَّ سِرُّ البائِع — قِراءَةٌ لا كِتابَة.
        // (‏2026-10-13، G5 (viii)؛ ‏ADR-033 §٧‑ص‑٩ — ولا يُعادُ حَرفٌ فَوقَه: **والسِرُّ وَحدَه يُفَكُّ قَبلَ التَحَقُّق** — كانَ `CarrierForAsync` يَفُكُّ رَمزَ التَحديثِ
        // الدائِمَ مَعَه، فَإشعارٌ مُزَوَّرٌ بِلا مُصادَقَةٍ يُطلِقُ فَكَّين (تَوصيفُه 9dbc6f9). والرَمزُ يُفَكُّ بَعدَ أَن يَمُرَّ التَوقيع، قُبَيلَ سُؤالِ OTO.)
        if (await ResolveCarrierAsync(tenantSlug, deal!, ct) is not { } carrier || !TryOpenSecret(tenantSlug, carrier, out var secret))
            return new(OtoOrderStatusGate.CarrierUnavailable, deal);
        var at = _clock.GetUtcNow();
        var age = OtoWebhookSignature.AgeMilliseconds(notice.Timestamp, at)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        if (OtoOrderStatusGate.Verify(notice, secret, at) is { } unverified)
        {
            _log.LogInformation("[OTO] orderStatus {Slug}/{Deal} ← {Code} (age {Age} ms)", tenantSlug, notice.OrderId, unverified.Code, age);
            return new(unverified.Code, deal);
        }
        // والمُتَحَقَّقُ مِنه يُطبَعُ عُمرُه أَيضاً (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع): لا يُرَدُّ إشعارٌ عَلى عُمرِه بَعدَ اليَوم،
        // فَبِهذا السَطرِ يُقاسُ عَلى staging كَم تَتَأَخَّرُ OTO بَينَ الحَدَثِ والإرسال («ب» S8·١٠ (١)) — ولا سِرَّ فيه ولا رَمز.
        _log.LogInformation("[OTO] orderStatus {Slug}/{Deal} ← verified (age {Age} ms)", tenantSlug, notice.OrderId, age);

        // الإشعارُ إشارَةٌ لا بُرهان: حالُ الطَلَبِ مِن OTO نَفسِها قَبلَ أَيِّ كِتابَة — بِرَمزٍ يُفَكُّ الآنَ لا قَبلَ التَوقيع (G5 (viii)).
        if (!TryOpenToken(tenantSlug, carrier, out var token))
            return new(OtoOrderStatusGate.CarrierUnavailable, deal);
        var fetched = await _oto.GetOrderStatusAsync(token, notice.OrderId, ct);
        if (fetched.Value is not { } state)
            return new(OtoOrderStatusGate.StateUnconfirmed, deal, fetched.Failure!.Code);

        var outcome = await DeliverAsync(tenantSlug, deal!, state);
        return outcome.Code is OtoOrderStatusGate.OrderMismatch or DealsService.ChangedMeanwhile || outcome.Deal is not { } now
            ? outcome
            : outcome with { Deal = await AttachLinksAsync(tenantSlug, now, state, CancellationToken.None) };
    }

    /// <summary>القَرارُ ثُمَّ الانتِقال — ويُعادُ القَرارُ عَلى المَخزونِ بَعدَ كُلِّ رَدّ: <see cref="NoticeAttempts"/> كِتابَةً
    /// عَلى الأَكثَر، وقَرارٌ أَخيرٌ عَلى ما كُتِبَ بَعدَ آخِرِها (فَتَسليمٌ سَبَقَ يُجابُ «سَبَق» لا ‏503).
    /// <para>(<b>ويُلحَقُ ‏2026-10-12 — مُراجَعَةُ المَوجَة 3a، تَيّارُ S8؛ ‏ADR-033 §٧‑ط</b>: صَفقَةٌ عِندَ «تأكيد» بِطَلَبِها يُثبِتُ الجَوابُ شُحنَتَها
    /// وبوليصَتَها (<see cref="OtoOrderStatusGate.ShipsFirst"/>) تَدخُلُ «شَحن» أَوَّلاً — بِالبائِعِ طَرَفاً مُقابِلاً ومِن المَرحَلَةِ الَّتي قُرِئَت
    /// وبِالجَوابِ مَحمولاً، كَما تُدخِلُها «أَكمِل الشَّحن» — ثُمَّ يُسأَلُ التَسليمُ عَلى ما كُتِب. والكِتابَةُ مِن الحِصَّةِ نَفسِها، ولا انتِقالَ
    /// لِصَفقَةٍ عِندَ «شَحن»: لا تَصدُقُ عَلَيها <see cref="AwaitsCarrier"/>.)</para></summary>
    private async Task<OtoNoticeOutcome> DeliverAsync(string tenantSlug, Deal deal, OtoOrderState state)
    {
        var current = deal;
        var shipped = false;
        for (var attempt = 1; ; attempt++)
        {
            if (OtoOrderStatusGate.ShipsFirst(current, state))
            {
                if (attempt > NoticeAttempts) return new(DealsService.ChangedMeanwhile, current);
                var entered = await _deals.AdvanceAsync(tenantSlug, current.Id, current.CounterpartyId!.Value,
                    current.CounterpartyName ?? "", null, CancellationToken.None, current.Stage, state);
                if (entered.Deal is not { } now) return new(OtoOrderStatusGate.OrderUnknown, null);
                shipped |= entered.Ok;
                current = now;
                continue;
            }

            if (OtoOrderStatusGate.Decide(current, state) is { } refusal)
                return new(shipped && refusal.Code == OtoOrderStatusGate.NotDelivered ? OtoOrderStatusGate.Shipped : refusal.Code, current);
            if (attempt > NoticeAttempts) return new(DealsService.ChangedMeanwhile, current);

            var moved = await _deals.AdvanceAsync(tenantSlug, current.Id, current.CounterpartyId!.Value,
                current.CounterpartyName ?? "", null, CancellationToken.None, DealStage.Shipping);
            if (moved.Ok) return new(OtoOrderStatusGate.Delivered, moved.Deal);
            if (moved.Deal is not { } stored) return new(OtoOrderStatusGate.OrderUnknown, null);
            current = stored;
        }
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

/// <summary>
/// <para><b>ناقِلُ بائِعِ صَفقَةٍ مَفكوكاً</b> (تَيّارُ S8، ‏2026-10-12): رَمزُ تَحديثِ حِسابِه عِندَ OTO، وسِرُّ الوارِدِ الَّذي تُوَقِّعُ بِه
/// إشعاراتِ طَلَباتِه — أَو <c>null</c> حَيثُ لا سِرّ (الاحتِياطُ مِن التَهيئَة) — ومَصدَرُهُما. لا يُطبَعُ ولا يُكتَبُ: <c>ToString</c> مُعَتَّم.</para>
/// <para>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (iv) — ولا يُعادُ حَرفٌ فَوقَه: <b>و«مَصدَرُهُما» سَقَط</b> — كانَ حَقلُ <c>FromVendorEntry</c> لا يَقرَؤُه
/// وَقتَ التَشغيلِ إلّا <c>ToString</c> هذا، وثَلاثَةُ صُفوفٍ تَقيسُه وتَقيسُ الرَمزَ نَفسَه (القاعِدَة ١). والمَصدَرُ يُقرَأُ مِن الرَمزِ حَيثُ يُحتاج.)</para>
/// </summary>
public sealed record VendorCarrier(string RefreshToken, string? WebhookSecret)
{
    public override string ToString() => $"VendorCarrier(secret={(WebhookSecret is null ? "none" : "sealed")})";
}
