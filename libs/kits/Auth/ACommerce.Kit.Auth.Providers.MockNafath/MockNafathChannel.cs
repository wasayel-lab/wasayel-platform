using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACommerce.Kit.Auth.Providers.MockNafath;

public sealed class MockNafathOptions
{
    /// <summary>الرَقم الذي سيُعرَض في صَفحَة الدُخول (يَختاره المُستَخدِم في نَفاذ).</summary>
    public string DisplayCode { get; set; } = "00";

    /// <summary>بَعد كم ثانيَة يُعتَبَر "مُوافَقاً" تلقائيّاً (محاكاة المُستَخدِم).</summary>
    public int AutoApproveSeconds { get; set; } = 5;
}

/// <summary>
/// مُزَوِّد نَفاذ وَهميّ — يُحاكي خِدمَة نَفاذ بتَأكيد تلقائيّ بَعد ثَوانٍ.
/// الإنتاج: مَكتَبَة أُخرى تَنفِّذ <see cref="INafathChannel"/> فوق API نَفاذ.
/// </summary>
public sealed class MockNafathChannel : INafathChannel
{
    private readonly MockNafathOptions _opts;
    private readonly ILogger<MockNafathChannel> _logger;
    private static readonly ConcurrentDictionary<string, DateTime> _attempts = new();

    public MockNafathChannel(MockNafathOptions opts, ILogger<MockNafathChannel> logger)
    { _opts = opts; _logger = logger; }

    public string ChannelName => "MockNafath";

    public Task<NafathStartResult> StartAsync(string nationalId, CancellationToken ct)
    {
        var attemptId = Guid.NewGuid().ToString("N");
        _attempts[attemptId] = DateTime.UtcNow.AddSeconds(_opts.AutoApproveSeconds);
        _logger.LogInformation("[MockNafath] طَلَب لِـ {NID}، رَقم العَرض {Code}",
            nationalId, _opts.DisplayCode);
        return Task.FromResult(new NafathStartResult(
            attemptId, _opts.DisplayCode, _opts.AutoApproveSeconds));
    }

    public Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct)
    {
        if (!_attempts.TryGetValue(attemptId, out var approveAt))
            return Task.FromResult(false);
        var approved = DateTime.UtcNow >= approveAt;
        if (approved) _attempts.TryRemove(attemptId, out _);
        return Task.FromResult(approved);
    }
}

public static class MockNafathExtensions
{
    public static IServiceCollection AddMockNafathChannel(
        this IServiceCollection services, Action<MockNafathOptions>? configure = null)
    {
        var opts = new MockNafathOptions();
        configure?.Invoke(opts);
        services.AddSingleton(opts);
        services.AddSingleton<INafathChannel, MockNafathChannel>();
        return services;
    }
}
