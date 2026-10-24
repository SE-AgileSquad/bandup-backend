using BandUp.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace BandUp.Api.Health;

/// <summary>
/// Confirms the broker is reachable by opening a channel, which is stricter than checking
/// the cached connection flag: an open TCP connection can still be unusable.
/// </summary>
public sealed class RabbitMqHealthCheck(RabbitMqConnectionProvider connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IConnection connection = await connections.GetConnectionAsync(cancellationToken);

            if (!connection.IsOpen)
            {
                return HealthCheckResult.Unhealthy("The RabbitMQ connection is closed.");
            }

            await using IChannel channel = await connection.CreateChannelAsync(
                cancellationToken: cancellationToken);

            return channel.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ refused to open a channel.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception);
        }
    }
}