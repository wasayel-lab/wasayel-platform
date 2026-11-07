using ACommerce.Kit.Tenants;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>ما يَعرِفُه الطَلَبُ الحالِيُّ عَن المُضيف: النِطاقُ الأَساسُ المُهَيَّأ (<c>R</c>)، ومَتجَرُ المُضيفِ إن جاءَ الطَلَبُ عَلى <c>{slug}.R</c>، والمُخَطَّطُ
/// والمَنفَذُ لِلعُنوانِ المُطلَق. يَكتُبُ الحاقِنُ عَناصِرَه (<see cref="StoreHostItems"/>)، فَبِلا نِطاقٍ مُهَيَّإٍ كُلُّها فارِغَة.</summary>
public readonly record struct HostView(string? Apex, string? HostSlug, string? Scheme, string? Port)
{
    /// <summary>دَورُ الكَتالوجِ عَلى مُضيفِ الدَور (<c>{slug}-{rolehost}.R</c>) — <c>null</c> عَلى مُضيفِ المَتجَرِ وما سِواه (‏2026-11-07، W13-4·C2b·4).</summary>
    public string? HostRole { get; init; }
}

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
                ctx.Request.Host.Port is int port ? ":" + port : "")
            { HostRole = ctx.Items[StoreHostItems.Role] as string };

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
        // ‏(‏2026-11-07، W13-4·C2b·4) وعَلى مُضيفِ الدَور: دَورُه (أَو بِلا دَور — الحاقِنُ يَضَعُ دَورَ المُضيفِ في قالِبِ صَفحَةِ الدَور) نَظيفٌ بِلا /r/.
        var bare = string.IsNullOrEmpty(role) || string.Equals(role, view.HostRole, StringComparison.Ordinal);
        return HostTail(slug, string.IsNullOrEmpty(role) ? null : role, bare, p);
    }

    /// <summary>
    /// <para><b>رابِطُ تَنَقُّلٍ في المَتجَرِ — <c>AuthSession.LinkFor</c></b> (‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4). عَلى مُضيفٍ لِهذا المَتجَر:
    /// <b>تَطبيقُ الطَلَبِ نَفسُه</b> (مُضيفُ المَتجَرِ ورابِطٌ بِلا دَور، أَو مُضيفُ الدَورِ ودَورُه) ← <see cref="For"/> نَظيفاً؛ <b>وتَطبيقٌ آخَرُ مِن المَتجَرِ نَفسِه</b> ←
    /// مُضيفُه هُوَ مُطلَقاً: دَورٌ في <see cref="ACommerce.Kit.Roles.RoleHostNames"/> ← <c>{scheme}://{slug}-{rolehost}.R{port}/…</c> (بِطاقاتُ الأَدوارِ ومُنتَقي الدَورِ
    /// والرَوابِطُ العابِرَةُ بَينَ الأَدوار)، وبِلا دَورٍ مِن مُضيفِ دَورٍ ← <c>{slug}.R</c>، ودَورٌ بِلا اسمِ مُضيفٍ (مُؤَلَّفٌ في المَتجَر) ← <c>/r/{role}/…</c> عَلى
    /// مُضيفِ المَتجَر. <b>وما سِوى ذلك بِشَكلِ المَسارِ بِحَرفِه</b> — الجَذرُ (لا تَحويلَ لِلمَسارِ قَبلَ المَرحَلَةِ الخامِسَة)، والنِطاقُ فارِغ، ومَتجَرٌ آخَر.</para>
    /// <para><b>والجَلسَةُ لِكُلِّ مُضيفٍ وَحدَه</b> (ADR-050 §٥): الرابِطُ العابِرُ يَحمِلُ الزائِرَ إلى دُخولٍ عَلى مُضيفِ ذلكَ التَطبيق — فَتَحويلُ ما بَعدَ الدُخولِ
    /// والاستِمارات <see cref="For"/> لا هذا (لا يُغادِرُ المُضيفَ الَّذي كَتَبَ الكَعكَة).</para>
    /// <para>‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) <b>«وبِلا دَورٍ مِن مُضيفِ دَورٍ ← <c>{slug}.R</c>» سَقَط</b>:
    /// الرابِطُ بِلا دَورٍ عَلى مُضيفِ الدَورِ <see cref="For"/> عَلى المُضيفِ نَفسِه — الجَرَسُ والتَنزيلُ والمُرَشِّحاتُ داخِلَ تَطبيقِ الدَورِ لَيسَت تَبديلاً لِلتَطبيق،
    /// وكانَت تَحمِلُ العُضوَ إلى مُضيفٍ جَرَّتُه فارِغَة. والعابِرُ لِدَورٍ مُسَمّى غَيرِ دَورِ المُضيفِ وَحدَه.</para>
    /// </summary>
    public static string Link(HostView view, string slug, string? role, string path = "")
    {
        if (!OnHostOf(view, slug)) return PathForm(slug, role, path);
        role = string.IsNullOrEmpty(role) ? null : role;
        // ورابِطٌ بِلا دَورٍ مَسارُه `r/{role}/…` (بِطاقاتُ الأَدوار: `LinkFor(slug, null, "r/vendor/login")`) رابِطُ ذلكَ الدَور.
        if (role is null && PathRole(path) is { } carried) (role, path) = carried;
        if (string.Equals(role, view.HostRole, StringComparison.Ordinal)) return For(view, slug, role, path);
        var scheme = string.IsNullOrEmpty(view.Scheme) ? "https" : view.Scheme;
        var p = path.TrimStart('/');
        if (role is not null && ACommerce.Kit.Roles.RoleHostNames.ByCatalogSlug.TryGetValue(role, out var label))
            return $"{scheme}://{slug}-{label}.{view.Apex}{view.Port}{HostTail(slug, role, bare: true, p)}";
        // ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ورابِطٌ بِلا دَورٍ عَلى مُضيفِ الدَورِ يَبقى عَلَيه** (<see cref="For"/>):
        // كانَ يُسَمّي مُضيفَ المَتجَرِ `{scheme}://{slug}.R/…` — والكَعكَةُ مَقصورَةٌ عَلى مُضيفِ الدَور، فَالجَرَسُ وتَنزيلُ بَياناتي ومُرَشِّحاتُ الاستِكشافِ
        // ورَقائِقُ التَصنيفِ تَحمِلُ العُضوَ إلى حَيثُ لا جَلسَةَ لَه. والحاقِنُ عَلى مُضيفِ الدَورِ يَخدِمُ قالِبَ صَفحَةِ الدَورِ بِدَورِه، وما سِواه (`/me/data/download`)
        // بِلا دَورٍ بِالكَعكَةِ العامَّةِ الَّتي يَكتُبُها دُخولُ الدَورِ عَلى المُضيفِ نَفسِه. والعابِرُ يَبقى لِدَورٍ مُسَمّى غَيرِ دَورِ المُضيف.
        if (view.HostRole is null || role is null) return For(view, slug, role, path);
        return $"{scheme}://{slug}.{view.Apex}{view.Port}{HostTail(slug, role, bare: false, p)}";
    }

    /// <summary>الدَورُ الَّذي يَحمِلُه مَسارٌ بِلا دَور (<c>r/{role}</c> أَو <c>r/{role}/…</c>) — ومَعَه ما بَعدَه.</summary>
    private static (string Role, string Tail)? PathRole(string path)
    {
        var parts = path.TrimStart('/').Split('/', 3);
        return parts.Length >= 2 && parts[0] == "r" && parts[1].Length > 0
            ? (parts[1], parts.Length == 3 ? parts[2] : "")
            : null;
    }

    /// <summary>
    /// <para><b>ذَيلُ العُنوانِ عَلى مُضيفٍ لِهذا المَتجَر</b>: <c>/{path}</c> (<paramref name="bare"/> — مُضيفُ المَتجَرِ بِلا دَور، أَو مُضيفُ الدَورِ ودَورُه) أَو
    /// <c>/r/{role}/{path}</c> (وأَقَلُّه <c>/</c>).</para>
    /// <para>‏(‏2026-11-07، W13-4·C2b·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) <b>والنَظيفُ لِما يَحقِنُه الحاقِنُ وَحدَه</b>: ذَيلٌ مَقطَعُه الأَوَّلُ يُخدَمُ مِن الجَذر
    /// (<see cref="ReservedTenantSlugs.ServedAtRoot"/> — <c>terms</c> مِنطَقَةُ مَنَصَّة) أَو هُوَ اسمُ المَتجَرِ نَفسُه يَبقى <see cref="PathForm"/> — والحاقِنُ يُمَرِّرُ
    /// <c>/{slug}/…</c> كَما هُوَ عَلى المُضيفَين. كانَ <c>/terms</c>: <c>TermsFilter</c> و<c>GatedPage</c> يُرسِلانِ مَن لَم يَقبَل شُروطَ المَتجَرِ إلى شُروطِ المَنَصَّةِ
    /// عَلى الجَذرِ بِلا جَلسَتِه، فَلا يَبلُغُ زِرَّ القَبول. ويَحرُسُه <c>StoreUrlRoundTripTests</c>: رابِطُ كُلِّ قالِبِ صَفحَةٍ يَعودُ إلَيها عَبرَ الحاقِنِ الحَقيقِيّ.</para>
    /// </summary>
    private static string HostTail(string slug, string? role, bool bare, string p)
    {
        if (bare && !Injected(slug, p)) return PathForm(slug, role, p);
        var tail = (bare ? $"/{p}" : $"/r/{role}/{p}").TrimEnd('/');
        return tail.Length == 0 ? "/" : tail;
    }

    /// <summary>أَيَحقِنُ الحاقِنُ مَتجَرَ المُضيفِ في <c>/{p}</c>؟ — المَقطَعُ الأَوَّلُ (قَبلَ <c>/</c> أَو <c>?</c> أَو <c>#</c>) فارِغٌ، أَو لا يُخدَمُ مِن الجَذرِ ولَيسَ
    /// اسمَ المَتجَر (شَرطا <c>TenantResolverMiddleware.PathWithSlug</c> بِقائِمَتِه نَفسِها).</summary>
    private static bool Injected(string slug, string p)
    {
        var end = p.IndexOfAny(['/', '?', '#']);
        var first = end < 0 ? p : p[..end];
        return first.Length == 0
               || (!ReservedTenantSlugs.ServedAtRoot(first) && !string.Equals(first, slug, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// <para><b>أَصلُ المَنَصَّةِ القانونِيُّ لِلعَناوينِ المُستَديمَة</b> (‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·3 — G14): ما يُسَجَّلُ عِندَ
    /// مُزَوِّدٍ ويَبقى هُناك (إشعارُ Moyasar، ووارِدُ OTO) يُبنى مِن <c>{scheme}://R{port}</c> مَتى هُيِّئَ النِطاق — لا مِن مُضيفِ الطَلَب. كانَ
    /// <c>OriginFrom(req)</c>: بائِعٌ رَبَطَ مِن مُضيفِ مَتجَرٍ أَو دَورٍ يُسَجِّلُ ذلكَ المُضيف، ومُضيفٌ يَتَغَيَّرُ اسمُه أَو يَخرُجُ مِن الخِدمَةِ يَقطَعُ الخَطّافَ صامِتاً.
    /// والمَسارُ يَحمِلُ المَتجَرَ (<c>/{slug}/payments/notify</c>)، والجَذرُ لا يُحَوِّلُه أَبَداً (لا مُضيفَ ولا مَرحَلَةَ خامِسَة).</para>
    /// <para><b>وبِلا نِطاقٍ مُهَيَّإٍ أَصلُ الطَلَبِ كَما كان</b> (<c>OriginFrom</c> بِحَرفِه) — فَلا يَتَغَيَّرُ شَيءٌ في وَضعِ المَسار.</para>
    /// </summary>
    public static string PlatformOrigin(HttpRequest req)
    {
        var view = ViewOf(req.HttpContext);
        return string.IsNullOrEmpty(view.Apex)
            ? ACommerce.Templates.Customer.Marketplace.Billing.PayPalOrderSurface.OriginFrom(req)
            : $"{(string.IsNullOrEmpty(view.Scheme) ? "https" : view.Scheme)}://{view.Apex}{view.Port}";
    }
}
