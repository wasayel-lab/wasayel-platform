namespace ACommerce.Kit.Reports;

/// <summary>تَبليغ عَن مُحتَوى/مُستَخدِم. Marten document. مَفصول
/// عَن أَيّ تَطبيق — يَحتَوي عَلى <c>TargetType</c>+<c>TargetId</c>
/// لِيَعمَل مَع listings, users, chats, deals, إلخ.</summary>
public sealed class Report
{
    public Guid Id { get; set; }
    /// <summary>مَن الَّذي أَنشَأ التَّبليغ.</summary>
    public Guid ReporterId { get; set; }
    public string ReporterName { get; set; } = "";
    /// <summary>"listing" | "user" | "deal" | "chat".</summary>
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    /// <summary>صَيغَة سَبَب مُغَرَّبَة (slug): "spam", "scam", "adult", "wrong_info", "other".</summary>
    public string ReasonCode { get; set; } = "other";
    public string? Details { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    /// <summary>مُعَرِّف المُعَدِّل الَّذي راجَع التَّبليغ.</summary>
    public Guid? ModeratorId { get; set; }
    public string? ModeratorAction { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public enum ReportStatus { Pending, UnderReview, Resolved, Rejected }

public sealed record SubmitReport(
    Guid ReporterId, string ReporterName, string TargetType, Guid TargetId,
    string ReasonCode, string? Details);
public sealed record ResolveReport(Guid ReportId, Guid ModeratorId, string Action);
public sealed record RejectReport(Guid ReportId, Guid ModeratorId, string Reason);
