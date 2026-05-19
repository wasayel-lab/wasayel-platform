namespace ACommerce.Kit.Maps;

/// <summary>
/// مَوقِع جُغرافيّ بِسيط — Lat/Lng + اسم اختِياريّ يَظهَر لِلمُستَخدِم
/// (مَثَلاً عُنوان مَكتوب يَدَويّاً أَو نَتيجَة بَحث). الـ kit لا يَتَدَخَّل
/// في التَّخزين: الـ Listings تَضَع Lat/Lng كَ خَصائِص ديناميكِيَّة عادِيَّة
/// (pickup_lat / pickup_lng / dropoff_lat / dropoff_lng / lat / lng …).
/// </summary>
public sealed record Location(double Lat, double Lng, string? Label = null)
{
    /// <summary>قيمَة مَوقِع غَير مُعَيَّن (0,0) — يُستَخدَم كَ sentinel
    /// لِلتَّمييز بَين "لَم يُحَدَّد" و"مَركَز الكُرَة".</summary>
    public static readonly Location Empty = new(0, 0);

    public bool IsEmpty => Lat == 0 && Lng == 0;

    /// <summary>تَنسيق "lat,lng" مَع 6 خانات عَشريَّة (≈10 سَم دِقَّة).</summary>
    public string ToCoordString() =>
        IsEmpty ? "" : $"{Lat.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)},"
                     + $"{Lng.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}";

    public static Location? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var parts = s.Split(',', 2);
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out var lng))
            return null;
        return new Location(lat, lng);
    }
}

/// <summary>روابِط مُفيدَة لِخَدَمات OSM — لا API key، لا حِسابات.</summary>
public static class OsmLinks
{
    /// <summary>iframe لِعَرض مَوقِع واحِد كَ خَريطَة صَغيرَة. zoom افتراضي
    /// 14 (مَدينَة/حَيّ). يَستَخدِم openstreetmap.org/export/embed.</summary>
    public static string EmbedUrl(double lat, double lng, int zoom = 14)
    {
        var (s, w, n, e) = BoundingBox(lat, lng, zoom);
        return $"https://www.openstreetmap.org/export/embed.html"
             + $"?bbox={Inv(w)}%2C{Inv(s)}%2C{Inv(e)}%2C{Inv(n)}"
             + $"&layer=mapnik&marker={Inv(lat)}%2C{Inv(lng)}";
    }

    /// <summary>رابِط فَتح المَوقِع في OSM (لِلنَّقر مِن قَلِم/نَصّ).</summary>
    public static string ViewUrl(double lat, double lng, int zoom = 16) =>
        $"https://www.openstreetmap.org/?mlat={Inv(lat)}&mlon={Inv(lng)}#map={zoom}/{Inv(lat)}/{Inv(lng)}";

    /// <summary>صُندوق إحاطَة تَقريبيّ بِناءً عَلى zoom — كافٍ لِـ embed.</summary>
    private static (double S, double W, double N, double E) BoundingBox(double lat, double lng, int zoom)
    {
        var span = 360.0 / System.Math.Pow(2, zoom);
        return (lat - span / 2, lng - span, lat + span / 2, lng + span);
    }

    private static string Inv(double d) =>
        d.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
