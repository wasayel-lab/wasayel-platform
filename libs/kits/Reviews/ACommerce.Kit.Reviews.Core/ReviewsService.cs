using Marten;

namespace ACommerce.Kit.Reviews;

/// <summary>خَدمَة Reviews — ابعَث، رُدّ، أَخفِ، اجمَع المُلَخَّص.</summary>
public sealed class ReviewsService
{
    private readonly IDocumentStore _store;
    public ReviewsService(IDocumentStore store) => _store = store;

    public async Task<Review> SubmitAsync(
        string tenantSlug, Guid targetUserId, string targetName,
        Guid authorId, string authorName,
        int rating, string body,
        Guid? dealId = null, string dealPattern = "",
        CancellationToken ct = default)
    {
        if (rating < 1) rating = 1;
        if (rating > 5) rating = 5;
        var r = new Review
        {
            Id = Guid.NewGuid(), TenantSlug = tenantSlug,
            TargetUserId = targetUserId, TargetUserName = targetName,
            AuthorId = authorId, AuthorName = authorName,
            Rating = rating, Body = body,
            DealId = dealId, DealPattern = dealPattern,
            VerifiedTransaction = dealId is not null,
            At = DateTime.UtcNow
        };
        await using var s = _store.LightweightSession(tenantSlug);
        s.Store(r);
        await s.SaveChangesAsync(ct);
        return r;
    }

    public async Task<Review?> RespondAsync(
        string tenantSlug, Guid reviewId, string response, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var r = await s.LoadAsync<Review>(reviewId, ct);
        if (r is null) return null;
        r.Response = response;
        r.ResponseAt = DateTime.UtcNow;
        s.Store(r);
        await s.SaveChangesAsync(ct);
        return r;
    }

    public async Task<Review?> HideAsync(
        string tenantSlug, Guid reviewId, string reason, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var r = await s.LoadAsync<Review>(reviewId, ct);
        if (r is null) return null;
        r.Hidden = true; r.HiddenReason = reason;
        s.Store(r);
        await s.SaveChangesAsync(ct);
        return r;
    }

    public async Task<ReviewSummary> SummaryAsync(
        string tenantSlug, Guid userId, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var list = await s.Query<Review>()
            .Where(r => r.TargetUserId == userId).ToListAsync(ct);
        return ReviewSummary.From(userId, list);
    }

    public async Task<List<Review>> ListForUserAsync(
        string tenantSlug, Guid userId, int take = 50, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(tenantSlug);
        var list = await s.Query<Review>()
            .Where(r => r.TargetUserId == userId && !r.Hidden)
            .OrderByDescending(r => r.At).Take(take).ToListAsync(ct);
        return list.ToList();
    }

    public async Task<bool> HasReviewedAsync(
        string tenantSlug, Guid authorId, Guid? dealId, CancellationToken ct = default)
    {
        if (dealId is null) return false;
        await using var s = _store.QuerySession(tenantSlug);
        return await s.Query<Review>()
            .AnyAsync(r => r.AuthorId == authorId && r.DealId == dealId, ct);
    }
}
