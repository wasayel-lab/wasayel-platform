using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.Shared;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Support;
using ACommerce.Kit.Tenants;
using Dapper;
using Marten;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Importer;

/// <summary>
/// كاتِب الهَدَف: يُهَيِّئ Marten DocumentStore عَلى نَفس schema الَّتي
/// يَستَخدِمها V1.App (platform schema، conjoined tenancy) ثُمّ يَكتُب
/// مُستَنَدات + events بِنَفس الأَشكال الَّتي تُنتِجها التَطبيق
/// الأَصلي. مَفتاح الـ idempotency: نَستَخدِم نَفس Guid مِن الـ source DB
/// كَ Marten Id لِيَمنَع التَكرار عَبر تَشغيلات مُتَعَدِّدَة.
/// </summary>
public sealed class TargetWriter
{
    private readonly ILogger<TargetWriter> _log;
    private readonly ImporterOptions _opts;
    private IDocumentStore? _store;

    public TargetWriter(IOptions<ImporterOptions> opts, ILogger<TargetWriter> log)
    {
        _log = log;
        _opts = opts.Value;
    }

    public IDocumentStore Store => _store ?? throw new InvalidOperationException("Store not initialized — call InitAsync first.");

    public async Task InitAsync()
    {
        _store = DocumentStore.For(o =>
        {
            o.Connection(_opts.Target!.Postgres!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Events.TenancyStyle = global::Marten.Storage.TenancyStyle.Conjoined;

            o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
            o.Projections.Snapshot<Listing>(global::Marten.Events.Projections.SnapshotLifecycle.Inline);
            o.Schema.For<User>().Identity(x => x.Id);
            o.Schema.For<Notification>().Identity(x => x.Id);
            o.Schema.For<Conversation>().Identity(x => x.Id);
            o.Schema.For<Message>().Identity(x => x.Id);
            o.Schema.For<Favorite>().Identity(x => x.Id);
            o.Schema.For<Plan>().Identity(x => x.Id);
            o.Projections.Snapshot<Subscription>(global::Marten.Events.Projections.SnapshotLifecycle.Inline);
            o.Projections.Snapshot<Ticket>(global::Marten.Events.Projections.SnapshotLifecycle.Inline);

            // مُستَنَد عامّ لِكُلّ جَدول لا يَملِك تَعويض typed في
            // platform-v1. يُحفَظ كَ JSON كامِل مَع المَفتاح "{table}/{srcId}".
            o.Schema.For<ImportedRecord>().Identity(x => x.Id);

            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;
        });
        await Task.CompletedTask;
        _log.LogInformation("✓ Marten target ready (schema=platform).");
    }

    /// <summary>يَحذِف كُلّ مُستَنَدات tenant مُحَدَّد.</summary>
    public async Task ResetTenantAsync(string slug)
    {
        if (_store is null) throw new InvalidOperationException();
        await using var s = _store.LightweightSession(slug);
        s.DeleteWhere<Listing>(x => true);
        s.DeleteWhere<Favorite>(x => true);
        s.DeleteWhere<Conversation>(x => true);
        s.DeleteWhere<Message>(x => true);
        s.DeleteWhere<Notification>(x => true);
        s.DeleteWhere<User>(x => true);
        s.DeleteWhere<Plan>(x => true);
        s.DeleteWhere<Subscription>(x => true);
        s.DeleteWhere<Ticket>(x => true);
        s.DeleteWhere<ImportedRecord>(x => true);
        await s.SaveChangesAsync();
        _log.LogInformation("  ↻ reset tenant '{Slug}' (deleted documents).", slug);
    }

    public async Task UpsertTenantAsync(Tenant t)
    {
        if (_store is null) throw new InvalidOperationException();
        await using var s = _store.LightweightSession();
        s.Store(t);
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ tenant '{Slug}' ({Name}) — {Cats} categories.", t.Id, t.Name, t.Categories.Count);
    }

    public async Task UpsertAsync<T>(string tenantSlug, IReadOnlyList<T> docs) where T : class
    {
        if (_store is null) throw new InvalidOperationException();
        if (docs.Count == 0) return;
        await using var s = _store.LightweightSession(tenantSlug);
        foreach (var d in docs) s.Store(d);
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ {Count} {Type}", docs.Count, typeof(T).Name);
    }

    /// <summary>إنشاء/تَحديث Listings كَ event streams (Snapshot.Inline).</summary>
    public async Task UpsertListingsAsync(string tenantSlug, IReadOnlyList<Listing> listings)
    {
        if (_store is null) throw new InvalidOperationException();
        if (listings.Count == 0) return;

        await using var s = _store.LightweightSession(tenantSlug);
        var ids = listings.Select(l => l.Id).ToList();
        var existingIds = (await s.Query<Listing>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync())
            .ToHashSet();

        var created = 0; var edited = 0;
        foreach (var l in listings)
        {
            if (existingIds.Contains(l.Id))
            {
                s.Events.Append(l.Id, new ListingEdited(
                    l.Id, l.Title, l.Description, l.Price, l.CategorySlug,
                    l.City, l.District, l.Attributes, l.UpdatedAt));
                edited++;
            }
            else
            {
                s.Events.StartStream<Listing>(l.Id, new ListingCreated(
                    l.Id, l.TenantSlug, l.Title, l.Description, l.Price,
                    l.CategorySlug, l.City, l.District,
                    l.Attributes ?? new(), l.CreatedAt));
                created++;
            }
        }
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ {Created} new + {Edited} updated Listing streams", created, edited);
    }

    /// <summary>إنشاء/تَحديث Subscription/Ticket كَ event streams. الاثنان
    /// Snapshot.Inline في V1.App. تُولِّد الـ event الإنشائيّ
    /// إن لم يَكن stream مَوجوداً.</summary>
    public async Task UpsertSubscriptionsAsync(string tenantSlug, IReadOnlyList<SubscriptionImport> subs)
    {
        if (_store is null) throw new InvalidOperationException();
        if (subs.Count == 0) return;

        await using var s = _store.LightweightSession(tenantSlug);
        var ids = subs.Select(x => x.Id).ToList();
        var existing = (await s.Query<Subscription>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync())
            .ToHashSet();

        foreach (var x in subs)
        {
            if (existing.Contains(x.Id)) continue;
            s.Events.StartStream<Subscription>(x.Id, new SubscriptionCreated(
                x.Id, x.UserId, x.PlanId, x.Quota, x.DaysPeriod, x.StartsAt));
        }
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ {Count} Subscription streams", subs.Count - existing.Count);
    }

    public async Task UpsertTicketsAsync(string tenantSlug, IReadOnlyList<TicketImport> tickets)
    {
        if (_store is null) throw new InvalidOperationException();
        if (tickets.Count == 0) return;

        await using var s = _store.LightweightSession(tenantSlug);
        var ids = tickets.Select(x => x.Id).ToList();
        var existing = (await s.Query<Ticket>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => x.Id).ToListAsync())
            .ToHashSet();

        foreach (var t in tickets)
        {
            if (existing.Contains(t.Id)) continue;
            s.Events.StartStream<Ticket>(t.Id, new TicketCreated(
                t.Id, t.AuthorId, t.AuthorName, t.Subject, t.Body, t.CreatedAt));
        }
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ {Count} Ticket streams", tickets.Count - existing.Count);
    }

