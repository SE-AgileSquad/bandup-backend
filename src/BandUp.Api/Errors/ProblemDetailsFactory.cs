using BandUp.Api;
using BandUp.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BandUp.Api.Errors;

/// <summary>
/// Builds the RFC 7807 body the platform returns for every expected failure. The
/// <c>traceId</c> is the request id, so a short reference quoted by a user can be found in
/// the logs.
/// </summary>
public static class ProblemDetailsFactory
{
    public static ProblemDetails ForError(Error error, HttpContext httpContext)
    {
        int status = ErrorHttpMapper.StatusFor(error.Type);

        ProblemDetails problem = new()
        {
            Type = ErrorHttpMapper.TypeUriFor(error.Code),
            Title = ErrorHttpMapper.TitleFor(status),
            Status = status,
            Detail = error.Message,
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = RequestContext.From(httpContext)?.RequestId;

        if (error.ValidationErrors.Count > 0)
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        return problem;
    }

    public static ProblemDetails ForBadHttpRequest(HttpContext httpContext, int statusCode)
    {
        ProblemDetails problem = ForError(Error.Validation(new Dictionary<string, string[]>
        {
            ["body"] = ["The request body could not be read. Send a valid JSON object."],
        }), httpContext);
        problem.Status = statusCode;
        problem.Title = ErrorHttpMapper.TitleFor(statusCode);
        return problem;
    }

    public static ProblemDetails ForUnhandledException(HttpContext httpContext)
    {
        ProblemDetails problem = new()
        {
            Type = ErrorHttpMapper.TypeUriFor(Error.InternalErrorCode),
            Title = ErrorHttpMapper.TitleFor(StatusCodes.Status500InternalServerError),
            Status = StatusCodes.Status500InternalServerError,
            Detail = "An unexpected error occurred.",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["code"] = Error.InternalErrorCode;
        problem.Extensions["traceId"] = RequestContext.From(httpContext)?.RequestId;

        return problem;
    }
}
