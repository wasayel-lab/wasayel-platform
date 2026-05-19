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

    /// <summary>وَقت انتِهاء الصَلاحِيَّة لِلمُحادَثات المُؤَقَّتَة (مَثَلاً
    /// بَعد قَبول عَرض مُشوار في إنجيز — تَنتَهي بَعد 24 ساعَة). <c>null</c>
    /// = دائِمَة (السلوك الحالي لِكُلّ المُحادَثات قَبل هذه الإضافَة).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>إذا تَوَلَّدَت المُحادَثَة مِن قَبول Offer في كَيت Offers،
    /// نُسَجِّل الـ OfferId لِلتَّتَبُّع وَلِيَستَطيع الـ UI رَبط
    /// الإعلان+المُحادَثَة+المَوقِع.</summary>
    public Guid? LinkedOfferId { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
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
