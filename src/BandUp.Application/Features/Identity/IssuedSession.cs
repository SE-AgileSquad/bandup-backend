using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>Token material for internal use; never serialize or log this result.</summary>
public sealed record IssuedSession(
    AccessToken AccessToken,
    string RawRefreshToken,
    RefreshToken RefreshToken);
