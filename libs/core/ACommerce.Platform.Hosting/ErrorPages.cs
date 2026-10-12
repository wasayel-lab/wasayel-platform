using System.Net;
using ACommerce.Platform.I18n;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ACommerce.Platform.Hosting;

/// <summary>صِنفُ الصَفحَة — مَجموعَةٌ مُغلَقَة: كُلُّ رَمزٍ بَينَ ‏400 و‏599 يَقَعُ في واحِدٍ مِنها
/// (<see cref="ErrorPages.KindFor"/>)، ولِكُلٍّ عُنوانٌ وتَلميحٌ في القامُوس.</summary>
public enum ErrorPageKind
{
    NotFound,
    Request,
    Server,
}

/// <summary>
/// <para><b>صَفحَةُ خَطَإٍ واحِدَةٌ بِنُصوصِ القامُوسِ وهُوِيَّةِ البِناء</b> — بَدَلَ صَفحَةٍ بَيضاءَ بِلا بايتٍ واحِد
/// (‏2026-10-12، موجزُ الفجوات P3-4، و‏PRODUCTION-PLAN «المتطلبات الشاملة» ٥: «مُعالَجَةُ أَخطاءٍ موحَّدَة + صَفَحاتُ
/// خَطَإ»).</para>
///
/// <para><b>المَقيسُ قَبل</b> — إقلاعٌ بِوَضعِ Production عَلى قاعِدَةٍ مَحَلِّيَّة، ثُمَّ القاعِدَةُ تُغلَقُ تَحتَه
/// (‏<c>allow_connections false</c> و<c>pg_terminate_backend</c>): مَسارٌ لا صَفحَةَ لَه ⇒ ‏404 بِـ‏0 بايت؛ و<c>/</c>
/// و<c>/ejar</c> ⇒ ‏500 بِـ‏0 بايت؛ و<c>/health</c> ⇒ ‏200. أَي أَنّ الزائِرَ يَرى صَفحَةً بَيضاء، ومَن يُشَخِّصُ لا يَعرِفُ
/// أَيَّ بِناءٍ رَدَّها. و<c>grep UseExceptionHandler|UseStatusCodePages</c> في <c>libs/</c> و<c>apps/</c> = صِفر.</para>
///
/// <para><b>ولِماذا تُكتَبُ الصَفحَةُ هُنا ولا تُعادُ إلى صَفحَةِ Razor</b> (<c>UseStatusCodePagesWithReExecute</c>) —
/// <b>قيسَ قَبلَ الاختِيار</b>: لا <c>Error.razor</c> في المُستَودَع، وكُلُّ صَفحَةٍ تُصَيَّرُ داخِلَ <c>App.razor</c>،
/// و<c>App.razor</c> يَحقِنُ <c>TenantThemeService</c> ويَنتَظِرُ <c>Themes.EffectiveAsync(Tenant.Slug)</c> — <b>أَي
/// القاعِدَة</b>. فَإعادَةُ التَنفيذِ في أَكثَرِ الحالاتِ حاجَةً إلَيها (القاعِدَةُ ساقِطَة) <b>تَسقُطُ هي أَيضاً</b>
/// فَيَعودُ ‏500 الأَبيَض — خَطَأٌ في مُعالِجِ الخَطَإ. والكِتابَةُ مِن هُنا لا تَمَسُّ إلّا القامُوسَ (مَوارِدُ مُضَمَّنَةٌ
/// تُقرَأُ مِن التَجميعَة) وسِلسِلَةً حُسِبَت عِندَ الإقلاع.</para>
///
/// <para><b>والحَدُّ المَحروسُ ثَلاثَة</b>:</para>
/// <list type="number">
///   <item><b>لا أَثَرَ لِلاستِثناءِ في الصَفحَة خارِجَ Development — بِالبِنيَةِ لا بِالحِرص</b>: <see cref="Render"/> لا
///   تَأخُذُ الاستِثناءَ أَصلاً (اختِبارٌ يَفحَصُ ذلك بِالانعِكاس)، فَلا رِسالَةَ ولا سَطرَ مُكَدَّسٍ يَستَطيعُ الوُصولَ إلَيها.
///   <b>وفي Development لا يُرَكَّبُ شَيء</b>: صَفحَةُ المُطَوِّرِ الَّتي يُضيفُها <c>WebApplication</c> تَبقى كَما هي.</item>
///   <item><b>ما يَطلُبُه غَيرُ المُتَصَفِّحِ لا يَتَغَيَّرُ بايتاً</b>: الصَفحَةُ لِطَلَبٍ يُعلِنُ <c>text/html</c> في
///   <c>Accept</c> وَحدَه — أَي تَنَقُّلُ المُتَصَفِّحِ ونَماذِجُه. و<c>fetch()</c> (‏<c>*/*</c>) وعُمَلاءُ الـAPI و<c>curl</c>
///   وأَدَواتُ المُراقَبَةِ تَأخُذُ ما كانَت تَأخُذُ: الرَمزَ بِلا جِسم. ورَدٌّ يَحمِلُ جِسمَه (‏ProblemDetails، ‏426 بَوّابَةِ
///   الإصدار) لا يُمَسّ — ذاكَ شَرطُ <c>UseStatusCodePages</c> نَفسِه.</item>
///   <item><b>البِناءُ في الصَفحَةِ هُوَ البِناءُ في <c>/health</c></b>: نَفسُ السِمَةِ ونَفسُ <see cref="BuildIdentity.CommitFrom"/>
///   ونَفسُ <c>"unknown"</c> — فَلا يُكشَفُ شَيءٌ لَم يَكشِفه <c>/health</c> (‏ADR-019 §٦)، ولَقطَةُ شاشَةٍ مِن مُستَخدِمٍ
///   تَقولُ أَيَّ إيداعٍ رَدَّها.</item>
/// </list>
///
/// <para><b>ولِماذا هُنا لا في <c>Program.cs</c></b> — العِلَّةُ نَفسُها في <see cref="BuildIdentity"/>: مَشروعُ الاختِباراتِ
/// يُحيلُ إلى هذا المَشروعِ ولا يُحيلُ إلى <c>V1.App</c>، فَالمَكتوبُ هُنا يُقاسُ عَلى HTTP حَقيقيّ. <b>وشَرطُ الاستِخراجِ</b>
/// (القاعِدَة ١): مُستَهلِكٌ تَشغيليٌّ واحِد (<c>apps/V1.App</c>)، ولا يُنقَلُ إلى حُزمَةٍ قَبلَ ثَلاثَة.</para>
/// </summary>
public static class ErrorPages
{
    /// <summary>كُلُّ مِفتاحٍ تَقرَؤُه الصَفحَة — واختِبارٌ يَشتَرِطُها كُلَّها في <c>ar.json</c> بِقيمَةٍ لا نائِب.</summary>
    public static IReadOnlyList<string> Keys { get; } = new[]
    {
        "shell.error.not_found",
        "shell.error.not_found_hint",
        "shell.error.request_title",
        "shell.error.request_hint",
        "shell.error.server_title",
        "shell.error.server_hint",
        "shell.error.home",
        "shell.error.status",
        "shell.error.build",
    };

