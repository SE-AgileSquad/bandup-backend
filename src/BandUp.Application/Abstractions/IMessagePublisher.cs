namespace BandUp.Application.Abstractions;

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message to the job exchange.
    /// </summary>
    /// <remarks>
    /// Call this only after the database transaction has committed. A message that is
    /// published before the commit can reach a consumer for a change that is later rolled back.
    /// </remarks>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class;
}