using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing.Finalization;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentry;
using Serilog.Context;

namespace BandUp.Worker.Writing;

public sealed class WritingExpirationService(
    IServiceScopeFactory scopes, IOptions<WritingPublishingOptions> options, TimeProvider clock,
    ILogger<WritingExpirationService> logger) : BackgroundService
{
    private WritingExpirationCandidate? _cursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ExpireBatchAsync(settings.ExpirationBatchSize, stoppingToken);
                await Task.Delay(settings.ExpirationPollInterval, clock, stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExpireBatchAsync(int batchSize, CancellationToken ct)
    {
        using var logScope = LogContext.PushProperty(BandUpLogProperties.PrivateWritingExpiration, true);
        using var telemetryScope = SentrySdk.PushScope();
        SentrySdk.ConfigureScope(scope => scope.SetTag(BandUpLogProperties.PrivateWritingExpiration, "true"));
        IReadOnlyList<WritingExpirationCandidate> candidates;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var scan = scope.ServiceProvider.GetRequiredService<IWritingExpirationScan>();
            candidates = await scan.ReadDueAsync(_cursor, batchSize, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Writing expiration scan deferred ({FailureType})", exception.GetType().Name);
            return;
        }

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Advance even on failure, then wrap at the end so one bad row cannot monopolize the batch.
            _cursor = candidate;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
                var result = await dispatcher.SendAsync<ExpireWriting, bool>(new(candidate.SubmissionId), ct);
                if (result.IsFailure)
                {
                    logger.LogWarning("Writing expiration rejected ({ErrorCode})", result.Error!.Code);
                }
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Writing expiration deferred ({FailureType})", exception.GetType().Name);
            }
        }
        if (candidates.Count < batchSize)
        {
            _cursor = null;
        }
    }
}
