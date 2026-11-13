using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed class ListAdminUsersHandler(IUserRepository users)
    : IQueryHandler<ListAdminUsersQuery, PagedResult<AdminUserDto>>
{
    public async Task<Result<PagedResult<AdminUserDto>>> HandleAsync(
        ListAdminUsersQuery query, CancellationToken cancellationToken) =>
        Result<PagedResult<AdminUserDto>>.Success(await users.ListForAdminAsync(query.Page, cancellationToken));
}
