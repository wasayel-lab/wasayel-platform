using System.Net.Http.Json;
using ACommerce.Platform.I18n;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Auth.Providers.Brevo;

// ═══ قَناةُ بَريدٍ عَبر HTTPS — لِأَنّ ‏443 هُوَ المَنفَذُ المَضمون ══════
//
// **العِلَّةُ المَقيسَة (‏2026-08-23)**: الـSpace يَحجُب مَنافِذَ SMTP
// الصادِرَة (‏587/465)، فَـ`smtp` مَضبوطَةٌ ضَبطاً صَحيحاً ومَعَ ذلِك
// **لا تُرسِل** — كانَت تَعلَق تِسعينَ ثانِيَة، وصارَت (بِالمَوجَةِ
// السابِقَة) تَفشَل في عَشر. والفَشَلُ الصَريحُ أَفضَلُ مِن التَعليق،
// **ولَكِنَّه لَيسَ دُخولاً**. فَما يَحتاجُه المالِكُ نَقلٌ يَمُرّ.
//
// والمَنفَذُ الوَحيدُ المَضمونُ خُروجُه مِن الـSpace هُوَ ‏443 — وهو ما
// تَستَعمِلُه واجِهَةُ Brevo. أَي أَنّ التَبديلَ لَيسَ «مُزَوِّداً أَفضَل»
// بَل **نَقلاً يَعبُر**: نَفسُ الرِسالَة، نَفسُ الرَمز، نَفسُ الأُنبوب.
//
// **ولِماذا مَشروعٌ مُستَقِلٌّ لا فَرعٌ داخِلَ قَناةِ SMTP** (‏ADR-003،
// نَمَطُ المُزَوِّدين): كُلُّ SDK/واجِهَةٍ خارِجِيَّةٍ مَشروعٌ مُنفَصِل
// يُسَجَّل بِالتَهيئَة، والاختيارُ في `AuthChannelSelection` وَحدَها.
// ونَفسُ شَكلِ `TwilioSmsChannel` بِالضَبط: خِياراتٌ + `AddHttpClient`
// + رَمي عِندَ الفَشَل.

/// <summary>
/// إعداداتُ Brevo — تُقرَأ مِن **نَفسِ قِسمِ** <c>Auth:Email</c> الَّذي
/// تَقرَأُ مِنه قَناةُ SMTP. المُستَعمَلُ مِنه هُنا:
/// <c>ApiKey</c>، <c>From</c>، <c>FromName</c>، <c>TimeoutSeconds</c> —
/// و<c>Host</c>/<c>Port</c>/<c>Username</c>/<c>Password</c> تُتَجاهَل.
///
/// <para><b>ولِماذا القِسمُ نَفسُه لا قِسمٌ ثانٍ</b>: المالِكُ ضَبَطَ
/// <c>Auth__Email__From</c> بِالفِعل، وقِسمٌ ثانٍ يَعني إعادَةَ ضَبطِ ما
/// هُوَ مَضبوطٌ — ونِسيانَ حَقلٍ في أَحَدِهِما يُغلِق البابَ بِلا سَبَبٍ
/// ظاهِر.</para>
/// </summary>
public sealed class BrevoEmailOptions
{
    /// <summary>مِفتاحُ الواجِهَة (‏<c>Auth:Email:ApiKey</c>). يُرسَل في
    /// رَأسِ <c>api-key</c>، ولا يُكتَب في لوغٍ ولا في رِسالَةِ خَطَإ.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>عُنوانُ المُرسِل. يَجِب أَن يَكونَ نِطاقاً مُصادَقاً في
    /// حِسابِ Brevo، وإلّا رَدَّت الواجِهَةُ ‏400.</summary>
    public string From { get; set; } = "";

    /// <summary>اسمُ المُرسِلِ الظاهِر. اختِياريّ — يُحذَف مِن الجِسمِ إن
    /// كانَ فارِغاً بَدَلَ أَن يُرسَل فارِغاً.</summary>
    public string FromName { get; set; } = "";

    /// <summary>مُهلَةُ النِداءِ بِالثَواني. الافتِراضيّ
    /// <see cref="OtpSendGuard.DefaultTimeoutSeconds"/>، والصِفرُ أَو
    /// السالِبُ يَرتَدُّ إلَيه.</summary>
    public int TimeoutSeconds { get; set; } = OtpSendGuard.DefaultTimeoutSeconds;
}

/// <summary>
/// قَناةُ OTP بَريديَّةٌ فِعليَّةٌ عَبر واجِهَةِ Brevo الحَرفِيَّة. لا
/// تَعرِف شَيئاً عَن تَوليدِ الرَمزِ ولا تَخزينِه ولا حُدودِ مُعَدَّلِه —
/// ذلِكَ كُلُّه في <c>AuthHandlers</c> المُشتَرَك، تَماماً كَقَناةِ SMTP.
/// </summary>
public sealed class BrevoEmailChannel : IEmailOtpChannel
{
    /// <summary>النُقطَة. مُثَبَّتَةٌ هُنا لِيَقرَأَها الاختِبارُ بَدَلَ
    /// أَن يَنسَخَها.</summary>
    public const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    /// <summary>رَأسُ المُصادَقَة. ‏Brevo لا تَستَعمِل
    /// <c>Authorization: Bearer</c> بَل رَأساً خاصّاً — وخَطَأٌ فيه يَرُدّ
    /// ‏401 بِلا تَوضيح.</summary>
    public const string ApiKeyHeader = "api-key";

