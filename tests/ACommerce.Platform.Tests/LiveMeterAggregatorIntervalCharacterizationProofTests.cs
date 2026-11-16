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

// ═══ تَوصيفٌ قَبلَ التَبديل — المُجَمِّعُ في `--interval` وقُفلُه حَيّ (‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ · M6A-CNT-2؛ القاعِدَة ٣) ═══════════════════
//
// **ما لا يَتَغَيَّرُ حينَ تَتَحَقَّقُ كُلُّ صَفحَةٍ مِن نَفسِها**: التَبديلُ التالي يَجعَلُ الصَفحَةَ تَقرَأُ المُؤَشِّرَ المَخزونَ في لَقطَتِها وتُودِعُه بِشَرطِ ما قَرَأَت،
// ويَسأَلُ جَلسَةَ القُفلِ قَبلَ كُلِّ صَفحَة. وصُفوفُ `--once` السَبعَةُ في `LiveMeterAggregatorProofTests` تُثبِتُ ما لِذلك الوَضع؛ أَمّا `--interval` — وَضعُ آلَةِ
// العَدِّ في الإنتاج — فَلا صَفَّ يَمَسُّه. فَهُنا ما يَفعَلُه اليَومَ وقُفلُه حَيّ، ويَبقى بِحَرفِه بَعدَ التَبديل: سَطرُ البَدء؛ والدَورَةُ الأُولى تَسحَبُ ما في
// الدَفتَرِ بِصَفَحاتِها؛ والدَوراتُ الخالِيَةُ تَطبَعُ صِفراً ولا تَخرُج؛ والسُطورُ المُضافَةُ بَعدَها تُسحَبُ في دَورَةٍ تالِيَة؛ والقُفلُ مُمسَكٌ طولَ التَشغيل
// (`--once` آخَرُ يَخرُجُ 3 بِاسمِ خَلفِيَّتِه)؛ والمَجاميعُ = تَجميعُ الدَفتَر، والمُؤَشِّرُ آخِرُ مِفتاح. **ولا يُعَدَّلُ حَرفٌ فيه بَعدَ إيداعِه.**
//
// **الثُنائِيُّ المَبنِيُّ لا نُسخَةٌ مِنه** (القاعِدَة ١٤، كَـ`LiveMeterAggregatorProofTests`): `apps/V1.Meter/bin/<التَهيِئَة>/net10.0/V1.Meter.dll`؛ وقاعِدَةٌ
// جَديدَةٌ (`{قاعِدَةُ البيئَة}_mi_{8}`) يُنشِئُ دَفتَرَها `MeterBoot` الحَقيقِيّ وتُحذَفُ في `DisposeAsync`.

