using ACommerce.Platform.Shared;
using Marten;
using Marten.Events.Projections;
using Wolverine.Http;
using Wolverine.Marten;

namespace ACommerce.Kit.Listings.Server;

/// <summary>
/// Handlers الإعلانات. مُلاحَظَة في الـ multi-tenancy:
/// <list type="bullet">
///   <item>Marten conjoined-tenancy يُضيف <c>tenant_id</c> لِكُلّ event.</item>
///   <item>الـ session مَحصور بـ tenant عَن طَريق
///         <c>store.LightweightSession(tenantSlug)</c> داخِل الـ handler.</item>
///   <item>الـ slug يَأتي من <see cref="ITenantContext"/> الذي يَملَؤه
///         middleware من URL.</item>
/// </list>
/// </summary>
public static class ListingHandlers
{
    [WolverinePost("/{slug}/api/listings")]
    public static async Task<ListingCreated> Create(
        CreateListing cmd,
        IDocumentStore store,
        ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved)
            throw new InvalidOperationException("tenant_not_resolved");

        await using var session = store.LightweightSession(tenantCtx.Slug);
        var id = Guid.NewGuid();
        var ev = new ListingCreated(
            id, tenantCtx.Slug, cmd.Title, cmd.Description, cmd.Price,
            cmd.CategorySlug, cmd.City, cmd.District,
            cmd.Attributes ?? new(), DateTime.UtcNow);
        session.Events.StartStream<Listing>(id, ev);
        await session.SaveChangesAsync();
        return ev;
    }

    [WolverinePost("/{slug}/api/listings/{id}/edit")]
    public static async Task<ListingEdited?> Edit(
        Guid id,
        EditListing cmd,
        IDocumentStore store,
        ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var session = store.LightweightSession(tenantCtx.Slug);
        var current = await session.Events.AggregateStreamAsync<Listing>(id);
        if (current is null || current.IsDeleted) return null;

        var ev = new ListingEdited(
            id, cmd.Title, cmd.Description, cmd.Price,
            cmd.CategorySlug, cmd.City, cmd.District,
            cmd.Attributes, DateTime.UtcNow);
        session.Events.Append(id, ev);
        await session.SaveChangesAsync();
        return ev;
    }

    [WolverinePost("/{slug}/api/listings/{id}/delete")]
    public static async Task<ListingDeleted?> Delete(
        Guid id, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var session = store.LightweightSession(tenantCtx.Slug);
        var current = await session.Events.AggregateStreamAsync<Listing>(id);
        if (current is null || current.IsDeleted) return null;
        var ev = new ListingDeleted(id, DateTime.UtcNow);
        session.Events.Append(id, ev);
        await session.SaveChangesAsync();
        return ev;
    }

    [WolverineGet("/{slug}/api/listings/{id}")]
    public static async Task<Listing?> Get(
        Guid id, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var session = store.QuerySession(tenantCtx.Slug);
        return await session.Events.AggregateStreamAsync<Listing>(id);
    }

    [WolverineGet("/{slug}/api/listings")]
    public static async Task<IReadOnlyList<Listing>> List(
        IDocumentStore store, ITenantContext tenantCtx,
        string? category = null, int take = 50)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Listing>();
        await using var session = store.QuerySession(tenantCtx.Slug);
        var q = session.Query<Listing>().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(x => x.CategorySlug == category);
        var results = await q.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync();
        return results;
    }
}
