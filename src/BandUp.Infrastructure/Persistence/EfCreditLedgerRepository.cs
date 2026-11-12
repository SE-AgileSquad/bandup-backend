using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfCreditLedgerRepository(BandUpDbContext context) : ICreditLedgerRepository
{
    public Task AddAsync(CreditLedgerEntry entry, CancellationToken cancellationToken) =>
        context.CreditLedger.AddAsync(entry, cancellationToken).AsTask();
}
