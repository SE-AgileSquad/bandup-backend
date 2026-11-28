using System.Data;
using BandUp.Application.Features.Writing.Publishing;
using BandUp.Domain.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Persistence;

internal sealed class EfWritingPublishingStore(BandUpDbContext context, TimeProvider clock,
    IOptions<WritingPublishingOptions> options) : IWritingPublishingStore
{
    public async Task<WritingPublishReservation?> ReserveAsync(CancellationToken ct)
    {
        RequireReadCommittedTransaction();
        var scanTime = DatabaseTime();
        int maxAttempts = options.Value.MaxAttempts;
        var candidates = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM writing.writing_submissions
            WHERE status = 'Queued' AND published_at IS NULL AND queue_deadline > {scanTime}
                AND publish_attempts < {maxAttempts}
                AND (next_publish_attempt_at IS NULL OR next_publish_attempt_at <= {scanTime})
                AND (publish_lease_expires_at IS NULL OR publish_lease_expires_at <= {scanTime})
            ORDER BY submitted_at, id
            LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return null;
        }

        // The scan timestamp is only for selection. Fence the write using time sampled under the row lock.
        var now = DatabaseTime();
        var lease = new WritingPublishReservation(candidates[0], Guid.NewGuid());
        var expires = now.Add(options.Value.LeaseDuration);
        int updated = await QueuedUnpublished(lease.SubmissionId, now)
            .Where(s => s.PublishAttempts < maxAttempts &&
                (s.NextPublishAttemptAt == null || s.NextPublishAttemptAt <= now) &&
                (s.PublishLeaseExpiresAt == null || s.PublishLeaseExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.PublishAttempts, s => s.PublishAttempts + 1)
                .SetProperty(s => s.PublishLeaseId, (Guid?)lease.LeaseId)
                .SetProperty(s => s.PublishLeaseExpiresAt, (DateTimeOffset?)expires), ct);
        return updated == 1 ? lease : null;
    }

    public async Task<bool> RecordOutcomeAsync(WritingPublishReservation reservation, bool confirmed, CancellationToken ct)
    {
        RequireReadCommittedTransaction();
        var attempts = await context.Database.SqlQuery<int>($"""
            SELECT publish_attempts AS "Value" FROM writing.writing_submissions
            WHERE id = {reservation.SubmissionId} AND publish_lease_id = {reservation.LeaseId}
                AND status = 'Queued' AND published_at IS NULL
            FOR UPDATE
            """).ToListAsync(ct);
        if (attempts.Count == 0)
        {
            return false;
        }

        // Outcome updates may wait for a row lock. An expired lease cannot publish or schedule a retry.
        var now = DatabaseTime();
        var current = QueuedUnpublished(reservation.SubmissionId, now)
            .Where(s => s.PublishLeaseId == reservation.LeaseId && s.PublishLeaseExpiresAt > now);
        DateTimeOffset? nextAttempt = !confirmed && attempts[0] < options.Value.MaxAttempts
            ? now.Add(options.Value.RetryDelay(attempts[0])) : null;

        // Set-based writes touch only the five publishing fields allowed by the tracked mutation guard.
        // No submission aggregate or essay is loaded, and rollback covers these writes as usual.
        int updated = await current.ExecuteUpdateAsync(setters => setters
            .SetProperty(s => s.PublishedAt, confirmed ? (DateTimeOffset?)now : null)
            .SetProperty(s => s.PublishLeaseId, (Guid?)null)
            .SetProperty(s => s.PublishLeaseExpiresAt, (DateTimeOffset?)null)
            .SetProperty(s => s.NextPublishAttemptAt, nextAttempt), ct);
        return updated == 1;
    }

    private IQueryable<WritingSubmission> QueuedUnpublished(Guid id, DateTimeOffset now) =>
        context.WritingSubmissions.Where(s => s.Id == id && s.Status == WritingEvaluationStatus.Queued &&
            s.PublishedAt == null && s.QueueDeadline > now);

    private DateTimeOffset DatabaseTime()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private void RequireReadCommittedTransaction()
    {
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("Writing publication leases require a Read Committed transaction.");
        }
    }
}
