using BandUp.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BandUp.Api.Health;

public sealed class PostgresHealthCheck(BandUpDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL rejected the connection.");
        }
        catch (Exception exception)
        {
            // The description is returned to the caller, so it must not carry the exception
            // text: that would leak the connection string. The exception goes to the log.
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", exception);
        }
    }
}