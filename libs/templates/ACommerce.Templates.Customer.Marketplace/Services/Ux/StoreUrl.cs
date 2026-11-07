using ACommerce.Kit.Tenants;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>ما يَعرِفُه الطَلَبُ الحالِيُّ عَن المُضيف: النِطاقُ الأَساسُ المُهَيَّأ (<c>R</c>)، ومَتجَرُ المُضيفِ إن جاءَ الطَلَبُ عَلى <c>{slug}.R</c>، والمُخَطَّطُ
/// والمَنفَذُ لِلعُنوانِ المُطلَق. يَكتُبُ الحاقِنُ عَناصِرَه (<see cref="StoreHostItems"/>)، فَبِلا نِطاقٍ مُهَيَّإٍ كُلُّها فارِغَة.</summary>
public readonly record struct HostView(string? Apex, string? HostSlug, string? Scheme, string? Port);

/// <summary>
/// <para><b>عُنوانُ صَفحَةٍ في مَتجَرٍ كَما يَستَعمِلُه المُتَصَفِّحُ مِن الطَلَبِ الحالِيّ</b> (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1). كانَ
/// <c>AuthSession.LinkFor</c> يَكتُبُ <c>/{slug}/…</c> دائِماً، فَعَلى مُضيفِ المَتجَرِ يَظهَرُ <c>ashare.R/ashare/listings</c> في الشَريطِ ويَحمِلُه كُلُّ رابِطٍ
/// ويُشارَكُ بِه — صَحيحٌ يَعمَلُ (الحاقِنُ لا يَحقِنُ مَساراً يَحمِلُ المَتجَرَ سَلَفاً) لكِنّه لَيسَ العُنوانَ الَّذي يُعلِنُه الـcanonical ولا الَّذي تَسرُدُه خَريطَةُ المُضيف.</para>
///
/// <para><b>والقاعِدَة</b>: الطَلَبُ عَلى مُضيفِ هذا المَتجَرِ نَفسِه ← العُنوانُ النَظيف (<c>/listings/x</c>، و<c>/r/{role}/…</c> لِلدَور)؛ <b>وما سِوى ذلك بِشَكلِ
/// المَسارِ بِحَرفِه</b> — بِلا طَلَب، والنِطاقُ فارِغ، والجَذرُ <c>R</c>، ومُضيفُ مَتجَرٍ آخَر. فَوَضعُ المَسارِ لا يَتَغَيَّرُ بايتاً (يُبرهِنُه
/// <c>StoreLinkCharacterizationTests</c> وبَوّابَةُ المَظهَر). <b>والرَوابِطُ المُخزَّنَةُ لا تَمُرُّ هُنا</b> (<c>RelatedUrl</c> وعَناوينُ الإشعارِ تَبقى <c>/{slug}/…</c> —
/// G15): الرَسمُ وَحدَه يَعرِفُ المُضيف.</para>
/// </summary>
public static class StoreUrl
{
    /// <summary>ما يَعرِفُه <paramref name="ctx"/> عَن المُضيف — فارِغٌ بِلا طَلَب.</summary>
    public static HostView ViewOf(HttpContext? ctx)
        => ctx is null
            ? default
            : new HostView(
                ctx.Items[StoreHostItems.Apex] as string,
                ctx.Items[StoreHostItems.Slug] as string,
                string.IsNullOrEmpty(ctx.Request.Scheme) ? "https" : ctx.Request.Scheme,
                ctx.Request.Host.Port is int port ? ":" + port : "");

    /// <summary><b>شَكلُ المَسار</b> — جِسمُ <c>AuthSession.LinkFor</c> كَما كانَ بِحَرفِه: <c>/{slug}/{path}</c> أَو <c>/{slug}/r/{role}/{path}</c>، بِلا شَرطَةٍ أَخيرَة.</summary>
    public static string PathForm(string slug, string? role, string path)
    {
        path = path.TrimStart('/');
        return string.IsNullOrEmpty(role)
            ? $"/{slug}/{path}".TrimEnd('/')
            : $"/{slug}/r/{role}/{path}".TrimEnd('/');
    }

