using ACommerce.Platform.Shared.Metering;

namespace ACommerce.Kit.Files;

/// <summary>
/// مُجَرَّد تَخزين مَلَفّات — يُخفي إن كان Local/Aliyun OSS/Google Cloud.
/// كُلّ الـ providers يَستَلِمون stream + metadata، يُرجِعون URL ثابِت.
/// التَّبديل بَين provider وآخَر = سَطر DI واحِد في Program.cs.
/// </summary>
public interface IFileStorage
{
    /// <summary>اِسم المُزَوِّد (لِلتَّشخيص).</summary>
    string ProviderName { get; }

    /// <summary>اِرفَع مَلَفّاً. <paramref name="key"/> = مَسار مَنطِقيّ
    /// (مَثَلاً <c>"tenants/ejar/listings/{id}/cover.jpg"</c>).</summary>
    /// <remarks>‏(‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051 §١٩؛ التَصميم §6.1) <paramref name="call"/> بِوَحدَةِ <see cref="OutboundUnits.FileUpload"/> أَوَّلَ
    /// الوُسَطاءِ في التَوقيعِ نَفسِه ولا أَخَ بِدونِه — نِسيانُه خَطَأُ بِناء (القاعِدَة ٦). المُنادي (بابا الصورَة) يَحمِلُ المَتجَرَ ومُعَرِّفَ نِداءٍ جَديد (رَفعٌ ثانٍ
    /// رَفعٌ آخَر)، والمُزَوِّدُ الَّذي يُغادِرُ العَمَلِيَّةَ يَعُدُّ بايتاتِه ويَسِمُ المَصدَرَ بِمِفتاحٍ مُستَعار؛ والمَحَلِّيُّ وغَيرُ المُتاحِ لا يُغادِرانِ فَلا يَعُدّان.</remarks>
    Task<StoredFile> UploadAsync(
        ProviderCall call, string key, Stream content, string contentType,
        CancellationToken ct = default);

    /// <summary>اِجلِب stream مَلَفّ مَوجود.</summary>
    Task<Stream?> ReadAsync(string key, CancellationToken ct = default);

    /// <summary>حُذِف مَلَفّ.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>هَل المَلَفّ مَوجود؟</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>URL عامّ لِعَرض المَلَفّ في المُتَصَفِّح. قَد يَكون CDN URL أو
    /// signed URL مُؤَقَّت. تُمَرَّر <paramref name="expiresIn"/> لِمُزَوِّدي
    /// signed URL (Aliyun/GCS); تُتَجاهَل لِلـ Local الَّذي يَخدُم مُباشَرَة.</summary>
    Task<string> GetPublicUrlAsync(
        string key, TimeSpan? expiresIn = null, CancellationToken ct = default);
}

/// <summary>مَلَفّ مُخَزَّن — يُعاد بَعد upload ناجِح.</summary>
public sealed record StoredFile(
    string Key,
    string PublicUrl,
    long SizeBytes,
    string ContentType,
    DateTime UploadedAt);

/// <summary>اِستِثناء عامّ مِن مَكتَبَة Files.</summary>
public class FileStorageException : Exception
{
    public FileStorageException(string message) : base(message) { }
    public FileStorageException(string message, Exception inner) : base(message, inner) { }
}
