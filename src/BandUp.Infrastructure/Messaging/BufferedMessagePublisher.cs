using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Ensures a message never reaches the broker before the transaction that produced it has
/// committed. Inside a transaction the publish is deferred to <see cref="IMessageBuffer"/>;
/// outside one it goes straight through.
/// </summary>
public sealed class BufferedMessagePublisher(
    IMessageBuffer buffer,
    IDirectMessagePublisher inner)
    : IMessagePublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class
    {
        if (!buffer.IsOpen)
        {
            return inner.PublishAsync(message, cancellationToken);
        }

        buffer.Enqueue(token => inner.PublishAsync(message, token));
        return Task.CompletedTask;
    }
}