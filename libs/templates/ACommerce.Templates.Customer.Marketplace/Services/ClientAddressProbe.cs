using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>عُنوانُ الزائِرِ كَما يَراه الخادِم — قِياسٌ لا حَدّ</b> (‏2026-10-28، قَرارُ المالِك د57 «ب»). حَدٌّ لِكُلِّ عُنوانٍ (‏IP) فَوقَ سَقفِ
/// المَنَصَّةِ لا مَعنى لَه عَلى عُنوانٍ يُمكِنُ تَزويرُه: <c>ForwardedHeadersPolicy</c> يُفرِغُ <c>KnownProxies</c> (عُنوانُ حافَّةِ الـSpace غَيرُ مَعلوم)،
/// فَيَأخُذُ <c>UseForwardedHeaders</c> أَيمَنَ قيمَةٍ في <c>X-Forwarded-For</c> عُنواناً لِلزائِر (‏<c>ForwardLimit</c> = ‏1) ويَنقُلُ عُنوانَ النِدِّ إلى
/// <c>X-Original-For</c>. فَإن كانَ الـSpace <b>يُلحِقُ</b> عُنوانَ الزائِرِ الحَقيقيَّ يَصيرُ <c>RemoteIpAddress</c> صادِقاً ولو أَرسَلَ الزائِرُ رَأساً مُزَوَّراً،
/// وإن كانَ <b>يُمَرِّرُ</b> رَأسَ الزائِرِ كَما هُوَ صارَ العُنوانُ ما يَكتُبُه الزائِر. وهذا لا يُعرَفُ إلّا بِنِداءٍ حَقيقيٍّ خَلفَ الوَسيط («ج» د57).</para>
///
/// <para><b>فَهذِه الدالَّةُ تَقرَأُ ثَلاثَةَ أَشياءَ لا غَير</b>: العُنوانَ بَعدَ الوَسيط (<c>Connection.RemoteIpAddress</c>)، وما بَقِيَ في
/// <c>X-Forwarded-For</c> بَعدَه، و<c>X-Original-For</c> الَّذي كَتَبَه. لا تَكتُبُ ولا تُخَزِّنُ ولا تَحُدّ — والبابُ إلَيها لِمُشرِفِ المَنَصَّةِ وَحدَه
/// (‏<c>GET /admin/diagnostics/client-address</c>) لِأَنَّها تُعيدُ عُنوانَ الطالِبِ نَفسِه فَلا تَكشِفُ غَيرَه.</para>
///
/// <para><b>مُؤَقَّتَةٌ بِقَرار — وشَرطُ حَذفِها مَكتوبٌ هُنا</b>: «نُقطَةُ قِياسٍ مُؤَقَّتَةٌ تُنشَرُ ثُمَّ تُحذَف». تُحذَفُ هِيَ وبابُها وصُفوفُها في كوميتٍ
/// واحِدٍ يَومَ يُسَجِّلُ المالِكُ نَتيجَةَ القِياسِ في سِجِلِّه (يُلحِقُ الـSpace العُنوانَ أَم يُمَرِّرُه)؛ وحَدُّ العُنوانِ (‏٣٠ في السّاعَةِ عَلى المَنَصَّةِ كُلِّها)
/// لا يُبنى إلّا بَعدَ تِلكَ النَتيجَة، وإن ظَهَرَ أَنَّ العُنوانَ يُزَوَّرُ فَلا يُبنى أَبَداً (القاعِدَة ١: لا حَدَّ قَبلَ أَن يَصدُقَ مَصدَرُه).</para>
/// </summary>
public static class ClientAddressProbe
{
    /// <summary>ما يَراه الخادِمُ لِهذا الطَلَب. <c>null</c> = غائِب.</summary>
    public sealed record Report(string? RemoteIpAddress, string? XForwardedFor, string? XOriginalFor);

    public const string ForwardedForHeader = "X-Forwarded-For";
    public const string OriginalForHeader = "X-Original-For";

    /// <summary>يَقرَأُ الطَلَبَ ولا يَمَسُّه.</summary>
    public static Report Of(HttpRequest req) => new(
        req.HttpContext.Connection.RemoteIpAddress?.ToString(),
        Header(req, ForwardedForHeader),
        Header(req, OriginalForHeader));

    private static string? Header(HttpRequest req, string name)
        => req.Headers.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString() : null;
}
