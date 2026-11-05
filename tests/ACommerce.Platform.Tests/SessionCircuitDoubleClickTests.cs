using System.Diagnostics;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَقرَتانِ عَلى دائِرَةِ الوَكيلِ أَثناءَ سُؤالِ الجَلسَة — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 — مُراجَعَةُ `3d0b49a`، G2R1-1) ═════════════
//
// قَبلَ `3d0b49a` كانَ كُلُّ مُعالِجٍ يَقرَأُ `isWorking` ثُمَّ يَكتُبُه `true` بِلا انتِظارٍ بَينَهُما، فَنَقرَةٌ ثانِيَةٌ تَعودُ فَوراً. وصارَ سُؤالُ الجَلسَةِ
// (`!await StillPlatformAdminAsync()` — قِراءَةُ Postgres) داخِلَ شَرطِ الرُجوعِ **قَبلَ** أَن يُكتَبَ العَلَم: فَنَقرَةٌ تَصِلُ أَثناءَ تِلكَ القِراءَةِ تَرى
// `isWorking == false` وتَعبُر — نِداءُ نَموذَجٍ مَدفوعٌ مَرَّتَين، وأَداةٌ تُشَغَّلُ مَرَّتَين، وحِفظانِ لِلسِجِلِّ يَتَسابَقان.
//
// **كَيفَ يُقاسُ هُنا — والسِباقُ تَرتيبٌ حَتمِيٌّ لا حَظّ**: المُضيفُ المُرَكَّبُ وصَفحَتُه بِعَينِها (`CircuitProbe` و`LiveTemplateDoorHost`، كَما في
// `SessionCircuitTests.cs`)، واتِّصالٌ آخَرُ يَأخُذُ قُفلَ `ACCESS EXCLUSIVE` عَلى جَدوَلِ `StudioUser` (اسمُه مِن تَسجيلِ Marten لا بِاليَد) — فَسُؤالُ الجَلسَةِ
// يَنتَظِرُه. تُرسَلُ النَقرَةُ الأُولى، ويُنتَظَرُ حَتّى يَرى `pg_locks` سُؤالَها مُعَلَّقاً عَلى القُفل (الأَداةُ تُثبِتُ أَنّها تَقيسُ ما تَدَّعيه، القاعِدَة ١٠)؛
// ثُمَّ الثانِيَة: هَل عادَت قَبلَ أَن يَكتَمِلَ سُؤالُ الأُولى؟ وكَم سُؤالاً يَنتَظِرُ القُفل؟ ثُمَّ يُفَكُّ القُفلُ ويُعَدُّ ما جَرى: نِداءاتُ النَموذَج
// (`ProbeAgentBackends`)، وكِتاباتُ الأَداة (`set_branding` حَقيقِيَّةٌ عَلى مَتجَرٍ مُؤَقَّتٍ يَملِكُه المُشرِف — مُستَمِعُ Marten يَعُدُّ حِفظَ وَثيقَتِه)،
// وكِتاباتُ السِجِلّ، وحالَةُ الأَداة.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): نَقرَةٌ واحِدَةٌ بِجَلسَةٍ مَفتوحَةٍ تَفعَلُ ما تَفعَلُه — `OneClick` أَدناه لِكُلِّ مُعالِجٍ مِن الأَربَعَةِ في الصَفحَتَين —
// وسُؤالُ الأُولى مُعَلَّقٌ عَلى القُفلِ في كُلِّ تَشغيل. **وما يُطبَعُ ولا يُثَبَّت**: نَقرَتانِ أَثناءَ السُؤال — اليَومَ تَعبُرانِ مَعاً (والمُراجَعَةُ قاسَته
// «1,2,2,2,…» في خَمسَ عَشرَةَ دَورَةً بِلا قُفل)؛ ويَتَغَيَّرُ في كوميتِ التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

/// <summary>يَعُدُّ حِفظَ وَثيقَةِ المَتجَرِ المُؤَقَّتِ (أَثَرُ الأَداة) وحِفظَ سِجِلِّ الوَكيلِ أَو حَذفَه — مُنذُ <see cref="Armed"/>.</summary>
internal sealed class AgentCircuitWrites(string slug, string sessionId) : Marten.DocumentSessionListenerBase
{
    private int _tool, _session;
    private volatile bool _armed;

    public bool Armed { get => _armed; set => _armed = value; }
    public int ToolWrites => Volatile.Read(ref _tool);
    public int SessionWrites => Volatile.Read(ref _session);

