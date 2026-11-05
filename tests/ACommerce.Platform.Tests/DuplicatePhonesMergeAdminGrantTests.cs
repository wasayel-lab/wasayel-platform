using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.DuplicatePhonesMergeStalePlanTests;

namespace ACommerce.Platform.Tests;

// ═══ دَمجُ مَجموعَةٍ صارَ فيها حِسابٌ إداريٌّ بَعدَ إعادَةِ القِياس — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ Carry، الجَولَةُ ١ — Carry-R1-1) ══════════════
//
// **صِنفُ العَطَبِ نَفسُه الَّذي سَمّاه G2R3-1**: خُطَّةٌ تُبنى مِن قِراءَةٍ وتُطَبَّقُ بَعدَ قِراءَةٍ أُخرى ولا تُسأَلُ داخِلَ الكِتابَة. و«لا حِسابَ إدارِيّاً في المَجموعَة»
// (A1: «الدَمجُ لا يُنقِلُ صَلاحِيَّةً ولا يَمحو مَن يَملِكُها») جُزءٌ مِن `Scan.Mergeable` — يَحسِبُه `ScanAsync` وَحدَه (‏`DuplicatePhones.cs` :295-309)، والسكربتُ
// يُعيدُ القِياسَ قُبَيلَ `ApplyAsync`، ثُمَّ `ApplyOnceAsync` يُحَمِّلُ الأَعضاءَ ويَسأَلُ `StaleMember` (الجَوّالُ و`DeletedAt`) ولا يَسأَلُ الدَور — وهُوَ حَقلٌ عَلى
// وَثائِقِ `User` نَفسِها الَّتي يَقرَؤُها بِتَزامُنٍ تَفاؤُلِيٍّ في كُلِّ مُحاوَلَة. والإعادَةُ عَلى `ConcurrencyException` (‏G-1) تَقلِبُ مَنحاً وَقَعَ بَينَ قِراءَةِ
// الدَمجِ وحِفظِه مِن رَدٍّ آمِنٍ بِلا كِتابَةٍ إلى كِتابَةٍ عَلى القِراءَةِ الجَديدَة — كَما قَلَبَها `2158fed` لِإعادَةِ الكَتب.
//
// **الكاتِبُ هُوَ ما يَكتُبُه بابُ المَنحِ بِعَينِه** (`POST /admin/tenants/{slug}/users/{id}/grant-admin`، ‏`MarketplaceTemplateExtensions.cs`): يُحَمِّلُ الحِسابَ في جَلسَةِ
// المَتجَر، و`ActiveRole = "tenant_admin"` و`UpdatedAt`، ثُمَّ `Store` و`SaveChangesAsync`. ومِثلُه دَورٌ في المَتجَرِ رَمزُ كَتالوجِه `tenant_admin` (‏`boss` — الشَكلُ
// الثاني الَّذي يَسأَلُه القِياس)، ودَورٌ غَيرُ إداريٍّ (‏`vendor`) — السالِبُ الَّذي يَبقى يُدمَج.
//
// **كَيفَ يُقاس** — كَتَوصيفِ G2R3-1 (`DuplicatePhonesMergeStalePlanTests.cs`، `c291fed`) وبِأَدَواتِه:
// (أ) عالَمُ وَثائِق (`DocWorld`) بِوَثيقَةِ المَتجَرِ وأَدوارِه: القِياسُ بِصَفٍّ واحِدٍ يُعادُ تَوجيهُه، فَأَوَّلُ كِتابَةٍ `QueueSqlCommand` والعالَمُ لا يُنَفِّذُها —
//     **بُلوغُها هُوَ المَقيس**؛ والمَنحُ «بَعدَ القِياس» يُكتَبُ في العالَمِ قَبلَ `ApplyAsync`، و«بَينَ القِراءَةِ والحِفظ» يُحقَنُ عِندَ أَوَّلِ `QueueSqlCommand` ويَرمي
//     `ConcurrencyException` مَرَّةً واحِدَة.
// (ب) حَيّاً عَلى Postgres بِمَخزَنِ السكربت (`LiveDuplicatePhonesRenameProofTests.ScriptStore`) وشَكلِه: وَثيقَةُ المَتجَرِ بِأَدوارِه، والخُطَّةُ مِن قِراءَةِ الحِسابات،
//     والمَنحُ قَبلَ إعادَةِ القِياسِ أَو بَعدَها أَو بَينَ قِراءَةِ الدَمجِ وحِفظِه (`BeforeMergeSave` مِن مِلَفِّ G2R3-1)، ثُمَّ `ScanAsync` ثُمَّ `ApplyAsync`.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): المَنحُ قَبلَ إعادَةِ القِياسِ يَمنَعُ المَجموعَة (‏`merge_blocked`، ولا يُمحى أَحَد، والإداريُّ يَبقى إدارِيّاً)؛ والدَورُ
// غَيرُ الإداريِّ في أَيِّ لَحظَةٍ يُدمَجُ كامِلاً والباقي يَحمِلُه؛ وفي العالَمِ لا يُكتَبُ حَرفٌ ولا يُحفَظ، والسالِبُ يَبلُغُ أَوَّلَ كِتابَةٍ 1 ثُمَّ 2 بَعدَ التَعارُض.
// **وما يُطبَعُ ولا يُثَبَّت**: المَنحُ الإداريُّ بَعدَ إعادَةِ القِياسِ وبَينَ القِراءَةِ والحِفظ — ما رُمِيَ، ومَن مُحِيَ، ودَورُ كُلٍّ بَعدَه — ويَتَغَيَّرُ في كوميتِ
// التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

