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
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private const string Email = "reset@example.com";
    private const string Ip = "203.0.113.7";
    private const string Password = "NewPassword123!";
    private readonly IPasswordResetRateLimiter _limits = Substitute.For<IPasswordResetRateLimiter>();
    private readonly IPasswordResetChallengeStore _store = Substitute.For<IPasswordResetChallengeStore>();
    private readonly IPasswordResetCodeProtector _codes = Substitute.For<IPasswordResetCodeProtector>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IRefreshTokenStore _refresh = Substitute.For<IRefreshTokenStore>();
    private readonly IPostCommitEmailQueue _emails = Substitute.For<IPostCommitEmailQueue>();
    private readonly FakeTimeProvider _time = new(Now);

    public ResetPasswordHandlerTests()
    {
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(true);
        _hasher.Hash(Password).Returns("new-password-hash");
    }

    [Theory]
    [InlineData(PasswordResetRateLimitDecision.AccountSuppressed, true)]
    [InlineData(PasswordResetRateLimitDecision.ClientRateLimited, false)]
    public async Task AUTH_15_Verification_Quota_Is_Checked_Before_Account_Lookup(PasswordResetRateLimitDecision decision, bool completedResult)
    {
        _limits.AcquireVerificationAsync(Email, Ip, CancellationToken.None).Returns(decision);
        var result = await HandleAsync();
        result.IsSuccess.ShouldBe(completedResult);
        if (completedResult)
        {
            result.Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        }
        else
        {
            result.Error!.Code.ShouldBe(Error.PasswordResetRateLimitedCode);
        }
        _store.ReceivedCalls().ShouldBeEmpty();
        AssertNoPasswordOrSessionEffects();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task AUTH_15_Unknown_Inactive_Missing_Expired_Closed_And_Future_Challenges_Share_One_Rejection(int state)
    {
        User user = NewUser();
        PasswordResetChallenge challenge = NewChallenge(user.Id);
        if (state == 1)
        {
            user.Deactivate();
        }
        if (state == 3)
        {
            _time.Advance(TimeSpan.FromMinutes(10));
        }
        if (state == 4)
        {
            challenge.Consume(Now);
        }
        if (state == 5)
        {
            challenge.Invalidate(Now);
        }
        if (state == 6)
        {
            for (int index = 0; index < 5; index++)
            {
                challenge.RecordFailedAttempt(Now);
            }
        }
        if (state == 7)
        {
            challenge = PasswordResetChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('a', 64),
                Now.AddSeconds(1), Now.AddMinutes(10), 5);
        }
        if (state != 0)
        {
            _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(new PasswordResetSession(user, state == 2 ? null : challenge));
        }
        int previousAttempts = challenge.FailedAttempts;
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        user.PasswordHash.ShouldBe("old-password-hash");
        challenge.FailedAttempts.ShouldBe(previousAttempts);
        _codes.ReceivedCalls().ShouldBeEmpty();
        AssertNoPasswordOrSessionEffects();
    }

    [Fact]
    public async Task AUTH_15_Incorrect_Guesses_Use_A_Committing_Rejection_And_Exhaust_The_Challenge()
    {
        var session = SetSession();
        _codes.Verify(session.Challenge!.Id, session.User.Id, "012345", session.Challenge.CodeMac).Returns(false);
        for (int count = 1; count <= 5; count++)
        {
            (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
            session.Challenge.FailedAttempts.ShouldBe(count);
        }
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        session.Challenge.FailedAttempts.ShouldBe(5);
        session.Challenge.ConsumedAt.ShouldBeNull();
        _codes.Received(5).Verify(session.Challenge.Id, session.User.Id, "012345", session.Challenge.CodeMac);
        AssertNoPasswordOrSessionEffects();
    }

    [Fact]
    public async Task AUTH_15_Valid_Code_Stages_Password_Consumption_Revocation_And_Notification_In_Order()
    {
        var session = SetSession();
        var result = await HandleAsync();
        result.Value.ShouldBeOfType<ResetPasswordOutcome.Completed>();
        session.User.PasswordHash.ShouldBe("new-password-hash");
        session.Challenge!.ConsumedAt.ShouldBe(Now);
        Received.InOrder(() =>
        {
            _hasher.Hash(Password);
            _store.InvalidateOthersAsync(session.User.Id, session.Challenge.Id, Now, CancellationToken.None);
            _refresh.RevokeAllForUserAsync(session.User.Id, Now, CancellationToken.None);
            _emails.Enqueue(Arg.Is<EmailMessage>(message => message.To == Email &&
                !message.TextBody.Contains(Password) && !message.TextBody.Contains("012345")), null);
        });
    }

    [Fact]
    public async Task AUTH_15_A_Code_That_Expires_During_Hashing_Does_Not_Change_Anything()
    {
        var session = SetSession();
        _hasher.Hash(Password).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            return "new-password-hash";
        });
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        session.User.PasswordHash.ShouldBe("old-password-hash");
        session.Challenge!.ConsumedAt.ShouldBeNull();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_15_Expiry_After_A_Lock_Wait_Is_Checked_Before_Verification_And_Hashing()
    {
        var session = SetSession();
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            return session;
        });
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        _codes.ReceivedCalls().ShouldBeEmpty();
        AssertNoPasswordOrSessionEffects();
    }

    [Fact]
    public async Task AUTH_15_Expiry_During_Verification_Does_Not_Increment_A_Closed_Challenge()
    {
        var session = SetSession();
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromMinutes(10));
            return false;
        });
        (await HandleAsync()).Value.ShouldBeOfType<ResetPasswordOutcome.Rejected>();
        session.Challenge!.FailedAttempts.ShouldBe(0);
        AssertNoPasswordOrSessionEffects();
    }

    [Fact]
    public async Task AUTH_15_Revocation_Failure_Does_Not_Schedule_A_Notification()
    {
        var session = SetSession();
        _refresh.RevokeAllForUserAsync(session.User.Id, Now, CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("revocation failed")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_15_Quota_Outage_Fails_Closed()
    {
        _limits.AcquireVerificationAsync(Email, Ip, CancellationToken.None)
            .Returns(Task.FromException<PasswordResetRateLimitDecision>(new InvalidOperationException("quota failed")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _store.ReceivedCalls().ShouldBeEmpty();
        AssertNoPasswordOrSessionEffects();
    }

    private PasswordResetSession SetSession()
    {
        User user = NewUser();
        PasswordResetSession session = new(user, NewChallenge(user.Id));
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(session);
        return session;
    }

    private Task<Result<ResetPasswordOutcome>> HandleAsync() =>
        new ResetPasswordHandler(_limits, _store, _codes, _hasher, _refresh, _emails, _time)
            .HandleAsync(new ResetPasswordCommand(Email, "012345", Password, Ip), CancellationToken.None);

    private void AssertNoPasswordOrSessionEffects()
    {
        _hasher.ReceivedCalls().ShouldBeEmpty();
        _refresh.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    private static User NewUser() => User.Create(Email, "Reset", "old-password-hash", Now.AddDays(-1));
    private static PasswordResetChallenge NewChallenge(Guid userId) =>
        PasswordResetChallenge.Issue(Guid.CreateVersion7(), userId, new string('a', 64), Now, Now.AddMinutes(10), 5);
}
