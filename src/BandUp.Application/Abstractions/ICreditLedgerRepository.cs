using BandUp.Domain.Billing;
using BandUp.Application.Common;
using BandUp.Application.Features.Billing.Admin;

namespace BandUp.Application.Abstractions;

public interface ICreditLedgerRepository
{
    Task AddAsync(CreditLedgerEntry entry, CancellationToken cancellationToken);

    Task<CreditLedgerEntry?> FindAdminAdjustmentAsync(Guid requestId, CancellationToken cancellationToken);

    Task<PagedResult<CreditHistoryItem>> ListForUserAsync(Guid userId, PageRequest page, CancellationToken cancellationToken);
}
