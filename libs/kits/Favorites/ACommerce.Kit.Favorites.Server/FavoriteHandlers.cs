using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Favorites.Server;

public static class FavoriteHandlers
{
    [WolverineGet("/{slug}/api/favorites")]
    public static async Task<IReadOnlyList<Favorite>> ListMine(
        IDocumentStore store, ITenantContext tenantCtx, Guid userId)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Favorite>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Favorite>()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.At)
            .Take(100)
            .ToListAsync();
    }
}
