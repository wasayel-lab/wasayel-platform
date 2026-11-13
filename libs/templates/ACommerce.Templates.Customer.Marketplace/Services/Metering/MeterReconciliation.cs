using System.Data;
using System.Globalization;
using ACommerce.Platform.Shared.Metering;
using Marten;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

// ═══ المُطابَقَة — المَصادِرُ مُقابِلَ السُطور، والنِيّاتُ مُقابِلَ السُطور، والسُطورُ مُقابِلَ المَجاميع (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9) ═══
//
// **ما تُجيبُ عَنه**: أَكُلُّ ما وُلِدَ عُدّ؟ وأَكُلُّ ما عُدَّ وُلِد؟ وأَيُطابِقُ المَجموعُ ما جَمَعَه؟ — في لَقطَةٍ واحِدَة (`REPEATABLE READ`، قِراءَةٌ فَقَط) يَقرَؤُها
// السكربتُ (`scripts/meter-reconcile.cs`، القاعِدَةُ كُلُّها) وصَفحَةُ المَتجَر (`/admin/meter/stores/{slug}`، مَتجَرٌ واحِد) والبُرهانُ الحَيُّ (G13) بِالدالَّةِ نَفسِها.
//
// **وما هُوَ عَيبٌ — وما يُطبَعُ ولا يُفشِل** (التَصميم §9):
//   عَيب: مَصدَرٌ في نافِذَةِ وَحدَتِه بِلا سَطر (ناقِص) · سَطرٌ بِلا مَصدَر (يَتيم — المَصادِرُ لا تُحذَفُ إلّا بِالمُستَورِد، مَقيس) · مَتجَرٌ لَه مَصادِرُ بِلا سَطرٍ واحِد ·
//         نِيَّةٌ مَقبولَةٌ بِلا سَطر، وسَطرٌ صادِرٌ بِلا نِيَّةٍ مَقبولَة · (يَوم، مَتجَر، وَحدَة) لا يُطابِقُ مَجموعُه دَفتَرَه حَتّى المُؤَشِّر · سَطرُ حُضورٍ في يَومٍ بِلا مُطالَبَة ·
//         **وصِفرُ مَصدَرٍ قورِن** (أَداةٌ عَمياء، القاعِدَة ١٠).
//   يُطبَعُ: النِيّاتُ غَيرُ المُسَجَّلَةِ والمَجهولَةُ والمَرفوضَة، والأَيّامُ بِلا لَقطَة، والبَذر، والمَتاجِرُ المُستَورَدَةُ المُستَثناة، وما لا تاريخَ لَه.
//
// **ولا سِعرَ في شَيءٍ هُنا** (القاعِدَة ١٦): عَدٌّ وَحدَه.

/// <summary>ما انتَهَت إلَيه المُطابَقَة: أَرقام، أَو «لا دَفتَر» (جَدوَلٌ غائِب)، أَو عُطلٌ سُجِّلَ سَطراً.</summary>
public enum MeterReconcileState { Data, NoLedger, Error }

/// <summary>
/// وَحدَةُ حِفظٍ واحِدَة: <see cref="Compared"/> مَصادِرُ النافِذَة، و<see cref="Missing"/> ما لا سَطرَ لَه مِنها، و<see cref="Orphans"/> سُطورٌ لا مَصدَرَ لَها،
/// و<see cref="ZeroEntryStores"/> مَتاجِرُ لَها مَصادِرُ ولا سَطرَ واحِد، و<see cref="Seed"/> سُطورُ البَذر، و<see cref="Excluded"/> مَصادِرُ المَتاجِرِ المُستَورَدَة،
/// و<see cref="Undated"/> ما لا تاريخَ لَه — ومَعَها عَيِّناتٌ مِن المُعَرِّفات.
/// </summary>
public sealed record MeterUnitCheck(
    string Unit, string Reconcile, bool HasEpoch, int Compared, int Missing, int Orphans, IReadOnlyList<string> ZeroEntryStores,
    int Seed, int Excluded, int Undated, IReadOnlyList<string> MissingIds, IReadOnlyList<string> OrphanIds)
{
    public int Defects => Missing + Orphans + ZeroEntryStores.Count;
}

