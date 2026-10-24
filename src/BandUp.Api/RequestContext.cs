namespace BandUp.Api;

/// <summary>
/// The request id assigned for the current request. Identity is read from the request rather
/// than stored, because the principal is not populated when the id is assigned.
/// </summary>
public sealed class RequestContext(string requestId)
{
    public string RequestId { get; } = requestId;

    public static RequestContext? From(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(typeof(RequestContext), out object? value)
            ? value as RequestContext
            : null;
}