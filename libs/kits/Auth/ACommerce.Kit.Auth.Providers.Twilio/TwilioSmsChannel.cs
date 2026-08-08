using System.Net.Http.Headers;
using System.Text;
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
/// </summary>
public sealed class TwilioSmsChannel : IOtpChannel
{
    private readonly TwilioOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<TwilioSmsChannel> _logger;

    public TwilioSmsChannel(IOptions<TwilioOptions> opts, HttpClient http, ILogger<TwilioSmsChannel> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.AccountSid) || string.IsNullOrEmpty(_opts.AuthToken) || string.IsNullOrEmpty(_opts.FromNumber))
            throw new InvalidOperationException("Twilio AccountSid/AuthToken/FromNumber مَطلوبَة (Auth:Twilio:*).");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.AccountSid}:{_opts.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public string ChannelName => "Twilio";
    public string? DevHintCode => null;

    public async Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        var body = $"Your verification code: {code}";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"]   = phone.StartsWith("+") ? phone : "+" + phone,
            ["From"] = _opts.FromNumber,
            ["Body"] = body
        });
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_opts.AccountSid}/Messages.json";
        using var resp = await _http.PostAsync(url, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("[Twilio] فَشِل {Status}: {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Twilio فَشِل: {(int)resp.StatusCode}");
        }
        _logger.LogInformation("[Twilio] أُرسِلَ كود لِـ {Phone}", phone);
    }
}

public static class TwilioExtensions
{
    public static IServiceCollection AddTwilioSmsChannel(
        this IServiceCollection services, Action<TwilioOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IOtpChannel, TwilioSmsChannel>();
        return services;
    }
}
