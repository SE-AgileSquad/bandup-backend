using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.ResetPassword;

/// <summary>AUTH-15: verify the emailed code and replace the password in one operation.</summary>
public sealed record ResetPasswordCommand(
    string Email,
    string Code,
    string NewPassword,
    string? ClientIp = null) : ICommand<ResetPasswordOutcome>;
