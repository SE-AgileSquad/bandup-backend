using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditLedgerRepository
{
    Task<CreditLedgerEntry?> FindAsync(string reason, Guid referenceId, CancellationToken cancellationToken);

    Task AddAsync(CreditLedgerEntry entry, CancellationToken cancellationToken);
}
