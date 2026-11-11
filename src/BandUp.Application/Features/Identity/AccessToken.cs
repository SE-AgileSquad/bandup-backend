namespace BandUp.Application.Features.Identity;

/// <summary>
/// A signed access token with the two facts logout needs to revoke it: the identifier inside it,
/// and when it expires. The blacklist entry lives no longer than the token itself.
/// </summary>
public sealed record AccessToken(string Value, Guid TokenId, DateTimeOffset ExpiresAt);