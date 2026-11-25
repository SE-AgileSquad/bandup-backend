using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Refresh;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class RefreshHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private const string RawToken = "presented-refresh-credential";
    private const string TokenHash = "hash-of-presented-credential";
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IRefreshTokenStore _store = Substitute.For<IRefreshTokenStore>();
    private readonly ISessionTokenIssuer _issuer = Substitute.For<ISessionTokenIssuer>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ILogger<RefreshHandler> _logger = Substitute.For<ILogger<RefreshHandler>>();

    public RefreshHandlerTests() => _tokens.HashRefreshToken(RawToken).Returns(TokenHash);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AUTH_09_Missing_Credentials_Are_Unauthorized_Without_Session_Work(string? token)
    {
        Result<RefreshOutcome> result = await CreateHandler().HandleAsync(new RefreshCommand(token), CancellationToken.None);

        AssertInvalid(result);
        _tokens.ReceivedCalls().ShouldBeEmpty();
        _store.ReceivedCalls().ShouldBeEmpty();
        _issuer.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_09_Unknown_Credentials_Are_Rejected_Without_Issuance_Or_Revocation()
    {
        Result<RefreshOutcome> result = await RefreshAsync();

        AssertInvalid(result);
        await _store.Received(1).FindSessionForUpdateAsync(TokenHash, CancellationToken.None);
        AssertNoIssuanceOrMutation();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task AUTH_09_Expired_Unrevoked_Credentials_Are_Rejected(int expiresInSeconds)
    {
        RefreshSession session = CreateSession(expiresInSeconds);
        SetSession(session);

        AssertInvalid(await RefreshAsync());

        session.Token.RevokedAt.ShouldBeNull();
        AssertNoIssuanceOrMutation();
    }

    [Fact]
    public async Task AUTH_09_Inactive_User_Does_Not_Receive_A_New_Session()
    {
        RefreshSession session = CreateSession();
        session.User.Deactivate();
        SetSession(session);

        AssertInvalid(await RefreshAsync());

        session.Token.RevokedAt.ShouldBeNull();
        AssertNoIssuanceOrMutation();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AUTH_09_Reuse_Completes_Revocation_Even_For_Expired_Tokens_Or_Inactive_Users(
        bool expired,
        bool inactive)
    {
        RefreshSession session = CreateSession(expired ? -1 : 60);
        session.Token.Revoke(Now.AddMinutes(-1));

        if (inactive)
        {
            session.User.Deactivate();
        }

        SetSession(session);
        Result<RefreshOutcome> result = await RefreshAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<RefreshOutcome.RejectedAfterRevocation>();
        await _store.Received(1).RevokeAllForUserAsync(session.User.Id, Now, CancellationToken.None);
        _issuer.ReceivedCalls().ShouldBeEmpty();
        await _store.DidNotReceive().AddAsync(Arg.Any<RefreshToken>(), Arg.Any<CancellationToken>());
        session.Token.RevokedAt.ShouldBe(Now.AddMinutes(-1));

        var log = _logger.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
        log.GetArguments()[0].ShouldBe(LogLevel.Warning);
        object? state = log.GetArguments()[2];
        state!.ToString()!.ShouldNotContain(RawToken);
        state.ToString()!.ShouldNotContain(TokenHash);
        var properties = ((IEnumerable<KeyValuePair<string, object?>>)state).ToDictionary();
        properties["UserId"].ShouldBe(session.User.Id);
    }

    [Fact]
    public async Task AUTH_09_Usable_Credentials_Rotate_And_Return_Only_The_New_Session_Material()
    {
        RefreshSession session = CreateSession();
        session.User.ChangeRole(Role.Admin);
        SetSession(session);
        RefreshToken successor = RefreshToken.Issue(session.User.Id, "new-hash", Now, Now.AddDays(3), "browser");
        AccessToken accessToken = new("new-access-token", Guid.CreateVersion7(), Now.AddMinutes(5));
        _issuer.Issue(session.User, Now, "browser").Returns(new IssuedSession(accessToken, "new-refresh-token", successor));

        Result<RefreshOutcome> result = await CreateHandler().HandleAsync(
            new RefreshCommand(RawToken, "browser"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        RefreshOutcome.Issued issued = result.Value.ShouldBeOfType<RefreshOutcome.Issued>();
        issued.AccessToken.ShouldBe(accessToken);
        issued.RefreshToken.ShouldBe("new-refresh-token");
        issued.RefreshTokenExpiresAt.ShouldBe(successor.ExpiresAt);
        session.Token.RevokedAt.ShouldBe(Now);
        session.Token.ReplacedById.ShouldBe(successor.Id);
        _issuer.Received(1).Issue(session.User, Now, "browser");
        await _store.Received(1).AddAsync(successor, CancellationToken.None);
        await _store.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        session.User.LastLoginAt.ShouldBeNull();
    }

    [Fact]
    public async Task AUTH_09_Credentials_That_Expire_While_Waiting_For_The_Lock_Are_Rejected()
    {
        RefreshSession session = CreateSession(expiresInSeconds: 1);
        _store.FindSessionForUpdateAsync(TokenHash, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult<RefreshSession?>(session);
        });

        AssertInvalid(await RefreshAsync());
        AssertNoIssuanceOrMutation();
    }

    [Fact]
    public async Task AUTH_09_Revocation_Failure_Propagates_Without_Claiming_A_Completed_Outcome()
    {
        RefreshSession session = CreateSession();
        session.Token.Revoke(Now.AddMinutes(-1));
        SetSession(session);
        _store.RevokeAllForUserAsync(session.User.Id, Now, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("test storage failure")));

        await Should.ThrowAsync<InvalidOperationException>(() => RefreshAsync());

        _issuer.ReceivedCalls().ShouldBeEmpty();
        _logger.ReceivedCalls().ShouldBeEmpty();
    }

    private RefreshHandler CreateHandler() => new(_tokens, _store, _issuer, _time, _logger);

    private Task<Result<RefreshOutcome>> RefreshAsync() =>
        CreateHandler().HandleAsync(new RefreshCommand(RawToken), CancellationToken.None);

    private static RefreshSession CreateSession(int expiresInSeconds = 60)
    {
        User user = User.Create("refresh@example.com", "Refresh", "test-hash", Now.AddDays(-1));
        RefreshToken token = RefreshToken.Issue(user.Id, TokenHash, Now.AddDays(-1), Now.AddSeconds(expiresInSeconds), null);
        return new RefreshSession(user, token);
    }

    private void SetSession(RefreshSession session) =>
        _store.FindSessionForUpdateAsync(TokenHash, Arg.Any<CancellationToken>()).Returns(session);

    private static void AssertInvalid(Result<RefreshOutcome> result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(Error.InvalidRefreshTokenCode);
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
    }

    private void AssertNoIssuanceOrMutation()
    {
        _issuer.ReceivedCalls().ShouldBeEmpty();
        _store.ReceivedCalls().ShouldAllBe(call => call.GetMethodInfo().Name == nameof(IRefreshTokenStore.FindSessionForUpdateAsync));
    }
}
