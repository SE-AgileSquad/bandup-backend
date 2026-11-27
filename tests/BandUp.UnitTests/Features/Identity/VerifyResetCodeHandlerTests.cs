using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.VerifyResetCode;
using BandUp.Domain.Identity;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class VerifyResetCodeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
    private const string Email = "reset@example.com";
    private readonly IPasswordResetRateLimiter _limits = Substitute.For<IPasswordResetRateLimiter>();
    private readonly IPasswordResetChallengeStore _challenges = Substitute.For<IPasswordResetChallengeStore>();
    private readonly IPasswordResetCodeProtector _codes = Substitute.For<IPasswordResetCodeProtector>();
    private readonly IPasswordResetTokenProtector _protector = Substitute.For<IPasswordResetTokenProtector>();
    private readonly IPasswordResetTokenStore _tokens = Substitute.For<IPasswordResetTokenStore>();
    private readonly FakeTimeProvider _time = new(Now);
    private static readonly string Raw = new('A', 43);

    public VerifyResetCodeHandlerTests()
    {
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(true);
        _protector.GenerateToken().Returns(Raw);
        _protector.HashToken(Raw).Returns(new string('b', 64));
    }

    [Fact]
    public async Task AUTH_17_Verification_Consumes_Code_And_Stages_Only_A_Reset_Authorization()
    {
        var session = Session();
        _challenges.FindForUpdateAsync(Email, CancellationToken.None).Returns(session);
        var outcome = (await HandleAsync()).Value.ShouldBeOfType<VerifyResetCodeOutcome.Verified>();
        outcome.ResetToken.ShouldBe(Raw);
        outcome.ExpiresAt.ShouldBe(Now.AddMinutes(5));
        session.Challenge!.ConsumedAt.ShouldBe(Now);
        session.User.PasswordHash.ShouldBe("unchanged-hash");
        session.User.LastLoginAt.ShouldBeNull();
        await _tokens.Received(1).ReplaceAsync(Arg.Is<PasswordResetToken>(token => token.UserId == session.User.Id &&
            token.ChallengeId == session.Challenge.Id && token.TokenHash == new string('b', 64)), CancellationToken.None);
    }

    [Fact]
    public async Task AUTH_17_Five_Wrong_Guesses_Are_Committable_And_Exhaust_The_Code()
    {
        var session = Session();
        _challenges.FindForUpdateAsync(Email, CancellationToken.None).Returns(session);
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(false);
        for (int attempt = 0; attempt < 6; attempt++) { (await HandleAsync()).Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>(); }
        session.Challenge!.FailedAttempts.ShouldBe(5);
        _tokens.ReceivedCalls().ShouldBeEmpty();
        _protector.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("consumed")]
    [InlineData("invalidated")]
    public async Task AUTH_17_Ineligible_Code_State_Does_Not_Create_A_Reset_Token(string state)
    {
        var session = Session(state == "future" ? Now.AddMinutes(2) : Now);
        if (state == "inactive") { session.User.Deactivate(); }
        if (state == "missing") { session = session with { Challenge = null }; }
        if (state == "expired") { _time.Advance(TimeSpan.FromMinutes(10)); }
        if (state == "consumed") { session.Challenge!.Consume(Now); }
        if (state == "invalidated") { session.Challenge!.Invalidate(Now); }
        _challenges.FindForUpdateAsync(Email, CancellationToken.None).Returns(state == "unknown" ? null : session);
        (await HandleAsync()).Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>();
        _tokens.ReceivedCalls().ShouldBeEmpty();
        _protector.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_17_Expiry_During_Crypto_Work_Does_Not_Consume_Or_Increment_Closed_Code(bool matched)
    {
        var session = Session();
        _challenges.FindForUpdateAsync(Email, CancellationToken.None).Returns(session);
        _codes.Verify(Arg.Any<Guid>(), Arg.Any<Guid>(), "012345", Arg.Any<string>()).Returns(_ => { _time.Advance(TimeSpan.FromMinutes(10)); return matched; });
        (await HandleAsync()).Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>();
        session.Challenge!.FailedAttempts.ShouldBe(0);
        session.Challenge.ConsumedAt.ShouldBeNull();
        _tokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AUTH_17_Expiry_During_Token_Generation_Does_Not_Consume_Code()
    {
        var session = Session();
        _challenges.FindForUpdateAsync(Email, CancellationToken.None).Returns(session);
        _protector.GenerateToken().Returns(_ => { _time.Advance(TimeSpan.FromMinutes(10)); return Raw; });
        (await HandleAsync()).Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>();
        session.Challenge!.ConsumedAt.ShouldBeNull();
        _tokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PasswordResetRateLimitDecision.ClientRateLimited, false)]
    [InlineData(PasswordResetRateLimitDecision.AccountSuppressed, true)]
    public async Task AUTH_17_Verification_Quotas_Precede_Challenge_Lookup(PasswordResetRateLimitDecision decision, bool success)
    {
        _limits.AcquireVerificationAsync(Email, "203.0.113.7", CancellationToken.None).Returns(decision);
        var result = await HandleAsync();
        result.IsSuccess.ShouldBe(success);
        if (success) { result.Value.ShouldBeOfType<VerifyResetCodeOutcome.Rejected>(); }
        else { result.Error!.Code.ShouldBe(Error.PasswordResetRateLimitedCode); }
        _challenges.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<VerifyResetCodeOutcome>> HandleAsync() =>
        new VerifyResetCodeHandler(_limits, _challenges, _codes, _protector, _tokens, _time)
            .HandleAsync(new VerifyResetCodeCommand(Email, "012345", "203.0.113.7"), CancellationToken.None);

    private static PasswordResetSession Session(DateTimeOffset? at = null)
    {
        User user = User.Create(Email, "Learner", "unchanged-hash", Now.AddDays(-1));
        DateTimeOffset issued = at ?? Now;
        return new(user, PasswordResetChallenge.Issue(Guid.CreateVersion7(), user.Id, new string('a', 64), issued.AddMinutes(-1), issued.AddMinutes(9), 5));
    }
}
