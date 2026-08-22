using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// <para><b>الحارِسُ الوَحيد تَحتَ <c>/api/v1</c></b> — على نَمَط
/// <see cref="AuthFilter"/> حَرفاً: يَقرَأُ الاعتِماد، ويَملَأُ
/// <b>نَفس</b> <see cref="GateKeys"/> (‏<c>UserId</c>،
/// <c>SlugItem</c>)، ويَرُدُّ مُبَكِّراً عِندَ الفَشَل. والفَرقُ
/// مَوضِعُ الاعتِماد وَحدَه: رَأسُ <c>Authorization</c> بَدَلَ
/// الكوكي.</para>
///
/// <para><b>ولِماذا يُملَأُ <c>GateKeys</c> بِعَينِه ولا يُخترَع
/// ثالِث</b> (‏§٣٫٤، القاعِدَة ٨): <c>EntitlementFilter</c> —
/// البَوّابَةُ الوَحيدَة القابِلَة لِإعادَةِ الاستِعمال في
/// المُستَودَع — تَقرَأُ الهُوِيَّةَ مِن <c>HttpContext.Items</c> لا
/// مِن الكوكي. فَمَن يَملَأُ <c>Items</c> يَرِثُ كُلَّ ما بَعدَه بِلا
/// نَسخِ سَطر. والمُستَودَعُ فيه أَربَعَةُ أَنابيبِ اعتِراضٍ مَبنيَّة
/// ومَهجورَة — فَالمُشكِلَةُ لَيسَت غِيابَ أُنبوبٍ بَل أَنابيبَ بِلا
/// مُستَعمِل.</para>
///
/// <para><b>وثَلاثَةُ فُحوصٍ في مُرَشِّحٍ واحِد لا في ثَلاثَة —
/// والسَبَبُ مَقيس</b>: الاعتِماد، ثُمَّ النِطاق، ثُمَّ استِحقاقُ
/// الباقَة. ولَو فُصِلَ الاستِحقاقُ في مُرَشِّحٍ ثانٍ يُضاف بِاليَد
/// لَجازَ أَن يُنسى على نُقطَة — وذاكَ بِعَينِه الخَطَرُ ٧ في وَثيقَة
/// التَصميم: <c>AllowCustomPattern</c> عُرِضَت في ثَلاثِ شاشات
/// و<b>صِفرُ مَوضِعٍ يَفحَصُها</b>. فَسَطرٌ واحِدٌ يَحمِل الثَلاثَة
/// <b>لا يُنسى نِصفُه</b>.</para>
///
/// <para><b>وما لا يَفعَلُه، ويُقال</b>: لا يَحُلُّ المُستَأجِرَ مِن
/// المَسار — لا مَقطَعَ سلاجٍ في <c>/api/v1/…</c> أَصلاً، وذاكَ
/// مَقصود (‏§٣٫٦). المُستَأجِرُ يَخرُجُ مِن الوَثيقَة، وكُلُّ
/// جَلسَةٍ بَعدَه تُفتَح بِه.</para>
/// </summary>
public sealed class ApiKeyFilter : IEndpointFilter
{
    private readonly string _requiredScope;

    /// <param name="requiredScope">مِن <see cref="ApiScopeCatalog"/>
    /// <b>حَصراً</b> — يُفحَص عِندَ التَركيب لا عِندَ الطَلَب، فَرَمزٌ
    /// مَجهولٌ يُفشِلُ الإقلاعَ بِرِسالَتِه.</param>
    public ApiKeyFilter(string requiredScope)
    {
        _requiredScope = ApiScopeCatalog.Require(requiredScope);

        // ورَمزُ القُدرَة يُفحَص هُنا أَيضاً — لِيَقَعَ الخَطَأُ عِندَ
        // بِناء المَسار لا عِندَ أَوَّل طَلَبٍ في اللَيل.
        CapabilityCatalog.Require(CapabilityCatalog.ApiCall);
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;

        var presented = ApiKeyService.BearerFrom(http.Request.Headers.Authorization.ToString());
        if (presented is null) return ApiError.Of(ApiErrorCatalog.AuthMissing);

        var keys = http.RequestServices.GetRequiredService<ApiKeyService>();
        var auth = await keys.AuthenticateAsync(presented, http.RequestAborted);
        if (!auth.Ok) return ApiError.Of(ApiErrorCatalog.AuthInvalid);

        var principal = auth.Principal!;

        if (!principal.HasScope(_requiredScope))
            return ApiError.Of(ApiErrorCatalog.ScopeMissing,
                new { required = _requiredScope, granted = principal.Scopes });

        // نَفسُ المَفاتيح الَّتي يَملَؤُها AuthFilter — فَما بَعدَها
        // لا يَعرِف مِن أَينَ جاءَت الهُوِيَّة، وهذا هُوَ المَقصود.
        http.Items[GateKeys.UserId]       = principal.ActorUserId;
        http.Items[GateKeys.SlugItem]     = principal.TenantSlug;
        http.Items[GateKeys.ApiPrincipal] = principal;

        var ents = http.RequestServices.GetRequiredService<IEntitlements>();
        var peek = await ents.PeekAsync(
            principal.TenantSlug, principal.ActorUserId,
            CapabilityCatalog.ApiCall, http.RequestAborted);

        if (!peek.Allowed)
            return ApiError.Of(ApiErrorCatalog.EntitlementDenied,
                new { capability = CapabilityCatalog.ApiCall, reason_ar = peek.ReasonAr });

        return await next(ctx);
    }
}
