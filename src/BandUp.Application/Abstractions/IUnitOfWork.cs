namespace BandUp.Application.Abstractions;

/// <summary>
/// A rollback or failed save retires the scoped context. Retry in a fresh scope and transaction.
/// Direct EF transaction owners must likewise dispose the scope on failure. After a manual
/// savepoint rollback, abandon the whole transaction before further Billing work.
/// EF automatic savepoints stay enabled.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task BeginTransactionAsync(CancellationToken cancellationToken);

    Task CommitTransactionAsync(CancellationToken cancellationToken);

    Task RollbackTransactionAsync(CancellationToken cancellationToken);
}
