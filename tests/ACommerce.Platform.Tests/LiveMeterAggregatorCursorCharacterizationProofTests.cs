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

// ═══ تَوصيفٌ قَبلَ التَبديل — وِلادَةُ المُؤَشِّرِ وتَقَدُّمُه وإعادَةُ البِناء (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ الجَولَةُ الثانِيَة · M6A-CNT-R2-1؛ القاعِدَة ٣) ═══════
//
// **ما لا يَتَغَيَّرُ حينَ يَنقَسِمُ إيداعُ المُؤَشِّرِ ويَصيرُ التَفريغُ حَذفاً**: التَبديلُ التالي يَجعَلُ `AggCursorSet` جُملَتَين — وِلادَةً لِصَفحَةٍ لَم تَقرَأ مُؤَشِّراً
// (`insert … on conflict (singleton) do nothing`) وتَقَدُّماً لِما سِواها (`update … where (المُؤَشِّر) = (ما قَرَأَته)`)، وكِلاهُما صَفٌّ واحِدٌ أَو تَتَراجَعُ الصَفحَة —
// ويَجعَلُ `AggRebuild` حَذفاً لا `truncate`. وما يَمَسُّه ذلك اليَومَ بِلا صَفٍّ يُثَبِّتُه بِعَينِه: المُؤَشِّرُ يولَدُ عِندَ آخِرِ مِفتاحٍ بِتَرتيبِ (generation, writer_xid, id)؛
// والتَشغيلُ الخالي لا يَكتُبُ صَفَّ المُؤَشِّرِ أَصلاً (‏`xmin` و`pulled_at` بِحالِهِما)؛ والتالي يَتَقَدَّمُ مِنه إلى آخِرِ مِفتاحٍ جَديد؛ و`--rebuild` يَطبَعُ سَطرَه ويَعودُ
// إلى المَجاميعِ والمُؤَشِّرِ التَدريجِيَّينِ بِأَعيانِهِما؛ وعَلى دَفتَرٍ بِلا سُطورٍ لا يَترُكُ مَجموعاً ولا مُؤَشِّراً. **ولا يُعَدَّلُ حَرفٌ فيه بَعدَ إيداعِه.**
//
// **الثُنائِيُّ المَبنِيُّ لا نُسخَةٌ مِنه** (القاعِدَة ١٤، كَـ`LiveMeterAggregatorProofTests`): `apps/V1.Meter/bin/<التَهيِئَة>/net10.0/V1.Meter.dll`؛ وقاعِدَةٌ
// جَديدَةٌ لِكُلِّ دَفتَر (`{قاعِدَةُ البيئَة}_mc_{8}`) يُنشِئُه `MeterBoot` الحَقيقِيّ وتُحذَفُ في `DisposeAsync`.

