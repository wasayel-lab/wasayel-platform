using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>عَدّادُ المُشاهَدَة — مَن يُشاهِد، ومَتى تُعَدّ</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-5، كُبرى). نُقطَةُ
/// <c>POST /{slug}/listings/{id}/view</c> عامَّةٌ بِالتَصميم — مُشاهَدَةُ الزائِرِ المَجهولِ أَغلَبُ المُشاهَدات — وكانَت تُلحِقُ
/// <c>ListingViewed</c> بِكُلِّ طَلَبٍ بِلا حَدّ، وتَقرَأُ رَمزَ الجَلسَةِ في جِسمِها <b>لِلنِسبَةِ لا لِلمَنع</b> فَيَعُدُّها
/// <c>WriteEndpointGuardTests</c> مَحروسَة. صارَت النِسبَةُ هُنا (<see cref="ViewerOf"/>)، والنُقطَةُ مُثَبَّتَةً عامَّةً بِسَبَبِها، و<b>مُشاهَدَةٌ
/// واحِدَةٌ لِكُلِّ زائِرٍ في الإعلانِ في النافِذَة</b> تُعَدُّ قَبلَ أَيِّ قِراءَةٍ أَو كِتابَة (<see cref="Counts"/>).</para>
///
/// <para><b>ومَعنى العَدّادِ يَتَبَدَّلُ ويُقال</b>: كانَ «كَم طَلَباً»، وصارَ «كَم زائِراً في السّاعَة» — والفَتحُ المُتَكَرِّرُ مِن مُتَصَفِّحٍ واحِدٍ
/// لا يَرفَعُه. والمَجهولُ يُعرَفُ بِعُنوانِه (<c>Connection.RemoteIpAddress</c> بَعدَ <c>UseForwardedHeaders</c>)، وأَيُلحِقُ الـSpace عُنوانَ
/// الزائِرِ أَم يُصَدِّقُ رَأسَه غَيرُ مَقيس («ب» W8-2).</para>
///
/// <para><b>شَرطُ الاستِخراجِ إلى ما هُوَ أَعَمّ</b> (القاعِدَة ١): مُستَهلِكٌ ثانٍ في وَقتِ التَشغيلِ يَعُدُّ أَثَرَ قِراءَةٍ بِحَدّ — اليَومَ نُقطَةٌ واحِدَة.</para>
/// </summary>
public static class ListingViews
{
    /// <summary>صاحِبُ جَلسَةِ هذا المَتجَرِ في الطَلَب، أَو <c>null</c> لِلمَجهول — نِسبَةٌ لا مَنع.</summary>
    public static Guid? ViewerOf(HttpRequest req, string slug)
    {
        var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
        return parsed is { } p && p.TenantSlug == slug ? p.UserId : null;
    }

    /// <summary>مِفتاحُ «مُشاهَدَةٍ واحِدَة»: الإعلانُ في المَتجَر، والزائِرُ بِهُوِيَّتِه إن وُثِّقَ وإلّا بِعُنوانِه.</summary>
    public static string QuotaKey(string slug, Guid listingId, Guid? viewerId, string? clientIp)
        => viewerId is Guid u
            ? $"{slug}|view:{listingId:N}|u:{u:N}"
            : $"{slug}|view:{listingId:N}|ip:{(string.IsNullOrWhiteSpace(clientIp) ? "-" : clientIp.Trim())}";

    /// <summary>أَتُعَدُّ هذِه المُشاهَدَة؟ الأُولى في النافِذَةِ نَعَم وما بَعدَها لا — الحِصَّةُ في <see cref="AuthHandlers.TryConsumeViewQuota"/>.</summary>
    public static bool Counts(string slug, Guid listingId, Guid? viewerId, string? clientIp)
        => AuthHandlers.TryConsumeViewQuota(QuotaKey(slug, listingId, viewerId, clientIp));
}
