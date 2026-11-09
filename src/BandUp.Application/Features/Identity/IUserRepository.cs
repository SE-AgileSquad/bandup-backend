using BandUp.Application.Abstractions;
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

    /// <summary>
    /// Guards AUTH-14: the last admin cannot be demoted.
    /// </summary>
    Task<int> CountAdminsAsync(CancellationToken cancellationToken);
}