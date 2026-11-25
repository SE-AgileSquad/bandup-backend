using System.Net.Mail;

namespace BandUp.Infrastructure.Configuration;

public sealed class EmailSettings
{
    public const string SectionName = "Email";
    public string Provider { get; set; } = ServiceProviders.InMemory;
    public string MailpitBaseUrl { get; set; } = "http://localhost:8025/";
    public string? ResendApiKey { get; set; }
    public string FromEmail { get; set; } = "no-reply@bandup.local";
    public string FromName { get; set; } = "BandUp";
    public int TimeoutSeconds { get; set; } = 5;
    public int QueueCapacity { get; set; } = 256;

    internal static bool IsLocalEndpoint(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    internal static bool IsResendKey(string? value) => value is { Length: > 3 } &&
        value.StartsWith("re_", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    internal static bool IsResendSender(string? value) => MailAddress.TryCreate(value, out MailAddress? address) &&
        string.Equals(address.Address, value, StringComparison.Ordinal) &&
        !address.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
