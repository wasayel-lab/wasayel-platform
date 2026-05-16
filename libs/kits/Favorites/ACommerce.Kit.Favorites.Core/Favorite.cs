namespace ACommerce.Kit.Favorites;

/// <summary>
/// مُفَضَّلَة — مَرجِع بَسيط (userId, listingId). الـ Id مُرَكَّب كَ
/// "{userId}:{listingId}" حَتى يَكون upsert سَهلاً وَتَكرار مُستَحيلاً.
/// </summary>
public sealed class Favorite
{
    public string Id { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid ListingId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;

    public static string MakeId(Guid userId, Guid listingId) => $"{userId:N}:{listingId:N}";
}
