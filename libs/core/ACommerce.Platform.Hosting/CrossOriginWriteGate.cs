using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ACommerce.Platform.Hosting;

/// <summary>خَرقُ فَحصِ <c>Origin</c> — رَمزٌ ثابِتٌ ورِسالَةٌ لِلسِجِلّ (شَكلُ المُصادِقِ المُوَحَّد، القاعِدَة ٤).</summary>
public sealed record CrossOriginRefusal(string Code, string MessageAr);

/// <summary>
/// <para><b>طَلَبُ الكِتابَةِ مِن أَصلٍ آخَرَ يُرَدّ — عِلاجُ «<c>SameSite=Lax</c> الحارِسُ الوَحيد»</b> (‏2026-11-07، ADR-050 §٦ · W13-4·C2c·1؛ A2 §9 البَند ٥؛
/// تَوصيفُه <c>CrossOriginWriteCharacterizationTests</c>). كانَت كُلُّ نُقطَةِ كِتابَةٍ (123 في minimal API وواحِدَةٌ في Wolverine) تَتَّكِلُ عَلى أَنّ المُتَصَفِّحَ
/// لا يُرسِلُ كوكيَّ <c>Lax</c> مَعَ <c>POST</c> مِن مَوقِعٍ آخَر — <b>والمُضيفاتُ الشَقيقَةُ تَحتَ النِطاقِ مَوقِعٌ واحِد</b> (same-site): صَفحَةٌ عَلى
/// <c>ejar.R</c> تُرسِلُ استِمارَةً إلى <c>ashare.R</c> فَتَحمِلُ كوكيَّ الضَحِيَّة. وكُلُّ مُتَصَفِّحٍ حَديثٍ يُرسِلُ <c>Origin</c> مَعَ كُلِّ طَلَبٍ غَيرِ آمِن.</para>
///
/// <para><b>القاعِدَة</b> — بِأَوَّلِ خَرقٍ (‏ADR-038): فِعلٌ آمِن (<c>GET</c>/<c>HEAD</c>/<c>OPTIONS</c>/<c>TRACE</c>) يَمُرّ؛ وبِلا رَأسِ <c>Origin</c> يَمُرُّ
/// إلى الحُرّاسِ القائِمين (خَطّافاتُ الخَوادِمِ وأَدَواتُ سَطرِ الأَوامِرِ لا تُرسِلُه)؛ ونِقاطُ المُزَوِّدينَ <see cref="ProviderCallbacks"/> تَمُرُّ بِأَسمائِها؛
/// و<c>Origin</c> لا يُقرَأُ أَصلاً (<c>null</c> المُعتِمُ، أَو بِمَسارٍ، أَو بِمُخَطَّطٍ غَيرِ http(s)، أَو قيمَتان) ← <see cref="OpaqueOrigin"/>؛ وأَصلٌ
/// غَيرُ أَصلِ الطَلَبِ نَفسِه (المُخَطَّطُ والمُضيفُ والمَنفَذ — <c>Request.Scheme</c> و<c>Request.Host</c> بَعدَ <c>UseForwardedHeaders</c>) ← <see cref="ForeignOrigin"/>.</para>
///
/// <para><b>لا يَقرَأُ حالَةً مَخزونَة</b> (القاعِدَة ٧) — رُؤوسُ الطَلَبِ وَحدَها، فَهُوَ مِن صِنفِ الحاقِنِ ويُرَكَّبُ بِجِوارِه في
/// <see cref="HostingExtensions.UsePlatformRequestFront"/> (القاعِدَة ٨: يُصلِحُ السِلسِلَةَ القائِمَةَ ولا يَبني أُنبوباً رابِعاً). ويُجيبُ 403 بِرَمزِ الحالَةِ
/// وَحدَه (لا <c>Results.Forbid()</c> — دَرسُ القاعِدَة ٧)، وتَكتُبُ <c>UsePlatformErrorPages</c> صَفحَتَه.</para>
/// </summary>
public static class CrossOriginWriteGate
{
    /// <summary><c>Origin</c> حاضِرٌ ولا يُقرَأُ أَصلاً: <c>null</c> (أَصلٌ مُعتِم — إطارٌ مَعزول، أَو تَحويلٌ عابِرٌ لِلأُصول)، أَو بِمَسار، أَو بِمُخَطَّطٍ غَيرِ http(s)، أَو قيمَتان.</summary>
    public const string OpaqueOrigin = "origin_opaque";

    /// <summary><c>Origin</c> أَصلٌ صالِحٌ غَيرُ أَصلِ الطَلَبِ نَفسِه — مُخَطَّطاً أَو مُضيفاً أَو مَنفَذاً.</summary>
    public const string ForeignOrigin = "origin_foreign";

