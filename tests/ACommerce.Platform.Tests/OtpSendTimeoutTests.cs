using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ مُهلَةُ إرسالِ الرَمز — «تَعليقٌ يَبدو عَمَلاً» يَصير فَشَلاً يُقرَأ ══
//
// **المَقيسُ في الإنتاج (‏2026-08-23، الـSpace، `Auth__Email__Provider=smtp`،
// المَنفَذ ‏587)**:
//
//     curl --max-time 90 -X POST …/studio/auth/email/login   ⇒  000
//     curl --max-time 90 -X POST …/ejar/auth/email/login     ⇒  000
//
// أَي: لا رَدَّ بَعدَ تِسعينَ ثانِيَة. والـ`000` لَيسَ رَمزَ حالَةٍ بَل
// انقِطاعُ العَميلِ نَفسِه — الخادِمُ لَم يَقُل شَيئاً قَطّ. السَبَب:
// الـSpace يَحجُب مَنافِذَ SMTP الصادِرَة، و`ConnectAsync` كانَ **بِلا
// مُهلَة**، فَعَلِقَ الطَلَبُ ومَعَه المُستَخدِمُ أَمامَ صَفحَةٍ تَدور.
//
// هذِه الاختِبارات هي **الحَدُّ المَقيس** (القاعِدَة ٢): «أَضَفنا مُهلَة»
// دَعوى، وخادِمٌ صامِتٌ يُجيبُه الكودُ بِخَطَإٍ في وَقتِه بُرهان.

public class OtpSendGuardTests
{
    // ─── القيمَةُ المُثَبَّتَة ────────────────────────────────────────
    // يُكتَب في `docs/DEPLOY.md` وفي `Auth__Email__TimeoutSeconds`.
    // انزِياحُها صامِتاً يُعيد فَتحَ نافِذَةِ التَعليق.

    [Fact]
    public void DefaultTimeout_IsTenSeconds()
    {
        Assert.Equal(10, OtpSendGuard.DefaultTimeoutSeconds);
        Assert.Equal(TimeSpan.FromSeconds(10), OtpSendGuard.DefaultTimeout);
    }

    /// <summary>نافِذَةُ الأُنبوبِ **أَوسَعُ** مِن نافِذَةِ المُزَوِّد —
    /// وإلّا تَسابَقا على العَشرِ نَفسِها فَتَبَدَّلَ سَطرُ اللوغِ بَينَ
    /// طَلَبٍ وطَلَب (مَقيسٌ حَيّاً: بابانِ مُتَتالِيانِ، رِسالَتان).
    /// المُزَوِّدُ يَقطَع أَوَّلاً ويَقولُ **لِماذا**، والأُنبوبُ سَقفٌ
    /// أَخيرٌ لِمَن لا مُهلَةَ لَه.</summary>
    [Fact]
    public void PipelineWindow_LeavesTheProviderRoomToSpeakFirst()
    {
        Assert.True(OtpSendGuard.PipelineTimeout > OtpSendGuard.DefaultTimeout);
        Assert.Equal(TimeSpan.FromSeconds(12), OtpSendGuard.PipelineTimeout);
    }

