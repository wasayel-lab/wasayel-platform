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
//
// **العِلاج** (‏2026-10-24): `AuthHandlers.PhoneDestination` — رَقَمٌ سُعودِيٌّ بِأَيِّ كِتابَةٍ ⇒ `+9665XXXXXXXX`، وما سِواه كَما كُتِب — مِفتاحُ
// الحِصَّةِ ووِجهَةُ الرَمزِ في البابَين، والمَوضوعُ (المُحاوَلَةُ والمُستَخدِم) كَما كُتِب؛ وبَدءُ نَفاذٍ يَستَهلِكُ الحِصَّةَ نَفسَها بِمِفتاحِ
// `{tenant}|nafath:{nid}` وبابُه يَرُدُّ `send_failed`؛ و`EvictExpiredAttempts` قَبلَ كُلِّ إصدار؛ وسَقفُ المَنَصَّةِ `ACOMMERCE_OTP_SENDS_PER_HOUR`
// مُطفَأٌ حَتّى يَضبِطَه المالِك. الصُفوفُ الأَربَعَةُ انقَلَبَت بِأَسمائِها، والصَفّانِ الآخَرانِ كَما أُودِعا.
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
        public Task SendNoticeAsync(string phone, string text, CancellationToken ct) => Task.CompletedTask;
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

    // ─── ٣. ثَلاثُ كِتاباتٍ لِرَقَمٍ واحِدٍ حِصَّةٌ واحِدَة، في البابَين ──────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_three_spellings_of_one_saudi_number_are_three_quota_keys_at_both_doors`.

    [Fact]
    public async Task Three_spellings_of_one_saudi_number_share_one_quota_at_both_doors()
    {
        var (slug, nine) = Fresh();
        var sms = new RecordingSms();
        var refused = 0;
        foreach (var spelling in new[] { "0" + nine, "966" + nine, "+966" + nine })
            for (var i = 0; i < 10; i++)
                if (!await PhoneSendAsync(slug, spelling, sms)) refused++;
        output.WriteLine($"بابُ المَتجَر: أُرسِلَت {sms.To.Count} رِسالَةً لِرَقَمٍ واحِد، ورُدَّ {refused}");
        Assert.Equal(20, refused);
        Assert.Equal(10, sms.To.Count);

        var studio = new RecordingSms();
        var studioRefused = 0;
        var (_, other) = Fresh();
        foreach (var spelling in new[] { "0" + other, "+966" + other })
            for (var i = 0; i < 10; i++)
                try { await StudioAuth.SendPhoneCodeAsync(studio, spelling); }
                catch (InvalidOperationException) { studioRefused++; }
        output.WriteLine($"بابُ الاستوديو: أُرسِلَت {studio.To.Count} رِسالَةً لِرَقَمٍ واحِد، ورُدَّ {studioRefused}");
        Assert.Equal(10, studioRefused);
        Assert.Equal(10, studio.To.Count);
        Assert.All(studio.To, to => Assert.Equal("+966" + other, to));
    }

    // ─── ٤. والرَمزُ يُرسَلُ إلى الرَقَمِ بِصيغَةِ E.164 ─────────────────────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_the_code_goes_to_a_saudi_number_as_typed`.

    [Fact]
    public async Task The_code_goes_to_a_saudi_number_in_e164_whatever_the_spelling()
    {
        var (slug, nine) = Fresh();
        var sms = new RecordingSms();
        foreach (var spelling in new[] { "0" + nine, "966" + nine, "00966" + nine, nine, "+966" + nine, " 0" + nine + " " })
            Assert.True(await PhoneSendAsync(slug, spelling.Trim(), sms));
        Assert.Equal(Enumerable.Repeat("+966" + nine, 6), sms.To);
    }

    /// <summary><b>الدالَّةُ بِحُدودِها</b>: كُلُّ كِتابَةٍ سُعودِيَّةٍ لِلجَوّالِ تَصيرُ E.164 (مُوجَب)، وما لَيسَ جَوّالاً سُعودِيّاً يَبقى حَرفاً (سالِب) —
    /// فَلا يَصيرُ نَصّانِ مُختَلِفانِ مِفتاحاً واحِداً إلّا وهُما رَقَمٌ واحِد.</summary>
    [Theory]
    [InlineData("0551234567", "+966551234567")]
    [InlineData("551234567", "+966551234567")]
    [InlineData("966551234567", "+966551234567")]
    [InlineData("00966551234567", "+966551234567")]
    [InlineData("+966551234567", "+966551234567")]
    [InlineData("+966 55 123 4567", "+966551234567")]
    [InlineData("055-123-4567", "+966551234567")]
    [InlineData("(055) 123 4567", "+966551234567")]
    [InlineData("  0551234567  ", "+966551234567")]
    // ما لَيسَ جَوّالاً سُعودِيّاً — كَما كُتِب (بَعدَ القَصّ)
    [InlineData("+201001234567", "+201001234567")]
    [InlineData("00201001234567", "00201001234567")]
    [InlineData("0112345678", "0112345678")]
    [InlineData("05512345678", "05512345678")]
    [InlineData("+9665512345678", "+9665512345678")]
    [InlineData("oto-register:abc", "oto-register:abc")]
    [InlineData("055x1234567", "055x1234567")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_saudi_mobile_in_any_spelling_becomes_e164_and_anything_else_stays_as_typed(string? typed, string expected)
        => Assert.Equal(expected, AuthHandlers.PhoneDestination(typed));

    // ─── ٥. بَدءُ نَفاذٍ يَستَهلِكُ حِصَّةَ هُوِيَّتِه ─────────────────────────────────────────────────

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_nafath_start_calls_the_channel_on_every_request_with_no_quota`.
    [Fact]
    public async Task Nafath_start_spends_the_send_quota_of_its_national_id()
    {
        var (slug, _) = Fresh();
        var nafath = new CountingNafath();
        var refused = 0;
        for (var i = 0; i < 12; i++)
            if (!await NafathStartAsync(slug, "1012345678", nafath)) refused++;
        Assert.Equal(2, refused);
        Assert.Equal(10, nafath.Started);

        // والمِفتاحُ لا يُصادِفُ جَوّالاً بِالأَرقامِ نَفسِها في المَتجَرِ نَفسِه.
        Assert.True(await PhoneSendAsync(slug, "1012345678", new RecordingSms()));
        Assert.True(await NafathStartAsync(slug, "1012345679", nafath));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2 الجَولَة ١، صُغرى؛ كانَ <c>Today_the_nafath_http_door_is_the_throwing_handler_so_a_refused_start_escapes_it</c>،
    /// مُودَعٌ في <c>4618eec</c>): كانَ وَسمُ <c>[WolverinePost("/{slug}/auth/nafath/request")]</c> عَلى <c>RequestNafathHandler</c> فَيَصعَدُ رَدُّه مِن جِسمِ
    /// النُقطَة — ومَقيسٌ حَيّاً: كُلُّ طَلَبٍ ‏500 بِـ<c>tenant_required</c>، فَالنُقطَةُ لَم تَعمَل لِأَحَدٍ ولا مُنادِيَ لَها. <b>والآن</b>: لا وَسمَ عَلى الدالَّة، وبابُها
    /// الوَحيدُ الاستِمارَةُ (يَلتَقِطُ ما تَرميه — الصَفُّ أَدناه)؛ والدالَّةُ تَرمي كَما كانَت — فَما يُرَدُّ يُرَدُّ في البابِ الَّذي يَلتَقِطُه.</para>
    /// </summary>
    [Fact]
    public async Task The_nafath_start_has_no_http_door_of_its_own_and_the_form_door_is_its_only_caller()
    {
        var auth = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Source(
            "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs"));
        Assert.DoesNotContain("nafath/request", auth, StringComparison.Ordinal);
        Assert.Matches(@"\}\s*public static async Task<NafathPending> RequestNafathHandler\(", auth);

        var callers = 0;
        foreach (var (_, text) in EntitlementContractTests.SourceFiles())
            callers += System.Text.RegularExpressions.Regex.Matches(WriteEndpointGuardTests.StripComments(text), @"\bRequestNafathHandler\(").Count;
        Assert.Equal(2, callers);   // التَعريفُ ونِداءُ بابِ الاستِمارَة

        var (slug, _) = Fresh();
        var nafath = new CountingNafath();
        for (var i = 0; i < 10; i++)
            await AuthHandlers.RequestNafathHandler(new RequestNafath("1098765432"), new VendorRegisterBucketTests.ResolvedTenant(slug), nafath, CancellationToken.None);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AuthHandlers.RequestNafathHandler(new RequestNafath("1098765432"), new VendorRegisterBucketTests.ResolvedTenant(slug), nafath, CancellationToken.None));
        Assert.StartsWith("rate_limited", refused.Message, StringComparison.Ordinal);
        var unresolved = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AuthHandlers.RequestNafathHandler(new RequestNafath("1098765432"), new UnresolvedTenant(), nafath, CancellationToken.None));
        Assert.Equal("tenant_required", unresolved.Message);
        output.WriteLine($"لا بابَ HTTP لِلبَدء · مَواضِعُ RequestNafathHandler( {callers} · الحادِيَ عَشَرَ «{refused.Message}» يَبلُغُ بابَ الاستِمارَة");
    }

    /// <summary>مُستَأجِرٌ لَم يُحَلّ — ما يَراه الطَلَبُ بِسلاجٍ لا مَتجَرَ لَه.</summary>
    internal sealed class UnresolvedTenant : ACommerce.Platform.Shared.ITenantContext
    {
        public string Slug => "";
        public string Name => "";
        public string BrandColor => "";
        public string AuthChannel => "";
        public string TagLine => "";
        public string City => "";
        public bool IsResolved => false;
        public bool HasRoles => false;
    }

    /// <summary>وبابُ الاستِمارَةِ يَلتَقِطُ الرَدَّ فَيُعيدُ رِسالَةً — <c>send_failed</c>، النَصُّ القائِمُ في القامُوس — لا صَفحَةَ ‏500 (كَبابِ الاستوديو).</summary>
    [Fact]
    public void The_nafath_door_turns_a_refused_start_into_send_failed_not_a_server_error()
    {
        var ext = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Endpoints());
        var at = ext.IndexOf("app.MapPost(\"/{slug}/auth/nafath/login\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا بابَ نَفاذ — الأَداةُ عَمياء.");
        var body = ext[at..ext.IndexOf("app.MapPost(", at + 10, StringComparison.Ordinal)];
        var call = body.IndexOf("AuthHandlers.RequestNafathHandler(", StringComparison.Ordinal);
        var tryAt = body.LastIndexOf("try {", call, StringComparison.Ordinal);
        var catchAt = body.IndexOf("catch (Exception) { return Results.Redirect(Link(req, slug, \"login?err=send_failed\")); }", call, StringComparison.Ordinal);
        Assert.True(call > 0 && tryAt > 0 && catchAt > call, $"النِداءُ@{call} المُحاوَلَةُ@{tryAt} الالتِقاطُ@{catchAt}");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(body, @"RequestNafathHandler\(").Count);
        Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(
            ACommerce.Platform.I18n.LocaleCatalog.Arabic, "auth.login.err_send_failed")));
    }

    /// <summary><b>انقَلَبَ بِاسمِه</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-9) مِن <c>Today_the_phone_door_lets_a_refused_or_failed_send_escape_as_a_server_error</c>:
    /// بابُ الجَوّالِ يَلتَقِطُ كُلَّ ما يَرميه الإرسالُ (رَدُّ الحِصَّةِ وعُطلُ القَناةِ ومُهلَتُها) فَيَعودُ بِـ<c>send_failed</c> والدَورِ — النَصُّ القائِمُ في
    /// القامُوس — ولا يَحمِلُ الرَقَمَ في رابِطِ الخَطَإ؛ والجاران كَما كانا.</summary>
    [Fact]
    public void The_phone_door_turns_a_refused_or_failed_send_into_send_failed_not_a_server_error()
    {
        var ext = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Endpoints());
        var at = ext.IndexOf("app.MapPost(\"/{slug}/auth/phone/login\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا بابَ جَوّال — الأَداةُ عَمياء.");
        var body = ext[at..ext.IndexOf("app.MapPost(", at + 10, StringComparison.Ordinal)];
        var call = body.IndexOf("AuthHandlers.RequestPhoneOtpHandler(", StringComparison.Ordinal);
        var tryAt = body.LastIndexOf("try {", call, StringComparison.Ordinal);
        var catchAt = body.IndexOf("catch (Exception) { return Results.Redirect(Link(req, slug, $\"login?err=send_failed{asParam}\")); }", call, StringComparison.Ordinal);
        var verify = body.IndexOf("login?stage=verify", StringComparison.Ordinal);
        Assert.True(call > 0 && tryAt > 0 && catchAt > call && verify > catchAt,
            $"النِداءُ@{call} المُحاوَلَةُ@{tryAt} الالتِقاطُ@{catchAt} التَحَقُّق@{verify}");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, @"RequestPhoneOtpHandler\("));

        var email = ext[ext.IndexOf("app.MapPost(\"/{slug}/auth/email/login\"", StringComparison.Ordinal)..];
        Assert.Contains("err=send_failed", email[..email.IndexOf("app.MapPost(", 10, StringComparison.Ordinal)], StringComparison.Ordinal);
        var studio = ext[ext.IndexOf("app.MapPost(\"/studio/auth/login\"", StringComparison.Ordinal)..];
        Assert.Contains("catch (Exception) { return Results.Redirect(\"/studio/auth?err=send_failed\"); }",
            studio[..studio.IndexOf("app.MapPost(", 10, StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    // ─── ٦. المُحاوَلَةُ المُنتَهِيَةُ تُطرَدُ حينَ يُصدَرُ ما بَعدَها ───────────────────────────────────

    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_an_expired_attempt_stays_in_memory_after_new_codes_are_issued`.
    [Fact]
    public void An_expired_attempt_is_evicted_when_the_next_code_is_issued()
    {
        var (slug, nine) = Fresh();
        var expired = AuthHandlers.IssueAttempt(slug, "0" + nine, "111111", AuthHandlers.AuthKind.PhoneOtp,
            lifetime: TimeSpan.FromSeconds(-1));
        var live = AuthHandlers.IssueAttempt(slug, "0" + nine + "1", "222222", AuthHandlers.AuthKind.PhoneOtp);
        Assert.False(AuthHandlers.Attempts.ContainsKey(expired));
        Assert.True(AuthHandlers.Attempts.ContainsKey(live));
        AuthHandlers.Attempts.TryRemove(live, out _);
    }

    // ─── ٧. سَقفُ المَنَصَّة — مُطفَأٌ حَتّى يَضبِطَه المالِك، ويَرُدُّ ما فَوقَه حينَ يُضبَط ──────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("ten")]
    [InlineData("1.5")]
    [InlineData(" +7")]
    public void A_platform_cap_that_is_not_a_positive_whole_number_is_no_cap(string? raw)
    {
        Assert.Null(AuthHandlers.ParseLimit(raw));
        Assert.True(AuthHandlers.TryConsumeGlobalSendQuota(AuthHandlers.ParseLimit(raw)));
    }

    [Fact]
    public void The_platform_cap_is_off_today_and_refuses_beyond_the_number_once_set()
    {
        Assert.Equal("ACOMMERCE_OTP_SENDS_PER_HOUR", AuthHandlers.GlobalSendsPerHourVar);
        Assert.Null(Environment.GetEnvironmentVariable(AuthHandlers.GlobalSendsPerHourVar));
        Assert.Null(AuthHandlers.GlobalSendsPerHour);
        Assert.Equal(3, AuthHandlers.ParseLimit(" 3 "));

        // العَدّادُ لِلمَنَصَّةِ كُلِّها — وهذا الصَفُّ وَحدَه يَستَهلِكُه بِرَقَم؛ الأَبوابُ تُمَرِّرُ `GlobalSendsPerHour` (‏null هُنا) فَلا تَمَسُّه.
        var passed = 0;
        for (var i = 0; i < 5; i++) if (AuthHandlers.TryConsumeGlobalSendQuota(3)) passed++;
        Assert.Equal(3, passed);
        Assert.True(AuthHandlers.TryConsumeGlobalSendQuota(null));
    }
}
