using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Files;

/// <summary>اِعدادات تَخزين مَحَلّيّ.</summary>
public sealed class LocalFileStorageOptions
{
    /// <summary>مَسار الجَذر (افتِراضيّ: <c>./uploads</c>).</summary>
    public string RootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

    /// <summary>المَسار العامّ في URLs (افتِراضيّ: <c>/uploads</c>).</summary>
    public string PublicPathPrefix { get; set; } = "/uploads";
}

/// <summary>
/// تَخزين مَحَلّيّ — يَكتُب على القُرص تَحتَ
/// <see cref="LocalFileStorageOptions.RootPath"/>، والـ URL العامّ يَخدُم
/// بِـ<c>UseStaticFiles</c> عَلى نَفس الـ host.
///
/// <para><b>لِلتَطويرِ وَحدَه مُنذُ ‏2026-08-30</b>، ويَحمِل
/// <see cref="IDevelopmentStubFileStorage"/> لِذلك. **والسَبَبُ مَقيسٌ لا
/// مَظنون**: كانَ مُسَجَّلاً بِلا شَرطِ بيئَةٍ على `wwwroot/uploads`
/// **داخِلَ الحاوِيَة**، وقُرصُ الـSpace زائِل — فَكُلُّ صورَةِ إعلانٍ
/// أَو صورَةٍ شَخصِيَّةٍ تَذهَب عِندَ أَوَّلِ إعادَةِ نَشر، **ويَبقى
/// رابِطُها في القاعِدَة** فَتُرسَم صورَةٌ مَكسورَةٌ لا فَراغٌ يُفهَم
/// (‏`/uploads/…` يَرُدُّ ‏404 مَقيساً على النُسخَةِ المَنشورَة).
/// التَفصيلُ في
/// <c>docs/ADR-017-TENANT-IMAGES-OUTLIVE-THE-CONTAINER.md</c>.</para>
///
/// <para><b>وهُوَ لا يَكذِب</b> — يَكتُب ويَقرَأُ صِدقاً؛ عَيبُه أَنّ ما
/// كَتَبَه يَذهَب. وذلك يَكفي لِحَملِ العَلامَة: الفَرقُ بَينَه وبَينَ
/// مُحاكي الدَفعِ فيمَن يَخسَر، لا في نَوعِ الكَذِب.</para>
/// </summary>
public sealed class LocalFileStorage : IFileStorage, IDevelopmentStubFileStorage
{
    private readonly LocalFileStorageOptions _opts;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> opts, ILogger<LocalFileStorage> logger)
    {
        _opts = opts.Value;
        _logger = logger;
        Directory.CreateDirectory(_opts.RootPath);
    }

    public string ProviderName => "Local";

    public async Task<StoredFile> UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var fs = File.Create(path))
            await content.CopyToAsync(fs, ct);
        var info = new FileInfo(path);
        _logger.LogInformation("[Local] رُفِعَ {Key} ({Size} B)", key, info.Length);
        return new StoredFile(key, BuildPublicUrl(key), info.Length, contentType, info.CreationTimeUtc);
    }

    public Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(File.Exists(ResolvePath(key)));

    public Task<string> GetPublicUrlAsync(string key, TimeSpan? expiresIn = null, CancellationToken ct = default)
        => Task.FromResult(BuildPublicUrl(key));

    private string ResolvePath(string key)
    {
        // مَنع directory traversal: نُطَهِّر '..' ونُجَبِّر مَسار نِسبِيّ.
        var safe = key.Replace("..", "").Replace('\\', '/').TrimStart('/');
        return Path.Combine(_opts.RootPath, safe);
    }

    private string BuildPublicUrl(string key)
        => $"{_opts.PublicPathPrefix.TrimEnd('/')}/{key.TrimStart('/')}";
}

public static class LocalFileStorageExtensions
{
    /// <summary>تَسجيل تَخزين مَحَلّيّ كَ<see cref="IFileStorage"/>.</summary>
    public static IServiceCollection AddLocalFileStorage(
        this IServiceCollection services,
        Action<LocalFileStorageOptions>? configure = null)
    {
        if (configure is not null) services.Configure(configure);
        else services.Configure<LocalFileStorageOptions>(_ => { });
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }

    /// <summary>تَفعيل خِدمَة المَلَفّات عَلى نَفس الـ host (لِلـ Local فَقَط).
    /// يُضيف <c>UseStaticFiles</c> عَلى المُجَلَّد المَحَلّيّ بِالـ prefix
    /// المُعَرَّف. لا تُستَخدَم في الإنتاج خَلف CDN.</summary>
    public static IApplicationBuilder UseLocalFileStorage(this WebApplication app)
    {
        var opts = app.Services.GetRequiredService<IOptions<LocalFileStorageOptions>>().Value;
        Directory.CreateDirectory(opts.RootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(opts.RootPath),
            RequestPath = opts.PublicPathPrefix
        });
        return app;
    }
}
