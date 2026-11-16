using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.Twilio;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ شَكلُ طَلَبِ Twilio — يُثَبَّتُ عَلى مُستَوى القالِبِ قَبلَ أَن يَصيرَ عَميلُه `ProviderHttp` (‏2026-11-12، الموجَة ١٣ W13-6·١١ — ADR-051) ═══════
//
// **لِماذا قَبلَ التَبديل** (القاعِدَتانِ ٣ و١٣): البَندُ ١١ يَنزِعُ `HttpClient` مِن `TwilioSmsChannel` ويُمَرِّرُ إرسالَه بِالمُستَدعي المَعدود (`ProviderHttp`)،
// ويَنقُلُ رَأسَ Basic مِن `DefaultRequestHeaders` في الباني إلى الطَلَب، ويَقلِبُ `AddHttpClient<IOtpChannel, TwilioSmsChannel>` إلى `AddProviderHttp`.
// ووَعدُه أَنّ **الطَلَبَ الَّذي يَبلُغُ Twilio لا يَتَغَيَّرُ بايتاً**: الفِعلُ والعُنوانُ والإصدارُ والرُؤوسُ والجِسم، وما يَرميه البابُ حينَ يَرُدُّ Twilio بِخَطَأ.
// والوَعدُ لا يُحفَظُ بِاسمِه (القاعِدَة ٢): هذا المِلَفُّ يَلتَقِطُ الطَلَبَ عِندَ المُعالِجِ الأَوَّلِ ويُثَبِّتُه، ويُودَعُ وَحدَه، ثُمَّ يُعادُ تَشغيلُه بَعدَ التَبديلِ
// **بِلا تَعديلِ حَرف** (بَصمَتُه في رِسالَةِ الكوميتَين).
//
// **والالتِقاطُ بِما يَبقى بَعدَ التَبديل** (التَصميم §6.3): `ConfigureHttpClientDefaults(… ConfigurePrimaryHttpMessageHandler(() => recorder))` يَسري عَلى كُلِّ
// عَميلٍ مُسَمّىً أَو مُطَبَّعٍ يَبنيه المَصنَع — فَيَبقى بَعدَ `AddHttpClient<T>` ← `AddProviderHttp<T>`. والمِلَفُّ لا يُسَمّي باني القَناةِ ولا نَوعاً يولَدُ
// في البَند: التَسجيلُ (`AddTwilioSmsChannel`) والأَبوابُ (`AuthHandlers.RequestPhoneOtpHandler` و`StudioAuth.SendPhoneCodeAsync`) والقَناةُ بِواجِهَتِها.
//
// **وبِلا قاعِدَةٍ ولا شَبَكَة**: المُعالِجُ يُجيبُ بِلا اتِّصال، والاعتِمادُ وَهمِيٌّ (`AC` + أَصفار)، والأَرقامُ عَشوائِيَّةٌ لِكُلِّ صَفٍّ فَلا تَتَقاطَعُ حِصَّةُ
// الإرسالِ مَعَ صَفٍّ آخَر. والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): كُلُّ صَفٍّ يَعُدُّ الطَلَباتِ ويَفشَلُ إن لَم يَكُن واحِداً.

public class TwilioRequestShapeCharacterizationTests(ITestOutputHelper output)
{
    private const string Sid = "AC00000000000000000000000000000000";
    private const string Token = "characterization-token-0";
    private const string From = "+15005550006";
    private const string Url = "https://api.twilio.com/2010-04-01/Accounts/" + Sid + "/Messages.json";

