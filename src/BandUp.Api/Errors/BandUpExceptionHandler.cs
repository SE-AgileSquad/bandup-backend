using BandUp.Domain.Common;
using BandUp.Infrastructure.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace BandUp.Api.Errors;

/// <summary>
/// Turns an unhandled exception into a 500 in the platform's error format. The detail is
/// always generic: the exception text goes to the log, never to the response.
/// </summary>
public sealed class BandUpExceptionHandler(ILogger<BandUpExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string requestId = RequestContext.From(httpContext)?.RequestId ?? string.Empty;
        string route = httpContext.Request.Path.Value ?? "/";

        // This handler runs after the request middleware has unwound, so the ambient log
        // context is gone: the request id has to be restored for the line to be traceable.
        using (LogContext.PushProperty(BandUpLogProperties.RequestId, requestId))
        {
            if (exception is DomainException)
            {
                logger.LogError(exception, "Domain invariant violated while handling {route}", route);
            }
            else
            {
                logger.LogError(exception, "Unhandled exception while handling {route}", route);
            }
        }

        ProblemDetails problem = ProblemDetailsFactory.ForUnhandledException(httpContext);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }
}