using BandUp.Application.Abstractions;
using BandUp.Application.Features.Exams;
using BandUp.Domain.Exams;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

/// <summary>
/// Composes the generic repository like <see cref="EfUserRepository"/>. The two lock methods
/// use PostgreSQL row and advisory locks, which last until the surrounding transaction ends.
/// </summary>
public sealed class EfAttemptRepository(BandUpDbContext context) : IAttemptRepository
{
    private readonly EfRepository<Attempt> _inner = new(context);

    public Task<Attempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _inner.GetByIdAsync(id, cancellationToken);

    public Task AddAsync(Attempt entity, CancellationToken cancellationToken) =>
        _inner.AddAsync(entity, cancellationToken);

    public void Remove(Attempt entity) => _inner.Remove(entity);

    public Task<Attempt?> GetWithAnswersAsync(Guid attemptId, CancellationToken cancellationToken) =>
        context.Attempts
            .Include(attempt => attempt.Answers)
            .SingleOrDefaultAsync(attempt => attempt.Id == attemptId, cancellationToken);

    public async Task<Attempt?> GetWithAnswersForUpdateAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM exams.attempts WHERE id = {attemptId} FOR UPDATE",
            cancellationToken);

        return await GetWithAnswersAsync(attemptId, cancellationToken);
    }

    public Task<Attempt?> FindInProgressAsync(Guid userId, Guid passageId, CancellationToken cancellationToken) =>
        context.Attempts
            .Include(attempt => attempt.Answers)
            .SingleOrDefaultAsync(
                attempt => attempt.UserId == userId
                    && attempt.PassageId == passageId
                    && attempt.Status == AttemptStatus.InProgress,
                cancellationToken);

    public Task<bool> IdempotencyKeyUsedAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken) =>
        context.Attempts.AnyAsync(
            attempt => attempt.UserId == userId && attempt.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task AcquireStartLockAsync(Guid userId, Guid passageId, CancellationToken cancellationToken)
    {
        string key = $"exams:start:{userId:N}:{passageId:N}";

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            cancellationToken);
    }
}
