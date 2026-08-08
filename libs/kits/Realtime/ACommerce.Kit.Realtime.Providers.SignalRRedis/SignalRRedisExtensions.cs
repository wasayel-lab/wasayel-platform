using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Realtime.Providers.SignalRRedis;

/// <summary>
/// Backplane Redis لِـ SignalR — لِـ scale-out (أَكثَر مِن سيرفِر يُوَزِّع
/// الـ broadcasts بَينَه عَبر Redis pub/sub). يُستَدعى مَكان <c>AddSignalR()</c>.
/// </summary>
public static class SignalRRedisExtensions
{
    /// <summary>تَفعيل SignalR مَع Redis backplane.</summary>
    public static ISignalRServerBuilder AddRedisBackplane(
        this ISignalRServerBuilder builder, string connectionString, string channelPrefix = "acommerce")
    {
        builder.AddStackExchangeRedis(connectionString, options =>
        {
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(channelPrefix);
        });
        return builder;
    }
}
