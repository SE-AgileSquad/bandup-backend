using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.VerifyOtpLogin;

/// <summary>Reduces account-state timing differences across login rejections, including counter commits.</summary>
public sealed class VerifyOtpLoginTimingBehavior(TimeProvider timeProvider)
    : IPipelineBehavior<VerifyOtpLoginCommand, VerifyOtpLoginOutcome>
{
    public async Task<Result<VerifyOtpLoginOutcome>> HandleAsync(
        VerifyOtpLoginCommand request, CancellationToken cancellationToken, RequestHandlerDelegate<VerifyOtpLoginOutcome> next)
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
