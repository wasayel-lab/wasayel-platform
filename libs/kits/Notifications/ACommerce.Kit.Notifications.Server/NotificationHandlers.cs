using ACommerce.Kit.Realtime;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Notifications.Server;

public static class NotificationHandlers
{
    /// <summary>
    /// إنشاء إشعار جَديد. Wolverine يَكتُب المُستَنَد +
    /// يُرسِله كَ <see cref="BroadcastToUser"/> عَبر outbox (cascade).
    /// </summary>
    public static async Task<BroadcastToUser> Send(
        SendNotification cmd, IDocumentSession session, ITenantContext tenantCtx)
    {
        var notif = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = cmd.UserId,
            Type = cmd.Type,
            Title = cmd.Title,
            Body = cmd.Body,
            RelatedUrl = cmd.RelatedUrl,
            At = DateTime.UtcNow
        };
        session.Store(notif);
        await session.SaveChangesAsync();
        return new BroadcastToUser(tenantCtx.Slug, cmd.UserId, "notification", notif);
    }

    [WolverineGet("/{slug}/api/notifications")]
    public static async Task<IReadOnlyList<Notification>> List(
        IDocumentStore store, ITenantContext tenantCtx, Guid userId)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Notification>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Notification>()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.At)
            .Take(50)
            .ToListAsync();
    }

    [WolverinePost("/{slug}/api/notifications/{id}/read")]
    public static async Task<bool> MarkRead(
        Guid id, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return false;
        await using var s = store.LightweightSession(tenantCtx.Slug);
        var n = await s.LoadAsync<Notification>(id);
        if (n is null) return false;
        if (!n.IsRead) { n.IsRead = true; s.Store(n); await s.SaveChangesAsync(); }
        return true;
    }
}
