using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfCreditAccountRepository(BandUpDbContext context) : ICreditAccountRepository
{
    private Guid? _transactionId;
    private readonly Dictionary<Guid, CreditAccount> _locked = [];

    public async Task<CreditAccount?> LockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var transaction = context.Database.CurrentTransaction;
        if (transaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
        {
            throw new InvalidOperationException("Credit operations require an open Read Committed transaction.");
        }

        if (_transactionId != transaction.TransactionId)
        {
            _locked.Clear();
            _transactionId = transaction.TransactionId;
        }

        if (_locked.TryGetValue(userId, out var locked))
        {
            return locked;
        }

        var fresh = await context.CreditAccounts
            .FromSqlInterpolated($"SELECT * FROM billing.credit_accounts WHERE user_id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (fresh is null)
        {
            return null;
        }

        var tracked = context.CreditAccounts.Local.SingleOrDefault(account => account.Id == fresh.Id);
        if (tracked is null)
        {
            context.Attach(fresh);
            tracked = fresh;
        }
        else
        {
            var entry = context.Entry(tracked);
            if (entry.State != EntityState.Unchanged)
            {
                throw new InvalidOperationException("Lock the credit account before modifying it.");
            }
            entry.CurrentValues.SetValues(fresh);
            entry.OriginalValues.SetValues(fresh);
        }

        _locked.Add(userId, tracked);
        return tracked;
    }

    public Task AddAsync(CreditAccount account, CancellationToken cancellationToken) =>
        context.CreditAccounts.AddAsync(account, cancellationToken).AsTask();

    public Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        context.CreditAccounts.FirstOrDefaultAsync(account => account.UserId == userId, cancellationToken);
}
