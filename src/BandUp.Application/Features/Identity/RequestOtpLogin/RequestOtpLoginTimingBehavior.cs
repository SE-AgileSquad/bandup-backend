using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.RequestOtpLogin;

/// <summary>Includes transaction commit and queue handoff in the minimum response duration.</summary>
public sealed class RequestOtpLoginTimingBehavior(TimeProvider timeProvider)
    : IPipelineBehavior<RequestOtpLoginCommand, RequestOtpLoginOutcome>
{
    public async Task<Result<RequestOtpLoginOutcome>> HandleAsync(
        RequestOtpLoginCommand request, CancellationToken cancellationToken, RequestHandlerDelegate<RequestOtpLoginOutcome> next)
    {
        long started = timeProvider.GetTimestamp();
        try
        {
            return await next();
        }
        finally
        {
            TimeSpan remaining = OtpLoginPolicy.MinimumRequestDuration - timeProvider.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(remaining, timeProvider, cancellationToken);
            }
        }
    }
}
