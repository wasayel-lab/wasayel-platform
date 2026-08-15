using Marten;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace ACommerce.Kit.Tenants.Server;

/// <summary>
/// نُقطَتا الزَحف عَلى جَذر المَنصَّة: <c>/robots.txt</c> و
/// <c>/sitemap.xml</c>. كُلّ المَنطِق النَصّيّ في
/// <see cref="SeoDocuments"/> (نَقِيّ وَمُختَبَر بِلا قاعِدَة بَيانات)؛
/// هذِه الطَبَقَة تَجلِب المُستَأجِرين وَتَكتُب الاستِجابَة فَقَط.
///
/// <para>الرابِط الأَساسيّ يُشتَقّ مِن الطَلَب نَفسِه (<c>Scheme</c> +
/// <c>Host</c>) بَعد <c>UseForwardedHeaders</c> — فَيَصِحّ خَلف أَيّ proxy
/// بِلا إعداد إضافيّ.</para>
///
/// <para>الوَثيقَتان تُكتَبان مُباشَرَةً في <c>HttpContext.Response</c>
/// (بِلا قيمَة راجِعَة) لِأَنّ كِلتَيهِما نَصّ خام بِنَوع مُحتَوى
/// مَخصوص — لا JSON.</para>
/// </summary>
public static class SeoHandlers
{
    /// <summary><c>[NotTenanted]</c> استِثناءٌ مُعلَن: التَركيب يَفرِض
    /// وُجود مُستَأجِر عَلى كُلّ نُقطَة Wolverine
    /// (<c>opts.TenantId.AssertExists()</c>)، وهذِه وَثيقَةُ زَحفٍ عَلى
    /// جَذر المَنصَّة لا تَحمِل <c>slug</c> بِطَبيعَتِها. مُثَبَّتَة
    /// بِاسمِها في <c>WolverineTenancyContractTests</c>.</summary>
    [NotTenanted]
    [WolverineGet("/robots.txt")]
    public static async Task Robots(HttpContext context)
    {
        var body = SeoDocuments.BuildRobotsTxt(BaseUrlOf(context));
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    /// <summary><c>[NotTenanted]</c> — نَفس عِلَّة <see cref="Robots"/>،
    /// وأَشَدّ: خَريطَةُ المَوقِع تَستَعرِض **كُلّ** المُستَأجِرين
    /// فَجَلسَتُها بِلا سلاج هي الصَحيحَة (‏وَثيقَة <c>Tenant</c>
    /// مُسَجَّلَة <c>SingleTenanted</c>).</summary>
    [NotTenanted]
    [WolverineGet("/sitemap.xml")]
    public static async Task Sitemap(HttpContext context, IDocumentStore store)
    {
        // وَثيقَة Tenant مُسَجَّلَة SingleTenanted (عامَّة) — جَلسَة بِلا
        // مُستَأجِر هي الصَحيحَة هُنا، كَما في TenantResolverMiddleware.
        await using var session = store.QuerySession();
        var tenants = await session.Query<Tenant>()
            .Take(5000).ToListAsync(context.RequestAborted);

        var entries = SeoDocuments.TenantEntries(tenants, BaseUrlOf(context));
        var body = SeoDocuments.BuildSitemapXml(entries);

        context.Response.ContentType = "application/xml; charset=utf-8";
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static string BaseUrlOf(HttpContext context)
        => $"{context.Request.Scheme}://{context.Request.Host}";
}
