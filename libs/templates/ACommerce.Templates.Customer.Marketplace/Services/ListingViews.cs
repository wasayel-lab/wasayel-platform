using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
///
/// <para><b>‏(‏2026-10-28، قَرارُ المالِك د58؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: «كَم زائِراً في السّاعَة» أَعلاه صارَ <b>«كَم زائِراً في اليَوم»</b>
/// (‏<c>AuthHandlers.ViewWindow</c>)، ولِلعَدّادِ في الذاكِرَةِ سَقفُ مَفاتيح (‏<c>AuthHandlers.MaxViewKeys</c>): مُمتَلِئٌ بَعدَ كَنسِ المُنتَهي ⇒ لا تُعَدُّ
/// المُشاهَدَةُ الجَديدَة، والنُقطَةُ تُجيبُ ‏204 كَما تُجيبُ المُكَرَّرَة، وسَطرُ تَحذيرٍ في السِجِلّ. وأَرقامُ المُشاهَداتِ المُسَجَّلَةُ لا تُمَسّ — طَريقَةُ
/// العَدِّ وَحدَها تَتَغَيَّرُ مِن يَومِ النَشر.</para>
/// </summary>
public static class ListingViews
{
    /// <summary>صاحِبُ جَلسَةِ هذا المَتجَرِ في الطَلَب، أَو <c>null</c> لِلمَجهول — نِسبَةٌ لا مَنع.</summary>
    public static Guid? ViewerOf(HttpRequest req, string slug)
    {
        var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug));
        return parsed is { } p && p.TenantSlug == slug ? p.UserId : null;
    }

    /// <summary>عُنوانُ الزائِرِ كَما يَراه الخادِمُ بَعدَ <c>UseForwardedHeaders</c> — مَصدَرٌ واحِدٌ لِلاستِهلاكِ والإعادَة.</summary>
    public static string? ClientIp(HttpRequest req) => req.HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// <para><b>رَدُّ ‏404 لِبابِ المُشاهَدَة — ومِفتاحُ مُعَرِّفٍ لا وُجودَ لَه يُعاد</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-3؛ تَوصيفُه <c>c74b2ac</c>).
    /// الحِصَّةُ تَسبِقُ القِراءَةَ (lens-security-5) فَيُستَهلَكُ المِفتاحُ قَبلَ أَن يُعرَفَ أَهُناكَ إعلان؛ وكانَ ‏404 يُبقيه يَوماً، فَمُعَرِّفاتٌ عَشوائِيَّةٌ
    /// — لا تُكَلِّفُ المُرسِلَ شَيئاً — تَملَأُ سَقفَ المَفاتيحِ فَلا تُعَدُّ مُشاهَدَةٌ حَقيقِيَّة. والآن <b>مُعَرِّفٌ لا مَجرى لَه</b>
    /// (<paramref name="existed"/> = <c>false</c>) يُعيدُ مِفتاحَه.</para>
    /// <para><b>والإعلانُ المَحذوفُ يُبقي مِفتاحَه — عَمداً</b>: مَفاتيحُه مَحدودَةٌ بِما وُجِدَ فِعلاً مِن إعلانات، وإعادَتُها تَجعَلُ كُلَّ طَلَبٍ مُكَرَّرٍ
    /// لِإعلانٍ مَحذوفٍ إعادَةَ بِناءٍ لِمَجراه كُلِّه (<c>AggregateStreamAsync</c>) بَدَلَ مَرَّةٍ في اليَوم. والمُعَرِّفُ الَّذي لا مَجرى لَه قِراءَتُه فارِغَة.</para>
    /// </summary>
    public static IResult NotFound(HttpRequest req, string slug, Guid listingId, Guid? viewerId, bool existed)
    {
        if (!existed) AuthHandlers.ForgetView(QuotaKey(slug, listingId, viewerId, ClientIp(req)));
        return Results.NotFound();
    }

    /// <summary>مِفتاحُ «مُشاهَدَةٍ واحِدَة»: الإعلانُ في المَتجَر، والزائِرُ بِهُوِيَّتِه إن وُثِّقَ وإلّا بِعُنوانِه.</summary>
    public static string QuotaKey(string slug, Guid listingId, Guid? viewerId, string? clientIp)
        => viewerId is Guid u
            ? $"{slug}|view:{listingId:N}|u:{u:N}"
            : $"{slug}|view:{listingId:N}|ip:{(string.IsNullOrWhiteSpace(clientIp) ? "-" : clientIp.Trim())}";

    /// <summary>أَتُعَدُّ هذِه المُشاهَدَة؟ الأُولى في النافِذَةِ نَعَم وما بَعدَها لا — الحِصَّةُ في <see cref="AuthHandlers.ConsumeViewQuota"/>. وعَدّادٌ
    /// كُنِسَ وبَقِيَ مُمتَلِئاً يُكتَبُ لَه تَحذيرٌ في <paramref name="logs"/> (‏2026-10-28، د58) — ولا يَرى الزائِرُ شَيئاً.</summary>
    public static bool Counts(string slug, Guid listingId, Guid? viewerId, string? clientIp, ILoggerFactory? logs = null)
    {
        var outcome = AuthHandlers.ConsumeViewQuota(QuotaKey(slug, listingId, viewerId, clientIp));
        if (outcome == AuthHandlers.ViewCount.FullAfterSweep)
            logs?.CreateLogger("ACommerce.ListingViews").LogWarning(
                "[views] counter full ({Max} keys after sweeping expired ones) — new views are not counted until a key expires",
                AuthHandlers.MaxViewKeys);
        return outcome == AuthHandlers.ViewCount.Counted;
    }
}
