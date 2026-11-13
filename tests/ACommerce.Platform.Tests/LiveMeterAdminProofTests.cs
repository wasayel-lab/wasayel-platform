using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ G11 حَيّاً — صَفحَتا العَدِّ عَلى دَفتَرٍ حَقيقِيّ: بِلا مُجَمِّعٍ وبَعدَه، ولِلمَجهول، وبِلا دَفتَر، وعِندَ العُطل ═══════════════════════════════
// (‏2026-11-12، الموجَة ١٣ W13-6·٥ — ADR-051 §١٣؛ التَصميم §11 و§12 G11، RULES-12 وCOR-13). **الصَفحَةُ بِخِدماتِها هِيَ** (`HtmlRenderer` عَلى مُضيفِ
// القالِبِ المُرَكَّب — `LiveTemplateDoorHost`): البَوّابَةُ `RequirePlatformAdmin` تَقرَأُ كوكيَّ الاستوديو مِن القاعِدَةِ المُشتَرَكَة، و`MeterQueries` الحَقيقِيُّ
// يُبنى عَلى **قاعِدَةٍ جَديدَةٍ لِكُلِّ صَفّ** (`{قاعِدَةُ البيئَة}_mq_{8}`، تُحذَفُ في `finally`) دَفتَرُها مِن `MeterBoot` الحَقيقِيّ — فَالأَعدادُ مَعلومَةٌ لا يُلَوِّثُها
// بُرهانٌ آخَرُ يَكتُبُ في القاعِدَةِ المُشتَرَكَة. والمُجَمِّعُ **الثُنائِيُّ المَبنِيُّ نَفسُه** (`apps/V1.Meter`، القاعِدَة ١٤) لا جُمَلُه مَنسوخَة.
//
// **وما يُقاس**: (١) بِلا مُؤَشِّرٍ تُرسَمُ أَرقامُ الدَفتَرِ مُباشَرَةً مَعَ «المُجَمِّعُ لَم يَعمَل بَعد»، والذَيلُ مَحدودٌ بِنافِذَةِ الصَفحَةِ بِحافَّتَيها (يَومُ الرِياض)؛
// (٢) بَعدَ `--once` وسُطورٍ بَعدَه: المَجموعُ + الذَيلُ = عَدُّ الدَفتَرِ في النافِذَةِ لِكُلِّ يَوم؛ (٣) المَجهولُ وغَيرُ المُشرِفِ لا يُرسَمُ لَهُما رَقَمٌ **ولا يُقرَأُ
// لَهُما الدَفتَر** (قارِئٌ عَلى قاعِدَةٍ غائِبَةٍ لا يَكتُبُ سَطراً)؛ (٤) قاعِدَةٌ بِلا دَفتَر (‏42P01) ← «لا بَيانات» بِلا سَطرِ عُطل؛ (٥) أَيُّ عُطلٍ آخَر ← سَطرٌ واحِدٌ
// `[meter-page]` وبِطاقَةُ خَطَأ — والتَصييرُ لا يَرمي (أَي لا ‏500).

