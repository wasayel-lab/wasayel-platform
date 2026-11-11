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

    /// <summary>الوَسيط: 400 لِرَأسٍ لا يُقرَأ، وما سِواه إلى ما بَعدَه.</summary>
    public static IApplicationBuilder UseHostHeaderGuard(this IApplicationBuilder app)
        => app.Use(next => ctx =>
        {
            if (IsReadable(ctx.Request.Headers.Host.ToString())) return next(ctx);
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
}
