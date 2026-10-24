using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Culture;

/// <summary>
/// سِياق ثَقافيّ لِكُلّ طَلَب — اللُّغَة، المَنطِقَة الزَمَنِيَّة، العُملَة،
/// نِظام الأَرقام. مَفهوم مَنقول مِن libs/kits/Culture القَديم. تُحدَّد
/// مِن headers: <c>Accept-Language</c>, <c>X-Timezone</c>, <c>X-Currency</c>.
/// </summary>
public interface ICultureContext
{
    string Language { get; }      // "ar" | "en"
    string TimeZone { get; }      // "Asia/Riyadh"
    string Currency { get; }      // "SAR"
    string Numerals { get; }      // "western" | "arabic"
    DateTime NowInZone { get; }
    string FormatMoney(decimal amount);

    /// <summary>لَحظَةٌ مُخَزَّنَةٌ بِـUTC بِساعَةِ مِنطَقَةِ هذا الطَلَب — <see cref="CultureTime.InZone"/> بِـ<see cref="TimeZone"/>.
    /// كُلُّ وَقتٍ يَراه المُستَخدِمُ يَمُرُّ مِن هُنا (‏2026-10-24، تَيّارُ S15 البَند B).</summary>
    DateTime InZone(DateTime storedUtc);
}

/// <summary>
/// <para><b>الوَقتُ المَعروضُ — دالَّةٌ نَقِيَّةٌ واحِدَة</b> (‏2026-10-24، تَيّارُ S15 البَند B؛ RELEASE-READINESS §١ البَند ٦).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (تَوصيفُها <c>DisplayTimeZoneTests</c>): الإشعاراتُ تُرسَمُ بِساعَةِ UTC المُخَزَّنَةِ (ثَلاثُ ساعاتٍ خَلفَ
/// الرياض)، والمُحادَثاتُ بِـ<c>ToLocalTime()</c> أَي بِمِنطَقَةِ <b>الخادِم</b> (UTC في الحاوِيَة) — ونَحوُ خَمسينَ راسِماً لِلوَقتِ في القَوالِب.
/// والاصطِلاحُ كانَ هُنا بِلا مُستَهلِكِ عَرض: <see cref="ICultureContext.TimeZone"/> = <see cref="DefaultTimeZone"/>. فَلا عُدَّةَ جَديدَة
/// (القاعِدَة ٨) — الدالَّةُ في العُدَّةِ الَّتي تَحمِلُ المِنطَقَة.</para>
///
/// <para><b>و<c>DateTimeKind</c> يُعامَلُ صَراحَةً</b> — مَقيساً في <c>LiveDisplayTimeKindProofTests</c>: Marten يَكتُبُ <c>Utc</c> بِـ<c>Z</c>
/// فَيَعودُ <c>Utc</c>، و<c>Unspecified</c> بِلا لاحِقَةٍ فَيَعودُ <c>Unspecified</c>. والكُتّابُ كُلُّهُم <c>DateTime.UtcNow</c>، فَـ<c>Unspecified</c>
/// <b>يُقرَأُ UTC</b> لا مِنطَقَةَ الخادِم (وهي ما كانَ <c>ToLocalTime</c> يَفعَلُه بِـ<c>Local</c>)؛ و<c>Local</c> يُرَدُّ إلى UTC أَوَّلاً.</para>
///
/// <para><b>ومِنطَقَةٌ لا تُعرَف</b> (رَأسُ <c>X-Timezone</c> مَكتوبٌ خَطَأً) تَسقُطُ إلى <see cref="DefaultTimeZone"/> لا إلى UTC؛ فَإن لَم
/// تُحَلَّ هذه أَيضاً (حاوِيَةٌ بِلا tzdata) فَإلى UTC — وهو ما كانَ يُعرَضُ قَبلَها، لا أَسوَأ. وصَفحَةٌ لا تَسقُطُ بِوَقتٍ.</para>
/// </summary>
public static class CultureTime
{
    /// <summary>مِنطَقَةُ المَنَصَّةِ الافتِراضِيَّة — مِن <see cref="MutableCultureContext"/> مُنذُ نُقِلَ المَفهوم، لا رَقَمٌ جَديد.
    /// ومِنطَقَةٌ لِكُلِّ مَتجَرٍ قَرارُ مالِك («ب» في سِجِلِّه).</summary>
    public const string DefaultTimeZone = "Asia/Riyadh";

    /// <summary>لَحظَةٌ مُخَزَّنَةٌ ← ساعَتُها في <paramref name="timeZoneId"/> (أَو <see cref="DefaultTimeZone"/>).</summary>
    public static DateTime InZone(DateTime stored, string? timeZoneId = null)
    {
        var utc = stored.Kind switch
        {
            DateTimeKind.Utc   => stored,
            DateTimeKind.Local => stored.ToUniversalTime(),
            _                  => DateTime.SpecifyKind(stored, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone(timeZoneId));
    }

    /// <summary>المِنطَقَةُ بِمُعَرِّفِها، وإلّا الافتِراضِيَّة، وإلّا UTC — لا يَرمي.</summary>
    public static TimeZoneInfo Zone(string? timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, DefaultTimeZone })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}

public sealed class MutableCultureContext : ICultureContext
{
    public string Language { get; set; } = "ar";
    public string TimeZone { get; set; } = CultureTime.DefaultTimeZone;
    public string Currency { get; set; } = "SAR";
    public string Numerals { get; set; } = "arabic";
    public DateTime NowInZone => CultureTime.InZone(DateTime.UtcNow, TimeZone);
    public DateTime InZone(DateTime storedUtc) => CultureTime.InZone(storedUtc, TimeZone);
    /// <summary>تُفَوِّض إلى <see cref="Money.Format"/>. وكانَت قَبلَ
    /// ذلك <c>N2</c> ورَمزاً لاحِقاً — أَي **تُخالِف كُلّ شاشَة** تَعرِض
    /// <c>N0</c>، ولَم يُكتَشَف لِأَنَّها بِصِفر مُستَهلِك. مُنَسِّقٌ
    /// واحِد لِلمال لا اثنان.</summary>
    public string FormatMoney(decimal amount)
        => Money.Format(amount, Currency, Language);
}

/// <summary>middleware يَملَأ <see cref="ICultureContext"/> مِن headers
/// الطَلَب. يُحقَن كَ Scoped لِيَكون فَريداً لِكُلّ طَلَب.</summary>
public sealed class CultureMiddleware
{
    private readonly RequestDelegate _next;
    public CultureMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, ICultureContext culture)
    {
        if (culture is MutableCultureContext m)
        {
            var lang = ctx.Request.Headers["Accept-Language"].ToString();
            if (lang.StartsWith("en")) m.Language = "en";
            if (lang.StartsWith("ar")) m.Language = "ar";
            var tz = ctx.Request.Headers["X-Timezone"].ToString();
            if (!string.IsNullOrEmpty(tz)) m.TimeZone = tz;
            var cur = ctx.Request.Headers["X-Currency"].ToString();
            if (!string.IsNullOrEmpty(cur)) m.Currency = cur;
        }
        await _next(ctx);
    }
}

public static class CultureExtensions
{
    public static IServiceCollection AddCultureContext(this IServiceCollection services)
    {
        services.AddScoped<ICultureContext, MutableCultureContext>();
        return services;
    }
    public static IApplicationBuilder UseCultureContext(this IApplicationBuilder app)
        => app.UseMiddleware<CultureMiddleware>();
}
