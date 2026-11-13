using BandUp.Application.Abstractions;
using BandUp.Application.Common;
using BandUp.Application.Features.Identity;
using BandUp.Application.Features.Identity.Admin.ListAdminUsers;
using BandUp.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace BandUp.Infrastructure.Persistence;

/// <summary>
/// Composes the generic repository rather than deriving from it, so the write operations stay
/// in one implementation and this class only adds the reads.
/// </summary>
public sealed class EfUserRepository(BandUpDbContext context) : IUserRepository
{
    private readonly EfRepository<User> _inner = new(context);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _inner.GetByIdAsync(id, cancellationToken);

    public Task AddAsync(User entity, CancellationToken cancellationToken) =>
        _inner.AddAsync(entity, cancellationToken);

    public void Remove(User entity) => _inner.Remove(entity);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public Task<PagedResult<AdminUserDto>> ListForAdminAsync(PageRequest page, CancellationToken cancellationToken) =>
        EfStoreOperations.ReadPageAsync(
            context.Users.AsNoTracking().OrderBy(user => user.Email).ThenBy(user => user.Id)
                .Select(user => new AdminUserDto(user.Id, user.DisplayName, user.Email, user.Role, user.IsActive)),
            page, cancellationToken);

    public Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(user => user.Id == userId && user.Role == Role.Admin && user.IsActive, cancellationToken);

    public Task LockRoleChangesAsync(CancellationToken cancellationToken)
    {
        const int identityLockNamespace = 42411;
        const int roleChangesLock = 1;
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({identityLockNamespace}, {roleChangesLock})", cancellationToken);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        context.Users.CountAsync(user => user.Role == Role.Admin && user.IsActive, cancellationToken);
}
