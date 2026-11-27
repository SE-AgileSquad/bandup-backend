using RabbitMQ.Client;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Declares the job exchange and its queues. Declaring is idempotent, so both the API and
/// the Worker call this at startup without coordinating.
/// </summary>
public sealed class RabbitMqTopology(MessageRoutes routes)
{
    public const string ExchangeName = "bandup.jobs";
    public const string ExchangeType = "direct";

    public int QueueCount => routes.Queues.Count;

    public async Task DeclareAsync(IConnection connection, CancellationToken cancellationToken)
    {
        IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await channel.ExchangeDeclareAsync(
                ExchangeName,
                ExchangeType,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            foreach (string queue in routes.Queues)
            {
                await channel.QueueDeclareAsync(
                    queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    cancellationToken: cancellationToken);
            }

            foreach (var (queue, routingKey) in routes.Bindings)
            {
                await channel.QueueBindAsync(
                    queue,
                    ExchangeName,
                    routingKey: routingKey,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }
}
