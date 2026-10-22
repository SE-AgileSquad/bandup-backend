namespace BandUp.Application.Abstractions;

public interface ICacheService
{
    Task<TValue?> GetAsync<TValue>(string key, CancellationToken cancellationToken);

    Task SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan? timeToLive,
        CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}