[Collection(LiveProofCollection.Name)]
public class LiveMeterAggregatorCursorCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private static readonly Regex PullLine = new(
        @"^\[meter-agg\] gen (?<gen>\d+) · pulled (?<ops>\d+)(?: \((?<units>[^)]*)\))? · presence (?<presence>\d+)(?: \([^)]*\))? · pages (?<pages>\d+) · horizon (?<h>\S+) · lag (?<lag>\d+)s$",
        RegexOptions.CultureInvariant);

    private const string RebuildLine = "[meter-agg] rebuild — agg_daily emptied and the cursor reset; pulling the ledger from its start";

    private sealed class Db : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Cs { get; init; }
        public required DocumentStore Store { get; init; }

        public static async Task<Db> CreateAsync(ITestOutputHelper output)
        {
            var b = new NpgsqlConnectionStringBuilder(BaseCs);
            var stem = (b.Database ?? "wasayel").ToLowerInvariant();
            if (stem.Length > 48) stem = stem[..48];
            b.Database = $"{stem}_mc_{Guid.NewGuid().ToString("N")[..8]}";
            await TextAsync(BaseCs, $"create database \"{b.Database}\"");
            var store = DocumentStore.For(o =>
            {
                o.Connection(b.ConnectionString);
                o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
            });
            var boot = await MeterBoot.EnsureAsync(store);
            output.WriteLine($"[cursor] {b.Database}: {boot.Line}");
            return new Db { Name = b.Database, Cs = b.ConnectionString, Store = store };
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            NpgsqlConnection.ClearAllPools();
            await TextAsync(BaseCs, $"drop database if exists \"{Name}\" with (force)");
        }
    }

    private static async Task<string> TextAsync(string cs, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }

    private static async Task EntriesAsync(string cs, string app, int count)
    {
        for (var i = 0; i < count; i++)
            await TextAsync(cs,
                "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation) "
                + "values (@id, 'deal.create', 1, @app, 'e-' || @app, '_none', 'deal_status', '_none', 'deal', @id, "
                + "coalesce((select max(g.generation) from meter.generation g), 1))",
                ("id", $"deal.create:{app}:{Guid.NewGuid():D}"), ("app", app));
    }

    /// <summary>المَجاميعُ صَفّاً صَفّاً.</summary>
    private static Task<string> AggAsync(string cs)
        => TextAsync(cs, "select coalesce(string_agg(concat_ws('|', day, app, entrepreneur, role, feature, provider, unit, entries, quantity), E'\\n' "
                         + "order by day, app, entrepreneur, role, feature, provider, unit), '') from meter.agg_daily");

    /// <summary>تَجميعُ الدَفتَرِ كُلِّه بِيَومِ الرِياض — المَرجِع.</summary>
    private static Task<string> LedgerTotalsAsync(string cs)
        => TextAsync(cs, "select coalesce(string_agg(concat_ws('|', day, app, entrepreneur, role, feature, provider, unit, entries, quantity), E'\\n' "
                         + "order by day, app, entrepreneur, role, feature, provider, unit), '') from ("
                         + "select (occurred_at at time zone 'Asia/Riyadh')::date as day, app, entrepreneur, role, feature, provider, unit, "
                         + "count(*) as entries, sum(quantity) as quantity from meter.usage_entry group by 1, 2, 3, 4, 5, 6, 7) t");

    /// <summary>صَفُّ المُؤَشِّرِ كَما هُوَ: المِفتاح، و<c>pulled_at</c>، و<c>xmin</c> صَفِّه (يَتَغَيَّرُ بِكُلِّ كِتابَة) — أَو <c>(none)</c>.</summary>
    private static Task<string> CursorRowAsync(string cs)
        => TextAsync(cs, "select coalesce((select concat_ws('|', after_gen, after_xid, after_id, pulled_at, xmin) from meter.agg_cursor), '(none)')");

    private static Task<string> CursorAsync(string cs)
        => TextAsync(cs, "select coalesce((select concat_ws('|', after_gen, after_xid, after_id) from meter.agg_cursor), '(none)')");

    /// <summary>آخِرُ مِفتاحٍ في الدَفتَرِ بِتَرتيبِ المُؤَشِّر.</summary>
    private static Task<string> LastKeyAsync(string cs)
        => TextAsync(cs, "select coalesce((select concat_ws('|', generation, writer_xid, id) from meter.usage_entry "
                         + "order by generation desc, writer_xid desc, id desc limit 1), '(none)')");

    private async Task<(int Code, string[] Lines)> RunAsync(string cs, string label, params string[] args)
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var dll = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.Meter", "bin", tfm.Parent!.Name, tfm.Name, "V1.Meter.dll");
        Assert.True(File.Exists(dll), $"المُجَمِّعُ غَيرُ مَبنِيّ: {dll}");
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
        var lines = new List<string>();
        using var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add("! " + e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"{label}: المُجَمِّعُ لَم يَخرُج في 180 ثانِيَة:\n{string.Join("\n", lines)}");
        }
        process.WaitForExit();
        string[] all;
        lock (lines) all = lines.ToArray();
        foreach (var l in all) output.WriteLine($"[{label}] {l}");
        output.WriteLine($"[{label}] exit {process.ExitCode}");
        return (process.ExitCode, all);
    }

    /// <summary>سَطرُ السَحبِ الواحِد: ما سُحِبَ لِكُلِّ وَحدَة («deal.create 5»، وفارِغٌ لِلصِفر) والصَفَحات.</summary>
    private static (string Units, int Pages) Pulled(string[] lines)
    {
        var m = PullLine.Match(Assert.Single(lines, l => PullLine.IsMatch(l)));
        return (m.Groups["units"].Success ? m.Groups["units"].Value : "", int.Parse(m.Groups["pages"].Value, CultureInfo.InvariantCulture));
    }

    // ─── (١) الوِلادَةُ عِندَ آخِرِ مِفتاح، والتَشغيلُ الخالي لا يَكتُب، والتالي يَتَقَدَّم ─────────────────────────────────────────────

    [Fact]
    public async Task The_first_run_births_the_cursor_at_the_last_key_an_idle_run_writes_no_cursor_row_and_the_next_run_advances_it()
    {
        if (!Enabled) return;
        await using var db = await Db.CreateAsync(output);
        Assert.Equal("(none)", await CursorRowAsync(db.Cs));
        await EntriesAsync(db.Cs, "w13-c", 5);

        var born = await RunAsync(db.Cs, "cursor born", "--once", "--page", "2");
        Assert.Equal(0, born.Code);
        Assert.Equal(("deal.create 5", 3), Pulled(born.Lines));
        var lastBorn = await LastKeyAsync(db.Cs);
        Assert.Equal(lastBorn, await CursorAsync(db.Cs));
        Assert.Equal(await LedgerTotalsAsync(db.Cs), await AggAsync(db.Cs));
        var rowBorn = await CursorRowAsync(db.Cs);

        var idle = await RunAsync(db.Cs, "cursor idle", "--once");
        Assert.Equal(0, idle.Code);
        Assert.Equal(("", 0), Pulled(idle.Lines));
        var rowIdle = await CursorRowAsync(db.Cs);
        Assert.Equal(rowBorn, rowIdle);

        await EntriesAsync(db.Cs, "w13-c", 3);
        var next = await RunAsync(db.Cs, "cursor advanced", "--once");
        Assert.Equal(0, next.Code);
        Assert.Equal(("deal.create 3", 1), Pulled(next.Lines));
        var lastNext = await LastKeyAsync(db.Cs);
        Assert.NotEqual(lastBorn, lastNext);
        Assert.Equal(lastNext, await CursorAsync(db.Cs));
        Assert.Equal(await LedgerTotalsAsync(db.Cs), await AggAsync(db.Cs));
        var advancedAt = await TextAsync(db.Cs, "select pulled_at from meter.agg_cursor");
        Assert.Equal("t", await TextAsync(db.Cs, "select case when pulled_at > @b::timestamptz then 't' else 'f' end from meter.agg_cursor",
            ("b", rowBorn.Split('|')[3])));
        output.WriteLine($"[cursor] وُلِدَ عِندَ {lastBorn} · الخالي لا يَكتُب ({rowIdle == rowBorn}) · تَقَدَّمَ إلى {lastNext} (pulled_at {advancedAt}) · المَجاميع = الدَفتَر");
    }

    // ─── (٢) إعادَةُ البِناءِ تَعودُ إلى التَدريجِيِّ بِعَينِه، وعَلى دَفتَرٍ بِلا سُطورٍ لا تَترُكُ شَيئاً ─────────────────────────────────────

    [Fact]
    public async Task A_rebuild_returns_the_incremental_totals_and_cursor_exactly_and_on_an_empty_ledger_leaves_no_total_and_no_cursor()
    {
        if (!Enabled) return;
        await using (var db = await Db.CreateAsync(output))
        {
            await EntriesAsync(db.Cs, "w13-r", 4);
            await EntriesAsync(db.Cs, "w13-s", 2);
            var inc = await RunAsync(db.Cs, "rebuild inc", "--once");
            Assert.Equal((0, ("deal.create 6", 1)), (inc.Code, Pulled(inc.Lines)));
            var aggInc = await AggAsync(db.Cs);
            var cursorInc = await CursorAsync(db.Cs);
            Assert.Equal(await LedgerTotalsAsync(db.Cs), aggInc);
            Assert.Equal(await LastKeyAsync(db.Cs), cursorInc);

            var rebuilt = await RunAsync(db.Cs, "rebuild", "--once", "--rebuild", "--page", "4");
            Assert.Equal(0, rebuilt.Code);
            Assert.Contains(RebuildLine, rebuilt.Lines);
            Assert.Equal(("deal.create 6", 2), Pulled(rebuilt.Lines));
            Assert.Equal(aggInc, await AggAsync(db.Cs));
            Assert.Equal(cursorInc, await CursorAsync(db.Cs));
            output.WriteLine($"[cursor] إعادَةُ البِناءِ = التَدريجِيّ: المُؤَشِّر {cursorInc} · المَجاميع:\n{aggInc}");
        }

        await using (var empty = await Db.CreateAsync(output))
        {
            var rebuilt = await RunAsync(empty.Cs, "rebuild empty", "--once", "--rebuild");
            Assert.Equal(0, rebuilt.Code);
            Assert.Contains(RebuildLine, rebuilt.Lines);
            Assert.Equal(("", 0), Pulled(rebuilt.Lines));
            Assert.Equal(("0", "(none)"), (await TextAsync(empty.Cs, "select count(*) from meter.agg_daily"), await CursorRowAsync(empty.Cs)));
            output.WriteLine("[cursor] إعادَةُ البِناءِ عَلى دَفتَرٍ بِلا سُطور: مَجاميع 0 · مُؤَشِّر (none)");
        }
    }
}
