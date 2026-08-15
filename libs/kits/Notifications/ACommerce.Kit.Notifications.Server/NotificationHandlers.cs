using ACommerce.Kit.Realtime;
using ACommerce.Platform.Shared;
using Marten;

namespace ACommerce.Kit.Notifications.Server;

/// <summary>
/// <para>مُعالِج رِسالَة الإشعار — <b>لا نِقاط HTTP</b>.</para>
///
/// <para><b>ما زال مِن هُنا:</b> <c>GET /{slug}/api/notifications?userId=…</c>
/// و<c>POST /{slug}/api/notifications/{id}/read</c> — كِلتاهُما
/// <b>بِلا حارِس</b>: الأُولى تَقرَأ إشعارات أَيّ مُستَخدِم لِمَجهول،
/// والثانِيَة تُعَلِّم أَيّ إشعار مَقروءاً. وبِصِفر مُستَهلِك مَقيس —
/// الواجِهَة تَقرَأ عَدّاداتِها مِن <c>/{slug}/api/me/unread</c> في
/// القالَب، وهي نُقطَة أُخرى قائِمَة بِذاتِها.</para>
/// </summary>
public static class NotificationHandlers
{
    /// <summary>
    /// إنشاء إشعار جَديد. Wolverine يَكتُب المُستَنَد +
    /// يُرسِله كَ <see cref="BroadcastToUser"/> عَبر outbox (cascade).
    /// </summary>
    public static async Task<BroadcastToUser> Send(
        SendNotification cmd, IDocumentSession session, ITenantContext tenantCtx)
    {
        var notif = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = cmd.UserId,
            Type = cmd.Type,
            Title = cmd.Title,
            Body = cmd.Body,
            RelatedUrl = cmd.RelatedUrl,
            At = DateTime.UtcNow
        };
        session.Store(notif);
        await session.SaveChangesAsync();
        return new BroadcastToUser(tenantCtx.Slug, cmd.UserId, "notification", notif);
    }
}
