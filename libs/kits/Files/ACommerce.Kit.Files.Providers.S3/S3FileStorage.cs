using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ACommerce.Platform.Shared.Metering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Files.Providers.S3;

/// <summary>إعداداتُ مَخزَنِ كائِناتٍ مُتَوافِقٍ مَع S3.</summary>
public sealed class S3FileStorageOptions
{
    /// <summary>عُنوانُ الخِدمَة — لِـR2:
    /// <c>https://&lt;account-id&gt;.r2.cloudflarestorage.com</c>.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>اسمُ الدَلو.</summary>
    public string Bucket { get; set; } = "";

    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";

    /// <summary>الجَذرُ العامُّ الَّذي تُبنى مِنه روابِطُ العَرض — نِطاقُ
    /// ‏r2.dev أَو نِطاقٌ مُخَصَّصٌ أَمامَ الدَلو. <b>ولَيسَ هُوَ
    /// <see cref="Endpoint"/></b>: ذاكَ عُنوانُ واجِهَةِ البَرمَجَةِ
    /// المُوَقَّعَة، وهذا عُنوانُ القِراءَةِ العامّ.</summary>
    public string PublicBaseUrl { get; set; } = "";
}

/// <summary>
/// <para>مَخزَنُ كائِناتٍ مُتَوافِقٌ مَع S3 — والمَقصودُ اليَوم
/// <b>Cloudflare R2</b>، ويَعمَل كَما هُوَ مَع B2 وMinIO وWasabi.</para>
///
/// <para><b>ولِماذا R2 دونَ الجارَين المَكتوبَينِ سَلَفاً</b> (‏Aliyun
/// OSS ‏167 سَطراً، وGCS ‏184): ‏<b>صِفرُ رَسمِ صادِر</b> — وسوقُ صُوَرٍ
/// كَثيرَةٍ يَدفَع الصادِرَ لا التَخزين؛ و<b>مُتَغَيِّراتُ بيئَةٍ
/// صِرفَة</b> بَينَما GCS يَطلُب <b>مِلَفَّ JSON على القُرص</b> وهُوَ
/// عِبءٌ على حاوِيَةٍ قُرصُها زائِل — أَي أَنّ عِلاجَ الزَوالِ كانَ
/// سَيَتَّكِئُ على الزائِل نَفسِه؛ و<b>تَوافُقُ S3 يَنفي الارتِهان</b>،
/// فَنَفسُ هذا المِلَفِّ يَعمَل مَع أَيِّ مَخزَنٍ آخَرَ بِتَبديلِ
/// مُتَغَيِّرَين.</para>
///
/// <para><b>ولا تَوقيعَ يُكتَب بِيَد</b>: ‏<c>AWSSDK.S3</c> يَحمِل SigV4.
/// وهذا لَيسَ ذَوقاً — الجارانِ في المُجَلَّدِ نَفسِه يَكتُبانِ
/// تَوقيعَهُما (‏<c>HMACSHA1</c> في Aliyun، و<c>RSA.SignData</c> وJWT في
/// GCS)، وكِلاهُما ‏351 سَطراً <b>تُبنى وتُشحَن ولا تُنادى</b>. وسَطرُ
/// تَوقيعٍ خاطِئٌ لا يُخطِئُ بِوُضوح: يَرُدُّ ‏403 لِسَبَبٍ لا
/// يُقرَأ.</para>
///
/// <para><b>وثَلاثُ خَصائِصَ لِـR2 تُكتَب هُنا ولا تُترَك
/// لِلافتِراض</b>:</para>
/// <list type="number">
/// <item><c>ForcePathStyle</c> — ‏R2 لا يَخدُم أُسلوبَ «الدَلوُ في اسمِ
/// المُضيف»، فَالافتِراضيُّ يُعطي مُضيفاً لا يُحَلّ.</item>
/// <item><c>AuthenticationRegion = "auto"</c> — ‏R2 يَقبَل هذِه
/// وَحدَها، والتَوقيعُ يَحمِلُها.</item>
/// <item><c>RequestChecksumCalculation = WhenRequired</c> — عَميلُ AWS
/// الرابِعُ يُرسِل ‏CRC32 في مُذَيَّلٍ مُقطَّعٍ افتِراضِيّاً، وذلك ما
/// تَختَنِق بِه المَخازِنُ المُتَوافِقَةُ الَّتي لا تُنَفِّذ
/// التَقطيعَ.</item>
/// </list>
/// <para>‏(‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051 §١٨) <b>كُلُّ فِعلٍ عَلى <c>_client</c> داخِلَ المُستَدعي المَعدود</b> (الطَبَقَةُ ٨-ج تَقيسُه بِعُمقِ الأَقواس):
/// الرَفعُ نِداءٌ مَعدودٌ (<see cref="OutboundUnits.FileUpload"/>، كَمِّيَّتُه البايتاتُ المَرفوعَة، ومَصدَرُه المِفتاحُ اسماً مُستَعاراً — مِفتاحُ الصورَةِ يَحمِلُ
/// مُعَرِّفَ صاحِبِها)، والقِراءَةُ والوُجودُ والحَذفُ مُساعِدَةٌ لا تُعَدّ (<see cref="ProviderHelperReasons.StorageRead"/>،
/// <see cref="ProviderHelperReasons.StorageDelete"/>). والطَلَباتُ هِيَ هِيَ (<c>NafathS3PushRequestShapeCharacterizationTests</c>، أُودِعَ قَبلَ التَبديلِ ولَم يُمَسّ).</para>
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly S3FileStorageOptions _opts;
    private readonly ILogger<S3FileStorage> _logger;
    private readonly IAmazonS3 _client;
    private readonly ProviderCalls _calls;

    private static readonly ProviderHelper Reading = new(ProviderNames.S3, ProviderHelperReasons.StorageRead);
    private static readonly ProviderHelper Deleting = new(ProviderNames.S3, ProviderHelperReasons.StorageDelete);

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·١٣ — التَصميم §6.3) <b>مَقعَدُ اختِبار</b>: الباني القَديمُ يَبني المَخزَنَ بِمُستَدعٍ لا يُسَجِّل (<see cref="ProviderCalls.Unrecorded"/>) —
    /// فَيَبقى بِناؤُه في اختِباراتِه وسكربتاتِ المُشَغِّلِ كَما كان. <b>ولا يُناديه كودُ الإنتاج</b>: التَسجيلُ (<see cref="S3FileStorageExtensions.AddS3FileStorage"/>)
    /// يَحُلُّ الباني الأَطوَلَ بِمُستَدعي المُضيف، و<c>MeterTestSeamTests</c> (G6c) يَحمَرُّ عَلى مُنادٍ لِهذا في <c>libs/</c> أَو <c>apps/</c>.</para>
    /// </summary>
    public S3FileStorage(IOptions<S3FileStorageOptions> opts, ILogger<S3FileStorage> logger)
        : this(opts, ProviderCalls.Unrecorded, logger)
    {
    }

    public S3FileStorage(IOptions<S3FileStorageOptions> opts, ProviderCalls calls, ILogger<S3FileStorage> logger)
    {
        _opts = opts.Value;
        _logger = logger;
        _calls = calls;

        // الحارِسُ هُنا **مُكَرَّرٌ عَمداً** مَعَ حارِسِ الإقلاع في
        // `FileStorageSelection`: ذاكَ يَحرُس التَركيب، وهذا يَحرُس مَن
        // بَنى الصِنفَ بِيَدِه في اختِبارٍ أَو سُكريبت. والرِسالَةُ
        // تَذكُر المِفتاحَ الناقِصَ بِحَرفِه.
        foreach (var (name, value) in new[]
                 {
                     (nameof(_opts.Endpoint),        _opts.Endpoint),
                     (nameof(_opts.Bucket),          _opts.Bucket),
                     (nameof(_opts.AccessKeyId),     _opts.AccessKeyId),
                     (nameof(_opts.SecretAccessKey), _opts.SecretAccessKey),
                     (nameof(_opts.PublicBaseUrl),   _opts.PublicBaseUrl),
                 })
            if (string.IsNullOrWhiteSpace(value))
                throw new FileStorageException(
                    $"مَخزَنُ S3 غَير مُهَيَّأ: `Files:S3:{name}` فارِغ.");

        _client = new AmazonS3Client(
            new BasicAWSCredentials(_opts.AccessKeyId, _opts.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = _opts.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public string ProviderName => "S3";

    public async Task<StoredFile> UploadAsync(
        ProviderCall call, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var safe = NormalizeKey(key);

        // الطولُ يُقرَأُ قَبلَ الرَفعِ لِأَنّ المَجرى قَد يَكون غَيرَ
        // قابِلٍ لِلبَحثِ بَعدَه، ولِأَنّ `StoredFile.SizeBytes` جُزءٌ مِن
        // العَقدِ القائِم. وما لا يُعرَف طولُه يُنسَخ إلى الذاكِرَةِ —
        // والسَقفُ مَفروضٌ عِندَ المُستَهلِكِ سَلَفاً (‏5MB لِلصورَة،
        // ‏2MB لِلمَلَفِّ الشَخصيّ).
        Stream body = content;
        long size;
        if (content.CanSeek) { size = content.Length - content.Position; }
        else
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            body = buffer;
            size = buffer.Length;
        }

        // ‏(‏2026-11-12، W13-6·١٣) الرَفعُ نِداءٌ مَعدود: المَخزَنُ يَسِمُه بِاسمِه، والمَصدَرُ المِفتاحُ المُطَهَّرُ اسماً مُستَعاراً، والقَبولُ عَودَةُ الـPUT
        // بِكَمِّيَّةِ البايتاتِ المَرفوعَة؛ وما يَرميه الـSDK مَجهولٌ عَلى النِيَّةِ ويُعادُ رَميُه فَيُحَوَّلُ أَدناه كَما كان.
        var counted = call.By(ProviderNames.S3).About(ProviderCall.FileKeySource, UsageEntryId.Pseudonym(call.Unit, call.App, safe));
        try
        {
            await _calls.InvokeAsync(counted, t => _client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _opts.Bucket,
                Key = safe,
                InputStream = body,
                ContentType = contentType,
                DisablePayloadSigning = false,
            }, t), _ => new ProviderAcceptance(ProviderOutcome.Accepted, size), ct);
        }
        catch (AmazonS3Exception ex)
        {
            throw new FileStorageException($"فَشَلَ رَفعُ {safe} إلى مَخزَنِ S3.", ex);
        }
        finally
        {
            if (!ReferenceEquals(body, content)) await body.DisposeAsync();
        }

        _logger.LogInformation("[S3] رُفِعَ {Key} ({Size} B)", safe, size);
        return new StoredFile(safe, BuildPublicUrl(safe), size, contentType, DateTime.UtcNow);
    }

    public async Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var res = await _calls.HelperAsync(Reading, t => _client.GetObjectAsync(_opts.Bucket, NormalizeKey(key), t), ct);
            return res.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        // ‏S3 لا يُفَرِّق بَينَ «حُذِف» و«لَم يَكُن» في جَوابِ الحَذف،
        // فَالوُجودُ يُسأَلُ عَنه أَوَّلاً لِيَبقى العَقدُ كَما وَصَفَه
        // `IFileStorage` (‏`false` = لَم يَكُن).
        if (!await ExistsAsync(key, ct)) return false;
        await _calls.HelperAsync(Deleting, t => _client.DeleteObjectAsync(_opts.Bucket, NormalizeKey(key), t), ct);
        return true;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _calls.HelperAsync(Reading, t => _client.GetObjectMetadataAsync(_opts.Bucket, NormalizeKey(key), t), ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>رابِطٌ عامٌّ ثابِتٌ مِن
    /// <see cref="S3FileStorageOptions.PublicBaseUrl"/> — لا رابِطٌ
    /// مُوَقَّتٌ مُوَقَّع. **والسَبَبُ أَنّ الرابِطَ يُخَزَّن في
    /// القاعِدَة**: رابِطٌ يَنتَهي بَعدَ ساعَةٍ يُكتَب في وَثيقَةِ إعلانٍ
    /// تُقرَأُ بَعدَ شَهر، فَيَصير الرابِطُ المُوَقَّعُ صورَةً مَكسورَةً
    /// بِتَأخير — وهُوَ العَطَبُ نَفسُه الَّذي جاءَت هذِه المَوجَةُ
    /// لِإغلاقِه. فَالدَلوُ يُفتَح لِلقِراءَةِ العامَّة، والسِرُّ يَبقى
    /// لِلكِتابَةِ وَحدَها.</summary>
    public Task<string> GetPublicUrlAsync(
        string key, TimeSpan? expiresIn = null, CancellationToken ct = default)
        => Task.FromResult(BuildPublicUrl(NormalizeKey(key)));

    /// <summary>نَفسُ تَطهيرِ <c>LocalFileStorage</c> حَرفاً — ‏`..`
    /// و`\` والبادِئَةُ `/`. والمِفتاحُ يُبنى عِندَنا لا يُستَقبَل مِن
    /// نَموذَج، لكِنّ التَطهيرَ يَبقى لِأَنّ التَعاقُدَ واحِدٌ
    /// لِلمُزَوِّدَين.</summary>
    private static string NormalizeKey(string key)
        => key.Replace("..", "", StringComparison.Ordinal)
              .Replace('\\', '/')
              .TrimStart('/');

    private string BuildPublicUrl(string key)
        => $"{_opts.PublicBaseUrl.TrimEnd('/')}/{key}";

    public void Dispose() => _client.Dispose();
}

