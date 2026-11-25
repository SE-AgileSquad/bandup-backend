using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.ForgotPassword;

/// <summary>AUTH-15: request recovery for an email. ClientIp is supplied by the API, never the JSON body.</summary>
public sealed record ForgotPasswordCommand(string Email, string? ClientIp = null) : ICommand<ForgotPasswordOutcome>;
