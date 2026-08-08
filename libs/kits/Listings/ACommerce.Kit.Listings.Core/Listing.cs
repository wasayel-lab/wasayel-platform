namespace ACommerce.Kit.Listings;

// ─── Events ───────────────────────────────────────────────────────────
/// <summary>
/// كلّ event يَحتَوي ما يَكفي لإعادَة بِناء الحالَة. عَنَدنا
/// <c>TenantSlug</c> مَع كلّ event لأنّ Marten conjoined tenancy يَضَع
/// عَمود <c>tenant_id</c> لكن نَحتَفِظ بالـ slug صراحَة لتَوضيح Wolverine
/// listeners وَلتَجَنُّب الاعتِماد عَلى ambient context عند الاستِعلام.
/// </summary>
public sealed record ListingCreated(
    Guid Id,
    string TenantSlug,
    string Title,
    string? Description,
    decimal Price,
    string CategorySlug,
    string? City,
    string? District,
    Dictionary<string, string> Attributes,
    DateTime At);

public sealed record ListingEdited(
    Guid Id,
    string? Title,
    string? Description,
    decimal? Price,
    string? CategorySlug,
    string? City,
    string? District,
    Dictionary<string, string>? Attributes,
    DateTime At);

public sealed record ListingDeleted(Guid Id, DateTime At);
public sealed record ListingViewed(Guid Id, Guid? ViewerId, DateTime At);

/// <summary>تَحديث صُوَر الإعلان — يَستَبدِل كامِل القائِمَة.</summary>
public sealed record ListingMediaSet(Guid Id, List<string> MediaUrls, DateTime At);

/// <summary>تَحديث شارات مُمَيَّز/مُوَثَّق — مِن لَوحَة الإدارَة أَو
/// اشتِراك مَدفوع.</summary>
public sealed record ListingFlagsSet(Guid Id, bool? IsFeatured, bool? IsVerified, DateTime At);

/// <summary>إخفاء/إظهار إشرافيّ — مُنفَصِل عَن Delete العادي.
/// المالِك المُديريّ يَقدِر يُخفي إعلاناً مِن البَحث بِدون حَذف فِعليّ.</summary>
public sealed record ListingModerated(Guid Id, bool Hidden, string Reason, Guid ModeratorId, DateTime At);

// ─── Aggregate (read model مُحَدَّث inline عَبر projection) ──────────
public sealed class Listing
{
    public Guid Id { get; set; }
    public string TenantSlug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string CategorySlug { get; set; } = "";
    public string? City { get; set; }
    public string? District { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = new();
    /// <summary>صُوَر الإعلان — قائِمَة روابِط مَخرَج <c>IFileStorage</c>
    /// (مَثَلاً <c>/uploads/tenants/ashare/listings/{id}/0.jpg</c>). أَوَّل
    /// عُنصُر هو الصورَة الرَئيسيَّة (cover) المُستَخدَمَة في البِطاقَة.</summary>
    public List<string> MediaUrls { get; set; } = new();
    /// <summary>إعلان مُمَيَّز (يَظهَر في كاروسيل «مُمَيَّز» في الرَئيسيَّة).
    /// يُضبَط مِن لَوحَة الإدارَة أَو عَبر اشتِراك مَدفوع.</summary>
    public bool IsFeatured { get; set; }
    /// <summary>إعلان مُوَثَّق (المالِك حَقَّقَ هويَّتَه عَبر نَفاذ/يَقين).</summary>
    public bool IsVerified { get; set; }
    public int ViewCount { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>أُخفي مِن البَحث العامّ بِقَرار إشرافيّ. لا يَزال
    /// المالِك يَراه في "إعلاناتي" لِفَهم السَبَب.</summary>
    public bool IsHiddenByModerator { get; set; }
    public string? ModerationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public void Apply(ListingCreated e)
    {
        Id = e.Id; TenantSlug = e.TenantSlug;
        Title = e.Title; Description = e.Description;
        Price = e.Price; CategorySlug = e.CategorySlug;
        City = e.City; District = e.District;
        Attributes = new(e.Attributes);
        CreatedAt = e.At; UpdatedAt = e.At;
    }

    public void Apply(ListingEdited e)
    {
        if (e.Title is not null) Title = e.Title;
        if (e.Description is not null) Description = e.Description;
        if (e.Price.HasValue) Price = e.Price.Value;
        if (e.CategorySlug is not null) CategorySlug = e.CategorySlug;
        if (e.City is not null) City = e.City;
        if (e.District is not null) District = e.District;
        if (e.Attributes is not null) Attributes = new(e.Attributes);
        UpdatedAt = e.At;
    }

    public void Apply(ListingDeleted e)
    {
        IsDeleted = true;
        UpdatedAt = e.At;
    }

    public void Apply(ListingViewed e) => ViewCount++;

    public void Apply(ListingMediaSet e)
    {
        MediaUrls = new(e.MediaUrls);
        UpdatedAt = e.At;
    }

    public void Apply(ListingFlagsSet e)
    {
        if (e.IsFeatured.HasValue) IsFeatured = e.IsFeatured.Value;
        if (e.IsVerified.HasValue) IsVerified = e.IsVerified.Value;
        UpdatedAt = e.At;
    }

    public void Apply(ListingModerated e)
    {
        IsHiddenByModerator = e.Hidden;
        ModerationReason = e.Hidden ? e.Reason : null;
        UpdatedAt = e.At;
    }
}

// ─── Commands ─────────────────────────────────────────────────────────
public sealed record CreateListing(
    string Title, string? Description, decimal Price,
    string CategorySlug, string? City, string? District,
    Dictionary<string, string>? Attributes);

public sealed record EditListing(
    Guid Id,
    string? Title, string? Description, decimal? Price,
    string? CategorySlug, string? City, string? District,
    Dictionary<string, string>? Attributes);

public sealed record DeleteListing(Guid Id);
public sealed record ViewListing(Guid Id, Guid? ViewerId);

// ─── Queries ──────────────────────────────────────────────────────────
public sealed record ListingsByCategory(string CategorySlug);
public sealed record ListingsSearch(string? Query, string? CategorySlug, decimal? MaxPrice);
