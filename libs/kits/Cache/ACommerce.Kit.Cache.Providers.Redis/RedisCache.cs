using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ACommerce.Kit.Cache.Providers.Redis;

public sealed class RedisCacheOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string KeyPrefix { get; set; } = "acommerce:";
}

/// <summary>كاش Redis — لِلإنتاج (multi-server). يَستَخدِم SET NX EX لِلـ atomic
/// set-if-absent، INCRBY لِلعَدّادات، و SET NX لِلأَقفال (lock-by-key).</summary>
public sealed class RedisCache : ICache, IAsyncDisposable
{
    private readonly RedisCacheOptions _opts;
    private readonly Lazy<Task<ConnectionMultiplexer>> _mux;

    public RedisCache(IOptions<RedisCacheOptions> opts)
    {
        _opts = opts.Value;
        _mux = new(() => ConnectionMultiplexer.ConnectAsync(_opts.ConnectionString));
    }

    private async Task<IDatabase> DbAsync() => (await _mux.Value).GetDatabase();
    private string K(string key) => _opts.KeyPrefix + key;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var db = await DbAsync();
        var v = await db.StringGetAsync(K(key));
        return v.HasValue ? JsonSerializer.Deserialize<T>(v.ToString()) : null;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        var db = await DbAsync();
        await db.StringSetAsync(K(key), JsonSerializer.Serialize(value), expiry);
    }

    public async Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) where T : class
    {
        var db = await DbAsync();
        return await db.StringSetAsync(K(key), JsonSerializer.Serialize(value), expiry, When.NotExists);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        var db = await DbAsync();
        return await db.KeyDeleteAsync(K(key));
    }

    public async Task<long> IncrementAsync(string key, long by = 1, CancellationToken ct = default)
    {
        var db = await DbAsync();
        return await db.StringIncrementAsync(K(key), by);
    }

    public async Task<IDistributedLock?> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = await DbAsync();
        var token = Guid.NewGuid().ToString("N");
        var ok = await db.StringSetAsync(K($"lock:{key}"), token, ttl, When.NotExists);
        return ok ? new RedisLock(db, K($"lock:{key}"), token) : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_mux.IsValueCreated) await (await _mux.Value).DisposeAsync();
    }

    private sealed class RedisLock(IDatabase db, string key, string token) : IDistributedLock
    {
        public string Key => key;
        public async ValueTask DisposeAsync()
        {
            // اِحذِف فَقَط لَو ما زِلنا أَصحاب القُفل (token مُطابِق).
            const string lua = "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";
            await db.ScriptEvaluateAsync(lua, new RedisKey[] { key }, new RedisValue[] { token });
        }
    }
}

public static class RedisExtensions
{
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services, Action<RedisCacheOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ICache, RedisCache>();
        return services;
    }
}
