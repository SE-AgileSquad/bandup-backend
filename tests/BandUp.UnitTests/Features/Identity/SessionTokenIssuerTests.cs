using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Domain.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class SessionTokenIssuerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("browser")]
    [InlineData("long")]
    public void AUTH_06_Session_Uses_Configured_Lifetimes_Current_Role_And_Only_Persists_A_Hash(string? agent)
    {
        DateTimeOffset now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        AuthSettings settings = new()
        {
            SigningKey = new string('k', 48),
            AccessTokenMinutes = 5,
            RefreshTokenDays = 3,
        };
        ITokenService tokens = new JwtTokenService(Options.Create(settings));
        ISessionTokenIssuer issuer = new SessionTokenIssuer(tokens, Options.Create(settings));
        User user = User.Create("issuer@example.com", "Issuer", "test-hash", now);
        user.ChangeRole(Role.Admin);
        string? userAgent = agent == "long" ? new string('a', 240) : agent;

        IssuedSession session = issuer.Issue(user, now, userAgent);

        session.AccessToken.ExpiresAt.ShouldBe(now.AddMinutes(5));
        JsonWebToken decoded = new JsonWebTokenHandler().ReadJsonWebToken(session.AccessToken.Value);
        decoded.GetClaim(IdentityClaimTypes.Subject).Value.ShouldBe(user.Id.ToString());
        decoded.GetClaim(IdentityClaimTypes.Role).Value.ShouldBe("Admin");
        session.RefreshToken.UserId.ShouldBe(user.Id);
        session.RefreshToken.CreatedAt.ShouldBe(now);
        session.RefreshToken.ExpiresAt.ShouldBe(now.AddDays(3));
        session.RefreshToken.TokenHash.ShouldBe(tokens.HashRefreshToken(session.RawRefreshToken));
        session.RefreshToken.TokenHash.ShouldNotBe(session.RawRefreshToken);
        session.RefreshToken.UserAgent.ShouldBe(agent switch
        {
            "long" => new string('a', 200),
            "browser" => "browser",
            _ => null,
        });
    }
}
