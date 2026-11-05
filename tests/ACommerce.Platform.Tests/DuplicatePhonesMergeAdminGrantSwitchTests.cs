using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Marten;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.DuplicatePhonesMergeAdminGrantTests;
using static ACommerce.Platform.Tests.DuplicatePhonesMergeStalePlanTests;

namespace ACommerce.Platform.Tests;

// ═══ مانِعُ الإداريِّ يُسأَلُ عَلى قِراءَةِ كُلِّ مُحاوَلَة — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ Carry، الجَولَةُ ١ — Carry-R1-1) ═══════════════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`DuplicatePhonesMergeAdminGrantTests.cs`، `ab38ffe`، والمِلَفُّ بِبَصمَتِه): «لا رَمي» ودَمجٌ كامِلٌ لِمَجموعَةٍ صارَ فيها حِسابٌ إداريٌّ
// بَعدَ إعادَةِ القِياسِ أَو بَينَ قِراءَةِ الدَمجِ وحِفظِه — يُمحى الإداريُّ الجَديد، أَو يُدمَجُ في باقٍ إداريٍّ مَن تَمنَعُه السِياسَة، أَو يَرِثُ الباقي دَوراً إدارِيّاً
// بِرَمزِ كَتالوجِه. والآن `ApplyOnceAsync` يُحَمِّلُ أَدوارَ المَتجَرِ في الجَلسَةِ نَفسِها بَعدَ `StaleMember` ويَسأَلُ `DuplicatePhones.AdminMember` قَبلَ أَوَّلِ كِتابَة —
// في كُلِّ مُحاوَلَة — ويَرمي `merge_stale: admin …` بِلا حَرف، والسكربتُ يَتَخَطّى المَجموعَة. وما بَقِيَ بِعَينِه: الدَورُ غَيرُ الإداريِّ يُدمَجُ في كُلِّ لَحظَة،
// والمَنحُ قَبلَ إعادَةِ القِياسِ يَمنَعُه القِياسُ (`merge_blocked`).
//
// **والسُؤالانِ سُؤالٌ واحِد** (القاعِدَة ٢): `ScanAsync` يَسأَلُ بِـSQL و`AdminMember` بِالوَثيقَة — صُفوفٌ حَيَّةٌ تُقابِلُ جَوابَيهِما بِكُلِّ شَكل؛ **ومَوضِعُ السُؤالِ يُعَدّ**
// (القاعِدَة ١٠): بَعدَ قِراءَةِ الأَعضاءِ وأَدوارِ المَتجَر، وقَبلَ أَوَّلِ كِتابَة، في دالَّةٍ يُعيدُها غِلافُ الإعادَة.

public class DuplicatePhonesMergeAdminGrantSwitchTests(ITestOutputHelper output)
{
    private static readonly Guid A = Guid.Parse("0fa45677-0000-4000-8000-000000000794");
    private static readonly Guid B = Guid.Parse("0fa45677-0000-4000-8000-000000000795");
    private static readonly Guid C = Guid.Parse("0fa45677-0000-4000-8000-000000000796");

    private static User Member(Guid id, string role = "") => new() { Id = id, Phone = To, ActiveRole = role };

    private static IReadOnlyList<Role> Roles(bool tenantDoc) => tenantDoc ? TenantOf("zz-g2r3a").Roles : [];