    /// <summary>
    /// <para><b>نِقاطُ المُزَوِّدينَ — تُستَثنى بِأَسمائِها</b>: يُناديها خادِمُ المُزَوِّد (PayPal، Paddle، بَوّابَةُ المَتجَر، OTO) بِسِرِّه أَو بَصمَتِه لا مُتَصَفِّحٌ
    /// بِكوكي، فَلا كوكيَّ يُزَوَّرُ عَلَيها — وعَميلُ مُزَوِّدٍ يُضيفُ <c>Origin</c> يَوماً لا يَقطَعُ مالاً واقِعاً. وكُلٌّ <c>MapPost</c> واحِدٌ لا يَقرَأُ جَلسَة
    /// (مُثَبَّتٌ في التَوصيف)، والقائِمَةُ لا تَزيدُ إلّا بِقَرارٍ مَرئِيّ: <c>CrossOriginWriteGateTests</c> يَحمَرُّ عَلى اسمٍ بِلا نُقطَةٍ أَو بِنُقطَتَين.</para>
    /// <para><b>وصَفَحاتُ عَودَةِ الدَفعِ لَيسَت هُنا</b>: <c>/billing/paypal/return</c> و<c>/billing/paypal/cancel</c> و<c>/billing/paddle/return</c> و<c>/{slug}/pay/return</c>
    /// صَفَحاتُ <c>@page</c> يَعودُ إلَيها المُتَصَفِّحُ بِـ<c>GET</c> (مَقيسٌ في التَوصيف) — فَالفِعلُ الآمِنُ يُمَرِّرُها، واستِثناءُ ما لا يُكتَبُ إلَيه اسمٌ بِلا مُستَهلِك (القاعِدَة ١).</para>
    /// </summary>
    public static IReadOnlyList<string> ProviderCallbacks { get; } =
    [
        "/api/billing/paypal",
        "/api/billing/paddle",
        "/{slug}/payments/notify",
        "/{slug}/shipping/oto/order-status",
    ];

