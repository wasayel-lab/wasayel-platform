using ACommerce.Kit.Maps;

namespace ACommerce.Kit.Delivery;

/// <summary>
/// مُجَرَّد مُزَوِّد التَّوصيل — تَسعير، حَجز، تَتَبُّع، إلغاء. تُحقَن
/// عَبر DI، تُسمَح بِتَبديل مُزَوِّد فِعليّ (Saee/Mrsool/Aramex) بِلا
/// لَمس المَنطِق. الواجِهَة تُعَبِّر عَن "حَزمَة" (Package) بِأَبعاد +
/// نُقطَة استِلام + نُقطَة تَسليم.
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
