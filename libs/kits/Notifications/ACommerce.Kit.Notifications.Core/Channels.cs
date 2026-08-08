namespace ACommerce.Kit.Notifications;

/// <summary>
/// قَناة إشعارات — push (FCM/APNS)، email (SMTP)، in-app (SignalR)، إلخ.
/// تُحقَن كَ مَجموعَة (<c>IEnumerable&lt;INotificationChannel&gt;</c>) لِيَعمَل
/// مَع جَميع القَنَوات الفَعّالَة لِكُلّ إشعار. مَن لا يَدعَم نَوع إشعار
/// مُعَيَّن يَتَجاهَلُه.
/// </summary>
public interface INotificationChannel
{
    string ChannelName { get; }

    /// <summary>أَرسِل إشعاراً. يَتَجاهَل إن لَم يَكُن مُلائِماً
    /// (مَثَلاً Email channel يَتَجاهَل لَو لا يوجد بَريد).</summary>
    Task SendAsync(NotificationPayload payload, CancellationToken ct);
}

/// <summary>حُمولَة الإشعار — تَحوي مُعَرِّف المُستَلِم وأَيّ بَيانات إضافِيَّة
/// قَد تَحتاجها قَناة (token push، بَريد، إلخ). القَنوات تَجلِب ما يَلزَمها
/// (مَثَلاً تَخزين tokens) بِنَفسِها.</summary>
public sealed record NotificationPayload(
    Guid UserId,
    string Type,
    string Title,
    string Body,
    string? RelatedUrl = null,
    Dictionary<string, string>? Data = null);

/// <summary>مَتجَر tokens المُستَخدِم (لِـ push channels). كُلّ مُستَخدِم
/// قَد يَكون لَه عِدَّة أَجهِزَة بِـ tokens مُختَلِفَة.</summary>
public interface IDeviceTokenStore
{
    Task SaveTokenAsync(Guid userId, string token, string platform, CancellationToken ct);
    Task<IReadOnlyList<DeviceToken>> ListTokensAsync(Guid userId, CancellationToken ct);
    Task RemoveTokenAsync(Guid userId, string token, CancellationToken ct);
}

public sealed record DeviceToken(string Token, string Platform, DateTime AddedAt);
