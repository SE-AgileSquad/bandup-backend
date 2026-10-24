using System.Text.Json;
using BandUp.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Consumes a single queue with manual acknowledgements. A handler that keeps failing is
/// retried in-process with backoff, then handed to the final-failure hook and acknowledged.
/// A process crash never acknowledges, so the broker redelivers the message, and the
/// handled-message record makes the redelivery a no-op.
/// </summary>
public abstract class RabbitMqConsumerBase<TMessage>(
    RabbitMqConnectionProvider connections,
    RabbitMqTopology topology,
    ICacheService cache,
    ILogger logger) : BackgroundService
    where TMessage : class
{
    private const int MaxAttempts = 3;
    private const ushort PrefetchCount = 1;

    /// <summary>
    /// How long a handled message id is remembered. Redelivery only happens while the message
    /// is still in the queue, so a week covers a broker or consumer outage.
    /// </summary>
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromDays(7);

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    protected abstract string QueueName { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IConnection connection = await connections.GetConnectionAsync(stoppingToken);

        await topology.DeclareAsync(connection, stoppingToken);

        IChannel channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: 1),
            stoppingToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.ReceivedAsync += (_, args) => OnReceivedAsync(channel, args, stoppingToken);

        await channel.BasicConsumeAsync(
            QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Called when every attempt failed, before the message is acknowledged. Override to record
    /// the failure or alert; the base implementation only logs.
    /// </summary>
    protected virtual Task OnFinalFailureAsync(
        TMessage message,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Giving up on {QueueName} after {MaxAttempts} attempts",
            QueueName,
            MaxAttempts);

        return Task.CompletedTask;
    }

    private async Task OnReceivedAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        TMessage? message = Deserialize(args);

        if (message is null)
        {
            logger.LogError(
                "Discarding an unreadable message on {QueueName} with delivery tag {DeliveryTag}",
                QueueName,
                args.DeliveryTag);

            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        string? messageId = args.BasicProperties.MessageId;

        if (messageId is not null && await WasHandledAsync(messageId, cancellationToken))
        {
            logger.LogInformation(
                "Skipping message {MessageId} on {QueueName}: already handled",
                messageId,
                QueueName);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        Exception? failure = await InvokeWithRetriesAsync(message, cancellationToken);

        if (failure is not null)
        {
            await OnFinalFailureAsync(message, failure, cancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        if (messageId is not null)
        {
            await MarkHandledAsync(messageId, cancellationToken);
        }

        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
    }

    private static TMessage? Deserialize(BasicDeliverEventArgs args)
    {
        try
        {
            return JsonSerializer.Deserialize<TMessage>(args.Body.Span, JsonSerialization.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task<bool> WasHandledAsync(string messageId, CancellationToken cancellationToken) =>
        cache.GetAsync<bool>(DedupeKey(messageId), cancellationToken);

    private Task MarkHandledAsync(string messageId, CancellationToken cancellationToken) =>
        cache.SetAsync(DedupeKey(messageId), true, DedupeWindow, cancellationToken);

    private string DedupeKey(string messageId) => $"consumed:{QueueName}:{messageId}";

    private async Task<Exception?> InvokeWithRetriesAsync(TMessage message, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await HandleAsync(message, cancellationToken);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Attempt {Attempt} of {MaxAttempts} failed on {QueueName}",
                    attempt,
                    MaxAttempts,
                    QueueName);

                if (attempt == MaxAttempts)
                {
                    return exception;
                }

                await Task.Delay(RetryDelays[attempt - 1], cancellationToken);
            }
        }

        return null;
    }
}
