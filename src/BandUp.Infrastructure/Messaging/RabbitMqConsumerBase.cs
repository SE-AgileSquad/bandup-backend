using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BandUp.Infrastructure.Messaging;

/// <summary>
/// Consumes a single queue with manual acknowledgements. A handler that keeps failing is
/// retried in-process with backoff, then handed to the final-failure hook and acknowledged.
/// A process crash never acknowledges, so the broker redelivers the message.
/// </summary>
public abstract class RabbitMqConsumerBase<TMessage>(
    RabbitMqConnectionProvider connections,
    RabbitMqTopology topology,
    ILogger logger) : BackgroundService
    where TMessage : class
{
    private const int MaxAttempts = 3;
    private const ushort PrefetchCount = 1;

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

    protected virtual Task OnFinalFailureAsync(
        TMessage message,
        Exception exception,
        CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task OnReceivedAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        TMessage? message = JsonSerializer.Deserialize<TMessage>(args.Body.Span);

        if (message is null)
        {
            logger.LogError(
                "Discarding an unreadable message on {QueueName} with delivery tag {DeliveryTag}",
                QueueName,
                args.DeliveryTag);

            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        Exception? failure = await InvokeWithRetriesAsync(message, cancellationToken);

        if (failure is not null)
        {
            await OnFinalFailureAsync(message, failure, cancellationToken);
        }

        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken);
    }

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