using BandUp.Application.Abstractions;

namespace BandUp.Infrastructure.Security;

/// <summary>
/// BCrypt with a work factor of 12 (ADR 0012). Includes a precomputed dummy hash so a login
/// handler can spend the same time on an unknown email as on a wrong password (AUTH-07).
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <summary>
    /// Any BCrypt hash, computed once at startup. Verifying an unknown email against this
    /// keeps the response time comparable to a real verification.
    /// </summary>
    public static string DummyHash { get; } =
        BCrypt.Net.BCrypt.HashPassword("dummy-value", WorkFactor);

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}