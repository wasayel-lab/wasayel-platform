namespace ACommerce.Kit.SavedSearches;

using ACommerce.Kit.Listings;

/// <summary>
/// بَحث مَحفوظ — يُخبِر المُستَخدِم عَن إعلانات جَديدَة تُطابِق
/// مَعاييره. السائِق (Injez) يَحفَظ "مَشاوير في حَدّة بِأَكثَر مِن 100",
/// النِظام يُولِّد إشعاراً كُلَّما نُشِر إعلان مُطابِق.
///
/// <para>المَعايير قاموس مَفاتيح/قِيَم تُقارَن مَع <c>Listing.Attributes</c>
/// (تَطابُق نَصِّي حَرفيّ، case-insensitive). الـ CategorySlug/City/Min/Max
/// حُقول مُتَخَصِّصَة لِأَنَّها شائِعَة + مُعَرَّفَة بِأَنواعها.</para>
/// </summary>
public sealed class SavedSearch
{
    public System.Guid Id { get; set; }
    public System.Guid UserId { get; set; }
    public string Label { get; set; } = "بَحث جَديد";

    public string? CategorySlug { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    /// <summary>مَعايير عَلى الخَصائِص الديناميكِيَّة. مَفتاح = كود الخاصِّيَّة
    /// (vehicle_type)، قيمَة = القيمَة المَطلوبَة (economy). تَطابُق
    /// case-insensitive حَرفيّ.</summary>
    public System.Collections.Generic.Dictionary<string, string> Criteria { get; set; } = new();

    public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
    public bool IsEnabled { get; set; } = true;

    /// <summary>هَل هذا الـ search يُطابِق إعلاناً مُعَيَّناً؟ يَستَخدِمه
    /// الـ POST handler لِإنشاء الإعلانات لِمُطابَقَة كُلّ البَحوث المَحفوظَة
    /// وَإنشاء إشعارات. يَعمَل بِالكامِل في الذاكِرَة.</summary>
    public bool Matches(Listing listing)
    {
        if (!IsEnabled) return false;
        if (!string.IsNullOrEmpty(CategorySlug) &&
            !string.Equals(CategorySlug, listing.CategorySlug, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(City) &&
            !string.Equals(City, listing.City, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(District) &&
            !string.Equals(District, listing.District, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (MinPrice.HasValue && listing.Price < MinPrice.Value) return false;
        if (MaxPrice.HasValue && listing.Price > MaxPrice.Value) return false;

        foreach (var (k, v) in Criteria)
        {
            if (!listing.Attributes.TryGetValue(k, out var actual)) return false;
            if (!string.Equals(actual, v, System.StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>وَصف نَصِّي لِلبَحث — يَظهَر في القائِمَة + الإشعار.</summary>
    public string Summary()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(CategorySlug)) parts.Add(CategorySlug);
        if (!string.IsNullOrEmpty(City))         parts.Add(City);
        if (!string.IsNullOrEmpty(District))     parts.Add(District);
        if (MinPrice.HasValue)                   parts.Add($"≥ {MinPrice:N0}");
        if (MaxPrice.HasValue)                   parts.Add($"≤ {MaxPrice:N0}");
        foreach (var (k, v) in Criteria) parts.Add($"{k}={v}");
        return parts.Count == 0 ? "(بِلا مَعايير)" : string.Join(" · ", parts);
    }
}
