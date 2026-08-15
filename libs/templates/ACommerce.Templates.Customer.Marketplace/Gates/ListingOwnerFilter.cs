using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>الطَبَقَة الخامِسَة: مِلكِيَّةُ المَورِد.</b> الحُرّاسُ
/// الأَربَعَة القائِمونَ يَسأَلونَ عَن <b>الفاعِل</b> — أَمُسَجَّل؟
/// أَقَبِلَ الشُروط؟ أَيَملِك دَورُه الصَلاحِيَّة؟ أَبَقِيَ في
/// باقَتِه رَصيد؟ — ولا واحِدَ مِنها يَسأَل عَن <b>المَفعولِ بِه</b>.
/// و«مُسَجَّلٌ يَملِك <c>listing.create</c>» يَعني أَنَّه يَملِك
/// إنشاءَ إعلانٍ لِنَفسِه، لا تَحريرَ إعلانِ غَيرِه.</para>
///
/// <para><b>ولِماذا مُرَشِّح وسَطرٌ في الخِدمَة مَعاً</b> — نَفسُ عِلَّة
/// <see cref="EntitlementFilter"/> حَرفاً: المُرَشِّحُ <b>يُعلِن</b>
/// الحارِسَ في التَوقيع (القاعِدَة ٦) ويَرُدّ قَبلَ أَن يُفتَح شَيء؛
/// والخِدمَةُ تَحكُم <b>داخِلَ</b> المُعامَلَة على الحالَة الَّتي
/// ستُكتَب. ولَو اكتُفِيَ بِالمُرَشِّح لَحَكَمَ على لَقطَةٍ أَقدَمَ
/// مِن الكِتابَة؛ ولَو اكتُفِيَ بِالخِدمَة لَما رَآهُ فاحِصُ الحُرّاس
/// في التَوقيع. <b>والقَرارُ واحِدٌ في المَوضِعَين</b>:
/// <see cref="ListingEditService.IsOwnedBy"/>، فَلا يَنجَرِفانِ.</para>
///
/// <para><b>وجَوابُ المَنع إعادَةُ تَوجيه لا <c>Results.Forbid()</c></b>:
/// الأَخيرُ يَطلُب <c>IAuthenticationService</c> غَيرَ المُسَجَّل في
/// المَنصَّة فَيَرمي ‏500 بَدَلَ الرَفض — العَطَبُ المُثَبَّت في
/// <c>ForbidResultTests</c>. والجُمهورُ هُنا نَموذَجُ مُتَصَفِّح،
/// فَالرَدُّ ‏302 إلى «إعلاناتي» بِـ<c>err</c> تَقرَؤُها الصَفحَة.</para>
///
/// <para><b>ولا يُفَرَّق «لَيسَ لَك» عَن «غَير مَوجود»</b>: كِلاهُما
/// نَفسُ الرَمز ونَفسُ الوِجهَة. والفَرقُ بَينَهُما يُعطي غَيرَ
/// المالِك أَداةَ استِطلاع: يُجَرِّب مُعَرِّفات فَيَعرِف أَيُّها
/// إعلانٌ قائِم.</para>
/// </summary>
public sealed class ListingOwnerFilter : IEndpointFilter
{
    /// <summary>وَسيطُ المَسار الَّذي يَحمِل مُعَرِّفَ الإعلان — نَفسُ
    /// الاسم في كُلّ مَسارات <c>/{slug}/listings/{id:guid}/…</c>.</summary>
    public const string RouteArgument = "id";

    /// <summary>الوِجهَةُ عِندَ الرَفض، نِسبِيّاً لِلمُستَأجِر.</summary>
    public const string RejectPath = "me/listings";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;

        // يَفتَرِض `AuthFilter` سَبَقَه — ومَجهولٌ يُرَدّ هُناكَ إلى
        // الدُخول قَبلَ أَن يَصِلَ هُنا.
        var userId = http.UserIdOrNull();
        if (userId is null) return Results.Unauthorized();

        var slug = http.Slug();
        var reject = Results.Redirect(AuthSession.LinkFor(
            slug, http.Role(), $"{RejectPath}?err={ListingEditCodes.NotOwner}"));

        if (!Guid.TryParse(http.Request.RouteValues[RouteArgument]?.ToString(), out var listingId))
            return reject;

        var lookup = http.RequestServices.GetRequiredService<ListingLookupService>();
        var owned = await lookup.LoadOwnedAsync(slug, listingId, userId.Value, http.RequestAborted);
        if (owned is null) return reject;

        return await next(ctx);
    }
}
