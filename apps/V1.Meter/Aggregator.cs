using System.Data;
using System.Diagnostics;
using System.Globalization;
using ACommerce.Platform.Shared.Metering;
using Npgsql;

namespace ACommerce.V1.Meter;

// ═══ المُجَمِّعُ المُستَقِلّ — يَسحَبُ مِن الدَفتَرِ ولا يُدفَعُ إلَيه (‏2026-11-12، الموجَة ١٣ W13-6·٤ — ADR-051 §١٢؛ التَصميم §10) ═══════════
//
// **المُؤَشِّرُ `(generation, writer_xid, id)` والأُفُقُ عَلى الجيلِ الجاري وَحدَه** (D5، R3، R4): مُعَرِّفُ المُعامَلَةِ الكاتِبَةِ يُرَتِّبُ ما أُودِعَ
// بِتَرتيبٍ لا يَسبِقُ فيه سَطرٌ أَقدَمُ ما لَم يُودَعْ بَعد — ما تَحتَ أُفُقِ اللَقطَةِ (`pg_snapshot_xmin`، أَقدَمُ مُعامَلَةٍ جارِيَةٍ عَلى العُنقود) انتَهى
// فَرُؤيَتُه نِهائِيَّة. ومُؤَشِّرُ تَسَلسُلِ الصُفوفِ يُضَيِّعُ السَطرَ الأَقدَمَ (D5)، ومُعَرِّفُ المُعامَلَةِ وَحدَه يَعمى بَعدَ الاستِعادَةِ في عُنقودٍ جَديد (R3)،
// والجيلُ يَقرَؤُه (R4). **وكُلُّ صَفحَةٍ مُعامَلَةُ `REPEATABLE READ` واحِدَة**: رَأسُها يُثَبِّتُ اللَقطَة، ثُمَّ مَفاتيحُها، ثُمَّ مَجاميعُ مَداها، ثُمَّ
// المُؤَشِّر — يُودَعُ الأَخيرانِ مَعاً أَو لا يُودَعُ أَحَدُهُما (فَالانهِيارُ بَينَهُما لا يُضاعِفُ شَيئاً)، وتُعادُ مَرَّةً عَلى 40001.
//
// **مُجَمِّعٌ واحِدٌ لِكُلِّ قاعِدَة** (COR-9): قُفلٌ استِشارِيٌّ لِلجَلسَةِ عَلى اتِّصالٍ يَبقى ما بَقِيَت العَمَلِيَّة. **و`--once` يَلحَقُ بِلَقطَةِ البَدءِ T0**
// (COR-8): لا يَنتَهي وسَطرٌ أُودِعَ قَبلَ البَدءِ لَم يُجمَع؛ وإن أَمسَكَ الأُفُقَ مُعامَلَةٌ في أَيِّ قاعِدَةٍ عَلى العُنقود (R2) أَكثَرَ مِن المُهلَة
// خَرَجَ 2 مُسَمِّياً مُمسِكَه. **ولا يَتَقَدَّمُ فَوقَ تَراجُع** (R4): مُؤَشِّرٌ أَو سَطرٌ في الجيلِ الجاري فَوقَ العُنقودِ ⇐ الخُروج 4 حَتّى يُقلِعَ التَطبيقُ
// فَيَفتَحَ جيلاً. **ولا يُنشِئُ شَيئاً**: دَفتَرٌ غائِبٌ ⇐ «لا شَيءَ يُسحَب» والخُروج 0.
//
// **وكُلُّ صَفحَةٍ تَتَحَقَّقُ مِن نَفسِها لا مِن القُفل** (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · M6A-CNT-2): تَقرَأُ المُؤَشِّرَ المَخزونَ في لَقطَتِها وتَسحَبُ
// مِنه — لا مِمّا في الذاكِرَة — وتُودِعُ تَقَدُّمَه بِشَرطِ ما قَرَأَت (`MeterSql.AggCursorSet`)، فَإعادَةُ 40001 تَقرَؤُه مِن جَديد؛ وتُسأَلُ جَلسَةُ القُفلِ قَبلَ
// كُلِّ صَفحَة، وجَلسَةٌ مَيِّتَةٌ ⇐ الخُروج 3. كانَ القُفلُ وَحدَه يَضمَنُ «مَرَّةً واحِدَة»، و`--interval` يَترُكُ اتِّصالَه خامِلاً: قُتِلَ فَأَخَذَ ثانٍ القُفلَ،
// وأُعيدَت صَفحَةُ الأَوَّلِ بَعدَ 40001 بِمُؤَشِّرٍ قَديمٍ فَجُمِعَ المَدى مَرَّتَين (مَقيس: «سُطورُ الدَفتَر 13 · مَجاميعُ agg_daily 23»).
//
// **ولا سِعرَ هُنا** (القاعِدَة ١٦): يَعُدُّ السُطورَ ويَجمَعُ الكَمِّيّات، ويَطبَعُ التَشغيلَ والحُضورَ مُنفَصِلَين — والتَأكيدُ لِكُلِّ وَحدَةٍ لا لِلمَجموع.

