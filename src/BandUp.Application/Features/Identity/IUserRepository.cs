using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity.Admin.ListAdminUsers;
using BandUp.Domain.Identity;

namespace BandUp.Application.Features.Identity;

/// <summary>
/// Identity's aggregate store. Extends the generic repository with the lookups the use cases
/// actually need; the implementation lives in Infrastructure.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Case-insensitive by construction: the column is citext and the unique index too.
    /// </summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    Task<PagedResult<AdminUserDto>> ListForAdminAsync(
        PageRequest page, Role? role, string? search, bool? isActive, CancellationToken cancellationToken);

    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsActiveSuperAdminAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Serializes role changes and deactivation for the lifetime of the transaction.</summary>
    Task LockRoleChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The last active super admin cannot be demoted or deactivated.
    /// </summary>
    Task<int> CountActiveSuperAdminsAsync(CancellationToken cancellationToken);
}