    private static readonly string Basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Sid}:{Token}"));

    /// <summary>طَلَبٌ كَما بَلَغَ المُعالِجَ الأَوَّل.</summary>
    internal sealed record Seen(string Method, string Uri, string Version, string RequestHeaders, string ContentHeaders, string Authorization, string Body);

    /// <summary>المُعالِجُ الأَوَّل: يَلتَقِطُ ويُجيبُ بِلا شَبَكَة.</summary>
    internal sealed class Recorder : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        public List<Seen> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var seen = new Seen(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Version.ToString(),
                string.Join(",", request.Headers.Select(h => h.Key).Order(StringComparer.Ordinal)),
                request.Content is null ? "" : string.Join(",", request.Content.Headers.Select(h => $"{h.Key}: {string.Join(";", h.Value)}").Order(StringComparer.Ordinal)),
                request.Headers.Authorization is { } a ? $"{a.Scheme} {a.Parameter}" : "",
                body);
            lock (Requests) Requests.Add(seen);
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"sid\":\"SM0\"}", Encoding.UTF8, "application/json") };
        }
    }

    private static (ServiceProvider Services, Recorder Recorder) Compose(Action<TwilioOptions>? configure = null)
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => recorder));
        services.AddTwilioSmsChannel(configure ?? (o => { o.AccountSid = Sid; o.AuthToken = Token; o.FromNumber = From; }));
        return (services.BuildServiceProvider(), recorder);
    }

    private static string FreshSlug() => $"tw-ch-{Guid.NewGuid():N}"[..20];

    private static string FreshNine() => "5" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Form(string to, string body)
        => $"To={Uri.EscapeDataString(to)}&From={Uri.EscapeDataString(From)}&Body={Uri.EscapeDataString(body).Replace("%20", "+")}";

    private Seen Single(Recorder recorder)
    {
        foreach (var r in recorder.Requests)
            output.WriteLine($"[twilio] {r.Method} {r.Uri} HTTP/{r.Version} · رُؤوس [{r.RequestHeaders}] · المُحتَوى [{r.ContentHeaders}] · {r.Authorization.Split(' ')[0]} · «{r.Body}»");
        Assert.True(recorder.Requests.Count == 1, $"أَداةٌ عَمياءُ أَو بابٌ يُرسِلُ غَيرَ مَرَّة: {recorder.Requests.Count} طَلَباً.");
        return recorder.Requests[0];
    }

    private static void AssertShape(Seen seen, string to, Regex body)
    {
        Assert.Equal("POST", seen.Method);
        Assert.Equal(Url, seen.Uri);
        Assert.Equal("1.1", seen.Version);
        Assert.Equal("Authorization", seen.RequestHeaders);
        Assert.Equal("Basic " + Basic, seen.Authorization);
        // مَقيسٌ لا مُخَمَّن: التَشغيلُ الأَوَّلُ بِـ«Content-Type» وَحدَه أَحمَر — الطولُ يُحسَبُ حينَ يُقرَأُ الجِسمُ، والجِسمُ المُرَمَّزُ ASCII.
        Assert.Equal($"Content-Length: {Encoding.ASCII.GetByteCount(seen.Body)},Content-Type: application/x-www-form-urlencoded", seen.ContentHeaders);
        Assert.StartsWith($"To={Uri.EscapeDataString(to)}&From={Uri.EscapeDataString(From)}&Body=", seen.Body, StringComparison.Ordinal);
        Assert.Matches(body, seen.Body);
    }

    private static readonly Regex OtpBody = new(@"&Body=Your\+verification\+code%3A\+[1-9][0-9]{5}$", RegexOptions.CultureInvariant);

    /// <summary><b>بابُ المَتجَر</b> (<c>POST /{slug}/auth/phone/login</c> ← <c>RequestPhoneOtpHandler</c>): رَقَمٌ سُعودِيٌّ بِأَيِّ كِتابَةٍ يَصِلُ <c>+9665…</c>،
    /// والطَلَبُ واحِد: <c>POST</c> إلى مَورِدِ الرَسائِلِ بِرَأسِ Basic، واستِمارَةٌ بِثَلاثَةِ حُقولٍ بِتَرتيبِها، والرَمزُ سِتَّةُ أَرقام.</summary>
    [Fact]
    public async Task The_store_door_posts_one_form_to_the_messages_resource_with_basic_auth()
    {
        var (services, recorder) = Compose();
        await using var _ = services;
        var channel = services.GetRequiredService<IOtpChannel>();
        var nine = FreshNine();

        var result = await AuthHandlers.RequestPhoneOtpHandler(new RequestPhoneOtp("0" + nine), new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()),
            channel, CancellationToken.None);

        var seen = Single(recorder);
        AssertShape(seen, "+966" + nine, OtpBody);
        Assert.Equal(32, result.AttemptId.Length);
        Assert.Equal("", result.DisplayCode);
        Assert.Equal($"أَرسَلنا الكود إلى 0{nine}", result.Hint);
    }

    /// <summary><b>بابُ الاستوديو</b> (<c>StudioAuth.SendPhoneCodeAsync</c>): الطَلَبُ نَفسُه بِحَرفِه إلى الرَقَمِ المُطَبَّع، ولا تَلميحَ مَعَ مُزَوِّدٍ فِعلِيّ.</summary>
    [Fact]
    public async Task The_studio_door_posts_the_same_form_to_the_canonical_number()
    {
        var (services, recorder) = Compose();
        await using var _ = services;
        var channel = services.GetRequiredService<IOtpChannel>();
        var nine = FreshNine();

        var hint = await StudioAuth.SendPhoneCodeAsync(channel, "966" + nine);

        AssertShape(Single(recorder), "+966" + nine, OtpBody);
        Assert.Null(hint);
    }

    /// <summary><b>الإشعار</b> (<c>SendNoticeAsync</c> — مُستَهلِكُه <c>AccountContacts.NoticeLinkedAsync</c>): المَورِدُ والرَأسُ والمُرسِلُ نَفسُها، والنَصُّ جِسمُ
    /// الرِسالَةِ بِحَرفِه، والرَقَمُ بِلا <c>+</c> يُلحَقُ بِه <c>+</c> (المُطَبَّعُ يَمُرُّ كَما هو).</summary>
    [Fact]
    public async Task The_notice_posts_its_text_as_the_body_and_prefixes_a_plus()
    {
        var (services, recorder) = Compose();
        await using var _ = services;
        var channel = services.GetRequiredService<IOtpChannel>();
        var nine = FreshNine();
        const string text = "أُضيفَت وَسيلَةُ دُخولٍ إلى حِسابِك: a***@b.co";

        await channel.SendNoticeAsync("966" + nine, text, CancellationToken.None);

        var seen = Single(recorder);
        AssertShape(seen, "+966" + nine, new Regex("&Body=" + Regex.Escape(Uri.EscapeDataString(text).Replace("%20", "+")) + "$"));
        Assert.Equal(Form("+966" + nine, text), seen.Body);
    }

    /// <summary><b>رَدُّ Twilio بِخَطَإٍ يَرمي بِرِسالَةِ القَناة</b> عِندَ البابِ — <c>InvalidOperationException</c> «Twilio فَشِل: {الرَمز}» لِلرَفضِ ولِخَطَإ الخادِمِ مَعاً،
    /// بَعدَ طَلَبٍ واحِدٍ بِالشَكلِ نَفسِه (لا إعادَة).</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_refusal_or_a_server_error_throws_the_channels_message_at_the_door(HttpStatusCode status)
    {
        var (services, recorder) = Compose();
        await using var _ = services;
        recorder.Status = status;
        var channel = services.GetRequiredService<IOtpChannel>();
        var nine = FreshNine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AuthHandlers.RequestPhoneOtpHandler(
            new RequestPhoneOtp("+966" + nine), new VendorRegisterBucketTests.ResolvedTenant(FreshSlug()), channel, CancellationToken.None));

        output.WriteLine($"[twilio] {(int)status} ⇒ {ex.GetType().Name}: {ex.Message}");
        Assert.Equal($"Twilio فَشِل: {(int)status}", ex.Message);
        AssertShape(Single(recorder), "+966" + nine, OtpBody);
    }

    /// <summary><b>التَسجيل</b>: <c>IOtpChannel</c> عابِرٌ (نُسخَةٌ لِكُلِّ حَلّ) يُحَلُّ إلى <c>TwilioSmsChannel</c> باسمِه وبِلا تَلميح؛ وإعدادٌ ناقِصٌ يَرمي عِندَ الحَلِّ
    /// بِرِسالَتِه — فَالقَناةُ لا تولَدُ بِلا مُرسِل.</summary>
    [Fact]
    public async Task The_registration_resolves_a_transient_twilio_channel_and_refuses_missing_settings()
    {
        var (services, recorder) = Compose();
        await using var _ = services;
        var descriptors = services.GetRequiredService<IServiceProviderIsService>();
        Assert.True(descriptors.IsService(typeof(IOtpChannel)));
        var first = services.GetRequiredService<IOtpChannel>();
        var second = services.GetRequiredService<IOtpChannel>();
        Assert.IsType<TwilioSmsChannel>(first);
        Assert.NotSame(first, second);
        Assert.Equal(("Twilio", (string?)null), (first.ChannelName, first.DevHintCode));
        Assert.Empty(recorder.Requests);

        foreach (var missing in new Action<TwilioOptions>[]
                 {
                     o => { o.AuthToken = Token; o.FromNumber = From; },
                     o => { o.AccountSid = Sid; o.FromNumber = From; },
                     o => { o.AccountSid = Sid; o.AuthToken = Token; },
                 })
        {
            var (bare, _) = Compose(missing);
            await using var __ = bare;
            var ex = Assert.Throws<InvalidOperationException>(() => bare.GetRequiredService<IOtpChannel>());
            Assert.Equal("Twilio AccountSid/AuthToken/FromNumber مَطلوبَة (Auth:Twilio:*).", ex.Message);
        }
        output.WriteLine("[twilio] التَسجيل: عابِرٌ · TwilioSmsChannel · ثَلاثَةُ إعداداتٍ ناقِصَةٍ تَرمي بِرِسالَتِها");
    }
}
