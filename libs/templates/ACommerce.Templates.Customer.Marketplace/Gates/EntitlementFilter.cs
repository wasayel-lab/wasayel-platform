using ACommerce.Kit.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>الطَبَقَة الرابِعَة: استِحقاق باقَة.</b> تَسأَل
/// <see cref="IEntitlements.PeekAsync"/> وتَرُدّ مُبَكِّراً عِندَ
/// النَفاد. يَفتَرِض <see cref="AuthFilter"/> سَبَقَه.</para>
///
/// <para><b>ولِماذا مُرَشِّح وسَطرٌ في الجِسم مَعاً، لا أَحَدُهُما</b>:
/// المُرَشِّح <b>يُعلِن</b> ويَرُدّ قَبل أَن يُنفَق عَمَل (مَسار إنشاء
/// الإعلان يَرفَع الصُوَر <b>قَبل</b> فَتح الجَلسَة — فَبِلا رَدٍّ
/// مُبَكِّر تُرفَع سِتّ صُوَر لِإعلانٍ سَيُرَدّ). لكِنَّه <b>لا يَستَهلِك</b>
/// لِأَنَّه لا جَلسَةَ لَدَيه، ولَو استَهلَكَ لَفَتَحَ جَلسَةً ثانِيَة
/// وخَسِرَ الذَرِّيَّة. والحَسم يَقَع في <c>ConsumeAsync</c> داخِل جَلسَة
/// العَمَلِيَّة. الفَحص الآليّ يَربِط الطَرَفَين فَلا يَفتَرِقان.</para>
///
/// <para><b>وجَواب المَنع رِسالَة لا رَقَم</b>: إعادَة تَوجيه إلى
/// الصَفحَة بِـ<c>err</c> تَقرَؤُها فَتَرسِم نَصّاً عَرَبِيّاً مِن
/// قامُوس المَفاتيح. و<c>Results.Forbid()</c> <b>ممنوع هُنا</b>: يَطلُب
/// <c>IAuthenticationService</c> غَير المُسَجَّل في المِنَصَّة فَيَرمي
/// ‏500 بَدَل الرَفض — العَطَب المُثَبَّت في
/// <c>ForbidResultTests</c>.</para>
/// </summary>
public sealed class EntitlementFilter : IEndpointFilter
{
    private readonly string  _capability;
    private readonly string  _redirectPath;
    private readonly string  _errCode;

    /// <param name="capability">مِن <see cref="CapabilityCatalog"/>
    /// <b>حَصراً</b> — يُفحَص عِندَ التَركيب لا عِندَ الطَلَب.</param>
    /// <param name="redirectPath">مَسار الصَفحَة الَّتي تَرسِم الرِسالَة،
    /// نِسبِيّاً لِلمُستَأجِر (مَثَلاً <c>create-listing</c>).</param>
    /// <param name="errCode">قيمَة <c>?err=</c> الَّتي تَقرَؤُها الصَفحَة.</param>
    public EntitlementFilter(string capability, string redirectPath, string errCode)
    {
        // البَوّابَة عِندَ التَركيب: رَمز مَجهول يُفشِل الإقلاع
        // بِرِسالَتِه، ولا يَمُرّ صامِتاً لِيُكتَشَف مِن سِجِلّ لَيلاً.
        _capability   = CapabilityCatalog.Require(capability);
        _redirectPath = redirectPath;
        _errCode      = errCode;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http   = ctx.HttpContext;
        var userId = http.UserIdOrNull();
        if (userId is null) return Results.Unauthorized();

        var slug = http.Slug();
        var ents = http.RequestServices.GetRequiredService<IEntitlements>();

        var peek = await ents.PeekAsync(slug, userId.Value, _capability, http.RequestAborted);
        if (!peek.Allowed)
            return Results.Redirect(AuthSession.LinkFor(
                slug, http.Role(), $"{_redirectPath}?err={_errCode}"));

        return await next(ctx);
    }
}
