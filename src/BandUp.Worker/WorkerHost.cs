using BandUp.Application;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Logging;
using BandUp.Worker.Consumers;
using BandUp.Worker.Writing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sentry;
using Sentry.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace BandUp.Worker;

/// <summary>
/// Composes the worker host. Exposed separately from the entry point so a test can start the
/// real worker rather than a reconstruction of it.
/// </summary>
public static class WorkerHost
{
    /// <summary>
    /// Builds the host. <paramref name="configure"/> runs before anything is registered, so a
    /// caller can supply configuration; <paramref name="logSink"/> receives the same events as
    /// the console sink.
    /// </summary>
    public static IHost Build(
        string[] args,
        ILogEventSink? logSink = null,
        Action<HostApplicationBuilder>? configure = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        configure?.Invoke(builder);

        builder.Services.AddSerilog(
            BandUpLoggerConfiguration.Create(
                BandUpLoggerConfiguration.Worker,
                builder.Configuration,
                logSink),
            dispose: true);

        string? sentryDsn = builder.Configuration["Sentry:Dsn"];

        if (!string.IsNullOrWhiteSpace(sentryDsn))
        {
            builder.Services.Configure<SentryLoggingOptions>(options =>
            {
                options.Dsn = sentryDsn;
                options.Environment = builder.Environment.EnvironmentName;
                options.SendDefaultPii = false;
                options.IsGlobalModeEnabled = false;
                options.SetBeforeBreadcrumb(crumb => WritingTelemetryBoundary.IsPrivate ? null : crumb);
                options.SetBeforeSendLog(entry => WritingTelemetryBoundary.IsPrivate ? null : entry);
                options.SetBeforeSendMetric(metric => WritingTelemetryBoundary.IsPrivate ? null : metric);
                options.SetBeforeSendTransaction(transaction => WritingTelemetryBoundary.IsPrivate ||
                    WritingTelemetryBoundary.HasPrivateTag(transaction.Tags) ? null : transaction);
                options.AutoSessionTracking = false;
                options.SetBeforeSend((@event, _) =>
                {
                    if (WritingTelemetryBoundary.IsPrivate || WritingTelemetryBoundary.HasPrivateTag(@event.Tags))
                    {
                        return null;
                    }
                    @event.SetTag("service", BandUpLoggerConfiguration.Worker);
                    return @event;
                });
            });
            builder.Services.AddHostedService<SentryLifetime>();
        }

        // Long enough for the consumer to finish the delivery it is holding, then the broker
        // redelivers anything still in flight to the next instance.
        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(15));

        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration, initializeBrokerOnStartup: false);

        builder.Services.AddHostedService<PingConsumer>();
        builder.Services.AddHostedService<WritingBackgroundPublisher>();
        builder.Services.AddHostedService<WritingExpirationService>();

        return builder.Build();
    }

    // Serilog owns ILoggerFactory. Start the SDK independently, before any hosted service starts.
    private sealed class SentryLifetime(IOptions<SentryLoggingOptions> options) : IHostedLifecycleService, IDisposable
    {
        private IDisposable? _sdk;

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sdk = SentrySdk.Init(options.Value);
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StoppedAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Workers have stopped producing telemetry. Flush every SDK buffer before closing the hub.
                await SentrySdk.FlushAsync(options.Value.FlushTimeout);
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _sdk, null)?.Dispose();
    }
}
