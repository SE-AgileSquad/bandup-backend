using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// BCrypt with a work factor of 12 (ADR 0012). The dummy hash is computed once per process;
/// login verifies unknown emails against it so both failure paths cost the same (AUTH-07).
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    // A hash of a fixed value no user can submit as their password, because the command
    // validator requires a digit and this has none.
    public string DummyHash { get; } = BCrypt.Net.BCrypt.HashPassword("dummy-value", WorkFactor);

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
