using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;

namespace BandUp.Application.Features.Dashboard.Admin.GetAdminDashboard;

public sealed class GetAdminDashboardHandler(
    IAdminDashboardQueries dashboard,
    IUserRepository users,
    ICurrentUser currentUser) : IQueryHandler<GetAdminDashboardQuery, AdminDashboardDto>
{
    public async Task<Result<AdminDashboardDto>> HandleAsync(
        GetAdminDashboardQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || !currentUser.IsAuthenticated)
        {
            return Result<AdminDashboardDto>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        if (!await users.IsActiveAdminAsync(actorId, cancellationToken))
        {
            return Result<AdminDashboardDto>.Failure(Error.Forbidden("An active admin is required."));
        }

        return Result<AdminDashboardDto>.Success(await dashboard.GetAsync(cancellationToken));
    }
}
