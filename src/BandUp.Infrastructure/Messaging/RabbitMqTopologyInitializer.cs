using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Warms the API's broker connection at startup. Workers acquire broker connections on demand
/// so PostgreSQL-only services can start even during a broker outage.
/// </summary>
public sealed class RabbitMqTopologyInitializer(
    RabbitMqConnectionProvider connections,
    RabbitMqTopology topology,
    IOptions<RabbitMqSettings> settings,
    ILogger<RabbitMqTopologyInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RabbitMqSettings broker = settings.Value;

        try
        {
            await connections.GetConnectionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                exception,
                "Could not reach RabbitMQ at {Host}:{Port} to declare the topology",
                broker.Host,
                broker.Port);

            throw;
        }

        logger.LogInformation(
            "RabbitMQ topology ready on {Host}:{Port} with {QueueCount} queue(s)",
            broker.Host,
            broker.Port,
            topology.QueueCount);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