    /// <summary>صُفوفُ الشَرطِ النَقِيّ — لِكُلِّ رَمزٍ مُوجَبٌ وسالِب: <c>admin survivor</c> و<c>admin loser i/n</c>، بِالشَكلَينِ (الاسمُ، ورَمزُ الكَتالوجِ بِوَثيقَةِ المَتجَر)،
    /// والسالِبُ دَورٌ غَيرُ إداريٍّ، وبِلا دَور، و<c>boss</c> بِلا وَثيقَةِ مَتجَرٍ (القِياسُ لا يَراه إدارِيّاً بِلاها)، ودَورٌ بِاسمِه الإداريِّ في الوَثيقَةِ ولَيسَ دَورَ العُضو.</summary>
    public static TheoryData<string, string, string[], bool, string?> Rows() => new()
    {
        { "الباقي tenant_admin بِلا وَثيقَة", Admin, [""], false, "admin survivor" },
        { "الباقي boss بِالوَثيقَة", Boss, [""], true, "admin survivor" },
        { "الباقي boss بِلا وَثيقَة", Boss, [""], false, null },
        { "الباقي vendor بِالوَثيقَة", Vendor, [""], true, null },
        { "المُندَمِجُ tenant_admin بِالوَثيقَة", "", [Admin], true, "admin loser 1/1" },
        { "المُندَمِجُ boss بِالوَثيقَة", "", [Boss], true, "admin loser 1/1" },
        { "الثاني مِن مُندَمِجَينِ boss", Vendor, [Vendor, Boss], true, "admin loser 2/2" },
        { "المُندَمِجُ vendor بِالوَثيقَة", "", [Vendor], true, null },
        { "المُندَمِجُ بِلا دَورٍ والوَثيقَةُ فيها الإداريّ", "", [""], true, null },
        { "المُندَمِجُ boss بِلا وَثيقَة", "", [Boss], false, null },
        { "الاثنانِ إدارِيّانِ — الباقي أَوَّلاً", Admin, [Admin], true, "admin survivor" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_group_holds_an_admin_while_a_member_role_is_tenant_admin_by_name_or_by_the_store_catalog(string row, string survivorRole, string[] loserRoles,
        bool tenantDoc, string? expected)
    {
        var losers = loserRoles.Select((r, i) => Member(i == 0 ? B : C, r)).ToList();
        var admin = DuplicatePhones.AdminMember(Member(A, survivorRole), losers, Roles(tenantDoc));
        output.WriteLine($"{row} ⇒ {admin ?? "لا إداريّ"}");
        Assert.Equal(expected, admin);
    }

    /// <summary>الرَمزُ يَعرِفُه السكربت: <c>merge_stale: admin …</c> تَخَطٍّ كَسائِرِ <c>merge_stale</c> (مُوجَب)، و<c>merge_blocked</c> بِمانِعِ الإداريِّ لَيسَ تَخَطّياً (سالِب).</summary>
    [Theory]
    [InlineData("merge_stale: admin survivor", true)]
    [InlineData("merge_stale: admin loser 1/1", true)]
    [InlineData("merge_blocked: tenant_admin: 1 حِساباً إدارِيّاً في المَجموعَة", false)]
    public void The_admin_reason_is_a_stale_skip(string message, bool expected)
    {
        var e = new InvalidOperationException(message);
        output.WriteLine($"{message} ⇒ {(DuplicatePhones.IsStale(e) ? "تَخَطٍّ" : "لَيسَ تَخَطّياً")}");
        Assert.Equal(expected, DuplicatePhones.IsStale(e));
    }

    /// <summary>ما يُنتَظَرُ في العالَمِ بَعدَ التَبديل: المَنحُ الإداريُّ يَرمي <c>merge_stale</c> قَبلَ أَوَّلِ كِتابَة — لا يَبلُغُها بَعدَ القِياس، ويَبلُغُها مَرَّةً حينَ يُكتَبُ
    /// بَينَ القِراءَةِ والحِفظ (المُحاوَلَةُ الأُولى) ثُمَّ تَسأَلُ المُعادَةُ فَتَقِف.</summary>
    internal static void AssertSwitched(WorldOutcome o, Grant grant, Moment when)
    {
        DuplicatePhonesMergeAdminGrantTests.AssertWhatStays(o, grant, when);
        if (IsAdminGrant(grant))
            Assert.Equal(($"{nameof(InvalidOperationException)}({DuplicatePhones.MergeStale})", when == Moment.BetweenReadAndSave ? 1 : 0), (o.Thrown, o.WriteReached));
    }

    [Theory]
    [MemberData(nameof(DuplicatePhonesMergeAdminGrantTests.WorldRows), MemberType = typeof(DuplicatePhonesMergeAdminGrantTests))]
    public async Task A_group_whose_member_became_an_admin_after_the_rescan_is_skipped_before_any_write_in_the_document_world(Grant grant, Moment when)
    {
        var outcome = await InWorldAsync(grant, when);
        output.WriteLine($"{grant} ({RoleOf(grant)}) · {when} ⇒ {outcome}");
        AssertSwitched(outcome, grant, when);
    }

    // ─── مَوضِعُ السُؤالِ يُعَدّ ─────────────────────────────────────────────────────────────

    private static readonly Regex MemberRead = new(@"LoadAsync<User>\(", RegexOptions.Compiled);
    private static readonly Regex RolesRead = new(@"LoadAsync<(?:ACommerce\.Kit\.Tenants\.)?Tenant>\(", RegexOptions.Compiled);
    private static readonly Regex AdminQuestion = new(@"\bAdminMember\(", RegexOptions.Compiled);
    private static readonly Regex WriteOp = new(@"QueueSqlCommand\(|\.Store\(|\.Delete<|\.Events\.Append\(|\.Insert\(", RegexOptions.Compiled);

    /// <summary>مَوضِعُ سُؤالِ الإداريِّ في دالَّةٍ تَحفَظُ: قِراءاتُ الأَعضاءِ وقِراءَةُ أَدوارِ المَتجَرِ قَبلَه، وهُوَ قَبلَ أَوَّلِ كِتابَة، والدالَّةُ يُعيدُها غِلافٌ في كُلِّ مُحاوَلَة.</summary>
    private static (int Reads, int Roles, int Question, int FirstWrite, bool Retried, bool Asked) Place(string code, string writer)
    {
        var methods = DuplicatePhonesMergeStalePlanSwitchTests.Methods(code);
        var body = Assert.Single(methods, m => m.Name == writer).Body;
        var reads = MemberRead.Matches(body).Select(x => x.Index).ToList();
        var roles = RolesRead.Matches(body).Select(x => x.Index).DefaultIfEmpty(-1).Max();
        var question = AdminQuestion.Matches(body).Select(x => x.Index).DefaultIfEmpty(-1).Min();
        var firstWrite = WriteOp.Matches(body).Select(x => x.Index).DefaultIfEmpty(int.MaxValue).Min();
        var retried = methods.Any(m => m.Name != writer && m.Body.Contains("for (var attempt = 1; ; attempt++)", StringComparison.Ordinal)
                                       && m.Body.Contains($"try {{ return await {writer}(", StringComparison.Ordinal));
        var asked = reads.Count > 0 && roles >= 0 && question > Math.Max(reads.Max(), roles) && question < firstWrite && retried;
        return (reads.Count, roles, question, firstWrite, retried, asked);
    }

    /// <summary><b>مانِعُ الإداريِّ يُسأَلُ في كِتابَةِ الدَمجِ عَلى قِراءَتِها</b>: في <c>ApplyOnceAsync</c> بَعدَ قِراءاتِ الأَعضاءِ وأَدوارِ المَتجَرِ وقَبلَ أَوَّلِ كِتابَة،
    /// و<c>ApplyAsync</c> يُعيدُها في كُلِّ مُحاوَلَة. ويُطبَعُ ما عُدّ، ويَحمَرُّ عَلى صِفرِ قِراءات.</summary>
    [Fact]
    public void The_admin_part_of_the_plan_is_asked_on_the_merge_write_own_read()
    {
        var code = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs"));
        var p = Place(code, "ApplyOnceAsync");
        output.WriteLine($"ApplyOnceAsync: قِراءاتُ أَعضاء {p.Reads} · أَدوارُ المَتجَر@{p.Roles} · السُؤال@{p.Question} · أَوَّلُ كِتابَة@{p.FirstWrite} · يُعادُ {p.Retried} ⇒ "
                         + (p.Asked ? "يُسأَل" : "لا يُسأَل"));
        Assert.True(p.Reads > 0, "صِفرُ قِراءَةِ عُضو — الأَداةُ عَمياء.");
        Assert.True(p.Asked, "مانِعُ الإداريِّ لا يُسأَلُ عَلى قِراءَةِ كِتابَةِ الدَمج.");
    }

    /// <summary>الفاحِصُ يَرى ما يَدَّعيه — عَيِّناتٌ مَحقونَة: يَسأَلُ بَعدَ أَوَّلِ كِتابَة، أَو قَبلَ قِراءَةِ الأَدوار، أَو لا يَسأَل، أَو خارِجَ الإعادَة ⇒ «لا يُسأَل»؛ والسَليمَةُ ⇒ «يُسأَل».</summary>
    [Theory]
    [InlineData("asks", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); var r = (await s.LoadAsync<Tenant>(t))?.Roles; if (AdminMember(u, l, r) is { } x) throw new(); s.Store(u); await s.SaveChangesAsync(); return 1; }", true)]
    [InlineData("asks-after-write", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); var r = (await s.LoadAsync<Tenant>(t))?.Roles; s.Store(u); if (AdminMember(u, l, r) is { } x) throw new(); await s.SaveChangesAsync(); return 1; }", false)]
    [InlineData("asks-before-roles", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); if (AdminMember(u, l, r) is { } x) throw new(); var r = (await s.LoadAsync<Tenant>(t))?.Roles; s.Store(u); await s.SaveChangesAsync(); return 1; }", false)]
    [InlineData("never-asks", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); var r = (await s.LoadAsync<Tenant>(t))?.Roles; s.Store(u); await s.SaveChangesAsync(); return 1; }", false)]
    [InlineData("asks-outside-the-retry", "public static async Task<int> RunAsync() { return await OnceAsync(); }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); var r = (await s.LoadAsync<Tenant>(t))?.Roles; if (AdminMember(u, l, r) is { } x) throw new(); s.Store(u); await s.SaveChangesAsync(); return 1; }", false)]
    public void The_admin_question_finder_sees_a_write_that_asks_late_early_never_or_outside_its_retry(string row, string sample, bool expected)
    {
        var p = Place(sample, "OnceAsync");
        output.WriteLine($"{row}: قِراءات {p.Reads} · الأَدوار@{p.Roles} · السُؤال@{p.Question} · أَوَّلُ كِتابَة@{p.FirstWrite} · يُعادُ {p.Retried} ⇒ {(p.Asked ? "يُسأَل" : "لا يُسأَل")}");
        Assert.Equal(expected, p.Asked);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesMergeAdminGrantSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> حَيّاً بِمَخزَنِ السكربت: المَنحُ الإداريُّ بَعدَ إعادَةِ القِياسِ أَو بَينَ القِراءَةِ والحِفظِ يَرمي <c>merge_stale</c> بِعُضوِه
    /// ولا يُكتَبُ حَرف — لا يُمحى أَحَد، والمَمنوحُ يَبقى بِدَورِه، والآخَرُ بِلا دَور.</summary>
    internal static void AssertSwitched(LiveDuplicatePhonesMergeAdminGrantProofTests.LiveOutcome o, Grant grant, Moment when)
    {
        LiveDuplicatePhonesMergeAdminGrantProofTests.AssertWhatStays(o, grant, when);
        if (IsAdminGrant(grant) && when != Moment.BeforeRescan)
        {
            var toSurvivor = ToSurvivor(grant);
            var role = RoleOf(grant);
            Assert.Equal(($"{nameof(InvalidOperationException)}({DuplicatePhones.MergeStale})", toSurvivor ? "admin survivor" : "admin loser 1/1",
                    false, toSurvivor ? role : "", false, toSurvivor ? "" : role),
                (o.Thrown, o.Reason, o.SurvivorDeleted, o.SurvivorRole, o.LoserDeleted, o.LoserRole));
        }
    }

    [Theory]
    [MemberData(nameof(LiveDuplicatePhonesMergeAdminGrantProofTests.LiveRows), MemberType = typeof(LiveDuplicatePhonesMergeAdminGrantProofTests))]
    public async Task A_group_whose_member_became_an_admin_after_the_rescan_is_skipped_on_Postgres(Grant grant, Moment when)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveDuplicatePhonesMergeAdminGrantProofTests.LiveAsync(grant, when);
        output.WriteLine($"{grant} ({RoleOf(grant)}) · {when} · مَخزَنُ السكربت ⇒ {outcome}");
        AssertSwitched(outcome, grant, when);
    }

    /// <summary><b>القِياسُ والمُعامَلَةُ يَسأَلانِ سُؤالاً واحِداً</b>: لِكُلِّ شَكلٍ — دَورُ المُندَمِجِ بِاسمِه الإداريّ، وبِرَمزِ الكَتالوجِ في وَثيقَةِ المَتجَر، وغَيرُ إداريّ، وبِلا دَور،
    /// وبِلا وَثيقَةِ مَتجَر — <c>ScanAsync</c> بِـSQL و<see cref="DuplicatePhones.AdminMember"/> بِالوَثيقَةِ المُحَمَّلَة يُجيبانِ الجَوابَ نَفسَه.</summary>
    [Theory]
    [InlineData(Admin, true, true)]
    [InlineData(Admin, false, true)]
    [InlineData(Boss, true, true)]
    [InlineData(Boss, false, false)]
    [InlineData(Vendor, true, false)]
    [InlineData("", true, false)]
    public async Task The_scan_and_the_merge_write_ask_the_same_admin_question_on_Postgres(string loserRole, bool tenantDoc, bool admin)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        var slug = "zz-g2r3-" + Guid.NewGuid().ToString("N")[..8];
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            if (tenantDoc)
                await using (var s = store.LightweightSession())
                {
                    s.Store(TenantOf(slug));
                    await s.SaveChangesAsync();
                }
            var loser = Loser(b, slug);
            loser.ActiveRole = loserRole;
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(Survivor(a, slug));
                s.Store(loser);
                await s.SaveChangesAsync();
            }
            var scan = await LiveDuplicatePhonesMergeStalePlanProofTests.RescanAsync(slug, await LiveDuplicatePhonesMergeStalePlanProofTests.PlanAsync(store, slug));
            string? asked;
            await using (var q = store.QuerySession(slug))
            {
                var roles = (await q.LoadAsync<Tenant>(slug))?.Roles ?? [];
                asked = DuplicatePhones.AdminMember((await q.LoadAsync<User>(a))!, [(await q.LoadAsync<User>(b))!], roles);
            }
            output.WriteLine($"المُندَمِجُ «{loserRole}» · وَثيقَةُ المَتجَر {tenantDoc} ⇒ القِياس: {(scan.Mergeable ? "قابِلَة" : string.Join(" · ", scan.Blocks.Select(DuplicatePhones.KindOf)))} · "
                             + $"المُعامَلَة: {asked ?? "لا إداريّ"}");
            Assert.Equal(!admin, scan.Mergeable);
            Assert.Equal(admin ? "admin loser 1/1" : null, asked);
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
}
