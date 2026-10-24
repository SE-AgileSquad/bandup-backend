using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Opens the broker connection and declares the exchange and queues at startup. Both the API
/// and the Worker register this, so a publisher can never target an exchange that no process
/// has created yet, and the first health probe meets an already-warm connection.
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
            IConnection connection = await connections.GetConnectionAsync(cancellationToken);

            await topology.DeclareAsync(connection, cancellationToken);
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