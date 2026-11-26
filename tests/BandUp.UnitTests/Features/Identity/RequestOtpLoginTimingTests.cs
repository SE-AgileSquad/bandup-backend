using BandUp.Application;
using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.RequestOtpLogin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace BandUp.UnitTests.Features.Identity;

public sealed class RequestOtpLoginTimingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUTH_16_Request_Floor_Includes_Downstream_Work_For_Accepted_And_Rejected_Requests(bool rejected)
    {
        FakeTimeProvider time = new();
        RequestOtpLoginTimingBehavior behavior = new(time);
        Result<RequestOtpLoginOutcome> expected = rejected
            ? Result<RequestOtpLoginOutcome>.Failure(Error.OtpLoginRateLimited())
            : Result<RequestOtpLoginOutcome>.Success(new RequestOtpLoginOutcome());
        Task<Result<RequestOtpLoginOutcome>> pending = behavior.HandleAsync(new RequestOtpLoginCommand("user@example.com", "fake-captcha-token"), CancellationToken.None, () =>
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            return Task.FromResult(expected);
        });
        time.Advance(TimeSpan.FromMilliseconds(149));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task AUTH_16_Cancelled_Requests_Do_Not_Wait_For_The_Response_Floor()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        var behavior = new RequestOtpLoginTimingBehavior(new FakeTimeProvider());
        await Should.ThrowAsync<OperationCanceledException>(() => behavior.HandleAsync(new RequestOtpLoginCommand("user@example.com", "fake-captcha-token"), cancellation.Token,
            () => Task.FromCanceled<Result<RequestOtpLoginOutcome>>(cancellation.Token)));
    }

    [Fact]
    public void AUTH_16_Timing_Wraps_The_Command_Pipeline_Without_Changing_Common_Behavior_Order()
    {
        ServiceCollection services = new();
        services.AddApplication();
        ServiceDescriptor timing = services.Single(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<RequestOtpLoginCommand, RequestOtpLoginOutcome>));
        ServiceDescriptor firstCommon = services.First(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>));
        services.IndexOf(timing).ShouldBeLessThan(services.IndexOf(firstCommon));
    }
}
