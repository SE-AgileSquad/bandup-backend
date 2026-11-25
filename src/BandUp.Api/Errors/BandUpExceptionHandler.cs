using BandUp.Application.Common;
using BandUp.Domain.Common;
using BandUp.Infrastructure.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace BandUp.Api.Errors;

/// <summary>
/// Preserves client request errors and maps infrastructure faults to the platform's generic
/// error format. Exception text is never returned to the caller.
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
            if (exception is BadHttpRequestException { StatusCode: >= 400 and < 500 } badRequest)
            {
                logger.LogWarning("Rejected request while handling {route} with {statusCode}", route, badRequest.StatusCode);
                await new ProblemResult(ProblemDetailsFactory.ForBadHttpRequest(httpContext, badRequest.StatusCode))
                    .ExecuteAsync(httpContext);
                return true;
            }

            if (exception is DomainException)
            {
                logger.LogError(exception, "Domain invariant violated while handling {route}", route);
            }
            else
            {
                logger.LogError(exception, "Unhandled exception while handling {route}", route);
            }
        }

        // A unique-index race is an expected conflict (AUTH-02), not an unhandled fault: it
        // surfaces as 409 with a stable code rather than a 500.
        if (exception is DuplicateEntityException)
        {
            ProblemDetails conflict = ProblemDetailsFactory.ForError(Error.EmailTaken(), httpContext);

            httpContext.Response.StatusCode = conflict.Status ?? StatusCodes.Status409Conflict;

            await httpContext.Response.WriteAsJsonAsync(
                conflict,
                options: null,
                contentType: "application/problem+json",
                cancellationToken: cancellationToken);

            return true;
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
