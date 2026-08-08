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
    System.DateTime At,
    System.Collections.Generic.Dictionary<string, string>? Attributes = null);

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

    /// <summary>خَصائِص العَرض الديناميكِيَّة — مَأخوذَة مِن form بِالبادِئَة
    /// <c>attr_</c> عِندَ التَّقديم. تَختَلِف عَن خَصائِص الإعلان: السائِق
    /// قَد يَذكُر "مَركَبَتي 7-رُكّاب" أَو "وَقت وُصولي 8 دَقائِق" كَ
    /// إجابات مُهَيكَلَة لا نَصّ حُرّ في الرِسالَة.</summary>
    public System.Collections.Generic.Dictionary<string, string> Attributes { get; set; } = new();

    public void Apply(OfferSubmitted e)
    {
        Id = e.Id; ListingId = e.ListingId;
        OffererId = e.OffererId; OffererName = e.OffererName;
        Price = e.Price; Message = e.Message;
        Lat = e.Lat; Lng = e.Lng;
        ExpiresAt = e.ExpiresAt; SubmittedAt = e.At;
        Status = OfferStatus.Pending;
        if (e.Attributes is not null) Attributes = new(e.Attributes);
    }
    public void Apply(OfferWithdrawn e) { Status = OfferStatus.Withdrawn; ResolvedAt = e.At; }
    public void Apply(OfferAccepted  e) { Status = OfferStatus.Accepted;  ResolvedAt = e.At; }
    public void Apply(OfferRejected  e) { Status = OfferStatus.Rejected;  ResolvedAt = e.At; }
    public void Apply(OfferExpired   e) { Status = OfferStatus.Expired;   ResolvedAt = e.At; }
}

/// <summary>حالات الرِحلَة بَعدَ القَبول:
/// active = جارِيَة (السائِق مُلتَزَم) — لا يَستَطيع تَقديم عُروض جَديدَة.
/// completed = انتَهَت بِنَجاح (وَصَل لِنُقطَة الوُصول).
/// aborted = قُطِعَت (السائِق أَو الراكِب أَلغَى) — السائِق يَدخُل
///           فَترَة تَهدِئَة قَبل أَن يَستَطيع تَقديم عَرض جَديد.
/// </summary>
public enum TripStatus { Active, Completed, Aborted }

/// <summary>
/// نَتيجَة المُطابَقَة عَلى مُستَوى الإعلان — doc بِـ Id = ListingId.
/// يُنشَأ/يُحَدَّث عِندَ قَبول عَرض. وُجودُه يَعني "الإعلان مُتَطابِق".
/// يَتَتَبَّع أَيضاً دَورَة حَياة الرِحلَة بَعد القَبول.
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

    /// <summary>الحالَة الحالِيَّة لِلرِحلَة. الافتراضي Active فَور القَبول.</summary>
    public TripStatus Status { get; set; } = TripStatus.Active;

    /// <summary>وَقت الانتِهاء (completed/aborted). null = لا تَزال Active.</summary>
    public System.DateTime? ResolvedAt { get; set; }

    /// <summary>مَن أَنهَى/قَطَع: "offerer" (السائِق) أَو "owner" (الراكِب).</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>اختِياريّ: مُذَكِّرَة عَن سَبَب الإلغاء.</summary>
    public string? AbortReason { get; set; }

    /// <summary>وَقت تَأكيد السائِق وُصولَه لِنُقطَة الانطِلاق. null = لَم
    /// يَصِل بَعد. لا يُغَيِّر الـ Status — تَبقَى Active، لكِنّ UI يَتَغَيَّر
    /// (تَظهَر "تَأكيد انتِهاء" بَدَل "أَنا واصِل").</summary>
    public System.DateTime? ArrivedAt { get; set; }
    public double ArrivedLat { get; set; }
    public double ArrivedLng { get; set; }
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
