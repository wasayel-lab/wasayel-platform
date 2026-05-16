using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace ACommerce.Kit.Realtime.Server;

/// <summary>
/// SignalR hub بَسيط. كلّ مُستَخدِم يَدخُل في group باسم "tenant:user".
/// </summary>
public sealed class RealtimeHub : Hub
{
    private static readonly ConcurrentDictionary<string, (string TenantSlug, Guid UserId)> Connections = new();

    public override Task OnConnectedAsync()
    {
        var httpCtx = Context.GetHttpContext();
        var token = httpCtx?.Request.Query["token"].ToString();
        var parsed = ACommerce.Kit.Auth.Server.AuthHandlers.ParseToken(token);
        if (parsed is null) { Context.Abort(); return Task.CompletedTask; }
        var (userId, tenantSlug, _) = parsed.Value;
        Connections[Context.ConnectionId] = (tenantSlug, userId);
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantSlug, userId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Connections.TryRemove(Context.ConnectionId, out _);
        return Task.CompletedTask;
    }

    public static string GroupName(string tenantSlug, Guid userId) => $"{tenantSlug}:{userId}";
}

/// <summary>Wolverine handler يَستَهلِك BroadcastToUser ويُمَرِّره عَبر SignalR.</summary>
public static class RealtimeBroadcastHandler
{
    public static Task Handle(BroadcastToUser cmd, IHubContext<RealtimeHub> hub)
        => hub.Clients
            .Group(RealtimeHub.GroupName(cmd.TenantSlug, cmd.UserId))
            .SendAsync(cmd.Channel, cmd.Payload);
}
