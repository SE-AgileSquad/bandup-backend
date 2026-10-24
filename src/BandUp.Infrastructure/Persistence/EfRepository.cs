using BandUp.Application.Abstractions;
using BandUp.Domain.Common;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfRepository<TEntity>(BandUpDbContext context) : IRepository<TEntity>
    where TEntity : Entity
{
    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<TEntity>().FindAsync([id], cancellationToken);

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken) =>
        context.Set<TEntity>().AddAsync(entity, cancellationToken).AsTask();

    public void Remove(TEntity entity) => context.Set<TEntity>().Remove(entity);
}