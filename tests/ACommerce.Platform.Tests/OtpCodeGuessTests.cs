using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — رَمزُ الدُخولِ مِن مُوَلِّدٍ غَيرِ تَشفيرِيّ، والتَخمينُ الخاطِئُ لا يُبطِلُه، وإعادَةُ الطَلَبِ تُبقي رَمزَين حَيَّين (تَدقيقُ الإطلاق lens-security-8) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `50079b3`):
//   ١. `AuthHandlers.NewCode` = `Random.Shared.Next(100000, 999999)` — مُوَلِّدٌ غَيرُ تَشفيرِيّ، والحَدُّ الأَعلى حَصرِيٌّ فَـ`999999` لا يُسحَبُ أَبَداً.
//   ٢. `ConsumeAttempt` يَرُدُّ الخاطِئَ ويُبقي المُحاوَلَة — فَلا حَدَّ لِلتَخمينِ عَلى الرَمزِ نَفسِه إلّا حِصَّةُ التَحَقُّق (خَمسٌ في الدَقيقَة) طَوالَ
//      عُمرِه (عَشرُ دَقائِق): نَحوُ خَمسينَ تَخميناً لِكُلِّ رَمز.
//   ٣. `IssueAttempt` يُضيفُ مُحاوَلَةً ولا يَمحو سابِقَتَها لِلمَوضوعِ نَفسِه — و`ConsumeAttempt` يَأخُذُ أَوَّلَ ما يَجِدُ بِتَرتيبِ قاموسٍ لا يَحفَظُ
//      الإدراج؛ فَبَعدَ «أَعِد الإرسال» قَد يُقارَنُ الرَمزُ الجَديدُ بِتَجزِئَةِ القَديم.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصُفوفُ الثَلاثَةُ `Today_…` تَنقَلِبُ بِأَسمائِها، وصَفُّ «الصَحيحُ يَفتَحُ مَرَّةً بَعدَ خَطَإٍ واحِد» يَبقى.
//
// **العِلاج** (‏2026-10-24): `RandomNumberGenerator.GetInt32(100000, 1000000)`؛ والخاطِئُ يُعَدُّ عَلى المُحاوَلَةِ وتُمحى عِندَ
// `MaxWrongGuessesPerCode` (خَمسَة — حَدُّ الدَقيقَةِ نَفسُه، لا رَقَمٌ جَديد)؛ والرَمزُ الجَديدُ يَحِلُّ مَحَلَّ سابِقِه لِلمَوضوعِ نَفسِه.
// الصُفوفُ الثَلاثَةُ انقَلَبَت بِأَسمائِها، والأَوَّلُ كَما أُودِع.
public class OtpCodeGuessTests(ITestOutputHelper output)
{
    private static string Subject() => $"guess-{Guid.NewGuid():N}";
    private const string Tenant = "otp-guess";

    private static int LiveFor(string subject) =>
        AuthHandlers.Attempts.Values.Count(a => a.TenantSlug == Tenant && a.Subject == subject && a.ExpiresAt > DateTime.UtcNow);

    // ─── ١. الصَحيحُ يَفتَحُ بَعدَ خَطَإٍ واحِدٍ ثُمَّ يُستَهلَك — لا يَتَغَيَّرُ بِالعِلاج ─────────────────

