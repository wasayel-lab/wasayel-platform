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

    // ─── ٢. «اليَوم» — عَشرَةُ تَخميناتٍ خاطِئَةٍ لا تُبطِلُ الرَمز ─────────────────────────────────────

    [Fact]
    public void Today_a_correct_code_still_opens_after_ten_wrong_guesses()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp);
        for (var i = 0; i < 10; i++)
            Assert.False(AuthHandlers.ConsumeAttempt(Tenant, s, (100000 + i).ToString(), AuthHandlers.AuthKind.PhoneOtp));
        Assert.True(AuthHandlers.ConsumeAttempt(Tenant, s, "424242", AuthHandlers.AuthKind.PhoneOtp));
    }

    // ─── ٣. «اليَوم» — المُوَلِّدُ غَيرُ تَشفيرِيٍّ وحَدُّه الأَعلى لا يُسحَب ──────────────────────────────

    [Fact]
    public void Today_the_code_comes_from_a_non_cryptographic_generator_whose_top_value_is_never_drawn()
    {
        var source = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));
        Assert.Single(Regex.Matches(source, @"Random\.Shared\.Next\(100000, 999999\)"));
        Assert.DoesNotContain("RandomNumberGenerator", source, StringComparison.Ordinal);

        var drawn = Enumerable.Range(0, 2000).Select(_ => int.Parse(AuthHandlers.NewCode(null))).ToList();
        output.WriteLine($"سُحِبَ {drawn.Count} رَمزاً: أَصغَرُها {drawn.Min()} وأَكبَرُها {drawn.Max()}");
        Assert.All(drawn, c => Assert.InRange(c, 100000, 999998));
    }

    // ─── ٤. «اليَوم» — إعادَةُ الطَلَبِ تُبقي الرَمزَين حَيَّين ─────────────────────────────────────────

    [Fact]
    public void Today_a_resent_code_leaves_two_live_attempts_for_one_subject()
    {
        var s = Subject();
        AuthHandlers.IssueAttempt(Tenant, s, "111111", AuthHandlers.AuthKind.EmailOtp);
        AuthHandlers.IssueAttempt(Tenant, s, "222222", AuthHandlers.AuthKind.EmailOtp);
        Assert.Equal(2, LiveFor(s));
        foreach (var id in AuthHandlers.Attempts.Where(a => a.Value.Subject == s).Select(a => a.Key).ToList())
            AuthHandlers.Attempts.TryRemove(id, out _);
    }
}
