namespace ACommerce.Kit.Maps;

/// <summary>
/// مُجَرَّد مُزَوِّد الخَرائِط — geocoding + routing + embed URL. تُحقَن
/// عَبر DI، يَسمَح بِتَبديل OSM/Google/MapBox بِسَطر واحِد دون لَمس
/// المَنطِق. النَّتائج مُحايِدَة (records).
/// </summary>
public interface IMapsProvider
{
    string ProviderName { get; }

    /// <summary>تَحويل عُنوان مَكتوب إلى إحداثيّات.</summary>
    Task<GeocodingResult?> GeocodeAsync(string address, CancellationToken ct = default);

    /// <summary>تَحويل إحداثيّات إلى عُنوان نَصّيّ.</summary>
    Task<string?> ReverseGeocodeAsync(Location point, CancellationToken ct = default);

    /// <summary>حِساب مَسار بَين نُقطَتَين (مَسافَة + زَمَن + خَطّ مُتَعَدِّد).</summary>
    Task<RouteResult?> RouteAsync(Location from, Location to, CancellationToken ct = default);

    /// <summary>URL لِـ embed خَريطَة (iframe) عَلى نُقطَة مَركَزِيَّة.</summary>
    string EmbedUrl(Location center, int zoom = 14);

    /// <summary>URL لِفَتح الخَريطَة لِلتَوَجُّه (Apple/Google maps).</summary>
    string DirectionsUrl(Location to, Location? from = null);
}

public sealed record GeocodingResult(Location Location, string FormattedAddress);

public sealed record RouteResult(
    double DistanceKm,
    int DurationMinutes,
    /// <summary>encoded polyline أَو null لَو المُزَوِّد لا يُرجِعه.</summary>
    string? PolylineEncoded);