/// <summary>وَحدَةٌ صادِرَة: النِيّاتُ بِنَتائِجِها، و<see cref="Missing"/> مَقبولَةٌ بِلا سَطر، و<see cref="Orphans"/> سَطرٌ بِلا نِيَّةٍ مَقبولَة، و<see cref="Unrecorded"/>
/// مُعَلَّقَةٌ قَديمَة («غَيرُ مُسَجَّلَة»)، و<see cref="Pending"/> مُعَلَّقَةٌ حَديثَة.</summary>
public sealed record MeterIntentCheck(
    string Unit, int Accepted, int Missing, int Orphans, int Unrecorded, int Pending, int Unknown, int Refused, IReadOnlyList<string> MissingIds)
{
    public int Defects => Missing + Orphans;
    public int Intents => Accepted + Unrecorded + Pending + Unknown + Refused;
}

/// <summary>(يَوم، مَتجَر، وَحدَة) لا يُطابِقُ فيه <c>agg_daily</c> دَفتَرَه حَتّى المُؤَشِّر.</summary>
public sealed record MeterAggregateMismatch(string Day, string App, string Unit, long LedgerEntries, long DailyEntries, long LedgerQuantity, long DailyQuantity);

/// <summary>الحُضور: الأَيّامُ مِن نافِذَتِه حَتّى اليَوم، والمُكتَمِلُ مِنها، والغائِبُ («لَقطَةٌ غائِبَة»)، وسُطورُ أَيّامٍ بِلا مُطالَبَة (يَتيمَة).</summary>
public sealed record MeterPresenceCheck(int Days, int Completed, IReadOnlyList<string> AbsentDays, int Orphans)
{
    /// <summary>كُلُّ أَيّامِ النافِذَة (<c>YYYY-MM-DD</c>) بِتَرتيبِها — تَقرَؤُها صَفحَةُ المَتجَرِ لِتَرسُمَ كُلَّ يَومٍ بِسُطورِه أَو «لَقطَةٌ غائِبَة».</summary>
    public IReadOnlyList<string> DayKeys { get; init; } = [];

    public int Defects => Orphans;
}

