namespace ACommerce.Kit.Support;

/// <summary>تذكِرَة دَعم. event-sourced لتاريخ الردود.</summary>
public sealed class Ticket
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Status { get; set; } = "open";    // open | answered | closed
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Reply> Replies { get; set; } = new();

    public void Apply(TicketCreated e)
    { Id = e.Id; AuthorId = e.AuthorId; AuthorName = e.AuthorName; Subject = e.Subject; Body = e.Body;
      CreatedAt = e.At; UpdatedAt = e.At; Status = "open"; }
    public void Apply(TicketReplied e)
    { Replies.Add(new Reply { Id = e.ReplyId, AuthorName = e.AuthorName, FromStaff = e.FromStaff, Body = e.Body, At = e.At });
      UpdatedAt = e.At; if (e.FromStaff) Status = "answered"; }
    public void Apply(TicketClosed e) { Status = "closed"; UpdatedAt = e.At; }
}

public sealed class Reply
{
    public Guid Id { get; set; }
    public string AuthorName { get; set; } = "";
    public bool FromStaff { get; set; }
    public string Body { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed record TicketCreated(Guid Id, Guid AuthorId, string AuthorName, string Subject, string Body, DateTime At);
public sealed record TicketReplied(Guid TicketId, Guid ReplyId, string AuthorName, bool FromStaff, string Body, DateTime At);
public sealed record TicketClosed(Guid TicketId, DateTime At);

public sealed record OpenTicket(Guid AuthorId, string AuthorName, string Subject, string Body);
public sealed record ReplyTicket(Guid TicketId, Guid AuthorId, string AuthorName, bool FromStaff, string Body);
