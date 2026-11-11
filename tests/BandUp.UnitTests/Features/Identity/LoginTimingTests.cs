using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Login;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

/// <summary>
/// AUTH-07: an unknown email and a wrong password must perform the same amount of work so
/// the response time does not reveal which emails exist. The proof is behavioural, not
/// wall-clock: the handler must call Verify on the password hasher on both paths, and on the
/// unknown-email path it must pass the dummy hash.
/// </summary>
public sealed class LoginTimingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly ICaptchaVerifier _captcha = Substitute.For<ICaptchaVerifier>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IRefreshTokenStore _refreshTokens = Substitute.For<IRefreshTokenStore>();
    private readonly FakeTimeProvider _time = new(Now);

    public LoginTimingTests()
    {
        _captcha.VerifyAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CaptchaOutcome.Valid);
    }

    [Fact]
    public async Task AUTH_07_Unknown_Email_Verifies_Against_The_Dummy_Hash()
    {
        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.DummyHash.Returns("dummy-hash");
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        IUserRepository users = Substitute.For<IUserRepository>();
        users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        LoginHandler handler = new(users, hasher, _captcha, _tokens, _refreshTokens, _time);

        Result<LoginResult> result = await handler.HandleAsync(
            new LoginCommand("nobody@example.com", "anyPassword1", "token"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("INVALID_CREDENTIALS");

        // The hasher must be called exactly once, with the dummy hash — proving the
        // unknown-email path does the same cryptographic work as the wrong-password path.
        hasher.Received(1).Verify("anyPassword1", "dummy-hash");
    }

    [Fact]
    public async Task AUTH_07_Wrong_Password_Verifies_Against_The_Real_Hash()
    {
        User existing = User.Create("known@example.com", "Known", "real-hash", Now);

        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.DummyHash.Returns("dummy-hash");
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        IUserRepository users = Substitute.For<IUserRepository>();
        users.FindByEmailAsync("known@example.com", Arg.Any<CancellationToken>())
            .Returns(existing);

        LoginHandler handler = new(users, hasher, _captcha, _tokens, _refreshTokens, _time);

        Result<LoginResult> result = await handler.HandleAsync(
            new LoginCommand("known@example.com", "wrongPassword1", "token"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("INVALID_CREDENTIALS");

        // The hasher must be called exactly once, with the real hash — proving the
        // wrong-password path does the same cryptographic work as the unknown-email path.
        hasher.Received(1).Verify("wrongPassword1", "real-hash");
    }
}