/// <summary>رُموزُ الخُروج — كُلُّ ما سِوى الصِفرِ يُحمِرُّ سكربتاً يُناديه، فَلا يَخضَرُّ صامِتاً (COR-8، COR-9، R4).</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    /// <summary>وَسيطٌ مَجهولٌ أَو اتِّصالٌ غائِبٌ أَو عُطلٌ غَيرُ مُتَوَقَّع.</summary>
    public const int Failure = 1;
    /// <summary><c>--once</c>: مُعامَلَةٌ عَلى العُنقودِ تُمسِكُ الأُفُقَ أَكثَرَ مِن المُهلَة وسُطورٌ أُودِعَت قَبلَ البَدءِ تَنتَظِرُ تَحتَه.</summary>
    public const int HorizonHeld = 2;
    /// <summary><c>--once</c>: مُجَمِّعٌ آخَرُ يُمسِكُ القُفلَ أَكثَرَ مِن المُهلَة — أَو (في الوَضعَين) جَلسَةُ قُفلِ هذا المُجَمِّعِ ماتَت تَحتَه، فَلا يَسحَبُ
    /// صَفحَةً بَعدَها (‏2026-11-12، M6A-CNT-2): ما أُودِعَ قَبلَها قائِم، ومُجَمِّعٌ آخَرُ قَد يُمسِكُ القُفلَ الآن.</summary>
    public const int LockHeld = 3;
    /// <summary>المُؤَشِّرُ أَو سَطرٌ في الجيلِ الجاري فَوقَ العُنقود: قاعِدَةٌ استُعيدَت ولَم يُقلِعِ التَطبيقُ عَلَيها.</summary>
    public const int XidRegression = 4;
}

/// <summary>خِياراتُ التَشغيل — مِن سَطرِ الأَوامِرِ وَحدَه (<see cref="Parse"/>).</summary>
public sealed record AggregatorOptions
{
    /// <summary>صَفحَةٌ بِأَلفِ سَطر: مُعامَلَةٌ قَصيرَةٌ لا تُمسِكُ الأُفُقَ طَويلاً، ومَجاميعُها تَتَراكَمُ في صَفٍّ لِكُلِّ مَفتاح. رَقمٌ تَشغيلِيٌّ لا مُنتَجِيّ.</summary>
    public const int DefaultPage = 1000;
    /// <summary>مُهلَةُ <c>--once</c> لِلقُفلِ ولِلأُفُق — 60 ثانِيَة (التَصميم §10.2، COR-8 وCOR-9): أَطوَلُ مِن أَيِّ مُعامَلَةِ عَمَلٍ في التَطبيق،
    /// وأَقصَرُ مِن أَن يَنتَظِرَ سكربتٌ بِلا نِهايَة. و<c>--wait</c> يُغَيِّرُها لِتَشغيلٍ واحِد.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);

    public const string Usage =
        "usage: V1.Meter --once [--rebuild] [--page N] [--wait S]  |  V1.Meter --interval S [--page N]   (ConnectionStrings__Postgres from the environment)";

    public bool Once { get; init; }
    public TimeSpan Interval { get; init; }
    public int Page { get; init; } = DefaultPage;
    public TimeSpan Wait { get; init; } = DefaultWait;
    public bool Rebuild { get; init; }

