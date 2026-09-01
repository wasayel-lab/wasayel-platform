namespace ACommerce.Kit.Compliance;

/// <summary>
/// <para><b>لَقطَةُ وَقائِعَ يُحكَمُ عَلَيها — لا مَصدَرٌ يُستَعلَمُ
/// مِنه.</b> الفاحِصُ دالَّةٌ نَقِيَّة: لا قاعِدَةَ بَيانات، ولا
/// وَقت، ولا عَشوائيَّة (نَفسُ عَقدِ <c>DealCancelAuthorization</c>
/// و<c>RoleDefinitionValidator</c>). فَنَفسُ اللَقطَةِ تُعطي نَفسَ
/// التَقريرِ دائِماً.</para>
///
/// <para><b>ولِماذا لَقطَةٌ لا واجِهَةُ استِعلام</b>: واجِهَةٌ تَعني
/// أَنَّ الفاحِصَ يَنادي أَثناءَ الحُكم، فَيَختَلِفُ حُكمُه بِاختِلافِ
/// مَن نادى ومَتى — ويَستَحيلُ حَقنُ عَيبٍ مَضبوطٍ لِقِياسِ أَنَّه
/// يَراه. واللَقطَةُ تَجعَلُ الفاحِصَ مَقيساً بِنَفسِ السَطرِ في
/// الاختِبارِ وفي الشاشَة.</para>
///
/// <para><b>ومَن يَجمَعُ اللَقطَةَ يُعلِنُ مَصدَرَه</b>: النُصوصُ مِن
/// <c>LocaleCatalog</c>، والمَساراتُ مِن جَدوَلِ نِهاياتِ التَطبيقِ
/// الحَيّ. وكِلاهُما مَقيسٌ لا مَظنون — والقاعِدَة ١٠ تَشتَرِطُ أَن
/// تَكونَ الأَداةُ نَفسُها مَقيسَة.</para>
/// </summary>
/// <param name="Level">مِن <see cref="ComplianceLevels"/>. الالتِزاماتُ
/// تُصَفّى بِه: مَنَصَّةٌ تُفحَصُ بِالتِزاماتِ المَنَصَّة، ومَتجَرٌ
/// بِالتِزاماتِ المُستَأجِر.</param>
/// <param name="SubjectId">‏slug المَتجَر، أَو <c>platform</c>.</param>
/// <param name="DisplayNameAr">الاسمُ كَما يُعرَضُ في اللَوحَة.</param>
/// <param name="Texts">قامُوسُ النُصوصِ المَقروء: مِفتاحٌ ← قيمَة.
/// <b>مِفتاحٌ غائِبٌ عَن الخَريطَةِ = غائِبٌ عَن القامُوس</b>، وقيمَةٌ
/// <c>null</c> كَذلك.</param>
/// <param name="Routes">أَنماطُ المَساراتِ المُسَجَّلَةُ فِعلاً.</param>
public sealed record ComplianceSubject(
    string Level,
    string SubjectId,
    string DisplayNameAr,
    IReadOnlyDictionary<string, string?> Texts,
    IReadOnlySet<string> Routes)
{
    /// <summary>القيمَةُ أَو <c>null</c>. لا سُقوطَ ولا تَخمين.</summary>
    public string? Text(string key) =>
        Texts.TryGetValue(key, out var v) ? v : null;

    /// <summary>هَل هذا النَمَطُ مُسَجَّلٌ في جَدوَلِ المَسارات؟
    /// المُقارَنَةُ حَرفِيَّةٌ عَلى النَمَطِ كَما كُتِب
    /// (<c>/{slug}/me/delete</c>) — لا مُطابَقَةَ عُنوانٍ فِعليّ:
    /// وُجودُ النَمَطِ هُوَ الشاهِد، وتَحويلُ القيمِ فيه شَأنُ
    /// المُوَجِّه.</summary>
    public bool HasRoute(string pattern) => Routes.Contains(pattern);
}
