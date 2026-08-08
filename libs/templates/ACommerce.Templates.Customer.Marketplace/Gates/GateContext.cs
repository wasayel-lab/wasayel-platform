using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// طَبَقات العَوائِق — نَمَط <c>IEndpointFilter</c> chain. كُلّ طَبَقَة
/// تَفحَص شَرطاً واحِداً (مُسَجَّل، قَبِل الشُروط، يَملِك صَلاحِيَّة، …)
/// وَإمّا تُمَرِّر لِلتالي أَو تُعيد <see cref="IResult"/> يَقطَع السِلسِلَة.
/// تُلغي الـ boilerplate الَّذي كانَ مُكَرَّراً في كُلّ endpoint.
///
/// <para>الفُلتَر يَكتُب الـ userId إلى <c>HttpContext.Items["ac.userId"]</c>
/// بَعد التَّوثيق الناجِح، فَالـ handler يَقرَأها عَبر
/// <see cref="GateAccessors.UserId"/> بِدون إعادَة فَكّ الـ token.</para>
/// </summary>
public static class GateKeys
{
    public const string UserId   = "ac.userId";
    public const string SlugItem = "ac.slug";
    public const string Role     = "ac.role";   // مُستَخرَج مِن URL
}

public static class GateAccessors
{
    public static Guid UserId(this HttpContext http)
        => (Guid)http.Items[GateKeys.UserId]!;

    public static Guid? UserIdOrNull(this HttpContext http)
        => http.Items[GateKeys.UserId] as Guid?;

    public static string Slug(this HttpContext http)
        => (string)(http.Items[GateKeys.SlugItem] ?? http.Request.RouteValues["slug"]!);

    public static string? Role(this HttpContext http)
        => http.Items[GateKeys.Role] as string
        ?? AuthSession.ExtractRoleFromPath(http.Request.Path);
}

/// <summary>إصدار الشُروط الحاليّ. بَدّله لِتُجبِر كُلّ المُستَخدِمين عَلى
/// إعادَة القَبول (مَثَلاً بَعد تَعديل سياسَة الخُصوصِيَّة).</summary>
public static class TermsPolicy
{
    public const int CurrentVersion = 1;
}
