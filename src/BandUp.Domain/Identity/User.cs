using BandUp.Domain.Common;

namespace BandUp.Domain.Identity;

public sealed class User : Entity
{
    private User()
    {
    }

    private User(Guid id, string email, string displayName, string passwordHash, DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public string Email { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public Role Role { get; private set; } = Role.User;

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public static User Create(string email, string displayName, string passwordHash, DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), email, displayName, passwordHash, createdAt);

    public void RecordLogin(DateTimeOffset at)
    {
        if (!IsActive)
        {
            throw new DomainException($"Login cannot be recorded for deactivated user {Id}.");
        }

        LastLoginAt = at;
    }

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>
    /// The caller and last-super-admin guards belong in Application because they need other users.
    /// </summary>
    public void ChangeRole(Role role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainException("The user role is invalid.");
        }

        Role = role;
    }

    public void Deactivate() => IsActive = false;
}
