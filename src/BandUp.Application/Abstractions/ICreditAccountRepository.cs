using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditAccountRepository
{
    Task<CreditAccount?> LockAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(CreditAccount account, CancellationToken cancellationToken);

    Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
