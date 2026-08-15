using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Kit.Reports.Server;

/// <summary>
/// <para>مُعالِجات رَسائِل البَلاغات — <b>لا نِقاط HTTP</b>.</para>
///
/// <para><b>ما زال مِن هُنا:</b> <c>GET /{slug}/api/reports/pending</c> —
/// <b>بِلا حارِس</b>، وكانَت تُسَلِّم <b>طابور الإشراف كامِلاً</b>
/// لِمَجهول: أَسماء المُبَلِّغين ونُصوص بَلاغاتِهم وأَهدافَها، حَتّى
/// مِئَتَي بَلاغ في الطَلَب الواحِد. وبِصِفر مُستَهلِك مَقيس — لَوحَة
/// الإشراف الحَيَّة صَفحَة Razor تَقرَأ داخِل جَلسَة مَحروسَة.</para>
/// </summary>
public static class ReportHandlers
{
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
