using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.Logout;

/// <summary>The API supplies the refresh-cookie value; caller identity comes from ICurrentUser.</summary>
public sealed record LogoutCommand(string? RefreshToken) : ICommand<LogoutOutcome>;
