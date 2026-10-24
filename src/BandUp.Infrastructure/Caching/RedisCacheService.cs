using System.Text.Json;
using BandUp.Application.Abstractions;
using StackExchange.Redis;

namespace BandUp.Infrastructure.Caching;

public sealed class RedisCacheService(IConnectionMultiplexer connection) : ICacheService
{
    private const string KeyPrefix = "bandup:";

    public async Task<TValue?> GetAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        RedisValue value = await connection.GetDatabase().StringGetAsync(KeyPrefix + key);

        return value.IsNullOrEmpty
            ? default
            : JsonSerializer.Deserialize<TValue>(value.ToString());
    }

    public async Task SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan? timeToLive,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(value);
        IDatabase database = connection.GetDatabase();

        if (timeToLive is { } lifetime)
        {
            await database.StringSetAsync(KeyPrefix + key, payload, lifetime);
            return;
        }

        await database.StringSetAsync(KeyPrefix + key, payload);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        connection.GetDatabase().KeyDeleteAsync(KeyPrefix + key);
}