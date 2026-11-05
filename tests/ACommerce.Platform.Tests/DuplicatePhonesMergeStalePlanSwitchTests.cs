using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.DuplicatePhonesMergeStalePlanTests;

namespace ACommerce.Platform.Tests;

// ═══ الدَمجُ يَسأَلُ الخُطَّةَ عَلى قِراءَةِ كُلِّ مُحاوَلَة — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ Carry — G2R3-1) ═══════════════════════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`DuplicatePhonesMergeStalePlanTests.cs`، `c291fed`، والمِلَفُّ بِبَصمَتِه): «لا رَمي» ودَمجٌ كامِلٌ لِمَجموعَةٍ مُحِيَ باقيها أَو مُندَمِجُها أَو
// تَغَيَّرَ رَقَمُ أَحَدِهِما بَعدَ بِناءِ الخُطَّة، وسَلَّةُ المُندَمِجِ فَوقَ سَلَّةِ الباقي، ومِفتاحٌ يَبقى لِمَمحُوّ. والآن `ApplyOnceAsync` يَسأَلُ `DuplicatePhones.StaleMember`
// بَعدَ قِراءَةِ الأَعضاءِ وقَبلَ أَوَّلِ كِتابَة — في كُلِّ مُحاوَلَة — ويَرمي `merge_stale` بِلا حَرف، والسكربتُ يَتَخَطّى المَجموعَة؛ وسَلَّةُ الباقي تُقرَأُ في المُعامَلَة،
// ومَفاتيحُ المُندَمِجِ تُسأَلُ ما دامَ جَدوَلُها قائِماً (`Scan.HasKeysTable`). وما بَقِيَ مِن G-1 بِعَينِه: كاتِبٌ لا يَمَسُّ الخُطَّةَ يُعادُ ويُدمَج، وتَعارُضانِ يَرمِيان.
//
// **وصِنفُ العَطَبِ يُعَدُّ لا يُستَنتَج** (القاعِدَة ١٠): `Every_plan_fact_a_merge_write_applies_is_asked_on_its_own_read` يَجِدُ كُلَّ دالَّةٍ تَحفَظُ في
// `DuplicatePhones.cs`، وكُلَّ قِراءَةِ حِسابٍ فيها، وكُلَّ حَقيقَةٍ مِن القِياسِ تَقرَؤُها الكِتابَة — ويَطبَعُ عَدَدَها ويَحمَرُّ عَلى الصِفر أَو عَلى ما لَم يُصَنَّف.

public class DuplicatePhonesMergeStalePlanSwitchTests(ITestOutputHelper output)
{
    private static readonly Guid A = Guid.Parse("0fa45677-0000-4000-8000-000000000791");
    private static readonly Guid B = Guid.Parse("0fa45677-0000-4000-8000-000000000792");
    private static readonly Guid C = Guid.Parse("0fa45677-0000-4000-8000-000000000793");

    private static User Member(Guid id, string phone, bool erased = false, bool deletedOnly = false)
    {
        var u = new User { Id = id, Phone = phone };
        if (erased) AccountDeletion.Erase(u, At);
        if (deletedOnly) u.DeletedAt = At;
        return u;
    }

