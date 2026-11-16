using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Notifications;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ دَمجُ مَجموعَةٍ عَلى قِراءَةٍ جَديدَةٍ بِخُطَّةٍ مِن قِراءَةٍ قَديمَة — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ Carry — G2R3-1) ══════════════════════
//
// **صِنفُ العَطَب** (عادَ في G2 جَولَةً بَعدَ جَولَة): خُطَّةٌ تُبنى مِن قِراءَةٍ وتُطَبَّقُ بَعدَ قِراءَةٍ أُخرى ولا تُسأَلُ داخِلَ الكِتابَة. أَغلَقَه `e8a2c09` في
// `RenameOnceAsync` (‏`DuplicatePhones.StillApplies`)، وبَقِيَ عَلى طَريقِ الكِتابَةِ الآخَرِ في السكربت: `ApplyOnceAsync` يُحَمِّلُ الباقي وكُلَّ مُندَمِجٍ ثُمَّ يَكتُبُ
// بِلا سُؤال — `Inherit` و`Erase(loser)` و`survivor.Phone = g.Canonical`. والسكربتُ يُعيدُ القِياسَ (`ScanAsync`) قَبلَه مُباشَرَةً، لكِنَّ القِياسَ يَعُدُّ الصُفوفَ
// المُحيلَةَ ولا يَسأَلُ `DeletedAt` ولا الجَوّال. ونافِذَتُه في `--apply` مِن قِراءَةِ الخُطَّةِ أَوَّلَ السكربتِ إلى `ApplyAsync` ذلِكَ المَتجَر — قِياسُ كُلِّ مَتجَرٍ
// ودَمجُ ما قَبلَه — والإعادَةُ عَلى `ConcurrencyException` تُوَسِّعُها كَما وَسَّعَها `2158fed` لِإعادَةِ الكَتب. ومِثلُه حَقيقَتانِ مِن القِياسِ يَأخُذُهُما الدَمجُ
// كَما قيسَتا ولا يَقرَؤُهُما في مُعامَلَتِه: «لِلباقي سَلَّة؟» (`SurvivorCart`) و«عَدَدُ المَفاتيح» (`Keys`، ولا يُسأَلُ جَدوَلُها إلّا إن عَدَّ فيه مِفتاحاً).
//
// **كَيفَ يُقاسُ هُنا**:
// (أ) عالَمُ وَثائِق (`DocWorld`): الحِسابانِ وَثيقَتانِ في الذاكِرَة، والقِياسُ مَكتوبٌ بِاليَدِ بِصَفٍّ واحِدٍ يُعادُ تَوجيهُه — فَأَوَّلُ كِتابَةٍ في الدَمجِ
//     `QueueSqlCommand`، والعالَمُ لا يُنَفِّذُها (يَرمي `NotSupportedException` بِاسمِها): **بُلوغُها هُوَ المَقيس**. والكاتِبُ المُوازي «بَعدَ القِراءَةِ» يُحقَنُ
//     عِندَ أَوَّلِ `QueueSqlCommand` (يَضَعُ نُسخَتَه ويَرمي `ConcurrencyException`) — العالَمُ لا يَبلُغُ الحِفظ، فَآخِرُ ما يَبلُغُه هُوَ مَوضِعُ التَعارُض.
// (ب) حَيّاً عَلى Postgres بِمَخزَنِ السكربتِ (`LiveDuplicatePhonesRenameProofTests.ScriptStore`) وشَكلِ السكربتِ بِعَينِه: الخُطَّةُ مِن قِراءَةِ الحِساباتِ
//     (`Build`، السكربت :108-111)، والكاتِبُ المُوازي «قَبلَ القِياس» أَو «بَينَ قِراءَةِ الدَمجِ وحِفظِه» (مُستَمِعُ جَلسَةٍ يَكتُبُ في جَلسَةٍ أُخرى قَبلَ حِفظِ الدَمج،
//     فَيَرُدُّه التَزامُنُ التَفاؤُلِيُّ ويُعيدُه `ApplyAsync`)، ثُمَّ `ScanAsync` كَما يُعيدُه السكربت (:165)، ثُمَّ `ApplyAsync`. ولِلمُندَمِجِ إشعارٌ يُعادُ تَوجيهُه —
//     فَيُرى أَينَ ذَهَبَت بَياناتُه. ولا يُطبَعُ جَوّال: صيغَتُه وَحدَها.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): المَجموعَةُ الحَيَّةُ — ومَعَها كاتِبٌ لا يَمَسُّ خُطَّتَها (إصدارُ الجَلسَة) — تُدمَجُ كامِلَةً؛ وتَعارُضانِ يَرمِيانِ بِلا كِتابَة؛
// والمَمحُوُّ يَبقى مَمحُوّاً، والحِسابُ الَّذي غَيَّرَ رَقَمَه لا يُمحى باقِيه؛ والسَلَّةُ والمِفتاحُ: لا رَمي، والمُندَمِجُ مَمحُوّ، والسَلَّةُ لِلباقي والمِفتاحُ قائِم.
// وفي العالَم: لا يُكتَبُ حَرفٌ ولا يُحفَظ، والغائِبُ يَرمي `InvalidOperationException`. **وما يُطبَعُ ولا يُثَبَّت**: ما رُمِيَ، وحالُ الباقي والمُندَمِجِ بَعدَه،
// ولِمَن الإشعارُ والسَلَّةُ والمِفتاح، وكَم مَرَّةً بُلِغَت أَوَّلُ كِتابَة — ويَتَغَيَّرُ في كوميتِ التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

