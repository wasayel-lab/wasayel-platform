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

// ═══ تَوصيفٌ: قِسمُ التَشغيلِ في فِهرِسِ العَدِّ (`/admin/meter`) — ما لا يَتَغَيَّرُ حينَ يولَدُ قِسمُ التِقنِيَةِ ويَصيرُ رائِدُ الأَعمالِ رابِطاً ══════════════
// (‏2026-11-14، الموجَة ١٣ W13-6·Z-R1·١ — مُراجَعَةُ الاكتِمالِ M6Z-1؛ التَصميم §11 صَفُّ `/admin/meter`؛ ADR-051 §١٣؛ القاعِدَتانِ ٣ و١٢).
// **لِماذا هذا المِلَفّ**: الفِهرِسُ اليَومَ قائِمَةُ تَشغيلٍ وَحدَها — صَفُّ المَتجَرِ رابِطٌ واحِدٌ يَلُفُّ الصَفَّ كُلَّه ورائِدُ الأَعمالِ فيه نَصّ —
// والتَبديلُ التالي يُضيفُ قِسمَ التِقنِيَة (حُضورُ اليَومِ لِكُلِّ مَتجَرٍ ولَو خامِلاً) ويَجعَلُ خانَةَ رائِدِ الأَعمالِ رابِطاً إلى صَفحَتِه، فَيَتَغَيَّرُ بِناءُ الصَفّ
// (رابِطانِ لا يَتَداخَلان). وهذا المِلَفُّ يُثَبِّتُ قَبلَه ما يَجِبُ أَلّا يَتَغَيَّر، ولا يُمَسُّ بَعدَه حَرفاً:
//   (أ) لِلمُشرِف: الحالَةُ وسَطرُ المُجَمِّعِ وعَدَدُ مَتاجِرِ التَشغيل، ولِكُلِّ صَفِّ تَشغيلٍ بِتَرتيبِه: سلاجُه، ورابِطُه إلى صَفحَتِه، وشاراتُ وَحَداتِه بِأَعدادِها
//       ونَصِّها، و`_unresolved` و`_seed` — وهِيَ ما يَقرَؤُه `scripts/prove-meter.sh` وصُفوفُ G11؛ والمَتجَرُ الخامِلُ (حُضورٌ وَحدَه) لَيسَ صَفَّ تَشغيل؛
//   (ب) لِلمَجهول: لا `data-meter-` واحِدَة.
// ولا يُثَبِّتُ ما يَتَغَيَّرُ عَمداً: نَصُّ خانَةِ رائِدِ الأَعمالِ وبِناءُ الصَفّ، وما يَرسُمُه قِسمُ التِقنِيَة (صُفوفُه في `LiveMeterAdminProofTests` G11·F
// و`MeterAdminReachabilityTests` مَعَ التَبديل). **والإسقاطُ لا يَرى إلّا سِماتِ قِسمِ التَشغيل** (`data-meter-app` و`data-meter-unit`/`data-entries`
// و`data-meter-unresolved`/`data-meter-seed`) — فَقِسمٌ بَعدَه بِسِماتٍ أُخرى لا يَدخُلُه.
// قاعِدَةٌ جَديدَةٌ لِلصَفِّ (`{قاعِدَةُ البيئَة}_mi_{8}`، تُحذَفُ في `finally`) دَفتَرُها مِن `MeterBoot` الحَقيقِيّ؛ ويَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1.

