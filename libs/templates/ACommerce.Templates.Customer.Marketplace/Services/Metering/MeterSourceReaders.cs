using System.Globalization;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Marten;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

// ═══ قُرّاءُ مَصادِرِ المُطابَقَة — القِراءَةُ العابِرَةُ لِلمَتاجِرِ هُنا وَحدَها (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9) ════════════════
//
// **لِماذا خامٌّ لا Marten**: المُطابَقَةُ تَسأَلُ «ما الَّذي وُلِدَ في كُلِّ مَتجَر؟»، ومُستَأجِرُ الوَثيقَةِ المُقتَرِنَةِ عَمودٌ (`tenant_id`) لا خاصِّيَّة — `Review`
// و`Message` و`User` لا تَحمِلُه (مَقيس: صِفرُ `TenantId` في أَنواعِها)، فَلا يُسقِطُه استِعلامُ LINQ. فَالقِراءَةُ جُملَةٌ عَلى جَدوَلِ النَوعِ كَما يُسَمّيه تَعيينُ
// Marten نَفسُه (`FindOrResolveDocumentType`، و`EventMappingFor` لِاسمِ الحَدَث) — لا اسمٌ مَنسوخٌ مِن قاعِدَةِ التَسمِيَة، ولا جَدوَلٌ يُنشَأُ بِالسُؤال
// (`to_regclass` قَبلَه، دَرسُ `PresenceReader.StoredAsync`).
//
// **والقِراءَةُ العابِرَةُ لِلمَتاجِرِ هُنا وَحدَها** (التَصميم §9: «Cross-tenant reads happen here only»): `MeterCrossTenantReadersTests` (G14) يَعُدُّ كُلَّ
// قِراءَةٍ تَتَجاوَزُ عَزلَ المُستَأجِرِ في `libs/` و`apps/` و`tools/` و`scripts/` — مُعامِلاتُ Marten العابِرَةُ (`AnyTenant` وأَخَواتُه) والجُمَلُ الخامُّ عَلى
// جَداوِلِه — ويُثَبِّتُها بِمِلَفِّها وعَدَدِها، فَمَوضِعٌ جَديدٌ خارِجَ القائِمَةِ أَحمَر.
//
// **وكُلُّ قارِئٍ يُحاكي المُصَنِّفَ لا يُخَمِّنُه**: مَتجَرُ المَصدَرِ ومُعَرِّفُ سَطرِه المُنتَظَرُ بِقَواعِدِ `MeterRules` نَفسِها (`SessionApp`/`StreamApp`،
// `UsageEntryId.For`/`Pseudonym`) — و`MeterReconciliationTests` يُطابِقُ المُعَرِّفَ المُنتَظَرَ بِما يَكتُبُه `MeterRules.Classify` لِكُلِّ وَحدَةِ حِفظ.

/// <summary>مَصدَرٌ واحِدٌ كَما تَراه المُطابَقَة: مَتجَرُه، ومُعَرِّفُ السَطرِ الَّذي يَنتَظِرُه، وهَل وُلِدَ في نافِذَةِ وَحدَتِه (عِندَ نافِذَتِها أَو بَعدَها).</summary>
public sealed record MeterSource(string App, string EntryId, bool InWindow);

/// <summary>سَطرٌ مِن الدَفتَرِ كَما تَراه المُطابَقَة — مُعَرِّفُه ووَحدَتُه ومَتجَرُه ودَورُه (لِعَدِّ البَذرِ بَعيداً).</summary>
public sealed record MeterLedgerRow(string Id, string Unit, string App, string Role);

/// <summary>نِيَّةٌ صادِرَة: مُعَرِّفُها (= مُعَرِّفُ سَطرِها) ووَحدَتُها ومَتجَرُها ونَتيجَتُها، و<see cref="Stale"/>: مُعَلَّقَةٌ أَقدَمُ مِن <see cref="MeterSourceReaders.UnrecordedAfter"/>.</summary>
public sealed record MeterIntentRow(string Id, string Unit, string App, string Outcome, bool Stale);

