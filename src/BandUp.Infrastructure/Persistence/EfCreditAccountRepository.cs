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
}
