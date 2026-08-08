using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Auth.Providers.Unifonic;

/// <summary>إعدادات Unifonic — أَكبَر مُزَوِّد SMS في KSA/MENA.</summary>
public sealed class UnifonicOptions
{
    public string AppSid { get; set; } = "";
    public string SenderId { get; set; } = "ACommerce";
    public string BaseUrl { get; set; } = "https://el.cloud.unifonic.com";
}

/// <summary>
/// قَناة SMS عَبر Unifonic REST API.
/// <code>POST /api/v1/Messaging/messages.json
/// AppSid=…&Recipient=966xxxxxxxxx&Body=…&SenderID=ACommerce</code>
/// </summary>
public sealed class UnifonicSmsChannel : IOtpChannel
{
    private readonly UnifonicOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<UnifonicSmsChannel> _logger;

    public UnifonicSmsChannel(IOptions<UnifonicOptions> opts, HttpClient http, ILogger<UnifonicSmsChannel> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_opts.AppSid))
            throw new InvalidOperationException("Unifonic AppSid غَير مُعَرَّف (Auth:Unifonic:AppSid).");
    }

    public string ChannelName => "Unifonic";
    public string? DevHintCode => null;

    public async Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        var body = $"رَمز التَّحَقُّق: {code}";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AppSid"]    = _opts.AppSid,
            ["Recipient"] = phone.TrimStart('+'),
            ["Body"]      = body,
            ["SenderID"]  = _opts.SenderId
        });
        using var resp = await _http.PostAsync($"{_opts.BaseUrl}/api/v1/Messaging/messages.json", form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("[Unifonic] فَشِل الإرسال {Status}: {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Unifonic فَشِل: {(int)resp.StatusCode}");
        }
        _logger.LogInformation("[Unifonic] أُرسِلَ كود لِـ {Phone}", phone);
    }
}

public static class UnifonicExtensions
{
    public static IServiceCollection AddUnifonicSmsChannel(
        this IServiceCollection services, Action<UnifonicOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IOtpChannel, UnifonicSmsChannel>();
        return services;
    }
}