    /// <summary>دَفعَة نَسخ خام — كُلّ صَفّ يَصير ImportedRecord بِـ
    /// Id = "{table}/{sourceId}" + Data = JSON كامِل لِلصَفّ. تُستَخدَم
    /// لِلجَداوِل الَّتي لا تَملِك نَوع typed مُقابِل في platform-v1.</summary>
    public async Task DumpGenericAsync(SqlConnection src, string tenantSlug,
        string table, string idColumn = "Id", string? whereClause = null)
    {
        if (_store is null) throw new InvalidOperationException();
        var sql = $"SELECT * FROM [{table}]";
        if (!string.IsNullOrEmpty(whereClause)) sql += $" WHERE {whereClause}";

        IEnumerable<dynamic>? rows;
        try
        {
            rows = await src.QueryAsync(sql);
        }
        catch (SqlException ex)
        {
            _log.LogWarning("  ⚠ skip [{Table}]: {Msg}", table, ex.Message.Split('\n')[0]);
            return;
        }

        var docs = new List<ImportedRecord>();
        var now = DateTime.UtcNow;
        foreach (var r in rows)
        {
            var dict = (IDictionary<string, object?>)r;
            var rawId = dict.TryGetValue(idColumn, out var v) ? v?.ToString() : null;
            if (string.IsNullOrEmpty(rawId)) rawId = Guid.NewGuid().ToString();
            docs.Add(new ImportedRecord
            {
                Id         = $"{table}/{rawId}",
                Table      = table,
                SourceId   = rawId,
                ImportedAt = now,
                Data       = dict.ToDictionary(k => k.Key, k => k.Value)
            });
        }

        if (docs.Count == 0)
        {
            _log.LogInformation("  · [{Table}] empty.", table);
            return;
        }

        await using var s = _store.LightweightSession(tenantSlug);
        foreach (var d in docs) s.Store(d);
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ [{Table}] {Count} rows → ImportedRecord", table, docs.Count);
    }
}

// ───────────────────────────────────────────────────────────────────────
// DTOs لِلتَّمرير مِن importers إلى TargetWriter (تَتَجَنَّب اشتِراك الـ
// importer في تَفاصيل event-sourcing).
// ───────────────────────────────────────────────────────────────────────

public sealed record SubscriptionImport(Guid Id, Guid UserId, string PlanId, int Quota, int DaysPeriod, DateTime StartsAt);
public sealed record TicketImport(Guid Id, Guid AuthorId, string AuthorName, string Subject, string Body, DateTime CreatedAt);

// ImportedRecord مَنقول إلى ACommerce.Platform.Shared لِيُشارَك مَع
// مَشاريع التَطبيق (DynamicAttributesService، صَفَحات ديناميكِيَّة).
