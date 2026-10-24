namespace BandUp.Infrastructure.Configuration;

public sealed class FeaturesSettings
{
    public const string SectionName = "Features";

    public bool Diagnostics { get; set; }
}