public class DuplicatePhonesMergeAdminGrantTests(ITestOutputHelper output)
{
    /// <summary>ما يُمنَحُ ولِمَن: الدَورُ الإداريُّ بِاسمِه (‏<c>tenant_admin</c>) أَو بِدَورِ مَتجَرٍ رَمزُ كَتالوجِه <c>tenant_admin</c> (‏<c>boss</c>)، أَو دَورٌ غَيرُ إداريّ (‏<c>vendor</c>).</summary>
    public enum Grant { LoserAdmin, SurvivorAdmin, LoserBoss, LoserVendor, SurvivorVendor }

    /// <summary>مَتى: قَبلَ إعادَةِ القِياس، أَو بَعدَها وقَبلَ <c>ApplyAsync</c>، أَو بَينَ قِراءَةِ الدَمجِ وحِفظِه مَرَّة.</summary>
    public enum Moment { BeforeRescan, AfterRescan, BetweenReadAndSave }

    internal const string Admin = "tenant_admin";
    internal const string Boss = "boss";
    internal const string Vendor = "vendor";

    /// <summary>أَدوارُ المَتجَر: الإداريُّ بِاسمِه، ودَورٌ اسمُه غَيرُه ورَمزُ كَتالوجِه الإداريّ (الشَكلُ الثاني في <c>ScanAsync</c>)، ودَورٌ غَيرُ إداريّ.</summary>
    internal static Tenant TenantOf(string slug) => new()
    {
        Id = slug,
        Roles =
        [
            new Role { Slug = Admin, CatalogSlug = Admin },
            new Role { Slug = Boss, CatalogSlug = Admin },
            new Role { Slug = Vendor, CatalogSlug = Vendor },
        ],
    };

    internal static bool IsAdminGrant(Grant g) => g is Grant.LoserAdmin or Grant.SurvivorAdmin or Grant.LoserBoss;

    internal static bool ToSurvivor(Grant g) => g is Grant.SurvivorAdmin or Grant.SurvivorVendor;

    /// <summary>الدَورُ الَّذي يَكتُبُه الكاتِب.</summary>
    internal static string RoleOf(Grant g) => g switch
    {
        Grant.LoserAdmin or Grant.SurvivorAdmin => Admin,
        Grant.LoserBoss => Boss,
        _ => Vendor,
    };

