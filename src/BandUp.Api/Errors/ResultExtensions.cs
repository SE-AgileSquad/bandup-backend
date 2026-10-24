using BandUp.Application.Common;

namespace BandUp.Api.Errors;

/// <summary>
/// Maps a failed <see cref="Result"/> to the HTTP problem response. Endpoints call this
/// instead of repeating the mapping, so every failure looks the same on the wire.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToProblem(this Error error, HttpContext httpContext) =>
        TypedResults.Problem(ProblemDetailsFactory.ForError(error, httpContext));

    public static IResult ToProblem<TValue>(this Result<TValue> result, HttpContext httpContext) =>
        result.Error is { } error
            ? TypedResults.Problem(ProblemDetailsFactory.ForError(error, httpContext))
            : throw new InvalidOperationException("A successful result cannot be mapped to a problem response.");
}