public class DuplicatePhonesMergeStalePlanTests(ITestOutputHelper output)
{
    /// <summary>ما يَكتُبُه الكاتِبُ المُوازي — <c>Unrelated</c> لا يَمَسُّ خُطَّةَ المَجموعَة (إصدارُ الجَلسَة)، و<c>…Gone</c> وَثيقَةٌ حُذِفَت (العالَمُ وَحدَه).</summary>
    public enum Change { None, Unrelated, SurvivorErased, SurvivorOtherPhone, LoserErased, LoserOtherPhone, SurvivorGone, LoserGone }

    /// <summary>مَتى: قَبلَ إعادَةِ القِياس، أَو بَينَ قِراءَةِ الدَمجِ وحِفظِه مَرَّة، أَو في كُلِّ مُحاوَلَة.</summary>
    public enum When { BeforeRescan, BetweenReadAndSave, EveryAttempt }

    internal const string Typed = "0500000791";
    internal const string To = "+966500000791";
    internal const string Other = "+966500000792";
    internal static readonly DateTime At = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>صيغَةُ الجَوّالِ المُخَزَّنِ لا الجَوّالُ نَفسُه (<c>TestOutputPersonalDataTests</c>).</summary>
    internal static string ShapeOf(string? phone)
        => string.IsNullOrEmpty(phone) ? "فارِغ" : phone == To ? "مُوَحَّدُ الخُطَّة" : phone == Other ? "ما كَتَبَه الآخَر" : phone == Typed ? "كَما كان" : "غَيرُها";

    /// <summary>اسمُ ما رُمِيَ — ورَمزُ <c>InvalidOperationException</c> (ما قَبلَ النُقطَتَين).</summary>
    internal static string NameOf(Exception? e)
        => e is null ? "" : e is InvalidOperationException ? $"{e.GetType().Name}({e.Message.Split(':')[0]})" : e.GetType().Name;

    internal static User Survivor(Guid id, string tenant) => new() { Id = id, TenantSlug = tenant, Phone = Typed, CreatedAt = At.AddDays(-10) };
    internal static User Loser(Guid id, string tenant) => new() { Id = id, TenantSlug = tenant, Phone = To, CreatedAt = At.AddDays(-2) };

