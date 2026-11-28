using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Contracts.Messages;
using BandUp.Infrastructure.Logging;
using BandUp.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;
using Sentry;
using Serilog.Context;

namespace BandUp.Worker.Writing;

public sealed class WritingBackgroundPublisher(
    IServiceScopeFactory scopes,
    IDirectMessagePublisher publisher,
    IOptions<WritingPublishingOptions> options,
    TimeProvider clock,
    ILogger<WritingBackgroundPublisher> logger,
    RabbitMqConnectionProvider? broker = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunCycleAsync(settings, stoppingToken);
                await Task.Delay(settings.PollInterval, clock, stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // Closing the transport can surface an I/O exception as well as cancellation.
        }
    }

    private async Task RunCycleAsync(WritingPublishingOptions settings, CancellationToken stoppingToken)
    {
        using var privacy = WritingTelemetryBoundary.Enter();
        using var logScope = LogContext.PushProperty(BandUpLogProperties.PrivateWritingPublication, true);
        using var telemetryScope = SentrySdk.PushScope();
        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag(BandUpLogProperties.PrivateWritingPublication, "true");
            scope.Transaction?.SetTag(BandUpLogProperties.PrivateWritingPublication, "true");
        });
        if (broker is not null)
        {
            // Do not spend attempts when the broker is already known to be unavailable.
            using var timeout = new CancellationTokenSource(settings.PublishTimeout, clock);
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, stoppingToken);
            try
            {
                await broker.GetConnectionAsync(operation.Token);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested &&
                RabbitMqConnectionProvider.IsTransientFailure(exception))
            {
                logger.LogWarning("Writing publication waiting for RabbitMQ ({FailureType})", exception.GetType().Name);
                return;
            }
        }
        try
        {
            await PublishBatchAsync(settings, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Database failures can leave a committed lease. Let it expire without retrying the command.
            logger.LogWarning("Writing publication cycle deferred ({FailureType})", exception.GetType().Name);
        }
    }

    private async Task PublishBatchAsync(WritingPublishingOptions settings, CancellationToken stoppingToken)
    {
        for (int count = 0; count < settings.BatchSize; count++)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var leased = await DispatchAsync<ReserveWritingPublication, WritingPublishReservation?>(new(), stoppingToken);
            if (leased is not { } reservation)
            {
                return;
            }

            // The reservation command has committed and its scope is disposed before touching the broker.
            var outcome = await TryPublishAsync(reservation.SubmissionId, settings.PublishTimeout, stoppingToken);
            stoppingToken.ThrowIfCancellationRequested();
            bool recorded = outcome == PublishOutcome.Confirmed
                ? await DispatchAsync<ConfirmWritingPublication, bool>(new(reservation), stoppingToken)
                : await DispatchAsync<RecordWritingPublishFailure, bool>(new(reservation), stoppingToken);
            if (!recorded)
            {
                logger.LogDebug("Writing publication outcome ignored because the lease no longer applies");
            }
            if (outcome == PublishOutcome.Interrupted)
            {
                // Do not spend the rest of the batch's attempts on a failing or uncertain transport.
                return;
            }
        }
    }

    private enum PublishOutcome { Confirmed, Returned, Interrupted }

    private async Task<PublishOutcome> TryPublishAsync(Guid submissionId, TimeSpan publishTimeout, CancellationToken stoppingToken)
    {
        using var timeout = new CancellationTokenSource(publishTimeout, clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, stoppingToken);
        try
        {
            // Supply the deadline to the transport and await it even on cancellation. A timed-out
            // send may have reached the broker, so a later attempt can legitimately redeliver it.
            operation.Token.ThrowIfCancellationRequested();
            await publisher.PublishAsync(new WritingEvaluationRequestedV1(submissionId), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            return PublishOutcome.Confirmed;
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Writing publication was not confirmed ({FailureType}, timeout: {TimedOut})",
                exception.GetType().Name, timeout.IsCancellationRequested);
            return exception is PublishException { IsReturn: true } && !timeout.IsCancellationRequested
                ? PublishOutcome.Returned : PublishOutcome.Interrupted;
        }
    }

    private async Task<TResult> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        await using var scope = scopes.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.SendAsync<TCommand, TResult>(command, ct);
        if (result.IsFailure)
        {
            throw new InvalidOperationException("A Writing publication command failed.");
        }
        return result.Value;
    }
}
