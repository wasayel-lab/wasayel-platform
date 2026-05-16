namespace ACommerce.Kit.Notifications;

/// <summary>
/// إشعار لمُستَخدِم. Marten document. الـ Id فَريد، نَستَخدِم Query
/// لِجَلب كلّ إشعارات userId.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = "info";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? RelatedUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public sealed record SendNotification(
    Guid UserId, string Type, string Title, string Body, string? RelatedUrl = null);

public sealed record MarkNotificationRead(Guid Id);
public sealed record MarkAllNotificationsRead(Guid UserId);
