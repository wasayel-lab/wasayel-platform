using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Auth.Providers.Nafath;

/// <summary>إعدادات تَكامُل نَفاذ (هَيئَة الاتِّصالات/يَقين).</summary>
public sealed class NafathOptions
{
    public string BaseUrl { get; set; } = "https://api.nafath.sa";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

/// <summary>
/// قَناة نَفاذ الحَقيقِيَّة (KSA). يُرسِل طَلَب MFA لِلمُواطِن — يَنبَثِق في
/// تَطبيق نَفاذ كَرَمز مُكَوَّن مِن رَقمَين، يَختاره المُستَخدِم لِلمُوافَقَة.
/// يُلائِم الإنتاج بَعد تَوقيع عَقد مَع يَقين/نَفاذ.
/// <para>‏(‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051 §١٩) <b>بِلا <c>HttpClient</c></b>: عَميلُها <see cref="ProviderHttp{TKit}"/>. <b>بَدءُ التَحَقُّقِ نِداءٌ مَعدود</b>
/// (<see cref="OutboundUnits.IdentityNafathStart"/>، نِيَّةٌ قَبلَه وسَطرٌ حينَ يَقبَلُ نَفاذ)، و<b>رَمزُ OAuth واستِطلاعُ الحالَةِ مُساعِدانِ لا يُعَدّان</b>
/// (<see cref="ProviderHelperReasons.OauthToken"/>، <see cref="ProviderHelperReasons.StatusPoll"/>) — والطَلَباتُ الثَلاثَةُ هِيَ هِيَ بايتاً
/// (<c>NafathS3PushRequestShapeCharacterizationTests</c>، أُودِعَ قَبلَ التَبديلِ ولَم يُمَسّ).</para>
/// </summary>
public sealed class NafathChannel : INafathChannel
{
    private readonly NafathOptions _opts;
    private readonly ProviderHttp<NafathChannel> _http;
    private readonly ILogger<NafathChannel> _logger;
    // كاش حالَة الـ attempts لِلـ polling. حَيّ في الذاكِرَة (دَقائِق).
    private static readonly ConcurrentDictionary<string, NafathAttemptState> _attempts = new();

    private static readonly ProviderHelper Token = new(ProviderNames.Nafath, ProviderHelperReasons.OauthToken);
    private static readonly ProviderHelper Poll = new(ProviderNames.Nafath, ProviderHelperReasons.StatusPoll);

    public NafathChannel(IOptions<NafathOptions> opts, ProviderHttp<NafathChannel> http, ILogger<NafathChannel> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.ClientId) || string.IsNullOrEmpty(_opts.ClientSecret))
            throw new InvalidOperationException("Nafath ClientId/ClientSecret مَطلوبَة.");
    }

    public string ChannelName => "Nafath";

    public async Task<NafathStartResult> StartAsync(ProviderCall call, string nationalId, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/api/v1/mfa/request")
        { Content = JsonContent.Create(new { nationalId, service = "Login" }) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(call.By(ProviderNames.Nafath), req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("[Nafath] فَشِل {Status}: {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Nafath فَشِل: {(int)resp.StatusCode}");
        }
        var dto = await resp.Content.ReadFromJsonAsync<StartDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Nafath: رَدّ فارِغ.");
        _attempts[dto.TransId] = new NafathAttemptState(dto.TransId, nationalId, DateTime.UtcNow);
        return new NafathStartResult(dto.TransId, dto.Random.ToString("00"), 90);
    }

    public async Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct)
    {
        if (!_attempts.ContainsKey(attemptId)) return false;
        var token = await GetAccessTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_opts.BaseUrl}/api/v1/mfa/status?transId={Uri.EscapeDataString(attemptId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendHelperAsync(Poll, req, ct);
        if (!resp.IsSuccessStatusCode) return false;
        var dto = await resp.Content.ReadFromJsonAsync<StatusDto>(cancellationToken: ct);
        return dto?.Status == "COMPLETED";
    }

    private string? _cachedToken;
    private DateTime _cachedTokenExp;
    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && _cachedTokenExp > DateTime.UtcNow.AddMinutes(2))
            return _cachedToken;
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = _opts.ClientId,
            ["client_secret"] = _opts.ClientSecret
        });
        // كانَ `_http.PostAsync(url, form, ct)` — وهُوَ هذا الطَلَبُ بِعَينِه: ‏POST بِالعُنوانِ والاستِمارَةِ وبِلا رَأس.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_opts.BaseUrl}/oauth/token") { Content = form };
        using var resp = await _http.SendHelperAsync(Token, request, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Nafath OAuth فَشِل: {(int)resp.StatusCode}");
        var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Nafath OAuth: رَدّ فارِغ.");
        _cachedToken = dto.AccessToken;
        _cachedTokenExp = DateTime.UtcNow.AddSeconds(dto.ExpiresIn);
        return _cachedToken;
    }

    private sealed record NafathAttemptState(string TransId, string NationalId, DateTime CreatedAt);
    private sealed record StartDto(string TransId, int Random);
    private sealed record StatusDto(string Status);
    private sealed record TokenDto(string AccessToken, int ExpiresIn);
}

public static class NafathExtensions
{
    public static IServiceCollection AddNafathChannel(
        this IServiceCollection services, Action<NafathOptions> configure)
    {
        services.Configure(configure);
        // ‏(‏2026-11-12، W13-6·١٣ — ADR-051 §١٩) كانَ `AddHttpClient<INafathChannel, NafathChannel>()`: عَميلٌ مُطَبَّعٌ عابِرٌ بِلا مُهلَةٍ خاصَّة. والآنَ عَميلُها
        // المُسَمّى يَملِكُه `ProviderHttp` (المُهلَةُ الافتِراضِيَّةُ نَفسُها)، والقَناةُ عابِرَةٌ كَما كانَت — فَرَمزُ OAuth في القَناةِ لا في العَمَلِيَّة كَما كان.
        services.AddProviderHttp<NafathChannel>();
        services.AddTransient<INafathChannel, NafathChannel>();
        return services;
    }
}
