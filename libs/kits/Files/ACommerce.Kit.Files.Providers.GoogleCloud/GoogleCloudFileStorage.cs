using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Files.Providers.GoogleCloud;

/// <summary>إعدادات Google Cloud Storage.</summary>
public sealed class GoogleCloudStorageOptions
{
    public string Bucket { get; set; } = "";
    /// <summary>مَسار مَلَفّ service-account JSON (يَحوي client_email + private_key).</summary>
    public string ServiceAccountJsonPath { get; set; } = "";
    /// <summary>اختياريّ — لَو فارِغ نَستَخدِم <c>https://storage.googleapis.com/{bucket}</c>.</summary>
    public string? CdnBaseUrl { get; set; }
}

/// <summary>
/// تَخزين Google Cloud Storage — لِأَسواق العالَم. يَستَخدِم HTTP API مُباشَر
/// (لا حاجَة لِـ Google.Cloud.Storage SDK) مَع JWT لِلتَّوثيق.
/// </summary>
public sealed class GoogleCloudFileStorage : IFileStorage
{
    private readonly GoogleCloudStorageOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<GoogleCloudFileStorage> _logger;

    // كاش لِلـ access token حَتَّى لا نُجَدِّد JWT مَع كُلّ طَلَب.
    private string? _accessToken;
    private DateTime _tokenExpiresAt;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _clientEmail;
    private string? _privateKeyPem;

    public GoogleCloudFileStorage(
        IOptions<GoogleCloudStorageOptions> opts, HttpClient http,
        ILogger<GoogleCloudFileStorage> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrEmpty(_opts.Bucket) || string.IsNullOrEmpty(_opts.ServiceAccountJsonPath))
            throw new FileStorageException(
                "GCS غَير مُهَيَّأ. اضبِط Files:GoogleCloud:{Bucket,ServiceAccountJsonPath}.");
        LoadServiceAccount();
    }

    public string ProviderName => "GoogleCloud";

    private void LoadServiceAccount()
    {
        if (!File.Exists(_opts.ServiceAccountJsonPath))
            throw new FileStorageException($"مَلَفّ service account غَير مَوجود: {_opts.ServiceAccountJsonPath}");
        var json = File.ReadAllText(_opts.ServiceAccountJsonPath);
        using var doc = JsonDocument.Parse(json);
        _clientEmail = doc.RootElement.GetProperty("client_email").GetString();
        _privateKeyPem = doc.RootElement.GetProperty("private_key").GetString();
    }

    public async Task<StoredFile> UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var url = $"https://storage.googleapis.com/upload/storage/v1/b/{_opts.Bucket}/o?uploadType=media&name={Uri.EscapeDataString(key)}";
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = new ByteArrayContent(bytes) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new FileStorageException($"رَفع GCS فَشِل: {(int)resp.StatusCode}");

        _logger.LogInformation("[GCS] رُفِعَ {Key} ({Size} B)", key, bytes.Length);
        return new StoredFile(key, await GetPublicUrlAsync(key, null, ct),
            bytes.Length, contentType, DateTime.UtcNow);
    }

    public async Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var url = $"https://storage.googleapis.com/storage/v1/b/{_opts.Bucket}/o/{Uri.EscapeDataString(key)}?alt=media";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new FileStorageException($"قِراءَة GCS فَشِلَت: {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var url = $"https://storage.googleapis.com/storage/v1/b/{_opts.Bucket}/o/{Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);
        var url = $"https://storage.googleapis.com/storage/v1/b/{_opts.Bucket}/o/{Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public Task<string> GetPublicUrlAsync(string key, TimeSpan? expiresIn = null, CancellationToken ct = default)
    {
        // رابِط CDN أو حاوية عامَّة: مُباشَر.
        if (!string.IsNullOrEmpty(_opts.CdnBaseUrl))
            return Task.FromResult($"{_opts.CdnBaseUrl.TrimEnd('/')}/{key.TrimStart('/')}");
        return Task.FromResult($"https://storage.googleapis.com/{_opts.Bucket}/{key.TrimStart('/')}");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return _accessToken;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt > DateTime.UtcNow.AddMinutes(5))
                return _accessToken;

            // اِبنِ JWT بـ RS256 ثُمَّ بادِل بِـ access_token عَبر OAuth2.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = _clientEmail,
                scope = "https://www.googleapis.com/auth/devstorage.read_write",
                aud = "https://oauth2.googleapis.com/token",
                exp = now + 3600,
                iat = now
            }));
            var unsigned = $"{header}.{payload}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_privateKeyPem);
            var sig = Base64Url(rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var jwt = $"{unsigned}.{sig}";

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = jwt
            });
            using var resp = await _http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
            if (!resp.IsSuccessStatusCode)
                throw new FileStorageException($"GCS OAuth فَشِل: {(int)resp.StatusCode}");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            _accessToken = doc.RootElement.GetProperty("access_token").GetString();
            var expSec = doc.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expSec);
            return _accessToken!;
        }
        finally { _tokenLock.Release(); }
    }

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class GoogleCloudStorageExtensions
{
    public static IServiceCollection AddGoogleCloudFileStorage(
        this IServiceCollection services, Action<GoogleCloudStorageOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IFileStorage, GoogleCloudFileStorage>();
        return services;
    }
}
