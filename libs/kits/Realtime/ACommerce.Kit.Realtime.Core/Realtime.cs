namespace ACommerce.Kit.Realtime;

/// <summary>
/// أَمر بَثّ لِمُستَخدِم مُعَيَّن. الـ Channel = نَوع الحَدَث (مثلاً
/// "notification" أو "chat"). الـ Payload = أيّ object يَتسلسَل JSON.
/// </summary>
public sealed record BroadcastToUser(
    string TenantSlug, Guid UserId, string Channel, object Payload);

/// <summary>عَقد التَّفويض البَسيط — مَن يَستَطيع الاتِّصال بالـ hub.</summary>
public interface IRealtimeAuthorizer
{
    (Guid UserId, string TenantSlug)? Authenticate(string? token);
}
