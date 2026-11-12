using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G8 · P8a — المُجَمِّعُ المُستَقِلُّ نَفسُه (`apps/V1.Meter` المَبنِيّ) عَلى قاعِدَةٍ جَديدَةٍ لِكُلِّ صَفّ ═══════════════════════════════════════
// (‏2026-11-12، الموجَة ١٣ W13-6·٤ — ADR-051 §١٢؛ التَصميم §10 و§12 G8 و§13 P8a). **الثُنائِيُّ المَبنِيُّ لا نُسخَةٌ مِنه** (القاعِدَة ١٤): مَشروعُ
// الاختِبارِ لا يُشيرُ إلى `V1.Meter` (G10)، فَيُشَغَّلُ `apps/V1.Meter/bin/<التَهيِئَة>/net10.0/V1.Meter.dll` — يَبنيه الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml) —
// بِرُموزِ خُروجِه ومُخرَجِه. والدَفتَرُ يُنشِئُه `MeterBoot` الحَقيقِيّ (ميزَةُ Weasel نَفسُها)، والسُطورُ تُكتَبُ بِمُعامَلاتٍ يَتَحَكَّمُ الصَفُّ في
// تَرتيبِها — فَالتَداخُلُ والانهِيارُ والاستِعادَةُ والأُفُقُ المُمسَكُ تُصنَعُ بِأَعيانِها لا تُنتَظَر.
//
// **والتَأكيدُ لِكُلِّ وَحدَةٍ لا لِلمَجموع** (COR-8): سَطرُ السَحبِ يُقرَأُ «deal.create 1» لا «pulled 1»، والمَجاميعُ تُقارَنُ صَفّاً بِصَفٍّ بِتَجميعِ
// الدَفتَرِ نَفسِه — وهُوَ ما يُعطيه مُجَمِّعٌ واحِدٌ مِن الصِفر (صَفُّ إعادَةِ البِناء يُثبِتُ ذلك).
//
// **وقاعِدَةٌ جَديدَةٌ لِكُلِّ صَفّ** (`{قاعِدَةُ البيئَة}_ma_{8}`، تُحذَفُ في `finally`): المُجَمِّعُ يَجمَعُ كُلَّ ما في القاعِدَة، والبَراهينُ الأُخرى تَكتُبُ
// في القاعِدَةِ المُشتَرَكَةِ سُطوراً لا يُعرَفُ عَدَدُها.