    /// <summary>ما يَكتُبُه بابُ المَنحِ عَلى الحِسابِ المُحَمَّل: الدَورُ النَشِطُ ووَقتُ التَحديث.</summary>
    internal static void Write(Grant g, User account) => (account.ActiveRole, account.UpdatedAt) = (RoleOf(g), DateTime.UtcNow);

    /// <summary>(أ) في عالَمِ الوَثائِق: الخُطَّةُ مِن الحِسابَين، ووَثيقَةُ المَتجَرِ بِأَدوارِه، والقِياسُ بِصَفٍّ واحِدٍ يُعادُ تَوجيهُه، والمَنحُ بِحَسَبِ <paramref name="when"/>.</summary>
    internal static async Task<WorldOutcome> InWorldAsync(Grant grant, Moment when)
    {
        const string tenant = "zz-g2r3a";
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var world = new DocWorld().Put(TenantOf(tenant)).Put(Survivor(a, tenant)).Put(Loser(b, tenant));
        var group = Assert.Single(DuplicatePhones.Build(new[]
        {
            new DuplicatePhones.Account(a, Typed, At.AddDays(-10), false),
            new DuplicatePhones.Account(b, To, At.AddDays(-2), false),
        }).Groups);
        var scan = new DuplicatePhones.Scan(group, [new DuplicatePhones.Repoint(b, "mt_doc_notification", 1)], [], 0, false, 0, 0, []);
        void Grant()
        {
            var target = ToSurvivor(grant) ? Survivor(a, tenant) : Loser(b, tenant);
            Write(grant, target);
            world.Put(target);
        }
        if (when == Moment.BetweenReadAndSave)
        {
            var conflicts = 0;
            world.BeforeMember = m =>
            {
                if (m != "QueueSqlCommand" || ++conflicts > 1) return;
                Grant();
                throw new JasperFx.ConcurrencyException(typeof(User), ToSurvivor(grant) ? a : b);
            };
        }
        else Grant();
        Exception? thrown = null;
        try { await DuplicatePhones.ApplyAsync(world.Store, tenant, scan, At); }
        catch (Exception e) { thrown = e; }
        return new(NameOf(thrown), world.Members.Count(m => m == "QueueSqlCommand"), world.Stored.Count, world.SaveCalls);
    }

    /// <summary><b>ما يَبقى</b> في العالَم: لا يُكتَبُ حَرفٌ ولا يُحفَظ؛ والدَورُ غَيرُ الإداريِّ يَبلُغُ أَوَّلَ كِتابَةٍ مَرَّةً بَعدَ القِياس، ومَرَّتَينِ بَعدَ تَعارُضٍ يُعادُ مِنه.
    /// <b>وما يُطبَع</b>: ما رُمِيَ وكَم مَرَّةً بُلِغَت الكِتابَةُ حينَ صارَ في المَجموعَةِ إداريّ.</summary>
    internal static void AssertWhatStays(WorldOutcome o, Grant grant, Moment when)
    {
        Assert.Equal(0, o.Stored);
        Assert.Equal(0, o.Saves);
        if (!IsAdminGrant(grant))
            Assert.Equal((nameof(NotSupportedException), when == Moment.BetweenReadAndSave ? 2 : 1), (o.Thrown, o.WriteReached));
    }

    public static TheoryData<Grant, Moment> WorldRows()
    {
        var rows = new TheoryData<Grant, Moment>();
        foreach (var g in Enum.GetValues<Grant>())
            foreach (var w in new[] { Moment.AfterRescan, Moment.BetweenReadAndSave })
                rows.Add(g, w);
        return rows;
    }

