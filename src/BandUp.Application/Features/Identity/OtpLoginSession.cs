using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>Fresh tracked state obtained under the user's session lock, including ineligible/closed state.</summary>
public sealed record OtpLoginSession(User User, OtpLoginChallenge? Challenge);
