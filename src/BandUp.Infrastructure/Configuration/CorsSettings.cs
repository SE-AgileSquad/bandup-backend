namespace BandUp.Infrastructure.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string? AllowedOrigin { get; set; }
}