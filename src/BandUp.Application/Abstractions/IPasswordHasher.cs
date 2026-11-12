namespace BandUp.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords. The algorithm and its cost are an implementation detail;
/// callers never see a salt or a work factor.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    /// A hash of a password that was never set. Login verifies an unknown email against this
    /// so both failure paths do the same amount of work (AUTH-07). It can never match: the
    /// implementation derives it from a value no user can submit.
    /// </summary>
    string DummyHash { get; }
}