namespace BandUp.Infrastructure.Configuration;

public sealed class CacheSettings
{
    public const string SectionName = "Cache";

    public string Provider { get; set; } = ServiceProviders.Redis;
}