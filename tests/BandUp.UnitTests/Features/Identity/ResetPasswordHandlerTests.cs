using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.ResetPassword;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class ResetPasswordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Password = "NewPassword123!";
    private static readonly string Raw = new('A', 43);
    private static readonly string Hash = new('b', 64);
    private readonly IPasswordResetRateLimiter _limits = Substitute.For<IPasswordResetRateLimiter>();
    private readonly IPasswordResetChallengeStore _challenges = Substitute.For<IPasswordResetChallengeStore>();
    private readonly IPasswordResetTokenProtector _protector = Substitute.For<IPasswordResetTokenProtector>();
    private readonly IPasswordResetTokenStore _tokens = Substitute.For<IPasswordResetTokenStore>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenStore _refresh = Substitute.For<IRefreshTokenStore>();
    private readonly IOtpLoginChallengeStore _login = Substitute.For<IOtpLoginChallengeStore>();
    private readonly IPostCommitEmailQueue _emails = Substitute.For<IPostCommitEmailQueue>();
    private readonly FakeTimeProvider _time = new(Now);

    public ResetPasswordHandlerTests()
    {
        _protector.HashToken(Raw).Returns(Hash);
        _hasher.Hash(Password).Returns("new-password-hash");
    }

    [Fact]
    public async Task AUTH_17_Reset_Consumes_Authorization_And_Revokes_Sessions_Before_Notification()
    {
        var session = Session();
        _tokens.FindForUpdateAsync(Hash, CancellationToken.None).Returns(session);
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Completed>();
        session.User.PasswordHash.ShouldBe("new-password-hash");
        session.Token.ConsumedAt.ShouldBe(Now);
        Received.InOrder(() =>
        {
            _limits.AcquireResetAsync(Hash, "203.0.113.7", CancellationToken.None);
            _tokens.FindForUpdateAsync(Hash, CancellationToken.None);
            _hasher.Hash(Password);
            _tokens.InvalidateOthersAsync(session.User.Id, session.Token.Id, Now, CancellationToken.None);
            _challenges.InvalidateAllForUserAsync(session.User.Id, Now, CancellationToken.None);
            _refresh.RevokeAllForUserAsync(session.User.Id, Now, CancellationToken.None);
            _login.InvalidateAllForUserAsync(session.User.Id, Now, CancellationToken.None);
            _emails.Enqueue(Arg.Is<EmailMessage>(message => message.To == session.User.Email && !message.TextBody.Contains(Password) && !message.TextBody.Contains(Raw)), null);
        });
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    public async Task AUTH_17_Ineligible_Tokens_Reject_Without_Password_Or_Session_Effects(string state)
    {
        var session = Session(state == "future" ? Now.AddMinutes(2) : Now);
        if (state == "inactive") { session.User.Deactivate(); }
        if (state == "expired") { _time.Advance(TimeSpan.FromMinutes(5)); }
        if (state == "consumed") { session.Token.Consume(Now); }
        if (state == "invalidated") { session.Token.Invalidate(Now); }
        _tokens.FindForUpdateAsync(Hash, CancellationToken.None).Returns(state == "unknown" ? null : session);
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        session.User.PasswordHash.ShouldBe("old-hash");
        AssertNoEffects();
    }

    [Theory]
    [InlineData(PasswordResetRateLimitDecision.AccountSuppressed, true)]
    [InlineData(PasswordResetRateLimitDecision.ClientRateLimited, false)]
    public async Task AUTH_17_Reset_Quotas_Precede_Token_Lookup(PasswordResetRateLimitDecision decision, bool success)
    {
        _limits.AcquireResetAsync(Hash, "203.0.113.7", CancellationToken.None).Returns(decision);
        var result = await HandleAsync();
        result.IsSuccess.ShouldBe(success);
        if (success) { result.Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>(); }
        else { result.Error!.Code.ShouldBe(Error.PasswordResetRateLimitedCode); }
        _tokens.ReceivedCalls().ShouldBeEmpty();
        AssertNoEffects();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_17_Expiry_During_Lock_Wait_Or_Hashing_Does_Not_Consume_The_Token(bool duringHash)
    {
        var session = Session();
        _tokens.FindForUpdateAsync(Hash, CancellationToken.None).Returns(_ =>
        {
            if (!duringHash) { _time.Advance(TimeSpan.FromMinutes(5)); }
            return session;
        });
        _hasher.Hash(Password).Returns(_ => { _time.Advance(TimeSpan.FromMinutes(5)); return "unused-hash"; });
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        session.Token.ConsumedAt.ShouldBeNull();
        session.User.PasswordHash.ShouldBe("old-hash");
        _refresh.ReceivedCalls().ShouldBeEmpty();
        _login.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_17_Quota_Failure_Fails_Closed()
    {
        _limits.AcquireResetAsync(Hash, "203.0.113.7", CancellationToken.None)
            .Returns(Task.FromException<PasswordResetRateLimitDecision>(new InvalidOperationException("quota failed")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _tokens.ReceivedCalls().ShouldBeEmpty();
        AssertNoEffects();
    }

    [Fact]
    public async Task AUTH_17_Revocation_Failure_Does_Not_Schedule_A_Notification()
    {
        var session = Session();
        _tokens.FindForUpdateAsync(Hash, CancellationToken.None).Returns(session);
        _refresh.RevokeAllForUserAsync(session.User.Id, Now, CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("revocation failed")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<ResetPasswordOutcome>> HandleAsync() =>
        new ResetPasswordHandler(_limits, _challenges, _protector, _tokens, _hasher, _refresh, _login, _emails, _time)
            .HandleAsync(new ResetPasswordCommand(Raw, Password, "203.0.113.7"), CancellationToken.None);

    private void AssertNoEffects()
    {
        _hasher.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        _login.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
        _challenges.ReceivedCalls().ShouldBeEmpty();
    }

    private static PasswordResetTokenSession Session(DateTimeOffset? at = null)
    {
        User user = User.Create("reset@example.com", "Learner", "old-hash", Now.AddDays(-1));
        DateTimeOffset issued = at ?? Now;
        var challenge = PasswordResetChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('a', 64), issued.AddMinutes(-1), issued.AddMinutes(9), 5);
        challenge.Consume(issued);
        return new(user, PasswordResetToken.Issue(challenge, Hash, issued, issued.AddMinutes(5)));
    }
}