    [Fact]
    public void A_correct_code_opens_once_after_a_single_typo_and_is_then_spent()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, "424243", AuthHandlers.AuthKind.PhoneOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
        Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
    }

    // ─── ٢. خَمسَةُ أَخطاءٍ تَمحو الرَمز — والصَحيحُ بَعدَها لا يَفتَح ────────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_a_correct_code_still_opens_after_ten_wrong_guesses`.

    [Fact]
    public void Five_wrong_guesses_burn_the_code_so_the_correct_one_no_longer_opens()
    {
        Assert.Equal(5, AuthHandlers.MaxWrongGuessesPerCode);
        var s = Subject();
        AuthHandlers.IssueAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        for (var i = 0; i < AuthHandlers.MaxWrongGuessesPerCode; i++)
            Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, (100000 + i).ToString(), AuthHandlers.AuthKind.PhoneOtp));
        Assert.Equal(0, LiveFor(s));
        Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
    }

    /// <summary>والحَدُّ لا يَسبِقُ وَقتَه: أَربَعَةُ أَخطاءٍ ثُمَّ الصَحيحُ يَفتَح — ما قاسَه المِجَسُّ «correct code after 4 wrong: True» يَبقى صَحيحاً.</summary>
    [Fact]
    public void Four_wrong_guesses_leave_the_correct_code_able_to_open()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        for (var i = 0; i < AuthHandlers.MaxWrongGuessesPerCode - 1; i++)
            Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, (100000 + i).ToString(), AuthHandlers.AuthKind.PhoneOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
    }

    // ─── ٣. المُوَلِّدُ تَشفيرِيٌّ والمَدى السُداسيُّ كُلُّه ─────────────────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_code_comes_from_a_non_cryptographic_generator_whose_top_value_is_never_drawn`.

    [Fact]
    public void The_code_comes_from_the_system_cryptographic_generator_over_the_whole_six_digit_range()
    {
        var source = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));
        Assert.Single(Regex.Matches(source, @"RandomNumberGenerator\.GetInt32\(100000, 1000000\)"));
        Assert.DoesNotContain("Random.Shared", source, StringComparison.Ordinal);

        var drawn = Enumerable.Range(0, 2000).Select(_ => AuthHandlers.NewCode(null)).ToList();
        output.WriteLine($"سُحِبَ {drawn.Count} رَمزاً: أَصغَرُها {drawn.Min()} وأَكبَرُها {drawn.Max()}");
        Assert.All(drawn, c => { Assert.Equal(6, c.Length); Assert.InRange(int.Parse(c), 100000, 999999); });
        Assert.True(drawn.Distinct().Count() > 1900, $"أَداةٌ عَمياء أَو مُوَلِّدٌ ثابِت: {drawn.Distinct().Count()} قيمَةً مُختَلِفَة.");
    }

    // ─── ٤. الرَمزُ الجَديدُ يَحِلُّ مَحَلَّ سابِقِه ──────────────────────────────────────────────────

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_a_resent_code_leaves_two_live_attempts_for_one_subject`.
    [Fact]
    public void A_resent_code_replaces_the_earlier_one_for_its_subject_only()
    {
        var s = Subject();
        var other = Subject();
        AuthHandlers.IssueAttempt(Tenant, other, "333333", AuthHandlers.AuthKind.EmailOtp);
        AuthHandlers.IssueAttempt(Tenant, s, "111111", AuthHandlers.AuthKind.EmailOtp);
        AuthHandlers.IssueAttempt(Tenant, s, "222222", AuthHandlers.AuthKind.EmailOtp);
        Assert.Equal(1, LiveFor(s));
        Assert.Equal(1, LiveFor(other));
        Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, "111111", AuthHandlers.AuthKind.EmailOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(Tenant, s, "222222", AuthHandlers.AuthKind.EmailOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(Tenant, other, "333333", AuthHandlers.AuthKind.EmailOtp));
    }
    // ─── ٥. التَزامُن — أَخطاءٌ مُتَزامِنَةٌ عَلى رَمزٍ واحِد، ورَمزٌ صَحيحٌ مُتَزامِنٌ مَرَّتَين (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2 الجَولَة ١، صُغرى) ──
    //
    // **تَوصيفُ «اليَوم»** (القاعِدَة ٣ — يُقلَبُ الصَفّانِ بِاسمَيهِما في كوميتِ الإصلاح): `ConsumeAttempt` يَقرَأُ المُحاوَلَةَ ثُمَّ يَكتُبُ
    // `TryUpdate(id, counted, attempt)` ويُهمِلُ جَوابَه بِلا حَلقَة — بِخِلافِ `TryConsume` فَوقَه — فَتَخمينٌ ثانٍ مُتَزامِنٌ مُقارِنُه قَديمٌ ويَضيعُ عَدُّه؛
    // ويَمحو الرَمزَ الصَحيحَ بِـ`TryRemove(id)` ويُعيدُ `true` أَيّاً كانَ جَوابُه — فَنِداءانِ صَحيحانِ مُتَزامِنانِ يَفتَحانِ مَعاً. والأَداةُ خُيوطٌ حَقيقِيَّةٌ
    // يُطلِقُها `Barrier` مَعاً عَلى الدالَّةِ نَفسِها، وتَطبَعُ ما عَدَّت.

    private const int Rounds = 200;

    /// <summary>جَولَةٌ: مُحاوَلَةٌ جَديدَةٌ، و<paramref name="threads"/> خُيوطٌ تُنادي <see cref="AuthHandlers.ConsumeAttempt"/> بِـ<paramref name="codeOf"/>
    /// في لَحظَةٍ واحِدَة. تُعيدُ مُعَرِّفَ المُحاوَلَةِ وعَدَدَ مَن فُتِحَ لَه.</summary>
    private static (string Id, int Opened) RaceOnce(int threads, Func<int, string> codeOf)
    {
        var s = Subject();
        var id = AuthHandlers.IssueAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        using var start = new Barrier(threads);
        var opened = 0;
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            if (AuthHandlers.ConsumeAttempt(Tenant, s, codeOf(i), AuthHandlers.AuthKind.PhoneOtp)) Interlocked.Increment(ref opened);
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        return (id, opened);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-25؛ كانَ <c>Today_concurrent_wrong_guesses_on_one_code_lose_counts</c>، مُودَعٌ في <c>07d8be6</c> — «ضاعَ عَدٌّ في
    /// 180 مِن 200»): أَربَعَةُ أَخطاءٍ مُتَزامِنَةٍ تُعَدُّ أَربَعاً في كُلِّ جَولَة، والرَمزُ حَيٌّ بَعدَها؛ لِأَنّ العَدَّ يُقارِنُ ما قَرَأَه ويُعيدُ القِراءَةَ إن سُبِق.</summary>
    [Fact]
    public void Concurrent_wrong_guesses_on_one_code_are_all_counted()
    {
        var counts = new List<int>();
        for (var r = 0; r < Rounds; r++)
        {
            var (id, _) = RaceOnce(4, i => (111110 + i).ToString());
            Assert.True(AuthHandlers.Attempts.TryGetValue(id, out var a), "مُحاوَلَةٌ مُحِيَت بِأَربَعَةِ أَخطاءٍ والحَدُّ خَمسَة.");
            counts.Add(a!.WrongGuesses);
        }
        output.WriteLine($"جَولات {Rounds} · أَربَعَةُ أَخطاءٍ مُتَزامِنَة · عُدَّت أَربَعاً في {counts.Count(c => c == 4)}");
        Assert.All(counts, c => Assert.Equal(4, c));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-25؛ كانَ <c>Today_one_correct_code_sent_twice_at_once_can_open_twice</c>، مُودَعٌ في <c>07d8be6</c> — «فَتَحَ الاثنانِ في
    /// 145 مِن 200»): الرَمزُ الصَحيحُ يُرسَلُ مَرَّتَينِ في لَحظَةٍ واحِدَةٍ فَيَفتَحُ لِواحِدٍ في كُلِّ جَولَة؛ لِأَنّ المَحوَ بِالزَوجِ المَقروءِ يَنجَحُ لِأَحَدِهِما وَحدَه.</summary>
    [Fact]
    public void One_correct_code_sent_twice_at_once_opens_exactly_once()
    {
        var opened = Enumerable.Range(0, Rounds).Select(_ => RaceOnce(2, _ => "424242").Opened).ToList();
        output.WriteLine($"جَولات {Rounds} · نِداءانِ صَحيحانِ مُتَزامِنان · فَتَحَ واحِدٌ في {opened.Count(o => o == 1)}");
        Assert.All(opened, o => Assert.Equal(1, o));
    }

    /// <summary>والحَدُّ يَصدُقُ مُتَزامِناً (‏2026-10-25): خَمسَةُ أَخطاءٍ في لَحظَةٍ واحِدَةٍ تَمحو الرَمزَ في كُلِّ جَولَة — وصَحيحٌ بَعدَها لا يَفتَح.</summary>
    [Fact]
    public void Five_concurrent_wrong_guesses_burn_the_code_every_time()
    {
        for (var r = 0; r < Rounds; r++)
        {
            var (id, opened) = RaceOnce(5, i => (111110 + i).ToString());
            Assert.Equal(0, opened);
            Assert.False(AuthHandlers.Attempts.ContainsKey(id), $"الجَولَة {r}: خَمسَةُ أَخطاءٍ مُتَزامِنَةٍ لَم تَمحُ الرَمز.");
        }
    }
}
