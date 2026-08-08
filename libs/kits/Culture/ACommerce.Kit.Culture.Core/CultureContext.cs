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
}

public sealed class MutableCultureContext : ICultureContext
{
    public string Language { get; set; } = "ar";
    public string TimeZone { get; set; } = "Asia/Riyadh";
    public string Currency { get; set; } = "SAR";
    public string Numerals { get; set; } = "arabic";
    public DateTime NowInZone => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        TryGetTz(TimeZone));
    public string FormatMoney(decimal amount)
        => $"{amount:N2} {(Currency == "SAR" ? "ر.س" : Currency)}";

    private static TimeZoneInfo TryGetTz(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
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
