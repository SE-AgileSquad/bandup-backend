using BandUp.Application.Features.Writing.Publishing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

internal sealed class EfWritingExpirationScan(BandUpDbContext context, TimeProvider clock) : IWritingExpirationScan
{
    public async Task<IReadOnlyList<WritingExpirationCandidate>> ReadDueAsync(
        WritingExpirationCandidate? after, int batchSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var deadline = after?.QueueDeadline ?? DateTimeOffset.MinValue;
        var id = after?.SubmissionId ?? Guid.Empty;
        // Unlike lease selection, expiration deliberately ignores all publication metadata and takes no lock.
        return await context.Database.SqlQuery<WritingExpirationCandidate>($"""
            SELECT id AS "SubmissionId", queue_deadline AS "QueueDeadline"
            FROM writing.writing_submissions
            WHERE status = 'Queued' AND queue_deadline <= {now}
                AND (queue_deadline, id) > ({deadline}, {id})
            ORDER BY queue_deadline, id
            LIMIT {batchSize}
            """).ToListAsync(ct);
    }
}
