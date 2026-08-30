using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Kit.Files;

/// <summary>
/// <para><b>السُقوطُ الآمِنُ لِرَوابِطِ <c>/uploads/</c> الَّتي لا مِلَفَّ
/// خَلفَها</b> — تُرَدُّ بِـ<b>‏200 وصورَةٍ مُعَرَّفَة</b> لا بِـ404.</para>
///
/// <para><b>ولِماذا يُهِمُّ الفَرقُ</b>: المُتَصَفِّحُ يَرسُم أَيقونَةَ
/// الكَسرِ عِندَ ‏404، ويَرسُم ما نُعطيه عِندَ ‏200. وذلك بِعَينِه فَرقُ
/// «فَراغٍ يُفهَم» عَن «صورَةٍ مَكسورَة» الَّذي كَتَبَ ‏ADR-017.</para>
///
/// <para><b>وهذا الحاجِزُ الثاني لا الأَوَّل</b>: الأَوَّلُ هُوَ الرَفضُ
/// عِندَ الكِتابَة (<see cref="UnavailableFileStorage"/>) — فَلا رابِطَ
/// مُعَلَّقٌ جَديدٌ يُكتَب أَصلاً. وهذا لِما كُتِبَ **قَبلَ** ذلك.</para>
///
/// <para><b>ومُستَهلِكُه مَقيسٌ لا مُتَوَقَّع</b> (القاعِدَة ١): مَسحُ
/// قاعِدَةِ الإنتاجِ يَومَ ‏2026-08-30 أَعطى **صِفرَ رابِطِ
/// <c>/uploads/</c> في ‏35 جَدوَلَ وَثائِق** — <b>لكِنَّ المَسارَ حَيٌّ
/// لا نَظَريّ</b>: التَطويرُ والإنتاجُ يَتَشارَكانِ القاعِدَةَ نَفسَها
/// (‏<c>docs/DEPLOY.md</c> §٢-٤)، فَرَفعُ صورَةٍ مِن جِهازِ تَطويرٍ
/// يَكتُب رابِطاً مَحَلِّيّاً **تَقرَؤُه النُسخَةُ المَنشورَة** ولا
/// مِلَفَّ خَلفَه هُناك.</para>
///
/// <para><b>ووَسيطٌ لا نُقطَةٌ مُعَلَّمَة</b>: هذا سُلوكُ أَنبوبٍ لِبادِئَةٍ
/// كامِلَة، لا مَورِدٌ ذو عَقد. وعَدّادُ نَزيفِ أَجسامِ النِقاط
/// (<c>EndpointBodyBleedTests</c>) يَعُدُّ ما بَينَ <c>Map…(</c> —
/// فَكِتابَتُه نُقطَةً كانَت سَتَرفَع سَقفاً بِلا مَنطِقٍ يُبَرِّرُه.</para>
/// </summary>
public static class MissingFilePlaceholderExtensions
{
    /// <summary>البادِئَةُ الافتِراضِيَّة — نَفسُ
    /// <see cref="LocalFileStorageOptions.PublicPathPrefix"/>.</summary>
    public const string DefaultPrefix = "/uploads";

    /// <summary>صورَةُ العُذر — ‏SVG صَغيرَةٌ بِلا اعتِمادٍ على أَصلٍ
    /// خارِجيّ، فَتَعمَل قَبلَ أَيِّ مَخزَن.</summary>
    public const string PlaceholderSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 240 180" width="240" height="180" role="img" aria-label="الصورة غير متاحة"><rect width="240" height="180" fill="#eceff3"/><circle cx="92" cy="70" r="12" fill="#c3cbd6"/><path d="M60 132l40-46 26 30 20-22 34 38z" fill="#c3cbd6"/></svg>
        """;

    /// <summary>يُرَكَّب حينَ <b>لا</b> يَخدُم المُزَوِّدُ المَحَلِّيُّ
    /// المِلَفّاتِ مِن نَفسِ المُضيف.</summary>
    public static IApplicationBuilder UseMissingFilePlaceholder(
        this IApplicationBuilder app, string prefix = DefaultPrefix)
    {
        var normalized = "/" + prefix.Trim('/') + "/";
        return app.Use(async (ctx, next) =>
        {
            if (!ctx.Request.Path.StartsWithSegments(normalized.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                await next(ctx);
                return;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "image/svg+xml; charset=utf-8";
            // لا تُخَزَّن طَويلاً: يَومَ يوجَد مَخزَنٌ دائِمٌ ويُعادُ رَفعُ
            // الصورَةِ، لا تَبقى صورَةُ العُذرِ في كاشِ الزائِرِ شَهراً.
            ctx.Response.Headers.CacheControl = "public, max-age=300";
            await ctx.Response.WriteAsync(PlaceholderSvg, ctx.RequestAborted);
        });
    }
}
