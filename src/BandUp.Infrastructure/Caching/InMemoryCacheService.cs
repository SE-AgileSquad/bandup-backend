using System.Collections.Concurrent;
using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Caching;

/// <summary>
/// Cache used by tests and by local runs that select <c>Cache__Provider=InMemory</c>.
/// </summary>
public sealed class InMemoryCacheService(TimeProvider timeProvider) : ICacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<TValue?> GetAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(key, out CacheEntry? entry))
        {
            return Task.FromResult(default(TValue));
        }

        if (entry.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult(default(TValue));
        }

        return Task.FromResult(entry.Value is TValue typed ? typed : default);
    }

    public Task SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan? timeToLive,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? expiresAt = timeToLive is { } lifetime
            ? timeProvider.GetUtcNow().Add(lifetime)
            : null;

        _entries[key] = new CacheEntry(value, expiresAt);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset? ExpiresAt);
}