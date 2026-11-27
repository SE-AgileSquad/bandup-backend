using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.VerifyResetCode;

/// <summary>Reduces account-state timing differences across code-verification rejections, including counter commits.</summary>
public sealed class VerifyResetCodeTimingBehavior(TimeProvider timeProvider)
    : IPipelineBehavior<VerifyResetCodeCommand, VerifyResetCodeOutcome>
{
    public async Task<Result<VerifyResetCodeOutcome>> HandleAsync(
        VerifyResetCodeCommand request, CancellationToken cancellationToken, RequestHandlerDelegate<VerifyResetCodeOutcome> next)
    {
        long started = timeProvider.GetTimestamp();
        try
        {
            return await next();
        }
        finally
        {
            TimeSpan remaining = PasswordResetPolicy.MinimumRequestDuration - timeProvider.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(remaining, timeProvider, cancellationToken);
            }
        }
    }
}
