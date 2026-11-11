using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.Login;

/// <summary>
/// AUTH-06: a returning user starting a session. The handler's response carries the tokens
/// for the endpoint to set as cookies; the command itself never sees them.
/// </summary>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? CaptchaToken,
    string? ClientIp = null,
    string? UserAgent = null) : ICommand<LoginResult>;
