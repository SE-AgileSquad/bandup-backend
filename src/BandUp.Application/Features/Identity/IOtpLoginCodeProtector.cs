namespace BandUp.Application.Features.Identity;

public interface IOtpLoginCodeProtector
{
    string GenerateCode();

    /// <summary>Produces a purpose-, user- and challenge-bound MAC; plaintext codes must never be persisted.</summary>
    string Protect(Guid challengeId, Guid userId, string code);

    bool Verify(Guid challengeId, Guid userId, string? code, string? codeMac);
}
