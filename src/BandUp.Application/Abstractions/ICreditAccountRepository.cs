using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditAccountRepository
{
    /// <summary>Read the authoritative balance under a Read Committed row lock. Repeated calls
    /// within the transaction return the same aggregate so pending debits and grants survive.</summary>
    Task<CreditAccount?> LockAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(CreditAccount account, CancellationToken cancellationToken);

    Task<CreditAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<CreditAccount?> FindByUserIdForUpdateAsync(Guid userId, CancellationToken cancellationToken);
}
