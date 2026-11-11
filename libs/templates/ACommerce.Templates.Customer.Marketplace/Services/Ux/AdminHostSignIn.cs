namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>
/// <para><b>دُخولٌ عَلى مُضيفِ الإدارَةِ لا يَنتَهي بِدَورِ الإدارَةِ يُرَدُّ ولا يُكتَب</b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-3؛ ADR-050 §١٤).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (عِندَ <c>61dab36</c> وعِندي): استِمارَةُ <c>{slug}-admin.R</c> تُرسِلُ <c>as=tenant_admin</c>، و<c>HeldAdminClaimOrNoneAsync</c> يُسقِطُ
/// الدَعوى لِمَن لا يَحمِلُ الدَورَ (7337b74) فَتُكتَبُ الكَعكَةُ العامَّةُ وَحدَها — والحاقِنُ عَلى ذلكَ المُضيفِ يَجعَلُ كُلَّ قالِبِ صَفحَةٍ <c>/r/tenant_admin/…</c> لا يَقرَأُ
/// إلّا كَعكَةَ الإدارَة (<c>AuthSession.ResolveCookieBase</c>). فَيَنجَحُ الدُخولُ وتَطلُبُ الصَفحَةُ التالِيَةُ الدُخول: <c>/me/name</c> ثُمَّ <c>/me/role</c> ثُمَّ
/// <c>/me</c>، كُلَّ مَرَّة (القاعِدَة ١٢ — مَدخَلٌ يَضُرّ). <b>والآن</b>: عَلى مُضيفِ الإدارَةِ لِهذا المَتجَرِ دُخولٌ لا يَحمِلُ دَورَ الإدارَةِ بَعدَ التَحَقُّقِ لا تُكتَبُ لَه
/// كَعكَةٌ ولا يُسَكَّنُ دَور، ويُحَوَّلُ إلى صَفحَةِ الدُخولِ بِرَمزِ <see cref="NotStoreAdmin"/> — رَفضٌ مِن القاموسِ بِمَخرَجٍ حَقيقيّ: المَتجَرُ عَلى مُضيفِه
/// (<see cref="StoreHome(HostView, string)"/>) حَيثُ تُقرَأُ الكَعكَةُ العامَّة، أَو دُخولٌ بِحِسابٍ آخَر. والمُديرُ المُخَزَّنُ ومُضيفُ المَتجَرِ ووَضعُ المَسارِ بِحَرفِهِم
/// (<c>LiveAdminHostSignInCharacterizationTests</c>).</para>
/// <para><b>ولِماذا لا يُحمَلُ الدُخولُ إلى المَتجَرِ تِلقائِيّاً</b>: الكَعكَةُ مَقصورَةٌ عَلى مُضيفِها (ADR-050 §٥)، والتَسليمُ بَينَ المُضيفاتِ «ب» لِلمالِك — فَالمَخرَجُ
/// رابِطٌ يُقرَأُ قَبلَ أَن يُنقَر، لا تَحويلٌ يَطلُبُ رَمزاً ثانِياً دونَ أَن يَقولَ لِماذا.</para>
/// </summary>
public static class AdminHostSignIn
{
    /// <summary>رَمزُ الرَفضِ في <c>?err=</c> عَلى صَفحَةِ الدُخول.</summary>
    public const string NotStoreAdmin = "not_store_admin";

    /// <summary>
    /// <para>وِجهَةُ الرَفضِ عَلى المُضيفِ نَفسِه، أَو <c>null</c>: الطَلَبُ عَلى مُضيفِ دَورِ الإدارَةِ لِهذا المَتجَرِ بِعَينِه (<see cref="StoreUrl.OnHostOf"/> و<see cref="HostView.HostRole"/>)
    /// و<paramref name="heldRole"/> — ما بَقِيَ مِن الدَعوى بَعدَ فَحصِ الحِيازَة — لَيسَ دَورَ الإدارَة. وما سِوى ذلك (مُضيفُ المَتجَر، ومُضيفُ دَورٍ آخَر، ووَضعُ المَسار) <c>null</c>.</para>
    /// </summary>
    public static string? Refusal(HostView view, string slug, string? heldRole)
    {
        if (!Applies(view, slug)) return null;
        if (Gates.SelfGrantPolicy.IsAdminSlug(heldRole)) return null;
        return StoreUrl.For(view, slug, null, $"login?err={NotStoreAdmin}");
    }

    /// <summary>
    /// <para><b>أَهذا الطَلَبُ عَلى مُضيفِ دَورِ الإدارَةِ لِهذا المَتجَرِ بِعَينِه؟</b> (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·4 — C2z-PATHS-R3-03) —
    /// شَرطُ <see cref="Refusal"/> بِحَرفِه، ويَسأَلُه <c>Login.razor</c> قَبلَ أَن يَرسُمَ البِطاقَة: كانَت تُرسَمُ لِكُلِّ <c>?err=not_store_admin</c> — في وَضعِ المَسارِ
    /// وعَلى مُضيفِ المَتجَرِ ومُضيفِ دَورٍ آخَر (مَقيسٌ عِندَ <c>ad0a770</c>) حَيثُ لا يَصدُرُ الرَمزُ أَبَداً والنَصُّ كاذِب، ورابِطٌ مَصنوعٌ يُري الزائِرَ رَفضاً بَدَلَ الاستِمارَة.</para>
    /// </summary>
    public static bool Applies(HostView view, string slug)
        => StoreUrl.OnHostOf(view, slug) && Gates.SelfGrantPolicy.IsAdminSlug(view.HostRole);

    /// <summary><see cref="Applies(HostView, string)"/> بِالطَلَبِ الحالِيِّ (<c>AuthSession.HttpAccessor</c>) — لِصَفحَةِ الدُخول.</summary>
    public static bool Applies(string slug) => Applies(StoreUrl.ViewOf(AuthSession.HttpAccessor?.HttpContext), slug);

    /// <summary>
    /// <para><b>المَتجَرُ عَلى مُضيفِه — مَخرَجُ الرَفض</b>: مِن مُضيفِ دَورٍ لِهذا المَتجَرِ <c>{scheme}://{slug}.R{port}/</c> (حَيثُ تُقرَأُ الكَعكَةُ العامَّة)، وما سِوى ذلك
    /// <see cref="StoreUrl.Open(HostView, string)"/> بِحَرفِه.</para>
    /// </summary>
    public static string StoreHome(HostView view, string slug)
        => StoreUrl.OnHostOf(view, slug) && view.HostRole is not null
            ? $"{(string.IsNullOrEmpty(view.Scheme) ? "https" : view.Scheme)}://{slug}.{view.Apex}{view.Port}/"
            : StoreUrl.Open(view, slug);

    /// <summary><see cref="StoreHome(HostView, string)"/> بِالطَلَبِ الحالِيِّ (<c>AuthSession.HttpAccessor</c>) — لِصَفحَةِ الدُخول.</summary>
    public static string StoreHome(string slug) => StoreHome(StoreUrl.ViewOf(AuthSession.HttpAccessor?.HttpContext), slug);
}
