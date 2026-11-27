using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

public sealed record PasswordResetTokenSession(User User, PasswordResetToken Token);