/// <summary>عَدُّ يَومٍ ومَتجَرٍ ووَحدَة — مِن الدَفتَرِ (حَتّى المُؤَشِّر) أَو مِن <c>agg_daily</c> — بِيَومِ <see cref="MeterSql.DayZone"/> بِصيغَةِ <c>YYYY-MM-DD</c>.</summary>
public sealed record MeterDayCount(string Day, string App, string Unit, long Entries, long Quantity);

/// <summary>ما قَرَأَه قارِئُ مَصدَرٍ لِوَحدَة: مَصادِرُها، وما لا تاريخَ لَه (حَقلُ الوَقتِ غائِبٌ أَو لا يُقرَأ — لا يُقارَنُ ويُطبَع).</summary>
public sealed record MeterSourceRead(IReadOnlyList<MeterSource> Sources, int Undated);

/// <summary>
/// <para><b>قُرّاءُ مَصادِرِ المُطابَقَة — قائِمَةٌ مُغلَقَةٌ في الكود</b> (التَصميم §9): لِكُلِّ قيمَةِ <c>reconcile</c> في المُعجَمِ قارِئٌ هُنا (<see cref="Reads"/>)، ووَحدَةٌ
/// بِقيمَةٍ لا قارِئَ لَها يَحمَرُّ لَها <c>MeterReconciliationTests</c>.</para>
/// <list type="bullet">
/// <item><c>deal_ids</c> — <c>Deal</c>: المَتجَرُ <c>TenantSlug</c> (كَما يَقرَؤُه المُصَنِّف)، والوَقتُ <c>CreatedAt</c>؛</item>
/// <item><c>tenant_ids</c> — <c>Tenant</c>: المَتجَرُ والمَصدَرُ سلاجُه، والوَقتُ <c>CreatedAt</c>؛</item>
/// <item><c>studio_user_ids</c> — <c>StudioUser</c>: المَتجَرُ <c>_platform</c>، والوَقتُ <c>CreatedAt</c>؛</item>
/// <item><c>user_ids</c> — <c>User</c>: المَتجَرُ مُستَأجِرُ الوَثيقَة، والمَصدَرُ <b>اسمٌ مُستَعار</b> يُعادُ حِسابُه مِن المُعَرِّف (التَصميم §2)، والوَقتُ <c>CreatedAt</c>؛</item>
/// <item><c>review_ids</c> — <c>Review</c>: المَتجَرُ مُستَأجِرُ الوَثيقَة، والوَقتُ <c>At</c>؛</item>
/// <item><c>message_ids</c> — <c>Message</c>: المَتجَرُ مُستَأجِرُ الوَثيقَة، والوَقتُ <c>SentAt</c>؛</item>
/// <item><c>event_ids:{نَوع}</c> — الحَدَثُ في <c>mt_events</c> بِاسمِه عِندَ Marten: المَتجَرُ مُستَأجِرُ المَجرى، والوَقتُ <c>timestamp</c>؛</item>
/// <item><c>intents</c> — <c>meter.outbound_intent</c>؛ و<c>presence_claims</c> — <c>meter.presence_claim</c> مَعَ سُطورِ يَومِها.</item>
/// </list>
/// <para><b>قِراءَةٌ لا كِتابَة</b>: كُلُّ جُملَةٍ هُنا <c>select</c> في مُعامَلَةِ قِراءَةٍ واحِدَة (<c>REPEATABLE READ</c>، <c>read only</c>) يَفتَحُها
/// <see cref="MeterReconciliation"/> — فَالمَصدَرُ وسَطرُه في لَقطَةٍ واحِدَة، ولا يُعَدُّ ما بَينَ قِراءَتَينِ ناقِصاً.</para>
/// </summary>
public static class MeterSourceReaders
{
    /// <summary><b>نِيَّةٌ مُعَلَّقَةٌ أَقدَمُ مِن ساعَةٍ «غَيرُ مُسَجَّلَة»</b> (التَصميم §9، RULES-08): رَقَمٌ تَشغيلِيٌّ لا رَقَمُ مُنتَج — أَطوَلُ نِداءٍ صادِرٍ اليَومَ مُهلَتُه
    /// دَقائِق (مُهلَةُ العُدَد)، فَنِيَّةٌ بَقِيَت مُعَلَّقَةً ساعَةً ماتَت عَمَلِيَّتُها أَو فَشِلَ تَسجيلُها مَرَّتَين.</summary>
    public static readonly TimeSpan UnrecordedAfter = TimeSpan.FromHours(1);