/// <summary>تَقريرُ المُطابَقَة — وسُطورُه (<see cref="Lines"/>) ورَمزُ خُروجِه (<see cref="ExitCode"/>).</summary>
public sealed record MeterReconciliationReport(
    MeterReconcileState State, string? App,
    IReadOnlyList<MeterUnitCheck> Units, IReadOnlyList<MeterIntentCheck> Intents,
    string? Cursor, int AggregateRows, IReadOnlyList<MeterAggregateMismatch> Mismatches,
    MeterPresenceCheck Presence, IReadOnlyList<MeterDayCount> PresenceRows,
    IReadOnlyList<string> ImporterTenants, int Epochs)
{
    public static MeterReconciliationReport Failed(MeterReconcileState state, string? app)
        => new(state, app, [], [], null, 0, [], new MeterPresenceCheck(0, 0, [], 0), [], [], 0);

    /// <summary>ما قورِن: مَصادِرُ الحِفظِ في نَوافِذِها والنِيّاتُ المَقبولَة — وصِفرُه أَداةٌ عَمياء.</summary>
    public int Compared => Units.Sum(u => u.Compared) + Intents.Sum(i => i.Accepted);

    public int Defects => Units.Sum(u => u.Defects) + Intents.Sum(i => i.Defects) + Mismatches.Count + Presence.Defects;

    public int Seed => Units.Sum(u => u.Seed);

    /// <summary>0 نَظيف · 1 عَيبٌ أَو صِفرُ مُقارَنَةٍ أَو لا دَفتَرَ أَو عُطل.</summary>
    public int ExitCode => State == MeterReconcileState.Data && Compared > 0 && Defects == 0 ? 0 : 1;

    /// <summary>سُطورُ الطِباعَة — لِكُلِّ وَحدَةٍ ما قورِنَ وما نَقَصَ وما يَتُمَ ومَتاجِرُ الصِفر، ثُمَّ النِيّات، ثُمَّ المَجاميعُ والحُضورُ والاستِثناءات، ثُمَّ الحُكم.</summary>
    public IReadOnlyList<string> Lines()
    {
        const string Tag = MeterReconciliation.LogTag;
        if (State != MeterReconcileState.Data)
            return [$"{Tag} {(State == MeterReconcileState.NoLedger ? "ledger absent — nothing to compare" : "read failed")} → exit {ExitCode}"];
        var lines = new List<string>
        {
            $"{Tag} scope {App ?? "all stores"} · units {Units.Count} persistence · {Intents.Count} outbound · epochs {Epochs}",
        };
        foreach (var u in Units)
        {
            lines.Add($"{Tag} {u.Unit} · compared {u.Compared} · missing {u.Missing} · orphans {u.Orphans} · zero-entry stores {u.ZeroEntryStores.Count}"
                      + (u.ZeroEntryStores.Count == 0 ? "" : $" ({string.Join(", ", u.ZeroEntryStores)})")
                      + $" · seed {u.Seed}" + (u.Excluded == 0 ? "" : $" · importer-excluded {u.Excluded}")
                      + (u.Undated == 0 ? "" : $" · undated {u.Undated}") + (u.HasEpoch ? "" : " · no epoch"));
            foreach (var id in u.MissingIds) lines.Add($"{Tag}   missing {id}");
            foreach (var id in u.OrphanIds) lines.Add($"{Tag}   orphan {id}");
        }
        foreach (var i in Intents)
        {
            lines.Add($"{Tag} {i.Unit} · intents {i.Intents} · accepted {i.Accepted} · missing {i.Missing} · orphans {i.Orphans} · unrecorded {i.Unrecorded} · "
                      + $"pending {i.Pending} · unknown {i.Unknown} · refused {i.Refused}");
            foreach (var id in i.MissingIds) lines.Add($"{Tag}   missing {id}");
        }
        lines.Add($"{Tag} aggregates · cursor {Cursor ?? "(none)"} · rows {AggregateRows} · mismatches {Mismatches.Count}");
        foreach (var m in Mismatches.Take(MeterReconciliation.Samples))
            lines.Add($"{Tag}   mismatch {m.Day} {m.App} {m.Unit} · ledger {m.LedgerEntries}/{m.LedgerQuantity} · agg_daily {m.DailyEntries}/{m.DailyQuantity}");
        lines.Add($"{Tag} presence · days {Presence.Days} · completed {Presence.Completed} · absent {Presence.AbsentDays.Count}"
                  + (Presence.AbsentDays.Count == 0 ? "" : $" ({string.Join(", ", Presence.AbsentDays.Take(MeterReconciliation.Samples))})")
                  + $" · orphans {Presence.Orphans} · entries {PresenceRows.Sum(r => r.Entries)}");
        lines.Add($"{Tag} exclusions · seed {Seed}"
                  + (Seed == 0 ? "" : $" ({string.Join(" · ", Units.Where(u => u.Seed > 0).Select(u => $"{u.Unit} {u.Seed}"))})")
                  + $" · importer tenants {ImporterTenants.Count}" + (ImporterTenants.Count == 0 ? "" : $" ({string.Join(", ", ImporterTenants)})"));
        lines.Add($"{Tag} result · sources compared {Compared} · defects {Defects} → exit {ExitCode}"
                  + (Compared == 0 ? " (0 sources compared — a blind run)" : ""));
        return lines;
    }
}

/// <summary>
/// <para><b>المُطابَقَة</b> (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9): الدَوالُّ النَقِيَّةُ (<see cref="Compare"/>، <see cref="CompareIntents"/>،
/// <see cref="CompareAggregates"/>، <see cref="ComparePresence"/>) تَحكُمُ عَلى ما قَرَأَه <see cref="MeterSourceReaders"/>، و<see cref="RunAsync"/> يَقرَأُ ويَحكُمُ في لَقطَةٍ واحِدَة.</para>
/// <para>ويُحقَنُ في صَفحَةِ المَتجَرِ (<see cref="StoreAsync"/>) بِسُلوكِ <c>MeterQueries</c>: <c>42P01</c> «لا دَفتَر»، وأَيُّ عُطلٍ آخَرَ سَطرُ سِجِلٍّ وبِطاقَة — لا ‏500.</para>
/// </summary>
public sealed class MeterReconciliation(IDocumentStore store, ILogger<MeterReconciliation> log)
{
    /// <summary>بادِئَةُ كُلِّ سَطرٍ يَطبَعُه السكربتُ وتُسَجِّلُه الصَفحَةُ عِندَ العُطل.</summary>
    public const string LogTag = "[meter-reconcile]";