    /// <summary>‏404 وَحدَه «غَيرُ مَوجود»، وكُلُّ ‏5xx «مِن جِهَتِنا»، وما بَقِيَ مِن ‏4xx «طَلَبٌ لا يُقبَل» — لِأَنّ
    /// قَولَ «صَفحَةٌ غَيرُ مَوجودَة» لِـ‏405 أَو ‏401 كَذِب.</summary>
    public static ErrorPageKind KindFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => ErrorPageKind.NotFound,
        >= 500 => ErrorPageKind.Server,
        _ => ErrorPageKind.Request,
    };

    /// <summary>هَل يُعلِنُ الطَلَبُ أَنَّه يَقرَأُ HTML؟ — التَنَقُّلُ في المُتَصَفِّحِ يُرسِلُ <c>text/html</c> صَراحَةً،
    /// و<c>fetch()</c> و<c>curl</c> يُرسِلانِ <c>*/*</c> فَلا يُعَدّانِ قارِئَينِ لَها.</summary>
    public static bool WantsHtml(HttpRequest request)
    {
        foreach (var value in request.Headers.Accept)
            if (value is not null && value.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>لُغَةُ الصَفحَة: لُغَةُ الطَلَبِ إن كانَت مَفاتيحُ الصَفحَةِ <b>كُلُّها</b> فيها، وإلّا العَرَبِيَّة. <b>والعِلَّة</b>:
    /// السُقوطُ إلى العَرَبِيَّةِ مِفتاحاً مِفتاحاً يَكتُبُ نَصّاً عَرَبِيّاً في صَفحَةٍ <c>dir="ltr"</c> — وهُوَ عَطَبُ زِرِّ اللُغَةِ
    /// الَّذي تُسَمّيهِ القاعِدَة ١٢. فَالاتِّجاهُ يَتبَعُ النَصَّ المَكتوبَ لا الكوكي.</summary>
    public static string LanguageFor(string? requested) =>
        requested is not null
        && requested != LocaleCatalog.Arabic
        && Keys.All(k => LocaleCatalog.Find(requested, k) is not null)
            ? requested
            : LocaleCatalog.Arabic;

    /// <summary>
    /// <para>الصَفحَةُ كامِلَةً — دالَّةٌ نَقِيَّةٌ في رَمزِ الحالَةِ والبِناءِ واللُغَة، <b>ولا شَيءَ غَيرَها</b>: لا استِثناءَ ولا
    /// طَلَبَ ولا خِدمَة. ولا مَوارِدَ خارِجِيَّة: لا ورَقَةَ أَنماطٍ ولا خَطَّ ولا صورَة — صَفحَةٌ تُكتَبُ حينَ يَكونُ غَيرُها
    /// مَكسوراً لا تَستَعيرُ مِنه شَيئاً. والتَخطيطُ بِخَصائِصَ مَنطِقِيَّةٍ وَحدَها (القاعِدَة ١١)، فَالاتِّجاهُ سِمَةٌ لا قاعِدَة.</para>
    /// </summary>
    public static string Render(int status, string commit, string lang)
    {
        var kind = KindFor(status);
        var (titleKey, hintKey) = kind switch
        {
            ErrorPageKind.NotFound => ("shell.error.not_found", "shell.error.not_found_hint"),
            ErrorPageKind.Server => ("shell.error.server_title", "shell.error.server_hint"),
            _ => ("shell.error.request_title", "shell.error.request_hint"),
        };

        string T(string key) => WebUtility.HtmlEncode(LocaleCatalog.Text(lang, key));
        var dir = lang == LocaleCatalog.Arabic ? "rtl" : "ltr";

        return $$$"""
            <!DOCTYPE html>
            <html lang="{{{WebUtility.HtmlEncode(lang)}}}" dir="{{{dir}}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>{{{T(titleKey)}}}</title>
            <style>
            :root{color-scheme:light dark;--bg:#f6f6f7;--card:#fff;--fg:#1c1c1e;--muted:#5c5c66;--line:#e2e2e6;--link:#0b57d0}
            @media (prefers-color-scheme:dark){:root{--bg:#111113;--card:#1b1b1f;--fg:#ececf1;--muted:#a3a3ad;--line:#2d2d33;--link:#8ab4f8}}
            *{box-sizing:border-box}
            body{margin:0;min-block-size:100vh;display:grid;place-items:center;padding:16px;background:var(--bg);color:var(--fg);font-family:system-ui,-apple-system,"Segoe UI",Tahoma,sans-serif;line-height:1.7}
            main{inline-size:100%;max-inline-size:32rem;background:var(--card);border:1px solid var(--line);border-radius:12px;padding-block:28px;padding-inline:24px;text-align:start}
            h1{margin-block:0 8px;font-size:1.25rem}
            p{margin-block:0 16px;color:var(--muted)}
            a{color:var(--link)}
            footer{padding-block-start:12px;border-block-start:1px solid var(--line);font-size:.8125rem;color:var(--muted)}
            code{font-size:.75rem;overflow-wrap:anywhere}
            </style>
            </head>
            <body>
            <main>
            <h1>{{{T(titleKey)}}}</h1>
            <p>{{{T(hintKey)}}}</p>
            <p><a href="/">{{{T("shell.error.home")}}}</a></p>
            <footer>{{{T("shell.error.status")}}} {{{status}}} · {{{T("shell.error.build")}}} <code dir="ltr">{{{WebUtility.HtmlEncode(commit)}}}</code></footer>
            </main>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// <para>يُرَكِّبُ المُعالِجَينِ — <c>UseExceptionHandler</c> لِلاستِثناءِ و<c>UseStatusCodePages</c> لِرَمزٍ بِلا جِسم —
    /// <b>خارِجَ Development وَحدَه</b>. ويُنادى <b>بَعدَ <c>UseForwardedHeaders</c> وقَبلَ <c>UsePlatformHost</c></b>، فَيَلُفُّ
    /// الأُنبوبَ كُلَّه: حَلَّ المُستَأجِرِ والنِقاطَ ومُصَيِّرَ Razor.</para>
    ///
    /// <para><paramref name="informationalVersion"/> هي السِلسِلَةُ نَفسُها الَّتي تُمَرَّرُ إلى
    /// <see cref="BuildIdentity.MapBuildIdentity"/> — ولِذلك تُقرَأُ في <c>Program.cs</c> مَرَّةً وتُمَرَّرُ إلى الاثنَين.</para>
    /// </summary>
    public static WebApplication UsePlatformErrorPages(this WebApplication app, string? informationalVersion)
    {
        if (app.Environment.IsDevelopment())
            return app;

        var commit = BuildIdentity.CommitFrom(informationalVersion) ?? "unknown";

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = ctx => WriteAsync(ctx, commit),
        });
        app.UseStatusCodePages(statusContext => WriteAsync(statusContext.HttpContext, commit));
        return app;
    }

    /// <summary>الرَمزُ قائِمٌ قَبلَ النِداء: ‏500 يَضبُطُه مُعالِجُ الاستِثناءِ قَبلَ أَن يَدعُوَنا، والرَمزُ بِلا جِسمٍ هُوَ مَن
    /// دَعانا. ولُغَةُ الطَلَبِ مِن <see cref="L"/> إن سُجِّلَ — <b>نَفسُ الكوكي ونَفسُ القاعِدَة</b> — وإلّا العَرَبِيَّة.</summary>
    private static Task WriteAsync(HttpContext ctx, string commit)
    {
        if (!WantsHtml(ctx.Request))
            return Task.CompletedTask;

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = BuildIdentity.NoStore;
        ctx.Response.Headers.Pragma = "no-cache";
        if (HttpMethods.IsHead(ctx.Request.Method))
            return Task.CompletedTask;

        var lang = LanguageFor(ctx.RequestServices.GetService<L>()?.Lang);
        return ctx.Response.WriteAsync(Render(ctx.Response.StatusCode, commit, lang));
    }
}