    public static (AggregatorOptions? Options, string? Error) Parse(IReadOnlyList<string> args)
    {
        bool once = false, rebuild = false;
        int? interval = null, page = null, wait = null;
        for (var i = 0; i < args.Count; i++)
        {
            string? Next() => i + 1 < args.Count ? args[++i] : null;
            switch (args[i])
            {
                case "--once": once = true; break;
                case "--rebuild": rebuild = true; break;
                case "--interval": interval = Positive(Next(), 86_400); if (interval is null) return (null, "--interval takes seconds (1..86400)"); break;
                case "--page": page = Positive(Next(), 100_000); if (page is null) return (null, "--page takes a row count (1..100000)"); break;
                case "--wait": wait = Positive(Next(), 3_600); if (wait is null) return (null, "--wait takes seconds (1..3600)"); break;
                default: return (null, $"unknown argument «{args[i]}»");
            }
        }
        if (once == (interval is not null)) return (null, "exactly one of --once or --interval S");
        if (rebuild && !once) return (null, "--rebuild runs with --once only");
        if (wait is not null && !once) return (null, "--wait applies to --once only (--interval waits for the lock)");
        return (new AggregatorOptions
        {
            Once = once,
            Rebuild = rebuild,
            Interval = TimeSpan.FromSeconds(interval ?? 0),
            Page = page ?? DefaultPage,
            Wait = wait is { } w ? TimeSpan.FromSeconds(w) : DefaultWait,
        }, null);
    }

    private static int? Positive(string? text, int max)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1 && v <= max ? v : null;
}

/// <summary>المُؤَشِّر: آخِرُ مِفتاحٍ جُمِع. و<see cref="Start"/> قَبلَ كُلِّ مِفتاحٍ (الجيلُ يَبدَأُ مِن 1).</summary>
internal readonly record struct Cursor(int Generation, string Xid, string Id)
{
    public static readonly Cursor Start = new(0, "0", "");
    public override string ToString() => $"({Generation}, {Xid}, {Id})";
}

