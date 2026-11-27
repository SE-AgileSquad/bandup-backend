namespace BandUp.Application.Features.Identity;

public interface IPasswordResetTokenProtector
{
    string GenerateToken();
    string HashToken(string resetToken);
}
