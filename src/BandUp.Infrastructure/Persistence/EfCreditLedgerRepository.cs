using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing.Admin;
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

    public Task<CreditLedgerEntry?> FindAdminAdjustmentAsync(Guid requestId, CancellationToken cancellationToken) =>
        FindAsync(CreditLedgerEntry.AdminAdjustReason, requestId, cancellationToken);

    public Task<PagedResult<CreditHistoryItem>> ListForUserAsync(Guid userId, PageRequest page, CancellationToken cancellationToken) =>
        EfStoreOperations.ReadPageAsync(context.CreditLedger.AsNoTracking().Where(entry => entry.UserId == userId)
            .OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
            .Select(entry => new CreditHistoryItem(entry.Id, entry.Delta, entry.Reason,
                entry.ReferenceId, entry.CreatedByAdminId, entry.Note, entry.CreatedAt)), page, cancellationToken);
}
