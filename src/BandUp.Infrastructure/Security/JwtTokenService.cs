using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// Signs access tokens with HS256 and mints opaque refresh tokens. The signing key never leaves
/// this class; nothing here reads configuration on the request path.
/// </summary>
public sealed class JwtTokenService(IOptions<AuthSettings> options) : ITokenService
{
    private readonly SymmetricSecurityKey _key = CreateKey(options.Value.SigningKey!);
    private readonly string _issuer = options.Value.Issuer;
    private readonly string _audience = options.Value.Audience;
    private readonly TimeSpan _accessLifetime = TimeSpan.FromMinutes(options.Value.AccessTokenMinutes);
    private readonly JsonWebTokenHandler _handler = new();

    private static SymmetricSecurityKey CreateKey(string signingKey) =>
        new(Encoding.UTF8.GetBytes(signingKey));

    public AccessToken CreateAccessToken(Guid userId, string role, DateTimeOffset now)
    {
        Guid tokenId = Guid.CreateVersion7();
        DateTimeOffset expiresAt = now.Add(_accessLifetime);

        Claim[] claims =
        [
            new(IdentityClaimTypes.Subject, userId.ToString()),
            new(IdentityClaimTypes.Role, role),
            new(IdentityClaimTypes.JwtId, tokenId.ToString()),
        ];

        SecurityTokenDescriptor descriptor = new()
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256Signature),
        };

        string token = _handler.CreateToken(descriptor);

        return new AccessToken(token, tokenId, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string HashRefreshToken(string rawToken)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}