    private static readonly string[][] CallbackSegments =
        ProviderCallbacks.Select(t => t.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray();

    /// <summary>فِعلٌ لا يُبَدِّلُ حالَةً بِعَقدِ HTTP — لا يُسأَلُ عَن أَصلِه.</summary>
    public static bool IsSafeMethod(string? method)
        => method is not null && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));

    /// <summary>أَيُطابِقُ المَسارُ (بَعدَ الحَقن) نُقطَةً مِن <see cref="ProviderCallbacks"/>؟ المَقطَعُ الحَرفِيُّ بِلا حالَةِ أَحرُفٍ كَما يُطابِقُ التَوجيه، و<c>{slug}</c> أَيُّ مَقطَعٍ غَيرِ فارِغ.</summary>
    public static bool IsProviderCallback(string? path)
    {
        var segments = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var template in CallbackSegments)
        {
            if (template.Length != segments.Length) continue;
            var all = true;
            for (var i = 0; i < template.Length && all; i++)
                all = template[i].StartsWith('{') || string.Equals(template[i], segments[i], StringComparison.OrdinalIgnoreCase);
            if (all) return true;
        }
        return false;
    }

    /// <summary>
    /// <para>أَوَّلُ خَرقٍ أَو <c>null</c>. <paramref name="origin"/> <c>null</c> = لا رَأس؛ <paramref name="scheme"/> و<paramref name="host"/> أَصلُ الطَلَبِ نَفسِه
    /// (<c>Request.Scheme</c> و<c>Request.Host.Value</c>)؛ و<paramref name="path"/> المَسارُ بَعدَ الحَقن.</para>
    /// </summary>
    public static CrossOriginRefusal? Validate(string? method, string? origin, string? scheme, string? host, string? path)
        => Validate(method, origin, scheme, host, path, isUpgrade: false);

    /// <summary>
    /// <para><b>…ومُصافَحَةُ WebSocket تُسأَلُ كَالكِتابَة</b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · SEC-C2c-2 وSEC-C2c-R2-1 وSEC-C2c-R3-2 وC2c-P2-04 وC2c-P3-02؛
    /// ADR-050 §١٥). المُصافَحَةُ <c>GET</c> فَكانَت تَمُرُّ، والمُتَصَفِّحُ لا يُخضِعُها لِـCORS ويُرسِلُ مَعَها كَعكَةَ <c>Lax</c> بَينَ مُضيفاتِ المَوقِعِ الواحِد — فَصَفحَةٌ
    /// عَلى مُضيفٍ شَقيقٍ فَتَحَت <c>/realtime</c> بِجَلسَةِ الضَحِيَّةِ وانضَمَّت إلى مَجموعَتِه (101 ثُمَّ <c>{}</c>، و<c>unread_changed</c> يَصِلُها — مَقيسٌ عِندَ المُراجِعين)،
    /// وطَلَبُ <c>negotiate</c> المَردودُ لا يُسأَلُ إن تَخَطّاه العَميل. الآن بِـ<paramref name="isUpgrade"/> (<see cref="IsWebSocketUpgrade"/>) يُحكَمُ عَلى <c>Origin</c>
    /// بِالقاعِدَةِ نَفسِها: حاضِرٌ غَيرُ أَصلِ الطَلَبِ ← <see cref="ForeignOrigin"/>، ولا يُقرَأ ← <see cref="OpaqueOrigin"/>، وغائِبٌ يَمُرّ (المُتَصَفِّحُ يُرسِلُه دائِماً
    /// مَعَ المُصافَحَة؛ وعَميلٌ لَيسَ مُتَصَفِّحاً لا يَحمِلُ كَعكَةَ أَحَد). وبِلا <paramref name="isUpgrade"/> القَرارُ بِحَرفِه.</para>
    /// </summary>
    public static CrossOriginRefusal? Validate(string? method, string? origin, string? scheme, string? host, string? path, bool isUpgrade)
    {
        if (IsSafeMethod(method) && !isUpgrade) return null;
        if (origin is null) return null;
        if (IsProviderCallback(path)) return null;
        if (!TryReadOrigin(origin, out var o))
            return isUpgrade
                ? new(OpaqueOrigin, "اتِّصالٌ حَيٌّ بِأَصلٍ لا يُقرَأ — لا يُقبَل.")
                : new(OpaqueOrigin, "طَلَبُ كِتابَةٍ بِأَصلٍ لا يُقرَأ — لا يُقبَل.");
        if (IsSameOrigin(o, scheme, host)) return null;
        return isUpgrade
            ? new(ForeignOrigin, "اتِّصالٌ حَيٌّ مِن أَصلٍ غَيرِ أَصلِ الصَفحَة — لا يُقبَل.")
            : new(ForeignOrigin, "طَلَبُ كِتابَةٍ مِن أَصلٍ غَيرِ أَصلِ الصَفحَة — لا يُقبَل.");
    }

    /// <summary><b>أَهذا طَلَبُ مُصافَحَةِ WebSocket؟</b> — <c>GET</c> ورَأسُ <c>Upgrade</c> يَحمِلُ الرَمزَ <c>websocket</c> (بِلا حالَةِ أَحرُف، بَينَ رُموزٍ مَفصولَةٍ
    /// بِفاصِلَة — RFC 6455 §٤·١). ومُصافَحَةُ HTTP/2 (‏<c>CONNECT</c>) لَيسَت فِعلاً آمِناً فَيَسأَلُها الفَحصُ أَصلاً.</summary>
    public static bool IsWebSocketUpgrade(string? method, string? upgrade)
        => method is not null && HttpMethods.IsGet(method) && !string.IsNullOrEmpty(upgrade)
           && upgrade.Split(',').Any(t => string.Equals(t.Trim(), "websocket", StringComparison.OrdinalIgnoreCase));

    public static bool IsAllowed(string? method, string? origin, string? scheme, string? host, string? path)
        => Validate(method, origin, scheme, host, path) is null;

    /// <summary>تَسَلسُلُ الأَصلِ كَما يَكتُبُه المُتَصَفِّح: <c>scheme://host[:port]</c> — بِلا مَسارٍ ولا شَرطَةٍ أَخيرَةٍ ولا استِعلامٍ ولا هُوِيَّة، والمُخَطَّطُ http(s).</summary>
    private static bool TryReadOrigin(string origin, out Uri uri)
    {
        uri = null!;
        if (origin.Length == 0 || origin.EndsWith('/') || origin.Contains(',')) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (parsed.AbsolutePath != "/" || parsed.Query.Length > 0 || parsed.Fragment.Length > 0 || parsed.UserInfo.Length > 0) return false;
        uri = parsed;
        return true;
    }

    private static bool IsSameOrigin(Uri origin, string? scheme, string? host)
    {
        if (string.IsNullOrEmpty(scheme) || string.IsNullOrEmpty(host)) return false;
        if (!string.Equals(origin.Scheme, scheme, StringComparison.OrdinalIgnoreCase)) return false;
        var request = new HostString(host);
        if (!string.Equals(origin.Host, request.Host, StringComparison.OrdinalIgnoreCase)) return false;
        var requestPort = request.Port ?? (string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return origin.Port == requestPort;
    }
}

/// <summary>يُنَفِّذُ <see cref="CrossOriginWriteGate"/> ولا يُقَرِّر — 403 بِرَمزِ الحالَةِ وسَطرٌ في السِجِلّ.</summary>
public sealed class CrossOriginWriteMiddleware(RequestDelegate next, ILogger<CrossOriginWriteMiddleware> log)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var origin = req.Headers.Origin.Count == 0 ? null : req.Headers.Origin.ToString();
        var refusal = CrossOriginWriteGate.Validate(req.Method, origin, req.Scheme, req.Host.Value, req.Path.Value,
                                                    CrossOriginWriteGate.IsWebSocketUpgrade(req.Method, req.Headers.Upgrade.ToString()));
        if (refusal is null) return next(ctx);
        log.LogWarning("[origin] {Code} {Method} {Host}{Path} Origin={Origin}", refusal.Code, req.Method, req.Host.Value, req.Path.Value, origin);
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public static class CrossOriginWriteExtensions
{
    /// <summary>يُرَكَّبُ في <see cref="HostingExtensions.UsePlatformRequestFront"/> بَعدَ الحاقِن — فَيَرى المَسارَ بِشَكلِ القالِب (<c>/{slug}/…</c>) عَلى كُلِّ مُضيف.</summary>
    public static IApplicationBuilder UseCrossOriginWriteGate(this IApplicationBuilder app)
        => app.UseMiddleware<CrossOriginWriteMiddleware>();
}
