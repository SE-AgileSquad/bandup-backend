using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.ForgotPassword;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class ForgotPasswordHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
    private const string Email = "learner@example.com";
    private const string Ip = "203.0.113.7";
    private readonly IPasswordResetRateLimiter _limits = Substitute.For<IPasswordResetRateLimiter>();
    private readonly IPasswordResetChallengeStore _store = Substitute.For<IPasswordResetChallengeStore>();
    private readonly IPasswordResetCodeProtector _codes = Substitute.For<IPasswordResetCodeProtector>();
    private readonly IPasswordResetTokenStore _tokens = Substitute.For<IPasswordResetTokenStore>();
    private readonly IPostCommitEmailQueue _emails = Substitute.For<IPostCommitEmailQueue>();
    private readonly FakeTimeProvider _time = new(Now);

    public ForgotPasswordHandlerTests()
    {
        _codes.GenerateCode().Returns("012345");
        _codes.Protect(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345").Returns(new string('a', 64));
    }

    [Theory]
    [InlineData(PasswordResetRateLimitDecision.AccountSuppressed, true)]
    [InlineData(PasswordResetRateLimitDecision.ClientRateLimited, false)]
    public async Task AUTH_15_Quota_Rejection_Precedes_Account_Lookup(PasswordResetRateLimitDecision decision, bool accepted)
    {
        _limits.AcquireRequestAsync(Email, Ip, CancellationToken.None).Returns(decision);
        Result<ForgotPasswordOutcome> result = await HandleAsync();
        result.IsSuccess.ShouldBe(accepted);
        if (!accepted)
        {
            result.Error!.Code.ShouldBe(Error.PasswordResetRateLimitedCode);
        }

        _store.ReceivedCalls().ShouldBeEmpty();
        _codes.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_15_Unknown_And_Inactive_Accounts_Get_The_Same_Acknowledgement_Without_Delivery(bool inactive)
    {
        if (inactive)
        {
            User user = NewUser();
            user.Deactivate();
            _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(new PasswordResetSession(user, null));
        }
        Result<ForgotPasswordOutcome> result = await HandleAsync();
        result.Value.ShouldBe(new ForgotPasswordOutcome());
        _codes.Received(1).GenerateCode();
        _codes.Received(1).Protect(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345");
        await _store.DidNotReceive().ReplaceAsync(Arg.Any<PasswordResetChallenge>(), Arg.Any<CancellationToken>());
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_15_Eligible_Request_Stages_A_Protected_Challenge_And_Post_Commit_Email()
    {
        User user = NewUser();
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(new PasswordResetSession(user, null));
        PasswordResetChallenge? staged = null;
        _store.ReplaceAsync(Arg.Do<PasswordResetChallenge>(challenge => staged = challenge), CancellationToken.None).Returns(Task.CompletedTask);

        (await HandleAsync()).Value.ShouldBe(new ForgotPasswordOutcome());
        await _limits.Received(1).AcquireRequestAsync(Email, Ip, CancellationToken.None);
        staged.ShouldNotBeNull();
        staged.UserId.ShouldBe(user.Id);
        staged.CodeMac.ShouldBe(new string('a', 64));
        staged.CreatedAt.ShouldBe(Now);
        staged.ExpiresAt.ShouldBe(Now.AddMinutes(10));
        staged.MaxFailedAttempts.ShouldBe(5);
        _codes.Received(1).Protect(staged.Id, user.Id, "012345");
        _emails.Received(1).Enqueue(Arg.Is<EmailMessage>(message =>
            message.To == Email && message.TextBody.Contains("012345") && !message.TextBody.Contains(staged.CodeMac)), staged.ExpiresAt);
        await _tokens.Received(1).InvalidateAllForUserAsync(user.Id, Now, CancellationToken.None);
        user.PasswordHash.ShouldBe("unchanged-password-hash");
    }

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public async Task AUTH_15_Database_Cooldown_Protects_Existing_Code_Even_After_Quota_State_Is_Lost(int ageSeconds, bool replaced)
    {
        User user = NewUser();
        PasswordResetChallenge old = PasswordResetChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('b', 64),
            Now.AddSeconds(-ageSeconds), Now.AddMinutes(9), 5);
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(new PasswordResetSession(user, old));

        (await HandleAsync()).IsSuccess.ShouldBeTrue();
        await _store.Received(replaced ? 1 : 0).ReplaceAsync(Arg.Any<PasswordResetChallenge>(), CancellationToken.None);
        old.InvalidatedAt.ShouldBeNull();
        _emails.ReceivedCalls().Count().ShouldBe(replaced ? 1 : 0);
    }

    [Fact]
    public async Task AUTH_15_Expiry_Is_Calculated_After_Waiting_For_The_User_Lock()
    {
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(_ =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            return new PasswordResetSession(NewUser(), null);
        });
        await HandleAsync();
        await _store.Received(1).ReplaceAsync(Arg.Is<PasswordResetChallenge>(challenge =>
            challenge.CreatedAt == Now.AddMinutes(2) && challenge.ExpiresAt == Now.AddMinutes(12)), CancellationToken.None);
    }

    [Fact]
    public async Task AUTH_15_Storage_Failure_Does_Not_Schedule_Email()
    {
        _store.FindForUpdateAsync(Email, CancellationToken.None).Returns(new PasswordResetSession(NewUser(), null));
        _store.ReplaceAsync(Arg.Any<PasswordResetChallenge>(), CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("storage unavailable")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<ForgotPasswordOutcome>> HandleAsync() =>
        new ForgotPasswordHandler(_limits, _store, _codes, _tokens, _emails, _time).HandleAsync(new ForgotPasswordCommand(Email, Ip), CancellationToken.None);

    [Fact]
    public async Task AUTH_15_Quota_Outage_Fails_Closed_Before_Account_Lookup_Or_Delivery()
    {
        _limits.AcquireRequestAsync(Email, Ip, CancellationToken.None)
            .Returns(Task.FromException<PasswordResetRateLimitDecision>(new InvalidOperationException("quota store unavailable")));
        await Should.ThrowAsync<InvalidOperationException>(() => HandleAsync());
        _store.ReceivedCalls().ShouldBeEmpty();
        _emails.ReceivedCalls().ShouldBeEmpty();
    }

    private static User NewUser() => User.Create(Email, "Learner", "unchanged-password-hash", Now.AddDays(-1));
}
