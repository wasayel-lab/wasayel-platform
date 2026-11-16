using ACommerce.Kit.Maps;

namespace ACommerce.Kit.Delivery;

/// <summary>
/// مُجَرَّد مُزَوِّد التَّوصيل — تَسعير، حَجز، تَتَبُّع، إلغاء. تُحقَن
/// عَبر DI، تُسمَح بِتَبديل مُزَوِّد فِعليّ (Saee/Mrsool/Aramex) بِلا
/// لَمس المَنطِق. الواجِهَة تُعَبِّر عَن "حَزمَة" (Package) بِأَبعاد +
/// نُقطَة استِلام + نُقطَة تَسليم.
///
/// <para><b>ولِماذا تَبقى بَعدَ حَذفِ مُحاكيها — عَلى نَسَقِ حُكمِ ‏ADR-018 §٣‑أ في <c>Cache.Core</c></b> (‏ADR-033 §٢‑هـ
/// الخُطوَة 9، ‏2026-10-12). مُنذُ <c>ec807a6</c> لا يُنَفِّذُها شَيءٌ ولا يُسَجِّلُها شَيء: حُذِفَ <c>MockDeliveryProvider</c>
/// وسَطرُ تَسجيلِه، وعَميلُ OTO (<see cref="OtoClient"/>) بِجِوارِها في العُدَّةِ لا تَحتَها. <b>فَقَولُ السَطرِ الأَوَّلِ أَعلاه
/// «تُحقَن عَبر DI» لَم يَعُد يَصدُق — ولا يُعادُ حَرفٌ فَوقَه.</b> وتَبقى لِسَبَبَينِ مَكتوبَين:</para>
/// <list type="number">
///   <item><b>هي الطَرَفُ المَقيسُ لِصَفِّ <c>delivery</c> في جَدوَلِ القُدُراتِ التِسع</b> (<c>ProviderCapabilities</c> في
///   <c>ProviderDefinition.cs</c>): <c>ProviderSelectionCharacterizationTests</c> يَقرَأُ هذا المِلَفَّ مَصدَراً لِلصَفِّ بِلا سَطرِ
///   تَسجيل، عَلى نَسَقِ صَفَّي <c>notifications</c> و<c>cache</c>. والجَدوَلُ قيمَتُه أَنَّه يَعُدُّ ما لا يُبلَغ — فَحَذفُها
///   يُصَغِّرُ عَدَدَ ما يُقاسُ بِحُجَّةِ أَنَّه لا يُقاس. والصَفُّ لا يُحذَف: تَعريفُ OTO يُعلِنُ القُدرَة (‏ADR-033 الخُطوَة 6).</item>
///   <item><b>ولا مُستَهلِكَ لَها اليَوم — مَقيس</b>: <c>grep -rn IDeliveryProvider libs apps tests</c> = هذا المِلَفّ، وذِكرُها في
///   مَعجَمِ <c>ProviderDefinition.cs</c>، وتَعليقٌ في <c>OtoClient.cs</c>، ومَرساةُ الصَفِّ وتَعليقٌ في اختِبارَين — صِفرُ تَنفيذٍ
///   وصِفرُ تَسجيلٍ وصِفرُ مُستَهلِكٍ وَقتَ التَشغيل. و<c>DeliveryInterfaceStaysTests</c> يَحمَرُّ يَومَ يَتَغَيَّرُ ذلك، فَيُعادُ
///   النَظَرُ في هذا السَبَبِ ولا يَبقى بَعدَه.</item>
/// </list>
/// <para><b>وشَرطُ مَصيرِها مَكتوبٌ لا مَتروك</b>: (أ) <b>الاستِخراجُ لا التَمديد</b> — يَومَ يوجَدُ ثَلاثَةُ مُستَهلِكينَ وَقتَ
/// التَشغيلِ يَحتاجونَ تَبديلَ الناقِل (القاعِدَة ١) تُستَخرَجُ واجِهَةٌ مِن <see cref="OtoClient"/> بِما تَحتاجُه البوليصَة —
/// طَلَبٌ بِشُحنَتِه، وحالُه، ورابِطاه (والشَرطُ مَكتوبٌ في رَأسِ ذاكَ المِلَفّ) — لا مِن هذِه الواجِهَةِ الَّتي تُنَمذِجُ مَندوباً
/// بِمَوقِعِه؛ ويَومَئِذٍ تُحذَفُ هذِه ويَصيرُ طَرَفُ الصَفِّ الواجِهَةَ المُستَخرَجَة. (ب) <b>أَو الحَذفُ مَعَ الصَفّ</b> — يَومَ يُقَرَّرُ
/// أَنّ القُدرَةَ لا تُطلَبُ فَيُحذَفُ صَفُّ <c>delivery</c> مِن الجَدوَلِ «بِيَدٍ مَرئِيَّةٍ في مُراجَعَة» (‏ADR-018 §٣‑أ) — تَتبَعُه.
/// <b>ولا تُحذَفُ صامِتَة، ولا تُنَفَّذُ لِلناقِلِ ولا تُمَدَّد</b> (‏ADR-033 §٢‑هـ القاعِدَة ١).</para>
/// <para><b>ولا <c>Decide</c> لِمُزَوِّدِ شَحن</b> (‏ADR-014 §٢‑ج): النَمَطُ يَختارُ تَنفيذاً لِواجِهَةٍ واحِدَة، وهذِه بِلا تَنفيذ،
/// و<see cref="OtoClient"/> لا يُنَفِّذُها — «فَمِفتاحٌ يَقبَل قيمَةً لا تُسَجِّل شَيئاً شَرطٌ لا يَكذِبُ أَبَداً — وذلك أَسوَأُ مِن
/// غِيابِه». يُضافُ المِفتاحُ يَومَ يوجَدُ ناقِلانِ يُختارُ بَينَهُما.</para>
/// </summary>
public interface IDeliveryProvider
{
    string ProviderName { get; }

