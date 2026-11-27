using BandUp.Application.Abstractions;
using BandUp.Application.Features.Writing.SubmitWritingEssay;
using BandUp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfUnitOfWork(BandUpDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await AbandonAfterFailureAsync(exception);
            // Only a save-time violation after successful transaction disposal is safe to recover.
            if (!exception.Data.Contains("TransactionCleanup") &&
                exception is DbUpdateException { InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    SchemaName: "writing",
                    TableName: "writing_submissions",
                    ConstraintName: "ix_writing_submissions_user_id_idempotency_key"
                } })
            {
                throw new WritingSubmissionKeyConflictException(exception);
            }
            if (exception is DbUpdateException update && IsUniqueViolation(update))
            {
                throw new DuplicateEntityException("A record with the same unique value already exists.", update);
            }
            throw;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        _transaction ??= await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await AbandonAfterFailureAsync(exception);
            throw;
        }

        var committed = _transaction;
        _transaction = null;
        await committed.DisposeAsync();
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        // The scoped context owns its connection and current transaction. Disposal rolls back
        // uncommitted PostgreSQL work and retires tracked entities, even after a successful flush.
        _transaction = null;
        await context.DisposeAsync();
    }

    private async Task AbandonAfterFailureAsync(Exception original)
    {
        try
        {
            _transaction = null;
            await context.DisposeAsync();
        }
        catch (Exception cleanup)
        {
            original.Data["TransactionCleanup"] = cleanup;
        }
    }
}
