namespace BandUp.Application.Abstractions;

/// <summary>
/// The authenticated caller of the current operation. Nulls mean anonymous: the Worker has no
/// request and therefore no user.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Role { get; }

    bool IsAuthenticated { get; }
}