[Collection(LiveProofCollection.Name)]
public class LiveMeterAdminProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private const string App = "mq-store";
    private const string Other = "mq-other";

    // ─── القاعِدَةُ والدَفتَرُ والقارِئ ──────────────────────────────────────────────────────────────────────────

    private sealed class Ledger : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Cs { get; init; }
        public required DocumentStore Store { get; init; }

        /// <summary>قاعِدَةٌ جَديدَة؛ و<paramref name="boot"/> يُنشِئُ الدَفتَرَ كَما يُنشِئُه التَطبيق (ميزَةُ Weasel و<c>MeterBoot.EnsureAsync</c>)، وبِدونِه تَبقى بِلا <c>meter</c>.</summary>
        public static async Task<Ledger> CreateAsync(ITestOutputHelper output, bool boot)
        {
            var b = new NpgsqlConnectionStringBuilder(BaseCs);
            var stem = (b.Database ?? "wasayel").ToLowerInvariant();
            if (stem.Length > 48) stem = stem[..48];
            b.Database = $"{stem}_mq_{Guid.NewGuid().ToString("N")[..8]}";
            await ExecAsync(BaseCs, $"create database \"{b.Database}\"");
            var store = DocumentStore.For(o =>
            {
                o.Connection(b.ConnectionString);
                o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
            });
            var line = boot ? (await MeterBoot.EnsureAsync(store)).Line : "بِلا دَفتَر";
            output.WriteLine($"[G11] {b.Database}: {line}");
            return new Ledger { Name = b.Database, Cs = b.ConnectionString, Store = store };
        }

        public async ValueTask DisposeAsync()
        {
            Store.Dispose();
            NpgsqlConnection.ClearAllPools();
            await ExecAsync(BaseCs, $"drop database if exists \"{Name}\" with (force)");
        }
    }

    /// <summary>سِجِلٌّ يَلتَقِطُ ما يَكتُبُه القارِئ — لِيُعَدَّ سَطرُ <c>[meter-page]</c> لا لِيُفتَرَض.</summary>
    private sealed class Lines : ILogger<MeterQueries>
    {
        private readonly List<string> _all = [];
        public string[] All { get { lock (_all) return _all.ToArray(); } }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_all) _all.Add($"{logLevel} {formatter(state, exception)}");
        }
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> TextAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>بِدايَةُ يَومِ الرِياضِ قَبلَ <paramref name="daysBack"/> يَوماً، لَحظَةً (<c>timestamptz</c>) — حافَّةُ النافِذَة.</summary>
    private static string DayStart(int daysBack)
        => $"(((transaction_timestamp() at time zone '{MeterSql.DayZone}')::date - {daysBack})::timestamp at time zone '{MeterSql.DayZone}')";

    /// <summary>سُطورُ <c>deal.create</c> بِالأَعمِدَةِ الَّتي يَكتُبُها المُستَمِع، بِلَحظَةِ <paramref name="at"/> (تَعبيرُ SQL).</summary>
    private static Task EntriesAsync(Ledger l, string app, int count, string at)
        => ExecAsync(l.Cs,
            "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation, occurred_at) "
            + $"select 'deal.create:{app}:' || gen_random_uuid(), 'deal.create', 1, '{app}', 'owner-{app}', '_none', 'deal_status', '_none', 'deal', gen_random_uuid()::text, "
            + $"(select max(generation) from meter.generation), {at} from generate_series(1, {count})");

    /// <summary>
    /// <para><b>الدَفتَرُ المَعلوم</b>: لِـ<see cref="App"/> ثَلاثَةٌ الآن، وواحِدٌ عِندَ بِدايَةِ النافِذَةِ تَماماً (يُعَدّ)، وواحِدٌ قَبلَها بِثانِيَة (لا يُعَدّ)،
    /// وواحِدٌ قَبلَ أَربَعينَ يَوماً (لا يُعَدّ)، وواحِدٌ آخِرَ ثانِيَةٍ مِن أَمسِ الرِياض (يُعَدُّ في المَتجَرِ لا في الفِهرِس)؛ ولِـ<see cref="Other"/> واحِدٌ الآن.</para>
    /// </summary>
    private static async Task FixtureAsync(Ledger l)
    {
        var start = DayStart(MeterQueries.StoreWindowDays - 1);
        await EntriesAsync(l, App, 3, "transaction_timestamp()");
        await EntriesAsync(l, App, 1, start);
        await EntriesAsync(l, App, 1, $"{start} - interval '1 second'");
        await EntriesAsync(l, App, 1, "transaction_timestamp() - interval '40 days'");
        await EntriesAsync(l, App, 1, $"{DayStart(0)} - interval '1 second'");
        await EntriesAsync(l, Other, 1, "transaction_timestamp()");
    }

    /// <summary>عَدُّ الدَفتَرِ الخامُّ لِمَتجَرٍ في نافِذَةِ صَفحَتِه، لِكُلِّ يَوم — المَرجِعُ الَّذي تُقارَنُ بِه الصَفحَة.</summary>
    private static Task<string> RawByDayAsync(Ledger l, string app)
        => TextAsync(l.Cs, "select coalesce(string_agg(d || ' ' || n, ' · ' order by d desc), '') from ("
                           + $"select to_char((occurred_at at time zone '{MeterSql.DayZone}')::date, 'YYYY-MM-DD') d, count(*) n from meter.usage_entry "
                           + $"where app = '{app}' and occurred_at >= {DayStart(MeterQueries.StoreWindowDays - 1)} group by 1) t");

    private static string ByDay(MeterReading r, string app)
        => string.Join(" · ", r.Days(app).Select(d => $"{d} {r.Entries(app, day: d)}"));

    // ─── الصَفحَة ────────────────────────────────────────────────────────────────────────────────────────────

    private static LiveTemplateDoorHost HostReading(DocumentStore ledger, Lines lines)
        => LiveTemplateDoorHost.Build(services: s => s.AddScoped(_ => new MeterQueries(ledger, lines)));

    /// <summary>الصَفحَةُ مُصَيَّرَةً بِكوكيِّ الاستوديو (أَو بِلا كوكي) — النَصُّ مَفكوكُ الكِيانات. والتَصييرُ الَّذي يَرمي هُوَ ‏500 الخادِم.</summary>
    private static async Task<string> RenderAsync<TPage>(LiveTemplateDoorHost host, string? token, string path, Dictionary<string, object?> parameters)
        where TPage : IComponent
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = path;
        if (token is not null) ctx.Request.Headers.Cookie = $"{StudioAuth.CookieName}={token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", "http://localhost" + path);
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TPage>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private static Task<string> StorePageAsync(LiveTemplateDoorHost host, string? token, string app)
        => RenderAsync<MeterStore>(host, token, $"/admin/meter/stores/{app}", new() { [nameof(MeterStore.Slug)] = app });

    private static Task<string> IndexPageAsync(LiveTemplateDoorHost host, string? token)
        => RenderAsync<MeterIndex>(host, token, "/admin/meter", new());

    private static string Text(string key) => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)!;

    private static string Attr(string html, string name)
        => string.Join(",", Regex.Matches(html, $@"\b{Regex.Escape(name)}=""(?<v>[^""]*)""").Select(m => m.Groups["v"].Value));

    /// <summary>قيمَةُ <c>data-entries</c> لِشارَةِ وَحدَةٍ في أَوَّلِ عُنصُرٍ يَحمِلُ <paramref name="anchor"/> — أَو فارِغٌ إن غابَت.</summary>
    private static string UnitEntries(string html, string anchor, string unit)
    {
        var from = html.IndexOf(anchor, StringComparison.Ordinal);
        var m = Regex.Match(from < 0 ? "" : html[from..], $@"data-meter-unit=""{Regex.Escape(unit)}"" data-entries=""(?<n>\d+)""");
        return m.Success ? m.Groups["n"].Value : "";
    }

    // ─── (١) بِلا مُؤَشِّر: الدَفتَرُ مُباشَرَةً، والنافِذَةُ بِحافَّتَيها ─────────────────────────────────────────────

    [Fact]
    public async Task Before_any_aggregator_run_the_pages_count_the_ledger_raw_inside_the_window_and_say_so()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: true);
        await FixtureAsync(l);
        var lines = new Lines();
        var q = new MeterQueries(l.Store, lines);
        var store = await q.StoreAsync(App);
        var today = await q.TodayAsync();
        var raw = await RawByDayAsync(l, App);
        var ledgerAll = await TextAsync(l.Cs, $"select count(*) from meter.usage_entry where app = '{App}'");
        output.WriteLine($"[G11·A] المَتجَر: {store.State} · المُجَمِّع {store.AggregatorRan} · {store.From}…{store.To} · {store.Entries(App)} (ذَيل {store.TailEntries(App)}) مِن دَفتَرِه {ledgerAll} · "
                         + $"لِكُلِّ يَوم «{ByDay(store, App)}» · الخامُّ «{raw}» · الفِهرِس {App} {today.Entries(App)} · {Other} {today.Entries(Other)} · سُطورُ سِجِلّ {lines.All.Length}");
        Assert.Equal(MeterReadState.Data, store.State);
        Assert.False(store.AggregatorRan);
        Assert.Equal(await TextAsync(l.Cs, $"select to_char((transaction_timestamp() at time zone '{MeterSql.DayZone}')::date - {MeterQueries.StoreWindowDays - 1}, 'YYYY-MM-DD')"), store.From);
        Assert.Equal(await TextAsync(l.Cs, $"select to_char((transaction_timestamp() at time zone '{MeterSql.DayZone}')::date, 'YYYY-MM-DD')"), store.To);
        Assert.Equal("7", ledgerAll);
        Assert.Equal(5L, store.Entries(App));                // ‏3 الآن + حافَّةُ النافِذَة + آخِرُ ثانِيَةٍ مِن أَمس — لا ما قَبلَ الحافَّةِ ولا الأَربَعون
        Assert.Equal(5L, store.TailEntries(App));            // ‏بِلا مُؤَشِّرٍ فَالكُلُّ ذَيل
        Assert.Equal(0L, store.Entries(Other));              // ‏صَفحَةُ مَتجَرٍ لا تَقرَأُ غَيرَه
        Assert.Equal(raw, ByDay(store, App));
        Assert.Equal(3L, today.Entries(App));                // ‏الفِهرِسُ يَومُ الرِياضِ الجاري وَحدَه
        Assert.Equal(1L, today.Entries(Other));
        Assert.Empty(lines.All);

        var admin = default(Probe);
        await using var host = HostReading(l.Store, lines);
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var page = await StorePageAsync(host, admin.Token, App);
            var index = await IndexPageAsync(host, admin.Token);
            output.WriteLine($"[G11·A] صَفحَةُ المَتجَر: الحالَة «{Attr(page, "data-meter-state")}» · المُجَمِّع «{Attr(page, "data-meter-agg")}» · المَجموع «{Attr(page, "data-meter-total")}» · "
                             + $"deal.create «{UnitEntries(page, "data-meter-total", "deal.create")}» · الأَيّام «{Attr(page, "data-meter-day")}» — "
                             + $"الفِهرِس: المَتاجِر «{Attr(index, "data-meter-app")}» · {App} «{UnitEntries(index, $"data-meter-app=\"{App}\"", "deal.create")}» · "
                             + $"{Other} «{UnitEntries(index, $"data-meter-app=\"{Other}\"", "deal.create")}»");
            Assert.Equal("data", Attr(page, "data-meter-state"));
            Assert.Equal("absent", Attr(page, "data-meter-agg"));
            Assert.Contains(Text("admin.meter.agg_absent"), page, StringComparison.Ordinal);
            Assert.Equal("5", Attr(page, "data-meter-total"));
            Assert.Equal("5", UnitEntries(page, "data-meter-total", "deal.create"));
            Assert.Equal(3, Attr(page, "data-meter-day").Split(',').Length);
            Assert.Equal("absent", Attr(index, "data-meter-agg"));
            Assert.Equal($"{Other},{App}", Attr(index, "data-meter-app"));
            Assert.Equal("3", UnitEntries(index, $"data-meter-app=\"{App}\"", "deal.create"));
            Assert.Equal("1", UnitEntries(index, $"data-meter-app=\"{Other}\"", "deal.create"));
            Assert.Contains($"href=\"/admin/meter/stores/{App}\"", index, StringComparison.Ordinal);
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        Assert.Empty(lines.All);
    }

    // ─── (٢) بَعدَ المُجَمِّعِ وسُطورٍ بَعدَه: المَجموعُ + الذَيلُ = الدَفتَر ────────────────────────────────────────────

    private static string MeterDll()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.Meter", "bin", tfm.Parent!.Name, tfm.Name, "V1.Meter.dll");
    }

    private async Task<(int Code, string Output)> AggregateOnceAsync(string cs)
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
        psi.ArgumentList.Add("--once");
        psi.Environment["ConnectionStrings__Postgres"] = cs;
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await p.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { p.Kill(entireProcessTree: true); Assert.Fail("المُجَمِّعُ لَم يَخرُج في 180 ثانِيَة."); }
        var text = (await stdout + await stderr).Trim();
        output.WriteLine($"[G11·B] V1.Meter --once ⇒ خُروج {p.ExitCode}: {text}");
        return (p.ExitCode, text);
    }

    [Fact]
    public async Task After_the_aggregator_runs_aggregate_plus_raw_tail_equals_the_ledger_in_the_window_per_day()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: true);
        await FixtureAsync(l);
        var (code, text) = await AggregateOnceAsync(l.Cs);
        Assert.Equal(0, code);
        Assert.Matches(@"· pulled 8 \(deal\.create 8\) ·", text);     // ‏كُلُّ ما في الدَفتَرِ — النافِذَةُ لِلصَفحَةِ لا لِلمُجَمِّع
        await EntriesAsync(l, App, 2, "transaction_timestamp()");      // ‏ذَيلٌ فَوقَ المُؤَشِّر

        var lines = new Lines();
        var q = new MeterQueries(l.Store, lines);
        var store = await q.StoreAsync(App);
        var today = await q.TodayAsync();
        var raw = await RawByDayAsync(l, App);
        var aggOnly = await TextAsync(l.Cs, $"select coalesce(sum(entries), 0) from meter.agg_daily where app = '{App}' and day >= (transaction_timestamp() at time zone '{MeterSql.DayZone}')::date - {MeterQueries.StoreWindowDays - 1}");
        output.WriteLine($"[G11·B] المَتجَر: المُجَمِّع {store.AggregatorRan} (سَحَبَ {store.PulledAtUtc:O}) · {store.Entries(App)} = مَجموعٌ {aggOnly} + ذَيلٌ {store.TailEntries(App)} · "
                         + $"لِكُلِّ يَوم «{ByDay(store, App)}» · الخامُّ «{raw}» · الفِهرِس {App} {today.Entries(App)} (ذَيل {today.TailEntries(App)}) · سُطورُ سِجِلّ {lines.All.Length}");
        Assert.Equal(MeterReadState.Data, store.State);
        Assert.True(store.AggregatorRan);
        Assert.NotNull(store.PulledAtUtc);
        Assert.Equal(7L, store.Entries(App));
        Assert.Equal(2L, store.TailEntries(App));
        Assert.Equal("5", aggOnly);
        Assert.Equal(raw, ByDay(store, App));
        Assert.Equal(5L, today.Entries(App));
        Assert.Equal(2L, today.TailEntries(App));
        Assert.Empty(lines.All);

        var admin = default(Probe);
        await using var host = HostReading(l.Store, lines);
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var page = await StorePageAsync(host, admin.Token, App);
            var index = await IndexPageAsync(host, admin.Token);
            output.WriteLine($"[G11·B] صَفحَةُ المَتجَر: المُجَمِّع «{Attr(page, "data-meter-agg")}» · الذَيل «{Attr(page, "data-meter-tail")}» · المَجموع «{Attr(page, "data-meter-total")}» — "
                             + $"الفِهرِس: المُجَمِّع «{Attr(index, "data-meter-agg")}» · {App} «{UnitEntries(index, $"data-meter-app=\"{App}\"", "deal.create")}»");
            Assert.Equal("ran", Attr(page, "data-meter-agg"));
            Assert.Equal("2", Attr(page, "data-meter-tail"));
            Assert.Equal("7", Attr(page, "data-meter-total"));
            Assert.DoesNotContain(Text("admin.meter.agg_absent"), page, StringComparison.Ordinal);
            Assert.Equal("ran", Attr(index, "data-meter-agg"));
            Assert.Equal("5", UnitEntries(index, $"data-meter-app=\"{App}\"", "deal.create"));
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
    }

    // ─── (٣) المَجهولُ وغَيرُ المُشرِف: لا رَقَمَ يُرسَمُ ولا دَفتَرَ يُقرَأ ─────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_and_non_admin_requests_render_no_counts_and_never_read_the_ledger()
    {
        if (!Enabled) return;
        // ‏قارِئٌ عَلى قاعِدَةٍ لا وُجودَ لَها: أَيُّ قِراءَةٍ تَفشَلُ (‏3D000) فَتَكتُبُ سَطرَ [meter-page] — فَصِفرُ سُطورٍ = لَم يُقرَأ شَيء.
        var b = new NpgsqlConnectionStringBuilder(BaseCs) { Database = $"wasayel_absent_{Guid.NewGuid().ToString("N")[..8]}" };
        using var nowhere = DocumentStore.For(o => o.Connection(b.ConnectionString));
        var lines = new Lines();
        var control = await new MeterQueries(nowhere, lines).TodayAsync();
        output.WriteLine($"[G11·C] الضابِطُ: قِراءَةٌ عَلى {b.Database} ⇒ {control.State} · سُطور {lines.All.Length}: {string.Join(" | ", lines.All)}");
        Assert.Equal(MeterReadState.Error, control.State);
        Assert.Single(lines.All);

        var reader = new Lines();
        var member = default(Probe);
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddScoped(_ => new MeterQueries(nowhere, reader)));
        try
        {
            member = await UserAsync(host, consented: true, platformAdmin: false);
            var rendered = new List<(string Who, string Page, string Html)>
            {
                ("مَجهول", "index", await IndexPageAsync(host, null)),
                ("مَجهول", "store", await StorePageAsync(host, null, App)),
                ("غَيرُ مُشرِف", "index", await IndexPageAsync(host, member.Token)),
                ("غَيرُ مُشرِف", "store", await StorePageAsync(host, member.Token, App)),
            };
            foreach (var (who, page, html) in rendered)
                output.WriteLine($"[G11·C] {who} · {page}: data-meter «{Attr(html, "data-meter-state")}{Attr(html, "data-meter-agg")}{Attr(html, "data-meter-total")}» · "
                                 + $"دُخول {html.Contains("href=\"/studio/auth\"", StringComparison.Ordinal)} · مَنع {html.Contains(Text("admin.guard.forbidden_title"), StringComparison.Ordinal)}");
            Assert.All(rendered, r => Assert.DoesNotContain("data-meter-", r.Html, StringComparison.Ordinal));
            Assert.All(rendered.Take(2), r => Assert.Contains(Text("admin.guard.need_auth_title"), r.Html, StringComparison.Ordinal));
            Assert.All(rendered.Skip(2), r => Assert.Contains(Text("admin.guard.forbidden_title"), r.Html, StringComparison.Ordinal));
        }
        finally { await CleanAsync(host, member is null ? [] : [member]); }
        output.WriteLine($"[G11·C] صَفَحاتٌ مُصَيَّرَة 4 · قِراءاتٌ لِلدَفتَر {reader.All.Length}");
        Assert.Empty(reader.All);
    }

    // ─── (٤) قاعِدَةٌ بِلا دَفتَر: «لا بَيانات» (‏42P01) بِلا سَطرِ عُطل ───────────────────────────────────────────────

    [Fact]
    public async Task A_database_without_the_ledger_reads_no_data_and_logs_nothing()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: false);
        var lines = new Lines();
        var q = new MeterQueries(l.Store, lines);
        var index = await q.TodayAsync();
        var store = await q.StoreAsync(App);
        var schemas = await TextAsync(l.Cs, "select count(*) from information_schema.schemata where schema_name = 'meter'");
        output.WriteLine($"[G11·D] مُخَطَّطُ meter {schemas} · الفِهرِس {index.State} · المَتجَر {store.State} · سُطورُ سِجِلّ {lines.All.Length}");
        Assert.Equal("0", schemas);
        Assert.Equal(MeterReadState.NoLedger, index.State);
        Assert.Equal(MeterReadState.NoLedger, store.State);

        var admin = default(Probe);
        await using var host = HostReading(l.Store, lines);
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var page = await StorePageAsync(host, admin.Token, App);
            output.WriteLine($"[G11·D] الصَفحَة: الحالَة «{Attr(page, "data-meter-state")}» · «{Text("admin.meter.no_ledger_title")}» {page.Contains(Text("admin.meter.no_ledger_title"), StringComparison.Ordinal)}");
            Assert.Equal("no-ledger", Attr(page, "data-meter-state"));
            Assert.Contains(Text("admin.meter.no_ledger_title"), page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-meter-agg", page, StringComparison.Ordinal);
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        Assert.Empty(lines.All);
    }

    // ─── (٥) أَيُّ عُطلٍ آخَر: سَطرٌ واحِدٌ [meter-page] وبِطاقَةُ خَطَأ — لا ‏500 ──────────────────────────────────────────

    [Fact]
    public async Task Any_other_failure_logs_one_meter_page_line_and_draws_the_error_card_not_a_500()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: true);
        await FixtureAsync(l);
        await ExecAsync(l.Cs, "alter table meter.agg_daily rename column quantity to quantity_gone");   // ‏عُطلٌ غَيرُ 42P01: ‏42703
        var lines = new Lines();
        var store = await new MeterQueries(l.Store, lines).StoreAsync(App);
        output.WriteLine($"[G11·E] القِراءَة: {store.State} · سُطور {lines.All.Length}: {string.Join(" | ", lines.All)}");
        Assert.Equal(MeterReadState.Error, store.State);
        var line = Assert.Single(lines.All);
        Assert.StartsWith($"Error {MeterQueries.LogTag} store {App}: PostgresException 42703 ", line, StringComparison.Ordinal);

        var admin = default(Probe);
        await using var host = HostReading(l.Store, lines);
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var page = await StorePageAsync(host, admin.Token, App);       // ‏لا يَرمي — والرَميُ هُنا هُوَ ‏500 الخادِم
            var index = await IndexPageAsync(host, admin.Token);
            output.WriteLine($"[G11·E] الصَفحَتان: «{Attr(page, "data-meter-state")}» و«{Attr(index, "data-meter-state")}» · «{Text("admin.meter.error_title")}» "
                             + $"{page.Contains(Text("admin.meter.error_title"), StringComparison.Ordinal)} · سُطورُ سِجِلّ {lines.All.Length}");
            Assert.Equal("error", Attr(page, "data-meter-state"));
            Assert.Equal("error", Attr(index, "data-meter-state"));
            Assert.Contains(Text("admin.meter.error_title"), page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-meter-total", page, StringComparison.Ordinal);
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        Assert.Equal(3, lines.All.Length);                                  // ‏سَطرٌ لِكُلِّ قِراءَة: المُباشِرَةُ والصَفحَتان
        Assert.All(lines.All, x => Assert.Contains(MeterQueries.LogTag, x, StringComparison.Ordinal));
    }

    // ─── (٦) جانِبُ التِقنِيَةِ في الجَدوَلِ نَفسِه: لا يَدخُلُ مَجموعَ التَشغيلِ ولا بُعدَيه ولا قائِمَةَ الفِهرِس ─────────────────────────
    // (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ د الأولى · W13-6·D-R1·١ — M6D-SEC-R1-1 وM6D-CNT-2 وM6D-RULES-R1-1؛ ADR-051 §٢٣ وسَطرُه المُؤَرَّخ). لَقطَةُ الحُضورِ تَكتُبُ
    // سُطورَ `presence.*` بِدَورِ `_system` (أَو الدَورِ المَعدودِ لِسَطرِ الدَور) عِندَ بِدايَةِ اليَوم؛ والصُفوفُ أَعلاه بِلا سَطرِ حُضور، فَكانَت عَمياءَ عَن الخَلط.

    private const string Idle = "mq-idle";

    /// <summary>سَطرُ حُضورٍ بِشَكلِ سَطرِ اللَقطَة (<c>MeterSql.PresenceInsert</c>): عِندَ بِدايَةِ يَومِ الرِياض، والمَصدَرُ المَتجَر.</summary>
    private static Task PresenceAsync(Ledger l, string app, string unit, string role, string entrepreneur)
        => ExecAsync(l.Cs,
            "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation, occurred_at) "
            + $"values ('{unit}:{app}:' || gen_random_uuid(), '{unit}', 1, '{app}', '{entrepreneur}', '{role}', 'tenants', '_none', 'tenant', '{app}', "
            + $"(select max(generation) from meter.generation), {DayStart(0)})");

    /// <summary>مَجموعُ <c>data-entries</c> لِبَلاطاتِ الوَحَداتِ تَحتَ <c>data-meter-total</c> — مِنها حَتّى بَلاطَةِ <c>_unresolved</c>/<c>_seed</c>.</summary>
    private static long TileSum(string html)
    {
        var from = html.IndexOf("data-meter-total=", StringComparison.Ordinal);
        var to = from < 0 ? -1 : html.IndexOf("data-meter-unresolved=", from, StringComparison.Ordinal);
        if (to < 0) return -1;
        return Regex.Matches(html[from..to], @"data-meter-unit=""[^""]+"" data-entries=""(?<n>\d+)""").Sum(m => long.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Presence_rows_never_enter_the_operations_total_its_dimensions_or_the_store_list()
    {
        if (!Enabled) return;
        await using var l = await Ledger.CreateAsync(output, boot: true);
        await EntriesAsync(l, App, 2, "transaction_timestamp()");
        await PresenceAsync(l, App, "presence.store", MeterSentinels.SystemJob, $"owner-{App}");
        await PresenceAsync(l, App, "presence.role", "vendor", $"owner-{App}");
        await PresenceAsync(l, App, "presence.auth_channel", MeterSentinels.SystemJob, $"owner-{App}");
        await PresenceAsync(l, Idle, "presence.store", MeterSentinels.SystemJob, MeterSentinels.Unowned);
        var sides = await TextAsync(l.Cs, "select string_agg(app || ' ' || unit || ' ' || n, ' · ' order by app, unit) from (select app, unit, count(*) n from meter.usage_entry group by app, unit) t");
        var lines = new Lines();
        var q = new MeterQueries(l.Store, lines);
        var store = await q.StoreAsync(App);
        var idle = await q.StoreAsync(Idle);
        var today = await q.TodayAsync();
        string Dim(MeterReading r, Func<MeterCountRow, string> d, string app) => string.Join(",", r.By(d, app).Select(v => $"{v.Value}={v.Entries}"));
        output.WriteLine($"[G11·F] الدَفتَر «{sides}» · السُطورُ المَقروءَةُ لِـ{App} {store.Rows.Count(r => r.App == App)} (حُضور {store.Rows.Count(r => r.App == App && !MeterReading.IsOperation(r.Unit))}) — "
                         + $"المَجموع {store.Entries(App)} · الذَيل {store.TailEntries(App)} · الدَور «{Dim(store, r => r.Role, App)}» · رائِدُ الأَعمال «{Dim(store, r => r.Entrepreneur, App)}» · "
                         + $"الأَيّام [{string.Join(", ", store.Days(App))}] · {Idle} {idle.Entries(Idle)} · مَتاجِرُ الفِهرِس [{string.Join(", ", today.Apps())}]");
        // ‏القِراءَةُ تَحمِلُ الجانِبَين (قِسمُ الحُضورِ يَقرَؤُها في البَند ٢٠)، والدَوالُّ تَعُدُّ التَشغيلَ وَحدَه.
        Assert.Equal(3, store.Rows.Count(r => r.App == App && !MeterReading.IsOperation(r.Unit)));
        Assert.Equal(2L, store.Entries(App));
        Assert.Equal(2L, store.TailEntries(App));
        Assert.Equal(new[] { ("_none", 2L) }, store.By(r => r.Role, App));
        Assert.Equal(new[] { ($"owner-{App}", 2L) }, store.By(r => r.Entrepreneur, App));
        Assert.Single(store.Days(App));
        Assert.Equal(0L, idle.Entries(Idle));
        Assert.Equal(new[] { App }, today.Apps());
        Assert.Empty(lines.All);

        var admin = default(Probe);
        await using var host = HostReading(l.Store, lines);
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var page = await StorePageAsync(host, admin.Token, App);
            var idlePage = await StorePageAsync(host, admin.Token, Idle);
            var index = await IndexPageAsync(host, admin.Token);
            output.WriteLine($"[G11·F] صَفحَةُ {App}: المَجموع «{Attr(page, "data-meter-total")}» · مَجموعُ البَلاطات {TileSum(page)} · الوَحَدات «{Attr(page, "data-meter-unit")}» · "
                             + $"الأَدوار «{Attr(page, "data-meter-role")}» · رُوّادُ الأَعمال «{Attr(page, "data-meter-entrepreneur")}» · الأَيّام «{Attr(page, "data-meter-day")}» — "
                             + $"صَفحَةُ {Idle}: المَجموع «{Attr(idlePage, "data-meter-total")}» · «لا عَمَلِيّات» {idlePage.Contains(Text("admin.meter.store_empty"), StringComparison.Ordinal)} — "
                             + $"الفِهرِس: المَتاجِر «{Attr(index, "data-meter-app")}»");
            Assert.Equal("2", Attr(page, "data-meter-total"));
            Assert.Equal(2L, TileSum(page));
            Assert.Equal("deal.create,deal.create", Attr(page, "data-meter-unit"));   // ‏بَلاطَةُ المَجموعِ وشارَةُ يَومِه — لا وَحدَةَ حُضور
            Assert.Equal("_none", Attr(page, "data-meter-role"));
            Assert.Equal($"owner-{App}", Attr(page, "data-meter-entrepreneur"));
            Assert.DoesNotContain($"data-meter-role=\"{MeterSentinels.SystemJob}\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("presence.", page, StringComparison.Ordinal);
            Assert.Equal("0", Attr(idlePage, "data-meter-total"));
            Assert.Contains(Text("admin.meter.store_empty"), idlePage, StringComparison.Ordinal);
            Assert.Equal(App, Attr(index, "data-meter-app"));
            Assert.Equal("1", Attr(index, "data-meter-apps"));
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        Assert.Empty(lines.All);
    }
}
