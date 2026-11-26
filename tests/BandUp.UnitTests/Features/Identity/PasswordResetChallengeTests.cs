using BandUp.Domain.Common;
using BandUp.Domain.Identity;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class PasswordResetChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly string Mac = new('a', 64);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(599, true)]
    [InlineData(600, false)]
    public void AUTH_15_Challenge_Usability_Respects_Both_Time_Boundaries(int seconds, bool usable) =>
        NewChallenge().IsUsable(Now.AddSeconds(seconds)).ShouldBe(usable);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void AUTH_15_Final_Incorrect_Guess_Exhausts_The_Challenge(int limit)
    {
        PasswordResetChallenge challenge = NewChallenge(limit);
        for (int index = 0; index < limit; index++)
        {
            challenge.IsUsable(Now).ShouldBeTrue();
            challenge.RecordFailedAttempt(Now);
        }

        challenge.FailedAttempts.ShouldBe(limit);
        challenge.IsUsable(Now).ShouldBeFalse();
        Should.Throw<DomainException>(() => challenge.Consume(Now));
        Should.Throw<DomainException>(() => challenge.RecordFailedAttempt(Now));
        challenge.FailedAttempts.ShouldBe(limit);
    }

    [Fact]
    public void AUTH_15_Consumption_Is_One_Time_And_Preserves_Previous_Failed_Attempts()
    {
        PasswordResetChallenge challenge = NewChallenge();
        challenge.RecordFailedAttempt(Now);
        challenge.Consume(Now.AddSeconds(1));

        challenge.ConsumedAt.ShouldBe(Now.AddSeconds(1));
        challenge.FailedAttempts.ShouldBe(1);
        challenge.IsUsable(Now.AddSeconds(1)).ShouldBeFalse();
        Should.Throw<DomainException>(() => challenge.Consume(Now.AddSeconds(2)));
        Should.Throw<DomainException>(() => challenge.RecordFailedAttempt(Now.AddSeconds(2)));
        challenge.Invalidate(Now.AddSeconds(2));
        challenge.InvalidatedAt.ShouldBeNull();
    }

    [Fact]
    public void AUTH_15_Invalidation_Is_Idempotent_And_Blocks_Consumption()
    {
        PasswordResetChallenge challenge = NewChallenge();
        challenge.Invalidate(Now.AddSeconds(1));
        challenge.Invalidate(Now.AddSeconds(2));

        challenge.InvalidatedAt.ShouldBe(Now.AddSeconds(1));
        challenge.IsUsable(Now.AddSeconds(2)).ShouldBeFalse();
        Should.Throw<DomainException>(() => challenge.Consume(Now.AddSeconds(2)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(600)]
    public void AUTH_15_Closed_Time_Window_Cannot_Record_Attempts_Or_Consume(int seconds)
    {
        PasswordResetChallenge challenge = NewChallenge();
        Should.Throw<DomainException>(() => challenge.RecordFailedAttempt(Now.AddSeconds(seconds)));
        Should.Throw<DomainException>(() => challenge.Consume(Now.AddSeconds(seconds)));
        challenge.FailedAttempts.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void AUTH_15_Invalid_Attempt_Limits_Are_Programming_Errors(int limit) =>
        Should.Throw<DomainException>(() => NewChallenge(limit));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("012345")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void AUTH_15_Challenge_Rejects_Plaintext_Or_Malformed_Macs(string? mac) =>
        Should.Throw<DomainException>(() => PasswordResetChallenge.Issue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), mac!, Now, Now.AddMinutes(10), 5));

    [Fact]
    public void AUTH_15_Challenge_Rejects_Invalid_Identity_Lifetime_And_Non_Utc_Writes()
    {
        Should.Throw<DomainException>(() => PasswordResetChallenge.Issue(Guid.Empty, Guid.CreateVersion7(), Mac, Now, Now.AddMinutes(10), 5));
        Should.Throw<DomainException>(() => PasswordResetChallenge.Issue(Guid.CreateVersion7(), Guid.Empty, Mac, Now, Now.AddMinutes(10), 5));
        Should.Throw<DomainException>(() => PasswordResetChallenge.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Mac, Now, Now, 5));
        Should.Throw<DomainException>(() => PasswordResetChallenge.Issue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Mac, Now.ToOffset(TimeSpan.FromHours(3)), Now.AddMinutes(10), 5));
        PasswordResetChallenge challenge = NewChallenge();
        Should.Throw<DomainException>(() => challenge.Invalidate(Now.AddSeconds(-1)));
        Should.Throw<DomainException>(() => challenge.Consume(Now.ToOffset(TimeSpan.FromHours(3))));
    }

    private static PasswordResetChallenge NewChallenge(int limit = 5) =>
        PasswordResetChallenge.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), Mac, Now, Now.AddMinutes(10), limit);
}
