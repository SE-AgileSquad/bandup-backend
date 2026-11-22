namespace BandUp.Infrastructure.Configuration;

/// <summary>
/// Values accepted by the <c>&lt;Service&gt;__Provider</c> configuration keys.
/// </summary>
public static class ServiceProviders
{
    public const string Redis = "Redis";
    public const string InMemory = "InMemory";
    public const string RabbitMq = "RabbitMq";
    public const string Turnstile = "Turnstile";
    public const string Fake = "Fake";
    public const string Mailpit = "Mailpit";
    public const string Resend = "Resend";

    public static readonly string[] Cache = [Redis, InMemory];
    public static readonly string[] MessagePublisher = [RabbitMq, InMemory];
    public static readonly string[] Captcha = [Turnstile, Fake];
    public static readonly string[] Email = [InMemory, Mailpit, Resend];
    public static readonly string[] PasswordResetRateLimit = [Redis, InMemory];
}
