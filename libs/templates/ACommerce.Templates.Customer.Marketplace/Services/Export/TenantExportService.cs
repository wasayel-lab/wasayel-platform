using System.Reflection;
using System.Text.Json.Nodes;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Export;

/// <summary>عَدَدُ صِنفٍ واحِدٍ كَما تَعرِضُه الشاشَة.</summary>
public sealed record ExportCount(
    string TypeName, string Entry, ExportDisposition Disposition, int Rows, string? ErrorAr);

/// <summary>ناتِجُ نِداءِ التَصدير — <b>حَقيبَةٌ أَو رَمزُ رَفض، لا
/// ثالِثَ</b>. والرَفضُ رَمزٌ مِن مَعجَمٍ مُغلَقٍ لا رِسالَةٌ حُرَّة،
/// فَالشاشَةُ تُتَرجِمُه ولا تَطبَعُه خاماً.</summary>
public sealed record TenantExportResult(
    TenantExportRefusal Refusal, byte[]? Zip, string? FileName);

/// <summary>
/// <para><b>جامِعُ الحَقيبَة.</b> يَقرَأُ الأَصنافَ الَّتي يُسَمّيها
/// <see cref="TenantExportLedger"/> ولا يَمسَحُ الأَنواعَ المُسَجَّلَةَ
/// مَسحاً عامّاً — فَنَوعٌ يُسَجَّلُ غَداً <b>لا يَخرُجُ
/// تِلقائِيّاً</b>.</para>
///
/// <para><b>ونِطاقاتُ جَلَساتِه ثَلاثَةٌ لا رابِعَ لَها</b>: السلاجُ
/// المُحَلَّلُ <b>بَعدَ</b> التَخويل، وثابِتا <c>_studio</c>
/// و<c>_incubator</c> المَكتوبانِ في الكود — <b>وهُما يُقرَآنِ
/// بِمُرَشِّحِ المالِكِ لا جُملَةً</b>. ولا نِطاقَ يَأتي مِن مَسارِ
/// الطَلَب: تِلكَ هي الثَغرَةُ الَّتي تُخرِجُ سِجِلَّ تَدقيقِ
/// المَنَصَّةِ بِسَطرِ عُنوان.</para>
///
/// <para><b>ولا وَثيقَةَ <c>Tenant</c> تُحَمَّلُ هُنا</b>: تُمَرَّرُ
/// مُحَمَّلَةً ومُخَوَّلَةً مِن النُقطَة. فَلا سَبيلَ إلى نِداءِ هذِه
/// الخِدمَةِ بِسلاجٍ لَم يُحَلّ.</para>
///
/// <para><b>وقِراءَةٌ تَتَعَذَّرُ تُقالُ ولا تُبتَلَع</b>: نَوعٌ حَيٌّ
/// بِلا جَدوَلٍ في الإنتاجِ (مَقيسٌ: أَربَعَةُ أَصنافٍ كَذلك) كانَ
/// سَيُعطي جَدوَلاً فارِغاً يُقرَأُ «لا بَياناتِ لَك». فَالخَطَأُ
/// يُكتَبُ في الفَهرَسَينِ وفي <c>README</c>.</para>
/// </summary>
public sealed class TenantExportService
{
    /// <summary><b>سَقفُ المِلَفّاتِ في حَقيبَةٍ واحِدَة.</b> الحَقيبَةُ
    /// تُبنى في الذاكِرَة، والقاعِدَةُ كُلُّها ‏1.74 MB يَومَ الكِتابَة.
    /// وعِندَ بُلوغِ هذا السَقفِ تُقَصُّ المِلَفّاتُ <b>ويُكتَبُ ذلك في
    /// الفَهرَس</b> — ويُنقَلُ التَوليدُ إلى مَخزَنِ الكائِناتِ ورابِطٍ
    /// مُنتَهي الصَلاحِيَّة. الشَرطُ مَكتوبٌ ولا يُبنى الآن.</summary>
    public const long FileBudgetBytes = 64L * 1024 * 1024;

    private readonly IDocumentStore _store;
    private readonly IFileStorage _files;
    private readonly Audit.AuditWriter _audit;

    public TenantExportService(IDocumentStore store, IFileStorage files, Audit.AuditWriter audit)
    {
        _store = store;
        _files = files;
        _audit = audit;
    }

    // ─── النِداءُ الكامِل — ما تَستَدعيه النُقطَة ───────────────────

