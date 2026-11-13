using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.ChangeUserRole;

public sealed class ChangeUserRoleHandler(IUserRepository users, ICurrentUser currentUser)
    : ICommandHandler<ChangeUserRoleCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(ChangeUserRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result<bool>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        await users.LockRoleChangesAsync(cancellationToken);
        // The caller may have been demoted while this command waited for the lock.
        if (!await users.IsActiveAdminAsync(actorId, cancellationToken))
        {
            return Result<bool>.Failure(Error.Forbidden("An active admin is required."));
        }

        User? target = await users.GetByIdAsync(command.UserId, cancellationToken);
        if (target is null)
        {
            return Result<bool>.Failure(Error.NotFound("User not found."));
        }

        Role newRole = command.Role!.Value;
        if (target.Role == newRole)
        {
            return Result<bool>.Success(false);
        }

        if (target.Role == Role.Admin)
        {
            if (target.IsActive && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
            {
                return Result<bool>.Failure(Error.Conflict("The last active admin cannot be demoted."));
            }

            if (target.Id == actorId)
            {
                return Result<bool>.Failure(Error.Conflict("Admins cannot demote themselves."));
            }
        }

        target.ChangeRole(newRole);
        return Result<bool>.Success(true);
    }
}
