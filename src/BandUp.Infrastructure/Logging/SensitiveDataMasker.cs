namespace BandUp.Infrastructure.Logging;

/// <summary>
/// Decides which log properties are sensitive and how they are obscured. Every process
/// funnels its log events through here, so a new sink cannot bypass the policy.
/// </summary>
public static class SensitiveDataMasker
{
    private const string Replacement = "***";

    /// <summary>
    /// Property names whose value is never logged. Matched case-insensitively as substrings,
    /// so <c>passwordHash</c> and <c>confirmPassword</c> are both covered.
    /// </summary>
    private static readonly string[] HiddenMarkers =
    [
        "password",
        "token",
        "authorization",
        "cookie",
        "secret",
        "essaytext",
    ];

    private const string EmailMarker = "email";

    public static bool IsHidden(string propertyName) =>
        HiddenMarkers.Any(marker => propertyName.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public static bool IsPartiallyMasked(string propertyName) =>
        propertyName.Contains(EmailMarker, StringComparison.OrdinalIgnoreCase);

    public static bool IsSensitive(string propertyName) =>
        IsHidden(propertyName) || IsPartiallyMasked(propertyName);

    /// <summary>
    /// Obscures a value. Hidden properties keep nothing; partially masked ones keep the first
    /// character of the local part and the whole domain, which is enough to spot a wrong
    /// account without exposing the address.
    /// </summary>
    public static string Mask(string propertyName, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (IsHidden(propertyName))
        {
            return Replacement;
        }

        if (!IsPartiallyMasked(propertyName))
        {
            return value;
        }

        int at = value.IndexOf('@');

        if (at <= 0 || at == value.Length - 1)
        {
            return Replacement;
        }

        return string.Concat(value[0], Replacement, value[at..]);
    }
}