    /// <summary>صُفوفُ الشَرطِ النَقِيّ: لِكُلِّ سَبَبٍ مُوجَبٌ وسالِب — الوَثيقَةُ قائِمَة، وغَيرُ مَحذوفَة، والجَوّالُ في الخُطَّةِ بِصيغَةِ المَجموعَة؛ الباقي أَوَّلاً ثُمَّ المُندَمِجون.</summary>
    public static TheoryData<string, User?, User?[], string?> Rows() => new()
    {
        { "حَيّانِ بِصيغَتَي الرَقَم", Member(A, Typed), [Member(B, To)], null },
        { "حَيّانِ والباقي مُوَحَّدٌ والمُندَمِجُ كَما كُتِب", Member(A, To), [Member(B, Typed)], null },
        { "ثَلاثَةٌ حَيَّة", Member(A, Typed), [Member(B, To), Member(C, " 05 0000 0791 ")], null },
        { "الباقي غائِب", null, [Member(B, To)], "survivor" },
        { "الباقي مَمحُوّ (Erase)", Member(A, Typed, erased: true), [Member(B, To)], "survivor" },
        { "الباقي DeletedAt وجَوّالُه باقٍ", Member(A, Typed, deletedOnly: true), [Member(B, To)], "survivor" },
        { "الباقي بِرَقَمٍ آخَر", Member(A, Other), [Member(B, To)], "survivor" },
        { "المُندَمِجُ غائِب", Member(A, Typed), [null], "loser 1/1" },
        { "المُندَمِجُ مَمحُوّ (Erase)", Member(A, Typed), [Member(B, To, erased: true)], "loser 1/1" },
        { "المُندَمِجُ بِرَقَمٍ آخَر", Member(A, Typed), [Member(B, Other)], "loser 1/1" },
        { "المُندَمِجُ حِسابُ نَفاذ (NID-)", Member(A, Typed), [Member(B, "NID-1000000001")], "loser 1/1" },
        { "الثاني مِن مُندَمِجَينِ بِرَقَمٍ آخَر", Member(A, Typed), [Member(B, To), Member(C, Other)], "loser 2/2" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_plan_still_applies_to_a_group_only_while_every_member_is_there_alive_with_the_group_number(string row, User? survivor, User?[] losers, string? expected)
    {
        var group = new DuplicatePhones.Group(To, A, losers.Select((l, i) => l?.Id ?? (i == 0 ? B : C)).ToList());
        var stale = DuplicatePhones.StaleMember(group, survivor, losers);
        output.WriteLine($"{row} ⇒ {stale ?? "تَنطَبِق"}");
        Assert.Equal(expected, stale);
    }

    /// <summary>الرَمزُ يَعرِفُه السكربت: <c>merge_stale:</c> بِعَينِه ⇒ تَخَطٍّ (مُوجَب)؛ و<c>merge_blocked:</c> أَو رَمزٌ آخَرُ أَو نَوعٌ آخَرُ ⇒ لَيسَ تَخَطّياً (سالِب).</summary>
    [Theory]
    [InlineData("InvalidOperationException", "merge_stale: survivor", true)]
    [InlineData("InvalidOperationException", "merge_stale: loser 1/1", true)]
    [InlineData("InvalidOperationException", "merge_blocked: mt_doc_deal: 1 صَفّاً يَجمَعُ الحِسابَين", false)]
    [InlineData("InvalidOperationException", "listing_gone", false)]
    [InlineData("InvalidOperationException", "merge_stalework", false)]
    [InlineData("NotSupportedException", "merge_stale: survivor", false)]
    public void Only_the_stale_code_is_a_skip(string type, string message, bool expected)
    {
        Exception e = type == nameof(NotSupportedException) ? new NotSupportedException(message) : new InvalidOperationException(message);
        output.WriteLine($"{type}({message}) ⇒ {(DuplicatePhones.IsStale(e) ? "تَخَطٍّ" : "لَيسَ تَخَطّياً")}");
        Assert.Equal(expected, DuplicatePhones.IsStale(e));
    }

    private static bool Stale(Change change) => change is not (Change.None or Change.Unrelated);

    /// <summary>ما يُنتَظَرُ في العالَمِ بَعدَ التَبديل: ما لا تَنطَبِقُ عَلَيه الخُطَّةُ يَرمي <c>merge_stale</c> قَبلَ أَوَّلِ كِتابَة — لا يَبلُغُها قَبلَ القِياس، ويَبلُغُها
    /// مَرَّةً واحِدَةً حينَ يُكتَبُ بَينَ القِراءَةِ والحِفظ (المُحاوَلَةُ الأُولى) ثُمَّ تَسأَلُ المُعادَةُ فَتَقِف.</summary>
    internal static void AssertSwitched(WorldOutcome o, Change change, When when)
    {
        AssertWhatStays(o, change, when);
        if (Stale(change))
            Assert.Equal(($"{nameof(InvalidOperationException)}({DuplicatePhones.MergeStale})", when == When.BeforeRescan ? 0 : 1), (o.Thrown, o.WriteReached));
    }

    [Theory]
    [MemberData(nameof(WorldRows), MemberType = typeof(DuplicatePhonesMergeStalePlanTests))]
    public async Task A_group_whose_account_changed_after_the_plan_is_skipped_before_any_write_in_the_document_world(Change change, When when)
    {
        var outcome = await InWorldAsync(change, when);
        output.WriteLine($"{change} · {when} ⇒ {outcome}");
        AssertSwitched(outcome, change, when);
    }

    // ─── صِنفُ العَطَبِ يُعَدّ ──────────────────────────────────────────────────────────────

    private static string Code() => WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs"));

    private static readonly Regex MethodDecl = new(
        @"(?:public|private|internal)\s+static\s+(?:async\s+)?[\w<>\[\]?,.]+(?:<[^()]*?>)?\s+(?<name>\w+)\s*(?:<\w+>)?\s*\(", RegexOptions.Compiled);

    /// <summary>جِسمُ كُلِّ دالَّةٍ ساكِنَةٍ في المِلَفّ — مِن أَوَّلِ <c>{</c> بَعدَ تَوقيعِها إلى مُقابِلِه. والدالَّةُ بِسَهمٍ (<c>=&gt;</c>) خارِجَه.</summary>
    internal static IReadOnlyList<(string Name, string Body)> Methods(string code)
    {
        var list = new List<(string, string)>();
        foreach (Match m in MethodDecl.Matches(code))
        {
            var open = code.IndexOf('{', m.Index + m.Length);
            var arrow = code.IndexOf("=>", m.Index + m.Length, StringComparison.Ordinal);
            var semi = code.IndexOf(';', m.Index + m.Length);
            if (open < 0 || (arrow >= 0 && arrow < open) || (semi >= 0 && semi < open)) continue;
            var depth = 0;
            for (var i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) { list.Add((m.Groups["name"].Value, code[open..(i + 1)])); break; }
            }
        }
        return list;
    }

    private static readonly Regex AccountRead = new(@"LoadAsync<(?:User|T)>\(", RegexOptions.Compiled);
    private static readonly Regex PlanQuestion = new(@"\b(?:StaleMember|StillApplies)\(", RegexOptions.Compiled);
    private static readonly Regex WriteOp = new(@"QueueSqlCommand\(|\.Store\(|\.Delete<|\.Events\.Append\(|\.Insert\(|\bsetPhone\(", RegexOptions.Compiled);

    /// <summary>
    /// <para><b>حَقائِقُ القِياسِ الَّتي تَقرَؤُها كِتابَةُ الدَمجِ — كُلٌّ بِسَبَبِه</b>. حَقيقَةٌ جَديدَةٌ في <c>ApplyAsync</c>/<c>ApplyOnceAsync</c> لا سَطرَ لَها هُنا تَحمَرّ:
    /// إمّا تُقرَأُ في المُعامَلَة، وإمّا يُكتَبُ هُنا لِماذا يَكفي أَخذُها مِن القِياس. وما خَرَجَ (<c>SurvivorCart</c>، <c>Keys</c>) لا يَعودُ إلّا بِسَطر.</para>
    /// </summary>
    internal static readonly (string Fact, string WhyAr)[] ScanFacts =
    {
        ("Mergeable", "مَوانِعُ المَجموعَة: مانِعُ الإداريِّ يُسأَلُ في المُعامَلَة — AdminMember عَلى قِراءَةِ كُلِّ مُحاوَلَةٍ بِأَدوارِ المَتجَرِ مِن الجَلسَةِ نَفسِها، والمَنحُ يَكتُبُ "
                      + "وَثيقَةَ User فَيَرُدُّه التَزامُنُ التَفاؤُلِيّ (Carry-R1-1)؛ وما سِواه صُفوفٌ في جَداوِلَ أُخرى يُعيدُ السكربتُ قِياسَها قُبَيلَ الدَمجِ لا داخِلَه — "
                      + "الباقي المَقولُ في ADR-041 §٣ (نافِذَتُه مِن إعادَةِ القِياسِ إلى الحِفظ)."),
        ("Blocks", "نَصُّ المانِعِ في رِسالَةِ merge_blocked — قِراءَةٌ لِلتَقرير."),
        ("Group", "أَعضاءُ المَجموعَةِ وصيغَتُها: تُحَمَّلُ في المُعامَلَةِ وتُسأَلُ StaleMember قَبلَ أَوَّلِ كِتابَة، في كُلِّ مُحاوَلَة."),
        ("Repoints", "جَداوِلُ الصُفوفِ المُحيلَة: شَرطُ SQL يَختارُ الصُفوفَ داخِلَ المُعامَلَة، والعَدَدُ لِلتَقرير؛ وجَدوَلٌ بِلا صَفٍّ حينَ قيسَ داخِلٌ في الباقي المَقول."),
        ("Listings", "إعلاناتُ المُندَمِج: مالِكُها لا يَكتُبُه بَعدَ الإنشاءِ إلّا الدَمج (ListingEditService يُمَرِّرُ Attributes: null)، والدَمجُ يَمحو المُندَمِج — فَيَسأَلُه StaleMember."),
        ("HasKeysTable", "وُجودُ جَدوَلِ المَفاتيح (مُخَطَّط): المَفاتيحُ نَفسُها تُسأَلُ في المُعامَلَة."),
    };

    /// <summary>
    /// <para><b>كُلُّ حَقيقَةٍ مِن الخُطَّةِ تُطَبِّقُها كِتابَةُ الدَمجِ تُسأَلُ عَلى قِراءَتِها</b> — صِنفُ العَطَبِ الَّذي عادَ في G2 ثَلاثَ مَرّات (‏<c>2158fed</c> ← <c>e8a2c09</c>
    /// ← G2R3-1). يَجِدُ كُلَّ دالَّةٍ تَحفَظُ في <c>DuplicatePhones.cs</c>: قِراءاتُ الحِسابِ فيها تَسبِقُ سُؤالَ الخُطَّة، والسُؤالُ يَسبِقُ أَوَّلَ كِتابَة، والدالَّةُ يُناديها
    /// غِلافُ الإعادَةِ فَتُسأَلُ في كُلِّ مُحاوَلَة؛ وكُلُّ حَقيقَةٍ مِن القِياسِ في كِتابَةِ الدَمجِ مُصَنَّفَةٌ في <see cref="ScanFacts"/>.</para>
    /// <para><b>وحَدُّه</b>: النَصُّ لا الدَلالَة — سُؤالٌ يُكتَبُ ولا يُعمَلُ بِجَوابِه لا يَراه (تَراه صُفوفُ العالَمِ والحَيّ).</para>
    /// </summary>
    [Fact]
    public void Every_plan_fact_a_merge_write_applies_is_asked_on_its_own_read()
    {
        var code = Code();
        var methods = Methods(code);
        var writers = methods.Where(m => m.Body.Contains("SaveChangesAsync(", StringComparison.Ordinal)).ToList();
        var reads = 0;
        var asked = 0;
        var off = new List<string>();
        foreach (var (name, body) in writers)
        {
            var at = AccountRead.Matches(body).Select(x => x.Index).ToList();
            var question = PlanQuestion.Matches(body).Select(x => x.Index).Where(q => at.Count > 0 && q > at.Max()).DefaultIfEmpty(-1).Min();
            var firstWrite = WriteOp.Matches(body).Select(x => x.Index).DefaultIfEmpty(int.MaxValue).Min();
            var retried = methods.Any(m => m.Name != name && m.Body.Contains("for (var attempt = 1; ; attempt++)", StringComparison.Ordinal)
                                           && m.Body.Contains($"try {{ return await {name}(", StringComparison.Ordinal));
            reads += at.Count;
            if (at.Count > 0 && question >= 0 && question < firstWrite && at.All(r => r < firstWrite) && retried) asked += at.Count;
            else off.Add($"{name}: قِراءات {at.Count} · السُؤال@{question} · أَوَّلُ كِتابَة@{firstWrite} · يُعادُ {retried}");
        }
        var merge = string.Concat(methods.Where(m => m.Name is "ApplyAsync" or "ApplyOnceAsync").Select(m => m.Body));
        var facts = Regex.Matches(merge, @"\bscan\.(\w+)").Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var known = ScanFacts.Select(f => f.Fact).ToHashSet(StringComparer.Ordinal);
        var unclassified = facts.Where(f => !known.Contains(f)).ToList();
        var stale = known.Where(f => !facts.Contains(f)).ToList();
        output.WriteLine($"دَوالُّ تَحفَظ: {writers.Count} ({string.Join(" · ", writers.Select(w => w.Name))}) · قِراءاتُ حِساب: {reads} · تُسأَلُ الخُطَّةُ بَعدَها وقَبلَ أَوَّلِ كِتابَةٍ وفي كُلِّ مُحاوَلَة: {asked}/{reads}");
        output.WriteLine($"حَقائِقُ القِياسِ في كِتابَةِ الدَمج: {facts.Count} ({string.Join(" · ", facts)}) · مُصَنَّفَة: {facts.Count - unclassified.Count}/{facts.Count}");
        Assert.True(writers.Count > 0 && reads > 0 && facts.Count > 0, "صِفرٌ مَفحوص — الأَداةُ عَمياء.");
        Assert.True(off.Count == 0, "كِتابَةٌ لا تَسأَلُ خُطَّتَها عَلى قِراءَتِها: " + string.Join(" | ", off));
        Assert.True(unclassified.Count == 0, "حَقيقَةٌ مِن القِياسِ تَكتُبُ عَلَيها الكِتابَةُ بِلا سَطر: " + string.Join(" · ", unclassified));
        Assert.True(stale.Count == 0, "سَطرٌ لِحَقيقَةٍ لا تَقرَؤُها الكِتابَة — يُرفَع: " + string.Join(" · ", stale));
    }

    /// <summary>الفاحِصُ يَرى ما يَدَّعيه — عَيِّناتٌ مَحقونَة: كِتابَةٌ تَسأَلُ بَعدَ أَوَّلِ كِتابَة، وكِتابَةٌ لا تَسأَلُ، وكِتابَةٌ لا يُعيدُها غِلاف ⇒ خارِجَ «تُسأَل»؛ والسَليمَةُ داخِلَها.</summary>
    [Theory]
    [InlineData("asks", "public static async Task<int> RunAsync() { try { return await OnceAsync(); } catch { } for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); if (StaleMember(g, u, l) is { } x) throw new(); s.Store(u); await s.SaveChangesAsync(); return 1; }", true)]
    [InlineData("asks-after-write", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); s.Store(u); if (StaleMember(g, u, l) is { } x) throw new(); await s.SaveChangesAsync(); return 1; }", false)]
    [InlineData("never-asks", "public static async Task<int> RunAsync() { for (var attempt = 1; ; attempt++) { try { return await OnceAsync(); } catch { } } }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); s.Store(u); await s.SaveChangesAsync(); return 1; }", false)]
    [InlineData("asks-outside-the-retry", "public static async Task<int> RunAsync() { return await OnceAsync(); }\nprivate static async Task<int> OnceAsync() { var u = await s.LoadAsync<User>(id); if (StaleMember(g, u, l) is { } x) throw new(); s.Store(u); await s.SaveChangesAsync(); return 1; }", false)]
    public void The_fact_finder_sees_a_write_that_asks_late_never_or_outside_its_retry(string row, string sample, bool expected)
    {
        var methods = Methods(sample);
        var writer = Assert.Single(methods, m => m.Body.Contains("SaveChangesAsync(", StringComparison.Ordinal));
        var at = AccountRead.Matches(writer.Body).Select(x => x.Index).ToList();
        var question = PlanQuestion.Matches(writer.Body).Select(x => x.Index).Where(q => q > at.Max()).DefaultIfEmpty(-1).Min();
        var firstWrite = WriteOp.Matches(writer.Body).Select(x => x.Index).DefaultIfEmpty(int.MaxValue).Min();
        var retried = methods.Any(m => m.Name != writer.Name && m.Body.Contains("for (var attempt = 1; ; attempt++)", StringComparison.Ordinal)
                                       && m.Body.Contains($"try {{ return await {writer.Name}(", StringComparison.Ordinal));
        var ok = question >= 0 && question < firstWrite && retried;
        output.WriteLine($"{row}: دَوالّ {methods.Count} · قِراءات {at.Count} · السُؤال@{question} · أَوَّلُ كِتابَة@{firstWrite} · يُعادُ {retried} ⇒ {(ok ? "تُسأَل" : "لا تُسأَل")}");
        Assert.Equal(expected, ok);
    }

    private static string Script() => string.Join('\n', StudioConsentCharacterizationTests.Source("scripts", "merge-duplicate-phones.cs").Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary>السكربتُ يَتَخَطّى المَجموعَةَ الَّتي لَم تَعُد خُطَّتُها تَنطَبِق كَما يَتَخَطّى الَّتي صارَ فيها مانِع: «تَخَطٍّ» يُطبَع، ولا سَطرَ تَدقيقٍ ولا «دُمِجَت»،
    /// ويُطبَعُ ما دُمِجَ وما تُخُطِّيَ لِكُلِّ مَتجَرٍ ولِلكُلّ، ورَمزُ الخُروجِ كَما هُوَ.</summary>
    [Fact]
    public void The_merge_script_skips_a_stale_group_like_a_blocked_one_and_counts_what_it_merged()
    {
        var code = Script();
        int At(string s, int from = 0) { var i = code.IndexOf(s, from, StringComparison.Ordinal); Assert.True(i >= 0, $"لا «{s}» — الأَداةُ عَمياء."); return i; }
        var apply = At("try { moved = await DuplicatePhones.ApplyAsync(store, slug, scan, now); }");
        var catchAt = At("catch (InvalidOperationException e) when (DuplicatePhones.IsStale(e))", apply);
        var open = code.IndexOf('{', catchAt);
        var catchEnd = open;
        for (var depth = 0; catchEnd < code.Length; catchEnd++)
            if (code[catchEnd] == '{') depth++;
            else if (code[catchEnd] == '}' && --depth == 0) break;
        var skip = code[open..catchEnd];
        var audit = At("await audit.WriteAsync(", catchAt);
        var mergedAt = At("merged++;", catchAt);
        var blocked = code[At("if (!scan.Mergeable)")..apply];
        output.WriteLine($"الدَمج@{apply} التَخَطّي@{catchAt}..{catchEnd} التَدقيق@{audit} العَدّ@{mergedAt} · تَخَطّي المانِعِ يُعَدّ: {blocked.Contains("groupsSkipped++;", StringComparison.Ordinal)}");
        Assert.True(apply < catchAt && catchEnd < audit && catchEnd < mergedAt);
        Assert.Contains("Console.WriteLine($\"  تَخَطٍّ: {slug} — ", skip, StringComparison.Ordinal);
        Assert.Contains("groupsSkipped++;", skip, StringComparison.Ordinal);
        Assert.EndsWith("continue;", skip.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("groupsSkipped++;", blocked, StringComparison.Ordinal);
        Assert.Contains("دُمِجَت {mergedHere} · تُخُطِّيَت {ok.Count - mergedHere}", code, StringComparison.Ordinal);
        Assert.Contains("وتُخُطِّيَت {groupsSkipped}", code, StringComparison.Ordinal);
        Assert.DoesNotContain("دُمِجَت {ok.Count}", code, StringComparison.Ordinal);
        Assert.EndsWith("return 0;", code.TrimEnd(), StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesMergeStalePlanSwitchProofTests(ITestOutputHelper output)
{
    private static bool Stale(Change change) => change is not (Change.None or Change.Unrelated);

    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> حَيّاً بِمَخزَنِ السكربت: ما لا تَنطَبِقُ عَلَيه الخُطَّةُ يَرمي <c>merge_stale</c> ولا يُكتَبُ حَرف — المَمحُوُّ يَبقى بِلا جَوّال،
    /// والرَقَمُ الجَديدُ يَبقى، والحِسابُ الآخَرُ كَما كان، وإشعارُ المُندَمِجِ لَه.</summary>
    internal static void AssertSwitched(LiveDuplicatePhonesMergeStalePlanProofTests.LiveOutcome o, Change change, When when)
    {
        LiveDuplicatePhonesMergeStalePlanProofTests.AssertWhatStays(o, change, when);
        if (Stale(change))
        {
            var survivorPhone = change switch { Change.SurvivorErased => "", Change.SurvivorOtherPhone => Other, _ => Typed };
            var (loserDeleted, loserPhone, loserVersion) = change switch
            {
                Change.LoserErased => (true, "", 1),
                Change.LoserOtherPhone => (false, Other, 0),
                _ => (false, To, 0),
            };
            Assert.Equal(($"{nameof(InvalidOperationException)}({DuplicatePhones.MergeStale})", survivorPhone, loserDeleted, loserPhone, loserVersion, "المُندَمِج"),
                (o.Thrown, o.SurvivorPhone, o.LoserDeleted, o.LoserPhone, o.LoserSessionVersion, o.NoticeOwner));
        }
    }

    [Theory]
    [MemberData(nameof(LiveDuplicatePhonesMergeStalePlanProofTests.LiveRows), MemberType = typeof(LiveDuplicatePhonesMergeStalePlanProofTests))]
    public async Task A_group_whose_account_changed_after_the_plan_is_skipped_on_Postgres(Change change, When when)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveDuplicatePhonesMergeStalePlanProofTests.LiveAsync(change, when);
        output.WriteLine($"{change} · {when} · مَخزَنُ السكربت ⇒ {outcome}");
        AssertSwitched(outcome, change, when);
    }

    /// <summary>سَلَّةُ الباقي تُقرَأُ في المُعامَلَة: سَلَّةٌ حَفِظَها بَعدَ القِياسِ تَبقى لَه (5)، وسَلَّةُ المُندَمِجِ تُزالُ كَما تُزالُ حينَ لِلباقي سَلَّة.</summary>
    [Fact]
    public async Task A_survivor_cart_saved_after_the_rescan_is_kept_on_Postgres()
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var o = await LiveDuplicatePhonesMergeStalePlanProofTests.LiveCartAsync();
        output.WriteLine($"سَلَّةُ الباقي بَعدَ القِياس · مَخزَنُ السكربت ⇒ {o}");
        Assert.Equal(("", 5, false, true), (o.Thrown, o.SurvivorCart, o.LoserCart, o.LoserDeleted));
    }

    /// <summary>مَفاتيحُ المُندَمِجِ تُسأَلُ ما دامَ جَدوَلُها قائِماً: مِفتاحٌ صَدَرَ لَه بَعدَ القِياسِ يَصيرُ لِلباقي ويُصادِقُ بِاسمِه.</summary>
    [Fact]
    public async Task A_key_issued_to_the_loser_after_the_rescan_moves_to_the_survivor_on_Postgres()
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var o = await LiveDuplicatePhonesMergeStalePlanProofTests.LiveKeyAsync();
        output.WriteLine($"مِفتاحٌ لِلمُندَمِجِ بَعدَ القِياس · مَخزَنُ السكربت ⇒ {o}");
        Assert.Equal(("", "الباقي", ApiKeyRejection.None, true, true), (o.Thrown, o.Actor, o.Rejection, o.KeyKept, o.LoserDeleted));
    }
}
