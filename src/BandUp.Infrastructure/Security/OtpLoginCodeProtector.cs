using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Security;

public sealed class OtpLoginCodeProtector : IOtpLoginCodeProtector
{
    private const string Purpose = OtpLoginPolicy.ProtectionPurpose;
    private readonly byte[] _key;

    public OtpLoginCodeProtector(IOptions<OtpLoginSettings> options)
    {
        if (!TryDecodeKey(options.Value.CodeProtectionKey, out byte[] key))
        {
            throw new OptionsValidationException(
                Options.DefaultName, typeof(OtpLoginSettings),
                ["OtpLogin:CodeProtectionKey must be Base64 encoding at least 32 random bytes."]);
        }

        _key = key;
    }

    public string GenerateCode() =>
        RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public string Protect(Guid challengeId, Guid userId, string code)
    {
        if (challengeId == Guid.Empty || userId == Guid.Empty || !IsCode(code))
        {
            throw new ArgumentException("A challenge id, user id and six-digit code are required.");
        }

        return Convert.ToHexStringLower(ComputeMac(challengeId, userId, code));
    }

    public bool Verify(Guid challengeId, Guid userId, string? code, string? codeMac)
    {
        if (challengeId == Guid.Empty || userId == Guid.Empty || !IsCode(code) ||
            codeMac is not { Length: 64 } ||
            !codeMac.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(codeMac), ComputeMac(challengeId, userId, code!));
    }

    internal static bool IsValidKey(string? value) => TryDecodeKey(value, out _);

    private byte[] ComputeMac(Guid challengeId, Guid userId, string code) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{Purpose}\n{challengeId:D}\n{userId:D}\n{code}"));

    private static bool IsCode(string? code) =>
        code is { Length: OtpLoginPolicy.CodeLength } && code.All(char.IsAsciiDigit);

    private static bool TryDecodeKey(string? value, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(value);
            return key.Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
