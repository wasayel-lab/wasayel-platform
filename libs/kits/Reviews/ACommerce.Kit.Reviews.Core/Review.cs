namespace ACommerce.Kit.Reviews;

/// <summary>
/// <para>تَقييم لِمُستَخدِم (بائِع، سائِق، مُؤَجِّر، …) صادِر مِن مُستَخدِم
/// آخَر بَعد إكمال صَفقَة (Deal). Marten doc تَحت tenant.</para>
///
/// <para><b>تَقييم مُتَبادَل</b>: في الـ trip pattern مَثَلاً، الراكِب يُقَيِّم
/// السائِق وَالسائِق يُقَيِّم الراكِب — يَنتُج عَن صَفقَة واحِدَة سِجِلّان.
/// نُمَيِّز بِـ <c>AuthorId/TargetUserId</c>.</para>
///
/// <para><b>VerifiedTransaction</b>: تَقييم مَربوط بِـ <c>DealId</c> فِعليّ
/// اكتَمَلَ — شارَة ثِقَة. التَّقييم بِدون deal مُمكِن نَظَريّاً لَكِنّه
/// غَير مُوَثَّق.</para>
/// </summary>
public sealed class Review
{
    public Guid Id { get; set; }
    public string TenantSlug { get; set; } = "";

    public Guid TargetUserId { get; set; }       // مَن يُقَيَّم
    public string TargetUserName { get; set; } = "";

    public Guid AuthorId { get; set; }            // مَن يُقَيِّم
    public string AuthorName { get; set; } = "";

    /// <summary>١-٥ نُجوم.</summary>
    public int Rating { get; set; }
    public string Body { get; set; } = "";

    /// <summary>سياق التَّقييم — Deal الَّتي وَلَّدَته (لِلتَّحَقُّق).</summary>
    public Guid? DealId { get; set; }
    public string DealPattern { get; set; } = "";

    public bool VerifiedTransaction { get; set; }

    /// <summary>رَدّ المالِك (المُقَيَّم) — اختياريّ. يُعرَض تَحت التَّقييم.</summary>
    public string? Response { get; set; }
    public DateTime? ResponseAt { get; set; }

    /// <summary>Hidden = أُخفي بِقَرار إشرافيّ (مَنصَّة أَو مالِك التَّطبيق).</summary>
    public bool Hidden { get; set; }
    public string? HiddenReason { get; set; }

    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>إجماليّ تَقييمات مُستَخدِم — projection بَسيطَة في الذاكِرَة
/// (لِلسُّرعَة، يَكفي حَتَّى ١٠٠٠ تَقييم لِمُستَخدِم؛ بَعدها projection).</summary>
public sealed record ReviewSummary(
    Guid UserId,
    int Count,
    double AverageRating,
    int Stars5, int Stars4, int Stars3, int Stars2, int Stars1)
{
    public static ReviewSummary Empty(Guid userId) => new(userId, 0, 0, 0, 0, 0, 0, 0);

    public static ReviewSummary From(Guid userId, IEnumerable<Review> reviews)
    {
        var visible = reviews.Where(r => !r.Hidden).ToList();
        if (visible.Count == 0) return Empty(userId);
        return new(
            userId, visible.Count,
            Math.Round(visible.Average(r => r.Rating), 1),
            Stars5: visible.Count(r => r.Rating == 5),
            Stars4: visible.Count(r => r.Rating == 4),
            Stars3: visible.Count(r => r.Rating == 3),
            Stars2: visible.Count(r => r.Rating == 2),
            Stars1: visible.Count(r => r.Rating == 1));
    }
}

// ─── Commands (لِـ Wolverine لاحِقاً، نَستَخدِم endpoints مُباشَرَة الآن) ──
public sealed record SubmitReview(
    Guid TargetUserId, int Rating, string Body, Guid? DealId);
public sealed record RespondToReview(Guid ReviewId, string Response);
public sealed record HideReview(Guid ReviewId, string Reason);