    public override Task BeforeSaveChangesAsync(Marten.IDocumentSession session, CancellationToken token)
    {
        if (!_armed) return Task.CompletedTask;
        var changes = session.PendingChanges;
        if (changes.AllChangedFor<ACommerce.Kit.Tenants.Tenant>().Any(t => t.Id == slug)) Interlocked.Increment(ref _tool);
        if (changes.AllChangedFor<AgentSession>().Any(a => a.Id == sessionId)
            || changes.DeletionsFor<AgentSession>().Any(d => Equals(d.Id, sessionId)))
            Interlocked.Increment(ref _session);
        return Task.CompletedTask;
    }
}

/// <summary>ما جَرى لِنَقرَةٍ أَو نَقرَتَين: نِداءاتُ النَموذَج، وكِتاباتُ الأَداةِ والسِجِلّ، وحالَةُ الأَداةِ في السِجِلِّ بَعدَها، وهَل مُحِي؛ وهَل كانَ سُؤالُ الأُولى
/// مُعَلَّقاً عَلى القُفلِ حينَ أُرسِلَت الثانِيَة، وهَل عادَت الثانِيَةُ قَبلَ أَن يَكتَمِل، وكَم سُؤالاً انتَظَرَ القُفل؛ والتَنقُّلُ والبابُ والأَخطاء.</summary>
internal sealed record DoubleClickOutcome(int Clicks, int ModelCalls, int ToolWrites, int SessionWrites, string? Status, bool Cleared,
    bool FirstHeld, bool SecondReturnedFirst, int Waiting, IReadOnlyList<string> Went, bool DoorOpen, IReadOnlyList<string> Errors)
{
    /// <summary>ما يَفعَلُه المُعالِجُ — بِلا ما يَخُصُّ السِباق.</summary>
    public (int ModelCalls, int ToolWrites, int SessionWrites, string? Status, bool Cleared) Acted => (ModelCalls, ToolWrites, SessionWrites, Status, Cleared);

    public override string ToString()
        => $"نَقَرات {Clicks} · نِداءاتُ النَموذَج {ModelCalls} · كِتاباتُ الأَداة {ToolWrites} · كِتاباتُ السِجِلّ {SessionWrites} · الأَداة {Status ?? "—"} · "
           + $"مُحِيَ السِجِلّ {Cleared} · سُؤالُ الأُولى مُعَلَّقٌ عَلى القُفل {FirstHeld} · الثانِيَةُ عادَت قَبلَه {SecondReturnedFirst} · "
           + $"أَسئِلَةٌ تَنتَظِرُ القُفل {Waiting} · التَنقُّل {(Went.Count == 0 ? "لا شَيء" : string.Join(" ، ", Went))} · البابُ مَفتوح {DoorOpen} · أَخطاء {Errors.Count}"
           + (Errors.Count == 0 ? "" : " (" + string.Join(" | ", Errors) + ")");
}

[Collection(LiveProofCollection.Name)]
public class LiveSessionCircuitDoubleClickProofTests(ITestOutputHelper output)
{
    internal const string ToolId = "g2d-tool";

    /// <summary>ما تَفعَلُه نَقرَةٌ واحِدَةٌ بِجَلسَةٍ مَفتوحَة — مَقيسٌ اليَومَ ويُثَبَّتُ هُنا: «أَرسِل» نِداءُ نَموذَجٍ وحِفظُ سِجِلّ؛ «طَبِّق» تَكتُبُ المَتجَرَ مَرَّةً
    /// ثُمَّ حالَةُ الأَداةِ وما بَعدَها (حِفظان) ونِداءُ نَموذَج؛ «ارفُض» حالَةُ الأَداةِ وما بَعدَها ونِداءُ نَموذَج؛ «امسَح» حَذفُ السِجِلِّ وَحدَه.</summary>
    internal static (int ModelCalls, int ToolWrites, int SessionWrites, string? Status, bool Cleared) OneClick(string handler) => handler switch
    {
        "SendAsync" => (1, 0, 1, "pending", false),
        "ApplyAsync" => (1, 1, 2, "applied", false),
        "RejectAsync" => (1, 0, 2, "rejected", false),
        "ResetAsync" => (0, 0, 1, null, true),
        _ => throw new ArgumentOutOfRangeException(nameof(handler), handler, "مُعالِجٌ غَيرُ مَعروف — الأَداةُ عَمياء."),
    };

