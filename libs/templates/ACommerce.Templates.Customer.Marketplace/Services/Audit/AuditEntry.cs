using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Audit;

/// <summary>
/// قَيد تَدقيق — مَن فَعَل ماذا، عَلى أَيّ كائِن، مَتى. مَحفوظ في
/// tenant الحَدَث (tenant-scoped) أَو "_platform" لِلأَحداث الإداريَّة.
/// إلزاميّ في الإنتاج: لا قَرار إداريّ بِلا أَثَر.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;

    public string Scope { get; set; } = "";        // tenant slug | "_platform" | "_studio"
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = "";

    /// <summary>فِعل قَصير: tenant.create, listing.hide, deal.advance, ticket.reply…</summary>
    public string Action { get; set; } = "";

    public string EntityType { get; set; } = "";   // tenant | listing | deal | ticket | review …
    public string EntityId { get; set; } = "";

    public string? Note { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>قَبل/بَعد كَ JSON اختياريّ — لِلأَفعال المُهِمَّة.</summary>
    public string? Before { get; set; }
    public string? After { get; set; }
}

public sealed class AuditWriter
{
    private readonly IDocumentStore _store;
    public AuditWriter(IDocumentStore store) => _store = store;

    public const string PlatformScope = "_platform";

    public async Task WriteAsync(
        string scope, Guid? actorId, string actorName,
        string action, string entityType, string entityId,
        string? note = null, string? ip = null, string? userAgent = null,
        string? before = null, string? after = null,
        CancellationToken ct = default)
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(), Scope = scope,
            ActorId = actorId, ActorName = actorName,
            Action = action, EntityType = entityType, EntityId = entityId,
            Note = note, Ip = ip, UserAgent = userAgent,
            Before = before, After = after,
            At = DateTime.UtcNow
        };
        await using var s = _store.LightweightSession(scope);
        s.Store(entry);
        await s.SaveChangesAsync(ct);
    }

    /// <summary>
    /// <para><b>عَدُّ سُطورِ الأَثَرِ في مَساحَةٍ بِشَرطٍ — أَو <c>null</c> حينَ تَتَعَذَّرُ القِراءَة</b> (‏2026-10-29، قَرارُ المالِك د11 (٢): «تُعدّ من سجلّ الأثر
    /// المحفوظ في القاعدة لا من ذاكرة الخادم»). الشَرطُ يَبنيه المُنادي دالَّةً نَقِيَّةً تُقاسُ بِلا قاعِدَة؛ والفَشَلُ سَطرٌ في مَجرى الخَطَإ ولا يُقرَأُ صِفراً —
    /// يُقَرِّرُ المُنادي ما يَعنيه (مُستَهلِكُه <c>VendorShippingService.RegisterAsync</c>).</para>
    /// </summary>
    public async Task<int?> CountAsync(
        string scope, System.Linq.Expressions.Expression<Func<AuditEntry, bool>> where, CancellationToken ct = default)
    {
        try
        {
            await using var s = _store.QuerySession(scope);
            return await s.Query<AuditEntry>().CountAsync(where, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[audit] عَدُّ سُطورِ الأَثَرِ في «{scope}» تَعَذَّر: {ex.GetType().Name}");
            return null;
        }
    }

    public async Task<List<AuditEntry>> ListAsync(
        string scope, int take = 200, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(scope);
        var list = await s.Query<AuditEntry>()
            .OrderByDescending(a => a.At).Take(take).ToListAsync(ct);
        return list.ToList();
    }
}
