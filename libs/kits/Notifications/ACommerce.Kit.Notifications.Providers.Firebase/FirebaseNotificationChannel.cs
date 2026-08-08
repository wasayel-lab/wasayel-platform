using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Notifications.Providers.Firebase;

/// <summary>إعدادات Firebase Cloud Messaging.</summary>
public sealed class FirebaseOptions
{
    /// <summary>مَسار service account JSON (يَحوي project_id + client_email + private_key).</summary>
    public string ServiceAccountJsonPath { get; set; } = "";
}

/// <summary>
/// قَناة push notifications عَبر Firebase Cloud Messaging — يَدعَم Android/iOS/Web.
/// يَجلِب tokens المُستَخدِم مِن <see cref="IDeviceTokenStore"/> ويُرسِل كُلّ
/// واحِد على HTTPv1 API. لِلتَّفعيل: نَزِّل service account JSON مِن Firebase
/// Console وضَع مَسارَه في الإعدادات.
/// </summary>
public sealed class FirebaseNotificationChannel : INotificationChannel
{
    private readonly FirebaseOptions _opts;
    private readonly IDeviceTokenStore _tokens;
    private readonly HttpClient _http;
    private readonly ILogger<FirebaseNotificationChannel> _logger;

    private string? _projectId, _clientEmail, _privateKeyPem;
    private string? _cachedToken;
    private DateTime _tokenExp;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public FirebaseNotificationChannel(
        IOptions<FirebaseOptions> opts, IDeviceTokenStore tokens,
        HttpClient http, ILogger<FirebaseNotificationChannel> logger)
    {
        _opts = opts.Value;
        _tokens = tokens;
        _http = http;
        _logger = logger;
        LoadServiceAccount();
    }

    public string ChannelName => "Firebase";

    private void LoadServiceAccount()
    {
        if (string.IsNullOrEmpty(_opts.ServiceAccountJsonPath) || !File.Exists(_opts.ServiceAccountJsonPath))
            throw new InvalidOperationException($"Firebase service account غَير مَوجود: {_opts.ServiceAccountJsonPath}");
        var json = File.ReadAllText(_opts.ServiceAccountJsonPath);
        using var doc = JsonDocument.Parse(json);
        _projectId    = doc.RootElement.GetProperty("project_id").GetString();
        _clientEmail  = doc.RootElement.GetProperty("client_email").GetString();
        _privateKeyPem = doc.RootElement.GetProperty("private_key").GetString();
    }

    public async Task SendAsync(NotificationPayload p, CancellationToken ct)
    {
        var tokens = await _tokens.ListTokensAsync(p.UserId, ct);
        if (tokens.Count == 0) return;

        var accessToken = await GetAccessTokenAsync(ct);
        var url = $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send";

        foreach (var t in tokens)
        {
            var msg = new
            {
                message = new
                {
                    token = t.Token,
                    notification = new { title = p.Title, body = p.Body },
                    data = BuildDataDict(p),
                    android = new { priority = "high" },
                    apns = new { headers = new Dictionary<string, string> { ["apns-priority"] = "10" } }
                }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(msg) };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[Firebase] فَشِل token {Token}: {Status} {Body}", t.Token, resp.StatusCode, err);
                // 404/410 = token باطِل — اِحذِفه.
                if (resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
                    await _tokens.RemoveTokenAsync(p.UserId, t.Token, ct);
            }
        }
    }

    private static Dictionary<string, string> BuildDataDict(NotificationPayload p)
    {
        var d = p.Data is null ? new Dictionary<string, string>() : new Dictionary<string, string>(p.Data);
        d["type"] = p.Type;
        if (p.RelatedUrl is not null) d["url"] = p.RelatedUrl;
        return d;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && _tokenExp > DateTime.UtcNow.AddMinutes(5))
            return _cachedToken;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && _tokenExp > DateTime.UtcNow.AddMinutes(5))
                return _cachedToken;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = _clientEmail,
                scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud = "https://oauth2.googleapis.com/token",
                exp = now + 3600,
                iat = now
            }));
            var unsigned = $"{header}.{payload}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_privateKeyPem);
            var sig = B64(rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var jwt = $"{unsigned}.{sig}";
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"]  = jwt
            });
            using var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            _cachedToken = doc.RootElement.GetProperty("access_token").GetString();
            _tokenExp = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32());
            return _cachedToken!;
        }
        finally { _tokenLock.Release(); }
    }

    private static string B64(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class FirebaseExtensions
{
    /// <summary>تَفعيل Firebase push كَ <see cref="INotificationChannel"/>.</summary>
    public static IServiceCollection AddFirebaseNotifications(
        this IServiceCollection services, Action<FirebaseOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<FirebaseNotificationChannel>();
        services.AddSingleton<INotificationChannel>(sp => sp.GetRequiredService<FirebaseNotificationChannel>());
        return services;
    }
}
