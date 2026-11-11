using ACommerce.Platform.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>الاتِّصالانِ الحَيّانِ بِاسمِهِما — <c>/realtime</c> و<c>/_blazor</c></b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الأُولى · SEC-C2z-R1-1؛ ADR-050 §١٥).
/// كِلاهُما مَركَزُ SignalR يَقرَأُ كَعكَةَ الجَلسَةِ مِن طَلَبِ الاتِّصال (<c>RealtimeHub.OnConnectedAsync</c>، وصَفحَتا الوَكيلِ التَفاعُلِيَّتانِ عَبرَ
/// <c>IHttpContextAccessor</c>)، و<c>Program.cs</c> كانَ يُرَكِّبُهُما سَطرَينِ بِلا اسم: <c>MapHub&lt;RealtimeHub&gt;("/realtime")</c> و
/// <c>MapRazorComponents&lt;App&gt;().AddInteractiveServerRenderMode()</c>.</para>
///
/// <para><b>ولِماذا دالَّتانِ بِاسمٍ</b> (الدَرسُ نَفسُه الَّذي سَمّى <see cref="HostingExtensions.UsePlatformRequestFront"/>): تَوصيفُ ما يَنجو يَقيسُ التَركيبَ بِهذا
/// الاسمِ عَلى مُضيفٍ مُصَغَّرٍ حَيّ، فَيَبقى مِلَفُّه بِبَصمَتِه قَبلَ التَبديلِ وبَعدَه — ولا يُقاسُ تَركيبٌ مَنسوخٌ يَنجَرِفُ عَن هذا.
/// <c>RealtimeTransportCharacterizationTests</c> يَحمَرُّ إن عادَ <c>Program.cs</c> يُرَكِّبُهُما بِغَيرِ هذَين الاسمَين.</para>
///
/// <para><b>‏(‏2026-11-11، التَبديل — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) WebSocket النَقلُ الوَحيد</b>. فَحصُ <c>Origin</c> (<see cref="CrossOriginWriteGate"/>) يَسأَلُ
/// المُصافَحَةَ وَحدَها (<see cref="CrossOriginWriteGate.IsWebSocketUpgrade"/>)، والنَواقِلُ الافتِراضِيَّةُ ثَلاثَة: <b>الاستِطلاعُ الطَويلُ</b> (<c>GET ?id=…</c> بِلا
/// تَرقِيَة) و<b>Server-Sent Events</b> يَمُرّانِ بِالقاعِدَةِ «فِعلٌ آمِن». مَقيسٌ عِندَ المُراجِع (<c>61dab36</c>): خادِمُ المُهاجِمِ يُفاوِضُ بِلا أَصلٍ ولا كَعكَة، وصَفحَتُه
/// عَلى مُضيفٍ شَقيقٍ تُطلِقُ الاستِطلاعَ الأَوَّلَ بِكَعكَةِ الضَحِيَّة (<c>fetch</c> بِـ<c>credentials</c> أَو <c>no-cors</c> — <c>Lax</c> يَركَبُ بَينَ مُضيفاتِ
/// المَوقِعِ الواحِد)، ويُرسِلُ هُوَ المُصافَحَة — فَيَقرَأُ <c>OnConnectedAsync</c> كَعكَةَ الضَحِيَّةِ مِن نُسخَةِ ذلكَ الاستِطلاعِ ويَنضَمُّ الاتِّصالُ إلى مَجموعَتِه.
/// <b>والآن</b> كُلٌّ مِنهُما يَعرِضُ WebSockets وَحدَه في <c>negotiate</c>، والاستِطلاعُ وSSE ← 404 مِن الإطارِ قَبلَ المَركَز، فَلا طَريقَ إلَيه إلّا مُصافَحَةٌ
/// يُرسِلُ المُتَصَفِّحُ مَعَها <c>Origin</c> دائِماً — ويَحكُمُ عَلَيه الفَحصُ القائِم (القاعِدَة ٨: لا أُنبوبَ رابِع).</para>
/// <para><b>والعَميلانِ بِلا تَغيير</b> (<c>App.razor</c> و<c>realtime-nav.js</c> يُفاوِضانِ فَيَأخُذانِ ما يَعرِضُه الخادِم): بايتاتُ كُلِّ صَفحَةٍ كَما كانَت. ومَن يَحجُبُ
/// WebSocket في طَريقِه يَفقِدُ التَحديثَ الحَيَّ لا العَدّادات — السكربتُ يَسقُطُ إلى الاستِطلاعِ كُلَّ ٣٠ ثانِيَة (<c>conn.start().catch</c>).</para>
/// <para><b>‏(‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·3 — SEC-C2z-R2-3 وC2z-PATHS-R2-05 وC2z-PATHS-R3-02 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والتَضييقُ
/// حينَ يُهَيَّأُ النِطاقُ وَحدَه</b> (<see cref="HostsConfigured"/>): الهُجومُ يَحتاجُ صَفحَةً عَلى مُضيفٍ <b>مِن المَوقِعِ نَفسِه</b> تُرسِلُ كَعكَةَ الضَحِيَّة —
/// وكَعكَةُ الجَلسَةِ <c>SameSite=Lax</c> (مَقيسٌ حَيّاً في وَضعِ المَسار: <c>Set-Cookie: .acommerce.auth.ejar=…; samesite=lax</c>، و<c>__Host-…; secure; samesite=lax</c>
/// عَلى HTTPS)، و<c>Lax</c> لا يُرسَلُ مَعَ <c>fetch</c>/XHR مِن مَوقِعٍ آخَر. وبِلا نِطاقٍ لا مُضيفَ شَقيقاً: المَنَصَّةُ أَصلٌ واحِد، ومُضيفُها عَلى HF
/// (<c>&lt;space&gt;.hf.space</c>) — و<c>hf.space</c> في قائِمَةِ اللَواحِقِ العامَّة (publicsuffix.org، جُلِبَت ‏2026-11-11) فَكُلُّ Space آخَرُ مَوقِعٌ آخَر. فَكانَ
/// التَضييقُ في وَضعِ المَسارِ يُسقِطُ الاستِطلاعَ وSSE بِلا ثَغرَةٍ يُغلِقُها — وجَزيرَتا الوَكيلِ التَفاعُلِيَّتانِ (<c>/admin/agent</c> و<c>/studio/agent</c>) لا
/// تَبدَآنِ خَلفَ ما يَحجُبُ WebSocket. الآن: <b>بِنِطاقٍ مُهَيَّإ</b> WebSockets وَحدَه عَلى المَركَزَين (الجَذرُ والمُضيفاتُ كُلُّها)، <b>وبِلاه</b> نَواقِلُ الإطارِ
/// الثَلاثَة كَما قَبلَ <c>e8edcd6</c>.</para>
/// </summary>
public static class LiveConnections
{
    /// <summary>النَقلُ الوَحيدُ لِلمَركَزَين — مُصافَحَتُه وَحدَها تَحمِلُ <c>Origin</c> الَّذي يَسأَلُه <see cref="CrossOriginWriteGate"/>.</summary>
    public const HttpTransportType Transports = HttpTransportType.WebSockets;

