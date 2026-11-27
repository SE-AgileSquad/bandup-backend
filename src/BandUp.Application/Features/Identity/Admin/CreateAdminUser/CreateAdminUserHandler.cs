using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity.Admin.CreateAdminUser;

public sealed class CreateAdminUserHandler(
    IUserRepository users,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    ICreditService creditService,
    TimeProvider clock) : ICommandHandler<CreateAdminUserCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreateAdminUserCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId || !currentUser.IsAuthenticated)
        {
            return Result<Guid>.Failure(Error.Unauthorized("An authenticated user is required."));
        }

        await users.LockRoleChangesAsync(cancellationToken);
        if (!await users.IsActiveSuperAdminAsync(actorId, cancellationToken))
        {
            return Result<Guid>.Failure(Error.Forbidden("An active super admin is required."));
        }

        string email = command.Email.Trim();
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return Result<Guid>.Failure(Error.EmailTaken());
        }

        User user = User.Create(email, command.DisplayName.Trim(), passwordHasher.Hash(command.Password), clock.GetUtcNow());
        user.ChangeRole(command.Role!.Value);
        await users.AddAsync(user, cancellationToken);

        // Reuse the existing account-opening flow; the command transaction commits both together.
        Result account = await creditService.OpenSignupAccountAsync(user.Id, cancellationToken);
        return account.IsSuccess ? Result<Guid>.Success(user.Id) : Result<Guid>.Failure(account.Error!);
    }
}
