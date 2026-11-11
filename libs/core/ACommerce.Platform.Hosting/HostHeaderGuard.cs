using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>رَأسُ <c>Host</c> لا يُقرَأُ يُجابُ 400 قَبلَ أَن يَقرَأَه أَحَد</b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2b-5 وSEC-C2b-R2-6 وC2b-P2-06
/// وSEC-C2b-R3-3؛ ADR-050 §١٣).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (عِندَ المُراجِعين، في وَضعِ المَسارِ وبِنِطاقٍ مُهَيَّأ): <c>Host: xn--zz.localhost</c> و<c>xn--mgbh0fb-vendor.localhost</c> ←
/// <b>500</b> (<c>ArgumentException: Decoded string is not a valid IDN name</c> مِن <c>HostString.FromUriComponent</c> حينَ يَقرَأُ الحاقِنُ <c>Request.Host</c>)،
/// و<c>localhost:99999</c> ← <b>500</b> (<c>UriFormatException: Invalid port</c> في <c>NavigationManager</c>). سابِقٌ لِلتَيّار، وسِجِلُّ « wildcard <c>*.R</c>» يَجعَلُ
/// هذِه الأَسماءَ تَبلُغُ الأَصلَ مِن أَيِّ زائِر — فَمُعَدَّلُ الـ500 وحَجمُ السِجِلِّ بِيَدِه.</para>
///
/// <para><b>والقَرارُ دالَّةٌ نَقِيَّة</b> تَقرَأُ الرَأسَ الخامَ (<c>Headers.Host</c> — لا <c>Request.Host</c> الَّذي يَرمي): يُفَكُّ كَما يَفُكُّه الإطار، والمَنفَذُ في
/// 0..65535، وعُنوانٌ يُبنى مِنه. ويُرَكَّبُ أَوَّلَ <see cref="HostingExtensions.UsePlatformRequestFront"/> (قَبلَ الحاقِن) — لا يَقرَأُ حالَةً مَخزونَة (القاعِدَة ٧).
/// وفارِغٌ يَمُرُّ كَما كان (HTTP/1.0 بِلا <c>Host</c>؛ وKestrel يَرُدُّ غِيابَه في HTTP/1.1).</para>
/// </summary>
public static class HostHeaderGuard
{
    /// <summary>أَيُقرَأُ هذا الرَأسُ مُضيفاً ومَنفَذاً وعُنواناً بِلا استِثناء؟</summary>
    public static bool IsReadable(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return true;
        try
        {
            var host = HostString.FromUriComponent(raw);
            if (string.IsNullOrEmpty(host.Host)) return false;
            var colon = raw.LastIndexOf(':');
            if (colon > raw.LastIndexOf(']'))
            {
                var digits = raw[(colon + 1)..];
                if (digits.Length is 0 or > 5 || !digits.All(char.IsAsciiDigit) || int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) > 65535)
                    return false;
            }
            return Uri.TryCreate($"http://{host.ToUriComponent()}/", UriKind.Absolute, out _);
        }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// <para><b>واجِهَةُ الرُؤوسِ المُمَرَّرَةِ بِاسمِها</b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-2 وC2z-PATHS-R1-03): <c>Program.cs</c> كانَ
    /// يُرَكِّبُ <c>app.UseForwardedHeaders()</c> سَطراً بِلا اسم، وهُوَ ما يَقرَأُ <c>X-Forwarded-Host</c> قَبلَ الحارِسِ أَعلاه — فَتَوصيفُ ما يَنجو يَقيسُ هذا التَركيبَ
    /// بِاسمِه عَلى مُضيفٍ مُصَغَّرٍ بِسِياسَةِ المَنَصَّةِ نَفسِها (<see cref="ForwardedHeadersPolicy"/>)، فَيَبقى مِلَفُّه بِبَصمَتِه قَبلَ التَبديلِ وبَعدَه.
    /// والخِياراتُ مِن الحاوِيَةِ كَما كانَت (<c>Configure&lt;ForwardedHeadersOptions&gt;(policy.ApplyTo)</c>).</para>
    /// <para><b>‏(‏2026-11-11، التَبديل — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والرَأسُ يُسأَلُ قَبلَ أَن يُقرَأ</b>: خَلفَ عامِلٍ يُعيدُ كِتابَةَ <c>Host</c> (DEPLOY الخُطوَةُ ٦،
    /// <c>ForwardedHeaders__AllowedHosts=R,*.R</c>) يَصِلُ اسمُ الزائِرِ في <c>X-Forwarded-Host</c>، و<c>ForwardedHeadersMiddleware</c> يَفُكُّه بِـ
    /// <c>HostString.FromUriComponent</c> قَبلَ أَيِّ حارِس — فَـ<c>xn--zz.R</c> كانَ <b>500</b> بِأَثَرِ مُكَدَّس (مَقيسٌ عِندَ <c>61dab36</c>)، والحارِسُ في
    /// <see cref="HostingExtensions.UsePlatformRequestFront"/> بَعدَه لا يَراه. الآن <see cref="ForwardedReadable"/> قَبلَه: <c>Host</c> الخامُ يُقرَأ، ومَتى صَدَّقَت
    /// السِياسَةُ الرَأسَ (<see cref="ForwardedHeadersPolicy.TrustsForwardedHost"/>) فَكُلُّ قيمَةٍ مَفصولَةٍ بِفاصِلَةٍ فيه تُقرَأُ بِـ<see cref="IsReadable"/> — وإلّا 400.
    /// وبِلا قائِمَةٍ لا يُقرَأُ الرَأسُ فَلا يُسأَل (الإطارُ لا يَلمِسُه). والحارِسُ في الأُنبوبِ باقٍ لِتَركيبٍ بِلا هذِه الواجِهَة.</para>
    /// </summary>
    public static IApplicationBuilder UsePlatformForwardedHeaders(this IApplicationBuilder app, ForwardedHeadersPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        app.Use(next => ctx =>
        {
            var headers = ctx.Request.Headers;
            if (ForwardedReadable(policy, headers.Host.ToString(), headers["X-Forwarded-Host"].ToString())) return next(ctx);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        return app.UseForwardedHeaders();
    }

    /// <summary>
    /// <para><b>القَرارُ النَقِيُّ قَبلَ الرُؤوسِ المُمَرَّرَة</b>: <paramref name="host"/> الخامُ يُقرَأ (<see cref="IsReadable"/>)، و<paramref name="forwardedHost"/> — مَتى
    /// صَدَّقَته <paramref name="policy"/> — كُلُّ قيمَةٍ فيه تُقرَأ (القيَمُ المُتَعَدِّدَةُ تَصِلُ مَفصولَةً بِفاصِلَة، والإطارُ يَأخُذُ آخِرَها؛ وتُسأَلُ كُلُّها فَلا يَتَعَلَّقُ
    /// القَرارُ بِحَدِّ <c>ForwardLimit</c>).</para>
    /// </summary>
    public static bool ForwardedReadable(ForwardedHeadersPolicy policy, string? host, string? forwardedHost)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!IsReadable(host)) return false;
        if (!policy.TrustsForwardedHost || string.IsNullOrEmpty(forwardedHost)) return true;
        foreach (var value in forwardedHost.Split(','))
            if (!IsReadable(value.Trim())) return false;
        return true;
    }

    /// <summary>الوَسيط: 400 لِرَأسٍ لا يُقرَأ، وما سِواه إلى ما بَعدَه.</summary>
    public static IApplicationBuilder UseHostHeaderGuard(this IApplicationBuilder app)
        => app.Use(next => ctx =>
        {
            if (IsReadable(ctx.Request.Headers.Host.ToString())) return next(ctx);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
}
