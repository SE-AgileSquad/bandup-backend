namespace BandUp.Domain.Identity;

/// <summary>
/// Stored as text with a CHECK constraint listing these names. The order matters: it is the
/// order the constraint is generated with.
/// </summary>
public enum Role
{
    User,
    Admin,
    SuperAdmin,
}