    /// <summary>
    /// <para>يُرَكِّبُ الصَفحَةَ (<paramref name="page"/>) لِمُشرِفِ مَنَصَّةٍ مُؤَقَّتٍ في <c>_studio</c>، يَملِكُ مَتجَراً مُؤَقَّتاً، وفي سِجِلِّه أَداةُ <c>set_branding</c>
    /// مُعَلَّقَةٌ صالِحَةٌ لِذلِكَ المَتجَر؛ ثُمَّ يَأخُذُ القُفلَ عَلى جَدوَلِ <c>StudioUser</c>، ويُرسِلُ <paramref name="clicks"/> نَقرَةً عَلى <paramref name="handler"/>
    /// (الثانِيَةُ بَعدَ أَن يُرى سُؤالُ الأُولى مُعَلَّقاً)، ثُمَّ يَفُكُّه ويَنتَظِرُها ويُعيدُ ما جَرى. والسِجِلُّ المُشتَرَكُ (<c>agent-default</c>) يُعادُ كَما كان،
    /// والحِسابُ والمَتجَرُ وسُطورُ قِياسِ المِسبارِ تُمحى.</para>
    /// </summary>
    internal static async Task<DoubleClickOutcome> RunAsync(string page, string handler, int clicks)
    {
        var agents = new ProbeAgentBackends();
        var admin = new StudioUser { Id = Guid.NewGuid(), Phone = "zz-g2d-" + Guid.NewGuid().ToString("N")[..8], IsPlatformAdmin = true, FullName = "مُشرِفُ مِسبارِ النَقرَتَين" };
        var slug = "zz-g2d-" + Guid.NewGuid().ToString("N")[..8];
        var sessionId = page == LiveSessionCircuitProofTests.StudioPage ? "scope:studio-" + admin.Id.ToString("N") : AgentSession.SessionId;
        var writes = new AgentCircuitWrites(slug, sessionId);
        await using var host = LiveTemplateDoorHost.Build(o => o.Listeners.Add(writes), s =>
        {
            s.AddSingleton<IAgentBackendProvider>(agents);
            s.AddScoped<NavigationManager, ProbeNavigation>();
        });
        var store = host.Store;
        AgentSession? before;
        await using (var q = store.QuerySession("_admin")) before = await q.LoadAsync<AgentSession>(sessionId);
        try
        {
            await using (var s = store.LightweightSession(StudioAuth.Tenant)) { s.Store(admin); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession())
            {
                s.Store(new ACommerce.Kit.Tenants.Tenant { Id = slug, Name = "مَتجَرُ مِسبارِ النَقرَتَين", OwnerUserId = admin.Id });
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession("_admin"))
            {
                s.Store(new AgentSession
                {
                    Id = sessionId,
                    Turns =
                    {
                        new AgentTurn
                        {
                            Role = "assistant", Text = "أَداةٌ مُعَلَّقَة",
                            Tool = new AgentToolCall { Id = ToolId, Name = "set_branding", InputJson = $$"""{"slug":"{{slug}}","tagline":"شِعارُ المِسبار"}""" },
                        },
                    },
                });
                await s.SaveChangesAsync();
            }

            await using var scope = host.Services.CreateAsyncScope();
            var first = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            first.Request.Path = page == LiveSessionCircuitProofTests.StudioPage ? "/studio/agent" : "/admin/agent";
            first.Request.Headers.Cookie = $"{StudioAuth.CookieName}={AuthHandlers.MakeToken(admin.Id, StudioAuth.Tenant, admin.SessionVersion)}";
            await using var renderer = new CircuitProbe(scope.ServiceProvider);
            var type = page == LiveSessionCircuitProofTests.StudioPage
                ? typeof(ACommerce.Templates.Customer.Marketplace.Components.Pages.StudioAgent)
                : typeof(ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin.AgentChat);
            var component = await renderer.MountAsync(type, scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(), first);
            var doorOpen = page == LiveSessionCircuitProofTests.StudioPage
                ? CircuitProbe.Field<Guid>(component, "_userId") == admin.Id && CircuitProbe.Field<bool>(component, "_platformAdmin")
                : component is ACommerce.Templates.Customer.Marketplace.Components.Pages.Admin.AgentChatPanel { AdminUserId: { } id } && id == admin.Id;
            CircuitProbe.Set(component, "newMessage", "رِسالَةُ مِسبارِ النَقرَتَين");
            object[] args = handler is "ApplyAsync" or "RejectAsync" ? [ToolId] : [];

            // جَدوَلُ الحِسابِ كَما يَبنيه Marten مِن تَسجيلِه — الاسمُ لا يُكتَبُ بِاليَد.
            var options = Assert.IsType<Marten.StoreOptions>(store.Options, exactMatch: false);
            var table = Assert.Single(options.Storage.FindFeature(typeof(StudioUser)).Objects.OfType<Weasel.Postgresql.Tables.Table>()).Identifier.QualifiedName;
            var callsBefore = agents.Calls;
            writes.Armed = true;
            var dispatched = new List<Task>();
            bool firstHeld, secondReturnedFirst = false;
            int waiting;
            await using (var lockConn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString))
            {
                await lockConn.OpenAsync();
                await using var tx = await lockConn.BeginTransactionAsync();
                await using (var take = new NpgsqlCommand($"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE", lockConn, tx)) await take.ExecuteNonQueryAsync();

                dispatched.Add(renderer.ClickAsync(component, handler, args));
                waiting = await WaitingAsync(lockConn, tx, table, atLeast: 1);
                firstHeld = waiting >= 1 && !dispatched[0].IsCompleted;
                if (clicks > 1)
                {
                    dispatched.Add(renderer.ClickAsync(component, handler, args));
                    secondReturnedFirst = dispatched[1].IsCompleted && !dispatched[0].IsCompleted;
                    if (!dispatched[1].IsCompleted) waiting = await WaitingAsync(lockConn, tx, table, atLeast: 2);
                }
                await tx.RollbackAsync();
            }

            var errors = new List<string>();
            foreach (var t in dispatched)
            {
                try { await t; }
                catch (Exception e) { errors.Add($"{handler}: {e.GetType().Name}: {e.Message}"); }
            }
            writes.Armed = false;

            AgentSession? after;
            await using (var q = store.QuerySession("_admin")) after = await q.LoadAsync<AgentSession>(sessionId);
            var nav = (ProbeNavigation)scope.ServiceProvider.GetRequiredService<NavigationManager>();
            return new DoubleClickOutcome(clicks, agents.Calls - callsBefore, writes.ToolWrites, writes.SessionWrites,
                after?.Turns.LastOrDefault(t => t.Tool?.Id == ToolId)?.Tool?.Status, after is null,
                firstHeld, secondReturnedFirst, waiting, nav.Went.ToList(), doorOpen, errors);
        }
        finally
        {
            await using (var s = store.LightweightSession("_admin"))
            {
                if (before is null) s.Delete<AgentSession>(sessionId); else s.Store(before);
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession()) { s.Delete<ACommerce.Kit.Tenants.Tenant>(slug); await s.SaveChangesAsync(); }
            await using (var s = store.LightweightSession(StudioAuth.Tenant))
            {
                s.Delete<StudioUser>(admin.Id);
                s.DeleteWhere<ACommerce.Templates.Customer.Marketplace.Services.Metering.ModelCallRecord>(r => r.Provider == ProbeAgentBackends.Name);
                await s.SaveChangesAsync();
            }
        }
    }

    /// <summary>كَم سُؤالاً يَنتَظِرُ القُفلَ عَلى <paramref name="table"/> في هذِه القاعِدَة — يُعادُ السُؤالُ حَتّى <paramref name="atLeast"/> أَو ثَلاثينَ ثانِيَة.</summary>
    private static async Task<int> WaitingAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string table, int atLeast)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            await using var cmd = new NpgsqlCommand(
                "select count(*) from pg_locks where relation = to_regclass(@t) and not granted "
                + "and database = (select oid from pg_database where datname = current_database())", conn, tx);
            cmd.Parameters.AddWithValue("t", table);
            var n = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (n >= atLeast || clock.Elapsed > TimeSpan.FromSeconds(30)) return n;
            await Task.Delay(20);
        }
    }

    /// <summary><b>ما يَبقى</b>: نَقرَةٌ واحِدَةٌ تَفعَلُ <see cref="OneClick"/> بِلا تَنقُّلٍ ولا خَطَإ، وسُؤالُها مُعَلَّقٌ عَلى القُفلِ حينَ يُرى (الأَداةُ تَرى ما تَقيس).
    /// <b>وما يُطبَعُ ولا يُثَبَّت</b>: نَقرَتانِ أَثناءَ سُؤالِ الأُولى — ويُثَبَّتُ مِنهُما ما لا يَتَغَيَّر: سُؤالُ الأُولى مُعَلَّقٌ حينَ أُرسِلَت الثانِيَة، والبابُ مَفتوح.</summary>
    [Theory]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "SendAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "ApplyAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "RejectAsync")]
    [InlineData(LiveSessionCircuitProofTests.StudioPage, "ResetAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "SendAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "ApplyAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "RejectAsync")]
    [InlineData(LiveSessionCircuitProofTests.AdminPage, "ResetAsync")]
    public async Task One_click_acts_once_and_a_second_click_during_the_session_check_is_measured_on_Postgres(string page, string handler)
    {
        if (!LiveSessionCircuitProofTests.Enabled) return;
        var one = await RunAsync(page, handler, clicks: 1);
        output.WriteLine($"{page} · {handler} · نَقرَةٌ واحِدَة ⇒ {one}");
        var two = await RunAsync(page, handler, clicks: 2);
        output.WriteLine($"{page} · {handler} · نَقرَتانِ أَثناءَ السُؤال ⇒ {two}");

        Assert.Empty(one.Errors);
        Assert.True(one.DoorOpen);
        Assert.True(one.FirstHeld);
        Assert.Equal(OneClick(handler), one.Acted);
        Assert.Empty(one.Went);

        Assert.True(two.DoorOpen);
        Assert.True(two.FirstHeld);
    }
}
