using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Logout;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class LogoutHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IRefreshTokenStore _refresh = Substitute.For<IRefreshTokenStore>();
    private readonly IAccessTokenRevocationStore _access = Substitute.For<IAccessTokenRevocationStore>();
    private readonly AccessTokenIdentity _identity = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Now.AddMinutes(15));

    public LogoutHandlerTests()
    {
        _user.IsAuthenticated.Returns(true);
        _user.UserId.Returns(_identity.UserId);
        _user.AccessToken.Returns(_identity);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing-claims")]
    [InlineData("empty-user")]
    [InlineData("empty-jti")]
    public async Task AUTH_10_Invalid_Authentication_Does_Not_Mutate_Any_Session(string scenario)
    {
        switch (scenario)
        {
            case "anonymous": _user.IsAuthenticated.Returns(false); break;
            case "missing-claims": _user.AccessToken.Returns((AccessTokenIdentity?)null); break;
            case "empty-user": _user.AccessToken.Returns(_identity with { UserId = Guid.Empty }); break;
            case "empty-jti": _user.AccessToken.Returns(_identity with { TokenId = Guid.Empty }); break;
        }

        Result<LogoutOutcome> result = await LogoutAsync("refresh-cookie");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.UnauthorizedCode);
        _tokens.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        _access.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_10_Logout_Uses_Validated_Identity_And_Hashes_The_Refresh_Cookie()
    {
        _tokens.HashRefreshToken("refresh-cookie").Returns("refresh-hash");

        Result<LogoutOutcome> result = await LogoutAsync("refresh-cookie");

        result.IsSuccess.ShouldBeTrue();
        await _refresh.Received(1).RevokeForUserAsync(_identity.UserId, "refresh-hash", Now, CancellationToken.None);
        await _access.Received(1).RevokeAsync(_identity.TokenId, _identity.ExpiresAt, CancellationToken.None);
        await _refresh.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AUTH_10_Missing_Refresh_Cookie_Still_Revokes_The_Access_Token(string? refreshToken)
    {
        (await LogoutAsync(refreshToken)).IsSuccess.ShouldBeTrue();

        _tokens.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        await _access.Received(1).RevokeAsync(_identity.TokenId, _identity.ExpiresAt, CancellationToken.None);
    }

    [Fact]
    public async Task AUTH_10_Validated_Tokens_Within_Clock_Skew_Can_Be_Logged_Out()
    {
        _user.AccessToken.Returns(_identity with { ExpiresAt = Now.AddSeconds(-10) });

        (await LogoutAsync(null)).IsSuccess.ShouldBeTrue();

        await _access.Received(1).RevokeAsync(_identity.TokenId, Now.AddSeconds(-10), CancellationToken.None);
    }

    [Fact]
    public async Task AUTH_10_Refresh_Storage_Failure_Does_Not_Attempt_A_Blacklist_Write()
    {
        _tokens.HashRefreshToken("refresh-cookie").Returns("refresh-hash");
        _refresh.RevokeForUserAsync(_identity.UserId, "refresh-hash", Now, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("test storage failure")));

        await Should.ThrowAsync<InvalidOperationException>(() => LogoutAsync("refresh-cookie"));

        _access.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_10_Blacklist_Write_Failure_Does_Not_Return_A_Completed_Outcome()
    {
        _access.RevokeAsync(_identity.TokenId, _identity.ExpiresAt, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("test blacklist failure")));

        await Should.ThrowAsync<InvalidOperationException>(() => LogoutAsync(null));
    }

    private Task<Result<LogoutOutcome>> LogoutAsync(string? refreshToken) =>
        new LogoutHandler(_user, _tokens, _refresh, _access, new FakeTimeProvider(Now))
            .HandleAsync(new LogoutCommand(refreshToken), CancellationToken.None);
}