    /// <summary>مَركَزٌ حَيٌّ لِلمَنَصَّةِ عَلى <paramref name="pattern"/> — <c>MapHub&lt;THub&gt;</c> بِنَقلِ <see cref="Transports"/> وَحدَه.</summary>
    /// <remarks>‏(‏2026-11-11، W13-4·C2f·3 — يُلحَق) بِنِطاقٍ مُهَيَّإٍ وَحدَه (<see cref="HostsConfigured"/>)؛ وبِلاه نَواقِلُ الإطارِ كَما هي.</remarks>
    public static HubEndpointConventionBuilder MapPlatformHub<THub>(this IEndpointRouteBuilder endpoints, string pattern) where THub : Hub
        => HostsConfigured(endpoints.ServiceProvider) ? endpoints.MapHub<THub>(pattern, WebSocketsOnly) : endpoints.MapHub<THub>(pattern);

    /// <summary><b>أَمُهَيَّأٌ نِطاقٌ لِلمُضيفات؟</b> — <see cref="SubdomainTenancyOptions.Apex"/> المُسَجَّلُ في الحاوِيَة؛ وغائِبٌ أَو فارِغٌ = وَضعُ المَسار (‏2026-11-11، W13-4·C2f·3).
    /// هُوَ وَحدَه ما يَصنَعُ مُضيفاً شَقيقاً مِن المَوقِعِ نَفسِه.</summary>
    public static bool HostsConfigured(IServiceProvider? services)
        => services?.GetService<SubdomainTenancyOptions>()?.Apex is not null;

