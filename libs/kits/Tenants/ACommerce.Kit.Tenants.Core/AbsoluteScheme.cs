namespace ACommerce.Kit.Tenants;

/// <summary>
/// <para><b>مُخَطَّطُ كُلِّ عُنوانٍ مُطلَقٍ يُبنى مِن الطَلَب — <c>http</c> إن كانَه (بِلا حالَةِ أَحرُف)، وما سِواه <c>https</c></b> (‏2026-11-11، الإغلاقُ الأَخيرُ
/// لِـW13-4 · W13-4·C2f·2 — SEC-C2z-R3-2 وما بَقِيَ مِن SEC-C2z-R1-4 وC2z-PATHS-R2-02). <c>X-Forwarded-Proto</c> يُصَدَّقُ مِن أَيِّ نِدٍّ (<c>KnownProxies</c>
/// فارِغ — ADR-023 §٣)، فَالمُخَطَّطُ في <c>Request.Scheme</c> قيمَةٌ يَختارُها الطالِب: <c>javascript</c> كانَ يَصيرُ <c>javascript://…</c> في الـcanonical
/// وog:url وog:image وtwitter:image (<c>App.razor</c>) وأَصلِ الطَلَبِ (عَودَةُ PayPal، وأَصلُ الخَطّافِ بِلا نِطاق) وخَريطَةِ robots — مَقيسٌ حَيّاً في الوَضعَين.
/// قائِمَةُ سَماحٍ لا تَنظيف: <c>http</c> يَبقى <c>http</c> (التَطويرُ المَحَلِّيّ) و<c>https</c> كَما هُوَ، فَمُخرَجُهُما بايتاً كَما كان.</para>
/// <para><b>شَرطُ الاستِخراجِ (القاعِدَة ١) مُتَحَقِّقٌ ويُكتَبُ هُنا</b>: كانَت القاعِدَةُ نُسخَتَين خاصَّتَين — <c>TenantResolverMiddleware.SchemeOf</c> (تَحويلُ
/// مَنطِقَةِ المَنَصَّةِ عَلى المُضيف، ‏<c>023b686</c>) و<c>StoreUrl.SchemeOf</c> (بُناةُ المُضيفات، ‏<c>3ed58f5</c>) — وظَهَرَ مُستَهلِكٌ ثالِثٌ في مَشروعٍ ثالِث:
/// <c>SeoHandlers</c> في <c>ACommerce.Kit.Tenants.Server</c> لا يَرى أَيّاً مِنهُما. فَالقاعِدَةُ هُنا مَرَّةً في العُدَّةِ الَّتي تَرجِعُ إلَيها المَشاريعُ الثَلاثَة،
/// والنُسخَتانِ الخاصَّتانِ تُناديانِها بِاسمَيهِما (ماسِحُ <c>HostSchemeTests</c> يَقرَأُ <c>SchemeOf(ctx.Request.Scheme)</c>)، و<c>App.razor</c> و<c>OriginFrom</c>
/// يُناديانِها مُباشَرَةً. وماسِحُ <c>SchemeRobustnessTests</c> يُحمِّرُ قارِئاً جَديداً لِـ<c>Request.Scheme</c> يَبني عُنواناً بِحَرفِه.</para>
/// </summary>
public static class AbsoluteScheme
{
    /// <summary>المُخَطَّطُ لِعُنوانٍ مُطلَق: <c>http</c> إن كانَه (بِلا حالَةِ أَحرُف)، وإلّا <c>https</c> (والفارِغُ <c>https</c>).</summary>
    public static string Of(string? scheme)
        => string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
}
