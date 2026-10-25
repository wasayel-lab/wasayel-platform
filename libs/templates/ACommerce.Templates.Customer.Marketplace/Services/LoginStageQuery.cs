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

    /// <summary>المَوضوعُ الَّذي تَعرِضُه صَفحَةُ الرَمزِ وتُرسِلُه في حَقلِها المَخفِيّ: مِن المُحاوَلَةِ أَوَّلاً، ثُمَّ ما في الرابِطِ كَما هو. والثاني
    /// <b>نَصُّ الزائِرِ نَفسِه</b> — لا يَضَعُه خادِمُنا في رابِطٍ بَعدَ اليَوم — ويَبقى لِأَنّ أَداةَ اللَقَطاتِ (`capture-appearance.sh`) وسِكربتَي
    /// المَشيِ يَفتَحانِ مَرحَلَةَ الرَمزِ بِرَقَمِ عَيِّنَةٍ بِلا جَلسَة؛ وحَذفُه يُغَيِّرُ بايتاتِ ثَلاثِ لَقَطاتٍ بِلا مَكسَبٍ أَمنِيّ.</summary>
    public static string? SubjectFor(string tenantSlug, string? attemptId, AuthHandlers.AuthKind kind, string? typed)
        => AuthHandlers.SubjectOf(tenantSlug, attemptId, kind) ?? typed;
}
