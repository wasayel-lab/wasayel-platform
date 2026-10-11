namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>إلى أَينَ يَعودُ أُنبوبُ بَدءِ المُحادَثَةِ حينَ يَفشَل</b> — دالَّةٌ نَقِيَّة.
/// وُلِدَ <c>POST /{slug}/users/{userId}/chat</c> لِصَفحَةِ السائِقينَ فَكانَ يُسقِطُ كُلَّ
/// فَشَلٍ (دُخولٌ، نَفسٌ، طَرَفٌ مَحذوف) عَلى <c>/drivers</c>. وصارَ لَه مُستَهلِكٌ ثانٍ
/// — زِرُّ «تَواصَل مَع البائِع» عَلى صَفحَةِ الصَفقَة (‏S1·٧) — فَمُشتَرٍ حُذِفَ حِسابُ
/// بائِعِه كانَ يُقذَفُ إلى صَفحَةِ السائِقينَ بِلا كَلِمَة (مُراجَعَةُ المَوجَة ١).</para>
///
/// <para><b>المَنشَأُ يُحَلُّ بِـ<see cref="LocalRedirect.Resolve"/> القائِمَةِ لا هُنا</b>
/// (القاعِدَة ٨، و<c>LocalRedirectTests.NoEndpointDecidesRedirectLocalityOnItsOwn</c>
/// يَفرِضُ أَنّ كُلَّ قِراءَةٍ لِـ<c>returnUrl</c> مَوصولَةٌ بِها): <c>returnUrl</c> مَحَلِّيٌّ
/// أَو مَسقَطُ الرَأسِ. وهذِه الدالَّةُ تُلحِقُ الرَمزَ وَحدَه، فَتَقرَؤُه الصَفحَةُ
/// المُنشِئَةُ مِن القامُوس (<c>DealRefusalTexts</c>)؛ وصَفحَةُ السائِقينَ لا تَقرَأُ
/// <c>err</c> فَيَمُرُّ بِها الرَمزُ بِلا أَثَر.</para>
/// </summary>
public static class ChatStartReturn
{
    /// <summary>رَمزُ الفَشَل: الطَرَفُ المَطلوبُ لَم يَعُد مُسَجَّلاً في المَتجَر.</summary>
    public const string PartnerMissing = "partner_missing";

    /// <summary>وِجهَةُ الفَشَلِ بِرَمزِه: المَنشَأُ (المَحلولُ سَلَفاً) مَعَ <c>?err=</c> —
    /// أَو <c>&amp;err=</c> إن حَمَلَ استِعلاماً.</summary>
    public static string Failure(string origin, string code)
        => origin + (origin.Contains('?') ? "&" : "?") + "err=" + code;
}
