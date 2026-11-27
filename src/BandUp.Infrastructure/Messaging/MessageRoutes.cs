using BandUp.Contracts.Messages;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Maps contracts to queue bindings on the job exchange. A route defaults to its queue name.
/// </summary>
public sealed class MessageRoutes
{
    public const string PingQueue = "system.ping";
    public const string WritingEvaluationQueue = "bandup.writing.evaluation.v1";
    public const string WritingEvaluationRoutingKey = "writing.evaluation.requested.v1";

    private readonly Dictionary<Type, (string Queue, string RoutingKey)> _routes = [];

    public IReadOnlyCollection<string> Queues => _routes.Values.Select(route => route.Queue).Distinct(StringComparer.Ordinal).ToArray();
    internal IEnumerable<(string Queue, string RoutingKey)> Bindings => _routes.Values.Distinct();

    /// <summary>
    /// Every message route known to the system. Both the API and the Worker build the
    /// topology from this table, so a queue cannot exist in one process and not the other.
    /// </summary>
    public static MessageRoutes CreateDefault() =>
        new MessageRoutes().Map<PingRequested>(PingQueue)
            .Map<WritingEvaluationRequestedV1>(WritingEvaluationQueue, WritingEvaluationRoutingKey);

    public MessageRoutes Map<TMessage>(string queue, string? routingKey = null)
        where TMessage : class
    {
        _routes[typeof(TMessage)] = (queue, routingKey ?? queue);
        return this;
    }

    public string QueueFor<TMessage>()
        where TMessage : class =>
        QueueFor(typeof(TMessage));

    public string QueueFor(Type messageType) => RouteFor(messageType).Queue;

    public string RoutingKeyFor<TMessage>() where TMessage : class => RouteFor(typeof(TMessage)).RoutingKey;

    private (string Queue, string RoutingKey) RouteFor(Type messageType) =>
        _routes.TryGetValue(messageType, out var route)
            ? route
            : throw new InvalidOperationException(
                $"No queue is mapped for message '{messageType.FullName}'. Register it in MessageRoutes.CreateDefault.");
}