    /// <summary>عَيِّناتُ المُعَرِّفاتِ لِكُلِّ وَحدَة — تُطبَعُ لِيُبدَأَ مِنها التَحقيق، لا كُلُّها.</summary>
    public const int Samples = 5;

    /// <summary>صَفحَةُ المَتجَر: المُطابَقَةُ مَحصورَةٌ بِمَتجَرِها — وعُطلُها سَطرٌ وحالَة، لا ‏500.</summary>
    public async Task<MeterReconciliationReport> StoreAsync(string app, CancellationToken ct = default)
    {
        try
        {
            return await RunAsync(store, app, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError("{Tag} store {App}: {Type} {SqlState} {Message}", LogTag, app, e.GetType().Name, (e as PostgresException)?.SqlState ?? "-", e.Message);
            return MeterReconciliationReport.Failed(MeterReconcileState.Error, app);
        }
    }

    /// <summary>
    /// <para><b>القِراءَةُ والحُكمُ في لَقطَةٍ واحِدَة</b> — <paramref name="app"/>: مَتجَرٌ واحِد، و<c>null</c> القاعِدَةُ كُلُّها. ويَرمي عُطلَ القاعِدَةِ إلى مُناديه (السكربتُ يَطبَعُه
    /// ويَخرُجُ 1)؛ وجَدوَلُ دَفتَرٍ غائِبٌ «لا دَفتَر».</para>
    /// </summary>
    public static async Task<MeterReconciliationReport> RunAsync(IDocumentStore store, string? app = null, CancellationToken ct = default)
    {
        var catalog = MeterUnitCatalog.Default;
        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var ro = new NpgsqlCommand("set transaction read only", conn, tx)) await ro.ExecuteNonQueryAsync(ct);

        await using (var missing = new NpgsqlCommand(MeterSourceReaders.LedgerTablesMissing, conn, tx))
            if (await missing.ExecuteScalarAsync(ct) is string) return MeterReconciliationReport.Failed(MeterReconcileState.NoLedger, app);

        var epochs = await MeterSourceReaders.EpochsAsync(conn, tx, ct);
        var importer = await MeterSourceReaders.ImporterTenantsAsync(store, conn, tx, app, ct);
        var excluded = importer.ToHashSet(StringComparer.Ordinal);

        var persistence = catalog.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).ToList();
        var outbound = catalog.Units.Where(u => u.Boundary == MeterBoundaries.Outbound).ToList();
        var presence = catalog.Units.Where(u => u.Boundary == MeterBoundaries.Presence).ToList();

        var entries = await MeterSourceReaders.EntriesAsync(conn, tx, [.. persistence.Concat(outbound).Select(u => u.Code)], app, ct);
        var byUnit = entries.ToLookup(e => e.Unit, StringComparer.Ordinal);

