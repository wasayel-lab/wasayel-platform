using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// الطَّبَقَة الثانِيَة: قَبول الشُروط. تَفتَرِض أَنّ <see cref="AuthFilter"/>
/// رَكَّبَت <c>userId</c> في الـ context. لَو لَم يَقبَل المُستَخدِم الإصدار
/// الحاليّ، نُعيد توجيه لِصَفحَة قَبول الشُروط (تَعرِض المُحتَوى + form
/// قَبول). الـ returnUrl يَحفَظ المَسار الأَصليّ.
/// </summary>
public sealed class TermsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http  = ctx.HttpContext;
        var store = http.RequestServices.GetRequiredService<IDocumentStore>();
        var slug  = http.Slug();
        var role  = http.Role();
        var userId = http.UserIdOrNull();
        if (userId is null) return Results.Unauthorized();  // AuthFilter لَم يَجرِ — خَطَأ تَركيب

        await using var s = store.QuerySession(slug);
        var user = await s.LoadAsync<User>(userId.Value);
        // ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-01) التَحويلُ عَلى المُضيفِ الَّذي يَخدِمُ الطَلَب (`StoreUrl.For` — ADR-050 §١٤) لا رابِطُ تَنَقُّل:
        // كانَ `AuthSession.LinkFor` فَيُرسِلُ كِتابَةً مِن مُضيفِ الدَورِ إلى شُروطِ مُضيفِ المَتجَرِ حَيثُ لا جَلسَة — لا زِرَّ قَبولٍ ولا إعلانَ يُنشَأ.
        var here = Services.Ux.StoreUrl.ViewOf(http);
        if (user is null)
            return Results.Redirect(Services.Ux.StoreUrl.For(here, slug, role, "login"));

        if (TermsPolicy.IsAccepted(user)) return await next(ctx);

        var returnUrl = Uri.EscapeDataString(http.Request.Path + http.Request.QueryString);
        return Results.Redirect(Services.Ux.StoreUrl.For(here, slug, role,
            $"terms?returnUrl={returnUrl}"));
    }
}
