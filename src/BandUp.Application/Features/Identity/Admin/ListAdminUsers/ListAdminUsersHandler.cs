using BandUp.Application.Abstractions;
using BandUp.Application.Common;

namespace BandUp.Application.Features.Identity.Admin.ListAdminUsers;

public sealed class ListAdminUsersHandler(IUserRepository users, ICurrentUser currentUser)
    : IQueryHandler<ListAdminUsersQuery, PagedResult<AdminUserDto>>
{
    public async Task<Result<PagedResult<AdminUserDto>>> HandleAsync(
        ListAdminUsersQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || !currentUser.IsAuthenticated)
        {
            return Result<PagedResult<AdminUserDto>>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        if (!await users.IsActiveSuperAdminAsync(actorId, cancellationToken))
        {
            return Result<PagedResult<AdminUserDto>>.Failure(Error.Forbidden("An active super admin is required."));
        }

        return Result<PagedResult<AdminUserDto>>.Success(await users.ListForAdminAsync(
            query.Page, query.Role, query.Search?.Trim(), query.IsActive, cancellationToken));
    }
}
