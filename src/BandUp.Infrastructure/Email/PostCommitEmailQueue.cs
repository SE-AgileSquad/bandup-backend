using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Email;

public sealed class PostCommitEmailQueue(IMessageBuffer buffer, EmailDeliveryQueue queue) : IPostCommitEmailQueue
{
    public void Enqueue(EmailMessage message, DateTimeOffset? expiresAt = null)
    {
        if (!buffer.IsOpen)
        {
            throw new InvalidOperationException("Schedule email inside an open command transaction.");
        }
        buffer.Enqueue(_ =>
        {
            // Commit has completed. An aborted HTTP request must not cancel this local handoff.
            queue.TryEnqueue(message, expiresAt);
            return Task.CompletedTask;
        });
    }
}
