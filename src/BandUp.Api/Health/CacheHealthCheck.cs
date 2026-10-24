using BandUp.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BandUp.Api.Health;

/// <summary>
/// Proves the cache works by writing and reading a value, which exercises the configured
/// implementation whether it is Redis or the in-memory one.
/// </summary>
public sealed class CacheHealthCheck(ICacheService cache) : IHealthCheck
{
    private const string ProbeKey = "health:probe";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetAsync(ProbeKey, "ok", TimeSpan.FromSeconds(10), cancellationToken);
            string? value = await cache.GetAsync<string>(ProbeKey, cancellationToken);

            return value == "ok"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The cache did not return the value it stored.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("The cache is unreachable.", exception);
        }
    }
}