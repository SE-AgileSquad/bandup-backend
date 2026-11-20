using BandUp.Application.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BandUp.Infrastructure.Persistence;

internal static class EfStoreOperations
{
    internal static async Task<bool> TryDeleteAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        try
        {
            return await query.ExecuteDeleteAsync(cancellationToken) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The command returns Conflict, and the transaction behavior rolls back the failed statement.
            return false;
        }
    }

    internal static async Task<PagedResult<T>> ReadPageAsync<T>(
        IQueryable<T> query, PageRequest page, CancellationToken cancellationToken)
    {
        int count = await query.CountAsync(cancellationToken);
        long offset = (long)(page.Page - 1) * page.PageSize;
        List<T> items = offset >= count
            ? []
            : await query.Skip((int)offset).Take(page.PageSize).ToListAsync(cancellationToken);

        return PagedResult<T>.Create(items, page, count);
    }
}
