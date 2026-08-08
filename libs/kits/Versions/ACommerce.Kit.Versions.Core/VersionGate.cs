using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Versions;

/// <summary>
/// إعدادات بَوّابَة الإصدار. تُقَرِّر أَدنى إصدار مَدعوم وأَدنى مُحَدَّث
/// مُقتَرَح. التَّطبيق يَبعَث رَأس <c>X-App-Version: 2.5.1</c> وَ
/// <c>X-App-Platform: ios|android|web</c>.
/// </summary>
public sealed class VersionGateOptions
{
    /// <summary>أَدنى إصدار مَدعوم. أَقَلّ مِنه = رِسالَة فَرض تَحديث.</summary>
    public string MinimumSupported { get; set; } = "1.0.0";
    /// <summary>أَدنى إصدار مُحَدَّث. أَقَلّ مِنه = banner اِختياريّ.</summary>
    public string LatestSuggested { get; set; } = "1.0.0";
    public string UpdateMessageAr { get; set; } = "حَدِّث التَّطبيق لِأَحدَث إصدار";
}

public enum VersionStatus { Current, Outdated, Unsupported, Unknown }

public sealed record VersionCheckResult(VersionStatus Status, string Message, string? RequiredVersion);

/// <summary>middleware لِفَحص <c>X-App-Version</c>. لَو لا يوجد header
/// (مُتَصَفِّح عادِيّ) يَتَجاوَز. لَو أَدنى مِن MinimumSupported يَرجِع 426
/// Upgrade Required.</summary>
public sealed class VersionGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly VersionGateOptions _opts;
    public VersionGateMiddleware(RequestDelegate next, IOptions<VersionGateOptions> opts)
    { _next = next; _opts = opts.Value; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var v = ctx.Request.Headers["X-App-Version"].ToString();
        if (!string.IsNullOrEmpty(v) && Compare(v, _opts.MinimumSupported) < 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            ctx.Response.Headers["X-Required-Version"] = _opts.MinimumSupported;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "version_unsupported",
                required = _opts.MinimumSupported,
                message = _opts.UpdateMessageAr
            });
            return;
        }
        await _next(ctx);
    }

    /// <summary>مُقارَنَة semver بَسيطَة (a.b.c).</summary>
    public static int Compare(string a, string b)
    {
        var av = Parse(a); var bv = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            if (av[i] != bv[i]) return av[i].CompareTo(bv[i]);
        }
        return 0;
    }
    private static int[] Parse(string v)
    {
        var parts = v.Split('.', '-')[0..3.Min(v.Split('.').Length)];
        var res = new int[3];
        for (var i = 0; i < parts.Length && i < 3; i++)
            int.TryParse(parts[i], out res[i]);
        return res;
    }
}

internal static class IntExt
{
    public static int Min(this int a, int b) => a < b ? a : b;
}

public static class VersionGateExtensions
{
    public static IServiceCollection AddVersionGate(
        this IServiceCollection services, Action<VersionGateOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    public static IApplicationBuilder UseVersionGate(this IApplicationBuilder app)
        => app.UseMiddleware<VersionGateMiddleware>();
}
