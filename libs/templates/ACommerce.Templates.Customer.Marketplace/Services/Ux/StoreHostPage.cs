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
    /// <para><b>سِياقُ المُستَأجِرِ لِسكربتاتِ العَميل</b> — <c>{"slug":…,"role":…,"hostMode":…,"home":…}</c>: المَتجَرُ الَّذي حَلَّه الخادِم (فارِغٌ عَلى
    /// صَفَحاتِ المَنَصَّة)، ودَورُ المَسار، وهَل الطَلَبُ عَلى مُضيفِ المَتجَر، وهَل الصَفحَةُ جَذرُ المَتجَر. <b>فَلا تَستَخرِجُ كُتلَةُ سكربتٍ السلاجَ مِن
    /// <c>location.pathname</c></b> (G11). ومُسَلسَلٌ بِـ<c>System.Text.Json</c> بِمُرَمِّزِه الافتِراضيِّ الَّذي يَهرُبُ <c>&lt;</c> و<c>&gt;</c>
    /// و<c>&amp;</c> — فَلا يُغلِقُ حَرفٌ وَسمَ <c>&lt;script&gt;</c>.</para>
    /// </summary>
    public static string ClientContextJson(string? slug, string? role, bool hostMode, bool home)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["slug"] = slug ?? "",
            ["role"] = role ?? "",
            ["hostMode"] = hostMode,
            ["home"] = home,
        });
}
