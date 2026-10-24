using System.Buffers;

namespace BandUp.Api.Middleware;

/// <summary>
/// Assigns the request id for every request and echoes it back on the response. An incoming
/// id is honoured only when it is shaped like one this system would have generated, so a
/// client cannot inject arbitrary text into the logs.
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Request-Id";

    private static readonly SearchValues<char> AllowedIdCharacters = SearchValues.Create(
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-");

    public async Task InvokeAsync(HttpContext httpContext)
    {
        string requestId = IsAcceptable(httpContext.Request.Headers[HeaderName])
            ? httpContext.Request.Headers[HeaderName].ToString()
            : Guid.CreateVersion7().ToString("N");

        httpContext.Items[typeof(RequestContext)] = new RequestContext(requestId);
        httpContext.Response.OnStarting(
            state =>
            {
                ((HttpContext)state).Response.Headers[HeaderName] = requestId;
                return Task.CompletedTask;
            },
            httpContext);

        await next(httpContext);
    }

    private static bool IsAcceptable(string? candidate) =>
        candidate is { Length: >= 8 and <= 64 }
        && candidate.All(character => AllowedIdCharacters.Contains(character));
}