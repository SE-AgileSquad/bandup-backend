using System.Text.Json;
using BandUp.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Consumes a single queue with manual acknowledgements. A handler that keeps failing is
/// retried in-process with backoff, then handed to the final-failure hook and acknowledged.
/// The Redis check/handle/mark sequence suppresses recorded redeliveries, but a crash between
/// handling and marking can repeat effects across processes. Card 3 must enforce database-backed
/// SubmissionId idempotency and processing fencing.
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
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ConsumeUntilDisconnectedAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested &&
                    RabbitMqConnectionProvider.IsTransientFailure(exception))
                {
                    logger.LogWarning("RabbitMQ consumer on {QueueName} will reconnect ({FailureType})",
                        QueueName, exception.GetType().Name);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(CancellationToken stoppingToken)
    {
        IConnection connection = await connections.GetConnectionAsync(stoppingToken);

        await topology.DeclareAsync(connection, stoppingToken);

        using var generation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var delivery = new SemaphoreSlim(1);
        await using IChannel channel = await connection.CreateChannelAsync(
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
        var disconnected = new TaskCompletionSource<ShutdownEventArgs?>(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, reason) =>
        {
            disconnected.TrySetResult(reason);
            return Task.CompletedTask;
        };
        consumer.UnregisteredAsync += (_, _) =>
        {
            disconnected.TrySetResult(null);
            return Task.CompletedTask;
        };
        var generationToken = generation.Token;
        consumer.ReceivedAsync += (_, args) => OnReceivedAsync(channel, args, delivery, generationToken);

        try
        {
            await channel.BasicConsumeAsync(
                QueueName,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken);

            var shutdown = await disconnected.Task.WaitAsync(stoppingToken);
            if (shutdown is not null)
            {
                throw new OperationInterruptedException(shutdown);
            }
        }
        finally
        {
            // Broker callbacks only signal shutdown. Cancel and drain outside the dispatcher,
            // before disposing the channel or registering a replacement consumer.
            try
            {
                await generation.CancelAsync();
            }
            finally
            {
                await delivery.WaitAsync(CancellationToken.None);
                delivery.Release();
            }
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
        SemaphoreSlim delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            await delivery.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessAsync(channel, args, cancellationToken);
            }
            finally
            {
                delivery.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave an interrupted delivery unacknowledged for broker redelivery.
            logger.LogInformation(
                "Delivery interrupted for {DeliveryTag} on {QueueName}; leaving it unacknowledged",
                args.DeliveryTag,
                QueueName);
        }
    }

    private async Task ProcessAsync(
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
        cancellationToken.ThrowIfCancellationRequested();

        if (failure is not null)
        {
            await OnFinalFailureAsync(message, failure, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
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
