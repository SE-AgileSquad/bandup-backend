using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.VerifyOtpLogin;

/// <summary>Only Email and Code are client JSON; ClientIp and UserAgent are API metadata.</summary>
public sealed record VerifyOtpLoginCommand(
    string Email,
    string Code,
    string? ClientIp = null,
    string? UserAgent = null) : ICommand<VerifyOtpLoginOutcome>;