[Collection(LiveProofCollection.Name)]
public class LiveMeterAggregatorProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    /// <summary>تَهيِئَةُ البِناءِ مِن مَسارِ الاختِبارِ نَفسِه (<c>bin/Release/net10.0</c>) — فَيُشَغَّلُ المُجَمِّعُ المَبنِيُّ بِالتَهيِئَةِ ذاتِها.</summary>
    private static string MeterDll()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.Meter", "bin", tfm.Parent!.Name, tfm.Name, "V1.Meter.dll");
    }

    // ─── القاعِدَةُ والدَفتَر ───────────────────────────────────────────────────────────────────────────────────

    private sealed class Ledger : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Cs { get; init; }
        public DocumentStore? Store { get; init; }

        public static async Task<Ledger> CreateAsync(ITestOutputHelper output, bool boot = true)
        {
            var b = new NpgsqlConnectionStringBuilder(BaseCs);
            var stem = (b.Database ?? "wasayel").ToLowerInvariant();
            if (stem.Length > 48) stem = stem[..48];
            b.Database = $"{stem}_ma_{Guid.NewGuid().ToString("N")[..8]}";
            await ExecAsync(BaseCs, $"create database \"{b.Database}\"");
            DocumentStore? store = null;
            if (boot)
            {
                // ‏الدَفتَرُ كَما يُنشِئُه التَطبيق: ميزَةُ Weasel نَفسُها و`MeterBoot.EnsureAsync` نَفسُه (الجَداوِلُ الخَمسَةُ والجيلُ 1).
                store = DocumentStore.For(o =>
                {
                    o.Connection(b.ConnectionString);
                    o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
                });
                var boot1 = await MeterBoot.EnsureAsync(store);
                output.WriteLine($"[G8] {b.Database}: {boot1.Line}");
            }
            return new Ledger { Name = b.Database, Cs = b.ConnectionString, Store = store };
        }

        public async ValueTask DisposeAsync()
        {
            Store?.Dispose();
            NpgsqlConnection.ClearAllPools();
            await ExecAsync(BaseCs, $"drop database if exists \"{Name}\" with (force)");
        }
    }

    private static async Task ExecAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await ExecAsync(conn, null, sql, ps);
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> TextAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return await TextAsync(conn, null, sql, ps);
    }

    private static async Task<string> TextAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }

    private static async Task<NpgsqlConnection> OpenAsync(string cs)
    {
        var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>سَطرُ <c>deal.create</c> بِالأَعمِدَةِ الَّتي يَكتُبُها المُستَمِع — والجيلُ <c>0</c> يَأخُذُ آخِرَ جيلٍ مَفتوح كَما في <see cref="MeterSql.Insert"/>.</summary>
    private const string InsertEntry =
        "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation, occurred_at) "
        + "values (@id, 'deal.create', 1, @app, @ent, '_none', 'deal_status', '_none', 'deal', @id, "
        + "coalesce(nullif(@g, 0), (select max(g.generation) from meter.generation g), 1), coalesce(@at, transaction_timestamp()))";

    private static Task EntryAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string app, string? ent = null, int generation = 0, DateTimeOffset? at = null)
    {
        var id = $"deal.create:{app}:{Guid.NewGuid():D}";
        return ExecAsync(conn, tx, InsertEntry, ("id", id), ("app", app), ("ent", ent ?? "e-" + app), ("g", generation),
            ("at", at is { } t ? t : DBNull.Value));
    }

    private static async Task EntriesAsync(string cs, string app, int count, string? ent = null, DateTimeOffset? at = null)
    {
        await using var conn = await OpenAsync(cs);
        for (var i = 0; i < count; i++) await EntryAsync(conn, null, app, ent, at: at);
    }

    /// <summary>المَجاميعُ صَفّاً صَفّاً.</summary>
    private static Task<string> AggAsync(string cs)
        => TextAsync(cs, "select coalesce(string_agg(concat_ws('|', day, app, entrepreneur, role, feature, provider, unit, entries, quantity), E'\\n' "
                         + "order by day, app, entrepreneur, role, feature, provider, unit), '') from meter.agg_daily");

    /// <summary>ما يُعطيه تَجميعُ الدَفتَرِ كُلِّه (يَومُ الرِياض) — المَرجِعُ الَّذي تُقارَنُ بِه المَجاميع.</summary>
    private static Task<string> LedgerTotalsAsync(string cs)
        => TextAsync(cs, "select coalesce(string_agg(concat_ws('|', day, app, entrepreneur, role, feature, provider, unit, entries, quantity), E'\\n' "
                         + "order by day, app, entrepreneur, role, feature, provider, unit), '') from ("
                         + "select (occurred_at at time zone 'Asia/Riyadh')::date as day, app, entrepreneur, role, feature, provider, unit, "
                         + "count(*) as entries, sum(quantity) as quantity from meter.usage_entry group by 1, 2, 3, 4, 5, 6, 7) t");

    private static Task<string> CursorAsync(string cs)
        => TextAsync(cs, "select coalesce((select concat_ws('|', after_gen, after_xid, after_id) from meter.agg_cursor), '(none)')");

    // ─── تَشغيلُ المُجَمِّع ─────────────────────────────────────────────────────────────────────────────────────

    private sealed class Run
    {
        public required Process Process { get; init; }
        public List<string> Lines { get; } = [];
        public string[] Snapshot() { lock (Lines) return Lines.ToArray(); }
    }

    private static Run Start(string cs, params string[] args)
    {
        var dll = MeterDll();
        Assert.True(File.Exists(dll), $"المُجَمِّعُ غَيرُ مَبنِيّ: {dll} — يُبنى الحَلُّ قَبلَ الخُطوَةِ الحَيَّة (ci.yml).");
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ThemeZeroEquivalenceTests.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["ConnectionStrings__Postgres"] = cs;
        var run = new Run { Process = Process.Start(psi)! };
        run.Process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (run.Lines) run.Lines.Add(e.Data); };
        run.Process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (run.Lines) run.Lines.Add("! " + e.Data); };
        run.Process.BeginOutputReadLine();
        run.Process.BeginErrorReadLine();
        return run;
    }

    private async Task<(int Code, string[] Lines)> FinishAsync(Run run, string label)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await run.Process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            run.Process.Kill(entireProcessTree: true);
            Assert.Fail($"{label}: المُجَمِّعُ لَم يَخرُج في 180 ثانِيَة:\n{string.Join("\n", run.Snapshot())}");
        }
        run.Process.WaitForExit();
        var code = run.Process.ExitCode;
        run.Process.Dispose();
        var lines = run.Snapshot();
        foreach (var l in lines) output.WriteLine($"[{label}] {l}");
        output.WriteLine($"[{label}] exit {code}");
        return (code, lines);
    }

    private Task<(int Code, string[] Lines)> RunAsync(string cs, string label, params string[] args) => FinishAsync(Start(cs, args), label);

    private static readonly Regex PullLine = new(
        @"^\[meter-agg\] gen (?<gen>\d+) · pulled (?<ops>\d+)(?: \((?<units>[^)]*)\))? · presence (?<presence>\d+)(?: \([^)]*\))? · pages (?<pages>\d+) · horizon (?<h>\S+) · lag (?<lag>\d+)s$",
        RegexOptions.CultureInvariant);

    /// <summary>سَطرُ السَحبِ الواحِدُ مَقروءاً: الجيلُ، وما سُحِبَ لِكُلِّ وَحدَةٍ (نَصّاً مُرَتَّباً «deal.create 8»، وفارِغاً لِلصِفر)، والصَفَحات.</summary>
    private static (int Generation, string Units, int Pages) Pulled(string[] lines)
    {
        var m = PullLine.Match(Assert.Single(lines, l => PullLine.IsMatch(l)));
        var units = new SortedDictionary<string, long>(StringComparer.Ordinal);
        if (m.Groups["units"].Success)
            foreach (var part in m.Groups["units"].Value.Split(" · "))
            {
                var at = part.LastIndexOf(' ');
                units[part[..at]] = long.Parse(part[(at + 1)..], CultureInfo.InvariantCulture);
            }
        Assert.Equal(long.Parse(m.Groups["ops"].Value, CultureInfo.InvariantCulture), units.Values.Sum());
        return (int.Parse(m.Groups["gen"].Value, CultureInfo.InvariantCulture), string.Join(" · ", units.Select(u => $"{u.Key} {u.Value}")),
                int.Parse(m.Groups["pages"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>ما يُنتَظَرُ لِوَحدَةِ الهَيكَلِ الماشي: «deal.create n»، وفارِغٌ لِلصِفر.</summary>
    private static string Deals(long n) => n == 0 ? "" : $"deal.create {n}";

    // ─── (١) التَداخُل: مُؤَشِّرُ التَسَلسُلِ يُضَيِّعُ المُعامَلَةَ الأَقدَم، ومُؤَشِّرُ المُعامَلَةِ يَقرَؤُها (D5) ─────────────────────────────

    [Fact]
    public async Task An_older_transaction_committing_last_is_lost_by_a_sequence_cursor_and_read_by_the_xid_keyset()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);

        // ‏الضابِطُ السالِب (D5 بِحَرفِه): ج يَأخُذُ مُعَرِّفَ مُعامَلَتِه أَوَّلاً، وأ يَسحَبُ التَسَلسُلَ قَبلَه ويُودَعُ أَخيراً.
        await ExecAsync(l.Cs, "create schema probe; create table probe.seq_cursor (seq bigserial primary key, op text not null, "
                              + "writer_xid xid8 not null default pg_current_xact_id())");
        const string seqPull = "select coalesce(string_agg(op, ',' order by seq), '(none)') from probe.seq_cursor where seq > @c and writer_xid < pg_snapshot_xmin(pg_current_snapshot())";
        const string seqMax = "select coalesce(max(seq), @c) from probe.seq_cursor where seq > @c and writer_xid < pg_snapshot_xmin(pg_current_snapshot())";
        string seq1, seq2;
        await using (var c = await OpenAsync(l.Cs))
        await using (var a = await OpenAsync(l.Cs))
        {
            await using var tc = await c.BeginTransactionAsync();
            await TextAsync(c, tc, "select pg_current_xact_id()::text");
            await using var ta = await a.BeginTransactionAsync();
            await ExecAsync(a, ta, "insert into probe.seq_cursor (op) values ('A')");
            await ExecAsync(c, tc, "insert into probe.seq_cursor (op) values ('C')");
            await tc.CommitAsync();
            seq1 = await TextAsync(l.Cs, seqPull, ("c", 0L));
            var cursor = long.Parse(await TextAsync(l.Cs, seqMax, ("c", 0L)), CultureInfo.InvariantCulture);
            await ta.CommitAsync();
            seq2 = await TextAsync(l.Cs, seqPull, ("c", cursor));
        }
        output.WriteLine($"[G8·D5] مُؤَشِّرُ التَسَلسُل: السَحبُ 1 ← {seq1} · السَحبُ 2 ← {seq2} — أ لا يُقرَأُ أَبَداً");
        Assert.Equal(("C", "(none)"), (seq1, seq2));

        // ‏التَداخُلُ نَفسُه عَلى الدَفتَر، والمُجَمِّعُ المَبنِيّ.
        (int Code, string[] Lines) first, second;
        await using (var c = await OpenAsync(l.Cs))
        await using (var a = await OpenAsync(l.Cs))
        {
            await using var tc = await c.BeginTransactionAsync();
            await TextAsync(c, tc, "select pg_current_xact_id()::text");
            await using var ta = await a.BeginTransactionAsync();
            await EntryAsync(a, ta, "w13-a");
            await EntryAsync(c, tc, "w13-c");
            await tc.CommitAsync();
            first = await RunAsync(l.Cs, "G8·xid 1", "--once");
            await ta.CommitAsync();
        }
        second = await RunAsync(l.Cs, "G8·xid 2", "--once");

        Assert.Equal(0, first.Code);
        Assert.Equal(0, second.Code);
        Assert.Equal(Deals(1), Pulled(first.Lines).Units);
        Assert.Equal(Deals(1), Pulled(second.Lines).Units);
        var agg = await AggAsync(l.Cs);
        Assert.Equal(await LedgerTotalsAsync(l.Cs), agg);
        Assert.Contains("|w13-a|", agg, StringComparison.Ordinal);
        Assert.Contains("|w13-c|", agg, StringComparison.Ordinal);
        output.WriteLine($"[G8·D5] مُؤَشِّرُ المُعامَلَة: التَشغيلُ 1 ← deal.create 1 (ج) · التَشغيلُ 2 ← deal.create 1 (أ) · المَجاميع = الدَفتَر:\n{agg}");
    }

    // ─── (٢) انهِيارٌ بَينَ المَجاميعِ والمُؤَشِّر ⇐ لا شَيءَ أُودِع، والإعادَةُ تُساوي ───────────────────────────────────────

    [Fact]
    public async Task A_crash_between_the_totals_and_the_cursor_leaves_nothing_and_the_rerun_is_equal()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);
        await EntriesAsync(l.Cs, "w13-a", 3);
        var warm = await RunAsync(l.Cs, "G8·crash 0", "--once");
        Assert.Equal((0, Deals(3)), (warm.Code, Pulled(warm.Lines).Units));
        var aggBefore = await AggAsync(l.Cs);
        var cursorBefore = await CursorAsync(l.Cs);
        await EntriesAsync(l.Cs, "w13-a", 2);

        // ‏صَفُّ المُؤَشِّرِ مَقفولٌ بِمُعامَلَةِ الاختِبار: صَفحَةُ المُجَمِّعِ تَكتُبُ مَجاميعَها ثُمَّ تَقِفُ عِندَ المُؤَشِّر — فَيُقتَلُ هُناك.
        await using var holder = await OpenAsync(l.Cs);
        await using var hold = await holder.BeginTransactionAsync();
        await ExecAsync(holder, hold, "select 1 from meter.agg_cursor for update");
        var run = Start(l.Cs, "--once");
        int pid = 0;
        string locks = "";
        for (var i = 0; i < 600 && pid == 0; i++)
        {
            var found = await TextAsync(l.Cs,
                "select coalesce((select pid from pg_stat_activity where datname = current_database() and application_name = @app "
                + "and wait_event_type = 'Lock' and query like '%agg_cursor%' limit 1), 0)", ("app", MeterSql.AggregatorApplicationName));
            pid = int.Parse(found, CultureInfo.InvariantCulture);
            if (pid == 0) await Task.Delay(100);
        }
        Assert.True(pid != 0, "صَفحَةُ المُجَمِّعِ لَم تَقِف عِندَ المُؤَشِّر:\n" + string.Join("\n", run.Snapshot()));
        locks = await TextAsync(l.Cs,
            "select string_agg(c.relname || ':' || l.mode, ',' order by c.relname, l.mode) from pg_locks l join pg_class c on c.oid = l.relation "
            + "where l.pid = @p and l.granted and c.relname in ('agg_daily', 'agg_cursor')", ("p", pid));
        output.WriteLine($"[G8·crash] الخَلفِيَّة {pid} تَنتَظِرُ صَفَّ المُؤَشِّر وتُمسِك: {locks}");
        Assert.Contains("agg_daily:RowExclusiveLock", locks, StringComparison.Ordinal);

        run.Process.Kill(entireProcessTree: true);
        await TextAsync(l.Cs, "select pg_terminate_backend(@p)", ("p", pid));
        await run.Process.WaitForExitAsync();
        output.WriteLine($"[G8·crash] قُتِلَ المُجَمِّعُ وخَلفِيَّتُه (خُروج {run.Process.ExitCode}):\n{string.Join("\n", run.Snapshot())}");
        run.Process.Dispose();
        await hold.RollbackAsync();
        for (var i = 0; i < 100; i++)
        {
            if (await TextAsync(l.Cs, "select count(*) from pg_stat_activity where datname = current_database() and application_name = @app",
                    ("app", MeterSql.AggregatorApplicationName)) == "0") break;
            await Task.Delay(100);
        }

        Assert.Equal(aggBefore, await AggAsync(l.Cs));
        Assert.Equal(cursorBefore, await CursorAsync(l.Cs));
        var rerun = await RunAsync(l.Cs, "G8·crash 1", "--once");
        Assert.Equal((0, Deals(2)), (rerun.Code, Pulled(rerun.Lines).Units));
        var agg = await AggAsync(l.Cs);
        Assert.Equal(await LedgerTotalsAsync(l.Cs), agg);
        output.WriteLine($"[G8·crash] المَجاميعُ قَبلَ الانهِيار وبَعدَه واحِدَة ({aggBefore}) · الإعادَةُ ← deal.create 2 · المَجاميع = الدَفتَر: {agg}");
    }

    // ─── (٣) اللَحاقُ بَعدَ التَوَقُّف، وإعادَةُ البِناءِ تُساوي التَدريجِيّ، واليَومُ يَومُ الرِياض ─────────────────────────────────

    [Fact]
    public async Task It_catches_up_after_a_stop_and_a_rebuild_equals_the_incremental_totals()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);
        await EntriesAsync(l.Cs, "w13-a", 5);
        await EntriesAsync(l.Cs, "w13-b", 2);
        // ‏22:30 بِتَوقيتِ غرينتش = 01:30 في الرِياضِ مِن اليَومِ التالي — فَالسَطرُ في يَومِ 11 لا 10.
        await EntriesAsync(l.Cs, "w13-b", 1, at: new DateTimeOffset(2026, 11, 10, 22, 30, 0, TimeSpan.Zero));

        var first = await RunAsync(l.Cs, "G8·catch 1", "--once", "--page", "2");
        Assert.Equal(0, first.Code);
        var p1 = Pulled(first.Lines);
        Assert.Equal((Deals(8), 4), (p1.Units, p1.Pages));
        Assert.Equal("1", await TextAsync(l.Cs, "select coalesce(sum(entries), 0) from meter.agg_daily where app = 'w13-b' and day = date '2026-11-11'"));
        Assert.Equal("0", await TextAsync(l.Cs, "select count(*) from meter.agg_daily where day = date '2026-11-10'"));

        // ‏المُجَمِّعُ مُتَوَقِّفٌ والسُطورُ تَتَراكَم — ثُمَّ يَلحَق.
        await EntriesAsync(l.Cs, "w13-a", 2);
        await EntriesAsync(l.Cs, "w13-b", 2);
        var second = await RunAsync(l.Cs, "G8·catch 2", "--once", "--page", "3");
        Assert.Equal(0, second.Code);
        var p2 = Pulled(second.Lines);
        Assert.Equal((Deals(4), 2), (p2.Units, p2.Pages));
        var incremental = await AggAsync(l.Cs);
        Assert.Equal(await LedgerTotalsAsync(l.Cs), incremental);

        var rebuild = await RunAsync(l.Cs, "G8·rebuild", "--once", "--rebuild", "--page", "5");
        Assert.Equal(0, rebuild.Code);
        Assert.Contains(rebuild.Lines, x => x.StartsWith("[meter-agg] rebuild — agg_daily emptied and the cursor reset", StringComparison.Ordinal));
        var p3 = Pulled(rebuild.Lines);
        Assert.Equal((Deals(12), 3), (p3.Units, p3.Pages));
        var rebuilt = await AggAsync(l.Cs);
        output.WriteLine($"[G8·rebuild] التَدريجِيّ ({p1.Pages}+{p2.Pages} صَفَحات) = إعادَةُ البِناء ({p3.Pages} صَفَحات) = الدَفتَر:\n{rebuilt}");
        Assert.Equal(incremental, rebuilt);
    }

    // ─── (٤) مُجَمِّعانِ مَعاً = مُجَمِّعٌ واحِد، والقُفلُ المُمسَكُ ⇐ الخُروج 3 ───────────────────────────────────────────────

    [Fact]
    public async Task Two_concurrent_runners_total_what_one_runner_totals_and_a_held_lock_exits_3()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);
        await EntriesAsync(l.Cs, "w13-a", 12);
        await EntriesAsync(l.Cs, "w13-b", 8);

        // ‏جَدوَلُ المَجاميعِ مَقفولٌ بِمُعامَلَةِ الاختِبار فَيَقِفُ صاحِبُ القُفلِ عِندَ أَوَّلِ صَفحَة، ويَنتَظِرُ الثاني القُفل — تَداخُلٌ مَضمونٌ لا مُصادَفَة.
        await using var holder = await OpenAsync(l.Cs);
        await using var hold = await holder.BeginTransactionAsync();
        await ExecAsync(holder, hold, "lock table meter.agg_daily in share mode");
        var one = Start(l.Cs, "--once", "--page", "3");
        var two = Start(l.Cs, "--once", "--page", "3");
        var overlapped = false;
        for (var i = 0; i < 600 && !overlapped; i++)
        {
            var blocked = await TextAsync(l.Cs, "select count(*) from pg_stat_activity where datname = current_database() and application_name = @app "
                                              + "and wait_event_type = 'Lock'", ("app", MeterSql.AggregatorApplicationName));
            var waiting = one.Snapshot().Concat(two.Snapshot()).Any(x => x.Contains("lock held by pid", StringComparison.Ordinal));
            overlapped = blocked == "1" && waiting;
            if (!overlapped) await Task.Delay(100);
        }
        Assert.True(overlapped, "لَم يَتَداخَلِ المُجَمِّعان:\n" + string.Join("\n", one.Snapshot().Concat(two.Snapshot())));
        await hold.RollbackAsync();
        var r1 = await FinishAsync(one, "G8·two A");
        var r2 = await FinishAsync(two, "G8·two B");

        Assert.Equal((0, 0), (r1.Code, r2.Code));
        var pulled = new[] { Pulled(r1.Lines).Units, Pulled(r2.Lines).Units }.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { Deals(0), Deals(20) }, pulled);
        Assert.Single(r1.Lines.Concat(r2.Lines), x => Regex.IsMatch(x, @"^\[meter-agg\] lock held by pid \d+ — waiting$"));
        var agg = await AggAsync(l.Cs);
        Assert.Equal(await LedgerTotalsAsync(l.Cs), agg);
        output.WriteLine($"[G8·two] المُجَمِّعان: «{pulled[1]}» و«{pulled[0]}» — مَجموعُهُما ما يُعطيه واحِد، والمَجاميع = الدَفتَر:\n{agg}");

        // ‏القُفلُ مُمسَكٌ مِن خارِجٍ أَطوَلَ مِن المُهلَة ⇐ الخُروج 3 مُسَمِّياً مُمسِكَه.
        await using var lockHolder = await OpenAsync(l.Cs);
        await ExecAsync(lockHolder, null, $"select pg_advisory_lock(hashtext('{MeterSql.AggregatorLockName}'))");
        var holderPid = await TextAsync(lockHolder, null, "select pg_backend_pid()");
        var held = await RunAsync(l.Cs, "G8·lock", "--once", "--wait", "2");
        await ExecAsync(lockHolder, null, $"select pg_advisory_unlock(hashtext('{MeterSql.AggregatorLockName}'))");
        Assert.Equal(3, held.Code);
        Assert.Contains($"[meter-agg] lock held by pid {holderPid} for more than 2s — another aggregator runs on this database", held.Lines);
        Assert.DoesNotContain(held.Lines, x => PullLine.IsMatch(x));
    }

    // ─── (٥) استِعادَةٌ مَحقونَة ⇐ الخُروج 4 بِلا سَحب، ثُمَّ الإقلاعُ يَفتَحُ جيلاً والسَحبُ يَقرَأُ السَطرَ الجَديد (R4) ───────────────────

    [Fact]
    public async Task An_injected_restore_refuses_with_exit_4_until_boot_opens_a_generation_then_the_new_row_is_read()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);
        // ‏ما تَترُكُه استِعادَةٌ في عُنقودٍ جَديد: سَطرٌ ومُؤَشِّرٌ بِمُعَرِّفِ مُعامَلَةٍ فَوقَ العُنقودِ بِتِسعِمِئَةِ مَليون (R3/R4).
        var far = (ulong.Parse(await TextAsync(l.Cs, "select pg_current_xact_id()::text"), CultureInfo.InvariantCulture) + 900_000_000UL)
            .ToString(CultureInfo.InvariantCulture);
        const string old = "deal.create:w13-r:old";
        await ExecAsync(l.Cs, "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation, writer_xid) "
                              + "values (@id, 'deal.create', 1, 'w13-r', 'e-r', '_none', 'deal_status', '_none', 'deal', @id, 1, @x::xid8)", ("id", old), ("x", far));
        await ExecAsync(l.Cs, "insert into meter.agg_cursor (singleton, after_gen, after_xid, after_id) values (true, 1, @x::xid8, @id)", ("id", old), ("x", far));
        var cursorBefore = await CursorAsync(l.Cs);

        var refused = await RunAsync(l.Cs, "G8·R4 refuse", "--once");
        Assert.Equal(4, refused.Code);
        Assert.Contains(refused.Lines, x => x.StartsWith("[meter-agg] xid regression — boot the app (it opens a new generation) · gen 1", StringComparison.Ordinal));
        Assert.DoesNotContain(refused.Lines, x => x.Contains("pulled", StringComparison.Ordinal));
        Assert.Equal(("0", cursorBefore), (await TextAsync(l.Cs, "select count(*) from meter.agg_daily"), await CursorAsync(l.Cs)));

        var boot = await MeterBoot.EnsureAsync(l.Store!);
        output.WriteLine($"[G8·R4] الإقلاعُ بَعدَ الاستِعادَة: {boot.Line} · فَتَحَ جيلاً {boot.OpenedGeneration}");
        Assert.Equal((2, true), (boot.Generation, boot.OpenedGeneration));
        await using (var conn = await OpenAsync(l.Cs)) await EntryAsync(conn, null, "w13-r", "e-r");

        // ‏الضابِطُ السالِب (R3): مُؤَشِّرُ مُعَرِّفِ المُعامَلَةِ وَحدَه لا يَرى السَطرَ الجَديد.
        var xidOnly = await TextAsync(l.Cs, "select coalesce(string_agg(e.id, ','), '(no rows)') from meter.usage_entry e, meter.agg_cursor c "
                                            + "where (e.writer_xid, e.id) > (c.after_xid, c.after_id) and e.writer_xid < pg_snapshot_xmin(pg_current_snapshot())");
        output.WriteLine($"[G8·R4] مُؤَشِّرُ مُعَرِّفِ المُعامَلَةِ وَحدَه ← {xidOnly}");
        Assert.Equal("(no rows)", xidOnly);

        var read = await RunAsync(l.Cs, "G8·R4 read", "--once");
        Assert.Equal(0, read.Code);
        var p = Pulled(read.Lines);
        Assert.Equal((2, Deals(1)), (p.Generation, p.Units));
        Assert.StartsWith("2|", await CursorAsync(l.Cs), StringComparison.Ordinal);
        Assert.Equal("1", await TextAsync(l.Cs, "select coalesce(sum(entries), 0) from meter.agg_daily where app = 'w13-r'"));
        output.WriteLine($"[G8·R4] مُؤَشِّرُ الجيل ← deal.create 1 في الجيل 2 · المُؤَشِّر {await CursorAsync(l.Cs)}");
    }

    // ─── (٦) أُفُقٌ مُمسَكٌ مِن قاعِدَةٍ أُخرى عَلى العُنقود ⇐ --once يَخرُجُ 2 مُسَمِّياً مُمسِكَه (R2) ─────────────────────────────────

    [Fact]
    public async Task A_horizon_held_from_another_database_makes_once_exit_2_naming_the_blocker()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output);
        var baseDb = new NpgsqlConnectionStringBuilder(BaseCs).Database!;

        // ‏مُعامَلَةٌ مَفتوحَةٌ في قاعِدَةِ البيئَة — لا في قاعِدَةِ الدَفتَر — بِمُعَرِّفٍ أَقدَمَ مِن السَطر.
        await using var blocker = await OpenAsync(BaseCs);
        await using var blocking = await blocker.BeginTransactionAsync();
        var blockerXid = await TextAsync(blocker, blocking, "select pg_current_xact_id()::xid::text");
        var blockerPid = await TextAsync(blocker, blocking, "select pg_backend_pid()");
        await EntriesAsync(l.Cs, "w13-h", 1);

        var held = await RunAsync(l.Cs, "G8·horizon", "--once", "--wait", "3");
        Assert.Equal(2, held.Code);
        Assert.Equal(Deals(0), Pulled(held.Lines).Units);
        var expected = $"blocker pid {blockerPid} · xid {blockerXid} · datname {baseDb} · state idle in transaction";
        Assert.Contains(held.Lines, x => x.StartsWith("[meter-agg] horizon held for more than 3s — rows committed before the start wait below it · ", StringComparison.Ordinal)
                                         && x.Contains(expected, StringComparison.Ordinal));
        Assert.Equal("0", await TextAsync(l.Cs, "select count(*) from meter.agg_daily"));

        await blocking.RollbackAsync();
        var freed = await RunAsync(l.Cs, "G8·horizon freed", "--once");
        Assert.Equal((0, Deals(1)), (freed.Code, Pulled(freed.Lines).Units));
        output.WriteLine($"[G8·R2] المُمسِكُ في {baseDb} (pid {blockerPid} · xid {blockerXid}) ⇐ الخُروج 2؛ وبَعدَ تَراجُعِه ⇐ deal.create 1");
    }

    // ─── (٧) لا دَفتَر ⇐ لا شَيءَ يُسحَب، ولا يُنشِئُ شَيئاً ────────────────────────────────────────────────────────────

    [Fact]
    public async Task On_a_database_without_the_ledger_it_pulls_nothing_creates_nothing_and_exits_0()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: false);
        var run = await RunAsync(l.Cs, "G8·absent", "--once");
        Assert.Equal(0, run.Code);
        Assert.Contains(run.Lines, x => x.StartsWith("[meter-agg] ledger absent — nothing to pull (meter.agg_cursor, meter.agg_daily, meter.generation, meter.usage_entry absent", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Lines, x => PullLine.IsMatch(x));
        Assert.Equal("0", await TextAsync(l.Cs, "select count(*) from pg_namespace where nspname = 'meter'"));
    }
}
