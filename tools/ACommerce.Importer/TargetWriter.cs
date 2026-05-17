using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using Marten;
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
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;
        });
        await Task.CompletedTask;
        _log.LogInformation("✓ Marten target ready (schema=platform).");
    }

    /// <summary>يَحذِف كُلّ مُستَنَدات tenant مُحَدَّد. لا يَحذِف الـ Tenant document
    /// نَفسه (سَيُعاد إنشاؤه بِواسِطَة الـ importer).</summary>
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
        await s.SaveChangesAsync();
        _log.LogInformation("  ↻ reset tenant '{Slug}' (deleted documents).", slug);
    }

    /// <summary>UPSERT لِـ Tenant document (global، يَحفَظ ميتاداتا المَتجَر).</summary>
    public async Task UpsertTenantAsync(Tenant t)
    {
        if (_store is null) throw new InvalidOperationException();
        await using var s = _store.LightweightSession();
        s.Store(t);
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ tenant '{Slug}' ({Name}) — {Cats} categories.", t.Id, t.Name, t.Categories.Count);
    }

    /// <summary>UPSERT دَفعَة مُستَنَدات داخِل tenant. النَوع T يُتَبَنّى
    /// بِواسِطَة Marten، الـ Id الحاليّ يُحَدِّد upsert/insert.</summary>
    public async Task UpsertAsync<T>(string tenantSlug, IReadOnlyList<T> docs) where T : class
    {
        if (_store is null) throw new InvalidOperationException();
        if (docs.Count == 0) return;
        await using var s = _store.LightweightSession(tenantSlug);
        foreach (var d in docs) s.Store(d);
        await s.SaveChangesAsync();
        _log.LogInformation("  ✓ {Count} {Type}", docs.Count, typeof(T).Name);
    }
}