    /// <summary>اِحسُب تَسعيرَ تَوصيل بِناءً عَلى الشَّحنَة والمَسافَة.</summary>
    Task<DeliveryQuote> QuoteAsync(DeliveryRequest req, CancellationToken ct = default);

    /// <summary>اِحجِز التَّوصيل (تَنشَأ shipment بِـ Id لِلتَتَبُّع).</summary>
    Task<DeliveryShipment> BookAsync(DeliveryRequest req, string quoteId, CancellationToken ct = default);

    /// <summary>اِجلِب الحالَة + مَوقِع المَندوب الحاليّ.</summary>
    Task<DeliveryTracking?> TrackAsync(string shipmentId, CancellationToken ct = default);

    /// <summary>اِلغِ شَحنَة قَبل التَّسليم.</summary>
    Task<bool> CancelAsync(string shipmentId, string reason, CancellationToken ct = default);
}

public sealed record DeliveryRequest(
    Location Pickup,
    Location Dropoff,
    PackageSpec Package,
    Contact PickupContact,
    Contact DropoffContact,
    DateTime? ScheduledFor = null,
    string? Notes = null);

public sealed record PackageSpec(
    string Type,             // small | medium | large | bulky
    decimal? WeightKg = null,
    decimal? ValueSar = null);

public sealed record Contact(string Name, string Phone);

public sealed record DeliveryQuote(
    string QuoteId,
    decimal PriceSar,
    int EtaMinutes,
    double DistanceKm,
    DateTime ExpiresAt);

public enum ShipmentStatus
{
    Created,         // أُنشِئَت
    DriverAssigned,  // عُيِّن مَندوب
    PickedUp,        // اِستَلَم المَندوب
    InTransit,       // في الطَّريق
    Delivered,       // وُصِلَت
    Cancelled,       // أُلغِيَت
    Failed           // فَشِلَت
}

public sealed record DeliveryShipment(
    string ShipmentId,
    ShipmentStatus Status,
    string ProviderName,
    decimal PriceSar,
    DateTime CreatedAt);

public sealed record DeliveryTracking(
    string ShipmentId,
    ShipmentStatus Status,
    Location? CurrentLocation,
    string? DriverName,
    string? DriverPhone,
    int? EtaMinutes,
    List<DeliveryEvent> Timeline);

public sealed record DeliveryEvent(
    ShipmentStatus Status, DateTime At, string? Note = null);
