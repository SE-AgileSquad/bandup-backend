using BandUp.Application.Abstractions;
using BandUp.Contracts.Messages;
using BandUp.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;

namespace BandUp.Worker.Consumers;

/// <summary>
/// Consumes the diagnostic ping. Acknowledgement, retry and duplicate suppression are handled
/// by the base class; the handler only does the work.
/// </summary>
public sealed class PingConsumer(
    RabbitMqConnectionProvider connections,
    RabbitMqTopology topology,
    ICacheService cache,
    ILogger<PingConsumer> logger)
    : RabbitMqConsumerBase<PingRequested>(connections, topology, cache, logger)
{
    protected override string QueueName => MessageRoutes.PingQueue;

    protected override Task HandleAsync(PingRequested message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Received ping {PingId} requested at {RequestedAt}: {PingMessage}",
            message.PingId,
            message.RequestedAt,
            message.Message);

        return Task.CompletedTask;
    }
}
