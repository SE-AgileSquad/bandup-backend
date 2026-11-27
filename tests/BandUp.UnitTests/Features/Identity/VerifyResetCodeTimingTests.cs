using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.VerifyResetCode;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class VerifyResetCodeTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_17_Reset_Response_Floor_Includes_Attempt_And_Password_Commit_Work(bool completed)
    {
        FakeTimeProvider time = new();
        VerifyResetCodeOutcome outcome = completed ? new VerifyResetCodeOutcome.Verified(new string('A', 43), DateTimeOffset.UnixEpoch) : new VerifyResetCodeOutcome.Rejected();
        Task<Result<VerifyResetCodeOutcome>> pending = new VerifyResetCodeTimingBehavior(time).HandleAsync(
            new VerifyResetCodeCommand("reset@example.com", "012345"), CancellationToken.None, () =>
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                return Task.FromResult(Result<VerifyResetCodeOutcome>.Success(outcome));
            });
        time.Advance(TimeSpan.FromMilliseconds(149));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldBeSameAs(outcome);
    }

    [Fact]
    public void AUTH_17_Reset_Timing_Is_Outside_The_Common_Command_Pipeline()
    {
        ServiceCollection services = new();
        services.AddApplication();
        ServiceDescriptor timing = services.Single(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<VerifyResetCodeCommand, VerifyResetCodeOutcome>));
        ServiceDescriptor common = services.First(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>));
        services.IndexOf(timing).ShouldBeLessThan(services.IndexOf(common));
    }
}
