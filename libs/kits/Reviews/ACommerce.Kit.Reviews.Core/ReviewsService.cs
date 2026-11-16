using Marten;

namespace ACommerce.Kit.Reviews;

/// <summary>خَدمَة Reviews — ابعَث، رُدّ، أَخفِ، اجمَع المُلَخَّص.</summary>
public sealed class ReviewsService
{
    private readonly IDocumentStore _store;
    public ReviewsService(IDocumentStore store) => _store = store;

    /// <summary>
    /// <para>يَكتُبُ تَقييماً <b>جَديداً</b>.</para>
    /// <para><b>بِـ<c>Insert</c> لا <c>Store</c>، ومُعَرِّفٌ يَختارُه المُنادي اختِيارِيّاً</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (iv)؛
    /// ‏ADR-036 §١٢‑٤): <paramref name="id"/> بَعدَ <paramref name="ct"/> عَمداً فَالنِداءاتُ القائِمَةُ لا تَتَحَرَّك، و<c>null</c> مُعَرِّفٌ عَشوائِيٌّ
    /// كَما كان. ومُعَرِّفٌ مُعطًى كُتِبَ قَبلُ يُرمى عِندَ الحِفظِ (<c>DocumentAlreadyExistsException</c>) ولا يُدهَس — فَيَصيرُ «مَرَّةً واحِدَة»
    /// قَيداً في القاعِدَةِ لِمَن يَختارُ مُعَرِّفاً حَتمِيّاً (<c>DealReviews</c>)، بِلا فِهرِسٍ ولا تَرحيل. ولِلمُعَرِّفِ العَشوائِيِّ <c>Insert</c>
    /// و<c>Store</c> سَواء.</para>
    /// <para><b>و«مَوثوقٌ» قَرارٌ يُمَرَّرُ لا وُجودُ مُعَرِّف</b> (‏2026-10-29، قَرارُ المالِك د33 «٣»): كانَ كُلُّ تَقييمٍ مَربوطٍ بِصَفقَةٍ يُوسَمُ
    /// <c>VerifiedTransaction</c> (‏<c>dealId is not null</c>) — فَبائِعٌ مَشى صَفقَةَ نَقدٍ وَحدَه يُقَيِّمُ مُشتَرِيَها «مَوثوقاً». الوَسمُ الآنَ
    /// <paramref name="verified"/> يَقرَؤُه المُنادي مِن صَفقَتِه، مَعَ بَقاءِ رَبطِ التَقييمِ بِها؛ وغِيابُه «غَيرُ مَوثوق» (يُخطِئُ نَحوَ الأَمان)، ولا وَسمَ
    /// بِلا صَفقَة. وبَعدَ <paramref name="id"/> عَمداً: النِداءاتُ القائِمَةُ لا تَتَحَرَّك.</para>
    /// </summary>
    public async Task<Review> SubmitAsync(
        string tenantSlug, Guid targetUserId, string targetName,
        Guid authorId, string authorName,
        int rating, string body,
        Guid? dealId = null, string dealPattern = "",
        CancellationToken ct = default, Guid? id = null, bool verified = false)
    {
        if (rating < 1) rating = 1;
        if (rating > 5) rating = 5;
        var r = new Review
        {
            Id = id ?? Guid.NewGuid(), TenantSlug = tenantSlug,
            TargetUserId = targetUserId, TargetUserName = targetName,
            AuthorId = authorId, AuthorName = authorName,
            Rating = rating, Body = body,
            DealId = dealId, DealPattern = dealPattern,
            VerifiedTransaction = dealId is not null && verified,
            At = DateTime.UtcNow
        };
        await using var s = _store.LightweightSession(tenantSlug);
        s.Insert(r);
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
