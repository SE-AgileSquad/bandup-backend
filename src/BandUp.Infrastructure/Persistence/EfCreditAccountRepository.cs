using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfCreditAccountRepository(BandUpDbContext context) : ICreditAccountRepository
{
    public Task AddAsync(CreditAccount account, CancellationToken cancellationToken) =>
        context.CreditAccounts.AddAsync(account, cancellationToken).AsTask();

    public Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        context.CreditAccounts.FirstOrDefaultAsync(account => account.UserId == userId, cancellationToken);

    public async Task<CreditAccount?> FindByUserIdForUpdateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Credit adjustments require an open transaction.");
        }

        CreditAccount? fresh = await context.CreditAccounts.FromSqlInterpolated(
                $"SELECT * FROM billing.credit_accounts WHERE user_id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (fresh is null)
        {
            return null;
        }

        CreditAccount? tracked = context.CreditAccounts.Local.SingleOrDefault(account => account.Id == fresh.Id);
        if (tracked is null)
        {
            context.CreditAccounts.Attach(fresh);
            return fresh;
        }

        var entry = context.Entry(tracked);
        if (entry.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException("Lock and re-read the credit account before changing it.");
        }
        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        return tracked;
    }
}
