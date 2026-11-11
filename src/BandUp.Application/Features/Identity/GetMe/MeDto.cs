namespace BandUp.Application.Features.Identity.GetMe;

/// <summary>
/// The public user shape shared by register, login and /me: never the password hash, never
/// the tokens.
/// </summary>
public sealed record MeDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    int? CreditBalance);
