using Microsoft.AspNetCore.SignalR.Client;

namespace ACommerce.Kit.Realtime.Client;

/// <summary>
/// عَميل SignalR لِـ Blazor Server. يَتَّصِل بـ /realtime ويَكشِف
/// أحداثاً قابِلَة للاشتِراك. Scoped per-circuit. اسحَب الـ token من
/// AuthSession ثُمّ <c>ConnectAsync(baseUrl, token)</c>.
/// </summary>
public sealed class RealtimeClient : IAsyncDisposable
{
    private HubConnection? _hub;
    public event Action<object>? NotificationReceived;
    public event Action<object>? ChatReceived;
    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string baseUrl, string token)
    {
        if (_hub is not null) return;
        _hub = new HubConnectionBuilder()
            .WithUrl($"{baseUrl}/realtime?token={token}")
            .WithAutomaticReconnect()
            .Build();
        _hub.On<object>("notification", payload => NotificationReceived?.Invoke(payload));
        _hub.On<object>("chat",         payload => ChatReceived?.Invoke(payload));
        await _hub.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
