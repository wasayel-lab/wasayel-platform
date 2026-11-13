using System.Globalization;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ G11 حَيّاً لِصَفحَةِ رائِدِ الأَعمال — مَتاجِرُه الآنَ بِجِوارِ ما نُسِبَ إلَيه لَحظَةَ الكِتابَة (‏2026-11-13، الموجَة ١٣ W13-6·٢١ — التَصميم §11 وCOR-11) ════
//
// **ما يُقاس**: (١) **مَتجَرٌ مُتَبَنّى تَحتَ مالِكِه الآن، وسُطورُ ما قَبلَ التَبَنّي تَبقى `_unowned`** — عَلى قاعِدَةٍ جَديدَةٍ بِتَركيبِ التَطبيق
// (`AddPlatformHost` + `AddCustomerMarketplaceTemplate`، فَالمُستَمِعُ ما يُسَجِّلُه القالِب): مَتجَرٌ يولَدُ يَتيماً ويُطلَبُ مِنه، ثُمَّ يَتَبَنّاه المُتَبَنّي الحَقيقِيّ
// (`StudioOwnershipSeeder.RunAsync`، ‏`StudioOwnershipSeeder.cs:47-58`) فَلا يَزيدُ الدَفتَرَ سَطراً ولا يَمَسُّ سَطراً، ثُمَّ يُطلَبُ مِنه بِحامِلٍ مَملوءٍ كَما يَملَؤُه
// الوَسيطُ مِن الوَثيقَةِ الطازَجَة (`MeterScope.OwnerOf`، والوَسيطُ نَفسُه مَقيسٌ في G5)؛ ومَتجَرٌ ثانٍ يولَدُ لَه ثُمَّ يُنقَلُ إلى غَيرِه. فَصَفحَتُه: مَتاجِرُه الآنَ
// (المُتَبَنّى والقائِم) كُلٌّ بِما كُتِبَ لَه فيه وَحدَه، والمَنقولُ تَحتَ «نُسِبَت إلَيه خارِجَ مَتاجِرِه الآن» بِوِلادَتِه، و`_platform` بِحِسابِه في الاستوديو؛
// وصَفحَةُ المَتجَرِ المُتَبَنّى: رائِدا أَعمالِه — المالِكُ الآنَ رابِطٌ إلى صَفحَتِه و`_unowned` نَصٌّ لا رابِط. (٢) **بِلا دَفتَر** ← «لا بَيانات» ولا مَتاجِرَ تُقرَأ.
// (٣) **المَجهولُ وغَيرُ المُشرِف** لا يُرسَمُ لَهُما رَقَمٌ **ولا يُقرَأُ لَهُما الدَفتَرُ ولا السِجِلّ** (قارِئٌ عَلى قاعِدَةٍ غائِبَةٍ يَكتُبُ سَطرَ [meter-page] لِأَيِّ قِراءَة).
// والصُفوفُ في `LiveMeterAdminProofTests` لِلصَفحَتَينِ الأُخرَيَينِ كَما هِيَ — هذا المِلَفُّ لا يَمَسُّها.

