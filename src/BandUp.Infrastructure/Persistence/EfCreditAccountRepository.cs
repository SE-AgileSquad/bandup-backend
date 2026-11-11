using BandUp.Application.Abstractions;
using BandUp.Domain.Billing;
using BandUp.Infrastructure.Persistence;

namespace BandUp.Infrastructure.Persistence;

public sealed class EfCreditAccountRepository(BandUpDbContext dbContext) : ICreditAccountRepository
{
    public Task AddAsync(CreditAccount account, CancellationToken cancellationToken)
    {
        return dbContext.CreditAccounts.AddAsync(account, cancellationToken).AsTask();
    }
}
