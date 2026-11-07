using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>
/// <para><b>الخُروجُ يَمُرُّ بِمُضيفاتِ المَتجَرِ كُلِّها</b> (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-2؛ ADR-050 §٥ و§١٤). الكَعكاتُ مَقصورَةٌ عَلى
/// مُضيفِها (لا <c>Domain=</c>)، وبابُ <c>/{slug}/auth/logout</c> يَمحو جَرَّةَ المُضيفِ الَّذي خَدَمَه وَحدَها — فَعُضوٌ دَخَلَ عَلى <c>{slug}.R</c> وعَلى
/// <c>{slug}-vendor.R</c> وضَغَطَ «خُروج» عَلى أَحَدِهِما بَقِيَ داخِلاً عَلى الآخَرِ ثَلاثينَ يَوماً (مَقيسٌ حَيّاً عِندَ <c>f8e02c1</c>). والآن: بَعدَ أَن يَمحوَ البابُ
/// جَرَّتَه يُحَوِّلُ إلى <c>GET /{slug}/auth/logout/continue</c> عَلى المُضيفِ التالي، فَيَمحو كُلٌّ جَرَّتَه ويُحَوِّلُ إلى ما بَعدَه، وتَنتَهي السِلسِلَةُ في جَذرِ
/// مُضيفِ البِدايَة.</para>
/// <para><b>والوِجهَةُ لا تُؤخَذُ مِن الطَلَبِ أَبَداً</b> (لا تَحويلٌ مَفتوح): المُضيفاتُ مِن أَدوارِ المَتجَرِ (<see cref="StoreHostItems.HostedRoles"/> — يَكتُبُها
/// <c>TenantResolverMiddleware</c> بِشَرطِ الـ404 نَفسِه)، والتالي مِن مَوضِعِ المُضيفِ الَّذي يَخدِمُ الطَلَبَ ومُضيفِ البِدايَة، ومُضيفُ البِدايَةِ مِن تَذكِرَةٍ
/// مُوَقَّعَةٍ (<see cref="AuthHandlers.MakeLogoutHop"/>) تَربِطُ المُرورَ بِالحِسابِ الَّذي خَرَجَ — فَمُضيفٌ يَحمِلُ جَلسَةَ حِسابٍ آخَرَ لا يُمحى.</para>
/// <para><b>وفي وَضعِ المَسارِ لا شَيءَ يَتَغَيَّر</b>: لا مُضيفَ لِلمَتجَر، فَلا سِلسِلَة (<see cref="Next"/> يُجيبُ <c>null</c>).</para>
/// </summary>
public static class LogoutHops
{
    /// <summary>مِفتاحُ مُضيفِ المَتجَرِ نَفسِه في السِلسِلَة — مِحرَفٌ لا يَكونُ اسمَ دَور (<c>^[a-z][a-z0-9_]*$</c>).</summary>
    public const string StoreHost = "-";

    /// <summary>ذَيلُ نُقطَةِ المُرور — <c>/{slug}/auth/logout/continue</c>.</summary>
    public const string ContinueTail = "auth/logout/continue";

    /// <summary>اسمُ وَسيطِ التَذكِرَةِ في الاستِعلام.</summary>
    public const string TicketKey = "t";

