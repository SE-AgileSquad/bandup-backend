using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;

namespace BandUp.Application.Features.Billing.Admin;

public sealed record GetAdminUserCreditsQuery(Guid UserId) : IQuery<AdminCreditBalance>;
public sealed record ListAdminCreditHistoryQuery(Guid UserId, PageRequest Page) : IQuery<PagedResult<CreditHistoryItem>>;

public sealed class GetAdminUserCreditsHandler(IUserRepository users, ICreditService credits)
    : IQueryHandler<GetAdminUserCreditsQuery, AdminCreditBalance>
{
    public async Task<Result<AdminCreditBalance>> HandleAsync(GetAdminUserCreditsQuery query, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(query.UserId, cancellationToken);
        return user is null
            ? Result<AdminCreditBalance>.Failure(Error.NotFound("User not found."))
            : Result<AdminCreditBalance>.Success(new(user.Id, user.DisplayName, user.Email,
                await credits.GetBalanceAsync(user.Id, cancellationToken)));
    }
}

public sealed class ListAdminCreditHistoryHandler(IUserRepository users, ICreditLedgerRepository ledger)
    : IQueryHandler<ListAdminCreditHistoryQuery, PagedResult<CreditHistoryItem>>
{
    public async Task<Result<PagedResult<CreditHistoryItem>>> HandleAsync(
        ListAdminCreditHistoryQuery query, CancellationToken cancellationToken) =>
        await users.GetByIdAsync(query.UserId, cancellationToken) is null
            ? Result<PagedResult<CreditHistoryItem>>.Failure(Error.NotFound("User not found."))
            : Result<PagedResult<CreditHistoryItem>>.Success(await ledger.ListForUserAsync(query.UserId, query.Page, cancellationToken));
}