    /// <summary>الصِفرُ والسالِبُ **لا يَعنِيانِ «بِلا مُهلَة»** — يَرتَدّانِ
    /// إلى الافتِراضيّ. «بِلا مُهلَة» هي العِلَّةُ نَفسُها، فَلا يُترَك
    /// لَها بابٌ في التَهيئَة.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-600)]
    public void NonPositiveConfiguration_FallsBackToTheDefault(int seconds)
        => Assert.Equal(OtpSendGuard.DefaultTimeout, OtpSendGuard.Timeout(seconds));

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void PositiveConfiguration_IsHonoured(int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), OtpSendGuard.Timeout(seconds));

    // ─── القَطعُ الفِعليّ ─────────────────────────────────────────────

    /// <summary>مُزَوِّدٌ يَحتَرِمُ رَمزَه: يُلغى فَيَرمي، والحارِسُ
    /// يُتَرجِمُ الإلغاءَ إلى فَشَلٍ صَريح.</summary>
    [Fact]
    public async Task ProviderThatHonoursItsToken_IsCutWithinTheWindow()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OtpSendGuard.SendWithinAsync(
                token => Task.Delay(TimeSpan.FromSeconds(30), token),
                CancellationToken.None,
                TimeSpan.FromMilliseconds(300)));
        sw.Stop();

        Assert.Contains("send_timeout", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"القَطعُ تَأَخَّرَ: {sw.Elapsed}");
    }

    /// <summary><b>وهذا هو الفَرقُ بَينَ حارِسٍ وحارِسٍ بِالاسم</b>:
    /// مُزَوِّدٌ **يَبتَلِعُ رَمزَه** (أَو يَعلَق في نِداءٍ مُتَزامِنٍ
    /// داخِلَ مَكتَبَةٍ خارِجِيَّة) يَبقى مُعَلَّقاً إلى الأَبَدِ رَغمَ
    /// الرَمز. فَالمُهلَةُ تُقاس على المُنتَظِر لا على المُنَفِّذ.</summary>
    [Fact]
    public async Task ProviderThatIgnoresItsToken_IsStillCut()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OtpSendGuard.SendWithinAsync(
                _ => Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None),
                CancellationToken.None,
                TimeSpan.FromMilliseconds(300)));
        sw.Stop();

        Assert.Contains("send_timeout", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"القَطعُ تَأَخَّرَ: {sw.Elapsed}");
    }

    /// <summary>الرِسالَةُ لا تَحمِلُ عُنوانَ المُستَخدِمِ ولا الرَمز —
    /// تُكتَب في اللوغ.</summary>
    [Fact]
    public void TimeoutMessage_CarriesNeitherSubjectNorCode()
    {
        var message = OtpSendGuard.TimeoutMessage(TimeSpan.FromSeconds(10));
        Assert.StartsWith("send_timeout", message);
        Assert.Contains("10", message);
    }

    // ─── ما لا يُقطَع ─────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulSend_PassesThrough()
    {
        var sent = false;
        await OtpSendGuard.SendWithinAsync(
            _ => { sent = true; return Task.CompletedTask; },
            CancellationToken.None,
            TimeSpan.FromSeconds(5));
        Assert.True(sent);
    }

    /// <summary>فَشَلُ المُزَوِّدِ يُمَرَّر كَما هُوَ — الحارِسُ يُضيف
    /// مُهلَةً ولا يَبتَلِعُ خَطَأً.</summary>
    [Fact]
    public async Task ProviderFailure_PropagatesUnchanged()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OtpSendGuard.SendWithinAsync(
                _ => throw new InvalidOperationException("SMTP فَشِل الإرسال: 535"),
                CancellationToken.None,
                TimeSpan.FromSeconds(5)));
        Assert.Contains("535", ex.Message);
        Assert.DoesNotContain("send_timeout", ex.Message);
    }

    /// <summary>انقِطاعُ الطَلَبِ **لَيسَ** فَشَلَ إرسال: لا يُقال
    /// لِلمُستَخدِمِ «فَشِلَ الإرسال» لِأَنَّه أَغلَقَ التَبويب.</summary>
    [Fact]
    public async Task CallerCancellation_IsNotReportedAsSendFailure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OtpSendGuard.SendWithinAsync(
                token => Task.Delay(TimeSpan.FromSeconds(30), token),
                cts.Token,
                TimeSpan.FromSeconds(5)));
    }
}

// ═══ القَناةُ الفِعليَّة أَمامَ خادِمٍ يَقبَل ولا يُجيب ═════════════════
//
// **هذِه مُحاكاةُ العُطلِ المَقيسِ بِعَينِه**: الـSpace لا يَرفُض
// الاتِّصالَ بِـ`connection refused` (فَذاكَ كانَ سَيَفشَل فَوراً) — بَل
// يَبتَلِعُ الحُزَمَ فَتَبقى المُصافَحَةُ تَنتَظِر تَحيَّةَ الخادِم
// (`220 …`) الَّتي لا تَأتي. `TcpListener` يَقبَل ولا يَكتُب شَيئاً يُعيدُ
// المَشهَدَ نَفسَه مَحَلِّيّاً وبِلا شَبَكَةٍ خارِجِيَّة.

public class SmtpEmailChannelTimeoutTests
{
    private static SmtpEmailChannel Channel(int port, int timeoutSeconds) =>
        new(Options.Create(new SmtpEmailOptions
        {
            Host = "127.0.0.1",
            Port = port,
            From = "no-reply@wasayel.test",
            FromName = "وَسايِل",
            TimeoutSeconds = timeoutSeconds
        }), NullLogger<SmtpEmailChannel>.Instance);

    [Fact]
    public void DefaultTimeoutOption_IsTheGuardDefault()
        => Assert.Equal(OtpSendGuard.DefaultTimeoutSeconds,
            new SmtpEmailOptions().TimeoutSeconds);

