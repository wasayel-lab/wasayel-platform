using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// تَركيب الـ gates بِسَطر واحِد بَدَلاً مِن ٧+ أَسطُر مُكَرَّرَة في كُلّ
/// endpoint. الاستِعمال:
///
/// <code>
/// var protected = app.MapGroup("/{slug}").RequireAuth().RequireTerms();
/// protected.MapPost("/listings/create", …).RequirePermission("listing.create");
/// protected.MapPost("/offers/{id}/accept", …).RequirePermission("offer.accept");
/// </code>
/// </summary>
public static class GateExtensions
{
    /// <summary>تَوثيق مَطلوب — على فَشَل: redirect إلى صَفحَة الدُخول.</summary>
    public static TBuilder RequireAuth<TBuilder>(this TBuilder b)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new AuthFilter(redirectOnFailure: true));
        return b;
    }

    /// <summary>تَوثيق مَطلوب — على فَشَل: 401 (لِـ JSON APIs).</summary>
    public static TBuilder RequireAuthApi<TBuilder>(this TBuilder b)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new AuthFilter(redirectOnFailure: false));
        return b;
    }

    /// <summary>قَبول الشُروط مَطلوب. يَفتَرِض <see cref="RequireAuth{TBuilder}"/>
    /// سَبَقَه.</summary>
    public static TBuilder RequireTerms<TBuilder>(this TBuilder b)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new TermsFilter());
        return b;
    }

    /// <summary>صَلاحِيَّة دَور مَطلوبَة (مَثَلاً <c>"listing.create"</c>).
    /// يَفتَرِض <see cref="RequireAuth{TBuilder}"/> سَبَقَه.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder b, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new PermissionFilter(permission));
        return b;
    }

    /// <summary>
    /// <para><b>مِلكِيَّة الإعلان مَطلوبَة</b> — الحارِسُ الوَحيد هُنا
    /// الَّذي يَسأَل عَن <b>المَفعولِ بِه</b> لا عَن الفاعِل. يَقرَأ
    /// <c>{id}</c> مِن المَسار، ويُقارِن مالِكَ الإعلان بِصاحِبِ
    /// الجَلسَة. يَفتَرِض <see cref="RequireAuth{TBuilder}"/> سَبَقَه.</para>
    ///
    /// <para><b>ولِماذا لَم يَكفِ <c>RequirePermission("listing.create")</c></b>:
    /// الصَلاحِيَّة جَوابُها ثابِتٌ لِلدَور — «هذا الدَور يَنشُر
    /// إعلانات» — فَتَفتَح تَحريرَ <b>كُلّ</b> إعلانات المَتجَر لِكُلّ
    /// ناشِر. والمِلكِيَّة سُؤالٌ عَن صَفٍّ بِعَينِه، ولا يُجيبُه إلّا
    /// مَن يَقرَأ ذاكَ الصَفّ.</para>
    /// </summary>
    public static TBuilder RequireListingOwner<TBuilder>(this TBuilder b)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new ListingOwnerFilter());
        return b;
    }

    /// <summary>
    /// <para><b>استِحقاق باقَة مَطلوب</b> — القُدرَة مِن
    /// <c>CapabilityCatalog</c> حَصراً، ورَمزٌ خارِجَه يَرمي
    /// <b>عِندَ التَركيب</b> فَيُفشِل الإقلاع. يَفتَرِض
    /// <see cref="RequireAuth{TBuilder}"/> سَبَقَه.</para>
    ///
    /// <para><b>والفَرق عَن <see cref="RequirePermission{TBuilder}"/></b>:
    /// الصَلاحِيَّة تَسأَل «هَل يَملِك دَورُكَ هذا؟» وجَوابُها ثابِت
    /// لِلدَور؛ والاستِحقاق يَسأَل «هَل بَقِيَ في باقَتِكَ رَصيد؟»
    /// وجَوابُه يَتَغَيَّر بِكُلّ عَمَلِيَّة. الأَوَّل يَرُدّ ‏403،
    /// والثاني يَرُدّ رِسالَةً تَقول لِلمُستَخدِم ما يَفعَل.</para>
    /// </summary>
    public static TBuilder RequireEntitlement<TBuilder>(
        this TBuilder b, string capability, string redirectPath, string errCode)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new EntitlementFilter(capability, redirectPath, errCode));
        return b;
    }

    /// <summary>
    /// <para><b>مِفتاحُ API مَطلوب بِنِطاقٍ بِعَينِه</b> — الحارِسُ
    /// الوَحيد تَحتَ <c>/api/v1</c>. يَجمَع الاعتِمادَ والنِطاقَ
    /// واستِحقاقَ <c>api.call</c> في سَطرٍ واحِد، فَلا يُنسى نِصفُه.
    /// وجَوابُ المَنع <b>JSON بِرَمزٍ مِن مَعجَمٍ مُغلَق</b> — لا
    /// تَحويلَ ولا <c>Results.Forbid()</c>.</para>
    ///
    /// <para><b>ولا يُركَّب مَعَ <see cref="RequireAuth{TBuilder}"/></b>:
    /// الاعتِمادانِ بَديلانِ لا مُتَتالِيان — كوكي لِلمُتَصَفِّح،
    /// ومِفتاحٌ لِلآلَة.</para>
    /// </summary>
    /// <param name="scope">مِن <c>ApiScopeCatalog</c> حَصراً — رَمزٌ
    /// خارِجَه يَرمي <b>عِندَ التَركيب</b> فَيُفشِل الإقلاع.</param>
    public static TBuilder RequireApiKey<TBuilder>(this TBuilder b, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        b.AddEndpointFilter(new ApiKeyFilter(scope));
        return b;
    }
}