    private readonly BrevoEmailOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<BrevoEmailChannel> _logger;

    public BrevoEmailChannel(
        IOptions<BrevoEmailOptions> opts, HttpClient http, ILogger<BrevoEmailChannel> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new InvalidOperationException("Brevo ApiKey غَير مُعَرَّف (Auth:Email:ApiKey).");
        if (string.IsNullOrWhiteSpace(_opts.From))
            throw new InvalidOperationException("Brevo From غَير مُعَرَّف (Auth:Email:From).");
        if (!_http.DefaultRequestHeaders.Contains(ApiKeyHeader))
            _http.DefaultRequestHeaders.Add(ApiKeyHeader, _opts.ApiKey);
    }

    public string ChannelName => "Brevo";

    /// <summary><c>null</c> — قَناةُ إنتاج: لا كودَ مَعروضٌ في الواجِهَة.</summary>
    public string? DevHintCode => null;

    public async Task SendOtpAsync(string email, string code, CancellationToken ct)
    {
        // الرِسالَةُ مِن `OtpEmailMessage` — **نَفسُها** الَّتي يُرسِلُها
        // SMTP، لا نُسخَةٌ مِنها. والعُنوانُ مِن القامُوسِ بِالعَرَبِيَّةِ
        // صَراحَةً: يُقرَأ في صُندوقِ بَريدٍ لا في مُتَصَفِّح.
        var sender = new Dictionary<string, string> { ["email"] = _opts.From };
        if (!string.IsNullOrWhiteSpace(_opts.FromName))
            sender["name"] = _opts.FromName;

        var payload = new Dictionary<string, object>
        {
            ["sender"] = sender,
            ["to"] = new[] { new Dictionary<string, string> { ["email"] = email } },
            ["subject"] = LocaleCatalog.Text(LocaleCatalog.Arabic, OtpEmailMessage.SubjectKey),
            ["textContent"] = OtpEmailMessage.Text(code),
            ["htmlContent"] = OtpEmailMessage.Html(code)
        };

        // مُهلَةٌ ثانِيَةٌ إلى جانِبِ `HttpClient.Timeout`: الأُولى تَحرُس
        // النِداءَ، وهذِه تَربِطُه بِرَمزِ الطَلَب فَيَنقَطِع مَعَه.
        var window = OtpSendGuard.Timeout(_opts.TimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(window);

        try
        {
            using var resp = await _http.PostAsJsonAsync(Endpoint, payload, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                // الحالَةُ والجِسمُ يُكتَبانِ في اللوغ لِلتَشخيص — ولا
                // يَحمِلانِ المِفتاح؛ Brevo لا تُعيدُه في رَدِّها.
                _logger.LogError("[Brevo] فَشِل {Status}: {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"Brevo فَشِل: {(int)resp.StatusCode}");
            }
            _logger.LogInformation("[Brevo] أُرسِلَ كود لِـ {Email}", email);
        }
        // الحُكمُ لِرَمزِنا لا لِنَوعِ الاستِثناء — نَفسُ عِلَّةِ قَناةِ
        // SMTP المَقيسَة: المَكتَبَةُ تَختار نَوعَ ما تَرميه عِندَ الإلغاء،
        // والسُؤالُ الصَحيحُ «هَل أَلغَينا نَحن؟».
        catch (Exception) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // تَجاوُزُ مُهلَة — لا انقِطاعُ طَلَب. يُقال بِاسمِه فَيَصير
            // `send_failed` عِندَ النُقطَة، لا صَفحَةً تَدور.
            var message = OtpSendGuard.TimeoutMessage(window);
            _logger.LogError("[Brevo] {Message}", message);
            throw new InvalidOperationException("Brevo " + message);
        }
        catch (HttpRequestException ex)
        {
            // نَرمي ولا نَبتَلِع — فَشَلُ إرسالِ OTP يَعني أَنّ المُستَخدِمَ
            // لَن يَدخُل، فَيَجِب أَن يَظهَرَ لَه خَطَأٌ لا شاشَةُ انتِظار.
            _logger.LogError(ex, "[Brevo] فَشِل إرسال كود لِـ {Email}", email);
            throw new InvalidOperationException("Brevo فَشِل الإرسال: " + ex.Message, ex);
        }
    }
}

public static class BrevoEmailExtensions
{
    /// <summary>يُسَجِّل قَناةَ Brevo كَتَنفيذٍ لِـ
    /// <see cref="IEmailOtpChannel"/> — نَفسُ نَمَطِ
    /// <c>AddTwilioSmsChannel</c>: عَميلٌ مُطَبَّعٌ بِمُهلَة، والخِياراتُ
    /// مِن التَهيئَةِ في التَطبيقِ المُضيف.</summary>
    public static IServiceCollection AddBrevoEmailChannel(
        this IServiceCollection services, Action<BrevoEmailOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IEmailOtpChannel, BrevoEmailChannel>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BrevoEmailOptions>>().Value;
            // سَقفٌ احتِياطيٌّ لَو سَقَطَ الرَمز — **ضِعفُ** نافِذَةِ
            // القَناة لا مُساوِيها، لِيَسبِقَ الرَمزُ دائِماً فَتَثبُتَ
            // رِسالَةُ الخَطَإ بَدَلَ أَن تَتَبَدَّلَ بِسِباق.
            client.Timeout = OtpSendGuard.Timeout(opts.TimeoutSeconds) * 2;
        });
        return services;
    }
}