    [Fact]
    public async Task ASilentServer_FailsWithinTheTimeout_InsteadOfHanging()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // نَقبَل الاتِّصالَ ولا نَكتُب حَرفاً — بِلا هذا يُغلَق المِقبَسُ
        // فَيَفشَل الاتِّصالُ فَوراً ولا يُقاسُ شَيء.
        var accepting = Task.Run(async () =>
        {
            try
            {
                using var socket = await listener.AcceptSocketAsync();
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
            catch { /* الاختِبارُ انتَهى */ }
        });

        try
        {
            var sw = Stopwatch.StartNew();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Channel(port, timeoutSeconds: 2)
                    .SendOtpAsync("owner@wasayel.test", "123456", CancellationToken.None));
            sw.Stop();

            Assert.Contains("send_timeout", ex.Message);
            // ‏90 ثانِيَة كانَ المَقيسَ قَبل. الحَدُّ هُنا أَوسَعُ مِن
            // المُهلَةِ بِهامِشٍ لِبُطءِ آلَةِ البِناء، وأَضيَقُ بِكَثيرٍ
            // مِن نافِذَةِ التَعليقِ الَّتي كَتَبَت المَوجَة.
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
                $"الإرسالُ عَلِقَ: {sw.Elapsed}");
        }
        finally
        {
            listener.Stop();
            await Task.WhenAny(accepting, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    /// <summary>والمُهلَةُ لا تَبتَلِعُ الفَشَلَ السَريع: مَنفَذٌ مُغلَقٌ
    /// يَرتَدّ فَوراً بِفَشَلٍ صَريح، لا بِانتِظارِ عَشرِ ثَوانٍ.</summary>
    [Fact]
    public async Task AClosedPort_FailsImmediately()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();   // المَنفَذُ صارَ مُغلَقاً

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Channel(port, timeoutSeconds: 10)
                .SendOtpAsync("owner@wasayel.test", "123456", CancellationToken.None));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"تَأَخَّرَ الرَفض: {sw.Elapsed}");
    }
}

// ═══ الحارِسُ في الأُنبوب — لا في القَناةِ وَحدَها ═════════════════════
//
// القاعِدَة ٦: مُهلَةٌ داخِلَ مُزَوِّدٍ تَحرُسُ ذلِكَ المُزَوِّدَ وَحدَه،
// والتالي يُكتَب بِلا حارِسٍ ولا يُلاحَظ — وهذا ما وَقَعَ فِعلاً. فَالبُرهانُ
// المَطلوبُ أَن **مَسارَ الاستِدعاءِ المُشتَرَك** يَقطَع حَتّى لَو كانَت
// القَناةُ نَفسُها بِلا مُهلَةٍ إطلاقاً.

file sealed class HangingEmailChannel : IEmailOtpChannel
{
    public string ChannelName => "Hanging";
    public string? DevHintCode => null;
    // تَتَجاهَل رَمزَها عَمداً — تُمَثِّل مُزَوِّداً بِلا مُهلَة.
    public Task SendOtpAsync(string email, string code, CancellationToken ct)
        => Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None);
}

public class OtpSendPipelineTimeoutTests
{
    /// <summary>بابُ الاستوديو — المَوضِعُ الوَحيدُ الَّذي يُنتِج جَلسَةَ
    /// مُشرِفِ مَنَصَّة، وهُوَ الَّذي عَلِقَ ‏90+ ثانِيَة. القَناةُ هُنا
    /// بِلا مُهلَةٍ إطلاقاً، والقَطعُ يَأتي مِن الأُنبوب.
    ///
    /// <para><b>ولِماذا يَستَغرِقُ عَشرَ ثَوانٍ</b>: لِأَنّ المَقيسَ هُوَ
    /// المُهلَةُ الافتِراضِيَّةُ نَفسُها الَّتي تَعمَل في الإنتاج، لا قيمَةٌ
    /// مُصَغَّرَةٌ لِلاختِبار. تَصغيرُها كانَ سَيَقيسُ شَيئاً آخَر.</para></summary>
    [Fact]
    public async Task StudioEmailDoor_IsCutByThePipeline_EvenWithATimeoutlessChannel()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioAuth
                .SendEmailCodeAsync(new HangingEmailChannel(), "pipe-studio@wasayel.test"));
        sw.Stop();

        Assert.Contains("send_timeout", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"الأُنبوبُ لَم يَقطَع: {sw.Elapsed}");
    }

    /// <summary>وبابُ المُستَأجِر — `POST /{slug}/auth/email/login` — هُوَ
    /// المَوضِعُ الثاني المَقيس. مَوضِعا استِدعاءٍ مُختَلِفان، فَحَدّان
    /// مُختَلِفان: بُرهانُ أَحَدِهِما لا يُغني عَن الآخَر (القاعِدَة ٢).</summary>
    [Fact]
    public async Task TenantEmailDoor_IsCutByThePipeline_EvenWithATimeoutlessChannel()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ACommerce.Kit.Auth.Server.AuthHandlers.RequestEmailOtpHandler(
                new RequestEmailOtp("pipe-tenant@wasayel.test"),
                new ResolvedTenant("ejar"),
                new HangingEmailChannel(),
                CancellationToken.None));
        sw.Stop();

        Assert.Contains("send_timeout", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"الأُنبوبُ لَم يَقطَع: {sw.Elapsed}");
    }
}

file sealed class ResolvedTenant(string slug) : ACommerce.Platform.Shared.ITenantContext
{
    public string Slug => slug;
    public string Name => slug;
    public string BrandColor => "#000000";
    public string AuthChannel => "email";
    public string TagLine => "";
    public string City => "";
    public bool IsResolved => true;
    public bool HasRoles => false;
}
