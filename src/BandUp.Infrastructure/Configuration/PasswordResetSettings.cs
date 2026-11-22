namespace BandUp.Infrastructure.Configuration;

public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";

    /// <summary>Base64-encoded random key of at least 32 bytes, independent of Auth:SigningKey.</summary>
    public string? CodeProtectionKey { get; set; }
}
