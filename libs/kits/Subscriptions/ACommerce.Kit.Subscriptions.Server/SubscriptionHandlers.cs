using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Subscriptions.Server;

public static class SubscriptionHandlers
{
    [WolverineGet("/{slug}/api/plans")]
    public static async Task<IReadOnlyList<Plan>> ListPlans(IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Plan>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Plan>().Where(p => p.IsActive).OrderBy(p => p.Price).ToListAsync();
    }

    [WolverinePost("/{slug}/api/subscriptions/start")]
    public static async Task<Subscription?> Start(
        StartSubscription cmd, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.LightweightSession(tenantCtx.Slug);
        var plan = await s.LoadAsync<Plan>(cmd.PlanId);
        if (plan is null) return null;
        var ev = new SubscriptionCreated(
            Guid.NewGuid(), cmd.UserId, cmd.PlanId, plan.ListingsQuota, plan.DaysPeriod, DateTime.UtcNow);
        s.Events.StartStream<Subscription>(ev.Id, ev);
        await s.SaveChangesAsync();
        return await s.Events.AggregateStreamAsync<Subscription>(ev.Id);
    }

    [WolverineGet("/{slug}/api/subscriptions/{userId:guid}")]
    public static async Task<Subscription?> MySubscription(
        Guid userId, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return null;
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Subscription>()
            .Where(x => x.UserId == userId && x.Status == "active")
            .OrderByDescending(x => x.StartsAt).FirstOrDefaultAsync();
    }
}
