using System.Globalization;
using System.Security.Claims;
using BandUp.Api.Auth;
using BandUp.Application.Features.Identity;
using BandUp.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace BandUp.IntegrationTests;

public sealed class CurrentUserTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid TokenId = Guid.CreateVersion7();
    private static readonly DateTimeOffset ExpiresAt = new(2026, 11, 16, 8, 15, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AUTH_10_Current_User_Reads_Validated_Raw_Or_Mapped_Claims(bool mapped)
    {
        Claim[] claims = Claims(mapped);
        CurrentUser current = Create(claims, authenticated: true);

        current.IsAuthenticated.ShouldBeTrue();
        current.UserId.ShouldBe(UserId);
        current.Role.ShouldBe("User");
        current.AccessToken.ShouldBe(new AccessTokenIdentity(UserId, TokenId, ExpiresAt));
    }

    [Fact]
    public void AUTH_10_Anonymous_Claims_And_Worker_Context_Do_Not_Expose_Session_Metadata()
    {
        CurrentUser anonymous = Create(Claims(), authenticated: false);
        anonymous.IsAuthenticated.ShouldBeFalse();
        anonymous.UserId.ShouldBeNull();
        anonymous.Role.ShouldBeNull();
        anonymous.AccessToken.ShouldBeNull();
        new NullCurrentUser().AccessToken.ShouldBeNull();
    }

    [Theory]
    [InlineData("missing-sub")]
    [InlineData("invalid-sub")]
    [InlineData("empty-sub")]
    [InlineData("missing-jti")]
    [InlineData("invalid-jti")]
    [InlineData("empty-jti")]
    [InlineData("missing-exp")]
    [InlineData("invalid-exp")]
    [InlineData("overflow-exp")]
    [InlineData("out-of-range-exp")]
    [InlineData("duplicate-sub")]
    [InlineData("duplicate-jti")]
    [InlineData("duplicate-exp")]
    public void AUTH_10_Incomplete_Or_Ambiguous_Claims_Do_Not_Produce_A_Session(string scenario)
    {
        List<Claim> claims = Claims().ToList();
        string type = scenario.EndsWith("sub", StringComparison.Ordinal) ? IdentityClaimTypes.Subject
            : scenario.EndsWith("jti", StringComparison.Ordinal) ? IdentityClaimTypes.JwtId : IdentityClaimTypes.Expiry;

        if (!scenario.StartsWith("duplicate", StringComparison.Ordinal))
        {
            claims.RemoveAll(claim => claim.Type == type);
        }

        if (!scenario.StartsWith("missing", StringComparison.Ordinal))
        {
            string value = scenario switch
            {
                "empty-sub" or "empty-jti" => Guid.Empty.ToString(),
                "overflow-exp" => "99999999999999999999999",
                "out-of-range-exp" => long.MaxValue.ToString(CultureInfo.InvariantCulture),
                _ => "invalid-or-duplicate",
            };
            claims.Add(new Claim(type, value));
        }

        CurrentUser current = Create(claims, authenticated: true);
        current.AccessToken.ShouldBeNull();
        current.UserId.ShouldBeNull();
    }

    private static Claim[] Claims(bool mapped = false) =>
    [
        new(mapped ? ClaimTypes.NameIdentifier : IdentityClaimTypes.Subject, UserId.ToString()),
        new(IdentityClaimTypes.JwtId, TokenId.ToString()),
        new(IdentityClaimTypes.Expiry, ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        new(mapped ? ClaimTypes.Role : IdentityClaimTypes.Role, "User"),
    ];

    private static CurrentUser Create(IEnumerable<Claim> claims, bool authenticated) => new(new StubHttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "validated-test-scheme" : null)),
        },
    });

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
