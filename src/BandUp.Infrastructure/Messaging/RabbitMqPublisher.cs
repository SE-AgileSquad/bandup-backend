using System.Text.Json;
using BandUp.Application.Abstractions;
using RabbitMQ.Client;

namespace BandUp.Infrastructure.Messaging;

public sealed class RabbitMqPublisher(RabbitMqConnectionProvider connections, MessageRoutes routes)
    : IDirectMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class
    {
        string routingKey = routes.RoutingKeyFor<TMessage>();
        string messageId = Guid.NewGuid().ToString();

        BasicProperties properties = new()
        {
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
            MessageId = messageId,
            Headers = new Dictionary<string, object?>
            {
                ["messageId"] = messageId,
                ["attempt"] = 0,
            },
        };

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, JsonSerialization.Options);

        // The singleton channel must have a single writer, including while awaiting confirms.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IChannel channel = await GetChannelAsync(cancellationToken);
            await channel.BasicPublishAsync(
                RabbitMqTopology.ExchangeName,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        IConnection connection = await connections.GetConnectionAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }
}
