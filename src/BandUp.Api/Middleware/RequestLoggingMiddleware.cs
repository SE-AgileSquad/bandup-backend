using System.Diagnostics;
using BandUp.Api;
using BandUp.Infrastructure.Logging;
using Serilog.Context;

namespace BandUp.Api.Middleware;

/// <summary>
/// Writes one log line per request with the fields the platform standardises on, and pushes
/// the request id onto the log context so every line written during the request carries it.
/// Health probes are skipped so they do not drown the log.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly string[] IgnoredPaths = ["/health", "/live", "/ready"];

    public async Task InvokeAsync(HttpContext httpContext)
    {
        string requestId = RequestContext.From(httpContext)?.RequestId ?? string.Empty;

        using IDisposable requestIdScope = LogContext.PushProperty(BandUpLogProperties.RequestId, requestId);

        if (IsIgnored(httpContext.Request.Path))
        {
            await next(httpContext);
            return;
        }

        long started = Stopwatch.GetTimestamp();
        string method = httpContext.Request.Method;

        // The status code is only final once the response completes, which is after the
        // exception handler has run, so the line is written from the completion callback.
        httpContext.Response.OnCompleted(() =>
        {
            double elapsedMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);

            using IDisposable scope = LogContext.PushProperty(BandUpLogProperties.RequestId, requestId);

            logger.LogInformation(
                "{method} {route} responded {statusCode} in {durationMs}ms",
                method,
                RouteOf(httpContext),
                httpContext.Response.StatusCode,
                elapsedMs);

            return Task.CompletedTask;
        });

        await next(httpContext);
    }

    /// <summary>
    /// Logs the route template rather than the raw path, so ids in the URL do not become
    /// high-cardinality log values.
    /// </summary>
    private static string RouteOf(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.DisplayName ?? httpContext.Request.Path.Value ?? "/";

    private static bool IsIgnored(PathString path) =>
        IgnoredPaths.Any(ignored => path.StartsWithSegments(ignored, StringComparison.OrdinalIgnoreCase));
}