[Collection(LiveProofCollection.Name)]
public class LiveMeterAggregatorIntervalCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private static readonly Regex PullLine = new(
        @"^\[meter-agg\] gen (?<gen>\d+) · pulled (?<ops>\d+)(?: \((?<units>[^)]*)\))? · presence (?<presence>\d+)(?: \([^)]*\))? · pages (?<pages>\d+) · horizon (?<h>\S+) · lag (?<lag>\d+)s$",
        RegexOptions.CultureInvariant);

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
            b.Database = $"{stem}_mi_{Guid.NewGuid().ToString("N")[..8]}";
            await TextAsync(BaseCs, $"create database \"{b.Database}\"");
            var store = DocumentStore.For(o =>
            {
                o.Connection(b.ConnectionString);
                o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
            });
            var boot = await MeterBoot.EnsureAsync(store);
            output.WriteLine($"[interval] {b.Database}: {boot.Line}");
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

    private static Task<string> Totals(string cs, string from)
        => TextAsync(cs, "select coalesce(string_agg(concat_ws('|', day, app, entrepreneur, role, feature, provider, unit, entries, quantity), E'\\n' "
                         + "order by day, app, entrepreneur, role, feature, provider, unit), '') from " + from);

    private const string LedgerGrouped =
        "(select (occurred_at at time zone 'Asia/Riyadh')::date as day, app, entrepreneur, role, feature, provider, unit, "
        + "count(*) as entries, sum(quantity) as quantity from meter.usage_entry group by 1, 2, 3, 4, 5, 6, 7) t";

    private sealed class Run
    {
        public required Process Process { get; init; }
        public List<string> Lines { get; } = [];
        public string[] Snapshot() { lock (Lines) return Lines.ToArray(); }
    }

    private static Run Start(string cs, params string[] args)
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
        var run = new Run { Process = Process.Start(psi)! };
        run.Process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (run.Lines) run.Lines.Add(e.Data); };
        run.Process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (run.Lines) run.Lines.Add("! " + e.Data); };
        run.Process.BeginOutputReadLine();
        run.Process.BeginErrorReadLine();
        return run;
    }

    private static async Task UntilAsync(Func<bool> done, string what, Run run)
    {
        var ok = false;
        for (var i = 0; i < 600 && !(ok = done()); i++) await Task.Delay(100);
        Assert.True(ok, $"{what} — لَم يَحدُث في 60 ثانِيَة:\n" + string.Join("\n", run.Snapshot()));
    }

    [Fact]
    public async Task An_interval_runner_with_a_live_lock_pulls_each_tick_prints_zero_when_idle_and_holds_the_lock_throughout()
    {
        if (!Enabled) return;
        await using var db = await Db.CreateAsync(output);
        await EntriesAsync(db.Cs, "w13-i", 5);
        var run = Start(db.Cs, "--interval", "1", "--page", "2");
        try
        {
            string[] Pulls() => run.Snapshot().Where(x => PullLine.IsMatch(x)).ToArray();

            // ‏الدَورَةُ الأُولى: الخَمسَةُ بِثَلاثِ صَفَحات، ثُمَّ دَورَتانِ خالِيَتانِ عَلى الأَقَلّ.
            await UntilAsync(() => Pulls().Length >= 3, "ثَلاثُ دَوراتٍ أُولى", run);
            var first = PullLine.Match(Pulls()[0]);
            Assert.Equal(("1", "5", "deal.create 5", "0", "3"),
                (first.Groups["gen"].Value, first.Groups["ops"].Value, first.Groups["units"].Value, first.Groups["presence"].Value, first.Groups["pages"].Value));
            Assert.All(Pulls().Skip(1), x => Assert.Matches(@"^\[meter-agg\] gen 1 · pulled 0 · presence 0 · pages 0 · horizon \S+ · lag 0s$", x));

            // ‏سُطورٌ بَعدَ الخُمول ⇐ تُسحَبُ في دَورَةٍ تالِيَة.
            var before = Pulls().Length;
            await EntriesAsync(db.Cs, "w13-i", 2);
            await UntilAsync(() => Pulls().Skip(before).Any(x => x.Contains(" · pulled 2 (deal.create 2) · presence 0 · pages 1 · ", StringComparison.Ordinal)),
                "دَورَةٌ تَسحَبُ السَطرَينِ المُضافَين", run);

            // ‏القُفلُ مُمسَكٌ طولَ التَشغيل: خَلفِيَّتُه تُرى، و`--once` آخَرُ يَخرُجُ 3 بِاسمِها.
            var holder = await TextAsync(db.Cs, MeterSql.AggregatorLockHolder);
            var once = Start(db.Cs, "--once", "--wait", "2");
            using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(60))) await once.Process.WaitForExitAsync(limit.Token);
            once.Process.WaitForExit();
            var onceCode = once.Process.ExitCode;
            once.Process.Dispose();
            Assert.Equal(3, onceCode);
            Assert.Contains($"[meter-agg] lock held by pid {holder} for more than 2s — another aggregator runs on this database", once.Snapshot());

            // ‏المَجاميعُ = تَجميعُ الدَفتَر، والمُؤَشِّرُ آخِرُ مِفتاحٍ بِتَرتيبِ المُؤَشِّر.
            var agg = await Totals(db.Cs, "meter.agg_daily");
            Assert.Equal(await Totals(db.Cs, LedgerGrouped), agg);
            var cursor = await TextAsync(db.Cs, "select concat_ws('|', after_gen, after_xid, after_id) from meter.agg_cursor");
            var lastKey = await TextAsync(db.Cs, "select concat_ws('|', generation, writer_xid, id) from meter.usage_entry order by generation desc, writer_xid desc, id desc limit 1");
            Assert.Equal(lastKey, cursor);

            Assert.False(run.Process.HasExited, "المُجَمِّعُ خَرَجَ وَحدَه:\n" + string.Join("\n", run.Snapshot()));
            Assert.Equal($"[meter-agg] start interval 1s · db {new NpgsqlConnectionStringBuilder(db.Cs).Host}/{db.Name} · page 2", run.Snapshot()[0]);
            output.WriteLine($"[interval] دَوراتٌ {Pulls().Length} · الأُولى «{Pulls()[0]}» · القُفلُ عِندَ pid {holder} و`--once` خَرَجَ {onceCode} · "
                             + $"المُؤَشِّر {cursor} · المَجاميع = الدَفتَر:\n{agg}");
        }
        finally
        {
            try { if (!run.Process.HasExited) run.Process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            foreach (var l in run.Snapshot()) output.WriteLine($"[interval·run] {l}");
        }
    }
}
