using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// إصلاح 403: رَفض الصَلاحِيَّة يُخرِج ‏403 لا استِثناء ‏500.
///
/// <para><b>العَيب المُلتَقَط:</b> المِنَصَّة لا تَستَدعي
/// <c>AddAuthentication</c> إطلاقاً، فَكُلّ <c>Results.Forbid()</c> كانَ يَرمي
/// <c>System.InvalidOperationException: Unable to find the required
/// 'IAuthenticationService' service</c> مِن <c>ForbidHttpResult.ExecuteAsync</c>
/// → ‏500 بَدَل ‏403 لِكُلّ رَفض صَلاحِيَّة في المِنَصَّة.</para>
///
/// <para><see cref="Denial_As403StatusCode_ExecutesWithoutAuthenticationService"/>
/// يُثبِت أَنّ الرَّدّ المُختار بَعد الإصلاح (<c>Results.StatusCode(403)</c>)
/// يَعمَل بِلا خِدمَة المُصادَقَة، وَ
/// <see cref="Forbid_Throws_WhenAuthenticationServiceMissing"/> يُوَثِّق سَبَب
/// الـ500 القَديم على نَفس السياق.</para>
/// </summary>
public class ForbidResultTests
{
    // سياق يُحاكي المِنَصَّة: خِدمَة تَسجيل مَوجودَة، لكِنّ AddAuthentication
    // غَير مُسَجَّلَة إطلاقاً (أَصل العَيب).
    private static DefaultHttpContext ContextWithoutAuthentication()
    {
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = sp };
    }

    [Fact]
    public async Task Denial_As403StatusCode_ExecutesWithoutAuthenticationService()
    {
        // الرَّدّ المُختار في PermissionFilter بَعد الإصلاح — يَعمَل بِلا خِدمَة
        // المُصادَقَة، فَيُخرِج 403 نَظيفاً بَدَل الاستِثناء.
        var ctx = ContextWithoutAuthentication();
        await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(ctx);
        Assert.Equal(403, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Forbid_Throws_WhenAuthenticationServiceMissing()
    {
        // تَوثيق سَبَب الـ 500 القَديم: Results.Forbid() يَطلُب خِدمَة المُصادَقَة
        // المَفقودَة فَيَرمي بَدَل أَن يُخرِج 403.
        var ctx = ContextWithoutAuthentication();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Results.Forbid().ExecuteAsync(ctx));
        Assert.Contains("Authentication", ex.Message);
    }
}
