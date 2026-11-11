using BandUp.Application.Abstractions;

namespace BandUp.Application.Features.Identity.Register;

/// <summary>
/// AUTH-01: a visitor creating an account. The CAPTCHA token arrives from the widget and is
/// verified server-side before anything is written.
/// </summary>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string? CaptchaToken,
    string? ClientIp) : ICommand<Guid>;