    /// <summary>الكاتِبُ المُوازي عَلى نُسخَةٍ مِن الحِساب — يُعيدُ مَن مَسَّ (الباقي أَم المُندَمِج).</summary>
    internal static bool Apply(Change change, User survivor, User loser)
    {
        switch (change)
        {
            case Change.Unrelated: loser.SessionVersion++; return false;
            case Change.SurvivorErased: AccountDeletion.Erase(survivor, At); return true;
            case Change.SurvivorOtherPhone: (survivor.Phone, survivor.UpdatedAt) = (Other, At); return true;
            case Change.LoserErased: AccountDeletion.Erase(loser, At); return false;
            case Change.LoserOtherPhone: (loser.Phone, loser.UpdatedAt) = (Other, At); return false;
            default: throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    /// <summary>ما بَعدَ الدَمجِ في العالَم: ما رُمِيَ، وكَم مَرَّةً بُلِغَت أَوَّلُ كِتابَة (<c>QueueSqlCommand</c>)، وما كُتِبَ وما حُفِظ.</summary>
    internal sealed record WorldOutcome(string Thrown, int WriteReached, int Stored, int Saves)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · بَلَغَ أَوَّلَ كِتابَة {WriteReached} · كُتِب {Stored} · الحِفظ {Saves}";
    }

    /// <summary>(أ) في عالَمِ الوَثائِق: الخُطَّةُ مِن الحِسابَين، والقِياسُ بِصَفٍّ واحِدٍ يُعادُ تَوجيهُه، والكاتِبُ المُوازي بِحَسَبِ <paramref name="when"/>.</summary>
    internal static async Task<WorldOutcome> InWorldAsync(Change change, When when)
    {
        const string tenant = "zz-g2r3";
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var world = new DocWorld().Put(Survivor(a, tenant)).Put(Loser(b, tenant));
        var group = Assert.Single(DuplicatePhones.Build(new[]
        {
            new DuplicatePhones.Account(a, Typed, At.AddDays(-10), false),
            new DuplicatePhones.Account(b, To, At.AddDays(-2), false),
        }).Groups);
        var scan = new DuplicatePhones.Scan(group, [new DuplicatePhones.Repoint(b, "mt_doc_notification", 1)], [], 0, false, 0, 0, []);
        void Write()
        {
            var (s, l) = (Survivor(a, tenant), Loser(b, tenant));
            world.Put(Apply(change, s, l) ? s : l);
        }
        if (change is Change.SurvivorGone) world.Docs.Remove(DocWorld.KeyOf(typeof(User), a.ToString()));
        else if (change is Change.LoserGone) world.Docs.Remove(DocWorld.KeyOf(typeof(User), b.ToString()));
        else if (change is not Change.None && when == When.BeforeRescan) Write();
        else if (change is not Change.None)
        {
            var conflicts = 0;
            world.BeforeMember = m =>
            {
                if (m != "QueueSqlCommand" || (++conflicts > 1 && when != When.EveryAttempt)) return;
                Write();
                throw new JasperFx.ConcurrencyException(typeof(User), b);
            };
        }
        Exception? thrown = null;
        try { await DuplicatePhones.ApplyAsync(world.Store, tenant, scan, At); }
        catch (Exception e) { thrown = e; }
        return new(NameOf(thrown), world.Members.Count(m => m == "QueueSqlCommand"), world.Stored.Count, world.SaveCalls);
    }

    /// <summary><b>ما يَبقى</b> في العالَم: لا يُكتَبُ حَرفٌ ولا يُحفَظ؛ والغائِبُ يَرمي <c>InvalidOperationException</c>؛ والمَجموعَةُ الحَيَّةُ — ومَعَها كاتِبٌ لا يَمَسُّ
    /// خُطَّتَها — تَبلُغُ أَوَّلَ كِتابَة، وتَعارُضانِ يَرمِيانِ <c>ConcurrencyException</c>. <b>وما يُطبَع</b>: ما رُمِيَ وكَم مَرَّةً بُلِغَت الكِتابَةُ في الصُفوفِ المُتَغَيِّرَة.</summary>
    internal static void AssertWhatStays(WorldOutcome o, Change change, When when)
    {
        Assert.Equal(0, o.Stored);
        Assert.Equal(0, o.Saves);
        if (change is Change.SurvivorGone or Change.LoserGone) Assert.StartsWith(nameof(InvalidOperationException), o.Thrown, StringComparison.Ordinal);
        if (change is Change.None) Assert.Equal((nameof(NotSupportedException), 1), (o.Thrown, o.WriteReached));
        if (change is Change.Unrelated && when == When.BetweenReadAndSave) Assert.Equal((nameof(NotSupportedException), 2), (o.Thrown, o.WriteReached));
        if (change is Change.Unrelated && when == When.EveryAttempt) Assert.Equal((nameof(JasperFx.ConcurrencyException), 2), (o.Thrown, o.WriteReached));
    }

    public static TheoryData<Change, When> WorldRows() => new()
    {
        { Change.None, When.BeforeRescan },
        { Change.Unrelated, When.BetweenReadAndSave },
        { Change.Unrelated, When.EveryAttempt },
        { Change.SurvivorErased, When.BeforeRescan },
        { Change.SurvivorErased, When.BetweenReadAndSave },
        { Change.SurvivorOtherPhone, When.BeforeRescan },
        { Change.SurvivorOtherPhone, When.BetweenReadAndSave },
        { Change.LoserErased, When.BeforeRescan },
        { Change.LoserErased, When.BetweenReadAndSave },
        { Change.LoserOtherPhone, When.BeforeRescan },
        { Change.LoserOtherPhone, When.BetweenReadAndSave },
        { Change.SurvivorGone, When.BeforeRescan },
        { Change.LoserGone, When.BeforeRescan },
    };

    [Theory]
    [MemberData(nameof(WorldRows))]
    public async Task A_group_whose_account_changed_after_the_plan_is_measured_in_the_document_world(Change change, When when)
    {
        var outcome = await InWorldAsync(change, when);
        output.WriteLine($"{change} · {when} ⇒ {outcome}");
        AssertWhatStays(outcome, change, when);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesMergeStalePlanProofTests(ITestOutputHelper output)
{
    /// <summary>
    /// <para>يُشَغِّلُ الكاتِبَ المُوازِيَ قَبلَ حِفظِ الدَمج — بَعدَ قِراءَتِه، فَيَرُدُّ التَزامُنُ التَفاؤُلِيُّ الحِفظَ ويُعيدُه <c>ApplyAsync</c>. حِفظُ الدَمجِ يُعرَفُ بِما يَحمِلُه:
    /// حِسابٌ مَمحُوٌّ (المُندَمِج). ويَعمَلُ مَرَّةً أَو في كُلِّ مُحاوَلَة، ولا يَعمَلُ عَلى حِفظِ الكاتِبِ نَفسِه.</para>
    /// </summary>
    internal sealed class BeforeMergeSave : DocumentSessionListenerBase
    {
        private Func<Task>? _write;
        private bool _every;
        private bool _running;

        public void Arm(Func<Task> write, bool every) => (_write, _every) = (write, every);

        public void Disarm() => _write = null;

        public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            if (!_running && _write is { } write && session.PendingChanges.AllChangedFor<User>().Any(u => u.DeletedAt is not null))
            {
                if (!_every) _write = null;
                _running = true;
                try { await write(); }
                finally { _running = false; }
            }
        }
    }

    /// <summary>ما بَعدَ الدَمجِ عَلى Postgres: ما رُمِيَ، وحالُ الباقي والمُندَمِجِ وصيغَةُ جَوّالَيهِما، وإصدارُ جَلسَةِ المُندَمِج، ولِمَن إشعارُ المُندَمِج.</summary>
    internal sealed record LiveOutcome(string Thrown, bool SurvivorDeleted, string SurvivorPhone, bool LoserDeleted, string LoserPhone, int LoserSessionVersion,
        string NoticeOwner)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · الباقي مَحذوف {SurvivorDeleted} الجَوّال {DuplicatePhonesMergeStalePlanTests.ShapeOf(SurvivorPhone)} · "
               + $"المُندَمِج مَحذوف {LoserDeleted} الجَوّال {DuplicatePhonesMergeStalePlanTests.ShapeOf(LoserPhone)} إصدارُ الجَلسَة {LoserSessionVersion} · إشعارُ المُندَمِجِ لِـ{NoticeOwner}";
    }

    /// <summary>كُلُّ صَفٍّ لِلمَتجَرِ المُؤَقَّتِ في جَداوِلِ المُستَأجِرِ ومَجاري الأَحداث.</summary>
    internal static async Task CleanAsync(string slug)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        foreach (var t in (await DuplicatePhones.TenantTablesAsync(conn)).Select(t => t.Name).Concat(["mt_events", "mt_streams"]))
        {
            await using var d = new NpgsqlCommand($"delete from platform.{t} where tenant_id = @t", conn);
            d.Parameters.AddWithValue("t", slug);
            await d.ExecuteNonQueryAsync();
        }
    }

