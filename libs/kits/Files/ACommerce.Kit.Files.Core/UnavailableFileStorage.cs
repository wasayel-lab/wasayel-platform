using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Files;

/// <summary>
/// <para><b>مَخزَنُ المِلَفّاتِ حينَ لا مَخزَن — يَقول «لا» بِسَبَبٍ
/// مَقروء، ولا يَنفَجِر ولا يَكذِب.</b></para>
///
/// <para><b>ولِماذا صِنفٌ بَدَلَ «لا تَسجيلَ إطلاقاً»</b>: ‏
/// <c>IFileStorage</c> وَسيطٌ في جِسمَي
/// <c>POST /{slug}/listings/create</c> و<c>POST /{slug}/me/save</c>.
/// فَتَركُ الوِعاءِ فارِغاً يَعني انفِجارَ حَلٍّ عِندَ أَوَّلِ إعلانٍ
/// يُنشَر — <b>عُطلٌ عامٌّ في مَسارٍ لا عَلاقَةَ لَه بِالصُوَر</b>، بَدَلَ
/// رَفضٍ واحِدٍ مَوضِعيّ. نَفسُ حُجَّةِ
/// <c>UnavailablePaymentProvider</c> حَرفاً.</para>
///
/// <para><b>والكِتابَةُ تَرمي، والقِراءَةُ لا</b> — وهذا هُوَ السُقوطُ
/// الآمِن بِعَينِه: الرَفضُ عِندَ الكِتابَةِ يَمنَع الرابِطَ المُعَلَّقَ
/// مِن الوُجودِ أَصلاً، فَلا صورَةَ مَكسورَةٌ تُرسَم بَعدَ شَهر. وذلك
/// أَقوى مِن أَيِّ بَديلٍ يُعرَض عِندَ القِراءَة، لِأَنَّه يَقَع
/// <b>قَبلَ</b> أَن تُكتَبَ الكَذِبَةُ في القاعِدَة. أَمّا القِراءَةُ
/// فَتَرُدُّ «غَيرُ مَوجود» بِهُدوء — فَما لَم يُكتَب لا يُقرَأ،
/// والانفِجارُ عِندَ عَرضِ صَفحَةٍ عُطلٌ بِلا فائِدَة.</para>
///
/// <para><b>ولا يَحمِلُ <see cref="IDevelopmentStubFileStorage"/></b> —
/// فَهُوَ لَيسَ زائِلَ القُرص: لا قُرصَ لَه إطلاقاً. وحَملُه العَلامَةَ
/// كانَ سَيُفشِلُ الإقلاعَ في الإنتاجِ على المَضبوط.</para>
/// </summary>
public sealed class UnavailableFileStorage : IFileStorage
{
    /// <summary>السَبَبُ المَرميُّ في كُلِّ كِتابَة — <b>مَوضِعٌ واحِد</b>
    /// يَقرَؤُه المُنتِجُ والمُختَبِر، فَلا يَنجَرِف نَصّان.</summary>
    public const string Reason =
        "لا مَخزَنَ مِلَفّاتٍ دائِمٌ مَضبوطٌ في هذِه النُسخَة — فَلا تُكتَب "
        + "صورَةٌ يَذهَب مِلَفُّها ويَبقى رابِطُها.";

    public string ProviderName => "unavailable";

    /// <summary>تَرمي دائِماً. <b>ومُستَهلِكاها يَعرِفانِ الجَواب</b>:
    /// جِسمُ إنشاءِ الإعلانِ يَبتَلِع فَشَلَ الصورَةِ ويُتِمُّ الإعلانَ
    /// بِلا صُوَر (تَعليقُه في الكودِ يَقول ذلك مُنذُ كُتِب)، وجِسمُ
    /// المَلَفِّ الشَخصيِّ يُتِمُّ حِفظَ الاسمِ بِلا صورَة.</summary>
    public Task<StoredFile> UploadAsync(
        string key, Stream content, string contentType, CancellationToken ct = default)
        => throw new FileStorageException(Reason);

    public Task<Stream?> ReadAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream?>(null);

    public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>لا رابِطَ لِمِلَفٍّ لَم يُكتَب — سِلسِلَةٌ فارِغَة، ولا
    /// يُخترَع مَسارٌ يَرُدُّ ‏404.</summary>
    public Task<string> GetPublicUrlAsync(
        string key, TimeSpan? expiresIn = null, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}

public static class UnavailableFileStorageExtensions
{
    /// <summary>الفَشَلُ المُغلَق — مُسَجَّلٌ خارِجَ التَطويرِ بِلا
    /// تَهيئَةِ مَخزَنٍ دائِم، فَلا يَنفَجِر حَلُّ النُقطَتَينِ
    /// الحَيَّتَين، وتُرَدُّ كُلُّ كِتابَةٍ بِسَبَبٍ مَقروء.</summary>
    public static IServiceCollection AddUnavailableFileStorage(this IServiceCollection services)
    {
        services.AddSingleton<IFileStorage, UnavailableFileStorage>();
        return services;
    }
}
