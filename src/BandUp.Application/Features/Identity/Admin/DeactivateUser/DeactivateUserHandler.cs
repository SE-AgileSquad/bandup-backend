using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.DeactivateUser;

public sealed class DeactivateUserHandler(
    IUserRepository users,
    ICurrentUser currentUser,
    IRefreshTokenStore refreshTokens,
    TimeProvider clock) : ICommandHandler<DeactivateUserCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || !currentUser.IsAuthenticated)
        {
            return Result<bool>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        await users.LockRoleChangesAsync(cancellationToken);
        if (!await users.IsActiveSuperAdminAsync(actorId, cancellationToken))
        {
            return Result<bool>.Failure(Error.Forbidden("An active super admin is required."));
        }

        User? target = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (target is null)
        {
            return Result<bool>.Failure(Error.NotFound("User not found."));
        }

        if (!target.IsActive)
        {
            return Result<bool>.Success(false);
        }

        if (target.Role == Role.SuperAdmin && await users.CountActiveSuperAdminsAsync(cancellationToken) <= 1)
        {
            return Result<bool>.Failure(Error.Conflict("The last active super admin cannot be deactivated."));
        }

        if (target.Id == actorId)
        {
            return Result<bool>.Failure(Error.Conflict("Super admins cannot deactivate themselves."));
        }

        target.Deactivate();
        DateTimeOffset now = clock.GetUtcNow();
        foreach (RefreshToken token in await refreshTokens.ListUsableForUserAsync(target.Id, cancellationToken))
        {
            token.Revoke(now);
        }

        return Result<bool>.Success(true);
    }
}
