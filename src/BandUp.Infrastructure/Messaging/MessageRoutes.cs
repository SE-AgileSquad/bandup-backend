using BandUp.Contracts.Messages;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Maps message contracts to the queue that carries them. The queue name doubles as the
/// routing key on the job exchange, so the topology stays a single table of queues.
/// </summary>
public sealed class MessageRoutes
{
    public const string PingQueue = "system.ping";

    private readonly Dictionary<Type, string> _routes = [];

    public IReadOnlyCollection<string> Queues => _routes.Values.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Every message route known to the system. Both the API and the Worker build the
    /// topology from this table, so a queue cannot exist in one process and not the other.
    /// </summary>
    public static MessageRoutes CreateDefault() =>
        new MessageRoutes().Map<PingRequested>(PingQueue);

    public MessageRoutes Map<TMessage>(string queue)
        where TMessage : class
    {
        _routes[typeof(TMessage)] = queue;
        return this;
    }

    public string QueueFor<TMessage>()
        where TMessage : class =>
        QueueFor(typeof(TMessage));

    public string QueueFor(Type messageType) =>
        _routes.TryGetValue(messageType, out string? queue)
            ? queue
            : throw new InvalidOperationException(
                $"No queue is mapped for message '{messageType.FullName}'. Register it in MessageRoutes.CreateDefault.");
}