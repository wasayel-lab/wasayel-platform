using ACommerce.Kit.Auth;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Profiles.Server;

public sealed record UpdateProfile(Guid UserId, string FullName);

public static class ProfileHandlers
{
    [WolverineGet("/{slug}/api/profile/{userId:guid}")]
    public static async Task<User?> Get(Guid userId, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.LoadAsync<User>(userId);
    }

    [WolverinePost("/{slug}/api/profile/update")]
    public static async Task<User?> Update(UpdateProfile cmd, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.LightweightSession(tenantCtx.Slug);
        var user = await s.LoadAsync<User>(cmd.UserId);
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(cmd.FullName)) user.FullName = cmd.FullName.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync();
        return user;
    }
}
