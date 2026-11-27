using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.ResetPassword;

/// <summary>Replace the password using the authorization from verify-reset-code.</summary>
public sealed record ResetPasswordCommand(
    string ResetToken,
    string NewPassword,
    string? ClientIp = null) : ICommand<ResetPasswordOutcome>;
