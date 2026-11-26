namespace BandUp.Application.Abstractions;

/// <summary>
/// Email delivery is an external side effect: call only after the associated database commit.
/// Implementations must not log recipient/content or silently claim delivery on failure.
/// Real and offline development adapters are supplied in the delivery phase.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
