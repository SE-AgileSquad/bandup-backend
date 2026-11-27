using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BandUp.Infrastructure.Logging;

/// <summary>
/// Builds the logger for a process. Both the API and the Worker call this so their log lines
/// are identical apart from the <c>service</c> field.
/// </summary>
public static class BandUpLoggerConfiguration
{
    public const string Api = "api";
    public const string Worker = "worker";

    /// <summary>
    /// Sources whose routine output would either duplicate our own request log line or flood
    /// the log with per-query noise. Warnings and errors still come through.
    /// </summary>
    private static readonly string[] QuietedSources =
    [
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.EntityFrameworkCore.Infrastructure",
        "Microsoft.EntityFrameworkCore.Model.Validation",
    ];

    /// <summary>
    /// Builds the process logger. An extra sink receives the same events as the console sink,
    /// which is how a test asserts on what was actually logged.
    /// </summary>
    public static Logger Create(
        string serviceName,
        IConfiguration configuration,
        ILogEventSink? additionalSink = null)
    {
        LoggerConfiguration logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLevel(configuration["Serilog:MinimumLevel"]))
            .Enrich.FromLogContext()
            .Enrich.WithProperty(BandUpLogProperties.Service, serviceName)
            .Filter.ByExcluding(IsUnsafeWritingEvent)
            .WriteTo.Console(new BandUpJsonFormatter());

        if (additionalSink is not null)
        {
            logger = logger.WriteTo.Sink(additionalSink);
        }

        foreach (string source in QuietedSources)
        {
            logger = logger.MinimumLevel.Override(source, LogEventLevel.Warning);
        }

        return logger.CreateLogger();
    }

    private static bool IsUnsafeWritingEvent(LogEvent entry)
    {
        if (!entry.Properties.ContainsKey(BandUpLogProperties.PrivateWritingRequest))
        {
            return false;
        }

        // Framework and provider diagnostics can include raw exception details, parameters or headers.
        return entry.Exception is not null ||
            !entry.Properties.TryGetValue("SourceContext", out var source) ||
            source is not ScalarValue { Value: string category } ||
            (category != "BandUp.Api.Middleware.RequestLoggingMiddleware" &&
             category != "BandUp.Api.Errors.BandUpExceptionHandler" &&
             !category.StartsWith("BandUp.Application.Behaviors.LoggingBehavior", StringComparison.Ordinal));
    }

    private static LogEventLevel ParseLevel(string? value) => value?.ToLowerInvariant() switch
    {
        "verbose" => LogEventLevel.Verbose,
        "debug" => LogEventLevel.Debug,
        "warning" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