        var units = new List<MeterUnitCheck>();
        foreach (var unit in persistence)
        {
            DateTimeOffset? since = epochs.TryGetValue(unit.Code, out var s) ? s : null;
            var read = await MeterSourceReaders.PersistenceAsync(store, conn, tx, unit, since, app, ct);
            var unitExcluded = MeterSourceReaders.ImporterReconcilers.Contains(unit.Reconcile ?? "", StringComparer.Ordinal)
                ? excluded : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);
            units.Add(Compare(unit.Code, unit.Reconcile ?? "", since is not null, read.Sources, [.. byUnit[unit.Code]], unitExcluded, read.Undated));
        }

        var intents = await MeterSourceReaders.IntentsAsync(conn, tx, app, ct);
        var intentChecks = outbound.Select(u => CompareIntents(u.Code, intents, [.. byUnit[u.Code]])).ToList();

        var (cursor, ledger, daily) = await MeterSourceReaders.AggregatesAsync(conn, tx, app, ct);
        var mismatches = CompareAggregates(ledger, daily);

        var (completed, today, presenceRows) = await MeterSourceReaders.PresenceAsync(conn, tx, [.. presence.Select(u => u.Code)], app, ct);
        var presenceSince = presence.Select(u => epochs.TryGetValue(u.Code, out var p) ? p : (DateTimeOffset?)null).Where(p => p is not null).Min();
        var presenceCheck = ComparePresence(presenceSince is { } ps ? PresenceSnapshot.DayOf(ps) : null, DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            completed, presenceRows);

        await tx.CommitAsync(ct);
        return new MeterReconciliationReport(MeterReconcileState.Data, app, units, intentChecks, cursor, daily.Count, mismatches,
            presenceCheck, presenceRows, importer, epochs.Count);
    }

    /// <summary>
    /// <para><b>وَحدَةُ حِفظٍ بِالاتِّجاهَين — دالَّةٌ نَقِيَّة</b>: <b>ناقِص</b> = مَصدَرٌ في النافِذَةِ لا سَطرَ بِمُعَرِّفِه المُنتَظَر؛ <b>يَتيم</b> = سَطرٌ لا مَصدَرَ بِمُعَرِّفِه
    /// (أَيّاً كانَ وَقتُ المَصدَر — فَمَصدَرٌ قَبلَ النافِذَةِ لَه سَطرٌ لا يُعَدُّ يَتيماً)؛ <b>مَتجَرُ صِفر</b> = مَتجَرٌ لَه مَصدَرٌ في النافِذَةِ ولا سَطرَ لِلوَحدَةِ فيه.
    /// والمَتاجِرُ المُستَثناةُ (<paramref name="excludedApps"/>) لا تُقارَنُ مَصادِرُها ولا سُطورُها، وتُعَدُّ مَصادِرُها في <see cref="MeterUnitCheck.Excluded"/>.</para>
    /// </summary>
    public static MeterUnitCheck Compare(string unit, string reconcile, bool hasEpoch, IReadOnlyList<MeterSource> sources, IReadOnlyList<MeterLedgerRow> entries,
        IReadOnlySet<string> excludedApps, int undated = 0)
    {
        var kept = sources.Where(s => !excludedApps.Contains(s.App)).ToList();
        var counted = entries.Where(e => e.Unit == unit && !excludedApps.Contains(e.App)).ToList();
        var entryIds = counted.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var sourceIds = kept.Select(s => s.EntryId).ToHashSet(StringComparer.Ordinal);
        var compared = kept.Where(s => s.InWindow).ToList();
        var missing = compared.Where(s => !entryIds.Contains(s.EntryId)).Select(s => s.EntryId).Order(StringComparer.Ordinal).ToList();
        var orphans = counted.Where(e => !sourceIds.Contains(e.Id)).Select(e => e.Id).Order(StringComparer.Ordinal).ToList();
        var appsWithEntries = counted.Select(e => e.App).ToHashSet(StringComparer.Ordinal);
        var zero = compared.Select(s => s.App).Distinct(StringComparer.Ordinal).Where(a => !appsWithEntries.Contains(a)).Order(StringComparer.Ordinal).ToList();
        return new MeterUnitCheck(unit, reconcile, hasEpoch, compared.Count, missing.Count, orphans.Count, zero,
            counted.Count(e => e.Role == MeterSentinels.Seed), sources.Count - kept.Count, undated,
            [.. missing.Take(Samples)], [.. orphans.Take(Samples)]);
    }

    /// <summary>
    /// <para><b>وَحدَةٌ صادِرَة — دالَّةٌ نَقِيَّة</b>: <b>ناقِص</b> = نِيَّةٌ مَقبولَةٌ لا سَطرَ بِمُعَرِّفِها (والقَبولُ والسَطرُ مُعامَلَةٌ واحِدَة، فَهُوَ عَيب)؛ <b>يَتيم</b> = سَطرٌ لا نِيَّةَ
    /// مَقبولَةً بِمُعَرِّفِه (الكاتِبُ يَكتُبُ النِيَّةَ مَقبولَةً مَعَ كُلِّ سَطر)؛ و«غَيرُ مُسَجَّلَة» = مُعَلَّقَةٌ قَديمَة — تُعَدُّ وتُعرَضُ ولا تُفشِل، كَالمَجهولَةِ والمَرفوضَة.</para>
    /// </summary>
    public static MeterIntentCheck CompareIntents(string unit, IReadOnlyList<MeterIntentRow> intents, IReadOnlyList<MeterLedgerRow> entries)
    {
        var mine = intents.Where(i => i.Unit == unit).ToList();
        var accepted = mine.Where(i => i.Outcome == "accepted").Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var entryIds = entries.Where(e => e.Unit == unit).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var missing = accepted.Where(id => !entryIds.Contains(id)).Order(StringComparer.Ordinal).ToList();
        return new MeterIntentCheck(unit, accepted.Count, missing.Count, entryIds.Count(id => !accepted.Contains(id)),
            mine.Count(i => i.Outcome == "pending" && i.Stale), mine.Count(i => i.Outcome == "pending" && !i.Stale),
            mine.Count(i => i.Outcome == "unknown"), mine.Count(i => i.Outcome == "refused"), [.. missing.Take(Samples)]);
    }

    /// <summary><b>المَجاميعُ — دالَّةٌ نَقِيَّة</b>: لِكُلِّ (يَوم، مَتجَر، وَحدَة) في أَيٍّ مِن الطَرَفَين، عَدُّ الدَفتَرِ حَتّى المُؤَشِّرِ وكَمِّيَّتُه = <c>agg_daily</c> — بِالضَبطِ لا تَقريباً.</summary>
    public static IReadOnlyList<MeterAggregateMismatch> CompareAggregates(IReadOnlyList<MeterDayCount> ledger, IReadOnlyList<MeterDayCount> daily)
    {
        static string Key(MeterDayCount c) => $"{c.Day}|{c.App}|{c.Unit}";
        var l = ledger.ToDictionary(Key, StringComparer.Ordinal);
        var d = daily.ToDictionary(Key, StringComparer.Ordinal);
        return l.Keys.Union(d.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(k => (k, L: l.GetValueOrDefault(k), D: d.GetValueOrDefault(k)))
            .Where(x => (x.L?.Entries ?? 0) != (x.D?.Entries ?? 0) || (x.L?.Quantity ?? 0) != (x.D?.Quantity ?? 0))
            .Select(x =>
            {
                var c = x.L ?? x.D!;
                return new MeterAggregateMismatch(c.Day, c.App, c.Unit, x.L?.Entries ?? 0, x.D?.Entries ?? 0, x.L?.Quantity ?? 0, x.D?.Quantity ?? 0);
            }).ToList();
    }

    /// <summary><b>الحُضور — دالَّةٌ نَقِيَّة</b>: الأَيّامُ مِن يَومِ نافِذَةِ الحُضورِ (<paramref name="from"/>) حَتّى اليَوم؛ والغائِبُ ما لا مُطالَبَةَ مُكتَمِلَةَ لَه («لَقطَةٌ غائِبَة» —
    /// يُطبَعُ ولا يُفشِل)، واليَتيمُ سَطرُ حُضورٍ في يَومٍ بِلا مُطالَبَةٍ مُكتَمِلَة (السُطورُ والمُطالَبَةُ مُعامَلَةٌ واحِدَة — فَهُوَ عَيب).</summary>
    public static MeterPresenceCheck ComparePresence(DateOnly? from, DateOnly today, IReadOnlySet<string> completed, IReadOnlyList<MeterDayCount> rows)
    {
        var days = new List<string>();
        if (from is { } f)
            for (var d = f; d <= today; d = d.AddDays(1)) days.Add(PresenceReader.DayKey(d));
        var absent = days.Where(d => !completed.Contains(d)).ToList();
        var orphans = rows.Where(r => !completed.Contains(r.Day)).Sum(r => r.Entries);
        return new MeterPresenceCheck(days.Count, days.Count(completed.Contains), absent, (int)orphans) { DayKeys = days };
    }
}