    /// <summary>جَداوِلُ <c>ImportedRecord</c> الَّتي تَكتُبُها المَنَصَّةُ نَفسُها (السِماتُ والمَناطِق) — فَحامِلُها لَيسَ مَتجَراً استَورَدَه المُستَورِد.
    /// والأَسماءُ ثَوابِتُ كُتّابِها لا نُسَخٌ مِنها.</summary>
    public static readonly IReadOnlyList<string> PlatformImportedTables =
        [AttributesSaveService.DefinitionsTable, AttributesSaveService.MappingsTable, AttributesSaveService.ValuesTable, RegionsSaveService.Table];

    /// <summary>الوَحَداتُ الَّتي يَكتُبُ المُستَورِدُ مَصادِرَها بِلا مُستَمِع (‏<c>TargetWriter</c>: المَتجَرُ والأَعضاءُ والرَسائِلُ وثَلاثَةُ أَحداثِ بِدءٍ — مُثَبَّتَةٌ
    /// <c>ImporterExcluded</c> في <c>MeterSeedWritersTests</c>) ويَحذِفُها عِندَ إعادَةِ الاستيراد (<c>TargetWriter.cs:96-97</c>) — فَلا تُقارَنُ في مَتجَرِه.</summary>
    public static readonly IReadOnlyList<string> ImporterReconcilers =
        [MeterReconcilers.TenantIds, MeterReconcilers.UserIds, MeterReconcilers.MessageIds,
         MeterReconcilers.EventIdsPrefix + nameof(ACommerce.Kit.Listings.ListingCreated),
         MeterReconcilers.EventIdsPrefix + nameof(ACommerce.Kit.Subscriptions.SubscriptionCreated),
         MeterReconcilers.EventIdsPrefix + nameof(ACommerce.Kit.Support.TicketCreated)];

    /// <summary>أَلِهذه القيمَةِ قارِئ؟ — القائِمَةُ المُغلَقَة: قُرّاءُ الوَثائِقِ السِتَّة، والحَدَثُ بِاسمِ نَوعٍ يَعُدُّه المُصَنِّف، والنِيّات، ومُطالَباتُ الحُضور.</summary>
    public static bool Reads(string? reconcile) => reconcile switch
    {
        MeterReconcilers.DealIds or MeterReconcilers.TenantIds or MeterReconcilers.StudioUserIds or MeterReconcilers.UserIds
            or MeterReconcilers.ReviewIds or MeterReconcilers.MessageIds or MeterReconcilers.Intents or MeterReconcilers.PresenceClaims => true,
        _ => EventTypeOf(reconcile) is not null,
    };

    /// <summary>نَوعُ الحَدَثِ في <c>event_ids:{نَوع}</c> — مِن أَنواعِ الوِلادَةِ الَّتي يَعُدُّها المُصَنِّف (<see cref="MeterRules.StreamRules"/>) لا مِن اسمٍ حُرّ.</summary>
    public static Type? EventTypeOf(string? reconcile)
        => reconcile is not null && reconcile.StartsWith(MeterReconcilers.EventIdsPrefix, StringComparison.Ordinal)
            ? MeterRules.StreamRules.Keys.FirstOrDefault(t => t.Name == reconcile[MeterReconcilers.EventIdsPrefix.Length..])
            : null;

    /// <summary>أَقارِئُ وَثيقَةٍ أَو حَدَثٍ هذا (حِفظ) — لا نِيّاتٌ ولا حُضور.</summary>
    public static bool IsPersistence(string? reconcile) => Reads(reconcile) && reconcile is not MeterReconcilers.Intents and not MeterReconcilers.PresenceClaims;