[Collection(LiveProofCollection.Name)]
public class LiveMeterIndexOperationsCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private const string Store = "mi-store";
    private const string Other = "mi-other";
    private const string Idle = "mi-idle";

    private sealed class Silent : ILogger<MeterQueries>
    {
        public readonly List<string> Lines = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add($"{logLevel} {formatter(state, exception)}");
        }
    }

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>سُطورٌ بِأَعمِدَةِ المُستَمِعِ ولَحظَةِ الآن — بِدَورِها ورائِدِ أَعمالِها.</summary>
    private static Task RowsAsync(string cs, string app, string unit, int count, string role, string entrepreneur)
        => ExecAsync(cs,
            "insert into meter.usage_entry (id, unit, quantity, app, entrepreneur, role, feature, provider, source_type, source_id, generation) "
            + $"select '{unit}:{app}:' || gen_random_uuid(), '{unit}', 1, '{app}', '{entrepreneur}', '{role}', 'f', '_none', 's', gen_random_uuid()::text, "
            + $"(select max(generation) from meter.generation) from generate_series(1, {count})");

    /// <summary>الصَفحَةُ مُصَيَّرَةً بِكوكيِّ الاستوديو (أَو بِلا كوكي) — نَمَطُ <c>LiveMeterAdminProofTests</c>.</summary>
    private static async Task<string> IndexAsync(LiveTemplateDoorHost host, string? token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("localhost");
        ctx.Request.Path = "/admin/meter";
        if (token is not null) ctx.Request.Headers.Cookie = $"{StudioAuth.CookieName}={token}";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor) accessor.HttpContext = ctx;
        if (scope.ServiceProvider.GetService<NavigationManager>() is Microsoft.AspNetCore.Components.Routing.IHostEnvironmentNavigationManager nav)
            nav.Initialize("http://localhost/", "http://localhost/admin/meter");
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, NullLoggerFactory.Instance);
        return System.Net.WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MeterIndex>(ParameterView.Empty)).ToHtmlString()));
    }

    private static string Attr(string html, string name)
        => string.Join(",", Regex.Matches(html, $@"\b{Regex.Escape(name)}=""(?<v>[^""]*)""").Select(m => m.Groups["v"].Value));

    /// <summary>
    /// <para><b>إسقاطُ قِسمِ التَشغيل</b>: لِكُلِّ <c>data-meter-app</c> بِتَرتيبِه مَقطَعٌ مِن وَسمِه حَتّى وَسمِ التالي (أَو آخِرِ الصَفحَة)، وفيه: رابِطُ صَفحَةِ المَتجَرِ (١/٠)،
    /// والشاراتُ <c>data-meter-unit</c>/<c>data-entries</c> بِنَصِّها مَضغوطَ الفَراغ، و<c>_unresolved</c>/<c>_seed</c>.</para>
    /// </summary>
    internal static string Projection(string html)
    {
        var rows = Regex.Matches(html, @"\bdata-meter-app=""(?<a>[^""]*)""").ToList();
        var starts = rows.Select(r => html.LastIndexOf('<', r.Index)).ToList();   // ‏الوَسمُ الَّذي يَحمِلُ السِمَةَ — فَرابِطُه فيه
        var parts = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var app = rows[i].Groups["a"].Value;
            var seg = html[starts[i]..(i + 1 < rows.Count ? starts[i + 1] : html.Length)];
            var linked = seg.Contains($"href=\"/admin/meter/stores/{app}\"", StringComparison.Ordinal) ? 1 : 0;
            var chips = Regex.Matches(seg, @"\bdata-meter-unit=""(?<u>[^""]+)"" data-entries=""(?<n>\d+)""[^>]*>(?<t>[^<]*)<")
                .Select(m => $"{m.Groups["u"].Value}={m.Groups["n"].Value}«{Regex.Replace(m.Groups["t"].Value, @"\s+", " ").Trim()}»");
            parts.Add($"[{app} · رابِط {linked} · {string.Join(" ", chips)} · _unresolved {Attr(seg, "data-meter-unresolved")} · _seed {Attr(seg, "data-meter-seed")}]");
        }
        return string.Join(" ", parts);
    }

    private static string Text(string key) => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)!;

    [Fact]
    public async Task The_index_operations_section_shows_each_operating_store_with_its_link_units_and_sentinels_and_the_idle_store_is_not_an_operations_row()
    {
        if (!Enabled) return;
        var b = new NpgsqlConnectionStringBuilder(BaseCs);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        b.Database = $"{stem}_mi_{Guid.NewGuid().ToString("N")[..8]}";
        await ExecAsync(BaseCs, $"create database \"{b.Database}\"");
        var ledger = DocumentStore.For(o =>
        {
            o.Connection(b.ConnectionString);
            o.Storage.Add(new UsageLedgerFeature(o.Advanced.Migrator));
        });
        var owner = Guid.NewGuid().ToString();
        var admin = default(Probe);
        try
        {
            output.WriteLine($"[char] {b.Database}: {(await MeterBoot.EnsureAsync(ledger)).Line}");
            await RowsAsync(b.ConnectionString, Store, "deal.create", 3, "_none", owner);
            await RowsAsync(b.ConnectionString, Store, "deal.create", 1, MeterSentinels.Unresolved, MeterSentinels.Unresolved);
            await RowsAsync(b.ConnectionString, Store, "deal.create", 2, MeterSentinels.Seed, MeterSentinels.Seed);
            await RowsAsync(b.ConnectionString, Store, "store.create", 1, MeterSentinels.Entrepreneur, owner);
            await RowsAsync(b.ConnectionString, Other, "deal.create", 1, "_none", MeterSentinels.Unowned);
            await RowsAsync(b.ConnectionString, Store, "presence.store", 1, MeterSentinels.SystemJob, owner);
            await RowsAsync(b.ConnectionString, Idle, "presence.store", 1, MeterSentinels.SystemJob, MeterSentinels.Unowned);

            var lines = new Silent();
            await using var host = LiveTemplateDoorHost.Build(services: s => s.AddScoped(_ => new MeterQueries(ledger, lines)));
            try
            {
                admin = await UserAsync(host, consented: false, platformAdmin: true);
                var page = await IndexAsync(host, admin.Token);
                var anonymous = await IndexAsync(host, null);
                var projection = Projection(page);
                output.WriteLine($"[char] الفِهرِس: الحالَة «{Attr(page, "data-meter-state")}» · المُجَمِّع «{Attr(page, "data-meter-agg")}» · مَتاجِرُ التَشغيل «{Attr(page, "data-meter-apps")}» "
                                 + $"· «{Attr(page, "data-meter-app")}» — {projection}");
                output.WriteLine($"[char] المَجهول: قَرائِنُ data-meter- {Regex.Matches(anonymous, "data-meter-").Count} · سُطورُ سِجِلّ {lines.Lines.Count}");

                // ‏(أ) لِلمُشرِف
                Assert.Equal("data", Attr(page, "data-meter-state"));
                Assert.Equal("absent", Attr(page, "data-meter-agg"));
                Assert.Equal("2", Attr(page, "data-meter-apps"));
                Assert.Equal($"{Other},{Store}", Attr(page, "data-meter-app"));
                Assert.Contains(Text("admin.meter.title"), page, StringComparison.Ordinal);
                Assert.Contains(Text("admin.meter.side_operation"), page, StringComparison.Ordinal);
                var deal = Text(MeterUnitCatalog.LabelKey("deal.create"));
                var store = Text(MeterUnitCatalog.LabelKey("store.create"));
                Assert.Equal(
                    $"[{Other} · رابِط 1 · deal.create=1«{deal} 1» · _unresolved 0 · _seed 0] "
                    + $"[{Store} · رابِط 1 · deal.create=6«{deal} 6» store.create=1«{store} 1» · _unresolved 1 · _seed 2]",
                    projection);
                Assert.DoesNotContain($"data-meter-app=\"{Idle}\"", page, StringComparison.Ordinal);
                Assert.DoesNotContain("data-meter-unit=\"presence.", page, StringComparison.Ordinal);

                // ‏(ب) لِلمَجهول
                Assert.DoesNotContain("data-meter-", anonymous, StringComparison.Ordinal);
                Assert.Empty(lines.Lines);
            }
            finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        }
        finally
        {
            ledger.Dispose();
            NpgsqlConnection.ClearAllPools();
            await ExecAsync(BaseCs, $"drop database if exists \"{b.Database}\" with (force)");
        }
    }
}
