namespace BandUp.Application.Features.Writing.Publishing;

public sealed record WritingExpirationCandidate(Guid SubmissionId, DateTimeOffset QueueDeadline);

public interface IWritingExpirationScan
{
    Task<IReadOnlyList<WritingExpirationCandidate>> ReadDueAsync(WritingExpirationCandidate? after, int batchSize, CancellationToken ct);
}
