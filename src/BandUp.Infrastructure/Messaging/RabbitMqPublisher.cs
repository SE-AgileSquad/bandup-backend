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
        string queue = routes.QueueFor<TMessage>();
        IChannel channel = await GetChannelAsync(cancellationToken);

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

        await channel.BasicPublishAsync(
            RabbitMqTopology.ExchangeName,
            queue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
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

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            IConnection connection = await connections.GetConnectionAsync(cancellationToken);

            _channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }
}