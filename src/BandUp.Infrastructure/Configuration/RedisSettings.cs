namespace BandUp.Infrastructure.Configuration;

public sealed class RedisSettings
{
    public const string SectionName = "Redis";

    public string? ConnectionString { get; set; }
}