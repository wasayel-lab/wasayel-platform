using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Reports.Server;

public static class ReportHandlers
{
    [WolverineGet("/{slug}/api/reports/pending")]
    public static async Task<IReadOnlyList<Report>> Pending(IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return Array.Empty<Report>();
        await using var s = store.QuerySession(ctx.Slug);
        return await s.Query<Report>()
            .Where(r => r.Status == ReportStatus.Pending || r.Status == ReportStatus.UnderReview)
            .OrderByDescending(r => r.At).Take(200).ToListAsync();
    }

    public static async Task<Guid> Handle(SubmitReport cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return Guid.Empty;
        await using var s = store.LightweightSession(ctx.Slug);
        var r = new Report
        {
            Id = Guid.NewGuid(),
            ReporterId = cmd.ReporterId, ReporterName = cmd.ReporterName,
            TargetType = cmd.TargetType, TargetId = cmd.TargetId,
            ReasonCode = cmd.ReasonCode, Details = cmd.Details,
            Status = ReportStatus.Pending, At = DateTime.UtcNow
        };
        s.Store(r);
        await s.SaveChangesAsync();
        return r.Id;
    }

    public static async Task Handle(ResolveReport cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        var r = await s.LoadAsync<Report>(cmd.ReportId);
        if (r is null) return;
        r.Status = ReportStatus.Resolved;
        r.ModeratorId = cmd.ModeratorId;
        r.ModeratorAction = cmd.Action;
        r.ResolvedAt = DateTime.UtcNow;
        s.Store(r);
        await s.SaveChangesAsync();
    }

    public static async Task Handle(RejectReport cmd, IDocumentStore store, ITenantContext ctx)
    {
        if (!ctx.IsResolved) return;
        await using var s = store.LightweightSession(ctx.Slug);
        var r = await s.LoadAsync<Report>(cmd.ReportId);
        if (r is null) return;
        r.Status = ReportStatus.Rejected;
        r.ModeratorId = cmd.ModeratorId;
        r.ModeratorAction = cmd.Reason;
        r.ResolvedAt = DateTime.UtcNow;
        s.Store(r);
        await s.SaveChangesAsync();
    }
}
