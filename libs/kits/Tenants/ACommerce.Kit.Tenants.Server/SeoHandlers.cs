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
        // ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · C2b-P3-01 وC2b-P-05 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) عَلى مُضيفِ الدَورِ لا زَحفَ ولا خَريطَة: كانَ يُسَمّي
        // خَريطَتَه فَيُعلِنُ نُسخَةً ثانِيَةً مِن واجِهَةِ المَتجَر. ومُضيفُ المَتجَرِ كَما هُوَ.
        var body = context.Items[StoreHostItems.Role] is string
            ? SeoDocuments.RoleHostRobotsTxt
            : SeoDocuments.BuildRobotsTxt(BaseUrlOf(context));
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

        // ‏(‏2026-11-07، ADR-050 §٩ · W13-4·C2a·6 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) عَلى مُضيفِ المَتجَرِ خَريطَتُه وَحدَه بِعَناوينِه النَظيفَة: كانَت هذِه
        // الخَريطَةُ نَفسُها تُخدَمُ عَلى كُلِّ مُضيفٍ فَتَسرُدُ كُلَّ مَتجَرٍ بِمَسارِه تَحتَ مُضيفِ غَيرِه. والمَتجَرُ مِن الحاقِن (`StoreHostItems.Slug`)، وبِلا نِطاقٍ
        // مُهَيَّإٍ لا يُكتَبُ أَبَداً فَلا يَتَغَيَّرُ هذا الفَرع.
        // ‏(‏2026-11-11، C2b-P3-01 — يُلحَقُ) ومُضيفُ الدَورِ: خَريطَةٌ فارِغَة — كانَت تَسرُدُ مَداخِلَ المَتجَرِ تَحتَ اسمِ الدَور.
        if (context.Items[StoreHostItems.Role] is string)
        {
            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(SeoDocuments.BuildSitemapXml([]), context.RequestAborted);
            return;
        }

        if (context.Items[StoreHostItems.Slug] is string hostSlug)
        {
            IReadOnlyList<Tenant> own = await session.LoadAsync<Tenant>(hostSlug, context.RequestAborted) is { } t ? [t] : [];
            if (own.Count > 0 && context.RequestServices.GetService(typeof(ITenantShowcase)) is ITenantShowcase hostShowcase)
                own = await hostShowcase.ShowcaseAsync(own, context.RequestAborted);
            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(
                SeoDocuments.BuildSitemapXml(SeoDocuments.StoreHostEntries(own.FirstOrDefault(), BaseUrlOf(context))), context.RequestAborted);
            return;
        }

        IReadOnlyList<Tenant> tenants = await session.Query<Tenant>()
            .Take(5000).ToListAsync(context.RequestAborted);
        // ‏(‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) واجِهَةُ المَنَصَّةِ لا كُلُّ مُستَأجِر: كانَ كُلُّ مَتجَرٍ يُنشِئُه
        // حِسابٌ بِالاستِمارَةِ — بِصِفرِ إعلانٍ وبِاسمٍ يَختارُه — يَدخُلُ الخَريطَةَ لِلزاحِف (مَتجَرانِ ⇒ ثَمانِيَةُ مُدخَلات). والمُسنَدُ لِلقالِب (`ITenantShowcase`).
        if (context.RequestServices.GetService(typeof(ITenantShowcase)) is ITenantShowcase showcase)
            tenants = await showcase.ShowcaseAsync(tenants, context.RequestAborted);

        var entries = SeoDocuments.TenantEntries(tenants, BaseUrlOf(context));
        var body = SeoDocuments.BuildSitemapXml(entries);

        context.Response.ContentType = "application/xml; charset=utf-8";
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    private static string BaseUrlOf(HttpContext context)
        => $"{context.Request.Scheme}://{context.Request.Host}";
}
