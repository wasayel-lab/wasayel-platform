using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Brevo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ قَناةُ البَريدِ عَبر HTTPS — ما يُرسَل بِالضَبط ═══════════════════
//
// **لِماذا وُجِدَت (‏2026-08-23)**: الـSpace يَحجُب مَنافِذَ SMTP الصادِرَة،
// فَـ`smtp` مَضبوطَةً ضَبطاً صَحيحاً لا تُرسِل. والمَنفَذُ ‏443 هُوَ
// المَضمونُ خُروجُه، وعَلَيه تَعمَل واجِهَةُ Brevo.
//
// **ولا مِفتاحَ حَقيقيّاً في هذِه الاختِبارات ولا نِداءَ شَبَكَة**:
// ‏`HttpMessageHandler` وَهمِيٌّ يَلتَقِط الطَلَبَ فَيُقاس **ما كُنّا
// سَنُرسِلُه** — وهو المَوضِعُ الَّذي يَنكَسِر صامِتاً (رَأسٌ خاطِئٌ يَرُدّ
// ‏401 بِلا تَوضيح، وحَقلٌ مَنسيٌّ يَرُدّ ‏400).

file sealed class CapturingHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        Body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        };
    }
}

/// <summary>مُعالِجٌ لا يَرُدّ أَبَداً — يُحاكي حَجبَ الشَبَكَةِ بِلا
/// رَفض. ويَحتَرِمُ رَمزَه، فَالقَطعُ يَأتي مِن مُهلَةِ القَناة.</summary>
file sealed class SilentHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

public class BrevoEmailChannelTests
{
    private static BrevoEmailChannel Channel(
        HttpMessageHandler handler, string apiKey = "test-key", string fromName = "وَسايِل",
        int timeoutSeconds = 10)
        => new(
            Options.Create(new BrevoEmailOptions
            {
                ApiKey = apiKey,
                From = "no-reply@wasayel.test",
                FromName = fromName,
                TimeoutSeconds = timeoutSeconds
            }),
            new HttpClient(handler),
            NullLogger<BrevoEmailChannel>.Instance);

    // ─── الشَكل: النُقطَة والرَأسُ والجِسم ────────────────────────────

    [Fact]
    public async Task Send_PostsToTheBrevoEndpoint_WithTheApiKeyHeader()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        await Channel(handler, apiKey: "brevo-secret").SendOtpAsync(
            "owner@wasayel.test", "483920", CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request.RequestUri!.ToString());
        Assert.Equal(BrevoEmailChannel.Endpoint, handler.Request.RequestUri.ToString());

        // الرَأسُ خاصٌّ بِـBrevo — لا `Authorization: Bearer`.
        var header = Assert.Single(handler.Request.Headers.GetValues(BrevoEmailChannel.ApiKeyHeader));
        Assert.Equal("brevo-secret", header);
        Assert.Null(handler.Request.Headers.Authorization);