    /// <summary>
    /// <para><b>المَصدَرُ كَما يَكتُبُه المُصَنِّف — دالَّةٌ نَقِيَّة</b>: مِن القيمَةِ الخامِ (سلاجُ المَتجَرِ في الوَثيقَة، أَو مُستَأجِرُها، أَو مُستَأجِرُ المَجرى) ومُعَرِّفِ المَصدَرِ
    /// ووَقتِه — المَتجَرُ بِقاعِدَةِ <see cref="MeterRules"/>، والمُعَرِّفُ المُنتَظَرُ <c>{unit}:{app}:{source_id}</c> (والعُضوُ اسماً مُستَعاراً)، وهَل يَقَعُ في النافِذَة.</para>
    /// <para>والوَقتُ الغائِبُ خارِجَ النافِذَة: لا يُقارَنُ لِلنَقص — ويَبقى مَصدَراً قائِماً فَسَطرُه لا يُعَدُّ يَتيماً.</para>
    /// </summary>
    public static MeterSource SourceOf(string unit, string reconcile, string? raw, string sourceId, DateTimeOffset? at, DateTimeOffset? since)
    {
        var app = reconcile switch
        {
            MeterReconcilers.StudioUserIds => MeterSentinels.Platform,
            MeterReconcilers.DealIds or MeterReconcilers.TenantIds => string.IsNullOrWhiteSpace(raw) ? MeterSentinels.Unresolved : raw,
            _ when reconcile.StartsWith(MeterReconcilers.EventIdsPrefix, StringComparison.Ordinal) => MeterRules.StreamApp(raw, null),
            _ => MeterRules.SessionApp(raw),
        };
        var source = reconcile == MeterReconcilers.UserIds ? UsageEntryId.Pseudonym(unit, app, sourceId) : sourceId;
        return new MeterSource(app, UsageEntryId.For(unit, app, source), at is { } t && since is { } s && t >= s);
    }