    /// <summary>
    /// <para><b>تَخويلٌ، فَجَمعٌ، فَأَثَرٌ، فَحَقيبَة</b> — بِهذا
    /// التَرتيبِ لا غَيرِه (القاعِدَة ٦: التَخويلُ يَسبِقُ كُلَّ
    /// شَيء).</para>
    ///
    /// <para><b>والأَثَرُ قَبلَ التَسليم</b>: خُروجُ قاعِدَةِ العُملاءِ
    /// كامِلَةً حَدَثٌ يُقَيَّد. ويَقَعُ القَيدُ في قِسمِ المُستَأجِرِ
    /// نَفسِه، فَيَراهُ المالِكُ في شاشَتِه ومُشرِفُ المَنَصَّةِ في
    /// <c>/admin/audit/{slug}</c> — <b>بِلا وَثيقَةٍ جَديدَةٍ ولا
    /// عَدّاد</b> (القاعِدَة ٨).</para>
    ///
    /// <para><b>ولا مُهلَةَ تَبريدٍ بَينَ تَصديرَين اليَوم</b>، وذلك
    /// يُقالُ ولا يُبتَلَع: الرَقمُ حَدٌّ تَشغيليٌّ يُقَرِّرُه صاحِبُ
    /// المَشروعِ لا الكود (القاعِدَة ١٦). والقَيدُ المَكتوبُ هُنا هُوَ
    /// بِعَينِه ما يُقاسُ عَلَيه يَومَ يُقَرَّر.</para>
    /// </summary>
    public async Task<TenantExportResult> ProduceAsync(
        string? slug, Tenant? tenant, Guid? actorUserId, string? actorName, string? ip,
        CancellationToken ct = default)
    {
        var refusal = TenantExportAuthorization.Decide(slug, tenant, actorUserId);
        if (refusal != TenantExportRefusal.None)
            return new TenantExportResult(refusal, null, null);

        var content = await CollectAsync(tenant!, actorUserId!.Value, ct);

        await _audit.WriteAsync(tenant!.Id, actorUserId, actorName ?? "",
            TenantExportAudit.Action, "tenant", tenant.Id,
            note: $"tables={content.Tables.Count} " +
                  $"rows={content.Tables.Sum(t => t.Rows.Count)} " +
                  $"files={content.Files.Count}",
            ip: ip, ct: ct);

        using var buffer = new MemoryStream();
        TenantExportPackageWriter.Write(buffer, content);

        return new TenantExportResult(TenantExportRefusal.None, buffer.ToArray(),
            $"wasayel-{tenant.Id}-{content.GeneratedAtUtc:yyyyMMdd-HHmm}.zip");
    }

    // ─── الأَعداد — لِلشاشَة ───────────────────────────────────────

    /// <summary>عَدَدُ كُلِّ صِنفٍ كَما سَيَخرُجُ فِعلاً — <b>مِن هذِه
    /// الخِدمَةِ نَفسِها</b> لا مِن استِعلامٍ آخَر، وإلّا انجَرَفَ
    /// المَعروضُ عَن المُسَلَّم.</summary>
    public async Task<IReadOnlyList<ExportCount>> CountAsync(
        Tenant tenant, Guid ownerUserId, CancellationToken ct = default)
    {
        var content = await CollectAsync(tenant, ownerUserId, ct);
        return content.Tables
            .Select(t =>
            {
                var e = TenantExportLedger.Find(t.TypeName)!;
                return new ExportCount(t.TypeName, t.Entry, e.Disposition, t.Rows.Count, t.ReadErrorAr);
            })
            .ToArray();
    }

    // ─── الجَمع ───────────────────────────────────────────────────

