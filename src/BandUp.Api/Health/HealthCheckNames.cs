namespace BandUp.Api.Health;

public static class HealthCheckNames
{
    public const string Postgres = "postgres";
    public const string Cache = "redis";
    public const string RabbitMq = "rabbitmq";
    public const string Migrations = "migrations";

    /// <summary>
    /// Checks that prove a dependency is reachable. Reported by both /health and /ready.
    /// </summary>
    public const string DependencyTag = "dependency";

    /// <summary>
    /// Checks that only /ready runs, because they gate traffic rather than describe health.
    /// </summary>
    public const string ReadyTag = "ready";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
}