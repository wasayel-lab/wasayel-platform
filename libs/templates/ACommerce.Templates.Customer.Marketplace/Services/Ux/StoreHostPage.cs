using System.Text.Json;
using ACommerce.Kit.Tenants;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>
/// <para><b>الصَفحَةُ كَما يَراها المُتَصَفِّحُ عَلى مُضيفِ المَتجَر</b> (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·6). حاقِنُ المُضيفِ يُحَوِّلُ
/// <c>ashare.R/listings</c> إلى <c>/ashare/listings</c> قَبلَ التَوجيه — فَكُلُّ ما يَقرَأُ <c>Request.Path</c> بَعدَه يَرى مَساراً لَم يَرَه الزائِر.
/// <b>والعِلَّةُ المَقيسَة</b> (A2 §3.3): الـcanonical وog:url صارا <c>https://ashare.R/ashare/…</c>؛ وأَربَعُ كُتَلِ سكربتٍ تَقرَأُ السلاجَ مِن
/// <c>location.pathname</c> فَتَأخُذُ <c>listings</c> مَتجَراً عَلى العُنوانِ النَظيف.</para>
///
/// <para><b>والأَثَرُ بِلا نِطاقٍ مُهَيَّأٍ صِفر</b>: لا يَكتُبُ الحاقِنُ شَيئاً في <c>Items</c>، فَالمَسارُ الظاهِرُ هُوَ <c>Request.Path</c> نَفسُه.</para>
/// </summary>
public static class StoreHostPage
{
    /// <summary>مَتجَرُ المُضيفِ إن جاءَ الطَلَبُ عَلى <c>{slug}.R</c>، وإلّا <c>null</c>.</summary>
    public static string? HostSlug(HttpContext? ctx)
        => ctx?.Items[StoreHostItems.Slug] as string;

    /// <summary>المَسارُ كَما في شَريطِ العُنوان: ما قَبلَ الحَقنِ إن كانَ، وإلّا مَسارُ الطَلَب.</summary>
    public static string VisiblePath(HttpContext? ctx)
        => ctx?.Items[StoreHostItems.OriginalPath] as string is { Length: > 0 } original ? original : ctx?.Request.Path.Value ?? "/";

    /// <summary>
    /// <para><b>مَسارُ الـcanonical وog:url مِن العُنوانِ الظاهِر</b>: عَلى الجَذرِ (وبِلا نِطاق) هُوَ نَفسُه بِحَرفِه؛ وعَلى مُضيفِ المَتجَرِ يَسقُطُ مِنه
    /// <c>/{slug}</c> البادِئُ إن حَمَلَه — فَالمُضيفُ جَذرُ المَتجَر، و<c>ashare.R/ashare/x</c> و<c>ashare.R/x</c> صَفحَةٌ واحِدَةٌ بِعُنوانٍ قانونيٍّ
    /// واحِد (هُوَ ما تَسرُدُه خَريطَةُ مَوقِعِ المُضيف).</para>
    /// </summary>
    public static string CanonicalPath(string? visiblePath, string? hostSlug)
    {
        var path = string.IsNullOrEmpty(visiblePath) ? "/" : visiblePath;
        if (string.IsNullOrEmpty(hostSlug)) return path;
        var prefix = "/" + hostSlug;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return path;
        if (path.Length == prefix.Length) return "/";
        return path[prefix.Length] == '/' ? path[prefix.Length..] : path;
    }

