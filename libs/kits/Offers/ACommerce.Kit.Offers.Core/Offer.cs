namespace ACommerce.Kit.Offers;

// ═══════════════════════════════════════════════════════════════════
// نَموذَج التَفاوُض بِالعُروض (InDrive-style)
// ──────────────────────────────────────────────────────────────────
// الراكِب يَنشُر إعلاناً (Listing) — السائِقون يُقَدِّمون عُروضاً مُضادَّة
// عَلَيه (Offer) بِسِعر مُختَلِف + مَوقِعِهم. الراكِب يَختار عَرضاً واحِداً
// → باقي العُروض تَنغَلِق تِلقائيّاً.
//
// النَمَط مُستَقِلّ عَن Listings كَيت: Offers.Core يَعتَمِد فَقَط عَلى
// SDK الأَساسيّ. الربط: Offer.ListingId يُشير إلى Listing.Id. الـ
// كَيت لا يُعَدِّل Listing — يَستَخدِم doc مُنفَصِل ListingMatch لِتَسجيل
// أَيّ عَرض تَمّ قَبولُه.
// ═══════════════════════════════════════════════════════════════════

public sealed record OfferSubmitted(
    System.Guid Id,
    System.Guid ListingId,
    System.Guid OffererId,
    string OffererName,
    decimal Price,
    string? Message,
    double Lat,
    double Lng,
    System.DateTime ExpiresAt,
    System.DateTime At);

public sealed record OfferWithdrawn(System.Guid Id, System.DateTime At);
public sealed record OfferAccepted(System.Guid Id, System.DateTime At);
public sealed record OfferRejected(System.Guid Id, System.DateTime At);
public sealed record OfferExpired(System.Guid Id, System.DateTime At);

/// <summary>الحالات: pending → accepted | rejected | withdrawn | expired.</summary>
public enum OfferStatus { Pending, Accepted, Rejected, Withdrawn, Expired }

public sealed class Offer
{
    public System.Guid Id { get; set; }
    public System.Guid ListingId { get; set; }
    public System.Guid OffererId { get; set; }
    public string OffererName { get; set; } = "";
    public decimal Price { get; set; }
    public string? Message { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public System.DateTime SubmittedAt { get; set; }
    public System.DateTime ExpiresAt { get; set; }
    public System.DateTime? ResolvedAt { get; set; }

    public void Apply(OfferSubmitted e)
    {
        Id = e.Id; ListingId = e.ListingId;
        OffererId = e.OffererId; OffererName = e.OffererName;
        Price = e.Price; Message = e.Message;
        Lat = e.Lat; Lng = e.Lng;
        ExpiresAt = e.ExpiresAt; SubmittedAt = e.At;
        Status = OfferStatus.Pending;
    }
    public void Apply(OfferWithdrawn e) { Status = OfferStatus.Withdrawn; ResolvedAt = e.At; }
    public void Apply(OfferAccepted  e) { Status = OfferStatus.Accepted;  ResolvedAt = e.At; }
    public void Apply(OfferRejected  e) { Status = OfferStatus.Rejected;  ResolvedAt = e.At; }
    public void Apply(OfferExpired   e) { Status = OfferStatus.Expired;   ResolvedAt = e.At; }
}

/// <summary>
/// نَتيجَة المُطابَقَة عَلى مُستَوى الإعلان — doc بِـ Id = ListingId.
/// يُنشَأ/يُحَدَّث عِندَ قَبول عَرض. وُجودُه يَعني "الإعلان مُتَطابِق".
/// </summary>
public sealed class ListingMatch
{
    public System.Guid Id { get; set; }            // = ListingId
    public System.Guid AcceptedOfferId { get; set; }
    public System.Guid OffererId { get; set; }
    public string OffererName { get; set; } = "";
    public decimal AcceptedPrice { get; set; }
    public double OffererLat { get; set; }
    public double OffererLng { get; set; }
    public System.DateTime MatchedAt { get; set; }
}

// ─── Helpers ────────────────────────────────────────────────────────
public static class OfferHelpers
{
    /// <summary>المَسافَة بِالكيلومِترات بِصيغَة haversine — لِعَرض
    /// "السائِق على بُعد ٣.٢ كم". الـ Earth radius = 6371.</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371.0;
        double ToRad(double d) => d * System.Math.PI / 180.0;
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2)
              + System.Math.Cos(ToRad(lat1)) * System.Math.Cos(ToRad(lat2))
              * System.Math.Sin(dLng / 2) * System.Math.Sin(dLng / 2);
        return 2 * R * System.Math.Asin(System.Math.Sqrt(a));
    }
}
