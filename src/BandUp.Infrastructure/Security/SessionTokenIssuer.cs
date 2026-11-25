using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Security;

public sealed class SessionTokenIssuer(ITokenService tokens, IOptions<AuthSettings> options)
    : ISessionTokenIssuer
{
    private readonly TimeSpan _refreshLifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);

    public IssuedSession Issue(User user, DateTimeOffset now, string? userAgent)
    {
        AccessToken accessToken = tokens.CreateAccessToken(user.Id, user.Role.ToString(), now);
        string rawRefreshToken = tokens.GenerateRefreshToken();
        RefreshToken refreshToken = RefreshToken.Issue(
            user.Id,
            tokens.HashRefreshToken(rawRefreshToken),
            now,
            now.Add(_refreshLifetime),
            userAgent is { Length: > 0 } agent ? agent[..Math.Min(agent.Length, 200)] : null);

        return new IssuedSession(accessToken, rawRefreshToken, refreshToken);
    }
}
