using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Api;

/// <summary>
/// <para><b>قِراءَةُ نَموذَجِ الإصدار</b> — نَفسُ دَورِ
/// <c>TenantConfigSurface.Read*</c> حَرفاً: تَحويلُ
/// <c>req.Form[</c> إلى نَوعٍ مُسَمّىً، <b>خارِجَ جِسمِ النُقطَة</b>.
/// فَما يَبقى في الجِسم حارِسٌ ونِداءٌ ورَدّ.</para>
///
/// <para><b>ولا مَنطِقَ قَرارٍ هُنا</b>: التَحَقُّقُ في
/// <c>ApiKeyValidator</c> (دَوالُّ نَقِيَّة)، وهذِه تَقرَأُ حُقولاً
/// وتُطَبِّعُها لا غَير — فَتَبقى قابِلَةً لِلقِراءَة مِن مَصدَرٍ
/// آخَر (JSON) بِلا نَسخِ قَرار.</para>
/// </summary>
public static class ApiKeySurface
{
    /// <summary>اسمُ حَقلِ النِطاقات في النَموذَج — مُكَرَّرٌ
    /// (‏<c>checkbox</c> لِكُلّ نِطاق).</summary>
    public const string ScopesField = "scopes";

    public static ApiKeyValidator.IssueRequest Read(HttpRequest req)
    {
        var name      = req.Form["name"].ToString().Trim();
        var actorRaw  = req.Form["actor_user_id"].ToString().Trim();
        var actorName = req.Form["actor_name"].ToString().Trim();
        var daysRaw   = req.Form["expires_in_days"].ToString().Trim();

        var scopes = req.Form[ScopesField]
            .Select(s => (s ?? "").Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Guid.TryParse(actorRaw, out var actorId);
        int? days = int.TryParse(daysRaw, out var d) ? d : null;

        return new ApiKeyValidator.IssueRequest(name, actorId, actorName, scopes, days);
    }
}
