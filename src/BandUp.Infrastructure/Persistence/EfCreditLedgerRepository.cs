using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;

using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfCreditLedgerRepository(BandUpDbContext context) : ICreditLedgerRepository
{
    public async Task<CreditLedgerEntry?> FindAsync(string reason, Guid referenceId, CancellationToken cancellationToken)
    {
        var pending = context.ChangeTracker.Entries<CreditLedgerEntry>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .SingleOrDefault(entry => entry.Reason == reason && entry.ReferenceId == referenceId);
        return pending ?? await context.CreditLedger.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.Reason == reason && entry.ReferenceId == referenceId, cancellationToken);
    }

    public Task AddAsync(CreditLedgerEntry entry, CancellationToken cancellationToken) =>
        context.CreditLedger.AddAsync(entry, cancellationToken).AsTask();
}
