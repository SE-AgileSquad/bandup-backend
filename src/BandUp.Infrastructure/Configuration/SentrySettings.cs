namespace BandUp.Infrastructure.Configuration;

public sealed class SentrySettings
{
    public const string SectionName = "Sentry";

    /// <summary>
    /// An empty DSN disables Sentry.
    /// </summary>
    public string? Dsn { get; set; }
}