namespace BandUp.Infrastructure.Logging;

/// <summary>
/// The log-event property names every process emits. Kept in one place so the formatter,
/// the request middleware and the enrichers cannot drift apart.
/// </summary>
public static class BandUpLogProperties
{
    public const string Timestamp = "timestamp";
    public const string Level = "level";
    public const string Message = "message";
    public const string RequestId = "requestId";
    public const string UserId = "userId";
    public const string Route = "route";
    public const string StatusCode = "statusCode";
    public const string DurationMs = "durationMs";
    public const string Service = "service";
    public const string Exception = "exception";
    public const string PrivateWritingRequest = "privateWritingRequest";
    public const string PrivateWritingPublication = "privateWritingPublication";
    public const string PrivateWritingExpiration = "privateWritingExpiration";

    /// <summary>
    /// Names the formatter lifts to the top level of the JSON object rather than nesting
    /// them under the property bag.
    /// </summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        RequestId,
        UserId,
        Route,
        StatusCode,
        DurationMs,
        Service,
    };
}