[Collection(LiveProofCollection.Name)]
public class LiveMeterOwnerProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string BaseCs => LiveDealConcurrencyProofTests.ConnectionString!;

    private const string Adopted = "mo-mutabanna";
    private const string Own = "mo-lahu";
    private const string Sold = "mo-manqul";

    // ─── القاعِدَةُ والسِجِلّ ─────────────────────────────────────────────────────────────────────────────────

    private sealed class Db : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Cs { get; init; }
        public required WebApplication App { get; init; }
        public IDocumentStore Store => App.Services.GetRequiredService<IDocumentStore>();

        /// <summary>قاعِدَةٌ جَديدَةٌ ومُضيفٌ بِتَركيبِ التَطبيقِ عَلَيها — لا يُقلِعُ الخادِم؛ المَخزَنُ وَحدَه يُستَعمَل (نَسَقُ <c>LiveMeterReconciliationProofTests</c>).</summary>
        public static async Task<Db> CreateAsync(string tag)
        {
            var b = new NpgsqlConnectionStringBuilder(BaseCs);
            var stem = (b.Database ?? "wasayel").ToLowerInvariant();
            if (stem.Length > 48) stem = stem[..48];
            b.Database = $"{stem}_{tag}_{Guid.NewGuid().ToString("N")[..8]}";
            await ExecAsync(BaseCs, $"create database \"{b.Database}\"");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Configuration["ConnectionStrings:Postgres"] = b.ConnectionString.TrimEnd(';') + ";Timeout=60;Command Timeout=120";
            builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
            ACommerce.Platform.Hosting.HostingExtensions.AddPlatformHost(builder);
            ACommerce.Templates.Customer.Marketplace.MarketplaceTemplateExtensions.AddCustomerMarketplaceTemplate(builder.Services);
            return new Db { Name = b.Database, Cs = b.ConnectionString, App = builder.Build() };
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
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

    /// <summary>
    /// <para>صَفَقاتٌ في مَتجَرٍ بِحامِلٍ مَملوءٍ كَما يَملَؤُه الوَسيطُ لِمَتجَرٍ بِلا أَدوار: المَتجَرُ ورائِدُ أَعمالِه و<c>_none</c>. والحامِلُ يُفتَحُ في هذه الدالَّةِ
    /// وَحدَها — قيمَةُ <c>AsyncLocal</c> لا تَعودُ إلى مُناديها — فَلا يَرِثُه حِفظٌ بَعدَها.</para>
    /// </summary>
    private static async Task DealsAsync(IDocumentStore store, string slug, string entrepreneur, int n)
    {
        MeterScope.Open().Fill(slug, entrepreneur, MeterSentinels.None);
        await using var s = store.LightweightSession(slug);
        for (var i = 0; i < n; i++)
            s.Store(new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid() });
        await s.SaveChangesAsync();
    }

    /// <summary>سُطورُ الدَفتَرِ لِمَتجَرٍ بِرائِدِ أَعمالِه، و«كُلُّها» — قَبلَ المُتَبَنّي وبَعدَه.</summary>
    private static async Task<string> LedgerAsync(string cs, string app)
        => $"كُلُّها {await TextAsync(cs, "select count(*) from meter.usage_entry")} · {app}: "
           + await TextAsync(cs, "select coalesce(string_agg(entrepreneur || ' ' || unit || ' ' || n, ' · ' order by entrepreneur, unit), '') from ("
                                 + $"select entrepreneur, unit, count(*) n from meter.usage_entry where app = '{app}' group by 1, 2) t");

    // ─── الصَفحَة ────────────────────────────────────────────────────────────────────────────────────────────

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

    private static Task<string> OwnerPageAsync(LiveTemplateDoorHost host, string? token, Guid id)
        => RenderAsync<MeterOwner>(host, token, $"/admin/meter/owners/{id:D}", new() { [nameof(MeterOwner.Id)] = id });

    private static Task<string> StorePageAsync(LiveTemplateDoorHost host, string? token, string app)
        => RenderAsync<MeterStore>(host, token, $"/admin/meter/stores/{app}", new() { [nameof(MeterStore.Slug)] = app });

    private static string Text(string key) => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key)!;

    private static string Attr(string html, string name)
        => string.Join(",", Regex.Matches(html, $@"\b{Regex.Escape(name)}=""(?<v>[^""]*)""").Select(m => m.Groups["v"].Value));

    /// <summary><c>data-entries</c> لِلعُنصُرِ الَّذي يَحمِلُ <paramref name="name"/>=<paramref name="value"/> — أَو فارِغٌ إن غاب.</summary>
    private static string EntriesOf(string html, string name, string value)
    {
        var m = Regex.Match(html, $@"\b{Regex.Escape(name)}=""{Regex.Escape(value)}"" data-entries=""(?<n>\d+)""");
        return m.Success ? m.Groups["n"].Value : "";
    }

    private static string Show(MeterOwnerReading r)
        => $"مَتاجِرُه الآن [{string.Join(", ", r.OwnedNow.Select(t => t.Id))}] · «{string.Join(" · ", r.Stores().Select(s => $"{s.App}{(s.OwnedNow ? "*" : "")} {s.Entries}"))}» = {r.Reading.Entries()}";

    // ─── (١) المُتَبَنّى تَحتَ مالِكِه الآن، وما قَبلَ التَبَنّي `_unowned` ─────────────────────────────────────────────

    [Fact]
    public async Task An_adopted_store_shows_under_its_current_owner_while_its_earlier_entries_stay_unowned()
    {
        if (!Enabled) return;
        await using var db = await Db.CreateAsync("mo");
        var store = db.Store;
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var lines = new Lines();
        var q = new MeterQueries(store, lines);

        // ‏(٢) قَبلَ أَن يَلِدَ الدَفتَر: «لا بَيانات»، ولا مَتاجِرَ تُقرَأ، ولا سَطرَ عُطل.
        var bare = await q.OwnerAsync(owner);
        output.WriteLine($"[G11·owner] قَبلَ الدَفتَر: {bare.Reading.State} · مَتاجِر {bare.OwnedNow.Count} · سُطورُ سِجِلّ {lines.All.Length}");
        Assert.Equal(MeterReadState.NoLedger, bare.Reading.State);
        Assert.Empty(bare.OwnedNow);
        Assert.Empty(lines.All);

        output.WriteLine($"[G11·owner] {db.Name}: {(await MeterBoot.EnsureAsync(store)).Line}");
        var t0 = DateTime.UtcNow.AddHours(-1);
        await using (var s = store.LightweightSession(StudioAuth.Tenant))
        {
            s.Insert(new StudioUser { Id = owner, IsPlatformAdmin = true, CreatedAt = t0 });
            s.Insert(new StudioUser { Id = other, CreatedAt = t0.AddMinutes(1) });
            await s.SaveChangesAsync();
        }
        await using (var g = store.LightweightSession())
        {
            g.Insert(new Tenant { Id = Adopted, Name = "مَتجَرٌ وُلِدَ يَتيماً", OwnerUserId = Guid.Empty, CreatedAt = t0 });
            g.Insert(new Tenant { Id = Own, Name = "مَتجَرٌ وُلِدَ لَه", OwnerUserId = owner, CreatedAt = t0.AddMinutes(1) });
            g.Insert(new Tenant { Id = Sold, Name = "مَتجَرٌ نُقِلَ عَنه", OwnerUserId = owner, CreatedAt = t0.AddMinutes(2) });
            await g.SaveChangesAsync();
        }
        await DealsAsync(store, Adopted, MeterScope.OwnerOf(Guid.Empty), 1);    // ‏يَتيمٌ: الوَسيطُ يَملَؤُه `_unowned`
        await using (var g = store.LightweightSession())                        // ‏النَقل: تَحديثٌ لا يُعَدّ
        {
            var sold = (await g.LoadAsync<Tenant>(Sold))!;
            sold.OwnerUserId = other;
            g.Store(sold);
            await g.SaveChangesAsync();
        }

        var before = await LedgerAsync(db.Cs, Adopted);
        await StudioOwnershipSeeder.RunAsync(store);
        var after = await LedgerAsync(db.Cs, Adopted);
        var adopted = (await new TenantDirectory(store).FindAsync(Adopted))!;
        output.WriteLine($"[G11·owner] قَبلَ المُتَبَنّي «{before}» · بَعدَه «{after}» · مالِكُ {Adopted} الآن {(adopted.OwnerUserId == owner ? "المُشرِفُ الأَقدَم" : adopted.OwnerUserId.ToString("D"))}");
        Assert.Equal(owner, adopted.OwnerUserId);
        Assert.Equal(before, after);                                              // ‏التَبَنّي لا يَزيدُ سَطراً ولا يَمَسُّ سَطراً
        Assert.Contains($"{MeterSentinels.Unowned} deal.create 1 · {MeterSentinels.Unowned} store.create 1", after, StringComparison.Ordinal);

        await DealsAsync(store, Adopted, MeterScope.OwnerOf(adopted.OwnerUserId), 3);   // ‏بَعدَ التَبَنّي: الوَسيطُ يَقرَأُ المالِكَ الطازَج
        await DealsAsync(store, Sold, MeterScope.OwnerOf(other), 2);

        var mine = await q.OwnerAsync(owner);
        var theirs = await q.OwnerAsync(other);
        var page = await q.StoreAsync(Adopted);
        var key = MeterScope.OwnerOf(owner);
        output.WriteLine($"[G11·owner] المالِك: {Show(mine)} · غَيرُ مَحسومٍ {mine.Reading.Unresolved()}");
        output.WriteLine($"[G11·owner] المَنقولُ إلَيه: {Show(theirs)}");
        output.WriteLine($"[G11·owner] صَفحَةُ {Adopted}: «{string.Join(" · ", page.By(r => r.Entrepreneur, Adopted).Select(v => $"{(v.Value == key ? "المالِكُ الآن" : v.Value)} {v.Entries}"))}» · سُطورُ سِجِلّ {lines.All.Length}");
        Assert.Equal(MeterReadState.Data, mine.Reading.State);
        Assert.Equal([Adopted, Own], mine.OwnedNow.Select(t => t.Id));
        Assert.Equal(
            new[] { (Adopted, true, 3L), (Own, true, 1L), (MeterSentinels.Platform, false, 1L), (Sold, false, 1L) },
            mine.Stores().Select(s => (s.App, s.OwnedNow, s.Entries)));
        Assert.Equal(6L, mine.Reading.Entries());
        Assert.Equal([Sold], theirs.OwnedNow.Select(t => t.Id));
        Assert.Equal(new[] { (Sold, true, 2L), (MeterSentinels.Platform, false, 1L) }, theirs.Stores().Select(s => (s.App, s.OwnedNow, s.Entries)));
        Assert.Equal(2L, page.By(r => r.Entrepreneur, Adopted).Single(v => v.Value == MeterSentinels.Unowned).Entries);
        Assert.Equal(3L, page.By(r => r.Entrepreneur, Adopted).Single(v => v.Value == key).Entries);
        Assert.Empty(lines.All);

        // ‏الصَفحَتانِ مُصَيَّرَتَينِ بِمُشرِف: صَفحَةُ رائِدِ الأَعمال، ومَدخَلُها خانَتُه في صَفحَةِ المَتجَر.
        var admin = default(Probe);
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddScoped(_ => new MeterQueries(store, lines)));
        try
        {
            admin = await UserAsync(host, consented: false, platformAdmin: true);
            var ownerHtml = await OwnerPageAsync(host, admin.Token, owner);
            var otherHtml = await OwnerPageAsync(host, admin.Token, other);
            var storeHtml = await StorePageAsync(host, admin.Token, Adopted);
            output.WriteLine($"[G11·owner] صَفحَةُ المالِك: الحالَة «{Attr(ownerHtml, "data-meter-state")}» · المُجَمِّع «{Attr(ownerHtml, "data-meter-agg")}» · مَتاجِرُه «{Attr(ownerHtml, "data-meter-owned")}» "
                             + $"«{Attr(ownerHtml, "data-meter-owned-store")}» ({Adopted} {EntriesOf(ownerHtml, "data-meter-owned-store", Adopted)} · {Own} {EntriesOf(ownerHtml, "data-meter-owned-store", Own)}) · "
                             + $"المَجموع «{Attr(ownerHtml, "data-meter-total")}» · خارِجَها «{Attr(ownerHtml, "data-meter-elsewhere")}» · لا يَملِكُه «{Attr(ownerHtml, "data-meter-not-owned")}»");
            output.WriteLine($"[G11·owner] صَفحَةُ المَنقولِ إلَيه: مَتاجِرُه «{Attr(otherHtml, "data-meter-owned-store")}» ({Sold} {EntriesOf(otherHtml, "data-meter-owned-store", Sold)}) · المَجموع «{Attr(otherHtml, "data-meter-total")}»");
            output.WriteLine($"[G11·owner] صَفحَةُ {Adopted}: رُوّادُ الأَعمال «{string.Join(", ", Attr(storeHtml, "data-meter-entrepreneur").Split(',').Select(v => v == key ? "المالِكُ الآن" : v))}» · "
                             + $"رَوابِطُ صَفحَةِ رائِدِ الأَعمال {Regex.Matches(storeHtml, "href=\"/admin/meter/owners/").Count}");
            Assert.Equal("data", Attr(ownerHtml, "data-meter-state"));
            Assert.Equal(key, Attr(ownerHtml, "data-meter-owner"));
            Assert.Equal("absent", Attr(ownerHtml, "data-meter-agg"));
            Assert.Equal("2", Attr(ownerHtml, "data-meter-owned"));
            Assert.Equal($"{Adopted},{Own}", Attr(ownerHtml, "data-meter-owned-store"));
            Assert.Equal("3", EntriesOf(ownerHtml, "data-meter-owned-store", Adopted));
            Assert.Equal("1", EntriesOf(ownerHtml, "data-meter-owned-store", Own));
            Assert.Equal("6", Attr(ownerHtml, "data-meter-total"));
            Assert.Equal($"{MeterSentinels.Platform},{Sold}", Attr(ownerHtml, "data-meter-elsewhere"));
            Assert.Equal(Sold, Attr(ownerHtml, "data-meter-not-owned"));
            Assert.Contains($"href=\"/admin/meter/stores/{Adopted}\"", ownerHtml, StringComparison.Ordinal);
            Assert.Equal(Sold, Attr(otherHtml, "data-meter-owned-store"));
            Assert.Equal("2", EntriesOf(otherHtml, "data-meter-owned-store", Sold));
            Assert.Equal("3", Attr(otherHtml, "data-meter-total"));
            Assert.Contains($"href=\"/admin/meter/owners/{key}\" data-meter-entrepreneur=\"{key}\" data-entries=\"3\"", storeHtml, StringComparison.Ordinal);
            Assert.Contains($"data-meter-entrepreneur=\"{MeterSentinels.Unowned}\" data-entries=\"2\"", storeHtml, StringComparison.Ordinal);
            Assert.DoesNotContain($"href=\"/admin/meter/owners/{MeterSentinels.Unowned}\"", storeHtml, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(storeHtml, "href=\"/admin/meter/owners/"));
        }
        finally { await CleanAsync(host, admin is null ? [] : [admin]); }
        Assert.Empty(lines.All);
    }

    // ─── (٣) المَجهولُ وغَيرُ المُشرِف: لا رَقَمَ يُرسَمُ ولا دَفتَرَ ولا سِجِلَّ يُقرَأ ─────────────────────────────────────────

    [Fact]
    public async Task Anonymous_and_non_admin_requests_to_the_owner_page_render_no_counts_and_read_nothing()
    {
        if (!Enabled) return;
        // ‏قارِئٌ عَلى قاعِدَةٍ لا وُجودَ لَها: أَيُّ قِراءَةٍ تَفشَلُ (‏3D000) فَتَكتُبُ سَطرَ [meter-page] — فَصِفرُ سُطورٍ = لَم يُقرَأ شَيء.
        var b = new NpgsqlConnectionStringBuilder(BaseCs) { Database = $"wasayel_absent_{Guid.NewGuid().ToString("N")[..8]}" };
        using var nowhere = DocumentStore.For(o => o.Connection(b.ConnectionString));
        var id = Guid.NewGuid();
        var lines = new Lines();
        var control = await new MeterQueries(nowhere, lines).OwnerAsync(id);
        output.WriteLine($"[G11·owner] الضابِطُ: قِراءَةٌ عَلى {b.Database} ⇒ {control.Reading.State} · مَتاجِر {control.OwnedNow.Count} · سُطور {lines.All.Length}: {string.Join(" | ", lines.All)}");
        Assert.Equal(MeterReadState.Error, control.Reading.State);
        Assert.Empty(control.OwnedNow);
        Assert.StartsWith($"Error {MeterQueries.LogTag} owner {id:D}: ", Assert.Single(lines.All), StringComparison.Ordinal);

        var reader = new Lines();
        var member = default(Probe);
        await using var host = LiveTemplateDoorHost.Build(services: s => s.AddScoped(_ => new MeterQueries(nowhere, reader)));
        try
        {
            member = await UserAsync(host, consented: true, platformAdmin: false);
            var rendered = new List<(string Who, string Html)>
            {
                ("مَجهول", await OwnerPageAsync(host, null, id)),
                ("غَيرُ مُشرِف", await OwnerPageAsync(host, member.Token, id)),
            };
            foreach (var (who, html) in rendered)
                output.WriteLine($"[G11·owner] {who}: data-meter «{Attr(html, "data-meter-state")}{Attr(html, "data-meter-owner")}{Attr(html, "data-meter-total")}» · "
                                 + $"دُخول {html.Contains("href=\"/studio/auth\"", StringComparison.Ordinal)} · مَنع {html.Contains(Text("admin.guard.forbidden_title"), StringComparison.Ordinal)}");
            Assert.All(rendered, r => Assert.DoesNotContain("data-meter-", r.Html, StringComparison.Ordinal));
            Assert.Contains(Text("admin.guard.need_auth_title"), rendered[0].Html, StringComparison.Ordinal);
            Assert.Contains(Text("admin.guard.forbidden_title"), rendered[1].Html, StringComparison.Ordinal);
        }
        finally { await CleanAsync(host, member is null ? [] : [member]); }
        output.WriteLine($"[G11·owner] صَفَحاتٌ مُصَيَّرَة 2 · قِراءاتٌ لِلدَفتَرِ والسِجِلّ {reader.All.Length}");
        Assert.Empty(reader.All);
    }
}
