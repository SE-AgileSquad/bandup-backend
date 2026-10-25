using Microsoft.AspNetCore.Mvc;

namespace BandUp.Api.Errors;

/// <summary>
/// Writes a problem response as-is.
/// </summary>
/// <remarks>
/// <see cref="TypedResults.Problem(ProblemDetails)"/> is not used because the framework fills
/// the <c>traceId</c> extension from the current activity, overwriting the request id. The
/// trace id and the request id must be the same value, so the body is written here instead.
/// </remarks>
public sealed class ProblemResult(ProblemDetails problem) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: httpContext.RequestAborted);
    }
}