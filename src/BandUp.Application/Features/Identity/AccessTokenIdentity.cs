namespace BandUp.Application.Features.Identity;

/// <summary>Metadata from the validated authentication context, never an unverified JWT.</summary>
public sealed record AccessTokenIdentity(Guid UserId, Guid TokenId, DateTimeOffset ExpiresAt);
