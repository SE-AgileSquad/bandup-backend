using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditAccountRepository
{
    Task AddAsync(CreditAccount account, CancellationToken cancellationToken);

    Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<CreditAccount?> FindByUserIdForUpdateAsync(Guid userId, CancellationToken cancellationToken);
}
