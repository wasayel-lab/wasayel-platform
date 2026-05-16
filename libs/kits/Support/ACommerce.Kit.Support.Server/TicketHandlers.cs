using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Support.Server;

public static class TicketHandlers
{
    [WolverinePost("/{slug}/api/support/open")]
    public static async Task<Ticket?> Open(OpenTicket cmd, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.LightweightSession(tenantCtx.Slug);
        var ev = new TicketCreated(Guid.NewGuid(), cmd.AuthorId, cmd.AuthorName,
            cmd.Subject, cmd.Body, DateTime.UtcNow);
        s.Events.StartStream<Ticket>(ev.Id, ev);
        await s.SaveChangesAsync();
        return await s.Events.AggregateStreamAsync<Ticket>(ev.Id);
    }

    [WolverinePost("/{slug}/api/support/{id:guid}/reply")]
    public static async Task<TicketReplied?> Reply(Guid id, ReplyTicket cmd,
        IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.LightweightSession(tenantCtx.Slug);
        var ev = new TicketReplied(id, Guid.NewGuid(), cmd.AuthorName, cmd.FromStaff,
            cmd.Body, DateTime.UtcNow);
        s.Events.Append(id, ev);
        await s.SaveChangesAsync();
        return ev;
    }

    [WolverineGet("/{slug}/api/support/mine")]
    public static async Task<IReadOnlyList<Ticket>> Mine(IDocumentStore store, ITenantContext tenantCtx, Guid userId)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Ticket>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Ticket>()
            .Where(t => t.AuthorId == userId)
            .OrderByDescending(t => t.UpdatedAt).Take(50).ToListAsync();
    }
}
