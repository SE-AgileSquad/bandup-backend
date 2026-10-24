using BandUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BandUp.Api.Health;

/// <summary>
/// Keeps a replica out of the load balancer until the schema is current. Migrations are
/// applied by an explicit command, so a pod that starts before one has run must not serve.
/// </summary>
public sealed class PendingMigrationsHealthCheck(BandUpDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<string> pending = (await context.Database
                .GetPendingMigrationsAsync(cancellationToken)).ToList();

            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Count} migration(s) are pending.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Could not read the migration history.", exception);
        }
    }
}