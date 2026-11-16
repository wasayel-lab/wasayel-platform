using ACommerce.Kit.Auth.Server;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>مَرحَلَةُ الرَمزِ تُسَمّى بِالمُحاوَلَةِ لا بِالمَوضوع</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-13). كانَت أَحَدَ عَشَرَ تَحويلَ دُخولٍ
/// (المَتجَرُ بِالجَوّالِ والبَريدِ ونَفاذ، وبابُ الاستوديو) تَحمِلُ الرَقَمَ أَو العُنوانَ أَو الهُوِيَّةَ في الاستِعلام — إلى سِجِلِّ كُلِّ وَكيلٍ وتاريخِ
/// المُتَصَفِّح. الآن يَحمِلُ الرابِطُ مُعَرِّفَ المُحاوَلَةِ (عَشوائِيٌّ، يَموتُ بِمَوتِها)، والصَفحَةُ تَقرَأُ المَوضوعَ مِنه.</para>
///
/// <para><b>ومُستَهلِكوه</b>: نِقاطُ الدُخولِ في <c>MarketplaceTemplateExtensions</c> — بَعدَ الرَمزِ الخاطِئِ في الثَلاثَة، وبَعدَ إرسالِ
/// الاستوديو (إصدارُه لا يُعيدُ المُعَرِّف؛ ونِقاطُ المَتجَرِ تَأخُذُه مِمّا أَعادَه الإصدار) — وصَفحَتا الرَمز (<c>Login.razor</c> و<c>StudioAuth.razor</c>).</para>
/// </summary>
public static class LoginStageQuery
{
    /// <summary><c>stage=verify&amp;attempt=…</c> لِلمُحاوَلَةِ الحَيَّةِ لِلمَوضوع — أَو <c>null</c> إن لَم تَبقَ.</summary>
    public static string? Verify(string tenantSlug, string subject, AuthHandlers.AuthKind kind)
        => AuthHandlers.LiveAttemptOf(tenantSlug, subject, kind) is { } id
            ? $"stage=verify&attempt={Uri.EscapeDataString(id)}"
            : null;

    /// <summary>بَعدَ رَمزٍ خاطِئ: مَرحَلَةُ الرَمزِ نَفسُها بِخَطَئِها ما بَقِيَت المُحاوَلَة، وإلّا الخُطوَةُ الأُولى بِالخَطَإ نَفسِه — فَلا تُعادُ
    /// صَفحَةُ رَمزٍ لِمُحاوَلَةٍ أَحرَقَتها الأَخطاءُ أَو انتَهَت (كانَت تُعادُ بِالمَوضوعِ فَيُكتَبُ الرَمزُ إلى لا شَيء).</summary>
    public static string Retry(string tenantSlug, string subject, AuthHandlers.AuthKind kind, string err)
        => Verify(tenantSlug, subject, kind) is { } stage ? $"{stage}&err={err}" : $"err={err}";

    /// <summary>
    /// <para><b>بَعدَ رَمزٍ خاطِئٍ في بابٍ — المُحاوَلَةُ الَّتي فَتَحَت صَفحَةَ الرَمزِ لا المَوضوعُ المَكتوب</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2
    /// الجَولَة ١؛ تَوصيفُه <c>2590bd2</c>): تُعادُ مَرحَلَةُ الرَمزِ بِـ<see cref="Retry(string, string, AuthHandlers.AuthKind, string)"/> حينَ يُسَمّي
    /// <paramref name="attemptId"/> — حَقلُ الاستِمارَةِ المَخفِيُّ مِن رابِطِها — مُحاوَلَةً حَيَّةً لِلمَوضوعِ نَفسِه، وإلّا <c>err=…</c> وَحدَه لِكُلِّ
    /// مُرسِل. كانَت النِقاطُ تُنادي الحِملَ الرُباعيَّ بِالمَوضوعِ المَكتوب، والكاتِبُ يَختارُه: جَوّالُ غَيرِه بِرَمزٍ خاطِئٍ يُخبِرُ أَيَدخُلُ صاحِبُه الآن
    /// ويُعطي مُعَرِّفَ مُحاوَلَتِه. ومُعَرِّفُ المُحاوَلَةِ عَشوائِيٌّ يَموتُ بِمَوتِها، فَلا يَملِكُه إلّا مَن فُتِحَت لَه الصَفحَة.</para>
    /// </summary>
    public static string Retry(string tenantSlug, string? attemptId, string subject, AuthHandlers.AuthKind kind, string err)
        => AuthHandlers.SubjectOf(tenantSlug, attemptId, kind) is { } owner && string.Equals(owner, subject, StringComparison.Ordinal)
            ? Retry(tenantSlug, subject, kind, err)
            : $"err={err}";

    /// <summary>المَوضوعُ الَّذي تَعرِضُه صَفحَةُ الرَمزِ وتُرسِلُه في حَقلِها المَخفِيّ: مِن المُحاوَلَةِ أَوَّلاً، ثُمَّ ما في الرابِطِ كَما هو. والثاني
    /// <b>نَصُّ الزائِرِ نَفسِه</b> — لا يَضَعُه خادِمُنا في رابِطٍ بَعدَ اليَوم — ويَبقى لِأَنّ أَداةَ اللَقَطاتِ (`capture-appearance.sh`) وسِكربتَي
    /// المَشيِ يَفتَحانِ مَرحَلَةَ الرَمزِ بِرَقَمِ عَيِّنَةٍ بِلا جَلسَة؛ وحَذفُه يُغَيِّرُ بايتاتِ ثَلاثِ لَقَطاتٍ بِلا مَكسَبٍ أَمنِيّ.</summary>
    public static string? SubjectFor(string tenantSlug, string? attemptId, AuthHandlers.AuthKind kind, string? typed)
        => AuthHandlers.SubjectOf(tenantSlug, attemptId, kind) ?? typed;
}
