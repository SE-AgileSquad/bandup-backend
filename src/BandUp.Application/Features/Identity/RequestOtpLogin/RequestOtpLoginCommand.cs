using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.RequestOtpLogin;

/// <summary>ClientIp comes from the API connection, never from request JSON.</summary>
public sealed record RequestOtpLoginCommand(
    string Email,
    string? CaptchaToken,
    string? ClientIp = null) : ICommand<RequestOtpLoginOutcome>;
