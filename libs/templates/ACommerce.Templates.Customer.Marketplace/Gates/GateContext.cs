using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary><b>هُوِيَّةُ مِفتاح API</b> — يَملَؤُها
    /// <c>ApiKeyFilter</c> بَعدَ اعتِمادٍ ناجِح، إلى جانِب
    /// <see cref="UserId"/> و<see cref="SlugItem"/> نَفسِهِما. وهذا
    /// هُوَ سَبَبُ صَلاحِيَّةِ الأُنبوب القائِم بِلا تَعديل: كُلُّ
    /// حارِسٍ يَقرَأُ الهُوِيَّةَ مِن <c>HttpContext.Items</c> —
    /// و<c>EntitlementFilter</c> مِنها — يَرِثُ المِفتاحَ بِلا سَطرٍ
    /// جَديد (‏§٣٫٤). والمَفتاحُ الإضافيّ لِما لا يَحمِلُه المُستَخدِم:
    /// مُعَرِّفُ المِفتاحِ ونِطاقاتُه.</summary>
    public const string ApiPrincipal = "ac.apiPrincipal";
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

    /// <summary>هُوِيَّةُ مِفتاح API لِهذا الطَلَب، أَو <c>null</c>
    /// لِطَلَبٍ لَم يَمُرَّ بِـ<c>ApiKeyFilter</c>.</summary>
    public static Services.Api.ApiKeyPrincipal? ApiPrincipal(this HttpContext http)
        => http.Items[GateKeys.ApiPrincipal] as Services.Api.ApiKeyPrincipal;

    /// <summary>
    /// <para><b>تَنفيذُ الاستِحقاق الَّذي يَخدِم هذِه القُدرَة.</b>
    /// صارَ التَنفيذُ اثنَين يَومَ ‏2026-08-23: تَيارُ اشتِراكِ
    /// المُستَخدِم (<c>SubscriptionEntitlements</c>)، ووَثيقَةُ باقَةِ
    /// المُستَأجِر (<c>TenantPlanEntitlements</c>).</para>
    ///
    /// <para><b>ولِماذا مُوَجِّهٌ لا تَنفيذٌ مُرَكَّب</b>:
    /// <c>GetRequiredService&lt;IEntitlements&gt;()</c> يُعيد <b>آخِرَ
    /// مُسَجَّل</b> صامِتاً — فَتَسجيلُ الثاني كانَ سَيَكسِر
    /// <c>api.call</c> بِلا أَن يَحمَرَّ شَيء. والمُوَجِّهُ يَسأَل
    /// <c>Handles</c>، ويَرمي إن لَم يَخدِمها أَحَد: <b>سوءُ التَركيب
    /// عَطَبٌ مَسموع</b>، لا سَماحٌ صامِت.</para>
    ///
    /// <para>ومُستَهلِكاه اليَومَ اثنان — <c>EntitlementFilter</c> و
    /// <c>ApiKeyFilter</c> — وهُما كُلُّ مَن يَقرَأ الاستِحقاقَ مِن
    /// وِعاء الخِدمات.</para>
    /// </summary>
    public static IEntitlements Entitlements(this HttpContext http, string capability)
    {
        foreach (var e in http.RequestServices.GetServices<IEntitlements>())
            if (e.Handles.Contains(capability))
                return e;

        throw new NotSupportedException(
            $"لا تَنفيذَ استِحقاقٍ يَخدِم «{capability}» — تَركيبٌ ناقِص.");
    }
}

/// <summary>إصدار الشُروط الحاليّ. بَدّله لِتُجبِر كُلّ المُستَخدِمين عَلى
/// إعادَة القَبول (مَثَلاً بَعد تَعديل سياسَة الخُصوصِيَّة).</summary>
public static class TermsPolicy
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// <para><b>هَل قَبِلَ هذا المُستَخدِمُ الإصدارَ الحاليّ؟</b> —
    /// دالَّةٌ نَقِيَّة، وتَعريفٌ واحِد يَقرَؤُه <b>ثَلاثَة</b>:
    /// ‏<c>TermsFilter</c> (يَحرُس النُقطَة)، و<c>GatePipeline</c>
    /// (يَحرُس الأَمر)، و<c>AccountQueries</c> (تَقرَؤُها الشاشَة).</para>
    ///
    /// <para><b>وهي استُخرِجَت بِبُلوغِ العَدَد لا قَبلَه</b> (القاعِدَة
    /// ١: ثَلاثَةُ مُستَهلِكين قَبلَ الاستِخراج): العِبارَةُ كانَت
    /// مَكتوبَةً حَرفاً في المَوضِعَين الأَوَّلَين قَبلَ هذِه المَوجَة،
    /// والمَوجَةُ كانَت ستَكتُبُها **رابِعَةً**. فَتَعريفٌ واحِدٌ هُنا
    /// أَرخَصُ مِن رابِعَةٍ تَنجَرِف — وشَرطُ «قَبِلَ» و«الإصدار
    /// كافٍ» لا يَجوز أَن يَختَلِفا بَينَ حارِسٍ وشاشَة، وإلّا فُتِحَت
    /// شاشَةٌ لِمَن تَرُدُّه النُقطَة.</para>
    /// </summary>
    public static bool IsAccepted(ACommerce.Kit.Auth.User? user)
        => user is not null
        && user.AcceptedTermsAt is not null
        && user.AcceptedTermsVersion >= CurrentVersion;
}
