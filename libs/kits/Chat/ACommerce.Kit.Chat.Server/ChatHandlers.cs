using ACommerce.Kit.Notifications;
using ACommerce.Kit.Realtime;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Chat.Server;

public static class ChatHandlers
{
    [WolverinePost("/{slug}/api/chat/start")]
    public static async Task<Conversation> Start(
        StartConversation cmd, IDocumentSession session)
    {
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            OwnerId = cmd.OwnerId, OwnerName = cmd.OwnerName,
            PartnerId = cmd.PartnerId, PartnerName = cmd.PartnerName,
            Subject = cmd.Subject, ListingId = cmd.ListingId,
            LastAt = DateTime.UtcNow
        };
        session.Store(conv);
        await session.SaveChangesAsync();
        return conv;
    }

    /// <summary>
    /// إرسال رسالَة. cascading messages: نُرجِع الرسالَة + commands
    /// أُخرى تُنفَّذ عَبر outbox: بَثّ realtime للمُستَلِم + إشعار له.
    /// </summary>
    [WolverinePost("/{slug}/api/chat/send")]
    public static async Task<(Message, BroadcastToUser, SendNotification?)> Send(
        SendMessage cmd, IDocumentSession session, ITenantContext tenantCtx)
    {
        var conv = await session.LoadAsync<Conversation>(cmd.ConversationId)
            ?? throw new InvalidOperationException("conversation_not_found");

        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = cmd.ConversationId,
            SenderId = cmd.SenderId,
            Body = cmd.Body,
            SentAt = DateTime.UtcNow
        };
        session.Store(msg);

        // تَحديث المُحادَثَة لِيَظهَر آخِر رسالَة في الـ inbox
        conv.LastMessage = cmd.Body.Length > 100 ? cmd.Body[..100] : cmd.Body;
        conv.LastAt = msg.SentAt;
        if (cmd.SenderId == conv.OwnerId) conv.PartnerUnread++;
        else if (cmd.SenderId == conv.PartnerId) conv.OwnerUnread++;
        session.Store(conv);

        await session.SaveChangesAsync();

        // الطَرَف الآخَر = المُستَلِم
        var recipient = cmd.SenderId == conv.OwnerId ? conv.PartnerId : conv.OwnerId;
        var senderName = cmd.SenderId == conv.OwnerId ? conv.OwnerName : conv.PartnerName;

        var broadcast = new BroadcastToUser(tenantCtx.Slug, recipient, "chat", new
        {
            type = "message",
            conversationId = conv.Id,
            messageId = msg.Id,
            senderName,
            body = msg.Body,
            at = msg.SentAt
        });

        var notify = new SendNotification(
            UserId: recipient,
            Type: "chat",
            Title: $"رسالَة من {senderName}",
            Body: cmd.Body.Length > 60 ? cmd.Body[..60] + "…" : cmd.Body,
            RelatedUrl: $"/chats/{conv.Id}");

        return (msg, broadcast, notify);
    }

    [WolverinePost("/{slug}/api/chat/{conversationId}/read")]
    public static async Task<bool> MarkRead(
        Guid conversationId, MarkConversationRead cmd,
        IDocumentSession session)
    {
        var conv = await session.LoadAsync<Conversation>(conversationId);
        if (conv is null) return false;
        var changed = false;
        if (conv.OwnerId == cmd.UserId && conv.OwnerUnread > 0)   { conv.OwnerUnread = 0; changed = true; }
        if (conv.PartnerId == cmd.UserId && conv.PartnerUnread > 0) { conv.PartnerUnread = 0; changed = true; }
        if (changed) { session.Store(conv); await session.SaveChangesAsync(); }
        return changed;
    }

    [WolverineGet("/{slug}/api/chat/my")]
    public static async Task<IReadOnlyList<Conversation>> MyConversations(
        IDocumentStore store, ITenantContext tenantCtx, Guid userId)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Conversation>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Conversation>()
            .Where(c => c.OwnerId == userId || c.PartnerId == userId)
            .OrderByDescending(c => c.LastAt)
            .Take(50)
            .ToListAsync();
    }

    [WolverineGet("/{slug}/api/chat/{conversationId}/messages")]
    public static async Task<IReadOnlyList<Message>> Messages(
        Guid conversationId, IDocumentStore store, ITenantContext tenantCtx)
    {
        if (!tenantCtx.IsResolved) return Array.Empty<Message>();
        await using var s = store.QuerySession(tenantCtx.Slug);
        return await s.Query<Message>()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }
}
