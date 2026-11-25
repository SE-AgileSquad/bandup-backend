using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.ForgotPassword;

/// <summary>Includes transaction commit and queue handoff in the minimum response duration.</summary>
public sealed class ForgotPasswordTimingBehavior(TimeProvider timeProvider)
    : IPipelineBehavior<ForgotPasswordCommand, ForgotPasswordOutcome>
{
    public async Task<Result<ForgotPasswordOutcome>> HandleAsync(
        ForgotPasswordCommand request, CancellationToken cancellationToken, RequestHandlerDelegate<ForgotPasswordOutcome> next)
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