    /// <summary>مُضيفاتُ المَتجَرِ بِتَرتيبِ المُرور: مُضيفُه ثُمَّ أَدوارُه الَّتي لَها مُضيفٌ (في <see cref="RoleHostNames"/>، بِلا تَكرار، مُرَتَّبَة).</summary>
    public static IReadOnlyList<string> Tour(IReadOnlyList<string> hostedRoles)
        => [StoreHost, .. hostedRoles.Where(RoleHostNames.ByCatalogSlug.ContainsKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>مُضيفاتُ المَتجَرِ كَما كَتَبَها الوَسيطُ لِهذا الطَلَب — فارِغَةٌ في وَضعِ المَسار.</summary>
    public static IReadOnlyList<string> HostedOf(HttpContext ctx) => ctx.Items[StoreHostItems.HostedRoles] as IReadOnlyList<string> ?? [];

    /// <summary>
    /// <para><b>المَحَطَّةُ التالِيَة — قَرارٌ نَقِيّ</b>: <paramref name="origin"/> <c>null</c> = بِدايَةُ السِلسِلَةِ عَلى المُضيفِ الَّذي يَخدِمُ الطَلَب. يُجيبُ
    /// <c>null</c> حينَ لا سِلسِلَة (لا مُضيفَ لِهذا المَتجَر، أَو مُضيفُ دَورٍ لا يَحمِلُه، أَو مُضيفُ بِدايَةٍ لَيسَ مِن مُضيفاتِه، أَو مُضيفٌ لا يَقَعُ في الطَريق، أَو
    /// مَتجَرٌ بِمُضيفٍ واحِد)؛ وإلّا (<c>Url</c>، <c>Final</c>): نُقطَةُ المُرورِ عَلى المُضيفِ التالي، أَو جَذرُ مُضيفِ البِدايَةِ في آخِرِها.</para>
    /// </summary>
    public static (string Url, bool Final)? Next(HostView view, string slug, IReadOnlyList<string> hostedRoles, string? origin)
    {
        if (!StoreUrl.OnHostOf(view, slug)) return null;
        var keys = Tour(hostedRoles);
        var here = view.HostRole ?? StoreHost;
        var start = origin ?? here;
        if (!keys.Contains(here, StringComparer.Ordinal) || !keys.Contains(start, StringComparer.Ordinal)) return null;
        var tour = keys.Where(k => k != start).ToList();
        if (tour.Count == 0) return null;
        var at = origin is null ? -1 : tour.IndexOf(here);
        if (origin is not null && at < 0) return null;
        return at + 1 < tour.Count
            ? ($"{OriginOf(view, slug, tour[at + 1])}/{slug}/{ContinueTail}", false)
            : ($"{OriginOf(view, slug, start)}/", true);
    }

    /// <summary>أَصلُ مُضيفٍ لِلمَتجَر: <c>{scheme}://{slug}.R{port}</c> أَو <c>{scheme}://{slug}-{rolehost}.R{port}</c>.</summary>
    private static string OriginOf(HostView view, string slug, string key)
        => $"{(string.IsNullOrEmpty(view.Scheme) ? "https" : view.Scheme)}://{slug}{(key == StoreHost ? "" : "-" + RoleHostNames.ByCatalogSlug[key])}.{view.Apex}{view.Port}";

    /// <summary><b>أَوَّلُ مَحَطَّةٍ بَعدَ بابِ الخُروج</b> — لِجَلسَةٍ في هذا المَتجَرِ عَلى مُضيفٍ لَه، بِتَذكِرَةٍ لِحِسابِها؛ وإلّا <c>null</c> (البابُ يُحَوِّلُ إلى جَذرِه كَما كان).</summary>
    public static string? Begin(HttpContext http, string slug, string? sessionToken, DateTimeOffset now)
    {
        if (AuthHandlers.ParseToken(sessionToken) is not { } session || session.TenantSlug != slug) return null;
        var view = StoreUrl.ViewOf(http);
        if (Next(view, slug, HostedOf(http), origin: null) is not { } stop) return null;
        return $"{stop.Url}?{TicketKey}={AuthHandlers.MakeLogoutHop(session.UserId, slug, view.HostRole ?? StoreHost, now)}";
    }

    /// <summary>
    /// <para><b>مَحَطَّةُ مُرور</b>: تَذكِرَةٌ صالِحَةٌ لِهذا المَتجَرِ ومُضيفٌ في الطَريق ← (<c>Clear</c>: لا جَلسَةَ هُنا أَو هي لِلحِسابِ الَّذي خَرَج؛ <c>Next</c>: المَحَطَّةُ
    /// التالِيَةُ بِالتَذكِرَةِ نَفسِها، أَو جَذرُ مُضيفِ البِدايَة). وما سِوى ذلك (<c>false</c>، <c>null</c>): لا يُمحى شَيءٌ ويُحَوِّلُ البابُ إلى جَذرِه.</para>
    /// </summary>
    public static (bool Clear, string? Next) Continue(HttpContext http, string slug, DateTimeOffset now)
    {
        var ticket = http.Request.Query[TicketKey].ToString();
        if (AuthHandlers.ReadLogoutHop(ticket, slug, now) is not { } hop) return (false, null);
        if (Next(StoreUrl.ViewOf(http), slug, HostedOf(http), hop.Origin) is not { } stop) return (false, null);
        var here = AuthHandlers.ParseToken(AuthSession.ResolveToken(http.Request, slug));
        var clear = here is null || here.Value.TenantSlug != slug || here.Value.UserId == hop.UserId;
        return (clear, stop.Final ? stop.Url : $"{stop.Url}?{TicketKey}={ticket}");
    }
}
