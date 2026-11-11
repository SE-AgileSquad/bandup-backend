using BandUp.Domain.Billing;

namespace BandUp.Application.Abstractions;

public interface ICreditAccountRepository
{
    Task AddAsync(CreditAccount account, CancellationToken cancellationToken);
}
