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
        if (user is null)
            return Results.Redirect(AuthSession.LinkFor(slug, role, "login"));

        var accepted = user.AcceptedTermsAt is not null
                    && user.AcceptedTermsVersion >= TermsPolicy.CurrentVersion;
        if (accepted) return await next(ctx);

        var returnUrl = Uri.EscapeDataString(http.Request.Path + http.Request.QueryString);
        return Results.Redirect(AuthSession.LinkFor(slug, role,
            $"terms?returnUrl={returnUrl}"));
    }
}