    public async Task<ExportContent> CollectAsync(
        Tenant tenant, Guid ownerUserId, CancellationToken ct = default)
    {
        // حارِسٌ ثانٍ بِنَفسِ الدالَّةِ الَّتي تَقرَؤُها النُقطَة:
        // خِدمَةٌ تَثِقُ بِمُنادِيها تَصيرُ ثَغرَةً يَومَ يُنادى مِن
        // مَوضِعٍ ثانٍ.
        var refusal = TenantExportAuthorization.Decide(tenant.Id, tenant, ownerUserId);
        if (refusal != TenantExportRefusal.None)
            throw new TenantExportViolationException(
                $"تَخارُجٌ غَيرُ مَأذونٍ بِه: {TenantExportAuthorization.Code(refusal)}.");

        var slug = tenant.Id;
        var notes = new List<string>();
        var tables = new List<ExportTable>();

        // ‏١) وَثيقَةُ المَتجَرِ نَفسِها — بِصَفِّها هُوَ وَحدَه.
        var self = TenantExportLedger.Exported
            .First(e => e.Disposition == ExportDisposition.ExportSelf);
        tables.Add(new ExportTable(self.TypeName, self.Entry,
            new[] { Redact(self.TypeName, tenant) }.OfType<JsonObject>().ToArray()));

        // ‏٢) بَياناتُ المَتجَر — جَلسَةٌ واحِدَةٌ بِسلاجِه.
        await using (var s = _store.QuerySession(slug))
        {
            foreach (var e in TenantExportLedger.Exported
                         .Where(e => e.Disposition == ExportDisposition.Export))
            {
                var (rows, error) = await ReadAllAsync(s, e, ct);
                tables.Add(new ExportTable(e.TypeName, e.Entry, rows, error));
            }
        }

        // ‏٣) بَياناتُ صاحِبِ المَتجَر — نِطاقانِ ثابِتانِ في الكود،
        //    وكِلاهُما بِمُرَشِّحِ المالِك.
        await using (var s = _store.QuerySession(StudioAuth.Tenant))
        {
            tables.Add(await OneAsync("StudioUser",
                () => s.LoadAsync<StudioUser>(ownerUserId, ct)));

            tables.Add(await ManyAsync("ConsentRecord",
                () => s.Query<ConsentRecord>().Where(c => c.UserId == ownerUserId).ToListAsync(ct)));
        }

        await using (var s = _store.QuerySession(FeasibilityAnalysisService.IncubatorTenant))
        {
            tables.Add(await ManyAsync("IncubatorSession",
                () => s.Query<IncubatorSession>()
                       .Where(i => i.OwnerUserId == ownerUserId).ToListAsync(ct)));
        }

        // ‏٤) الكائِناتُ — مَفاتيحُها مُشتَقَّةٌ مِن الوَثائِقِ نَفسِها.
        var (files, missing, fileNote) = await CollectFilesAsync(slug, tables, ct);
        if (fileNote is not null) notes.Add(fileNote);

        return new ExportContent(
            TenantSlug: slug,
            TenantName: tenant.Name,
            OwnerUserId: ownerUserId,
            GeneratedAtUtc: DateTime.UtcNow,
            Tables: tables,
            Files: files,
            MissingFileKeys: missing,
            NotesAr: notes);
    }

    /// <summary>يَجمَعُ ثُمَّ يَكتُب — والكاتِبُ هُوَ الحارِس.</summary>
    public async Task WriteAsync(
        Stream destination, Tenant tenant, Guid ownerUserId, CancellationToken ct = default)
        => TenantExportPackageWriter.Write(destination, await CollectAsync(tenant, ownerUserId, ct));

    // ─── القِراءَة ────────────────────────────────────────────────

