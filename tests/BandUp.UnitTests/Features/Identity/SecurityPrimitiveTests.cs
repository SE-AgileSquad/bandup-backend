using System.Text;
using BandUp.Application.Abstractions;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

/// <summary>
/// The security primitives the auth handlers will stand on: the password hasher (AUTH-07
/// timing) and the token service (the claims and lifetimes the bearer handler will later
/// validate). Token round-trips are verified by decoding with the same key, the way the API
/// will.
/// </summary>
public sealed class SecurityPrimitiveTests
{
    private static readonly string SigningKey = new('k', 48);

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static AuthSettings Settings() => new()
    {
        SigningKey = SigningKey,
        Issuer = "bandup",
        Audience = "bandup-api",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 7,
    };

    [Fact]
    public void Hash_And_Verify_Round_Trip()
    {
        IPasswordHasher hasher = new BcryptPasswordHasher();

        string hash = hasher.Hash("correct horse battery staple");

        hash.ShouldNotBe("correct horse battery staple");
        hasher.Verify("correct horse battery staple", hash).ShouldBeTrue();
        hasher.Verify("wrong password", hash).ShouldBeFalse();
    }

    [Fact]
    public void Hashes_Are_Salted_And_Cost_12()
    {
        IPasswordHasher hasher = new BcryptPasswordHasher();

        string first = hasher.Hash("same password");
        string second = hasher.Hash("same password");

        first.ShouldNotBe(second);
        first.ShouldStartWith("$2");
        // $2a$12$... : algorithm identifier, work factor, salt. The prefix encodes the cost
        // the ADR fixes.
        first.Split('$', StringSplitOptions.RemoveEmptyEntries)[1].ShouldBe("12");
    }

    [Fact]
    public void Dummy_Hash_Verifies_Like_Any_Other_Wrong_Candidate()
    {
        IPasswordHasher hasher = new BcryptPasswordHasher();

        hasher.Verify("anything", hasher.DummyHash).ShouldBeFalse();
        hasher.DummyHash.ShouldStartWith("$2");
    }

    [Fact]
    public void Access_Token_Carries_Sub_Role_And_Jti_And_Expires_On_Time()
    {
        JwtTokenService service = new(Options.Create(Settings()));
        Guid userId = Guid.CreateVersion7();

        AccessToken token = service.CreateAccessToken(userId, "User", Now);

        token.TokenId.ShouldNotBe(Guid.Empty);
        token.ExpiresAt.ShouldBe(Now.AddMinutes(15));

        JsonWebToken decoded = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);
        decoded.Issuer.ShouldBe("bandup");
        decoded.Audiences.ShouldContain("bandup-api");
        decoded.GetClaim(IdentityClaimTypes.Subject).Value.ShouldBe(userId.ToString());
        decoded.GetClaim(IdentityClaimTypes.Role).Value.ShouldBe("User");
        decoded.GetClaim(IdentityClaimTypes.JwtId).Value.ShouldBe(token.TokenId.ToString());
    }

    [Fact]
    public async Task Access_Tokens_Are_Signed_And_Verifiable_With_The_Same_Key()
    {
        AuthSettings settings = Settings();
        JwtTokenService service = new(Options.Create(settings));
        Guid userId = Guid.CreateVersion7();

        AccessToken token = service.CreateAccessToken(userId, "Admin", Now);

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.Value,
            new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = false,
            });

        result.IsValid.ShouldBeTrue();
        result.Claims
            .Single(claim => claim.Key == IdentityClaimTypes.Role)
            .Value.ShouldBe("Admin");
    }

    [Fact]
    public void Refresh_Tokens_Are_Opaque_Unique_And_Hashed_One_Way()
    {
        ITokenService service = new JwtTokenService(Options.Create(Settings()));

        string first = service.GenerateRefreshToken();
        string second = service.GenerateRefreshToken();

        first.ShouldNotBe(second);
        first.Length.ShouldBe(64);
        first.ShouldMatch("^[0-9a-f]{64}$");

        string firstHash = service.HashRefreshToken(first);
        firstHash.ShouldNotBe(first);
        firstHash.ShouldMatch("^[0-9a-f]{64}$");
        service.HashRefreshToken(first).ShouldBe(firstHash);
        service.HashRefreshToken(second).ShouldNotBe(firstHash);
    }
}
