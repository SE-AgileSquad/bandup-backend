namespace BandUp.Infrastructure.Configuration;

public sealed class DatabaseSettings
{
    public const string SectionName = "ConnectionStrings";

    public string? Default { get; set; }
}