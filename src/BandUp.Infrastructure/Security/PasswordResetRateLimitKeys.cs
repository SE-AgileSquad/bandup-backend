using System.Net;
using System.Security.Cryptography;
using System.Text;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Security;

public sealed class PasswordResetRateLimitKeys
{
    private readonly byte[] _key;

    public PasswordResetRateLimitKeys(IOptions<PasswordResetSettings> options)
    {
        string? key = options.Value.CodeProtectionKey;
        if (!PasswordResetCodeProtector.IsValidKey(key))
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(PasswordResetSettings),
                ["PasswordReset:CodeProtectionKey must be Base64 encoding at least 32 random bytes."]);
        }
        _key = Convert.FromBase64String(key!);
    }

    public string Email(string email) => Hash("email", email.Trim().ToUpperInvariant());
    public string ResetToken(string tokenHash) => Hash("reset-token", tokenHash);

    public string ClientIp(string? clientIp)
    {
        string normalized = "unknown";
        if (IPAddress.TryParse(clientIp, out IPAddress? address))
        {
            normalized = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        }
        return Hash("ip", normalized);
    }

    private string Hash(string purpose, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"bandup:password-reset:quota:v1\n{purpose}\n{value}")));
}
