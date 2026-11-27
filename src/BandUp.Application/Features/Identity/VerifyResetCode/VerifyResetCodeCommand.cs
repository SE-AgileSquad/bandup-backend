using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.VerifyResetCode;

public sealed record VerifyResetCodeCommand(string Email, string Code, string? ClientIp = null) : ICommand<VerifyResetCodeOutcome>;