public static class S3FileStorageExtensions
{
    /// <summary>تَسجيلُ مَخزَنِ S3 كَ<see cref="IFileStorage"/>.
    /// <b>مُفرَدٌ كَجارِه</b> — العَميلُ آمِنٌ لِلتَوازي ويُعادُ
    /// استِعمالُ اتِّصالاتِه، وإنشاؤُه لِكُلِّ طَلَبٍ يُهدِر
    /// المُصافَحات.</summary>
    public static IServiceCollection AddS3FileStorage(
        this IServiceCollection services, Action<S3FileStorageOptions> configure)
    {
        services.Configure(configure);
        // ‏(‏2026-11-12، W13-6·١٣ — ADR-051 §١٨) المُستَدعي المَعدودُ المُفرَدُ بِكاتِبِ المُضيف، والحاوِيَةُ تَحُلُّ الباني الأَطوَلَ (ثَلاثَةُ وُسَطاءَ كُلُّها مُسَجَّلَة)
        // لا مَقعَدَ الاختِبار — يَحرُسُه `MeterCompositionTests` (G6d) عَلى المُضيفِ المُرَكَّب. والسَطرُ أَدناه بِحَرفِه: مِرساةُ مَخزَنِ الكائِناتِ في كاتالوجِ المُزَوِّدين (`ProviderCatalogTests`).
        services.AddProviderCalls();
        services.AddSingleton<IFileStorage, S3FileStorage>();
        return services;
    }
}
