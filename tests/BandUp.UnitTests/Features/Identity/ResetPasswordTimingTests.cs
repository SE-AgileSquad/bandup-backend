using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.ResetPassword;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class ResetPasswordTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_15_Reset_Response_Floor_Includes_Attempt_And_Password_Commit_Work(bool completed)
    {
        FakeTimeProvider time = new();
        ResetPasswordOutcome outcome = completed ? new ResetPasswordOutcome.Completed() : new ResetPasswordOutcome.Rejected();
        Task<Result<ResetPasswordOutcome>> pending = new ResetPasswordTimingBehavior(time).HandleAsync(
            new ResetPasswordCommand(new string('A', 43), "NewPassword123!"), CancellationToken.None, () =>
            {
                time.Advance(TimeSpan.FromMilliseconds(100));
                return Task.FromResult(Result<ResetPasswordOutcome>.Success(outcome));
            });
        time.Advance(TimeSpan.FromMilliseconds(149));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Value.ShouldBeSameAs(outcome);
    }

    [Fact]
    public void AUTH_15_Reset_Timing_Is_Outside_The_Common_Command_Pipeline()
    {
        ServiceCollection services = new();
        services.AddApplication();
        ServiceDescriptor timing = services.Single(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<ResetPasswordCommand, ResetPasswordOutcome>));
        ServiceDescriptor common = services.First(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>));
        services.IndexOf(timing).ShouldBeLessThan(services.IndexOf(common));
    }
}
