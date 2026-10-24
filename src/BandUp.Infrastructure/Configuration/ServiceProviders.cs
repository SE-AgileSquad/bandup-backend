namespace BandUp.Infrastructure.Configuration;

/// <summary>
/// Values accepted by the <c>&lt;Service&gt;__Provider</c> configuration keys.
/// </summary>
public static class ServiceProviders
{
    public const string Redis = "Redis";
    public const string InMemory = "InMemory";
    public const string RabbitMq = "RabbitMq";

    public static readonly string[] Cache = [Redis, InMemory];
    public static readonly string[] MessagePublisher = [RabbitMq, InMemory];
}