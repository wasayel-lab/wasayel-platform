using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Files.Providers.AliyunOss;

/// <summary>
/// إعدادات Aliyun OSS — تُمَرَّر مِن <c>appsettings.json</c>:
/// <code>
/// "Files": {
///   "Aliyun": {
///     "Endpoint": "oss-me-east-1.aliyuncs.com",
///     "Bucket":   "ejar-prod",
///     "AccessKeyId":     "LTAI…",
///     "AccessKeySecret": "…",
///     "CdnBaseUrl":      "https://cdn.ejar.app"
///   }
/// }
/// </code>
/// </summary>
public sealed class AliyunOssOptions
{
    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string AccessKeySecret { get; set; } = "";
    /// <summary>اختياريّ — لَو فارِغ نَستَخدِم <c>https://{bucket}.{endpoint}</c>.</summary>
    public string? CdnBaseUrl { get; set; }
}

/// <summary>
/// تَخزين مَلَفّات على Aliyun OSS — لِأَسواق الصين والشَّرق الأَوسَط.
/// يَكتُب بِـ HTTP API مُباشَر (لا حاجَة لِـ SDK خارِجيّ) باستِخدام
/// signature v1. يُلائِم الإنتاج بَعد ضَبط الـ AccessKey في الإعدادات.
/// </summary>
public sealed class AliyunOssFileStorage : IFileStorage
{
    private readonly AliyunOssOptions _opts;
    private readonly HttpClient _http;
    private readonly ILogger<AliyunOssFileStorage> _logger;

    public AliyunOssFileStorage(
        IOptions<AliyunOssOptions> opts,
        HttpClient http,
        ILogger<AliyunOssFileStorage> logger)
    {
        _opts = opts.Value;
        _http = http;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_opts.Endpoint) || string.IsNullOrWhiteSpace(_opts.Bucket)
         || string.IsNullOrWhiteSpace(_opts.AccessKeyId) || string.IsNullOrWhiteSpace(_opts.AccessKeySecret))
        {
            throw new FileStorageException(
                "Aliyun OSS غَير مُهَيَّأ. اضبِط Files:Aliyun:{Endpoint,Bucket,AccessKeyId,AccessKeySecret}.");
        }
    }

    public string ProviderName => "AliyunOSS";

    public async Task<StoredFile> UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        // اِقرأ كامِل الـ stream لِحِساب الـ Content-MD5 المَطلوب في التَّوقيع.
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var md5 = Convert.ToBase64String(MD5.HashData(bytes));

        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var canonical = $"PUT\n{md5}\n{contentType}\n{date}\n/{_opts.Bucket}/{key}";
        var sig = Sign(canonical, _opts.AccessKeySecret);

        using var req = new HttpRequestMessage(HttpMethod.Put, BuildObjectUrl(key))
        { Content = new ByteArrayContent(bytes) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        req.Content.Headers.ContentMD5 = Convert.FromBase64String(md5);
        req.Headers.Date = DateTime.Parse(date, CultureInfo.InvariantCulture);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "OSS", $"{_opts.AccessKeyId}:{sig}");

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new FileStorageException($"رَفع Aliyun OSS فَشِل: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        _logger.LogInformation("[Aliyun] رُفِعَ {Key} ({Size} B)", key, bytes.Length);
        return new StoredFile(key, await GetPublicUrlAsync(key, null, ct),
            bytes.Length, contentType, DateTime.UtcNow);
    }

    public async Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        using var req = SignedRequest(HttpMethod.Get, key, null);
        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new FileStorageException($"قِراءَة Aliyun OSS فَشِلَت: {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        using var req = SignedRequest(HttpMethod.Delete, key, null);
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        using var req = SignedRequest(HttpMethod.Head, key, null);
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode;
    }

    public Task<string> GetPublicUrlAsync(string key, TimeSpan? expiresIn = null, CancellationToken ct = default)
    {
        // لَو الـ bucket عامّ والـ CDN مُعَدّ، نَرجِع رابِط مُباشَر.
        if (!string.IsNullOrEmpty(_opts.CdnBaseUrl))
            return Task.FromResult($"{_opts.CdnBaseUrl.TrimEnd('/')}/{key.TrimStart('/')}");

        // وإلَّا signed URL مُؤَقَّت.
        var expires = DateTimeOffset.UtcNow.Add(expiresIn ?? TimeSpan.FromHours(1)).ToUnixTimeSeconds();
        var canonical = $"GET\n\n\n{expires}\n/{_opts.Bucket}/{key}";
        var sig = Sign(canonical, _opts.AccessKeySecret);
        return Task.FromResult(
            $"{BuildObjectUrl(key)}?OSSAccessKeyId={Uri.EscapeDataString(_opts.AccessKeyId)}" +
            $"&Expires={expires}&Signature={Uri.EscapeDataString(sig)}");
    }

    private HttpRequestMessage SignedRequest(HttpMethod method, string key, string? contentType)
    {
        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var canonical = $"{method.Method}\n\n{contentType ?? ""}\n{date}\n/{_opts.Bucket}/{key}";
        var sig = Sign(canonical, _opts.AccessKeySecret);
        var req = new HttpRequestMessage(method, BuildObjectUrl(key));
        req.Headers.Date = DateTime.Parse(date, CultureInfo.InvariantCulture);
        req.Headers.Authorization = new AuthenticationHeaderValue(
            "OSS", $"{_opts.AccessKeyId}:{sig}");
        return req;
    }

    private string BuildObjectUrl(string key)
        => $"https://{_opts.Bucket}.{_opts.Endpoint}/{key.TrimStart('/')}";

    private static string Sign(string canonical, string secret)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }
}

public static class AliyunOssExtensions
{
    /// <summary>تَفعيل Aliyun OSS كَ<see cref="IFileStorage"/>. اِقرأ القِسم
    /// <c>Files:Aliyun</c> مِن <c>appsettings.json</c>.</summary>
    public static IServiceCollection AddAliyunOssFileStorage(
        this IServiceCollection services,
        Action<AliyunOssOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<IFileStorage, AliyunOssFileStorage>();
        return services;
    }
}
