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
    public int ViewCount { get; set; }
    public bool IsDeleted { get; set; }
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
