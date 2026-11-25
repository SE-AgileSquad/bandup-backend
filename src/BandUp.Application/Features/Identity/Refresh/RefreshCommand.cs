using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.Refresh;

/// <summary>
/// Renews a session using the refresh-cookie value supplied by the API. No authenticated
/// access token is required. Missing credentials are an auth rejection, not a validation error.
/// </summary>
public sealed record RefreshCommand(string? RefreshToken, string? UserAgent = null) : ICommand<RefreshOutcome>;
