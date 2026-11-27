using System.Security.Cryptography;
using System.Text;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Security;

public sealed class PasswordResetTokenProtector : IPasswordResetTokenProtector
{
    private readonly byte[] _key;

    public PasswordResetTokenProtector(IOptions<PasswordResetSettings> options)
    {
        if (!PasswordResetCodeProtector.IsValidKey(options.Value.CodeProtectionKey))
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(PasswordResetSettings),
                ["PasswordReset:CodeProtectionKey must be Base64 encoding at least 32 random bytes."]);
        }
        _key = Convert.FromBase64String(options.Value.CodeProtectionKey!);
    }

    public string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string HashToken(string resetToken)
    {
        if (!PasswordResetPolicy.IsResetToken(resetToken)) { throw new ArgumentException("A valid reset token is required."); }
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key,
            Encoding.UTF8.GetBytes($"bandup:password-reset:authorization:v1\n{resetToken}")));
    }
}
