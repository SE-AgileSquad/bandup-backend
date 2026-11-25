namespace BandUp.Application.Abstractions;

/// <summary>
/// Schedules best-effort, in-process delivery after the current command commits. Rollback
/// discards the scheduled email. The optional deadline prevents delivery of expired codes.
/// </summary>
public interface IPostCommitEmailQueue
{
    void Enqueue(EmailMessage message, DateTimeOffset? expiresAt = null);
}