    /// <summary>
    /// <para><b>دَورُ الصَفحَةِ الَّتي أَرسَلَت الطَلَبَ مِن رَأسِ <c>Referer</c> — كَما قَرَأَ حاقِنُ المُضيفِ تِلكَ الصَفحَة</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ
    /// الثانِيَة · C2a-P2-01). كانَ مَسارُ الـReferer يُسأَلُ عَنه <c>AuthSession.ExtractRoleFromPath</c> كَما هُوَ — وهو يَنتَظِرُ <c>/{slug}/r/{role}/…</c> —
    /// فَصَفحَةُ دَورٍ بِعُنوانِها النَظيفِ عَلى مُضيفِ المَتجَرِ (<c>{slug}.R/r/customer/login</c>، وهو ما يُعلِنُه الـcanonical وog:url، <see cref="CanonicalPath"/>)
    /// تُرسِلُ <c>POST</c> فَيَرجِعُ تَحويلُه بِلا دَور: الزائِرُ يَخرُجُ مِن تَطبيقِ دَورِه ومِن نِطاقِ عامِلِ خِدمَتِه.</para>
    /// <para><b>والقاعِدَة</b>: عَلى مُضيفِ المَتجَرِ (<paramref name="hostSlug"/>)، ومُضيفُ الـReferer (بِمَنفَذِه) هُوَ مُضيفُ الطَلَبِ نَفسُه، ومَسارُه لا يَبدَأُ
    /// بِمَقطَعِ المَتجَر — يُقرَأُ بَعدَ <c>/{hostSlug}</c> كَما حَقَنَه الحاقِن. وما سِوى ذلك كَما كان بِحَرفِه: بِلا مُضيفِ مَتجَر، أَو الـReferer بِشَكلِ المَسار، أَو
    /// مِن مُضيفٍ آخَر (فَلا يَختارُ مُضيفٌ غَريبٌ دَوراً بِعُنوانٍ نَظيف). وتَوصيفُ ما يَنجو <c>RefererRoleCharacterizationTests</c>.</para>
    /// <para><b>ولِماذا لا يُحاكي الحاقِنَ في جُذورِ المَنَصَّة</b> (<c>/sw.js</c>، <c>/api/…</c>، <c>/studio</c>): الدَورُ مَقطَعٌ ثانٍ اسمُه <c>r</c> بَعدَ البادِئَة، و<c>r</c>
    /// لَيسَ جَذراً مَخدوماً — فَلا يُعطي جَذرٌ دَوراً بِالبادِئَةِ ولا بِغَيرِها، والحارِسُ يَحمَرُّ إن صارَ <c>r</c> جَذراً.</para>
    /// </summary>
    public static string? RoleFromReferer(string? referer, string? requestHost, string? hostSlug)
    {
        if (string.IsNullOrEmpty(referer)) return null;
        try
        {
            var uri = new Uri(referer);
            var path = uri.AbsolutePath;
            if (!string.IsNullOrEmpty(hostSlug) && !string.IsNullOrEmpty(requestHost)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase)
                && !StartsWithSegment(path, hostSlug))
                path = "/" + hostSlug + (path == "/" ? "" : path);
            return AuthSession.ExtractRoleFromPath(new PathString(path));
        }
        catch { return null; }
    }

    /// <summary>
    /// <para><b>وعَلى مُضيفِ الدَور</b> (‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4): الـReferer مِن المُضيفِ نَفسِه بِلا دَورٍ صَريحٍ في مَسارِه صَفحَةٌ في تَطبيقِ
    /// ذلكَ الدَور — فَالدَورُ دَورُ المُضيف (<paramref name="hostRole"/>)، كَما وَضَعَه الحاقِنُ في قالِبِ صَفحَتِه؛ والدَورُ الصَريحُ (<c>/r/{role}/…</c>) كَما هُوَ.
    /// ومِن مُضيفٍ آخَر لا دَورَ مِنه. وبِلا دَورِ مُضيفٍ هِيَ <see cref="RoleFromReferer(string?, string?, string?)"/> بِحَرفِها.</para>
    /// </summary>
    public static string? RoleFromReferer(string? referer, string? requestHost, string? hostSlug, string? hostRole)
    {
        var role = RoleFromReferer(referer, requestHost, hostSlug);
        if (role is not null || string.IsNullOrEmpty(hostRole) || string.IsNullOrEmpty(referer) || string.IsNullOrEmpty(requestHost)) return role;
        try
        {
            var uri = new Uri(referer);
            return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                   && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase) ? hostRole : null;
        }
        catch { return null; }
    }

    /// <summary>أَيَبدَأُ المَسارُ بِمَقطَعٍ هُوَ <paramref name="segment"/> بِعَينِه (بِلا حالَةِ أَحرُف) — كَما يَمتَنِعُ الحاقِنُ عَن مَسارٍ يَحمِلُ المَتجَرَ سَلَفاً.</summary>
    private static bool StartsWithSegment(string path, string segment)
    {
        var firstSlash = path.IndexOf('/', 1);
        var first = path.Length < 2 ? "" : firstSlash > 0 ? path[1..firstSlash] : path[1..];
        return string.Equals(first, segment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <para><b>سِياقُ المُستَأجِرِ لِسكربتاتِ العَميل</b> — <c>{"slug":…,"role":…,"hostMode":…,"home":…}</c>: المَتجَرُ الَّذي حَلَّه الخادِم (فارِغٌ عَلى
    /// صَفَحاتِ المَنَصَّة)، ودَورُ المَسار، وهَل الطَلَبُ عَلى مُضيفِ المَتجَر، وهَل الصَفحَةُ جَذرُ المَتجَر. <b>فَلا تَستَخرِجُ كُتلَةُ سكربتٍ السلاجَ مِن
    /// <c>location.pathname</c></b> (G11). ومُسَلسَلٌ بِـ<c>System.Text.Json</c> بِمُرَمِّزِه الافتِراضيِّ الَّذي يَهرُبُ <c>&lt;</c> و<c>&gt;</c>
    /// و<c>&amp;</c> — فَلا يُغلِقُ حَرفٌ وَسمَ <c>&lt;script&gt;</c>.</para>
    /// </summary>
    /// <remarks>‏(‏2026-11-07، ADR-050 §٤ · W13-4·C2b·4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وعَلى مُضيفِ الدَور</b> يُرسِلُ <c>App.razor</c> الدَورَ فارِغاً
    /// و<c>home</c> كاذِباً: العُنوانُ الظاهِرُ بِلا <c>/r/{role}</c> — فَنِطاقُ عامِلِ الخِدمَةِ ومِفتاحُ التَثبيتِ <c>/</c> كَما يُعلِنُ البَيان، وجَذرُ المُضيفِ بَيتُ تَطبيقِ
    /// الدَورِ لا مُنتَقٍ — فَيُعرَضُ زِرُّ التَثبيت. وسكربتُ العَميلِ لا يَتَغَيَّرُ حَرفاً (فَوَضعُ المَسارِ بايتاً بِبايت).</remarks>
    public static string ClientContextJson(string? slug, string? role, bool hostMode, bool home)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["slug"] = slug ?? "",
            ["role"] = role ?? "",
            ["hostMode"] = hostMode,
            ["home"] = home,
        });
}
