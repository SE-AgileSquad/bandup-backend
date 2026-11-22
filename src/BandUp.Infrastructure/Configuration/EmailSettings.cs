namespace BandUp.Infrastructure.Configuration;

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public string Provider { get; set; } = ServiceProviders.InMemory;
    public string MailpitBaseUrl { get; set; } = "http://localhost:8025/";
    public string FromEmail { get; set; } = "no-reply@bandup.local";
    public string FromName { get; set; } = "BandUp";
    public int TimeoutSeconds { get; set; } = 5;
    public int QueueCapacity { get; set; } = 256;

    internal static bool IsLocalEndpoint(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}
