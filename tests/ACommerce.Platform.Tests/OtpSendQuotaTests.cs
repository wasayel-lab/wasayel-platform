using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — حِصَّةُ إرسالِ رَمزِ الدُخولِ لِكُلِّ نَصٍّ كَما كُتِب، وبَدءُ نَفاذٍ بِلا حِصَّة، والمُحاوَلاتُ المُنتَهِيَةُ لا تُطرَد (تَدقيقُ الإطلاق lens-security-3) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `85cf1f4`):
//   ١. مِفتاحُ الحِصَّةِ (‏10 في السّاعَة، د28) `{tenant}|{phone}` بِالنَصِّ المَقصوصِ كَما كُتِب — فَـ`05…` و`9665…` و`+9665…`
//      لِلرَقَمِ نَفسِه ثَلاثَةُ مَفاتيح: ثَلاثونَ رِسالَةً في السّاعَةِ لِرَقَمٍ واحِد. وبابُ الاستوديو (`StudioAuth.SendAsync`) كَذلِك.
//   ٢. والرَمزُ يُرسَلُ إلى النَصِّ كَما كُتِب — و`TwilioSmsChannel` يُلحِقُ `+` بِما لا يَبدَأُ بِها، فَـ`0551234567` يَصيرُ `+0551234567`:
//      رَقَمٌ لا وُجودَ لَه.
//   ٣. `RequestNafathHandler` يُنادي `StartAsync` مُباشَرَةً بِلا حِصَّة — كُلُّ طَلَبٍ نِداءٌ خارِجِيّ.
//   ٤. `Attempts` لا يُطرَدُ مِنه إلّا ما استُهلِك — مُحاوَلَةٌ انتَهَت مُهلَتُها تَبقى في الذاكِرَةِ إلى إعادَةِ التَشغيل.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): صُفوفُ `Today_…` الأَربَعَةُ تَنقَلِبُ بِأَسمائِها، والصَفّانِ الآخَرانِ (الحادِيَ عَشَرَ لِلنَصِّ
// الواحِدِ يُرَدّ، ورَقَمٌ خارِجَ السُعودِيَّةِ أَو نَصٌّ لَيسَ رَقَماً يَبلُغُ القَناةَ كَما كُتِب) يَبقَيانِ بِلا تَعديلِ حَرف. **وما لا يُثَبَّتُ
// هُنا لِأَنّه رَقَمُ المالِك**: حَدٌّ لِكُلِّ IP أَو لِلمَنَصَّةِ كُلِّها (القاعِدَة ١٦).
public class OtpSendQuotaTests(ITestOutputHelper output)
{
    /// <summary>قَناةُ رَسائِلَ تُسَجِّلُ إلى أَينَ أُرسِلَ ولا تُرسِل.</summary>
    internal sealed class RecordingSms : IOtpChannel
    {
        public List<string> To { get; } = new();
        public string ChannelName => "recording-sms";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string phone, string code, CancellationToken ct)
        {
            lock (To) To.Add(phone);
            return Task.CompletedTask;
        }
    }

    /// <summary>قَناةُ نَفاذٍ تَعُدُّ نِداءاتِ البَدءِ ولا تَتَّصِل.</summary>
    internal sealed class CountingNafath : INafathChannel
    {
        public int Started;
        public string ChannelName => "counting-nafath";
        public Task<NafathStartResult> StartAsync(string nationalId, CancellationToken ct)
        {
            Interlocked.Increment(ref Started);
            return Task.FromResult(new NafathStartResult(Guid.NewGuid().ToString("N"), "42", 0));
        }
        public Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct) => Task.FromResult(false);
    }

    /// <summary>مُستَأجِرٌ ورَقَمٌ لا يَتَقاطَعانِ مَعَ صَفٍّ آخَر — العَدّاداتُ ساكِنَةٌ في العَمَلِيَّة.</summary>
    private static (string Slug, string Nine) Fresh()
    {
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString();
        return ($"otp-q-{Guid.NewGuid():N}"[..20], "5" + digits);
    }

    private static async Task<bool> PhoneSendAsync(string slug, string phone, IOtpChannel sms)
    {
        try
        {
            await AuthHandlers.RequestPhoneOtpHandler(new RequestPhoneOtp(phone),
                new VendorRegisterBucketTests.ResolvedTenant(slug), sms, CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("rate_limited", StringComparison.Ordinal)) { return false; }
    }

    private static async Task<bool> NafathStartAsync(string slug, string nid, INafathChannel nafath)
    {
        try
        {
            await AuthHandlers.RequestNafathHandler(new RequestNafath(nid),
                new VendorRegisterBucketTests.ResolvedTenant(slug), nafath, CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("rate_limited", StringComparison.Ordinal)) { return false; }
    }

    // ─── ١. النَصُّ الواحِدُ يُرَدُّ في حادِيَتِه عَشرَة — لا يَتَغَيَّرُ بِالعِلاج ─────────────────────────

    [Fact]
    public async Task The_eleventh_send_for_one_spelling_within_the_hour_is_refused()
    {
        var (slug, nine) = Fresh();
        var sms = new RecordingSms();
        var sent = new List<bool>();
        for (var i = 0; i < 11; i++) sent.Add(await PhoneSendAsync(slug, "0" + nine, sms));
        Assert.Equal(Enumerable.Repeat(true, 10).Append(false), sent);
        Assert.Equal(10, sms.To.Count);
    }

    // ─── ٢. ورَقَمٌ خارِجَ السُعودِيَّةِ أَو نَصٌّ لَيسَ رَقَماً يَبلُغُ القَناةَ كَما كُتِب — لا يَتَغَيَّرُ بِالعِلاج ──────

    [Theory]
    [InlineData("+201001234567")]
    [InlineData("+14155550123")]
    [InlineData("oto-register:3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task A_number_outside_saudi_or_a_subject_that_is_not_a_number_reaches_the_channel_as_typed(string typed)
    {
        var (slug, _) = Fresh();
        var sms = new RecordingSms();
        Assert.True(await PhoneSendAsync(slug, typed, sms));
        Assert.Equal(new[] { typed }, sms.To);
    }

    // ─── ٣. «اليَوم» — ثَلاثُ كِتاباتٍ لِرَقَمٍ واحِدٍ ثَلاثَةُ مَفاتيح ─────────────────────────────────

    [Fact]
    public async Task Today_three_spellings_of_one_saudi_number_are_three_quota_keys_at_both_doors()
    {
        var (slug, nine) = Fresh();
        var sms = new RecordingSms();
        var refused = 0;
        foreach (var spelling in new[] { "0" + nine, "966" + nine, "+966" + nine })
            for (var i = 0; i < 10; i++)
                if (!await PhoneSendAsync(slug, spelling, sms)) refused++;
        output.WriteLine($"بابُ المَتجَر: أُرسِلَت {sms.To.Count} رِسالَةً لِرَقَمٍ واحِد، ورُدَّ {refused}");
        Assert.Equal(0, refused);
        Assert.Equal(30, sms.To.Count);

        var studio = new RecordingSms();
        var studioRefused = 0;
        var (_, other) = Fresh();
        foreach (var spelling in new[] { "0" + other, "+966" + other })
            for (var i = 0; i < 10; i++)
                try { await StudioAuth.SendPhoneCodeAsync(studio, spelling); }
                catch (InvalidOperationException) { studioRefused++; }
        output.WriteLine($"بابُ الاستوديو: أُرسِلَت {studio.To.Count} رِسالَةً لِرَقَمٍ واحِد، ورُدَّ {studioRefused}");
        Assert.Equal(0, studioRefused);
        Assert.Equal(20, studio.To.Count);
    }

    // ─── ٤. «اليَوم» — الرَمزُ يُرسَلُ إلى النَصِّ كَما كُتِب ──────────────────────────────────────────

    [Fact]
    public async Task Today_the_code_goes_to_a_saudi_number_as_typed()
    {
        var (slug, nine) = Fresh();
        var sms = new RecordingSms();
        foreach (var spelling in new[] { "0" + nine, "966" + nine, "00966" + nine, nine, "+966" + nine, " 0" + nine + " " })
            Assert.True(await PhoneSendAsync(slug, spelling.Trim(), sms));
        Assert.Equal(new[] { "0" + nine, "966" + nine, "00966" + nine, nine, "+966" + nine, "0" + nine }, sms.To);
    }

    // ─── ٥. «اليَوم» — بَدءُ نَفاذٍ بِلا حِصَّة ────────────────────────────────────────────────────────

    [Fact]
    public async Task Today_nafath_start_calls_the_channel_on_every_request_with_no_quota()
    {
        var (slug, _) = Fresh();
        var nafath = new CountingNafath();
        var refused = 0;
        for (var i = 0; i < 12; i++)
            if (!await NafathStartAsync(slug, "1012345678", nafath)) refused++;
        Assert.Equal(0, refused);
        Assert.Equal(12, nafath.Started);
    }

    // ─── ٦. «اليَوم» — المُحاوَلَةُ المُنتَهِيَةُ تَبقى في الذاكِرَة ────────────────────────────────────

    [Fact]
    public void Today_an_expired_attempt_stays_in_memory_after_new_codes_are_issued()
    {
        var (slug, nine) = Fresh();
        var expired = AuthHandlers.IssueAttempt(slug, "0" + nine, "111111", AuthHandlers.AuthKind.PhoneOtp,
            lifetime: TimeSpan.FromSeconds(-1));
        AuthHandlers.IssueAttempt(slug, "0" + nine + "1", "222222", AuthHandlers.AuthKind.PhoneOtp);
        Assert.True(AuthHandlers.Attempts.ContainsKey(expired));
        AuthHandlers.Attempts.TryRemove(expired, out _);
    }
}
