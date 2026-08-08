using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Kit.Cache;

/// <summary>كاش مُجَرَّد — Redis في الإنتاج، InMemory في التَطوير.</summary>
public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class;
    Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class;
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);
    Task<long> IncrementAsync(string key, long by = 1, CancellationToken ct = default);

    /// <summary>قُفل تَوزيعيّ — لِمَنع race conditions في عَمَلِيّات حَرِجَة.
    /// <c>using var l = await cache.AcquireLockAsync("inventory:123", ...)</c>.</summary>
    Task<IDistributedLock?> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}

public interface IDistributedLock : IAsyncDisposable
{
    string Key { get; }
}

/// <summary>مَخزون داخِليّ (in-process). يُلائِم التَطوير والـ single-server.
/// لا يَدعَم scale-out — اِستَخدِم Redis لِأَكثَر مِن سيرفِر.</summary>
public sealed class InMemoryCache : ICache
{
    private sealed class Entry { public required object Value; public DateTime? ExpiresAt; }
    private readonly ConcurrentDictionary<string, Entry> _data = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        if (!_data.TryGetValue(key, out var e) || (e.ExpiresAt is { } x && x < DateTime.UtcNow))
            return Task.FromResult<T?>(null);
        return Task.FromResult((T?)e.Value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        _data[key] = new Entry { Value = value, ExpiresAt = expiry is { } d ? DateTime.UtcNow.Add(d) : null };
        return Task.CompletedTask;
    }

    public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        var added = _data.TryAdd(key, new Entry { Value = value, ExpiresAt = expiry is { } d ? DateTime.UtcNow.Add(d) : null });
        return Task.FromResult(added);
    }

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default)
        => Task.FromResult(_data.TryRemove(key, out _));

    public Task<long> IncrementAsync(string key, long by = 1, CancellationToken ct = default)
        => Task.FromResult(_counters.AddOrUpdate(key, by, (_, v) => v + by));

    public async Task<IDistributedLock?> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var ok = await SetIfNotExistsAsync($"lock:{key}", "1", ttl, ct);
        return ok ? new MemLock(this, $"lock:{key}") : null;
    }

    private sealed class MemLock(InMemoryCache c, string k) : IDistributedLock
    {
        public string Key => k;
        public ValueTask DisposeAsync() { c.RemoveAsync(k); return default; }
    }
}

public static class CacheExtensions
{
    public static IServiceCollection AddInMemoryCache(this IServiceCollection services)
    {
        services.AddSingleton<ICache, InMemoryCache>();
        return services;
    }
}
