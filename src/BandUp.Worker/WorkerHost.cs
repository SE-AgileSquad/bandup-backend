using BandUp.Application;
using BandUp.Infrastructure;
using BandUp.Infrastructure.Logging;
using BandUp.Worker.Consumers;
using Microsoft.Extensions.Hosting;
using Sentry;
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
            builder.Logging.AddSentry(options =>
            {
                options.Dsn = sentryDsn;
                options.Environment = builder.Environment.EnvironmentName;
                options.SendDefaultPii = false;
                options.AutoSessionTracking = false;
                options.SetBeforeSend((@event, _) =>
                {
                    @event.SetTag("service", BandUpLoggerConfiguration.Worker);
                    return @event;
                });
            });
        }

        // Long enough for the consumer to finish the delivery it is holding, then the broker
        // redelivers anything still in flight to the next instance.
        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(15));

        builder.Services
            .AddApplication()
            .AddInfrastructure(builder.Configuration);

        builder.Services.AddHostedService<PingConsumer>();

        return builder.Build();
    }
}