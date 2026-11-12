using BandUp.Application.Features.Identity;

namespace BandUp.Application.Features.Identity.Login;

/// <summary>
/// What a successful login produced. The endpoint keeps the tokens (it owns the cookies) and
/// returns the public user shape to the caller.
/// </summary>
public sealed record LoginResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Role,
    AccessToken AccessToken,
    string RefreshToken);