        Assert.Equal("application/json",
            handler.Request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Send_CarriesTheRecipient_TheCode_AndTheSender()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        await Channel(handler).SendOtpAsync("owner@wasayel.test", "483920", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Body!);
        var root = doc.RootElement;

        Assert.Equal("owner@wasayel.test",
            root.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("no-reply@wasayel.test",
            root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("وَسايِل", root.GetProperty("sender").GetProperty("name").GetString());

        // الرَمزُ في الجِسمَين — نَصّاً وHTML.
        Assert.Contains("483920", root.GetProperty("textContent").GetString());
        Assert.Contains("483920", root.GetProperty("htmlContent").GetString());
    }

    /// <summary><b>ونَفسُ الرِسالَةِ لا نُسخَةٌ مِنها</b>: القيمَتانِ
    /// المُرسَلَتانِ هُما مُخرَجُ <c>OtpEmailMessage</c> بِعَينِه — وهو
    /// المُثَبَّتُ بايتِيّاً مُقابِلَ ما كانَت تُرسِلُه SMTP.</summary>
    [Fact]
    public async Task Send_UsesTheSharedMessage_NotACopy()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        await Channel(handler).SendOtpAsync("owner@wasayel.test", "483920", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal(OtpEmailMessage.Text("483920"),
            doc.RootElement.GetProperty("textContent").GetString());
        Assert.Equal(OtpEmailMessage.Html("483920"),
            doc.RootElement.GetProperty("htmlContent").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            doc.RootElement.GetProperty("subject").GetString()));
    }

    /// <summary>اسمُ المُرسِلِ اختِياريّ — يُحذَف ولا يُرسَل فارِغاً.
    /// ‏Brevo تَرُدّ ‏400 على حَقلٍ فارِغ.</summary>
    [Fact]
    public async Task Send_OmitsTheSenderName_WhenItIsBlank()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        await Channel(handler, fromName: "   ").SendOtpAsync(
            "owner@wasayel.test", "483920", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.False(doc.RootElement.GetProperty("sender").TryGetProperty("name", out _));
    }

    // ─── الفَشَل: يُرمى ولا يُبتَلَع ──────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]     // مِفتاحٌ خاطِئ
    [InlineData(HttpStatusCode.BadRequest)]       // مُرسِلٌ غَيرُ مُصادَق
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task NonSuccess_Throws_SoTheEndpointCanSay_send_failed(HttpStatusCode status)
    {
        var handler = new CapturingHandler(status, "{\"message\":\"nope\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Channel(handler).SendOtpAsync("owner@wasayel.test", "483920", CancellationToken.None));
        Assert.Contains(((int)status).ToString(), ex.Message);
    }

    /// <summary>ورِسالَةُ الخَطَإ **لا تَحمِل المِفتاح**. رِسالَةٌ تُرَدّ
    /// إلى الواجِهَةِ أَو تُكتَب في لوغٍ مُشتَرَكٍ فيها سِرٌّ هي تَسريب.</summary>
    [Fact]
    public async Task FailureMessage_NeverCarriesTheApiKey()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Channel(handler, apiKey: "xkeysib-super-secret").SendOtpAsync(
                "owner@wasayel.test", "483920", CancellationToken.None));
        Assert.DoesNotContain("xkeysib", ex.Message);
    }

    /// <summary>شَبَكَةٌ تَبتَلِعُ الطَلَبَ بِلا رَدّ — نَفسُ عُطلِ
    /// الـSpace. تُقطَع في مُهلَتِها لا تَعلَق.</summary>
    [Fact]
    public async Task ASilentNetwork_IsCutWithinTheTimeout()
    {
        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Channel(new SilentHandler(), timeoutSeconds: 2).SendOtpAsync(
                "owner@wasayel.test", "483920", CancellationToken.None));
        sw.Stop();

        Assert.Contains("send_timeout", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"عَلِقَ: {sw.Elapsed}");
    }

    // ─── التَركيب: خِياراتٌ ناقِصَةٌ تُغلِق عِندَ الإقلاعِ لا عِندَ الطَلَب ──

    [Fact]
    public void MissingApiKey_FailsFast_NamingTheConfigurationKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Channel(new CapturingHandler(HttpStatusCode.OK), apiKey: ""));
        Assert.Contains("Auth:Email:ApiKey", ex.Message);
    }

    [Fact]
    public void MissingFrom_FailsFast_NamingTheConfigurationKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new BrevoEmailChannel(
                Options.Create(new BrevoEmailOptions { ApiKey = "k", From = "" }),
                new HttpClient(new CapturingHandler(HttpStatusCode.OK)),
                NullLogger<BrevoEmailChannel>.Instance));
        Assert.Contains("Auth:Email:From", ex.Message);
    }

    // ─── العَلاماتُ الَّتي يَقرَؤُها حارِسُ الإقلاع ───────────────────

    /// <summary>قَناةُ إنتاج: لا رَمزَ مَعروضاً ولا عَلامَةَ مُحاكاة —
    /// وإلّا أَغلَقَ حارِسُ الإقلاعِ الإنتاجَ على المَضبوط.</summary>
    [Fact]
    public void IsARealChannel_NotAStub()
    {
        Assert.False(typeof(IDevelopmentStubChannel)
            .IsAssignableFrom(typeof(BrevoEmailChannel)));
        Assert.Null(Channel(new CapturingHandler(HttpStatusCode.OK)).DevHintCode);
        Assert.Equal("Brevo", Channel(new CapturingHandler(HttpStatusCode.OK)).ChannelName);
    }

    [Fact]
    public void DefaultTimeoutOption_IsTheGuardDefault()
        => Assert.Equal(OtpSendGuard.DefaultTimeoutSeconds,
            new BrevoEmailOptions().TimeoutSeconds);
}