    private static readonly MethodInfo QueryAll = typeof(TenantExportService)
        .GetMethod(nameof(QueryAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<IReadOnlyList<object>> QueryAllAsync<T>(
        IQuerySession session, CancellationToken ct) where T : notnull
        => (await session.Query<T>().ToListAsync(ct)).Cast<object>().ToList();

    /// <summary>قِراءَةُ صِنفٍ كامِلاً — <b>بِنَوعِه لا بِاسمِه</b>،
    /// فَإعادَةُ تَسمِيَةٍ تَكسِرُ البِناءَ ولا تُسقِطُ الصِنفَ
    /// صامِتاً.</summary>
    private static async Task<(IReadOnlyList<JsonObject> Rows, string? Error)> ReadAllAsync(
        IQuerySession session, ExportedType entry, CancellationToken ct)
    {
        try
        {
            var task = (Task<IReadOnlyList<object>>)QueryAll
                .MakeGenericMethod(entry.ClrType)
                .Invoke(null, new object[] { session, ct })!;

            var docs = await task;
            return (docs.Select(d => Redact(entry.TypeName, d)).OfType<JsonObject>().ToArray(), null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return (Array.Empty<JsonObject>(), Describe(ex.InnerException));
        }
        catch (Exception ex)
        {
            return (Array.Empty<JsonObject>(), Describe(ex));
        }
    }

    private static async Task<ExportTable> OneAsync<T>(string typeName, Func<Task<T?>> load)
        where T : class
    {
        var e = TenantExportLedger.Find(typeName)!;
        try
        {
            var doc = await load();
            var rows = doc is null
                ? Array.Empty<JsonObject>()
                : new[] { Redact(typeName, doc) }.OfType<JsonObject>().ToArray();
            return new ExportTable(typeName, e.Entry, rows);
        }
        catch (Exception ex)
        {
            return new ExportTable(typeName, e.Entry, Array.Empty<JsonObject>(), Describe(ex));
        }
    }

    private static async Task<ExportTable> ManyAsync<T>(
        string typeName, Func<Task<IReadOnlyList<T>>> query) where T : notnull
    {
        var e = TenantExportLedger.Find(typeName)!;
        try
        {
            var docs = await query();
            return new ExportTable(typeName, e.Entry,
                docs.Select(d => Redact(typeName, d)).OfType<JsonObject>().ToArray());
        }
        catch (Exception ex)
        {
            return new ExportTable(typeName, e.Entry, Array.Empty<JsonObject>(), Describe(ex));
        }
    }

    private static JsonObject? Redact(string typeName, object document)
        => TenantExportRedaction.Apply(typeName, TenantExportRedaction.ToJson(document));

    /// <summary>سَبَبُ التَعَذُّرِ بِنَوعِه ورِسالَتِه — <b>ولا سِرَّ
    /// فيه</b>: رِسالَةُ Postgres عَن جَدوَلٍ غائِبٍ لا تَحمِلُ
    /// اعتِماداً.</summary>
    private static string Describe(Exception ex)
        => $"تَعَذَّرَت القِراءَة ({ex.GetType().Name}): {ex.Message}";

    // ─── الكائِنات ────────────────────────────────────────────────

    /// <summary>
    /// <para><b>المَفاتيحُ تُشتَقُّ مِن الوَثائِقِ لا مِن سَردِ
    /// المَخزَن</b> — و<c>IFileStorage</c> بِلا عَمَلِيَّةِ سَرد.
    /// والأَثَرُ مَقيسٌ ومُعلَن: كائِنٌ يَتيمٌ (صورَةٌ رُفِعَت ثُمَّ
    /// حُذِفَ إعلانُها) لا يَخرُج. و‏ADR-017 قاسَ أَنّ مِلَفّاً بِلا
    /// رابِطٍ في القاعِدَةِ غَيرُ مَوجودٍ عَمَلِيّاً.</para>
    /// </summary>
    private async Task<(IReadOnlyList<ExportFile> Files, IReadOnlyList<string> Missing, string? Note)>
        CollectFilesAsync(string slug, IReadOnlyList<ExportTable> tables, CancellationToken ct)
    {
        var keys = new List<string>();

        foreach (var t in tables)
        {
            if (t.TypeName == nameof(Listing))
                foreach (var row in t.Rows)
                    if (row["mediaUrls"] is JsonArray media)
                        foreach (var u in media)
                            Add(u?.GetValue<string>());

            if (t.TypeName == nameof(User))
                foreach (var row in t.Rows)
                    Add(row["avatarUrl"]?.GetValue<string>());
        }

        var files = new List<ExportFile>();
        var missing = new List<string>();
        string? note = null;
        long budget = FileBudgetBytes;

        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            if (budget <= 0)
            {
                note = $"بَلَغَت المِلَفّاتُ سَقفَ {FileBudgetBytes / (1024 * 1024)} مِيغابايت " +
                       "في هذِه الحَقيبَة، فَقُصَّ ما بَعدَه. اطلُب حَقيبَةً بِمَخزَنِ كائِناتٍ.";
                break;
            }

            try
            {
                await using var stream = await _files.ReadAsync(key, ct);
                if (stream is null) { missing.Add(key); continue; }

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                var bytes = buffer.ToArray();
                budget -= bytes.Length;
                files.Add(new ExportFile(key, bytes));
            }
            catch (Exception ex)
            {
                missing.Add(key);
                note ??= $"تَعَذَّرَ بُلوغُ مَخزَنِ المِلَفّات ({ex.GetType().Name}) — " +
                         "الوَثائِقُ كامِلَةٌ والصُوَرُ ناقِصَة، وقائِمَتُها في `files/MISSING.txt`.";
            }
        }

        return (files, missing, note);

        void Add(string? url)
        {
            var key = KeyFromUrl(url, slug);
            if (key is not null) keys.Add(key);
        }
    }

    /// <summary>مِفتاحُ المَخزَنِ مِن رابِطٍ مَحفوظ — <b>بِالبادِئَةِ لا
    /// بِنَزعِ عُنوانٍ مُهَيَّأ</b>: مُزَوِّدانِ يَبنِيانِ الرابِطَ
    /// بِشَكلَينِ (<c>{PublicBaseUrl}/{key}</c> و<c>/uploads/{key}</c>)،
    /// والبادِئَةُ واحِدَةٌ فيهِما.</summary>
    internal static string? KeyFromUrl(string? url, string slug)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var clean = url.Split('?', '#')[0];
        var marker = $"tenants/{slug}/";
        var i = clean.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? null : clean[i..];
    }
}