    /// <summary>أَجاءَ الطَلَبُ عَلى مُضيفِ هذا المَتجَرِ بِعَينِه؟ (بِلا حالَةِ أَحرُفٍ كَما يُطابِقُ الحاقِن.) <b>والعُنصُرانِ مَعاً</b>: الحاقِنُ يَكتُبُ النِطاقَ ومَتجَرَ
    /// المُضيفِ كِلَيهِما مُنذُ هذا البَند، فَسِياقٌ فيه مَتجَرُ مُضيفٍ بِلا نِطاقٍ لَم يَمُرَّ بِه — ويُجابُ بِشَكلِ المَسارِ كَما كان (وعَلَيه يَبقى تَوصيفُ
    /// <c>RefererRoleCharacterizationTests</c> بِبَصمَتِه: سِياقُه مَتجَرُ مُضيفٍ وَحدَه).</summary>
    public static bool OnHostOf(HostView view, string slug)
        => !string.IsNullOrEmpty(view.Apex)
           && !string.IsNullOrEmpty(view.HostSlug) && string.Equals(view.HostSlug, slug, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <para><b>الصَفحَةُ عَلى المُضيفِ الَّذي يَخدِمُ هذا الطَلَب</b>: عَلى مُضيفِ المَتجَرِ نَفسِه نَظيفَة (<c>/</c>، <c>/explore</c>، <c>/r/vendor/me</c>)، وما سِواه
    /// <see cref="PathForm"/>. ولا يُغادِرُ المُضيفَ أَبَداً — فَهُوَ ما يَسأَلُه تَحويلُ ما بَعدَ الدُخول: الكَعكَةُ الَّتي كُتِبَت لِلتَوِّ مَقصورَةٌ عَلى هذا المُضيف.</para>
    /// </summary>
    public static string For(HostView view, string slug, string? role, string path = "")
    {
        if (!OnHostOf(view, slug)) return PathForm(slug, role, path);
        var p = path.TrimStart('/');
        var clean = (string.IsNullOrEmpty(role) ? $"/{p}" : $"/r/{role}/{p}").TrimEnd('/');
        return clean.Length == 0 ? "/" : clean;
    }

    /// <summary>
    /// <para><b>«افتَح المَتجَر» — عُنوانُه هُوَ</b>: مِن خارِجِ مُضيفِه (الاستوديو والإشراف عَلى <c>R</c>) والنِطاقُ مُهَيَّأٌ والمُعَرِّفُ يَصلُحُ مُضيفاً
    /// (<see cref="StoreHostName"/>) ← <c>{scheme}://{slug}.R{port}/</c>؛ وعَلى مُضيفِه <c>/</c>؛ وما سِوى ذلك <c>/{slug}</c> كَما كان — فَمَتجَرٌ قائِمٌ لا يَجتازُ
    /// المُصادِقَ يَبقى بِمَسارِه (ADR-050 §٣).</para>
    /// </summary>
    public static string Open(HostView view, string slug)
    {
        if (OnHostOf(view, slug)) return "/";
        if (string.IsNullOrEmpty(view.Apex) || !StoreHostName.IsValid(slug)) return PathForm(slug, null, "");
        return $"{(string.IsNullOrEmpty(view.Scheme) ? "https" : view.Scheme)}://{slug}.{view.Apex}{view.Port}/";
    }

    /// <summary><see cref="Open(HostView, string)"/> بِالطَلَبِ الحالِيِّ (<c>AuthSession.HttpAccessor</c>) — لِصَفَحاتِ Razor.</summary>
    public static string Open(string slug) => Open(ViewOf(AuthSession.HttpAccessor?.HttpContext), slug);
}
