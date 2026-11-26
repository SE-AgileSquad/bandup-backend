using System.Net;
using System.Security.Cryptography;
using System.Text;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Security;

public sealed class OtpLoginRateLimitKeys
{
    private readonly byte[] _key;

    public OtpLoginRateLimitKeys(IOptions<OtpLoginSettings> options)
    {
        string? key = options.Value.CodeProtectionKey;
        if (!OtpLoginCodeProtector.IsValidKey(key))
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(OtpLoginSettings),
                ["OtpLogin:CodeProtectionKey must be Base64 encoding at least 32 random bytes."]);
        }
        _key = Convert.FromBase64String(key!);
    }

    public string Email(string email) => Hash("email", email.Trim().ToUpperInvariant());

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
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"bandup:otp-login:quota:v1\n{purpose}\n{value}")));
}