/// <summary>
/// <para><b>المُجَمِّع</b> — يَقرَأُ <c>meter.usage_entry</c> بِالمُؤَشِّرِ ويَكتُبُ <c>meter.agg_daily</c> و<c>meter.agg_cursor</c> وَحدَهُما، بِجُمَلِ
/// <see cref="MeterSql"/> لا بِنُسَخٍ مِنها.</para>
/// </summary>
public sealed class Aggregator
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IntervalLockPoll = TimeSpan.FromSeconds(5);

    private readonly string _connectionString;
    private readonly string _target;
    private readonly AggregatorOptions _options;
    private readonly TextWriter _out;
    private readonly MeterUnitCatalog _catalog = MeterUnitCatalog.Default;
    private NpgsqlConnection? _lock;
    private int _lockPid;

    public Aggregator(string connectionString, AggregatorOptions options, TextWriter output)
    {
        // ‏بِلا تَجميعِ اتِّصالات: إغلاقُ اتِّصالِ القُفلِ يُنهي جَلسَتَه فَيَسقُطُ القُفل، واتِّصالانِ طَويلانِ لا يَربَحانِ مِن التَجميع.
        var b = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = MeterSql.AggregatorApplicationName, Pooling = false };
        _connectionString = b.ConnectionString;
        _target = $"{b.Host}/{b.Database}";
        _options = options;
        _out = output;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var mode = _options.Once ? (_options.Rebuild ? "once rebuild" : "once") : $"interval {(int)_options.Interval.TotalSeconds}s";
        Line($"start {mode} · db {_target} · page {_options.Page}");

        await using var work = await OpenAsync(ct);
        var missing = await TextsAsync(work, MeterSql.LedgerTablesMissing, ct);
        if (missing.Count > 0)
        {
            Line($"ledger absent — nothing to pull ({string.Join(", ", missing)} absent; the app creates them at boot)");
            return ExitCodes.Ok;
        }

        // ‏اتِّصالُ القُفلِ يَبقى مَفتوحاً إلى آخِرِ التَشغيل — جَلسَتُه هِيَ القُفل.
        await using var lockConnection = await OpenAsync(ct);
        if (!await LockAsync(lockConnection, ct)) return ExitCodes.LockHeld;
        (_lock, _lockPid) = (lockConnection, lockConnection.ProcessID);

        try
        {
            return _options.Once ? await OnceAsync(work, ct) : await IntervalAsync(work, ct);
        }
        catch (LockLostException e)
        {
            Line($"lock lost — the lock session (pid {_lockPid}) {e.Message} — another aggregator may hold the lock now; "
                 + "pages committed before stand, nothing more is pulled");
            return ExitCodes.LockHeld;
        }
    }

    /// <summary><b>جَلسَةُ القُفلِ تُسأَلُ قَبلَ كُلِّ صَفحَة</b> (M6A-CNT-2): نِداءٌ عَلى جَلسَةٍ مَيِّتَةٍ يَرمي، وجَلسَةٌ حَيَّةٌ بِلا قُفلٍ تُجيبُ <c>false</c>.
    /// والصَفحَةُ لا تَتَّكِئُ عَلى هذا وَحدَه — جَلسَةٌ تَموتُ بَعدَ السُؤالِ يَحرُسُها شَرطُ المُؤَشِّرِ في الصَفحَةِ نَفسِها.</summary>
    private async Task EnsureLockAsync(CancellationToken ct)
    {
        bool held;
        try { held = await ScalarAsync(_lock!, MeterSql.AggregatorLockHeldHere, ct) is true; }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or IOException)
        {
            throw new LockLostException($"ended ({e.GetType().Name}: {e.Message.Split('\n')[0]})");
        }
        if (!held) throw new LockLostException("no longer holds the lock");
    }

    private sealed class LockLostException(string message) : Exception(message);

    /// <summary>المُؤَشِّرُ المَخزونُ تَغَيَّرَ بَينَ قِراءَةِ الصَفحَةِ وإيداعِها — فَتَتَراجَعُ ولا تَكتُبُ شَيئاً.</summary>
    private sealed class CursorMovedException(string message) : Exception(message);

    // ─── القُفل ───────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<bool> LockAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var waited = Stopwatch.StartNew();
        var announced = false;
        while (true)
        {
            if (await ScalarAsync(conn, MeterSql.AggregatorTryLock, ct) is true)
            {
                if (announced) Line($"lock acquired after {(int)waited.Elapsed.TotalSeconds}s");
                return true;
            }
            var holder = await ScalarAsync(conn, MeterSql.AggregatorLockHolder, ct);
            var pid = holder is int p ? p.ToString(CultureInfo.InvariantCulture) : "?";
            if (!announced) { Line($"lock held by pid {pid} — waiting"); announced = true; }
            if (_options.Once && waited.Elapsed > _options.Wait)
            {
                Line($"lock held by pid {pid} for more than {(int)_options.Wait.TotalSeconds}s — another aggregator runs on this database");
                return false;
            }
            await Task.Delay(_options.Once ? Poll : IntervalLockPoll, ct);
        }
    }

    // ─── --once: إلى لَقطَةِ البَدء ───────────────────────────────────────────────────────────────────────────

    private async Task<int> OnceAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (await RegressedAsync(conn, ct)) return ExitCodes.XidRegression;
        if (_options.Rebuild) await RebuildAsync(conn, ct);

        var t0 = (string)(await ScalarAsync(conn, MeterSql.SnapshotNow, ct))!;
        var cursor = await ReadCursorAsync(conn, ct);
        var tally = new Tally();
        Stopwatch? blocked = null;
        while (true)
        {
            var before = tally.Rows;
            cursor = await PullAsync(conn, cursor, tally, ct);
            if (tally.Rows > before) blocked = null;
            if (!await PendingAsync(conn, cursor, t0, ct)) break;

            blocked ??= Stopwatch.StartNew();
            if (blocked.Elapsed > _options.Wait)
            {
                await SummaryAsync(conn, cursor, tally, ct);
                Line($"horizon held for more than {(int)_options.Wait.TotalSeconds}s — rows committed before the start wait below it · "
                     + await BlockersAsync(conn, ct));
                return ExitCodes.HorizonHeld;
            }
            await Task.Delay(Poll, ct);
        }
        await SummaryAsync(conn, cursor, tally, ct);
        return ExitCodes.Ok;
    }

    // ─── --interval ───────────────────────────────────────────────────────────────────────────────────────────

    private async Task<int> IntervalAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        while (true)
        {
            if (await RegressedAsync(conn, ct)) return ExitCodes.XidRegression;
            var tally = new Tally();
            var cursor = await PullAsync(conn, await ReadCursorAsync(conn, ct), tally, ct);
            await SummaryAsync(conn, cursor, tally, ct);
            await Task.Delay(_options.Interval, ct);
        }
    }

    // ─── التَراجُع وإعادَةُ البِناء ─────────────────────────────────────────────────────────────────────────────

    private async Task<bool> RegressedAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var current = Convert.ToInt32(await ScalarAsync(conn, MeterSql.CurrentGenerationOrFirst, ct), CultureInfo.InvariantCulture);
        var cursorAbove = await ScalarAsync(conn, MeterSql.CursorRegression, ct, ("cur", current)) is true;
        var rowsAbove = await ScalarAsync(conn, MeterSql.RegressionInGeneration, ct, ("g", current)) is true;
        if (!cursorAbove && !rowsAbove) return false;
        Line($"xid regression — boot the app (it opens a new generation) · gen {current} · cursor {await ReadCursorAsync(conn, ct)} "
             + $"above the cluster {cursorAbove.ToString().ToLowerInvariant()} · rows above the cluster {rowsAbove.ToString().ToLowerInvariant()}");
        return true;
    }

    private async Task RebuildAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand(MeterSql.AggRebuild, conn, tx)) await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        Line("rebuild — agg_daily emptied and the cursor reset; pulling the ledger from its start");
    }

    // ─── السَحب ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>صَفَحاتٌ إلى الأُفُق: تَقِفُ عِندَ أَوَّلِ صَفحَةٍ ناقِصَة.</summary>
    private async Task<Cursor> PullAsync(NpgsqlConnection conn, Cursor cursor, Tally tally, CancellationToken ct)
    {
        while (true)
        {
            await EnsureLockAsync(ct);
            var page = await PageAsync(conn, cursor, ct);
            tally.Generation = page.Generation;
            tally.Horizon = page.Horizon;
            // ‏المُؤَشِّرُ ما قَرَأَتهُ الصَفحَةُ وأَودَعَته — لا ما في الذاكِرَة (صَفحَةٌ خالِيَةٌ تُعيدُ المَخزونَ كَما قَرَأَته).
            cursor = page.Last;
            if (page.Units.Count == 0) return cursor;
            tally.Pages++;
            foreach (var unit in page.Units) tally.Add(unit);
            if (page.Units.Count < _options.Page) return cursor;
        }
    }

    private sealed record Page(int Generation, string Horizon, Cursor From, Cursor Last, IReadOnlyList<string> Units);

    private async Task<Page> PageAsync(NpgsqlConnection conn, Cursor cursor, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            // ‏الإعادَةُ تَقرَأُ المُؤَشِّرَ المَخزونَ مِن جَديدٍ في لَقطَتِها (PageOnceAsync) — فَلا تَجمَعُ مَدىً أَودَعَه غَيرُها.
            try { return await PageOnceAsync(conn, cursor, ct); }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.SerializationFailure && attempt == 1)
            {
                Line("page retried once after 40001 (serialization failure)");
            }
            catch (CursorMovedException e) when (attempt == 1)
            {
                Line($"page retried once — {e.Message}");
            }
        }
    }

    private async Task<Page> PageOnceAsync(NpgsqlConnection conn, Cursor expected, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        int generation;
        string horizon;
        await using (var head = new NpgsqlCommand(MeterSql.PageHead, conn, tx))
        await using (var r = await head.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            generation = r.GetInt32(0);
            horizon = r.GetString(1);
        }

        // ‏المُؤَشِّرُ المَخزونُ في لَقطَةِ هذه الصَفحَة — مِنه تَسحَب، وبِشَرطِه تُودِع. وما في الذاكِرَةِ لِلمُقارَنَةِ وَحدَها.
        var cursor = await ReadCursorAsync(conn, ct, tx);
        if (cursor != expected)
            Line($"cursor moved outside this runner — memory {expected} · stored {cursor}; paging from the stored cursor");

        var keys = new List<(Cursor Key, string Unit)>();
        await using (var cmd = new NpgsqlCommand(MeterSql.PageKeys, conn, tx))
        {
            Bind(cmd, cursor);
            cmd.Parameters.AddWithValue("cur", generation);
            cmd.Parameters.AddWithValue("h", horizon);
            cmd.Parameters.AddWithValue("n", _options.Page);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                keys.Add((new Cursor(r.GetInt32(0), r.GetString(1), r.GetString(2)), r.GetString(3)));
        }
        if (keys.Count == 0)
        {
            await tx.CommitAsync(ct);
            return new Page(generation, horizon, cursor, cursor, []);
        }

        var last = keys[^1].Key;
        await using (var upsert = new NpgsqlCommand(MeterSql.AggUpsertRange, conn, tx))
        {
            Bind(upsert, cursor);
            BindLast(upsert, last);
            await upsert.ExecuteNonQueryAsync(ct);
        }
        await using (var move = new NpgsqlCommand(MeterSql.AggCursorSet, conn, tx))
        {
            Bind(move, cursor);
            BindLast(move, last);
            if (await move.ExecuteNonQueryAsync(ct) != 1)
                throw new CursorMovedException($"the stored cursor is no longer {cursor} — this page rolled back without writing");
        }
        await tx.CommitAsync(ct);
        return new Page(generation, horizon, cursor, last, keys.Select(k => k.Unit).ToList());
    }

    private async Task<bool> PendingAsync(NpgsqlConnection conn, Cursor cursor, string t0, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(MeterSql.PendingBeforeSnapshot, conn);
        Bind(cmd, cursor);
        cmd.Parameters.AddWithValue("t0", t0);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private async Task<string> BlockersAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var horizon = (string)(await ScalarAsync(conn, "select pg_snapshot_xmin(pg_current_snapshot())::text", ct))!;
        var blockers = new List<string>();
        await using (var cmd = new NpgsqlCommand(MeterSql.HorizonBlockers, conn))
        {
            cmd.Parameters.AddWithValue("h", horizon);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                blockers.Add($"blocker pid {r.GetString(0)} · xid {r.GetString(1)} · datname {r.GetString(2)} · state {r.GetString(3)}");
        }
        return blockers.Count > 0
            ? $"horizon {horizon} · {string.Join(" | ", blockers)}"
            : $"horizon {horizon} · no blocker found now (it ended between the checks)";
    }

    private async Task SummaryAsync(NpgsqlConnection conn, Cursor cursor, Tally tally, CancellationToken ct)
    {
        long lag;
        await using (var cmd = new NpgsqlCommand(MeterSql.Lag, conn))
        {
            Bind(cmd, cursor);
            lag = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        // ‏الجانِبانِ لا يَختَلِطان (مَبدَأُ المالِك): الحُضورُ — وَحدَةُ جانِبِ التِقنِيَة — يُطبَعُ بَعيداً عَن عَدِّ التَشغيل.
        var operations = tally.ByUnit.Where(u => !IsTechnology(u.Key)).ToList();
        var presence = tally.ByUnit.Where(u => IsTechnology(u.Key)).ToList();
        Line($"gen {tally.Generation} · pulled {Sum(operations)}{Units(operations)} · presence {Sum(presence)}{Units(presence)} · "
             + $"pages {tally.Pages} · horizon {tally.Horizon} · lag {lag}s");
    }

    private bool IsTechnology(string unit) => _catalog.Find(unit)?.Side == MeterSides.Technology;

    private static long Sum(IEnumerable<KeyValuePair<string, long>> units) => units.Sum(u => u.Value);

    private static string Units(IReadOnlyCollection<KeyValuePair<string, long>> units)
        => units.Count == 0 ? "" : " (" + string.Join(" · ", units.Select(u => $"{u.Key} {u.Value}")) + ")";

    // ─── أَدَوات ─────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Tally
    {
        public SortedDictionary<string, long> ByUnit { get; } = new(StringComparer.Ordinal);
        public long Rows { get; private set; }
        public int Pages { get; set; }
        public int Generation { get; set; }
        public string Horizon { get; set; } = "-";
        public void Add(string unit) { ByUnit[unit] = ByUnit.GetValueOrDefault(unit) + 1; Rows++; }
    }

    private static void Bind(NpgsqlCommand cmd, Cursor c)
    {
        cmd.Parameters.AddWithValue("g", c.Generation);
        cmd.Parameters.AddWithValue("x", c.Xid);
        cmd.Parameters.AddWithValue("i", c.Id);
    }

    private static void BindLast(NpgsqlCommand cmd, Cursor c)
    {
        cmd.Parameters.AddWithValue("lg", c.Generation);
        cmd.Parameters.AddWithValue("lx", c.Xid);
        cmd.Parameters.AddWithValue("li", c.Id);
    }

    private async Task<Cursor> ReadCursorAsync(NpgsqlConnection conn, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(MeterSql.AggCursorRead, conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new Cursor(r.GetInt32(0), r.GetString(1), r.GetString(2)) : Cursor.Start;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task<List<string>> TextsAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<string>();
        while (await r.ReadAsync(ct)) rows.Add(r.GetString(0));
        return rows;
    }

    private void Line(string text)
    {
        _out.WriteLine("[meter-agg] " + text);
        _out.Flush();
    }
}
