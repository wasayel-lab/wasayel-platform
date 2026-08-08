using System.Collections.Concurrent;
using ACommerce.Kit.Maps;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Delivery;

/// <summary>
/// مُزَوِّد تَوصيل وَهميّ — يُحاكي دَورَة الحَياة كامِلَة: عَرض سِعر،
/// حَجز، تَعيين مَندوب، حَركَة عَبر مَراحِل (DriverAssigned →
/// PickedUp → InTransit → Delivered) عَلى مُؤَقِّت. مُفيد لِلتَجرِبَة
/// التَّفاعُلِيَّة. التَّخزين في الذاكِرَة (يَختَفي بَعد إعادَة التَّشغيل).
/// </summary>
public sealed class MockDeliveryProvider : IDeliveryProvider
{
    private readonly IMapsProvider _maps;
    private readonly ConcurrentDictionary<string, MockShipment> _store = new();
    private static readonly string[] DriverNames =
        { "أَحمَد المَندوب", "خالِد التَّوصيل", "سَعيد السَّريع", "فَيصَل النَّقل" };

    public MockDeliveryProvider(IMapsProvider maps) => _maps = maps;
    public string ProviderName => "mock";

    public async Task<DeliveryQuote> QuoteAsync(DeliveryRequest req, CancellationToken ct = default)
    {
        var route = await _maps.RouteAsync(req.Pickup, req.Dropoff, ct);
        var d = route?.DistanceKm ?? MockMapsProvider.Haversine(req.Pickup, req.Dropoff);
        var sizeMultiplier = req.Package.Type.ToLowerInvariant() switch
        {
            "small"  => 1.0, "medium" => 1.3, "large"  => 1.7, "bulky"  => 2.5,
            _ => 1.0
        };
        var price = Math.Max(15m, (decimal)(d * 3.5 * sizeMultiplier)); // ≥15 ريال
        return new DeliveryQuote(
            QuoteId: Guid.NewGuid().ToString("N"),
            PriceSar: Math.Round(price, 2),
            EtaMinutes: route?.DurationMinutes ?? (int)Math.Max(15, d * 1.5),
            DistanceKm: Math.Round(d, 1),
            ExpiresAt: DateTime.UtcNow.AddMinutes(10));
    }

    public async Task<DeliveryShipment> BookAsync(
        DeliveryRequest req, string quoteId, CancellationToken ct = default)
    {
        var quote = await QuoteAsync(req, ct);
        var id = Guid.NewGuid().ToString("N");
        var ship = new MockShipment
        {
            Id = id,
            Request = req,
            Price = quote.PriceSar,
            DriverName = DriverNames[Random.Shared.Next(DriverNames.Length)],
            DriverPhone = $"+9665{Random.Shared.Next(10_000_000, 99_999_999)}",
            Timeline = { new(ShipmentStatus.Created, DateTime.UtcNow) }
        };
        _store[id] = ship;
        ScheduleAdvance(id); // اِبدَأ المُحاكاة في الخَلفِيَّة
        return new DeliveryShipment(id, ShipmentStatus.Created, ProviderName, quote.PriceSar, ship.Timeline[0].At);
    }

    public Task<DeliveryTracking?> TrackAsync(string shipmentId, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(shipmentId, out var s)) return Task.FromResult<DeliveryTracking?>(null);
        return Task.FromResult<DeliveryTracking?>(new(
            s.Id, s.Status, s.CurrentLocation,
            s.DriverName, s.DriverPhone,
            EtaMinutes: s.Status switch
            {
                ShipmentStatus.DriverAssigned => 12,
                ShipmentStatus.PickedUp       => 8,
                ShipmentStatus.InTransit      => 4,
                _ => null
            },
            s.Timeline.ToList()));
    }

    public Task<bool> CancelAsync(string shipmentId, string reason, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(shipmentId, out var s)) return Task.FromResult(false);
        if (s.Status is ShipmentStatus.Delivered or ShipmentStatus.Cancelled) return Task.FromResult(false);
        s.Status = ShipmentStatus.Cancelled;
        s.Timeline.Add(new(ShipmentStatus.Cancelled, DateTime.UtcNow, reason));
        return Task.FromResult(true);
    }

    private void ScheduleAdvance(string id)
    {
        // تَدَرُّج: Created → DriverAssigned (5s) → PickedUp (15s) → InTransit (25s) → Delivered (45s).
        // أَزمِنَة قَصيرَة لِلتَجرِبَة. في الإنتاج، يَأتي مِن webhooks فِعليَّة.
        _ = Task.Run(async () =>
        {
            await AdvanceAfter(id, TimeSpan.FromSeconds(5),  ShipmentStatus.DriverAssigned);
            await AdvanceAfter(id, TimeSpan.FromSeconds(10), ShipmentStatus.PickedUp);
            await AdvanceAfter(id, TimeSpan.FromSeconds(10), ShipmentStatus.InTransit);
            await AdvanceAfter(id, TimeSpan.FromSeconds(20), ShipmentStatus.Delivered);
        });
    }

    private async Task AdvanceAfter(string id, TimeSpan delay, ShipmentStatus to)
    {
        await Task.Delay(delay);
        if (!_store.TryGetValue(id, out var s)) return;
        if (s.Status == ShipmentStatus.Cancelled) return;
        s.Status = to;
        s.Timeline.Add(new(to, DateTime.UtcNow));
        // تَحديث المَوقِع: في InTransit يَكون بَين الـ pickup و dropoff.
        s.CurrentLocation = to switch
        {
            ShipmentStatus.DriverAssigned => Interpolate(s.Request.Pickup, s.Request.Pickup, 0),
            ShipmentStatus.PickedUp       => s.Request.Pickup,
            ShipmentStatus.InTransit      => Interpolate(s.Request.Pickup, s.Request.Dropoff, 0.5),
            ShipmentStatus.Delivered      => s.Request.Dropoff,
            _ => s.CurrentLocation
        };
    }

    private static Location Interpolate(Location a, Location b, double t)
        => new(a.Lat + (b.Lat - a.Lat) * t, a.Lng + (b.Lng - a.Lng) * t);

    private sealed class MockShipment
    {
        public string Id { get; set; } = "";
        public DeliveryRequest Request { get; set; } = default!;
        public decimal Price { get; set; }
        public ShipmentStatus Status { get; set; } = ShipmentStatus.Created;
        public Location? CurrentLocation { get; set; }
        public string DriverName { get; set; } = "";
        public string DriverPhone { get; set; } = "";
        public List<DeliveryEvent> Timeline { get; } = new();
    }
}

public static class DeliveryServiceExtensions
{
    public static IServiceCollection AddMockDelivery(this IServiceCollection services)
    {
        services.AddSingleton<IDeliveryProvider, MockDeliveryProvider>();
        return services;
    }
}