    /// <summary>جُزُرُ <c>InteractiveServer</c> (مَركَزُ <c>/_blazor</c>) — <c>AddInteractiveServerRenderMode()</c> ومَعَه عُرفُ <see cref="WebSocketsOnly(EndpointBuilder)"/>:
    /// الإطارُ لا يَعرِضُ نَواقِلَ <c>/_blazor</c> في <c>ServerComponentsEndpointOptions</c>، فَيُضَيَّقُ خِيارُ اتِّصالِه حَيثُ يَحمِلُه.</summary>
    public static RazorComponentsEndpointConventionBuilder AddPlatformInteractiveServer(this RazorComponentsEndpointConventionBuilder components)
    {
        components.AddInteractiveServerRenderMode();
        // ‏(‏2026-11-11، W13-4·C2f·3 — يُلحَق) بِنِطاقٍ مُهَيَّإٍ وَحدَه — والحاوِيَةُ مِن الطَلَب (`WebSocketsOnlyWithHosts`).
        components.Add(WebSocketsOnlyWithHosts);
        return components;
    }

    /// <summary>
    /// <para><b>العُرفُ نَفسُه بِنِطاقٍ مُهَيَّإٍ وَحدَه</b> (‏2026-11-11، W13-4·C2f·3): نُقَطُ <c>/_blazor</c> يَبنيها مَصدَرُ مُكَوِّناتِ Razor بِلا حاوِيَة
    /// (<c>EndpointBuilder.ApplicationServices</c> فارِغٌ هُناك — مَقيسٌ بِمُسبارٍ عَلى المُضيفِ المُصَغَّر)، فَلا يُعرَفُ الوَضعُ وَقتَ البِناء. فَيُلَفُّ مُعالِجُ كُلِّ
    /// نُقطَةٍ تَحمِلُ <see cref="HttpConnectionDispatcherOptions"/> (<c>negotiate</c> والاتِّصال — الكائِنُ نَفسُه): يَسأَلُ <see cref="HostsConfigured"/> حاوِيَةَ الطَلَبِ ثُمَّ
    /// يُضَيِّقُ قَبلَ أَن يَقرَأَ المُوَزِّعُ النَواقِل. والوَضعُ ثابِتٌ لِلعَمَلِيَّة، فَالتَضييقُ واحِدٌ في كُلِّ طَلَب؛ وبِلا نِطاقٍ لا يُمَسُّ شَيء.</para>
    /// </summary>
    public static void WebSocketsOnlyWithHosts(EndpointBuilder endpoint)
    {
        var options = endpoint.Metadata.OfType<HttpConnectionDispatcherOptions>().ToArray();
        if (options.Length == 0 || endpoint.RequestDelegate is not { } inner) return;
        endpoint.RequestDelegate = ctx =>
        {
            if (HostsConfigured(ctx.RequestServices))
                foreach (var o in options) WebSocketsOnly(o);
            return inner(ctx);
        };
    }

    /// <summary>يُضَيِّقُ نَواقِلَ اتِّصالٍ إلى <see cref="Transports"/>.</summary>
    public static void WebSocketsOnly(HttpConnectionDispatcherOptions options) => options.Transports = Transports;

    /// <summary>
    /// <para><b>عُرفٌ عَلى نُقطَةٍ</b>: كُلُّ <see cref="HttpConnectionDispatcherOptions"/> في بَياناتِها الوَصفِيَّةِ يُضَيَّق. <c>MapConnections</c> يَضَعُ خِيارَ الاتِّصالِ
    /// في بَياناتِ نُقطَةِ <c>negotiate</c> وهُوَ الكائِنُ نَفسُه الَّذي يَقرَؤُه المُوَزِّعُ في نُقطَةِ الاتِّصال — فَتَضييقُه هُناكَ يَحكُمُ الاثنَتَين (مَقيسٌ في
    /// <c>RealtimeTransportTests</c> عَلى <c>MapHub</c> بِلا خِيار، وفي المَجموعَةِ ١٨ مِن <c>prove-store-hosts.sh</c> عَلى <c>/_blazor</c> الحَيّ). ونُقطَةٌ بِلا خِيارِ
    /// اتِّصالٍ (صَفَحاتُ Razor) لا يَمَسُّها.</para>
    /// </summary>
    public static void WebSocketsOnly(EndpointBuilder endpoint)
    {
        foreach (var options in endpoint.Metadata.OfType<HttpConnectionDispatcherOptions>())
            WebSocketsOnly(options);
    }
}