    /// <summary>وَقتُ وَثيقَةٍ مِن نَصِّ <c>data</c> — بِتَوقيتِ UTC إن لَم يَحمِل إزاحَة (<c>DateTime.UtcNow</c> يُكتَبُ بِـ<c>Z</c>)، و<c>null</c> لِلغائِبِ وما لا يُقرَأ.</summary>
    public static DateTimeOffset? TimeOf(string? text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    // ─── الوَثائِقُ والأَحداث ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>نَوعُ وَثيقَةِ كُلِّ قارِئ، وحَقلُ وَقتِه في <c>data</c> — بِاسمِ الخاصِّيَّةِ نَفسِها (<c>nameof</c>)، فَإعادَةُ تَسمِيَتِها تُحَمِّرُ البِناءَ لا المُطابَقَة.</summary>
    private static (Type Type, string TimeField)? DocumentOf(string reconcile) => reconcile switch
    {
        MeterReconcilers.DealIds => (typeof(Deal), nameof(Deal.CreatedAt)),
        MeterReconcilers.TenantIds => (typeof(Tenant), nameof(Tenant.CreatedAt)),
        MeterReconcilers.StudioUserIds => (typeof(StudioUser), nameof(StudioUser.CreatedAt)),
        MeterReconcilers.UserIds => (typeof(User), nameof(User.CreatedAt)),
        MeterReconcilers.ReviewIds => (typeof(Review), nameof(Review.At)),
        MeterReconcilers.MessageIds => (typeof(Message), nameof(Message.SentAt)),
        _ => null,
    };

    /// <summary>
    /// <para><b>مَصادِرُ وَحدَةِ حِفظٍ واحِدَة</b> — كُلُّها (فَسَطرٌ بِلا مَصدَرٍ يُرى يَتيماً أَيّاً كانَ وَقتُه)، وكُلٌّ بِعَلامَةِ النافِذَة (<paramref name="since"/>: نافِذَةُ الوَحدَة؛
    /// و<c>null</c>: لا نافِذَةَ — لا إقلاعَ عَرَفَها — فَلا شَيءَ يُقارَنُ لِلنَقص). و<paramref name="app"/> يَحصُرُ المَتجَر (صَفحَتُه)، و<c>null</c> القاعِدَةُ كُلُّها (السكربت).</para>
    /// <para>جَدوَلٌ لا وُجودَ لَه (نَوعٌ لَم يُكتَب قَطّ) صِفرُ مَصدَر، ولا يُنشَأ.</para>
    /// </summary>
    internal static async Task<MeterSourceRead> PersistenceAsync(IDocumentStore store, NpgsqlConnection conn, NpgsqlTransaction tx,
        MeterUnit unit, DateTimeOffset? since, string? app, CancellationToken ct)
    {
        var reconcile = unit.Reconcile ?? "";
        string table, appExpr, idExpr, timeExpr, filter;
        if (DocumentOf(reconcile) is { } doc)
        {
            table = store.Options.FindOrResolveDocumentType(doc.Type).TableName.QualifiedName;
            timeExpr = $"d.data->>'{doc.TimeField}'";
            idExpr = "d.id::text";
            (appExpr, filter) = reconcile switch
            {
                MeterReconcilers.DealIds => ($"d.data->>'{nameof(Deal.TenantSlug)}'", $"d.data->>'{nameof(Deal.TenantSlug)}' = @app"),
                MeterReconcilers.TenantIds => ("d.id::text", "d.id = @app"),
                MeterReconcilers.StudioUserIds => ("null::text", "@app = '" + MeterSentinels.Platform + "'"),
                _ => ("d.tenant_id", "d.tenant_id = @app"),
            };
        }
        else if (EventTypeOf(reconcile) is { } eventType)
        {
            table = store.Options.Events.DatabaseSchemaName + ".mt_events";
            timeExpr = "to_char(d.timestamp at time zone 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"')";
            idExpr = "d.id::text";
            appExpr = "d.tenant_id";
            filter = "d.tenant_id = @app";
            // ‏اسمُ الحَدَثِ كَما يَكتُبُه Marten في `mt_events.type`: مِن أَنواعِه المَعروفَة، وإلّا بِتَسمِيَتِه الافتِراضِيَّة (`GetEventTypeName`) — لا اسمٌ مَنسوخ.
            var alias = store.Options.Events.AllKnownEventTypes().FirstOrDefault(e => e.EventType == eventType)?.EventTypeName
                        ?? JasperFx.Events.EventTypeExtensions.GetEventTypeName(eventType);
            filter = "d.type = @type" + (app is null ? "" : " and " + filter);
            return await ReadAsync(conn, tx, table, unit, reconcile, appExpr, idExpr, timeExpr, filter, app, since, ct, alias);
        }
        else return new MeterSourceRead([], 0);

        return await ReadAsync(conn, tx, table, unit, reconcile, appExpr, idExpr, timeExpr, app is null ? null : filter, app, since, ct);
    }

    private static async Task<MeterSourceRead> ReadAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, MeterUnit unit, string reconcile,
        string appExpr, string idExpr, string timeExpr, string? filter, string? app, DateTimeOffset? since, CancellationToken ct, string? eventType = null)
    {
        if (!await ExistsAsync(conn, tx, table, ct)) return new MeterSourceRead([], 0);
        var sources = new List<MeterSource>();
        var undated = 0;
        await using var cmd = new NpgsqlCommand($"select {appExpr}, {idExpr}, {timeExpr} from {table} d" + (filter is null ? "" : " where " + filter), conn, tx);
        if (app is not null) cmd.Parameters.AddWithValue("app", app);
        if (eventType is not null) cmd.Parameters.AddWithValue("type", eventType);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var at = TimeOf(r.IsDBNull(2) ? null : r.GetString(2));
            if (at is null) undated++;
            sources.Add(SourceOf(unit.Code, reconcile, r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1), at, since));
        }
        return new MeterSourceRead(sources, undated);
    }

    /// <summary>
    /// <para><b>المَتاجِرُ الَّتي استَورَدَها المُستَورِد — استِثناءٌ مُسَمّى يُطبَع</b> (التَصميم §9): مُستَأجِرٌ يَحمِلُ سَطرَ <c>ImportedRecord</c> مِن جَدوَلٍ لا تَكتُبُه المَنَصَّة
    /// (<see cref="PlatformImportedTables"/>). فَالمَنَصَّةُ نَفسُها تَكتُبُ <c>ImportedRecord</c> لِلسِماتِ والمَناطِق — وحامِلُها وَحدَه لَيسَ مُستَورَداً.</para>
    /// </summary>
    internal static async Task<IReadOnlyList<string>> ImporterTenantsAsync(IDocumentStore store, NpgsqlConnection conn, NpgsqlTransaction tx, string? app, CancellationToken ct)
    {
        var table = store.Options.FindOrResolveDocumentType(typeof(ImportedRecord)).TableName.QualifiedName;
        if (!await ExistsAsync(conn, tx, table, ct)) return [];
        await using var cmd = new NpgsqlCommand(
            $"select distinct d.tenant_id from {table} d where not (d.data->>'{nameof(ImportedRecord.Table)}' = any(@platform))"
            + (app is null ? "" : " and d.tenant_id = @app") + " order by 1", conn, tx);
        cmd.Parameters.AddWithValue("platform", PlatformImportedTables.ToArray());
        if (app is not null) cmd.Parameters.AddWithValue("app", app);
        var tenants = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) tenants.Add(r.GetString(0));
        return tenants;
    }

    // ─── الدَفتَر ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string Ledger = MeterSql.Schema + "." + MeterSql.UsageEntry;
    private const string Zone = MeterSql.DayZone;

    /// <summary>جَداوِلُ الدَفتَرِ الَّتي تَقرَؤُها المُطابَقَة — وغِيابُ أَحَدِها «لا دَفتَر» (لا تُخَمَّنُ نِصفُ مُطابَقَة).</summary>
    internal const string LedgerTablesMissing =
        "select n from unnest(array['meter.usage_entry', 'meter.unit_epoch', 'meter.outbound_intent', 'meter.presence_claim', 'meter.agg_daily', 'meter.agg_cursor']) as n "
        + "where to_regclass(n) is null order by n";

    /// <summary>نافِذَةُ كُلِّ وَحدَة: أَوَّلُ إقلاعٍ عَرَفَها (<c>meter.unit_epoch</c>، يَكتُبُه <c>MeterBoot</c>).</summary>
    internal static async Task<IReadOnlyDictionary<string, DateTimeOffset>> EpochsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        var epochs = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand("select unit, since from meter.unit_epoch", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) epochs[r.GetString(0)] = r.GetFieldValue<DateTimeOffset>(1);
        return epochs;
    }

    /// <summary>سُطورُ وَحَداتٍ مُعَيَّنَة — كُلُّها، أَو مَتجَرٍ واحِد.</summary>
    internal static async Task<IReadOnlyList<MeterLedgerRow>> EntriesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<string> units, string? app, CancellationToken ct)
    {
        var rows = new List<MeterLedgerRow>();
        await using var cmd = new NpgsqlCommand($"select id, unit, app, role from {Ledger} where unit = any(@units)" + (app is null ? "" : " and app = @app"), conn, tx);
        cmd.Parameters.AddWithValue("units", units.ToArray());
        if (app is not null) cmd.Parameters.AddWithValue("app", app);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(new MeterLedgerRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return rows;
    }

    /// <summary>النِيّاتُ الصادِرَةُ — و«المُعَلَّقَةُ القَديمَة» بِساعَةِ القاعِدَةِ نَفسِها (<see cref="UnrecordedAfter"/>).</summary>
    internal static async Task<IReadOnlyList<MeterIntentRow>> IntentsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string? app, CancellationToken ct)
    {
        var rows = new List<MeterIntentRow>();
        await using var cmd = new NpgsqlCommand(
            "select id, unit, app, outcome, created_at < transaction_timestamp() - @after from meter.outbound_intent" + (app is null ? "" : " where app = @app"), conn, tx);
        cmd.Parameters.AddWithValue("after", UnrecordedAfter);
        if (app is not null) cmd.Parameters.AddWithValue("app", app);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(new MeterIntentRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return rows;
    }

    /// <summary>
    /// <para><b>المَجاميعُ مُقابِلَ الدَفتَرِ حَتّى المُؤَشِّر</b> (التَصميم §9، COR-6d): لِكُلِّ (يَوم، مَتجَر، وَحدَة) عَدُّ السُطورِ الَّتي <c>(generation, writer_xid, id) ≤ المُؤَشِّر</c>
    /// وكَمِّيَّتُها، ومَجموعُ <c>agg_daily</c> — واليَومُ بِتَعبيرِ المُجَمِّعِ نَفسِه. وبِلا مُؤَشِّرٍ فَالدَفتَرُ «حَتّى المُؤَشِّر» فارِغ، والمَجاميعُ يَجِبُ أَن تَكونَ كَذلِك.</para>
    /// </summary>
    internal static async Task<(string? Cursor, IReadOnlyList<MeterDayCount> Ledger, IReadOnlyList<MeterDayCount> Daily)> AggregatesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string? app, CancellationToken ct)
    {
        (int G, string X, string I)? cursor = null;
        await using (var head = new NpgsqlCommand("select after_gen, after_xid::text, after_id from meter.agg_cursor where singleton", conn, tx))
        await using (var r = await head.ExecuteReaderAsync(ct))
            if (await r.ReadAsync(ct)) cursor = (r.GetInt32(0), r.GetString(1), r.GetString(2));

        var ledger = new List<MeterDayCount>();
        if (cursor is { } c)
        {
            await using var cmd = new NpgsqlCommand(
                $"select to_char((occurred_at at time zone '{Zone}')::date, 'YYYY-MM-DD'), app, unit, count(*)::bigint, sum(quantity)::bigint from {Ledger} "
                + "where (generation, writer_xid, id) <= (@g, @x::xid8, @i)" + (app is null ? "" : " and app = @app") + " group by 1, 2, 3", conn, tx);
            cmd.Parameters.AddWithValue("g", c.G);
            cmd.Parameters.AddWithValue("x", c.X);
            cmd.Parameters.AddWithValue("i", c.I);
            if (app is not null) cmd.Parameters.AddWithValue("app", app);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ledger.Add(new MeterDayCount(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
        }

        var daily = new List<MeterDayCount>();
        await using (var cmd = new NpgsqlCommand(
            "select to_char(day, 'YYYY-MM-DD'), app, unit, sum(entries)::bigint, sum(quantity)::bigint from meter.agg_daily"
            + (app is null ? "" : " where app = @app") + " group by 1, 2, 3", conn, tx))
        {
            if (app is not null) cmd.Parameters.AddWithValue("app", app);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) daily.Add(new MeterDayCount(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
        }
        return (cursor is { } k ? $"{k.G}|{k.X}|{k.I}" : null, ledger, daily);
    }

    /// <summary>
    /// <para><b>الحُضور</b> (‏<c>presence_claims</c>): الأَيّامُ ذاتُ المُطالَبَةِ المُكتَمِلَة، واليَومُ بِساعَةِ القاعِدَةِ بِمِنطَقَةِ المَنَصَّة، وسُطورُ وَحَداتِ الحُضورِ
    /// لِكُلِّ (يَوم، مَتجَر، وَحدَة) — فَسَطرٌ في يَومٍ بِلا مُطالَبَةٍ مُكتَمِلَةٍ كُتِبَ خارِجَ مُعامَلَتِها (يَتيم)، ويَومٌ بِلا مُطالَبَةٍ «لَقطَةٌ غائِبَة».</para>
    /// </summary>
    internal static async Task<(IReadOnlySet<string> Completed, string Today, IReadOnlyList<MeterDayCount> Rows)> PresenceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<string> units, string? app, CancellationToken ct)
    {
        var completed = new SortedSet<string>(StringComparer.Ordinal);
        string today;
        await using (var cmd = new NpgsqlCommand(
            "select to_char(day, 'YYYY-MM-DD') from meter.presence_claim where completed_at is not null", conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) completed.Add(r.GetString(0));
        await using (var cmd = new NpgsqlCommand($"select to_char((transaction_timestamp() at time zone '{Zone}')::date, 'YYYY-MM-DD')", conn, tx))
            today = (string)(await cmd.ExecuteScalarAsync(ct))!;

        var rows = new List<MeterDayCount>();
        await using (var cmd = new NpgsqlCommand(
            $"select to_char((occurred_at at time zone '{Zone}')::date, 'YYYY-MM-DD'), app, unit, count(*)::bigint, sum(quantity)::bigint from {Ledger} "
            + "where unit = any(@units)" + (app is null ? "" : " and app = @app") + " group by 1, 2, 3", conn, tx))
        {
            cmd.Parameters.AddWithValue("units", units.ToArray());
            if (app is not null) cmd.Parameters.AddWithValue("app", app);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) rows.Add(new MeterDayCount(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
        }
        return (completed, today, rows);
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("select to_regclass(@t) is not null", conn, tx);
        cmd.Parameters.AddWithValue("t", table);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }
}
