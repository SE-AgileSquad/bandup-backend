namespace BandUp.Application.Abstractions;

/// <summary>
/// Hashes and verifies passwords. The algorithm and its cost are an implementation detail;
/// callers never see a salt or a work factor.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}