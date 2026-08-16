using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Maps;

/// <summary>
/// مُزَوِّد خَرائِط وَهميّ — يُولِّد نَتائِج مَعقولَة دون الاتِّصال بِخِدمَة
/// خارِجيَّة. مُناسِب لِلتَجرِبَة وَلِدَورات CI. يَستَخدِم Haversine
/// لِلمَسافَة وتَخمين 60km/h لِلزَّمَن.
/// </summary>
public sealed class MockMapsProvider : IMapsProvider
{
    public string ProviderName => "mock";

    public Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken ct = default)
    {
        // مَواقِع شَهيرَة في السعودية لِتَوليد نَتائج واقِعِيَّة.
        var cities = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase)
        {
            ["الرياض"]  = new(24.7136, 46.6753),
            ["جدة"]    = new(21.4858, 39.1925),
            ["الدمام"] = new(26.4207, 50.0888),
            ["مكة"]    = new(21.3891, 39.8579),
            ["المدينة"] = new(24.5247, 39.5692),
            ["riyadh"]  = new(24.7136, 46.6753),
            ["jeddah"]  = new(21.4858, 39.1925),
        };
        foreach (var (key, loc) in cities)
            if (address.Contains(key, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<GeocodingResult?>(new(loc, $"{address}، السعودية"));
        // افتراضيّ: وَسَط الرياض مَع إزاحَة بِناء عَلى hash.
        //
        // والتَجزِئَةُ **صَريحَة** لا `string.GetHashCode`: بَذرَةُ تِلكَ
        // تَتَبَدَّل مَع كُلّ عَمَلِيَّة في dotnet، فَكانَ نَفسُ العُنوان
        // يُعطي إحداثِيَّتَين مُختَلِفَتَين بَعدَ كُلّ إقلاع — ومُزَوِّدٌ
        // وَهميّ غَيرُ حَتميّ يَجعَل كُلَّ اختِبارٍ يَعتَمِد عَلَيه
        // مُتَقَلِّباً بِلا سَبَبٍ ظاهِر. نَفسُ العِلَّة ونَفسُ العِلاج في
        // `ACommerce.Kit.Auth.NafathNames`.
        var h = (int)Fnv1a(address);
        var lat = 24.7 + ((h & 0xFF) / 5000.0);
        var lng = 46.7 + (((h >> 8) & 0xFF) / 5000.0);
        return Task.FromResult<GeocodingResult?>(new(new(lat, lng), $"{address}"));
    }

    public Task<string?> ReverseGeocodeAsync(Location point, CancellationToken ct = default)
    {
        // تَخمين المَدينَة الأَقرَب.
        var (lat, lng) = (point.Lat, point.Lng);
        string city;
        if (lat > 26.0 && lng > 50.0) city = "الدمام";
        else if (lat > 24.5 && lng > 46.5) city = "الرياض";
        else if (lat > 21.0 && lng < 40.0) city = "جدة";
        else city = "السعودية";
        return Task.FromResult<string?>($"{city} — {point.ToCoordString()}");
    }

    public Task<RouteResult?> RouteAsync(Location from, Location to, CancellationToken ct = default)
    {
        var d = Haversine(from, to);
        // أَقَلّ تَفصيلاً مِن المَسار الفِعليّ، لَكِن تَقدير واقِعيّ.
        var minutes = (int)Math.Max(2, d * 60.0 / 40.0); // 40 km/h في المُدُن
        return Task.FromResult<RouteResult?>(new(d, minutes, PolylineEncoded: null));
    }

    public string EmbedUrl(Location center, int zoom = 14)
        => OsmLinks.EmbedUrl(center.Lat, center.Lng, zoom);

    public string DirectionsUrl(Location to, Location? from = null)
        => from is null
            ? $"https://www.openstreetmap.org/?mlat={to.Lat}&mlon={to.Lng}#map={14}/{to.Lat}/{to.Lng}"
            : $"https://www.openstreetmap.org/directions?from={from.Lat},{from.Lng}&to={to.Lat},{to.Lng}";

    /// <summary>مَسافَة دائِرَة عُظمى بِالكيلومِترات — صيغَة Haversine.</summary>
    public static double Haversine(Location a, Location b)
    {
        const double R = 6371.0;
        double Rad(double deg) => deg * Math.PI / 180.0;
        var dLat = Rad(b.Lat - a.Lat);
        var dLng = Rad(b.Lng - a.Lng);
        var sa = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
               + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat))
               * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Sqrt(sa));
    }

    /// <summary>‏FNV-1a ‏32-بِت فَوق بايتات UTF-8 — ثابِتَة عَبر
    /// العَمَلِيّات والمِنَصّات، بِخِلاف تَجزِئَة الإطار.</summary>
    internal static uint Fnv1a(string s)
    {
        var h = 2166136261u;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(s))
        {
            h ^= b;
            h *= 16777619u;
        }
        return h;
    }
}

/// <summary>DI: <c>services.AddMockMaps();</c></summary>
public static class MapsServiceExtensions
{
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddMockMaps(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddSingleton<IMapsProvider, MockMapsProvider>();
        return services;
    }
}