    [Theory]
    [MemberData(nameof(WorldRows))]
    public async Task A_group_whose_member_was_granted_a_role_after_the_rescan_is_measured_in_the_document_world(Grant grant, Moment when)
    {
        var outcome = await InWorldAsync(grant, when);
        output.WriteLine($"{grant} ({RoleOf(grant)}) · {when} ⇒ {outcome}");
        AssertWhatStays(outcome, grant, when);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesMergeAdminGrantProofTests(ITestOutputHelper output)
{
    /// <summary>ما بَعدَ الدَمجِ عَلى Postgres: أَقابِلَةٌ لِلدَمجِ في إعادَةِ القِياس، وما رُمِيَ وسَبَبُه (ما بَعدَ النُقطَتَين)، ومَن مُحِيَ، ودَورُ كُلٍّ بَعدَه.</summary>
    internal sealed record LiveOutcome(bool Mergeable, string Thrown, string Reason, bool SurvivorDeleted, string SurvivorRole, bool LoserDeleted, string LoserRole)
    {
        public override string ToString()
            => $"إعادَةُ القِياسِ قابِلَة {Mergeable} · {(Thrown == "" ? "لا رَمي" : $"يَرمي {Thrown} «{Reason}»")} · "
               + $"الباقي مَحذوف {SurvivorDeleted} الدَور «{SurvivorRole}» · المُندَمِج مَحذوف {LoserDeleted} الدَور «{LoserRole}»";
    }

    /// <summary>سَبَبُ <c>InvalidOperationException</c> بِلا رَمزِه — لـ<c>merge_stale</c> عُضوُه، ولـ<c>merge_blocked</c> نَوعُ مانِعِه (لا جَوّالَ فيه ولا اسم).</summary>
    internal static string ReasonOf(Exception? e)
    {
        if (e is not InvalidOperationException) return "";
        var i = e.Message.IndexOf(':');
        var rest = i < 0 ? "" : e.Message[(i + 1)..].Trim();
        return e.Message.StartsWith("merge_blocked:", StringComparison.Ordinal)
            ? string.Join(" · ", rest.Split(" · ").Select(DuplicatePhones.KindOf))
            : rest;
    }

    /// <summary>(ب) حَيّاً: وَثيقَةُ المَتجَرِ بِأَدوارِه، والباقي (<c>05…</c>، أَقدَم) والمُندَمِج (<c>+966…</c>) بِلا دَور؛ الخُطَّة، والمَنحُ بِحَسَبِ <paramref name="when"/>
    /// (ما يَكتُبُه بابُ المَنحِ بِعَينِه في جَلسَةٍ أُخرى)، وإعادَةُ القِياس، ثُمَّ <c>ApplyAsync</c>. والمَتجَرُ ووَثيقَتُه يُمسَحانِ بَعدَه.</summary>
    internal static async Task<LiveOutcome> LiveAsync(DuplicatePhonesMergeAdminGrantTests.Grant grant, DuplicatePhonesMergeAdminGrantTests.Moment when)
    {
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        var hook = new LiveDuplicatePhonesMergeStalePlanProofTests.BeforeMergeSave();
        store.Options.Listeners.Add(hook);
        var slug = "zz-g2r3-" + Guid.NewGuid().ToString("N")[..8];
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var target = DuplicatePhonesMergeAdminGrantTests.ToSurvivor(grant) ? a : b;
        try
        {
            await using (var s = store.LightweightSession())
            {
                s.Store(DuplicatePhonesMergeAdminGrantTests.TenantOf(slug));
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(Survivor(a, slug));
                s.Store(Loser(b, slug));
                await s.SaveChangesAsync();
            }
            var group = await LiveDuplicatePhonesMergeStalePlanProofTests.PlanAsync(store, slug);
            async Task GrantAsync()
            {
                await using var p = store.LightweightSession(slug);
                var user = (await p.LoadAsync<User>(target))!;
                DuplicatePhonesMergeAdminGrantTests.Write(grant, user);
                p.Store(user);
                await p.SaveChangesAsync();
            }
            if (when == DuplicatePhonesMergeAdminGrantTests.Moment.BeforeRescan) await GrantAsync();
            var scan = await LiveDuplicatePhonesMergeStalePlanProofTests.RescanAsync(slug, group);
            if (when == DuplicatePhonesMergeAdminGrantTests.Moment.AfterRescan) await GrantAsync();
            if (when == DuplicatePhonesMergeAdminGrantTests.Moment.BetweenReadAndSave) hook.Arm(GrantAsync, every: false);
            Exception? thrown = null;
            try { await DuplicatePhones.ApplyAsync(store, slug, scan, At); }
            catch (Exception e) { thrown = e; }
            hook.Disarm();
            await using var q = store.QuerySession(slug);
            var (ua, ub) = ((await q.LoadAsync<User>(a))!, (await q.LoadAsync<User>(b))!);
            return new(scan.Mergeable, NameOf(thrown), ReasonOf(thrown), ua.DeletedAt is not null, ua.ActiveRole, ub.DeletedAt is not null, ub.ActiveRole);
        }
        finally
        {
            await using (var s = store.LightweightSession())
            {
                s.Delete<Tenant>(slug);
                await s.SaveChangesAsync();
            }
            await LiveDuplicatePhonesMergeStalePlanProofTests.CleanAsync(slug);
        }
    }

    /// <summary><b>ما يَبقى</b> حَيّاً: المَنحُ الإداريُّ قَبلَ إعادَةِ القِياسِ يَمنَعُ المَجموعَة — <c>merge_blocked</c> بِمانِعِ <c>admin_account</c>، ولا يُمحى أَحَد، والمَمنوحُ
    /// يَبقى بِدَورِه؛ وإعادَةُ القِياسِ بَعدَ ذلِكَ قابِلَةٌ لِلدَمج؛ والدَورُ غَيرُ الإداريِّ في أَيِّ لَحظَةٍ يُدمَجُ كامِلاً — المُندَمِجُ مَمحُوٌّ والباقي يَحمِلُ الدَور.
    /// <b>وما يُطبَع</b>: المَنحُ الإداريُّ بَعدَ إعادَةِ القِياسِ وبَينَ القِراءَةِ والحِفظ.</summary>
    internal static void AssertWhatStays(LiveOutcome o, DuplicatePhonesMergeAdminGrantTests.Grant grant, DuplicatePhonesMergeAdminGrantTests.Moment when)
    {
        var admin = DuplicatePhonesMergeAdminGrantTests.IsAdminGrant(grant);
        var role = DuplicatePhonesMergeAdminGrantTests.RoleOf(grant);
        var toSurvivor = DuplicatePhonesMergeAdminGrantTests.ToSurvivor(grant);
        Assert.Equal(!(admin && when == DuplicatePhonesMergeAdminGrantTests.Moment.BeforeRescan), o.Mergeable);
        if (admin && when == DuplicatePhonesMergeAdminGrantTests.Moment.BeforeRescan)
            Assert.Equal(($"{nameof(InvalidOperationException)}(merge_blocked)", "admin_account", false, toSurvivor ? role : "", false, toSurvivor ? "" : role),
                (o.Thrown, o.Reason, o.SurvivorDeleted, o.SurvivorRole, o.LoserDeleted, o.LoserRole));
        if (!admin)
            Assert.Equal(("", false, role, true), (o.Thrown, o.SurvivorDeleted, o.SurvivorRole, o.LoserDeleted));
    }

    public static TheoryData<DuplicatePhonesMergeAdminGrantTests.Grant, DuplicatePhonesMergeAdminGrantTests.Moment> LiveRows()
    {
        var rows = new TheoryData<DuplicatePhonesMergeAdminGrantTests.Grant, DuplicatePhonesMergeAdminGrantTests.Moment>();
        foreach (var g in Enum.GetValues<DuplicatePhonesMergeAdminGrantTests.Grant>())
            foreach (var w in Enum.GetValues<DuplicatePhonesMergeAdminGrantTests.Moment>())
                rows.Add(g, w);
        return rows;
    }

    [Theory]
    [MemberData(nameof(LiveRows))]
    public async Task A_group_whose_member_was_granted_a_role_around_the_rescan_is_measured_on_Postgres(DuplicatePhonesMergeAdminGrantTests.Grant grant,
        DuplicatePhonesMergeAdminGrantTests.Moment when)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveAsync(grant, when);
        output.WriteLine($"{grant} ({DuplicatePhonesMergeAdminGrantTests.RoleOf(grant)}) · {when} · مَخزَنُ السكربت ⇒ {outcome}");
        AssertWhatStays(outcome, grant, when);
    }
}
