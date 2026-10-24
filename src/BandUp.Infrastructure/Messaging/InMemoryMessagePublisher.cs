using System.Collections.Concurrent;
using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Publisher used by tests and by local runs that select <c>MessagePublisher__Provider=InMemory</c>.
/// </summary>
public sealed class InMemoryMessagePublisher : IMessagePublisher
{
    private readonly ConcurrentQueue<object> _published = new();

    public IReadOnlyList<object> Published => _published.ToArray();

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }
}