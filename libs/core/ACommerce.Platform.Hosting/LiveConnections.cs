using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;

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
/// </summary>
public static class LiveConnections
{
    /// <summary>مَركَزٌ حَيٌّ لِلمَنَصَّةِ عَلى <paramref name="pattern"/> — <c>MapHub&lt;THub&gt;</c> بِخِياراتِه الافتِراضِيَّة.</summary>
    public static HubEndpointConventionBuilder MapPlatformHub<THub>(this IEndpointRouteBuilder endpoints, string pattern) where THub : Hub
        => endpoints.MapHub<THub>(pattern);

    /// <summary>جُزُرُ <c>InteractiveServer</c> (مَركَزُ <c>/_blazor</c>) — <c>AddInteractiveServerRenderMode()</c> بِحَرفِه.</summary>
    public static RazorComponentsEndpointConventionBuilder AddPlatformInteractiveServer(this RazorComponentsEndpointConventionBuilder components)
        => components.AddInteractiveServerRenderMode();
}
