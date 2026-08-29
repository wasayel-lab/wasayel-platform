using ACommerce.Platform.I18n;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>حُقولُ الكِيانِ النِظاميِّ في الوَثائِقِ القانونِيَّة —
/// وكَم مِنها لَم يُملَأ بَعد.</b></para>
///
/// <para><b>ولِماذا مِلَفٌّ لا سَطرٌ في <c>@code</c></b> (القاعِدَة ٢):
/// قائِمَةٌ مَكتوبَةٌ داخِلَ <c>.razor</c> <b>لا يَقرَؤُها
/// اختِبار</b>، فَمِفتاحٌ نائِبٌ يُضافُ غَداً ولا يُدرَجُ فيها
/// يَمُرُّ بِلا تَحذير — وذاكَ بِعَينِه الانجِرافُ الصامِت. وهُنا
/// يَحرُسُها <c>PlatformDocFieldsTests</c>: <b>كُلُّ مِفتاحِ كِيانٍ
/// في <c>ar.json</c> مُدرَجٌ، والعَكسُ كَذلك</b>.</para>
///
/// <para><b>وَالتَوَتُّرُ مَعَ القاعِدَة ١ يُقالُ ولا يُبتلَع</b>:
/// مُستَهلِكُه في وَقتِ التَشغيل <b>واحِدٌ</b> — شاشَةُ <c>/admin</c> —
/// والقاعِدَةُ تَشتَرِط ثَلاثَة. والحُجَّة: هذا <b>لَيسَ تَجريداً</b> —
/// لا واجِهَةَ ولا طَبَقَةَ ولا خِيارَ تَركيب، بَل <b>خَمسُ سَلاسِلَ
/// وعَدّاد</b>. والقاعِدَةُ ٢ أَسبَقُ هُنا: البَديلُ كِتابَتُها في
/// <c>.razor</c> حَيثُ <b>لا فاحِصَ يَراها</b>. <b>وشَرطُ الحَذف</b>:
/// إن سَقَطَ الحارِسُ، فَمَكانُها جِسمُ تِلكَ الشاشَة.</para>
/// </summary>
public static class PlatformDocFields
{
    /// <summary>سابِقَةُ مَفاتيحِ الوَثائِقِ — يَقرَؤُها الحارِسُ
    /// لِيَجِدَ ما لَم يُدرَج.</summary>
    public const string KeyPrefix = "platform.doc.";

    /// <summary>
    /// <para><b>ما يَملَؤُه المالِكُ قَبلَ تَقديمِ الوَثائِق.</b>
    /// وهي الحُقولُ الَّتي تُصَيَّرُ داخِلَ الوَثائِقِ الخَمسِ
    /// مُحاطَةً بِـ<c>wsl-placeholder</c>.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> EntityKeys = new[]
    {
        "platform.doc.entity_name",
        "platform.doc.entity_registration",
        "platform.doc.entity_address",
        "platform.doc.entity_country",
        "platform.doc.contact_email",
    };

    /// <summary><b>كَم حَقلاً ما زالَ نائِباً في هذِه اللُغَة.</b>
    /// و<c>0</c> تَعني «الوَثائِقُ جاهِزَةٌ لِلتَقديم» — وهي
    /// الجُملَةُ الَّتي يَنتَظِرُها المُشرِف.</summary>
    public static int UnfilledCount(string lang)
        => EntityKeys.Count(k => LocaleCatalog.IsPlaceholderKey(lang, k));
}
