using BandUp.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BandUp.Infrastructure.Email;

public sealed class EmailDeliveryWorker(
    EmailDeliveryQueue queue, IEmailSender sender, TimeProvider timeProvider, ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (EmailDeliveryQueue.PendingEmail pending in queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (pending.ExpiresAt is { } expires && timeProvider.GetUtcNow() >= expires)
            {
                logger.LogWarning("An expired recovery email was discarded before delivery");
                continue;
            }
            try
            {
                await sender.SendAsync(pending.Message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Provider errors can include email addresses or body content. Log the type only.
                logger.LogWarning("Email delivery failed ({FailureType}); a new request can be made after cooldown", exception.GetType().Name);
            }
        }
    }
}
