using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace ACommerce.Kit.Realtime.Server;

/// <summary>
/// SignalR hub بَسيط. كلّ مُستَخدِم يَدخُل في group باسم "tenant:user".
///
/// <para><b>الجَلسَةُ مِن الكوكي لا مِن الرابِط</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-4، حاجِبَة): كانَ الهُبُّ يَقرَأُ الرَمزَ مِن
/// <c>?access_token=</c> ثُمَّ <c>Authorization: Bearer</c> ثُمَّ <c>?token=</c> — فَطَبَعَته الصَفحَةُ في سِكربتٍ (والكوكي <c>HttpOnly</c> لِيَمنَعَ
/// ذلك بِعَينِه) وأَرسَلَته في سِلسِلَةِ الاستِعلام، فَكَتَبَه سَطرُ <c>Request starting …?token=…</c> في سِجِلِّ الإنتاج: رَمزٌ لِثَلاثينَ يَوماً بِلا
/// إبطال (د28). صارَ يَقرَأُ كوكي الجَلسَةِ نَفسَه — المُتَصَفِّحُ يُرسِلُه في <c>negotiate</c> وفي مُصافَحَةِ WebSocket لِأَنّ الأَصلَ واحِد — بِالقَرارِ
/// الَّذي يَقرَأُ بِه كُلُّ بابٍ آخَر (<c>AuthSession.ResolveToken</c>)، والمَتجَرُ وَحدَه في الرابِط (<c>?slug=</c>، لَيسَ سِرّاً). ولا قِراءَةَ
/// لِلرَمزِ مِن الرابِطِ ولا مِن رَأسٍ بَعدَ اليَوم.</para>
///
/// <para><b>ولِماذا مُفَوَّضٌ لا مَقروءٌ هُنا</b>: اسمُ الكوكي وحَلُّ الدَورِ قَرارُ القالِبِ (<c>AuthSession.ResolveCookieBase</c>)، والكيتُ لا
/// يُرجِعُ إلى القالِب. فَـ<see cref="SessionCookieReader"/> مَنفَذٌ بِمُستَهلِكٍ واحِدٍ (هذا الهُبّ) ومُزَوِّدٍ واحِد (<c>AuthSession.ResolveToken</c>
/// يُسَجِّلُه <c>AddCustomerMarketplaceTemplate</c>)؛ **شَرطُ استِخراجِه إلى ما هُوَ أَعَمّ**: مُستَهلِكٌ ثانٍ في وَقتِ التَشغيلِ يَحتاجُ قِراءَةَ
/// الجَلسَةِ خارِجَ القالِب (القاعِدَة ١). وبِلا تَسجيلٍ لا يُبنى الهُبُّ فَلا اتِّصال — يُغلَقُ ولا يُفتَح.</para>
/// </summary>
public sealed class RealtimeHub(RealtimeHub.SessionCookieReader readSession) : Hub
{
    /// <summary>رَمزُ الجَلسَةِ الصالِحُ لِهذا المَتجَرِ في كوكي الطَلَب، أَو <c>null</c> — نَفسُ عَقدِ <c>AuthSession.ResolveToken</c>.</summary>
    public delegate string? SessionCookieReader(HttpRequest request, string tenantSlug);

    /// <summary>مِفتاحُ المَتجَرِ في رابِطِ الاتِّصال — يَكتُبُه العَميلانِ (<c>App.razor</c> و<c>realtime-nav.js</c>).</summary>
    public const string SlugQueryKey = "slug";

    private static readonly ConcurrentDictionary<string, (string TenantSlug, Guid UserId)> Connections = new();

    public override Task OnConnectedAsync()
    {
        var httpCtx = Context.GetHttpContext();
        var slug = httpCtx?.Request.Query[SlugQueryKey].ToString();
        var token = httpCtx is null || string.IsNullOrEmpty(slug) ? null : readSession(httpCtx.Request, slug);

        var parsed = ACommerce.Kit.Auth.Server.AuthHandlers.ParseToken(token);
        if (parsed is null || parsed.Value.TenantSlug != slug) { Context.Abort(); return Task.CompletedTask; }
        var (userId, tenantSlug, _) = parsed.Value;
        Connections[Context.ConnectionId] = (tenantSlug, userId);
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantSlug, userId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        Connections.TryRemove(Context.ConnectionId, out _);
        return Task.CompletedTask;
    }

    public static string GroupName(string tenantSlug, Guid userId) => $"{tenantSlug}:{userId}";
}

/// <summary>Wolverine handler يَستَهلِك BroadcastToUser ويُمَرِّره عَبر SignalR.</summary>
public static class RealtimeBroadcastHandler
{
    public static Task Handle(BroadcastToUser cmd, IHubContext<RealtimeHub> hub)
        => hub.Clients
            .Group(RealtimeHub.GroupName(cmd.TenantSlug, cmd.UserId))
            .SendAsync(cmd.Channel, cmd.Payload);
}
