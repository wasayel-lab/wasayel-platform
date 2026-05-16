namespace ACommerce.Kit.Chat;

/// <summary>المُحادَثَة — وَثيقَة Marten. حالَة بَسيطَة.</summary>
public sealed class Conversation
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public Guid PartnerId { get; set; }
    public string PartnerName { get; set; } = "";
    public string? Subject { get; set; }
    public Guid? ListingId { get; set; }
    public string? LastMessage { get; set; }
    public DateTime LastAt { get; set; }
    public int OwnerUnread { get; set; }
    public int PartnerUnread { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>الرسالَة — وَثيقَة Marten. ثابِتَة بَعد الإرسال.</summary>
public sealed class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

// ─── Commands ─────────────────────────────────────────────────────────
public sealed record StartConversation(
    Guid OwnerId, string OwnerName, Guid PartnerId, string PartnerName,
    string? Subject, Guid? ListingId);
public sealed record SendMessage(Guid ConversationId, Guid SenderId, string Body);
public sealed record MarkConversationRead(Guid ConversationId, Guid UserId);