    /// <summary>الخُطَّةُ كَما يَقرَؤُها السكربتُ (:108-111) — مَجموعَةٌ واحِدَة.</summary>
    internal static async Task<DuplicatePhones.Group> PlanAsync(IDocumentStore store, string slug)
    {
        List<DuplicatePhones.Account> members;
        await using (var q = store.QuerySession(slug))
            members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
        return Assert.Single(DuplicatePhones.Build(members).Groups);
    }

    /// <summary>إعادَةُ القِياسِ كَما يُعيدُها السكربتُ قَبلَ الدَمج (:165).</summary>
    internal static async Task<DuplicatePhones.Scan> RescanAsync(string slug, DuplicatePhones.Group group)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        return await DuplicatePhones.ScanAsync(conn, slug, group, await DuplicatePhones.TenantTablesAsync(conn));
    }

    /// <summary>(ب) حَيّاً: الباقي (<c>05…</c>، أَقدَم) والمُندَمِج (<c>+966…</c>) ولِلمُندَمِجِ إشعار؛ الخُطَّة، والكاتِبُ المُوازي بِحَسَبِ <paramref name="when"/>،
    /// وإعادَةُ القِياس، ثُمَّ <c>ApplyAsync</c>. والمَتجَرُ يُمسَحُ بَعدَه.</summary>
    internal static async Task<LiveOutcome> LiveAsync(DuplicatePhonesMergeStalePlanTests.Change change, DuplicatePhonesMergeStalePlanTests.When when)
    {
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        var hook = new BeforeMergeSave();
        store.Options.Listeners.Add(hook);
        var slug = "zz-g2r3-" + Guid.NewGuid().ToString("N")[..8];
        var (a, b, notice) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(DuplicatePhonesMergeStalePlanTests.Survivor(a, slug));
                s.Store(DuplicatePhonesMergeStalePlanTests.Loser(b, slug));
                s.Store(new Notification { Id = notice, UserId = b, Title = "إشعار", Body = "—" });
                await s.SaveChangesAsync();
            }
            var group = await PlanAsync(store, slug);
            async Task WriteAsync()
            {
                await using var p = store.LightweightSession(slug);
                var (su, lo) = ((await p.LoadAsync<User>(a))!, (await p.LoadAsync<User>(b))!);
                p.Store(DuplicatePhonesMergeStalePlanTests.Apply(change, su, lo) ? su : lo);
                await p.SaveChangesAsync();
            }
            if (change != DuplicatePhonesMergeStalePlanTests.Change.None && when == DuplicatePhonesMergeStalePlanTests.When.BeforeRescan) await WriteAsync();
            var scan = await RescanAsync(slug, group);
            if (change != DuplicatePhonesMergeStalePlanTests.Change.None && when != DuplicatePhonesMergeStalePlanTests.When.BeforeRescan)
                hook.Arm(WriteAsync, every: when == DuplicatePhonesMergeStalePlanTests.When.EveryAttempt);
            Exception? thrown = null;
            try { await DuplicatePhones.ApplyAsync(store, slug, scan, DuplicatePhonesMergeStalePlanTests.At); }
            catch (Exception e) { thrown = e; }
            hook.Disarm();
            await using var q = store.QuerySession(slug);
            var (ua, ub) = ((await q.LoadAsync<User>(a))!, (await q.LoadAsync<User>(b))!);
            var owner = (await q.LoadAsync<Notification>(notice))?.UserId;
            return new(DuplicatePhonesMergeStalePlanTests.NameOf(thrown), ua.DeletedAt is not null, ua.Phone, ub.DeletedAt is not null, ub.Phone, ub.SessionVersion,
                owner == a ? "الباقي" : owner == b ? "المُندَمِج" : "غَيرِهِما");
        }
        finally { await CleanAsync(slug); }
    }

    /// <summary><b>ما يَبقى</b> حَيّاً: المَجموعَةُ الحَيَّةُ — ومَعَها كاتِبٌ لا يَمَسُّ خُطَّتَها مَرَّةً — تُدمَجُ كامِلَةً؛ وتَعارُضانِ يَرمِيانِ بِلا كِتابَة؛ والمَمحُوُّ يَبقى
    /// مَمحُوّاً؛ والباقي الَّذي لَم يُمحَ لا يُمحى. <b>وما يُطبَع</b>: ما رُمِيَ، وجَوّالا الحِسابَين، وحالُ المُندَمِج، ولِمَن إشعارُه.</summary>
    internal static void AssertWhatStays(LiveOutcome o, DuplicatePhonesMergeStalePlanTests.Change change, DuplicatePhonesMergeStalePlanTests.When when)
    {
        if (change == DuplicatePhonesMergeStalePlanTests.Change.SurvivorErased) Assert.True(o.SurvivorDeleted);
        else Assert.False(o.SurvivorDeleted);
        if (change == DuplicatePhonesMergeStalePlanTests.Change.LoserErased) Assert.True(o.LoserDeleted);
        var merges = change == DuplicatePhonesMergeStalePlanTests.Change.None
                     || (change == DuplicatePhonesMergeStalePlanTests.Change.Unrelated && when == DuplicatePhonesMergeStalePlanTests.When.BetweenReadAndSave);
        if (merges)
            Assert.Equal(("", DuplicatePhonesMergeStalePlanTests.To, true, "", "الباقي"), (o.Thrown, o.SurvivorPhone, o.LoserDeleted, o.LoserPhone, o.NoticeOwner));
        if (change == DuplicatePhonesMergeStalePlanTests.Change.Unrelated && when == DuplicatePhonesMergeStalePlanTests.When.EveryAttempt)
            Assert.Equal((nameof(JasperFx.ConcurrencyException), DuplicatePhonesMergeStalePlanTests.Typed, false, DuplicatePhonesMergeStalePlanTests.To, "المُندَمِج"),
                (o.Thrown, o.SurvivorPhone, o.LoserDeleted, o.LoserPhone, o.NoticeOwner));
    }

    public static TheoryData<DuplicatePhonesMergeStalePlanTests.Change, DuplicatePhonesMergeStalePlanTests.When> LiveRows()
    {
        var rows = new TheoryData<DuplicatePhonesMergeStalePlanTests.Change, DuplicatePhonesMergeStalePlanTests.When>();
        foreach (var row in DuplicatePhonesMergeStalePlanTests.WorldRows())
            if (row[0] is DuplicatePhonesMergeStalePlanTests.Change c and not (DuplicatePhonesMergeStalePlanTests.Change.SurvivorGone or DuplicatePhonesMergeStalePlanTests.Change.LoserGone))
                rows.Add(c, (DuplicatePhonesMergeStalePlanTests.When)row[1]!);
        return rows;
    }

    [Theory]
    [MemberData(nameof(LiveRows))]
    public async Task A_group_whose_account_changed_after_the_plan_is_measured_on_Postgres(DuplicatePhonesMergeStalePlanTests.Change change,
        DuplicatePhonesMergeStalePlanTests.When when)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveAsync(change, when);
        output.WriteLine($"{change} · {when} · مَخزَنُ السكربت ⇒ {outcome}");
        AssertWhatStays(outcome, change, when);
    }

    /// <summary>ما بَعدَ دَمجِ مَجموعَةٍ لِلمُندَمِجِ فيها سَلَّة (كَمِّيَّتُها 2) وحَفِظَ الباقي سَلَّتَه (كَمِّيَّتُها 5) بَعدَ القِياس: ما رُمِيَ، وكَمِّيَّةُ سَلَّةِ الباقي،
    /// وسَلَّةُ المُندَمِجِ، وحالُه.</summary>
    internal sealed record CartOutcome(string Thrown, int? SurvivorCart, bool LoserCart, bool LoserDeleted)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · سَلَّةُ الباقي {(SurvivorCart is null ? "غائِبَة" : SurvivorCart == 5 ? "سَلَّتُه (5)" : SurvivorCart == 2 ? "سَلَّةُ المُندَمِج (2)" : "غَيرُهُما")} · "
               + $"سَلَّةُ المُندَمِجِ باقِيَة {LoserCart} · المُندَمِجُ مَحذوف {LoserDeleted}";
    }

    internal static async Task<CartOutcome> LiveCartAsync()
    {
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        var slug = "zz-g2r3-" + Guid.NewGuid().ToString("N")[..8];
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        static Cart CartOf(Guid owner, int quantity)
            => new() { Id = owner, Items = [new CartItem { ListingId = Guid.NewGuid(), Title = "بَند", UnitPriceSar = 5m, Quantity = quantity }] };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(DuplicatePhonesMergeStalePlanTests.Survivor(a, slug));
                s.Store(DuplicatePhonesMergeStalePlanTests.Loser(b, slug));
                s.Store(CartOf(b, 2));
                await s.SaveChangesAsync();
            }
            var scan = await RescanAsync(slug, await PlanAsync(store, slug));
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(CartOf(a, 5));
                await s.SaveChangesAsync();
            }
            Exception? thrown = null;
            try { await DuplicatePhones.ApplyAsync(store, slug, scan, DuplicatePhonesMergeStalePlanTests.At); }
            catch (Exception e) { thrown = e; }
            await using var q = store.QuerySession(slug);
            return new(DuplicatePhonesMergeStalePlanTests.NameOf(thrown), (await q.LoadAsync<Cart>(a))?.TotalQuantity, await q.LoadAsync<Cart>(b) is not null,
                (await q.LoadAsync<User>(b))!.DeletedAt is not null);
        }
        finally { await CleanAsync(slug); }
    }

    /// <summary><b>السَلَّة</b>: «لِلباقي سَلَّة؟» يَأخُذُه الدَمجُ مِن القِياس. <b>ما يَبقى</b>: لا رَمي، والمُندَمِجُ مَمحُوٌّ وسَلَّتُه أُزيلَت، ولِلباقي سَلَّة.
    /// <b>وما يُطبَع</b>: أَيُّ السَلَّتَين بَقِيَت لِلباقي.</summary>
    [Fact]
    public async Task A_survivor_cart_saved_after_the_rescan_is_measured_on_Postgres()
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var o = await LiveCartAsync();
        output.WriteLine($"سَلَّةُ الباقي بَعدَ القِياس · مَخزَنُ السكربت ⇒ {o}");
        Assert.Equal(("", false, true), (o.Thrown, o.LoserCart, o.LoserDeleted));
        Assert.NotNull(o.SurvivorCart);
    }

    /// <summary>ما بَعدَ دَمجِ مَجموعَةٍ صَدَرَ لِمُندَمِجِها مِفتاحُ رَبطٍ بَعدَ القِياس: ما رُمِيَ، وفاعِلُ المِفتاح، وجَوابُ المُصادَقَةِ بِه، وحالُ المُندَمِج.</summary>
    internal sealed record KeyOutcome(string Thrown, string Actor, ApiKeyRejection Rejection, bool KeyKept, bool LoserDeleted)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · فاعِلُ المِفتاح {Actor} · المُصادَقَة {Rejection} · المِفتاحُ قائِم {KeyKept} · المُندَمِجُ مَحذوف {LoserDeleted}";
    }

    internal static async Task<KeyOutcome> LiveKeyAsync()
    {
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        await using var keyStore = LiveApiKeyActorDeletionProofTests.BuildStore();
        var slug = "zz-g2r3-" + Guid.NewGuid().ToString("N")[..8];
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        string? issued = null;
        try
        {
            // ‏جَدوَلُ المَفاتيحِ قائِمٌ قَبلَ القِياس (مَفتاحٌ صَدَرَ في مَتجَرٍ آخَرَ يَوماً) — فَالمَقيسُ مِفتاحٌ جَديدٌ لا جَدوَلٌ جَديد.
            await keyStore.Storage.Database.EnsureStorageExistsAsync(typeof(ApiKeyDocument));
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(DuplicatePhonesMergeStalePlanTests.Survivor(a, slug));
                s.Store(DuplicatePhonesMergeStalePlanTests.Loser(b, slug));
                await s.SaveChangesAsync();
            }
            var scan = await RescanAsync(slug, await PlanAsync(store, slug));
            var keys = new ApiKeyService(keyStore);
            var key = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبطُ الناقِل", b, "سائِق", new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            issued = key.Key.Id;
            Exception? thrown = null;
            try { await DuplicatePhones.ApplyAsync(store, slug, scan, DuplicatePhonesMergeStalePlanTests.At); }
            catch (Exception e) { thrown = e; }
            var auth = await keys.AuthenticateAsync(key.Presented);
            ApiKeyDocument? doc;
            await using (var q = keyStore.QuerySession()) doc = await q.LoadAsync<ApiKeyDocument>(issued);
            bool loserDeleted;
            await using (var q = store.QuerySession(slug)) loserDeleted = (await q.LoadAsync<User>(b))!.DeletedAt is not null;
            return new(DuplicatePhonesMergeStalePlanTests.NameOf(thrown), doc?.ActorUserId == a ? "الباقي" : doc?.ActorUserId == b ? "المُندَمِج" : "غَيرُهُما",
                auth.Rejection, doc is not null, loserDeleted);
        }
        finally
        {
            if (issued is not null)
            {
                await using var g = keyStore.LightweightSession();
                g.Delete<ApiKeyDocument>(issued);
                await g.SaveChangesAsync();
            }
            await CleanAsync(slug);
        }
    }

    /// <summary><b>المِفتاح</b>: الدَمجُ لا يَسأَلُ جَدوَلَ المَفاتيحِ إلّا إن عَدَّ القِياسُ فيه مِفتاحاً. <b>ما يَبقى</b>: لا رَمي، والمُندَمِجُ مَمحُوّ، والمِفتاحُ قائِم.
    /// <b>وما يُطبَع</b>: فاعِلُه وجَوابُ المُصادَقَةِ بِه.</summary>
    [Fact]
    public async Task A_key_issued_to_the_loser_after_the_rescan_is_measured_on_Postgres()
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var o = await LiveKeyAsync();
        output.WriteLine($"مِفتاحٌ لِلمُندَمِجِ بَعدَ القِياس · مَخزَنُ السكربت ⇒ {o}");
        Assert.Equal(("", true, true), (o.Thrown, o.KeyKept, o.LoserDeleted));
    }
}
