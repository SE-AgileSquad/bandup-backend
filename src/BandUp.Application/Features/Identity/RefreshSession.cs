using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>
/// A fresh user snapshot and tracked refresh token read while holding the user's session
/// lock. The lock is held until the caller's transaction commits or rolls back.
/// </summary>
public sealed record RefreshSession(User User, RefreshToken Token);
