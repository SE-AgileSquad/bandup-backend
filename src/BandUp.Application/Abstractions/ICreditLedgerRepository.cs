using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditLedgerRepository
{
    Task AddAsync(CreditLedgerEntry entry, CancellationToken cancellationToken);
}
