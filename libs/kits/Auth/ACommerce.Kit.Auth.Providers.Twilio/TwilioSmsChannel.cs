using System.Net.Http.Headers;
using System.Text;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Auth.Providers.Twilio;

/// <summary>إعدادات Twilio — الأَسواق العالَمِيَّة.</summary>
public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    /// <summary>رَقم Twilio المُرسِل (E.164: +1xxx, +44xxx, …).</summary>
    public string FromNumber { get; set; } = "";
}

/// <summary>
/// قَناة SMS عَبر Twilio REST API.
/// <code>POST /2010-04-01/Accounts/{AccountSid}/Messages.json
/// To=+966xxx&From=+1xxx&Body=…  (Basic auth: AccountSid:AuthToken)</code>
/// <para>‏(‏2026-11-12، الموجَة ١٣ W13-6·١١ — ADR-051 §١٨) <b>بِلا <c>HttpClient</c></b>: عَميلُها <see cref="ProviderHttp{TKit}"/> — المُستَدعي المَعدودُ الَّذي
/// يَكتُبُ نِيَّةً قَبلَ الإرسالِ وسَطراً حينَ يَقبَلُ Twilio — ورَأسُ Basic عَلى الطَلَبِ لا عَلى العَميل، فَالطَلَبُ الَّذي يَبلُغُ Twilio هُوَ هُوَ بايتاً
/// (<c>TwilioRequestShapeCharacterizationTests</c>، أُودِعَ قَبلَ التَبديلِ ولَم يُمَسّ). والقَناةُ تَسِمُ النِداءَ بِاسمِها (<see cref="ProviderNames.Twilio"/>).</para>
/// </summary>
public sealed class TwilioSmsChannel : IOtpChannel
{
    private readonly TwilioOptions _opts;
    private readonly ProviderHttp<TwilioSmsChannel> _http;
    private readonly AuthenticationHeaderValue _basic;
    private readonly ILogger<TwilioSmsChannel> _logger;

    public TwilioSmsChannel(IOptions<TwilioOptions> opts, ProviderHttp<TwilioSmsChannel> http, ILogger<TwilioSmsChannel> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.AccountSid) || string.IsNullOrEmpty(_opts.AuthToken) || string.IsNullOrEmpty(_opts.FromNumber))
            throw new InvalidOperationException("Twilio AccountSid/AuthToken/FromNumber مَطلوبَة (Auth:Twilio:*).");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.AccountSid}:{_opts.AuthToken}"));
        _basic = new AuthenticationHeaderValue("Basic", creds);
    }

    public string ChannelName => "Twilio";
    public string? DevHintCode => null;

    public async Task SendOtpAsync(ProviderCall call, string phone, string code, CancellationToken ct)
    {
        await PostMessageAsync(call, phone, $"Your verification code: {code}", ct);
        _logger.LogInformation("[Twilio] أُرسِلَ كود لِـ {Phone}", phone);
    }

    /// <summary>‏(‏2026-10-29، تَدقيقُ الموجَة ١١ A3) إشعارٌ بِواجِهَةِ الرَسائِلِ نَفسِها (<c>Messages.json</c>) — نَفسُ الطَلَبِ والمُرسِلِ والرَمي، والنَصُّ نَصُّه.</summary>
    public async Task SendNoticeAsync(ProviderCall call, string phone, string text, CancellationToken ct)
    {
        await PostMessageAsync(call, phone, text, ct);
        _logger.LogInformation("[Twilio] أُرسِلَ إشعارٌ لِـ {Phone}", phone);
    }

    // ‏(‏2026-11-12، W13-6·١١) **الأَخَوانِ بِلا `ProviderCall`** — لا مُناديَ لَهُما في `libs/` ولا `apps/` (`MeterTestSeamTests`)، ويَبقَيانِ لِأَنّ الواجِهَةَ
    // تُبقيهِما (ومِلَفُّ تَوصيفٍ يُنَفِّذُهُما). فَإن نودِيَ أَحَدُهُما يَوماً لا يُرسِلُ بِلا عَدّ: يُعَدُّ بِمَتجَرٍ لا يُعرَفُ (`_unresolved`) فَيُرى ولا يَضيع.
    public Task SendOtpAsync(string phone, string code, CancellationToken ct)
        => SendOtpAsync(ProviderCall.ForChannel(OutboundUnits.OtpSmsSend, MeterSentinels.Unresolved, ProviderCall.FreshRequestId()), phone, code, ct);

    public Task SendNoticeAsync(string phone, string text, CancellationToken ct)
        => SendNoticeAsync(ProviderCall.ForChannel(OutboundUnits.NoticeSmsSend, MeterSentinels.Unresolved, ProviderCall.FreshRequestId()), phone, text, ct);

    private async Task PostMessageAsync(ProviderCall call, string phone, string body, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"]   = phone.StartsWith("+") ? phone : "+" + phone,
            ["From"] = _opts.FromNumber,
            ["Body"] = body
        });
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_opts.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = _basic;
        using var resp = await _http.SendAsync(call.By(ProviderNames.Twilio), request, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("[Twilio] فَشِل {Status}: {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Twilio فَشِل: {(int)resp.StatusCode}");
        }
    }
}

public static class TwilioExtensions
{
    public static IServiceCollection AddTwilioSmsChannel(
        this IServiceCollection services, Action<TwilioOptions> configure)
    {
        services.Configure(configure);
        // ‏(‏2026-11-12، W13-6·١١ — ADR-051 §١٨) كانَ `AddHttpClient<IOtpChannel, TwilioSmsChannel>()`: عَميلٌ مُطَبَّعٌ عابِرٌ بِلا مُهلَةٍ خاصَّة. والآنَ عَميلُها
        // المُسَمّى يَملِكُه `ProviderHttp` (المُهلَةُ الافتِراضِيَّةُ نَفسُها)، والقَناةُ عابِرَةٌ كَما كانَت.
        services.AddProviderHttp<TwilioSmsChannel>();
        services.AddTransient<IOtpChannel, TwilioSmsChannel>();
        return services;
    }
}
