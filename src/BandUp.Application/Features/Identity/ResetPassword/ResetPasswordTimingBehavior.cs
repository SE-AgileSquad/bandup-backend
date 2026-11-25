using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.ResetPassword;

/// <summary>Reduces account-state timing differences across reset rejections, including counter commits.</summary>
public sealed class ResetPasswordTimingBehavior(TimeProvider timeProvider)
    : IPipelineBehavior<ResetPasswordCommand, ResetPasswordOutcome>
{
    public async Task<Result<ResetPasswordOutcome>> HandleAsync(
        ResetPasswordCommand request, CancellationToken cancellationToken, RequestHandlerDelegate<ResetPasswordOutcome> next)
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
